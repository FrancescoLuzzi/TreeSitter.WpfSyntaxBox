using System;
using System.Collections.Generic;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Provides line and word-boundary helpers used by rendering virtualization and simple syntax rules.
/// </summary>
public static class TextExtensions
{
    /// <summary>
    /// Counts logical text lines using newline characters without allocating line substrings.
    /// </summary>
    internal static int CountLines(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Gets a bounded line range through a temporary line-start index.
    /// </summary>
    internal static List<TextLine> GetLineRange(this string text, int first, int last)
    {
        return TextLineIndex.Build(text).GetLineRange(text, first, last);
    }

    /// <summary>
    /// Gets lines in a range and returns the total number of lines in the source.
    /// </summary>
    public static List<TextLine> GetLines(this string text, int first, int last, out int totalLines)
    {
        var index = TextLineIndex.Build(text);
        totalLines = index.Count;
        return index.GetLineRange(text, first, last);
    }

    /// <summary>
    /// Returns the line containing a character position for editor features such as auto-indent.
    /// </summary>
    internal static TextLine? GetLineAtPosition(this string text, int position)
    {
        if (position < 0 || position > text.Length)
        {
            return null;
        }

        var start = position == 0 ? 0 : text.LastIndexOf('\n', Math.Min(position - 1, text.Length - 1));
        start = start < 0 ? 0 : start + 1;
        var end = text.IndexOf('\n', position);
        end = end < 0 ? text.Length : end + 1;
        return new TextLine(-1, start, text[start..end]);
    }

    /// <summary>
    /// Returns whether a position is at the start of a word for whole-keyword matching.
    /// </summary>
    internal static bool IsStartWordBoundary(this string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);

        return position <= 0 || !IsWordCharacter(text[position - 1]);
    }

    /// <summary>
    /// Returns whether a position is at the end of a word for whole-keyword matching.
    /// </summary>
    internal static bool IsEndWordBoundary(this string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);

        return position >= text.Length - 1 || !IsWordCharacter(text[position + 1]);
    }

    /// <summary>
    /// Returns whether a character participates in keyword word-boundary checks.
    /// </summary>
    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
