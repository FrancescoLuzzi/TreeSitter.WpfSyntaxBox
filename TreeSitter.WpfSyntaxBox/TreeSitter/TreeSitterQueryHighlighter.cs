using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TreeSitter;
using TreeSitterLanguage = global::TreeSitter.Language;
using TreeSitterNode = global::TreeSitter.Node;
using TreeSitterParser = global::TreeSitter.Parser;
using TreeSitterQuery = global::TreeSitter.Query;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Executes nvim/tree-sitter highlight queries, injection queries, and capture mapping for a parsed tree slice.
/// </summary>
internal sealed class TreeSitterQueryHighlighter : IDisposable
{
    private const int MaxInjectionDepth = 4;
    private const string QueryResourceRoot = "TreeSitter.Queries";
    private static readonly Regex CommentTokenRegex = new(@"\b(TODO|WIP|NOTE|XXX|INFO|DOCS|PERF|TEST|HACK|WARNING|WARN|FIX|FIXME|BUG|ERROR)\b", RegexOptions.Compiled);
    private static readonly Regex CommentIssueRegex = new(@"#[0-9]+\b", RegexOptions.Compiled);
    private static readonly Regex CommentUrlRegex = new(@"\b(?:https?|ftp)://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> LinkCaptureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "string.special.url",
        "markup.link.url",
        "markup.link",
    };

    private static readonly object HighlightKindCacheGate = new();
    private static readonly Dictionary<string, HighlightKind?> HighlightKindCache = new(StringComparer.Ordinal);
    private static readonly object QuerySourceCacheGate = new();
    private static readonly Dictionary<string, string?> QuerySourceCache = new(StringComparer.Ordinal);
    private static readonly Lazy<string[]> QueryResourceNames = new(() => typeof(TreeSitterQueryHighlighter).Assembly.GetManifestResourceNames());
    private readonly string languageId;
    private readonly TreeSitterQuery query;
    private readonly TreeSitterQuery? injectionQuery;

    /// <summary>
    /// Stores compiled tree-sitter queries for a normalized language id.
    /// </summary>
    private TreeSitterQueryHighlighter(string languageId, TreeSitterQuery query, TreeSitterQuery? injectionQuery)
    {
        this.languageId = languageId;
        this.query = query;
        this.injectionQuery = injectionQuery;
    }

