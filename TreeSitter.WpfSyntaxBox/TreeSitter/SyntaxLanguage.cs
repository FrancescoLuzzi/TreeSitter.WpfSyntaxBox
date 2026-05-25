using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Syntax driver that adapts tree-sitter highlighting to WPF formatting instructions for a selected language and theme.
/// </summary>
public sealed class SyntaxLanguage : IRangeSyntaxDriver, IIncrementalSyntaxDriver, ISynchronizedSyntaxDriver, IDisposable
{
    private string language = "CSharp";
    private TreeSitterDocument? document;
    private string documentLanguage = string.Empty;
    private string source = string.Empty;

    /// <summary>
    /// Gets or sets the tree-sitter language name or alias used for parsing.
    /// </summary>
    public string Language
    {
        get => language;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (string.Equals(language, value, StringComparison.Ordinal))
            {
                return;
            }

            language = value;
            ResetDocument();
        }
    }

    /// <summary>
    /// Gets or sets the theme used to convert highlight kinds into WPF brushes.
    /// </summary>
    public LanguageTheme Theme { get; set; } = LanguageTheme.Default;

    /// <summary>
    /// Gets the syntax operations supported by tree-sitter highlighting.
    /// </summary>
    public DriverOperation Abilities => DriverOperation.FullText;

    /// <summary>
    /// Gets the number of full parses performed, used by tests to guard scroll/resize hot paths.
    /// </summary>
    internal int FullParseCount { get; private set; }

    /// <summary>
    /// Gets the number of successful incremental parses performed after text edits.
    /// </summary>
    internal int IncrementalParseCount { get; private set; }

    /// <summary>
    /// Highlights the whole provided text for callers that do not supply a viewport range.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(DriverOperation operation, string text) =>
        Match(operation, text, 0, text.Length);

    /// <summary>
    /// Highlights a character range and yields formatting instructions only for spans with theme brushes.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(DriverOperation operation, string text, int start, int end)
    {
        if ((operation & DriverOperation.FullText) == 0 || string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var spans = HighlightRange(text, start, end);
        foreach (var span in spans)
        {
            var foreground = Theme.GetBrush(span.Kind);
            if (foreground is not null)
            {
                yield return new FormatInstruction((int)span.Kind, span.Start, span.Length, foreground: foreground, link: span.Link);
            }
        }
    }

    /// <summary>
    /// Replaces the current source and performs a full tree-sitter parse.
    /// </summary>
    public void SetSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureDocument();
        document!.SetSource(text);
        source = text;
        FullParseCount++;
    }

    /// <summary>
    /// Applies WPF edit deltas to the existing parse tree and records whether parsing stayed incremental.
    /// </summary>
    public bool ApplyTextChanges(IReadOnlyList<TextChange> changes, string currentText)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(currentText);
        EnsureDocument();

        var appliedIncrementally = document!.ApplyTextChanges(changes, currentText);
        source = currentText;
        if (appliedIncrementally)
        {
            IncrementalParseCount++;
        }
        else
        {
            FullParseCount++;
        }

        return appliedIncrementally;
    }

    /// <summary>
    /// Returns whether this driver already represents the text being rendered.
    /// </summary>
    bool ISynchronizedSyntaxDriver.IsSynchronizedWith(string text) => string.Equals(source, text, StringComparison.Ordinal);

    /// <summary>
    /// Ensures a synchronized document exists and then requests a viewport-sized highlight slice.
    /// </summary>
    private IReadOnlyList<HighlightSpan> HighlightRange(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureDocument();
        if (!string.Equals(source, text, StringComparison.Ordinal))
        {
            // Direct driver calls can synchronize explicitly. The WPF renderer checks
            // IsSynchronizedWith first so resize/scroll never parse as a side effect.
            SetSource(text);
        }

        return document!.HighlightSlice(start, end);
    }

    /// <summary>
    /// Creates or recreates the document when the language selection changes.
    /// </summary>
    private void EnsureDocument()
    {
        if (document is not null && string.Equals(documentLanguage, Language, StringComparison.Ordinal))
        {
            return;
        }

        ResetDocument();
        documentLanguage = Language;
        document = new TreeSitterDocument(Language);
        source = string.Empty;
    }

    /// <summary>
    /// Disposes the current document and clears synchronization state.
    /// </summary>
    private void ResetDocument()
    {
        document?.Dispose();
        document = null;
        source = string.Empty;
    }

    /// <summary>
    /// Releases native tree-sitter resources held by the current document.
    /// </summary>
    public void Dispose()
    {
        ResetDocument();
    }
}
