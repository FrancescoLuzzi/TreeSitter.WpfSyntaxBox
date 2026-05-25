using System;
using System.Collections.Generic;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Combines multiple syntax drivers into one range-aware driver.
/// </summary>
internal sealed class AggregateSyntaxDriver : IRangeSyntaxDriver
{
    private readonly SyntaxDriverCollection drivers;

    /// <summary>
    /// Creates an aggregate over an attached syntax-driver collection.
    /// </summary>
    public AggregateSyntaxDriver(SyntaxDriverCollection drivers)
    {
        this.drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
    }

    /// <summary>
    /// Gets the union of all child driver abilities.
    /// </summary>
    public DriverOperation Abilities
    {
        get
        {
            var abilities = DriverOperation.None;
            foreach (var driver in drivers)
            {
                abilities |= driver.Abilities;
            }

            return abilities;
        }
    }

    /// <summary>
    /// Runs all compatible drivers across the whole text.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(DriverOperation operation, string text) => Match(operation, text, 0, text.Length);

    /// <summary>
    /// Runs all compatible drivers, forwarding range limits to drivers that support viewport slicing.
    /// </summary>
    public IEnumerable<FormatInstruction> Match(DriverOperation operation, string text, int start, int end)
    {
        foreach (var driver in drivers)
        {
            if ((driver.Abilities & operation) == 0)
            {
                continue;
            }

            var matches = driver is IRangeSyntaxDriver rangeDriver
                ? rangeDriver.Match(operation, text, start, end)
                : driver.Match(operation, text);

            foreach (var match in matches)
            {
                yield return match;
            }
        }
    }
}
