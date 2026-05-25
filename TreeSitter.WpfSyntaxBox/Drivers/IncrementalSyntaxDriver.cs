using System.Collections.Generic;
using System.Windows.Controls;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Extends a syntax driver with range-aware matching so renderers can highlight only the visible viewport plus context.
/// </summary>
internal interface IRangeSyntaxDriver : ISyntaxDriver
{
    /// <summary>
    /// Returns formatting instructions for a character range within the provided text.
    /// </summary>
    IEnumerable<FormatInstruction> Match(DriverOperation operation, string text, int start, int end);
}

/// <summary>
/// Extends a syntax driver with source synchronization APIs for incremental parsing after text changes.
/// </summary>
internal interface IIncrementalSyntaxDriver : ISyntaxDriver
{
    /// <summary>
    /// Replaces the driver's source and rebuilds its syntax state from scratch.
    /// </summary>
    void SetSource(string text);

    /// <summary>
    /// Applies ordered WPF text changes to the driver's current source, returning whether the update stayed incremental.
    /// </summary>
    bool ApplyTextChanges(IReadOnlyList<TextChange> changes, string currentText);
}

/// <summary>
/// Marks a driver that can report whether its parse state matches a candidate source snapshot.
/// </summary>
internal interface ISynchronizedSyntaxDriver : ISyntaxDriver
{
    /// <summary>
    /// Returns whether the driver's parsed source is the supplied text.
    /// </summary>
    bool IsSynchronizedWith(string text);
}
