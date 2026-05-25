using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox.Demo;

/// <summary>
/// Demo window that exercises syntax highlighting, theme switching, editing commands, and large generated sources.
/// </summary>
public partial class MainWindow : Window
{
    private bool wrapLines;
    private string currentLanguage = "CSharp";

    /// <summary>
    /// Initializes the demo UI and applies the initial language and theme.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        ApplyLanguage();
    }

    /// <summary>
    /// Enables or disables the syntax overlay for the demo editor.
    /// </summary>
    private void ToggleSyntax_Click(object sender, RoutedEventArgs e)
    {
        SyntaxBox.SetEnable(Editor, !SyntaxBox.GetEnable(Editor));
    }

    /// <summary>
    /// Toggles line wrapping while keeping scroll behavior consistent with the selected mode.
    /// </summary>
    private void ToggleWrap_Click(object sender, RoutedEventArgs e)
    {
        wrapLines = !wrapLines;
        Editor.HorizontalScrollBarVisibility = wrapLines ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Editor.TextWrapping = wrapLines ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    /// <summary>
    /// Applies a selected language after the window has loaded.
    /// </summary>
    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyLanguage();
    }

    /// <summary>
    /// Applies a selected syntax theme after the window has loaded.
    /// </summary>
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyTheme();
    }

    /// <summary>
    /// Prefixes the selected lines with the current language's line-comment marker.
    /// </summary>
    public void OnCommentCommand(object sender, ExecutedRoutedEventArgs args)
    {
        var lineComment = GetLineComment();
        ReplaceSelectedLines(line => line.Text.Insert(FindLineStart(line), lineComment));
    }

    /// <summary>
    /// Removes the current language's line-comment marker from selected lines when present.
    /// </summary>
    public void OnUncommentCommand(object sender, ExecutedRoutedEventArgs args)
    {
        ReplaceSelectedLines(line =>
        {
            var lineComment = GetLineComment();
            var start = FindLineStart(line);
            return line.Text[start..].StartsWith(lineComment, StringComparison.Ordinal)
                ? line.Text[..start] + line.Text[(start + lineComment.Length)..]
                : line.Text;
        });
    }

    /// <summary>
    /// Loads generated sample code for the selected language and refreshes the syntax driver.
    /// </summary>
    private void ApplyLanguage()
    {
        currentLanguage = GetSelectedTag(LanguageSelector, "CSharp");
        EditorSyntaxLanguage.Language = currentLanguage;
        Editor.Text = currentLanguage == "Lua" ? CreateLuaSource() : CreateCSharpSource();
        Editor.CaretIndex = 0;
        Editor.ScrollToHome();
        UpdateStatusText();
        SyntaxBox.InvalidateDriver(Editor);
    }

    /// <summary>
    /// Applies the selected highlight theme and matching editor chrome colors.
    /// </summary>
    private void ApplyTheme()
    {
        var theme = GetSelectedTag(ThemeSelector, "TokyoNight");
        if (theme == "DotNetFluent")
        {
            EditorSyntaxLanguage.Theme = new DotNetFluentLanguageTheme();
            SetEditorColors(
                background: "#FFFFFF",
                foreground: "#1F1F1F",
                lineNumbersBackground: "#F3F3F3",
                lineNumbersForeground: "#767676",
                border: "#D1D1D1",
                caret: "#000000");
        }
        else
        {
            EditorSyntaxLanguage.Theme = new TokyoNightDarkLanguageTheme();
            SetEditorColors(
                background: "#1A1B26",
                foreground: "#C0CAF5",
                lineNumbersBackground: "#16161E",
                lineNumbersForeground: "#565F89",
                border: "#414868",
                caret: "#C0CAF5");
        }

        SyntaxBox.InvalidateDriver(Editor);
    }

    /// <summary>
    /// Applies editor, gutter, border, status, and caret colors for a theme.
    /// </summary>
    private void SetEditorColors(string background, string foreground, string lineNumbersBackground, string lineNumbersForeground, string border, string caret)
    {
        Editor.Background = Brush(background);
        Editor.Foreground = Brush(foreground);
        Editor.BorderBrush = Brush(border);
        Editor.CaretBrush = Brush(caret);
        SyntaxBox.SetLineNumbersBackground(Editor, Brush(lineNumbersBackground));
        SyntaxBox.SetLineNumbersForeground(Editor, Brush(lineNumbersForeground));
        StatusText.Foreground = Brush(foreground);
    }

    /// <summary>
    /// Returns the line-comment prefix for the currently selected language.
    /// </summary>
    private string GetLineComment() => currentLanguage == "Lua" ? "--" : "//";

    /// <summary>
    /// Returns the display name for the currently selected language.
    /// </summary>
    private string GetLanguageDisplayName() => currentLanguage == "Lua" ? "Lua" : "C#";

    /// <summary>
    /// Updates the status text with language and document-size information.
    /// </summary>
    private void UpdateStatusText() =>
        StatusText.Text = $"Vendored TreeSitter {GetLanguageDisplayName()} highlighting - {Editor.LineCount:N0} lines";

    /// <summary>
    /// Gets the selected combo-box item tag or a fallback value.
    /// </summary>
    private static string GetSelectedTag(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : fallback;

    /// <summary>
    /// Creates and freezes a solid brush for demo UI styling.
    /// </summary>
    private static Brush Brush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Generates a large C# sample to exercise viewport rendering and tree-sitter parse performance.
    /// </summary>
    private static string CreateCSharpSource()
    {
        var builder = new StringBuilder(256_000);
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine("namespace TreeSitter.GeneratedDemo;");
        builder.AppendLine();

        for (var type = 0; type < 180; type++)
        {
            builder.AppendLine($"public sealed class GeneratedProcessor{type:000}");
            builder.AppendLine("{");
            builder.AppendLine($"    private readonly Dictionary<string, int> cache = new(StringComparer.Ordinal) {{ [\"seed\"] = {type} }};");
            builder.AppendLine();

            for (var method = 0; method < 4; method++)
            {
                builder.AppendLine($"    public int Compute{method}(string input, int repeat)");
                builder.AppendLine("    {");
                builder.AppendLine($"        var value = input.Length + repeat + {type + method};");
                builder.AppendLine("        for (var i = 0; i < repeat; i++)");
                builder.AppendLine("        {");
                builder.AppendLine("            value += input.Contains(\"tree\", StringComparison.OrdinalIgnoreCase) ? i : -i;");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        if (value % 3 == 0)");
                builder.AppendLine("        {");
                builder.AppendLine($"            cache[input] = value + {method};");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        return cache.TryGetValue(input, out var cached) ? cached : value;");
                builder.AppendLine("    }");
                builder.AppendLine();
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Generates a large Lua sample to exercise non-C# highlighting and scrolling behavior.
    /// </summary>
    private static string CreateLuaSource()
    {
        var builder = new StringBuilder(220_000);
        builder.AppendLine("local processors = {}");
        builder.AppendLine("local state = { name = \"TreeSitter Lua demo\", enabled = true }");
        builder.AppendLine();

        for (var module = 0; module < 240; module++)
        {
            builder.AppendLine($"-- generated processor {module:000}");
            builder.AppendLine($"processors[{module + 1}] = function(input, repeat_count)");
            builder.AppendLine($"    local total = #input + repeat_count + {module}");
            builder.AppendLine("    for i = 1, repeat_count do");
            builder.AppendLine("        if string.find(input, \"tree\") then");
            builder.AppendLine("            total = total + i");
            builder.AppendLine("        else");
            builder.AppendLine("            total = total - i");
            builder.AppendLine("        end");
            builder.AppendLine("    end");
            builder.AppendLine();
            builder.AppendLine("    if total % 3 == 0 then");
            builder.AppendLine($"        return string.format(\"processor-{module:000}:%s:%d\", state.name, total)");
            builder.AppendLine("    end");
            builder.AppendLine();
            builder.AppendLine("    return total");
            builder.AppendLine("end");
            builder.AppendLine();
        }

        builder.AppendLine("return processors");
        return builder.ToString();
    }

    /// <summary>
    /// Replaces selected lines while preserving selection anchors relative to the affected line ends.
    /// </summary>
    private void ReplaceSelectedLines(Func<TextLine, string> transform)
    {
        var selectionStart = Editor.SelectionStart;
        var selectionLength = Editor.SelectionLength;
        var selectionEnd = selectionStart + selectionLength;
        var firstLine = Editor.GetLineIndexFromCharacterIndex(selectionStart);
        var lastLine = selectionLength > 0
            ? Editor.GetLineIndexFromCharacterIndex(selectionStart + selectionLength - 1)
            : firstLine;

        var affectedLines = Editor.Text.GetLines(firstLine, lastLine, out var totalLines).ToList();
        var startOffset = affectedLines[0].EndIndex - selectionStart;
        var endOffset = affectedLines[^1].EndIndex - selectionEnd;

        var replacement = string.Concat(affectedLines.Select(transform));
        var builder = new StringBuilder();
        builder.Append(Editor.Text[..affectedLines[0].StartIndex]);
        builder.Append(replacement);
        builder.Append(Editor.Text[(affectedLines[^1].StartIndex + affectedLines[^1].Text.Length)..]);
        Editor.Text = builder.ToString();

        var firstAffected = Editor.Text.GetLines(firstLine, firstLine, out totalLines).Single();
        var lastAffected = Editor.Text.GetLines(lastLine, lastLine, out totalLines).Single();
        selectionStart = Math.Max(firstAffected.StartIndex, firstAffected.EndIndex - startOffset);
        selectionEnd = Math.Max(lastAffected.StartIndex, lastAffected.EndIndex - endOffset);
        Editor.Select(selectionStart, Math.Max(0, selectionEnd - selectionStart));
    }

    /// <summary>
    /// Finds the first non-whitespace character in a line, excluding the trailing newline of blank lines.
    /// </summary>
    private static int FindLineStart(TextLine line)
    {
        for (var i = 0; i < line.Text.Length; i++)
        {
            if (!char.IsWhiteSpace(line.Text[i]))
            {
                return i;
            }
        }

        return line.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? line.Text.Length - Environment.NewLine.Length
            : line.Text.Length;
    }
}
