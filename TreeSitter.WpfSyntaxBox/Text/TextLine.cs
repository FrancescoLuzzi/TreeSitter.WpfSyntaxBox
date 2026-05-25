namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Describes one source line and its absolute character start offset.
/// </summary>
public readonly struct TextLine
{
    /// <summary>
    /// Gets the zero-based line number in the source document.
    /// </summary>
    public readonly int LineNumber;

    /// <summary>
    /// Gets the absolute character index where the line starts.
    /// </summary>
    public readonly int StartIndex;

    /// <summary>
    /// Gets the line text, including its trailing newline when present.
    /// </summary>
    public readonly string Text;

    /// <summary>
    /// Gets the exclusive absolute character index where the line ends.
    /// </summary>
    public int EndIndex => StartIndex + (Text?.Length ?? 0);

    /// <summary>
    /// Creates a line descriptor for rendering and text manipulation.
    /// </summary>
    public TextLine(int lineNumber, int startIndex, string text)
    {
        LineNumber = lineNumber;
        StartIndex = startIndex;
        Text = text;
    }
}
