using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Produces formatting instructions for one or more syntax-highlighting operations.
/// </summary>
public interface ISyntaxDriver
{
    /// <summary>
    /// Gets the operation types supported by this driver.
    /// </summary>
    DriverOperation Abilities { get; }

    /// <summary>
    /// Returns formatting instructions for the supplied text and requested operation.
    /// </summary>
    IEnumerable<FormatInstruction> Match(DriverOperation operation, string text);
}

/// <summary>
/// Describes a formatted character range produced by a syntax driver.
/// </summary>
public readonly struct FormatInstruction : IEquatable<FormatInstruction>
{
    /// <summary>
    /// Gets the rule or highlight identifier used to order overlapping instructions.
    /// </summary>
    public readonly int RuleId;

    /// <summary>
    /// Gets the zero-based character index where the instruction starts.
    /// </summary>
    public readonly int FromChar;

    /// <summary>
    /// Gets the number of characters covered by the instruction.
    /// </summary>
    public readonly int Length;

    /// <summary>
    /// Gets the optional foreground brush applied to the range.
    /// </summary>
    public readonly Brush? Foreground;

    /// <summary>
    /// Gets optional text decorations applied to the range.
    /// </summary>
    public readonly TextDecorationCollection? TextDecorations;

    /// <summary>
    /// Gets an optional URI string associated with the range.
    /// </summary>
    public readonly string? Link;

    /// <summary>
    /// Creates a formatting instruction for a character range.
    /// </summary>
    public FormatInstruction(
        int ruleId,
        int fromChar,
        int length,
        Brush? foreground = null,
        TextDecorationCollection? textDecorations = null,
        string? link = null)
    {
        RuleId = ruleId;
        FromChar = fromChar;
        Length = length;
        Foreground = foreground;
        TextDecorations = textDecorations;
        Link = link;
    }

    /// <summary>
    /// Compares instruction identity and range values used by tests and cache adjustment checks.
    /// </summary>
    public bool Equals(FormatInstruction other) =>
        RuleId == other.RuleId && FromChar == other.FromChar && Length == other.Length && Link == other.Link;

    /// <summary>
    /// Returns whether the supplied object is an equivalent instruction.
    /// </summary>
    public override bool Equals(object? obj) => obj is FormatInstruction other && Equals(other);

    /// <summary>
    /// Returns a hash code for the comparable instruction fields.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(RuleId, FromChar, Length, Link);

    /// <summary>
    /// Compares two instructions for equality.
    /// </summary>
    public static bool operator ==(FormatInstruction left, FormatInstruction right) => left.Equals(right);

    /// <summary>
    /// Compares two instructions for inequality.
    /// </summary>
    public static bool operator !=(FormatInstruction left, FormatInstruction right) => !left.Equals(right);
}

/// <summary>
/// Identifies the level at which a syntax driver can produce highlights.
/// </summary>
[Flags]
public enum DriverOperation : byte
{
    None = 0,
    Line = 1,
    Block = 2,
    FullText = 4,
}
