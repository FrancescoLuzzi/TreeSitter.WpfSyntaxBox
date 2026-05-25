using System;
using System.Collections.Generic;
using TreeSitterNode = global::TreeSitter.Node;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Provides a fallback classifier when language-specific tree-sitter highlight queries are unavailable.
/// </summary>
internal static class TreeSitterClassifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch", "class", "const", "continue", "default",
        "delegate", "do", "else", "enum", "event", "extern", "false", "finally", "for", "foreach", "from", "get", "global",
        "if", "implements", "import", "in", "interface", "internal", "is", "let", "namespace", "new", "null", "operator", "out",
        "override", "package", "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sealed",
        "static", "struct", "switch", "this", "throw", "true", "try", "type", "typeof", "using", "var", "void", "while", "with", "yield",
        "and", "do", "elseif", "end", "function", "goto", "local", "nil", "not", "or", "repeat", "then", "until",
    };

    private static readonly HashSet<string> BuiltinTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort",
    };

    /// <summary>
    /// Walks only nodes that intersect a requested character range and returns coalesced highlight spans.
    /// </summary>
    public static IReadOnlyList<HighlightSpan> HighlightSlice(TreeSitterNode root, string source, int start, int end)
    {
        start = Math.Clamp(start, 0, source.Length);
        end = Math.Clamp(end, start, source.Length);

        var spans = new List<HighlightSpan>();
        Walk(root, start, end, spans);
        spans.Sort(CompareSpans);
        return Coalesce(spans);
    }

    /// <summary>
    /// Traverses the syntax tree while pruning subtrees outside the visible slice.
    /// </summary>
    private static void Walk(TreeSitterNode node, int start, int end, List<HighlightSpan> spans)
    {
        if (node.EndIndex <= start || node.StartIndex >= end)
        {
            return;
        }

        var kind = Classify(node);
        if (kind is not null)
        {
            AddSpan(spans, node, kind.Value);
            if (IsTerminalHighlight(kind.Value))
            {
                return;
            }
        }

        foreach (var child in node.Children)
        {
            Walk(child, start, end, spans);
        }
    }

    /// <summary>
    /// Infers a highlight kind from generic tree-sitter node names and leaf text.
    /// </summary>
    private static HighlightKind? Classify(TreeSitterNode node)
    {
        if (node.EndIndex <= node.StartIndex)
        {
            return null;
        }

        var type = node.Type;
        if (type.Contains("comment", StringComparison.OrdinalIgnoreCase))
        {
            return type.Contains("documentation", StringComparison.OrdinalIgnoreCase)
                ? HighlightKind.DocumentationComment
                : HighlightKind.Comment;
        }

        if (type.Contains("string", StringComparison.OrdinalIgnoreCase) || type.Contains("char_literal", StringComparison.OrdinalIgnoreCase))
        {
            return HighlightKind.String;
        }

        if (type.Contains("number", StringComparison.OrdinalIgnoreCase))
        {
            return HighlightKind.Number;
        }

        if (type.EndsWith("_literal", StringComparison.Ordinal) && IsNumericLiteral(node.Text))
        {
            return HighlightKind.Number;
        }

        if (type is "predefined_type" or "primitive_type")
        {
            return HighlightKind.BuiltinType;
        }

        if (type is "type_identifier")
        {
            return HighlightKind.Type;
        }

        if (type is "modifier" or "keyword")
        {
            return HighlightKind.Keyword;
        }

        if (!node.IsNamed)
        {
            var text = node.Text;
            if (BuiltinTypes.Contains(text))
            {
                return HighlightKind.BuiltinType;
            }

            if (Keywords.Contains(text))
            {
                return HighlightKind.Keyword;
            }

            return text switch
            {
                "(" or ")" or "[" or "]" or "{" or "}" => HighlightKind.Bracket,
                "," or ";" or ":" or "." => HighlightKind.Delimiter,
                "+" or "-" or "*" or "/" or "%" or "=" or "==" or "!=" or "<" or ">" or "<=" or ">=" or "=>" or "&&" or "||" or "!" or "?" or "??" => HighlightKind.Operator,
                _ => null,
            };
        }

        if (node.Children.Count == 0)
        {
            var text = node.Text;
            if (BuiltinTypes.Contains(text))
            {
                return HighlightKind.BuiltinType;
            }

            if (Keywords.Contains(text))
            {
                return HighlightKind.Keyword;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds a span using the node's character offsets and the library's byte-offset convention.
    /// </summary>
    private static void AddSpan(List<HighlightSpan> spans, TreeSitterNode node, HighlightKind kind)
    {
        var start = node.StartIndex;
        var end = node.EndIndex;
        spans.Add(new HighlightSpan(start, end - start, start * 2, end * 2, kind));
    }

    /// <summary>
    /// Returns whether a classified node should prevent child spans from overriding it.
    /// </summary>
    private static bool IsTerminalHighlight(HighlightKind kind) => kind is
        HighlightKind.Comment or
        HighlightKind.DocumentationComment or
        HighlightKind.String or
        HighlightKind.Number or
        HighlightKind.BuiltinType or
        HighlightKind.Type or
        HighlightKind.Keyword;

    /// <summary>
    /// Returns whether a token text begins like a numeric literal.
    /// </summary>
    private static bool IsNumericLiteral(string value) => value.Length > 0 && char.IsDigit(value[0]);

    /// <summary>
    /// Orders spans by start and length before coalescing.
    /// </summary>
    private static int CompareSpans(HighlightSpan left, HighlightSpan right)
    {
        var startComparison = left.Start.CompareTo(right.Start);
        return startComparison != 0 ? startComparison : left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// Merges adjacent fallback spans of the same kind in place to reduce downstream formatting work.
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
                if (previous.Kind == span.Kind && previous.End == span.Start && previous.EndByte == span.StartByte)
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
}
