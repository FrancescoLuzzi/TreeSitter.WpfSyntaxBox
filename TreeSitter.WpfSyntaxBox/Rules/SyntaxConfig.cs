using System.Collections.Generic;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Aggregates simple syntax rules into a single driver.
/// </summary>
public sealed class SyntaxConfig : List<ISyntaxRule>, ISyntaxDriver
{
    /// <summary>
    /// Gets the union of operations supported by all contained rules.
    /// </summary>
    public DriverOperation Abilities
    {
        get
        {
            var abilities = DriverOperation.None;
            foreach (var rule in this)
            {
                abilities |= rule.Op;
            }

            return abilities;
        }
    }

    /// <summary>
    /// Runs matching rules for the requested operation and yields their formatting instructions.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(DriverOperation operation, string text)
    {
        for (var i = 0; i < Count; i++)
        {
            var rule = this[i];
            rule.RuleId = i;
            if ((rule.Op & operation) == 0)
            {
                continue;
            }

            foreach (var instruction in rule.Match(text))
            {
                yield return instruction;
            }
        }
    }
}
