namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Represents one substring match returned by the keyword search engine.
/// </summary>
public readonly struct Substring
{
    /// <summary>
    /// Gets the zero-based character position of the match.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Gets the number of characters in the match.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Gets the matched dictionary value.
    /// </summary>
    public string Value { get; init; }
}
