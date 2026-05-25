using System;
using System.Collections.Generic;
using System.Windows.Controls;
using TreeSitterLanguage = global::TreeSitter.Language;
using TreeSitterParser = global::TreeSitter.Parser;
using TreeSitterPoint = global::TreeSitter.Point;
using TreeSitterTree = global::TreeSitter.Tree;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Owns a tree-sitter parser, source snapshot, and syntax tree for one document, including incremental edit application.
/// </summary>
public sealed class TreeSitterDocument : IDisposable
{
    private readonly TreeSitterLanguage language;
    private readonly TreeSitterQueryHighlighter? queryHighlighter;
    private readonly TreeSitterParser parser;
    private TreeSitterTree? tree;
    private string source = string.Empty;
    private bool disposed;

    /// <summary>
    /// Creates a document parser for a supported tree-sitter language name or alias.
    /// </summary>
    public TreeSitterDocument(string languageName)
    {
        var languageId = TreeSitterLanguageFactory.GetSupportedLanguageId(languageName);
        language = new TreeSitterLanguage(languageId);
        queryHighlighter = TreeSitterQueryHighlighter.Create(language, languageId);
        parser = new TreeSitterParser(language);
    }

    /// <summary>
    /// Gets the source text represented by the current parse tree.
    /// </summary>
    public string Source => source;

    /// <summary>
    /// Replaces the entire source and reparses without using an existing tree as edit context.
    /// </summary>
    public void SetSource(string value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(value);

        ReplaceTree(Parse(value, null));
        source = value;
    }

    /// <summary>
    /// Applies one text replacement by editing the existing syntax tree before reparsing with tree-sitter's incremental parser.
    /// </summary>
    public void ApplyEdit(int start, int length, string newText)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(newText);

        if (start > source.Length || start + length > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var oldSource = source;
        var newSource = string.Concat(oldSource.AsSpan(0, start), newText, oldSource.AsSpan(start + length));
        ApplyIncrementalEdit(oldSource, newSource, start, length, newText.Length);
    }

    /// <summary>
    /// Applies WPF text changes incrementally when their offsets still match the stored source, otherwise falls back to a full parse.
    /// </summary>
    public bool ApplyTextChanges(IReadOnlyList<TextChange> changes, string currentText)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(currentText);

        if (changes.Count == 0)
        {
            if (!string.Equals(source, currentText, StringComparison.Ordinal))
            {
                SetSource(currentText);
                return false;
            }

            return true;
        }

        if (tree is null)
        {
            SetSource(currentText);
            return false;
        }

        if (changes.Count == 1)
        {
            return ApplySingleTextChange(changes[0], currentText);
        }

        foreach (var change in changes)
        {
            if (change.Offset < 0 || change.RemovedLength < 0 || change.AddedLength < 0 || change.Offset > source.Length || change.Offset + change.RemovedLength > source.Length)
            {
                SetSource(currentText);
                return false;
            }

            if (change.Offset > currentText.Length || change.Offset + change.AddedLength > currentText.Length)
            {
                SetSource(currentText);
                return false;
            }

            var insertedText = change.AddedLength == 0 ? string.Empty : currentText.Substring(change.Offset, change.AddedLength);
            ApplyEdit(change.Offset, change.RemovedLength, insertedText);
        }

        if (!string.Equals(source, currentText, StringComparison.Ordinal))
        {
            SetSource(currentText);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Highlights the complete current source.
    /// </summary>
    public IReadOnlyList<HighlightSpan> Highlight() => HighlightSlice(0, source.Length);

    /// <summary>
    /// Highlights only the requested character slice, allowing rendering to process the viewport instead of the whole document.
    /// </summary>
    public IReadOnlyList<HighlightSpan> HighlightSlice(int start, int end)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateRange(start, end);
        EnsureParsed();
        return queryHighlighter?.HighlightSlice(tree!.RootNode, source, start, end)
            ?? TreeSitterClassifier.HighlightSlice(tree!.RootNode, source, start, end);
    }

