using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Highlights comma-delimited keywords using an Aho-Corasick matcher for fast line-level scans.
/// </summary>
public sealed class KeywordRule : ISyntaxRule
{
    private AhoCorasickSearch? engine;
    private string? engineKeywords;
    private bool engineWholeWordsOnly;

    /// <summary>
    /// Gets or sets the rule identifier assigned by a <see cref="SyntaxConfig"/>.
    /// </summary>
    public int RuleId { get; set; }

    /// <summary>
    /// Gets or sets the operation level where keyword matching runs.
    /// </summary>
    public DriverOperation Op { get; set; } = DriverOperation.Line;

    /// <summary>
    /// Gets or sets the foreground brush applied to keyword matches.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets text decorations applied to keyword matches.
    /// </summary>
    public TextDecorationCollection TextDecorations { get; set; } = [];

    /// <summary>
    /// Gets or sets comma-delimited keywords used to build the search automaton.
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// Gets or sets whether matches must start and end on word boundaries.
    /// </summary>
    public bool WholeWordsOnly { get; set; } = true;

    /// <summary>
    /// Finds keyword matches and converts them into formatting instructions.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(string text)
    {
        foreach (var match in GetEngine().FindAll(text))
        {
            yield return new FormatInstruction(
                RuleId,
                match.Position,
                match.Length,
                Foreground,
                TextDecorations.Count > 0 ? TextDecorations : null);
        }
    }

    /// <summary>
    /// Reuses the compiled search automaton until keywords or whole-word behavior changes.
    /// </summary>
    private AhoCorasickSearch GetEngine()
    {
        if (engine is not null && engineKeywords == Keywords && engineWholeWordsOnly == WholeWordsOnly)
        {
            return engine;
        }

        var keywordList = (Keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        engine = new AhoCorasickSearch(keywordList, WholeWordsOnly);
        engineKeywords = Keywords;
        engineWholeWordsOnly = WholeWordsOnly;
        return engine;
    }
}
