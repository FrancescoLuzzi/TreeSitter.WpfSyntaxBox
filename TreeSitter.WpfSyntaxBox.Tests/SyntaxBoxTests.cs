using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TreeSitter.WpfSyntaxBox;

namespace TreeSitter.WpfSyntaxBox.Tests;

/// <summary>
/// Covers syntax-driver integration, tree-sitter highlighting, theming, template restoration, and line helpers.
/// </summary>
public sealed class SyntaxBoxTests
{
    /// <summary>
    /// Verifies simple rule configurations aggregate abilities and emit matching instructions.
    /// </summary>
    [Fact]
    public void SyntaxConfig_AggregatesRuleAbilitiesAndMatches()
    {
        var config = new SyntaxConfig
        {
            new KeywordRule { Foreground = Brushes.Blue, Keywords = "public,class" },
            new RegexRule { Foreground = Brushes.Green, Op = DriverOperation.Line, Pattern = "//.*" },
        };

        var matches = config.Match(DriverOperation.Line, "public class Demo // comment").ToList();

        Assert.True(config.Abilities.HasFlag(DriverOperation.Line));
        Assert.Contains(matches, match => match.FromChar == 0 && match.Length == "public".Length);
        Assert.Contains(matches, match => match.FromChar == "public class Demo ".Length && match.Foreground == Brushes.Green);
    }