    /// <summary>
    /// Creates a query highlighter when highlight queries exist and compile for the language.
    /// </summary>
    public static TreeSitterQueryHighlighter? Create(TreeSitterLanguage language, string languageId)
    {
        var querySource = LoadQuery(languageId, "highlights");
        if (string.IsNullOrWhiteSpace(querySource))
        {
            return null;
        }

        try
        {
            var query = language.CreateQuery(querySource);
            var injectionQuery = CreateOptionalQuery(language, languageId, "injections");
            return new TreeSitterQueryHighlighter(languageId, query, injectionQuery);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Highlights a source slice from a parsed root node.
    /// </summary>
    public IReadOnlyList<HighlightSpan> HighlightSlice(TreeSitterNode root, string source, int start, int end)
    {
        var injectedLanguages = new Dictionary<string, InjectedLanguageContext>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return HighlightSlice(root, source, start, end, depth: 0, injectedLanguages);
        }
        finally
        {
            foreach (var context in injectedLanguages.Values)
            {
                context.Dispose();
            }
        }
    }

    /// <summary>
    /// Executes captures for a slice and recursively adds injected-language spans up to a bounded depth.
    /// </summary>
    private IReadOnlyList<HighlightSpan> HighlightSlice(
        TreeSitterNode root,
        string source,
        int start,
        int end,
        int depth,
        Dictionary<string, InjectedLanguageContext> injectedLanguages)
    {
        var spans = new List<HighlightSpan>();
        using var cursor = query.Execute(root, new QueryOptions
        {
            StartIndex = start,
            EndIndex = end,
        });

        foreach (var match in cursor.Matches)
        {
            if (!MatchesUserPredicates(match))
            {
                continue;
            }

            foreach (var capture in match.Captures)
            {
                var kind = GetHighlightKind(capture.Name);
                if (kind is null || capture.Node.EndIndex <= start || capture.Node.StartIndex >= end)
                {
                    continue;
                }

                AddSpan(spans, capture.Node, kind.Value, GetLink(capture));
            }
        }

        if (depth < MaxInjectionDepth && injectionQuery is not null)
        {
            AddInjectionSpans(spans, root, source, start, end, depth, injectedLanguages);
        }

        spans.Sort(CompareSpans);
        return Coalesce(spans);
    }

    /// <summary>
    /// Releases compiled query handles.
    /// </summary>
    public void Dispose()
    {
        query.Dispose();
        injectionQuery?.Dispose();
    }

    /// <summary>
    /// Loads and compiles an optional query, ignoring missing or unsupported query files.
    /// </summary>
    private static TreeSitterQuery? CreateOptionalQuery(TreeSitterLanguage language, string languageId, string queryName)
    {
        var querySource = LoadQuery(languageId, queryName);
        if (string.IsNullOrWhiteSpace(querySource))
        {
            return null;
        }

        try
        {
            return language.CreateQuery(querySource);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adds spans produced by injection queries, translating injected offsets back to the host source.
    /// </summary>
    private void AddInjectionSpans(
        List<HighlightSpan> spans,
        TreeSitterNode root,
        string source,
        int start,
        int end,
        int depth,
        Dictionary<string, InjectedLanguageContext> injectedLanguages)
    {
        using var cursor = injectionQuery!.Execute(root, new QueryOptions
        {
            StartIndex = start,
            EndIndex = end,
        });

        foreach (var match in cursor.Matches)
        {
            if (!MatchesUserPredicates(match))
            {
                continue;
            }

            var injectedLanguage = GetInjectionLanguage(match);
            if (injectedLanguage is null)
            {
                continue;
            }

            foreach (var capture in match.Captures)
            {
                if (!string.Equals(capture.Name, "injection.content", StringComparison.Ordinal))
                {
                    continue;
                }

                var (contentStart, contentEnd) = GetInjectionContentRange(source, capture.Node, match.UserPredicates);
                if (contentEnd <= contentStart || contentEnd <= start || contentStart >= end)
                {
                    continue;
                }

                var injectedSource = source.Substring(contentStart, contentEnd - contentStart);
                foreach (var span in HighlightInjectedSource(injectedLanguage, injectedSource, depth + 1, injectedLanguages))
                {
                    var absoluteStart = contentStart + span.Start;
                    spans.Add(span with
                    {
                        Start = absoluteStart,
                        StartByte = absoluteStart * 2,
                        EndByte = (absoluteStart + span.Length) * 2,
                    });
                }
            }
        }
    }

    /// <summary>
    /// Resolves the language requested by a tree-sitter injection match.
    /// </summary>
    private string? GetInjectionLanguage(QueryMatch match)
    {
        if (match.SetProperties?.ContainsKey("injection.self") == true)
        {
            return languageId;
        }

        if (match.SetProperties?.TryGetValue("injection.language", out var setLanguage) == true && !string.IsNullOrWhiteSpace(setLanguage))
        {
            return setLanguage;
        }

        foreach (var capture in match.Captures)
        {
            if (string.Equals(capture.Name, "injection.language", StringComparison.Ordinal))
            {
                return NormalizeCapturedLanguage(capture.Node.Text);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses and highlights an injected source fragment, or applies comment-token highlighting for comment injections.
    /// </summary>
    private static IEnumerable<HighlightSpan> HighlightInjectedSource(
        string languageName,
        string source,
        int depth,
        Dictionary<string, InjectedLanguageContext> injectedLanguages)
    {
        if (string.IsNullOrEmpty(source))
        {
            return [];
        }

        var normalizedLanguage = NormalizeCapturedLanguage(languageName);
        if (string.Equals(normalizedLanguage, "comment", StringComparison.OrdinalIgnoreCase))
        {
            return HighlightCommentSource(source);
        }

        if (!TreeSitterLanguageFactory.TryGetSupportedLanguageId(normalizedLanguage, out var languageId))
        {
            return [];
        }

        try
        {
            if (!injectedLanguages.TryGetValue(languageId, out var context))
            {
                context = new InjectedLanguageContext(languageId);
                injectedLanguages[languageId] = context;
            }

            return context.Highlight(source, depth, injectedLanguages);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// Applies query offset predicates to calculate the actual content range inside an injection capture.
    /// </summary>
    private static (int Start, int End) GetInjectionContentRange(string source, TreeSitterNode node, IReadOnlyList<UserPredicate> predicates)
    {
        var start = node.StartIndex;
        var end = node.EndIndex;
        foreach (var predicate in predicates)
        {
            var steps = predicate.Steps;
            if (steps.Count != 6
                || steps[0] is not StringPredicateStep { Value: "offset!" }
                || steps[1] is not CapturePredicateStep { Name: "injection.content" }
                || steps[2] is not StringPredicateStep startRowStep
                || steps[3] is not StringPredicateStep startColumnStep
                || steps[4] is not StringPredicateStep endRowStep
                || steps[5] is not StringPredicateStep endColumnStep
                || !int.TryParse(startRowStep.Value, out var startRows)
                || !int.TryParse(startColumnStep.Value, out var startColumns)
                || !int.TryParse(endRowStep.Value, out var endRows)
                || !int.TryParse(endColumnStep.Value, out var endColumns))
            {
                continue;
            }

            var localStart = GetOffsetStart(source, start, end, startRows, startColumns);
            var localEnd = GetOffsetEnd(source, start, end, endRows, endColumns);
            start += localStart;
            end = node.StartIndex + localEnd;
        }

        start = Math.Clamp(start, node.StartIndex, node.EndIndex);
        end = Math.Clamp(end, start, node.EndIndex);
        return (start, end);
    }

    /// <summary>
    /// Converts positive row and column offsets from an injection node start into a local character offset.
    /// </summary>
    private static int GetOffsetStart(string source, int start, int end, int rows, int columns)
    {
        var offset = 0;
        for (var row = 0; row < rows && start + offset < end; row++)
        {
            var newline = source.IndexOf('\n', start + offset, end - start - offset);
            offset = newline < 0 ? end - start : newline - start + 1;
        }

        return Math.Clamp(offset + columns, 0, end - start);
    }

    /// <summary>
    /// Converts row and column offsets from an injection node end or start into a local character offset.
    /// </summary>
    private static int GetOffsetEnd(string source, int start, int end, int rows, int columns)
    {
        var offset = end - start;
        if (rows < 0)
        {
            for (var row = 0; row < -rows && offset > 0; row++)
            {
                var searchStart = start + Math.Max(0, offset - 2);
                var newline = source.LastIndexOf('\n', searchStart, searchStart - start + 1);
                offset = newline < start ? 0 : newline - start + 1;
            }
        }
        else if (rows > 0)
        {
            offset = GetOffsetStart(source, start, end, rows, 0);
        }

        return Math.Clamp(offset + columns, 0, end - start);
    }

    /// <summary>
    /// Evaluates all user predicates attached to a query match before accepting its captures.
    /// </summary>
    private static bool MatchesUserPredicates(QueryMatch match)
    {
        foreach (var predicate in match.UserPredicates)
        {
            if (!MatchesUserPredicate(predicate, match.Captures))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Dispatches supported nvim query predicates and treats unknown predicates as non-filtering.
    /// </summary>
    private static bool MatchesUserPredicate(UserPredicate predicate, IReadOnlyList<QueryCapture> captures)
    {
        var steps = predicate.Steps;
        if (steps.Count == 0 || steps[0] is not StringPredicateStep operatorStep)
        {
            return true;
        }

        return operatorStep.Value switch
        {
            "lua-match?" => MatchesLuaPatternPredicate(steps, captures),
            "vim-match?" => MatchesLuaPatternPredicate(steps, captures),
            "has-ancestor?" => MatchesAncestorPredicate(steps, captures, expected: true),
            "not-has-ancestor?" => MatchesAncestorPredicate(steps, captures, expected: false),
            "has-parent?" => MatchesParentPredicate(steps, captures, expected: true),
            "not-has-parent?" => MatchesParentPredicate(steps, captures, expected: false),
            _ => true,
        };
    }

    /// <summary>
    /// Evaluates lua/vim pattern predicates by translating their pattern syntax to a .NET regular expression.
    /// </summary>
    private static bool MatchesLuaPatternPredicate(IReadOnlyList<PredicateStep> steps, IReadOnlyList<QueryCapture> captures)
    {
        if (steps.Count != 3 || steps[1] is not CapturePredicateStep captureStep || steps[2] is not StringPredicateStep patternStep)
        {
            return true;
        }

        var nodes = FindCaptureNodesByName(captures, captureStep.Name);
        if (nodes.Count == 0)
        {
            return false;
        }

        try
        {
            var regex = new Regex(ConvertLuaPattern(patternStep.Value));
            return nodes.TrueForAll(node => regex.IsMatch(node.Text));
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// Evaluates ancestor predicates against each captured node.
    /// </summary>
    private static bool MatchesAncestorPredicate(IReadOnlyList<PredicateStep> steps, IReadOnlyList<QueryCapture> captures, bool expected)
    {
        if (steps.Count < 3 || steps[1] is not CapturePredicateStep captureStep)
        {
            return true;
        }

        var nodeTypes = GetStringSteps(steps, start: 2);
        if (nodeTypes.Count == 0)
        {
            return true;
        }

        var nodes = FindCaptureNodesByName(captures, captureStep.Name);
        return nodes.Count > 0 && nodes.TrueForAll(node => HasAncestor(node, nodeTypes) == expected);
    }

    /// <summary>
    /// Evaluates direct-parent predicates against each captured node.
    /// </summary>
    private static bool MatchesParentPredicate(IReadOnlyList<PredicateStep> steps, IReadOnlyList<QueryCapture> captures, bool expected)
    {
        if (steps.Count < 3 || steps[1] is not CapturePredicateStep captureStep)
        {
            return true;
        }

        var nodeTypes = GetStringSteps(steps, start: 2);
        if (nodeTypes.Count == 0)
        {
            return true;
        }

        var nodes = FindCaptureNodesByName(captures, captureStep.Name);
        return nodes.Count > 0 && nodes.TrueForAll(node => (node.Parent is not null && nodeTypes.Contains(node.Parent.Type)) == expected);
    }

    /// <summary>
    /// Extracts string predicate arguments into an ordinal lookup set.
    /// </summary>
    private static HashSet<string> GetStringSteps(IReadOnlyList<PredicateStep> steps, int start)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < steps.Count; i++)
        {
            if (steps[i] is StringPredicateStep stringStep)
            {
                values.Add(stringStep.Value);
            }
        }

        return values;
    }

    /// <summary>
    /// Returns whether a node has any ancestor whose tree-sitter type is in the supplied set.
    /// </summary>
    private static bool HasAncestor(TreeSitterNode node, HashSet<string> nodeTypes)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (nodeTypes.Contains(parent.Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds all captured nodes with a requested capture name.
    /// </summary>
    private static List<TreeSitterNode> FindCaptureNodesByName(IReadOnlyList<QueryCapture> captures, string name)
    {
        var nodes = new List<TreeSitterNode>();
        foreach (var capture in captures)
        {
            if (capture.Name == name)
            {
                nodes.Add(capture.Node);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Maps nvim/tree-sitter capture names into the renderer's finite highlight taxonomy.
    /// </summary>
    internal static HighlightKind? GetHighlightKind(string captureName)
    {
        lock (HighlightKindCacheGate)
        {
            if (HighlightKindCache.TryGetValue(captureName, out var cached))
            {
                return cached;
            }
        }

        var kind = MapHighlightKind(captureName);
        lock (HighlightKindCacheGate)
        {
            HighlightKindCache[captureName] = kind;
        }

        return kind;
    }

    /// <summary>
    /// Performs uncached capture-name mapping for <see cref="GetHighlightKind"/>.
    /// </summary>
    private static HighlightKind? MapHighlightKind(string captureName)
    {
        var name = captureName.ToLowerInvariant();
        if (name.Length == 0
            || name[0] == '_'
            || name is "none" or "spell" or "nospell" or "conceal"
            || name.StartsWith("injection.", StringComparison.Ordinal)
            || name.StartsWith("local.", StringComparison.Ordinal)
            || name.StartsWith("indent.", StringComparison.Ordinal))
        {
            return null;
        }

        return name switch
        {
            "comment.todo" => HighlightKind.CommentTodo,
            "comment.note" => HighlightKind.CommentNote,
            "comment.warning" => HighlightKind.CommentWarning,
            "comment.error" => HighlightKind.CommentError,
            "comment.documentation" => HighlightKind.DocumentationComment,
            _ when name.StartsWith("comment", StringComparison.Ordinal) => HighlightKind.Comment,
            _ when name.StartsWith("keyword", StringComparison.Ordinal) => HighlightKind.Keyword,
            "string.escape" => HighlightKind.StringEscape,
            "string.regexp" => HighlightKind.StringRegex,
            _ when name.StartsWith("string.special", StringComparison.Ordinal) => HighlightKind.StringSpecial,
            _ when name.StartsWith("string", StringComparison.Ordinal) => HighlightKind.String,
            "character.special" => HighlightKind.StringSpecial,
            _ when name.StartsWith("character", StringComparison.Ordinal) => HighlightKind.String,
            "boolean" => HighlightKind.Boolean,
            _ when name.StartsWith("number", StringComparison.Ordinal) => HighlightKind.Number,
            _ when name.StartsWith("float", StringComparison.Ordinal) => HighlightKind.Number,
            _ when name.StartsWith("boolean", StringComparison.Ordinal) => HighlightKind.Boolean,
            "constant.builtin" => HighlightKind.Constant,
            _ when name.StartsWith("constant", StringComparison.Ordinal) => HighlightKind.Constant,
            "type.builtin" => HighlightKind.BuiltinType,
            "type.qualifier" => HighlightKind.Keyword,
            "type.definition" or "typedef" or "interface" => HighlightKind.Type,
            _ when name.StartsWith("type", StringComparison.Ordinal) => HighlightKind.Type,
            _ when name.StartsWith("constructor", StringComparison.Ordinal) => HighlightKind.Type,
            "function.builtin" => HighlightKind.FunctionBuiltin,
            "function.macro" => HighlightKind.FunctionMacro,
            _ when name.StartsWith("function.method", StringComparison.Ordinal) => HighlightKind.Method,
            _ when name.StartsWith("function", StringComparison.Ordinal) => HighlightKind.Function,
            "func" or "callback" => HighlightKind.Function,
            _ when name.StartsWith("method", StringComparison.Ordinal) => HighlightKind.Method,
            "variable.builtin" => HighlightKind.VariableBuiltin,
            _ when name.StartsWith("variable.parameter", StringComparison.Ordinal) => HighlightKind.Parameter,
            _ when name.StartsWith("variable.member", StringComparison.Ordinal) => HighlightKind.Property,
            "property" or "prop" => HighlightKind.Property,
            _ when name.StartsWith("variable", StringComparison.Ordinal) => HighlightKind.Variable,
            "param" or "arg" or "argument" => HighlightKind.Parameter,
            _ when name.StartsWith("module", StringComparison.Ordinal) => HighlightKind.Module,
            _ when name.StartsWith("namespace", StringComparison.Ordinal) => HighlightKind.Module,
            "import" => HighlightKind.Module,
            "attribute.builtin" => HighlightKind.Attribute,
            _ when name.StartsWith("attribute", StringComparison.Ordinal) => HighlightKind.Attribute,
            "tag.attribute" => HighlightKind.TagAttribute,
            "tag.delimiter" => HighlightKind.TagDelimiter,
            "tag.builtin" => HighlightKind.Tag,
            _ when name.StartsWith("tag", StringComparison.Ordinal) => HighlightKind.Tag,
            "operator" => HighlightKind.Operator,
            "punctuation.bracket" => HighlightKind.Bracket,
            "punctuation.special" => HighlightKind.PunctuationSpecial,
            _ when name.StartsWith("punctuation", StringComparison.Ordinal) => HighlightKind.Delimiter,
            "label" => HighlightKind.Label,
            "error" => HighlightKind.Error,
            _ when name.StartsWith("markup.heading", StringComparison.Ordinal) => HighlightKind.MarkupHeading,
            "markup.italic" or "markup.strong" or "markup.underline" or "markup.strikethrough" => HighlightKind.MarkupEmphasis,
            _ when name.StartsWith("markup.link", StringComparison.Ordinal) => HighlightKind.MarkupLink,
            _ when name.StartsWith("markup.list", StringComparison.Ordinal) => HighlightKind.MarkupList,
            "markup.quote" => HighlightKind.MarkupQuote,
            _ when name.StartsWith("markup.raw", StringComparison.Ordinal) => HighlightKind.MarkupRaw,
            _ when name.StartsWith("markup", StringComparison.Ordinal) => HighlightKind.MarkupRaw,
            "charset" or "keyframes" or "media" or "supports" => HighlightKind.Keyword,
            "component" => HighlightKind.Type,
            _ => HighlightKind.Variable,
        };
    }

    /// <summary>
    /// Applies lightweight token, issue, and URL highlighting inside comment injections without reparsing them as a language.
    /// </summary>
    private static IReadOnlyList<HighlightSpan> HighlightCommentSource(string source)
    {
        var spans = new List<HighlightSpan>();
        foreach (Match match in CommentTokenRegex.Matches(source))
        {
            spans.Add(new HighlightSpan(match.Index, match.Length, match.Index * 2, (match.Index + match.Length) * 2, GetCommentTokenKind(match.Value)));
        }

        foreach (Match match in CommentIssueRegex.Matches(source))
        {
            spans.Add(new HighlightSpan(match.Index, match.Length, match.Index * 2, (match.Index + match.Length) * 2, HighlightKind.Number));
        }

        foreach (Match match in CommentUrlRegex.Matches(source))
        {
            spans.Add(new HighlightSpan(match.Index, match.Length, match.Index * 2, (match.Index + match.Length) * 2, HighlightKind.StringSpecial, match.Value));
        }

        spans.Sort(CompareSpans);
        return Coalesce(spans);
    }

    /// <summary>
    /// Maps well-known comment markers to more specific comment highlight kinds.
    /// </summary>
    private static HighlightKind GetCommentTokenKind(string value) => value switch
    {
        "TODO" or "WIP" => HighlightKind.CommentTodo,
        "NOTE" or "XXX" or "INFO" or "DOCS" or "PERF" or "TEST" => HighlightKind.CommentNote,
        "HACK" or "WARNING" or "WARN" or "FIX" => HighlightKind.CommentWarning,
        "FIXME" or "BUG" or "ERROR" => HighlightKind.CommentError,
        _ => HighlightKind.CommentTodo,
    };

    /// <summary>
    /// Extracts an openable URI from URL-like captures so rendered text can become a hyperlink.
    /// </summary>
    private static string? GetLink(QueryCapture capture)
    {
        if (!IsLinkCapture(capture.Name))
        {
            return null;
        }

        var text = capture.Node.Text;
        var link = text.Trim().Trim('"', '\'', '`', '<', '>', '(', ')');
        return Uri.TryCreate(link, UriKind.Absolute, out var uri) && IsOpenableLink(uri)
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>
    /// Returns whether a capture may contain a URI that should be exposed as a hyperlink.
    /// </summary>
    private static bool IsLinkCapture(string captureName) => LinkCaptureNames.Contains(captureName);

    /// <summary>
    /// Allows only URI schemes that should be launchable from highlighted source text.
    /// </summary>
    internal static bool IsOpenableLink(Uri uri) => uri.Scheme is "http" or "https" or "mailto" or "ftp";

    /// <summary>
    /// Converts a tree-sitter node range into a highlight span using the source's character-to-byte convention.
    /// </summary>
    private static void AddSpan(List<HighlightSpan> spans, TreeSitterNode node, HighlightKind kind, string? link = null)
    {
        var start = node.StartIndex;
        var end = node.EndIndex;
        spans.Add(new HighlightSpan(start, end - start, start * 2, end * 2, kind, link));
    }

    /// <summary>
    /// Loads a query file and any inherited query files for a language.
    /// </summary>
    private static string? LoadQuery(string languageId, string queryName)
    {
        languageId = NormalizeQueryLanguageId(languageId);
        var cacheKey = $"{languageId}\0{queryName}";
        lock (QuerySourceCacheGate)
        {
            if (QuerySourceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = LoadQueryUncached(languageId, queryName, visited);
        lock (QuerySourceCacheGate)
        {
            QuerySourceCache[cacheKey] = source;
        }

        return source;
    }

    /// <summary>
    /// Recursively loads inherited queries while preventing inheritance cycles.
    /// </summary>
    private static string? LoadQueryUncached(string languageId, string queryName, HashSet<string> visited)
    {
        languageId = NormalizeQueryLanguageId(languageId);
        if (!visited.Add(languageId))
        {
            return null;
        }

        var source = LoadQueryResource(languageId, queryName);
        if (source is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var inheritedLanguage in GetInheritedLanguages(source))
        {
            var inheritedSource = LoadQueryUncached(inheritedLanguage, queryName, visited);
            if (!string.IsNullOrWhiteSpace(inheritedSource))
            {
                builder.AppendLine(inheritedSource);
            }
        }

        builder.AppendLine(source);
        return builder.ToString();
    }

    /// <summary>
    /// Reads an embedded query resource for a normalized language id.
    /// </summary>
    private static string? LoadQueryResource(string languageId, string queryName)
    {
        var resourceLanguageId = languageId.Replace('-', '_');
        var resourceSuffix = $".{QueryResourceRoot}.{resourceLanguageId}.{queryName}.scm";
        var resourceName = QueryResourceNames.Value
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            return null;
        }

        var assembly = typeof(TreeSitterQueryHighlighter).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads nvim-style leading <c>; inherits:</c> directives from a query file.
    /// </summary>
    private static IEnumerable<string> GetInheritedLanguages(string source)
    {
        using var reader = new StringReader(source);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                yield break;
            }

            const string inheritsPrefix = "; inherits:";
            if (!trimmed.StartsWith(inheritsPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var inheritedLanguages = trimmed[inheritsPrefix.Length..]
                .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var inheritedLanguage in inheritedLanguages)
            {
                yield return inheritedLanguage;
            }
        }
    }

    /// <summary>
    /// Normalizes query language ids to match embedded resource folder names.
    /// </summary>
    private static string NormalizeQueryLanguageId(string languageId) => languageId.Trim().ToLowerInvariant() switch
    {
        "c_sharp" => "c-sharp",
        "embedded_template" => "embedded-template",
        "html_tags" => "html-tags",
        "markdown_inline" => "markdown-inline",
        "ocaml_interface" => "ocaml-interface",
        "php_only" => "php-only",
        "systemverilog" => "verilog",
        _ => languageId.Trim().ToLowerInvariant(),
    };

    /// <summary>
    /// Normalizes language names captured by injection queries into factory language ids.
    /// </summary>
    private static string NormalizeCapturedLanguage(string languageName)
    {
        var normalized = languageName.Trim().Trim('"', '\'', '`').TrimStart('@').ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "csharp" => "c-sharp",
            "c++" => "cpp",
            "javascriptreact" => "tsx",
            "typescriptreact" => "tsx",
            "styled" => "css",
            _ => normalized,
        };
    }

    /// <summary>
    /// Converts the subset of Lua pattern tokens used by nvim queries into .NET regex syntax.
    /// </summary>
    private static string ConvertLuaPattern(string pattern)
    {
        var builder = new StringBuilder(pattern.Length);
        var inCharacterClass = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == '[')
            {
                inCharacterClass = true;
                builder.Append(current);
                continue;
            }

            if (current == ']')
            {
                inCharacterClass = false;
                builder.Append(current);
                continue;
            }

            if (current != '%' || i + 1 >= pattern.Length)
            {
                builder.Append(current);
                continue;
            }

            var escaped = pattern[++i];
            builder.Append(escaped switch
            {
                'a' => inCharacterClass ? "A-Za-z" : "[A-Za-z]",
                'A' => inCharacterClass ? "^A-Za-z" : "[^A-Za-z]",
                'd' => "\\d",
                'D' => "\\D",
                'l' => inCharacterClass ? "a-z" : "[a-z]",
                'L' => inCharacterClass ? "^a-z" : "[^a-z]",
                'u' => inCharacterClass ? "A-Z" : "[A-Z]",
                'U' => inCharacterClass ? "^A-Z" : "[^A-Z]",
                's' => "\\s",
                'S' => "\\S",
                'w' => "\\w",
                'W' => "\\W",
                _ when IsRegexMetaCharacter(escaped) => "\\" + escaped,
                _ => escaped.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns whether a character has special meaning in .NET regular expressions.
    /// </summary>
    private static bool IsRegexMetaCharacter(char value) => value is '\\' or '^' or '$' or '.' or '|' or '?' or '*' or '+' or '(' or ')' or '[' or ']' or '{' or '}' or '-';

    /// <summary>
    /// Orders spans by start and then length so coalescing can process adjacent ranges linearly.
    /// </summary>
    private static int CompareSpans(HighlightSpan left, HighlightSpan right)
    {
        var startComparison = left.Start.CompareTo(right.Start);
        return startComparison != 0 ? startComparison : left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// Merges adjacent spans with the same highlight kind and link in place to reduce WPF formatting operations.
    /// </summary>
    private static IReadOnlyList<HighlightSpan> Coalesce(List<HighlightSpan> spans)
    {
        if (spans.Count == 0)
        {
            return spans;
        }

        var write = 0;
        var count = spans.Count;
        for (var read = 0; read < count; read++)
        {
            var span = spans[read];
            if (span.Length <= 0)
            {
                continue;
            }

            if (write > 0)
            {
                var previous = spans[write - 1];
                if (previous.Kind == span.Kind && previous.Link == span.Link && previous.End == span.Start && previous.EndByte == span.StartByte)
                {
                    spans[write - 1] = previous with
                    {
                        Length = previous.Length + span.Length,
                        EndByte = span.EndByte,
                    };
                    continue;
                }
            }

            spans[write++] = span;
        }

        if (write < spans.Count)
        {
            spans.RemoveRange(write, spans.Count - write);
        }

        return spans;
    }

    /// <summary>
    /// Reuses native parser and compiled query objects for repeated injected fragments of the same language during one highlight operation.
    /// </summary>
    private sealed class InjectedLanguageContext : IDisposable
    {
        private readonly TreeSitterLanguage language;
        private readonly TreeSitterParser parser;
        private readonly TreeSitterQueryHighlighter? highlighter;

        /// <summary>
        /// Creates reusable tree-sitter state for one injected language id.
        /// </summary>
        public InjectedLanguageContext(string languageId)
        {
            language = new TreeSitterLanguage(languageId);
            parser = new TreeSitterParser(language);
            highlighter = Create(language, languageId);
        }

        /// <summary>
        /// Parses and highlights one injected source fragment.
        /// </summary>
        public IReadOnlyList<HighlightSpan> Highlight(
            string source,
            int depth,
            Dictionary<string, InjectedLanguageContext> injectedLanguages)
        {
            if (highlighter is null)
            {
                return [];
            }

            using var tree = parser.Parse(source);
            return tree is null
                ? []
                : highlighter.HighlightSlice(tree.RootNode, source, 0, source.Length, depth, injectedLanguages);
        }

        /// <summary>
        /// Releases native parser, language, and query resources.
        /// </summary>
        public void Dispose()
        {
            highlighter?.Dispose();
            parser.Dispose();
            language.Dispose();
        }
    }
}
