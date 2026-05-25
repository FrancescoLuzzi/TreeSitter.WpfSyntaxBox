namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Represents a classified source range returned by tree-sitter highlighting.
/// </summary>
public readonly record struct HighlightSpan(
    int Start,
    int Length,
    int StartByte,
    int EndByte,
    HighlightKind Kind,
    string? Link = null)
{
    /// <summary>
    /// Gets the exclusive end character index for the span.
    /// </summary>
    public int End => Start + Length;
}
