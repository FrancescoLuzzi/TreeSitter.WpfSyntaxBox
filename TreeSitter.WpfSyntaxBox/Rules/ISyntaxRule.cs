using System.Collections.Generic;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Defines a simple syntax rule that can emit formatting instructions for a text segment.
/// </summary>
public interface ISyntaxRule
{
    /// <summary>
    /// Gets or sets the rule identifier assigned by the containing syntax configuration.
    /// </summary>
    int RuleId { get; set; }

    /// <summary>
    /// Gets or sets the operation level where this rule should run.
    /// </summary>
    DriverOperation Op { get; set; }

    /// <summary>
    /// Returns formatting instructions for all matches in the supplied text.
    /// </summary>
    IEnumerable<FormatInstruction> Match(string text);
}
