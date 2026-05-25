using System;
using System.Collections.Generic;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Indexes source line starts so viewport rendering can locate bottom-of-file lines without scanning from the document start.
/// </summary>
internal sealed class TextLineIndex
{
    private readonly int[] lineStarts;

    private TextLineIndex(int[] lineStarts)
    {
        this.lineStarts = lineStarts;
    }

    /// <summary>
    /// Gets the number of logical lines in the indexed source.
    /// </summary>
    public int Count => lineStarts.Length;

    /// <summary>
    /// Builds a line-start index for the supplied source text.
    /// </summary>
    public static TextLineIndex Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return new TextLineIndex(starts.ToArray());
    }

    /// <summary>
    /// Applies one text replacement to this line index, falling back to callers for multi-change edits.
    /// </summary>
    public TextLineIndex ApplyChange(string currentText, int offset, int removedLength, int addedLength)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(removedLength);
        ArgumentOutOfRangeException.ThrowIfNegative(addedLength);

        var addedEnd = Math.Min(currentText.Length, offset + addedLength);
        var insertedLineCount = 0;
        for (var i = offset; i < addedEnd; i++)
        {
            if (currentText[i] == '\n')
            {
                insertedLineCount++;
            }
        }

        var oldEnd = offset + removedLength;
        var delta = addedLength - removedLength;
        var prefixCount = 0;
        var suffixCount = 0;
        foreach (var lineStart in lineStarts)
        {
            if (lineStart <= offset)
            {
                prefixCount++;
            }
            else if (lineStart > oldEnd)
            {
                suffixCount++;
            }
        }

        var updated = new int[prefixCount + insertedLineCount + suffixCount];
        var write = 0;
        for (var i = 0; i < lineStarts.Length && lineStarts[i] <= offset; i++)
        {
            updated[write++] = lineStarts[i];
        }

        for (var i = offset; i < addedEnd; i++)
        {
            if (currentText[i] == '\n')
            {
                updated[write++] = i + 1;
            }
        }

        foreach (var lineStart in lineStarts)
        {
            if (lineStart > oldEnd)
            {
                updated[write++] = lineStart + delta;
            }
        }

        return new TextLineIndex(updated);
    }

    /// <summary>
    /// Gets indexed lines in an inclusive line range.
    /// </summary>
    public List<TextLine> GetLineRange(string text, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(text);

        first = Math.Max(0, first);
        last = Math.Min(Math.Max(first, last), Count - 1);
        var lines = new List<TextLine>(Math.Max(0, last - first + 1));
        for (var lineNumber = first; lineNumber <= last; lineNumber++)
        {
            var start = lineStarts[lineNumber];
            var end = lineNumber + 1 < Count ? lineStarts[lineNumber + 1] : text.Length;
            lines.Add(new TextLine(lineNumber, start, text[start..end]));
        }

        return lines;
    }

    /// <summary>
    /// Gets indexed line spans in an inclusive line range without allocating line substrings.
    /// </summary>
    public List<TextLineSpan> GetLineSpans(string text, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(text);

        first = Math.Max(0, first);
        last = Math.Min(Math.Max(first, last), Count - 1);
        var lines = new List<TextLineSpan>(Math.Max(0, last - first + 1));
        for (var lineNumber = first; lineNumber <= last; lineNumber++)
        {
            lines.Add(GetLineSpan(lineNumber, text.Length));
        }

        return lines;
    }

    /// <summary>
    /// Gets absolute character bounds for an inclusive line range without allocating line descriptors.
    /// </summary>
    public (int Start, int End) GetRangeBounds(int first, int last, int textLength)
    {
        first = Math.Max(0, first);
        last = Math.Min(Math.Max(first, last), Count - 1);
        var start = lineStarts[first];
        var end = last + 1 < Count ? lineStarts[last + 1] : textLength;
        return (start, end);
    }

    /// <summary>
    /// Gets a single indexed line span by line number.
    /// </summary>
    private TextLineSpan GetLineSpan(int lineNumber, int textLength)
    {
        var start = lineStarts[lineNumber];
        var end = lineNumber + 1 < Count ? lineStarts[lineNumber + 1] : textLength;
        return new TextLineSpan(lineNumber, start, end - start);
    }
}

/// <summary>
/// Describes one source line by absolute character range without materializing its text.
/// </summary>
internal readonly struct TextLineSpan
{
    /// <summary>
    /// Creates a source line span.
    /// </summary>
    public TextLineSpan(int lineNumber, int startIndex, int length)
    {
        LineNumber = lineNumber;
        StartIndex = startIndex;
        Length = length;
    }

    /// <summary>
    /// Gets the zero-based source line number.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the absolute character index where the line starts.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the line length in characters.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the exclusive absolute character index where the line ends.
    /// </summary>
    public int EndIndex => StartIndex + Length;

    /// <summary>
    /// Materializes this line's text from a source string.
    /// </summary>
    public string GetText(string source) => source.Substring(StartIndex, Length);
}
