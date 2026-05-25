using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Highlights ranges matched by a compiled regular expression.
/// </summary>
public sealed class RegexRule : ISyntaxRule
{
    private Regex? regex;
    private string? regexPattern;

    /// <summary>
    /// Gets or sets the rule identifier assigned by a <see cref="SyntaxConfig"/>.
    /// </summary>
    public int RuleId { get; set; }

    /// <summary>
    /// Gets or sets the operation level where the regex runs.
    /// </summary>
    public DriverOperation Op { get; set; } = DriverOperation.None;

    /// <summary>
    /// Gets or sets the foreground brush applied to regex matches.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets text decorations applied to regex matches.
    /// </summary>
    public TextDecorationCollection TextDecorations { get; set; } = [];

    /// <summary>
    /// Gets or sets the regular expression pattern used by the rule.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Enumerates regex matches as formatting instructions.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(string text)
    {
        foreach (Match match in GetRegex().Matches(text))
        {
            yield return new FormatInstruction(
                RuleId,
                match.Index,
                match.Length,
                Foreground,
                TextDecorations.Count > 0 ? TextDecorations : null);
        }
    }

    /// <summary>
    /// Reuses the compiled regex until the pattern changes.
    /// </summary>
    private Regex GetRegex()
    {
        if (regex is not null && regexPattern == Pattern)
        {
            return regex;
        }

        regex = new Regex(Pattern, RegexOptions.Compiled);
        regexPattern = Pattern;
        return regex;
    }
}