    /// <summary>
    /// Verifies C# tree-sitter highlighting produces keyword and string instructions.
    /// </summary>
    [Fact]
    public void SyntaxLanguage_ProducesFormatInstructionsFromTreeSitter()
    {
        var language = new SyntaxLanguage { Language = "CSharp" };

        var instructions = language.Match(DriverOperation.FullText, "public class Demo { string Message = \"hello\"; }").ToList();

        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.Keyword);
        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.String);
    }

    /// <summary>
    /// Verifies bundled nvim tree-sitter queries classify method captures.
    /// </summary>
    [Fact]
    public void SyntaxLanguage_UsesNvimTreeSitterHighlightQueries()
    {
        var language = new SyntaxLanguage { Language = "CSharp" };

        var instructions = language.Match(DriverOperation.FullText, "public class Demo { void Run() { Run(); } }").ToList();

        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.Method);
    }

    /// <summary>
    /// Verifies injection queries add comment tokens, URLs, issue numbers, and links.
    /// </summary>
    [Fact]
    public void SyntaxLanguage_UsesInjectionQueriesForCommentTokens()
    {
        var language = new SyntaxLanguage { Language = "CSharp" };

        var instructions = language.Match(DriverOperation.FullText, "// TODO: check https://example.com/#123\npublic class Demo { }").ToList();

        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.CommentTodo);
        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.StringSpecial);
        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.Number);
        Assert.Contains(instructions, instruction => instruction.Link == "https://example.com/#123");
    }

    /// <summary>
    /// Verifies hyperlink creation accepts safe schemes and rejects script-like schemes.
    /// </summary>
    [Fact]
    public void SyntaxRenderer_CreatesHyperlinksForOpenableLinks()
    {
        Assert.True(SyntaxRenderer.TryCreateHyperlink("https://example.com/path", out var hyperlink));
        Assert.Equal("https://example.com/path", hyperlink!.NavigateUri.AbsoluteUri.TrimEnd('/'));

        Assert.False(SyntaxRenderer.TryCreateHyperlink("javascript:alert(1)", out _));
    }

    /// <summary>
    /// Verifies nvim capture families map into normalized highlight kinds.
    /// </summary>
    [Fact]
    public void TreeSitterQueryHighlighter_MapsNvimCaptureFamilies()
    {
        Assert.Equal(HighlightKind.CommentWarning, TreeSitterQueryHighlighter.GetHighlightKind("comment.warning"));
        Assert.Equal(HighlightKind.StringEscape, TreeSitterQueryHighlighter.GetHighlightKind("string.escape"));
        Assert.Equal(HighlightKind.StringRegex, TreeSitterQueryHighlighter.GetHighlightKind("string.regexp"));
        Assert.Equal(HighlightKind.Boolean, TreeSitterQueryHighlighter.GetHighlightKind("boolean"));
        Assert.Equal(HighlightKind.Parameter, TreeSitterQueryHighlighter.GetHighlightKind("variable.parameter.builtin"));
        Assert.Equal(HighlightKind.TagAttribute, TreeSitterQueryHighlighter.GetHighlightKind("tag.attribute"));
        Assert.Equal(HighlightKind.MarkupHeading, TreeSitterQueryHighlighter.GetHighlightKind("markup.heading.2"));
        Assert.Equal(HighlightKind.PunctuationSpecial, TreeSitterQueryHighlighter.GetHighlightKind("punctuation.special"));
        Assert.Null(TreeSitterQueryHighlighter.GetHighlightKind("injection.content"));
        Assert.Null(TreeSitterQueryHighlighter.GetHighlightKind("nospell"));
    }

    /// <summary>
    /// Verifies Lua highlighting works through the same syntax-language driver.
    /// </summary>
    [Fact]
    public void SyntaxLanguage_ProducesFormatInstructionsForLua()
    {
        var language = new SyntaxLanguage { Language = "Lua" };

        var instructions = language.Match(DriverOperation.FullText, "local message = \"hello\"").ToList();

        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.Keyword);
        Assert.Contains(instructions, instruction => instruction.RuleId == (int)HighlightKind.String);
    }

    /// <summary>
    /// Verifies incremental edits update the stored source and produce current highlights.
    /// </summary>
    [Fact]
    public void TreeSitterDocument_AppliesIncrementalEditsAndHighlightsCurrentSource()
    {
        using var document = new TreeSitterDocument("CSharp");
        document.SetSource("public class Demo { string Message = null; }");

        document.ApplyEdit("public class Demo { string Message = ".Length, "null".Length, "\"hello\"");
        var spans = document.Highlight();

        Assert.Equal("public class Demo { string Message = \"hello\"; }", document.Source);
        Assert.Contains(spans, span => span.Kind == HighlightKind.String);
    }

    /// <summary>
    /// Verifies WPF text-change application handles a single multiline insertion incrementally.
    /// </summary>
    [Fact]
    public void TreeSitterDocument_ApplyTextChangesHandlesSingleMultilineInsertion()
    {
        using var document = new TreeSitterDocument("CSharp");
        var source = "public class Demo\n{\n    string Message = \"hello\";\n}\n";
        document.SetSource(source);
        var insert = "    string Other = \"world\";\n";
        var offset = source.LastIndexOf('}');
        IReadOnlyList<TextChange> changes = [];
        var currentText = string.Empty;
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var editor = new TextBox
            {
                AcceptsReturn = true,
                Text = source,
            };
            editor.TextChanged += (_, e) => changes = e.Changes.ToList();
            editor.Select(offset, 0);
            editor.SelectedText = insert;
            currentText = editor.Text;
        });

        var appliedIncrementally = document.ApplyTextChanges(changes, currentText);
        var spans = document.Highlight();

        Assert.True(appliedIncrementally);
        Assert.Equal(currentText, document.Source);
        Assert.True(spans.Count > 0);
        Assert.Contains(spans, span => span.Kind == HighlightKind.String);
    }

    /// <summary>
    /// Verifies incremental edit point calculation remains correct for edits near the end of larger sources.
    /// </summary>
    [Fact]
    public void TreeSitterDocument_ApplyEditHandlesNearEndReplacement()
    {
        using var document = new TreeSitterDocument("CSharp");
        var source = string.Concat(Enumerable.Repeat("public class Demo { string Message = \"hello\"; }\n", 250));
        document.SetSource(source);
        var start = source.LastIndexOf("hello", StringComparison.Ordinal);

        document.ApplyEdit(start, "hello".Length, "world");
        var spans = document.HighlightSlice(Math.Max(0, start - 100), Math.Min(document.Source.Length, start + 100));

        Assert.Equal(source.Remove(start, "hello".Length).Insert(start, "world"), document.Source);
        Assert.Contains(spans, span => span.Kind == HighlightKind.String);
    }

    /// <summary>
    /// Verifies the vendored Lua parser can be loaded and used for highlighting.
    /// </summary>
    [Fact]
    public void TreeSitterDocument_LoadsLuaParserFromVendor()
    {
        using var document = new TreeSitterDocument("Lua");
        document.SetSource("local message = \"hello\"");

        var spans = document.Highlight();

        Assert.Contains(spans, span => span.Kind == HighlightKind.Keyword);
        Assert.Contains(spans, span => span.Kind == HighlightKind.String);
    }

    /// <summary>
    /// Verifies built-in themes provide distinct keyword colors.
    /// </summary>
    [Fact]
    public void BuiltInThemes_ProvideDistinctKeywordColors()
    {
        var fluent = new DotNetFluentLanguageTheme();
        var tokyoNight = new TokyoNightDarkLanguageTheme();

        Assert.NotNull(fluent.GetBrush(HighlightKind.Keyword));
        Assert.NotNull(tokyoNight.GetBrush(HighlightKind.Keyword));
        Assert.NotEqual(
            fluent.GetBrush(HighlightKind.Keyword)?.ToString(),
            tokyoNight.GetBrush(HighlightKind.Keyword)?.ToString());
    }

    /// <summary>
    /// Verifies disabling syntax highlighting restores a user-provided original template.
    /// </summary>
    [Fact]
    public void SyntaxBox_RestoresOriginalTextBoxTemplateWhenDisabled()
    {
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var editor = new TextBox();
            var originalTemplate = new ControlTemplate(typeof(TextBox));
            editor.Template = originalTemplate;

            SyntaxBox.SetEnable(editor, true);

            Assert.Same(originalTemplate, SyntaxBox.GetOriginalTemplate(editor));
            Assert.NotSame(originalTemplate, editor.Template);

            SyntaxBox.SetEnable(editor, false);

            Assert.Same(originalTemplate, editor.Template);
        });
    }

    /// <summary>
    /// Verifies disabling syntax highlighting clears the template override when the original template was defaulted.
    /// </summary>
    [Fact]
    public void SyntaxBox_ClearsTemplateOverrideWhenOriginalTextBoxTemplateIsDefault()
    {
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var editor = new TextBox();
            Assert.Same(DependencyProperty.UnsetValue, editor.ReadLocalValue(Control.TemplateProperty));

            SyntaxBox.SetEnable(editor, true);
            Assert.NotSame(DependencyProperty.UnsetValue, editor.ReadLocalValue(Control.TemplateProperty));

            SyntaxBox.SetEnable(editor, false);

            Assert.Same(DependencyProperty.UnsetValue, editor.ReadLocalValue(Control.TemplateProperty));
        });
    }

    /// <summary>
    /// Verifies the renderer follows WPF's realized visible-line range after deep scrolling.
    /// </summary>
    [Fact]
    public void SyntaxRenderer_UsesTextBoxVisibleLineRangeAfterDeepScroll()
    {
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var editor = new TextBox
            {
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Height = 360,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Concat(Enumerable.Range(0, 2_000).Select(index => $"line {index:0000}\n")),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Width = 640,
            };
            SyntaxBox.SetSyntaxDriver(editor, new SyntaxLanguage { Language = "CSharp" });
            SyntaxBox.SetEnable(editor, true);

            var window = new Window
            {
                Content = editor,
                Height = 420,
                Left = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Top = -20_000,
                Width = 700,
                WindowStyle = WindowStyle.None,
            };

            try
            {
                window.Show();
                WpfScrollSimulation.PumpLayout(window);
                editor.ScrollToLine(1_500);
                WpfScrollSimulation.PumpLayout(window);

                var renderer = FindDescendant<SyntaxRenderer>(window, "PART_SyntaxRenderer");
                Assert.NotNull(renderer);

                var (first, last) = renderer.GetVisibleLineRange(lineHeight: 1, verticalOffset: 0);

                Assert.Equal(editor.GetFirstVisibleLineIndex(), first);
                Assert.True(last >= editor.GetLastVisibleLineIndex());
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Verifies line extraction preserves absolute start offsets.
    /// </summary>
    [Fact]
    public void TextExtensions_GetLinesPreservesStartOffsets()
    {
        var lines = "one\ntwo\nthree".GetLines(0, int.MaxValue, out var totalLines);

        Assert.Equal(3, totalLines);
        Assert.Equal([(0, "one\n"), (4, "two\n"), (8, "three")], lines.Select(line => (line.StartIndex, line.Text)).ToArray());
    }

    /// <summary>
    /// Verifies line indexing can retrieve bottom ranges without changing public line semantics.
    /// </summary>
    [Fact]
    public void TextLineIndex_GetLineRangePreservesBottomOffsets()
    {
        var text = "zero\none\ntwo\nthree\nfour\n";
        var index = TextLineIndex.Build(text);

        var lines = index.GetLineRange(text, 3, 5);

        Assert.Equal(6, index.Count);
        Assert.Equal([(13, "three\n"), (19, "four\n"), (24, "")], lines.Select(line => (line.StartIndex, line.Text)).ToArray());
    }

    /// <summary>
    /// Verifies single-change line-index updates match a full rebuild.
    /// </summary>
    [Fact]
    public void TextLineIndex_ApplyChangeMatchesRebuild()
    {
        var original = "zero\none\ntwo\nthree\nfour";
        var insert = "alpha\nbeta\n";
        var offset = original.IndexOf("three", StringComparison.Ordinal);
        var current = original.Insert(offset, insert);
        var incremental = TextLineIndex.Build(original).ApplyChange(current, offset, removedLength: 0, addedLength: insert.Length);
        var rebuilt = TextLineIndex.Build(current);

        Assert.Equal(rebuilt.Count, incremental.Count);
        Assert.Equal(
            rebuilt.GetLineRange(current, 0, rebuilt.Count - 1).Select(line => (line.StartIndex, line.Text)).ToArray(),
            incremental.GetLineRange(current, 0, incremental.Count - 1).Select(line => (line.StartIndex, line.Text)).ToArray());
    }

    /// <summary>
    /// Searches the WPF visual tree for a named descendant of a requested element type.
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            var descendant = FindDescendant<T>(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