    /// <summary>
    /// Releases tree-sitter native resources owned by the parser, tree, language, and optional query highlighter.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        tree?.Dispose();
        queryHighlighter?.Dispose();
        parser.Dispose();
        language.Dispose();
        disposed = true;
    }

    /// <summary>
    /// Converts a text replacement into tree-sitter byte and point ranges, then reparses against the edited old tree.
    /// </summary>
    private void ApplyIncrementalEdit(string oldSource, string newSource, int start, int removedLength, int addedLength)
    {
        if (tree is null)
        {
            ReplaceTree(Parse(newSource, null));
            source = newSource;
            return;
        }

        var oldTree = tree;
        var (startPosition, oldEndPosition, newEndPosition) = GetEditPoints(
            oldSource,
            newSource.AsSpan(start, addedLength),
            start,
            removedLength);

        oldTree.Edit(new global::TreeSitter.Edit
        {
            StartIndex = start,
            OldEndIndex = start + removedLength,
            NewEndIndex = start + addedLength,
            StartPosition = startPosition,
            OldEndPosition = oldEndPosition,
            NewEndPosition = newEndPosition,
        });

        var updatedTree = Parse(newSource, oldTree);
        tree = updatedTree;
        source = newSource;
        oldTree.Dispose();
    }

    /// <summary>
    /// Applies one validated WPF text change directly against the current text snapshot without rebuilding the final source.
    /// </summary>
    private bool ApplySingleTextChange(TextChange change, string currentText)
    {
        if (change.Offset < 0 || change.RemovedLength < 0 || change.AddedLength < 0 || change.Offset > source.Length || change.Offset + change.RemovedLength > source.Length)
        {
            SetSource(currentText);
            return false;
        }

        if (change.Offset > currentText.Length || change.Offset + change.AddedLength > currentText.Length)
        {
            SetSource(currentText);
            return false;
        }

        ApplyIncrementalEdit(source, currentText, change.Offset, change.RemovedLength, change.AddedLength);
        return true;
    }

    /// <summary>
    /// Parses text, optionally using a previously edited tree as incremental context.
    /// </summary>
    private TreeSitterTree Parse(string value, TreeSitterTree? oldTree)
    {
        var parsedTree = parser.Parse(value, oldTree);
        return parsedTree ?? throw new InvalidOperationException("TreeSitter.DotNet returned no syntax tree.");
    }

    /// <summary>
    /// Swaps the current tree and disposes the previous native tree handle.
    /// </summary>
    private void ReplaceTree(TreeSitterTree newTree)
    {
        var oldTree = tree;
        tree = newTree;
        oldTree?.Dispose();
    }

    /// <summary>
    /// Lazily creates a parse tree for callers that highlight before explicitly setting source.
    /// </summary>
    private void EnsureParsed()
    {
        if (tree is null)
        {
            SetSource(source);
        }
    }

    /// <summary>
    /// Converts a UTF-16 character index to tree-sitter's row/column point for edit ranges.
    /// </summary>
    private static TreeSitterPoint GetPoint(string text, int index)
    {
        var end = Math.Min(index, text.Length);
        return AdvancePoint(new TreeSitterPoint(0, 0), text.AsSpan(0, end));
    }

    /// <summary>
    /// Calculates tree-sitter edit points with one prefix scan and short scans over the edited text only.
    /// </summary>
    private static (TreeSitterPoint Start, TreeSitterPoint OldEnd, TreeSitterPoint NewEnd) GetEditPoints(
        string oldSource,
        ReadOnlySpan<char> insertedText,
        int start,
        int removedLength)
    {
        var startPosition = GetPoint(oldSource, start);
        var oldEndPosition = AdvancePoint(startPosition, oldSource.AsSpan(start, removedLength));
        var newEndPosition = AdvancePoint(startPosition, insertedText);
        return (startPosition, oldEndPosition, newEndPosition);
    }

    /// <summary>
    /// Advances a tree-sitter point over a text span using the document's current UTF-16 index convention.
    /// </summary>
    private static TreeSitterPoint AdvancePoint(TreeSitterPoint point, ReadOnlySpan<char> text)
    {
        var row = point.Row;
        var column = point.Column;
        foreach (var value in text)
        {
            if (value == '\n')
            {
                row++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return new TreeSitterPoint(row, column);
    }

    /// <summary>
    /// Validates a highlight slice range before it is sent to tree-sitter query execution.
    /// </summary>
    private static void ValidateRange(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(end);

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }
    }
}
