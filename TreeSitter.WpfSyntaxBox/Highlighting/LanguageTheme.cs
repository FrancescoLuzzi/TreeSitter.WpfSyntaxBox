using System.Collections.Generic;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Maps normalized highlight kinds to WPF brushes.
/// </summary>
public class LanguageTheme
{
    private readonly Dictionary<HighlightKind, Brush> brushes = new();

    /// <summary>
    /// Gets the default syntax-highlighting theme.
    /// </summary>
    public static LanguageTheme Default { get; } = new DotNetFluentLanguageTheme();

    /// <summary>
    /// Gets a theme modeled after Visual Studio/.NET light colors.
    /// </summary>
    public static LanguageTheme DotNetFluent { get; } = new DotNetFluentLanguageTheme();

    /// <summary>
    /// Gets a dark theme based on Tokyo Night colors.
    /// </summary>
    public static LanguageTheme TokyoNightDark { get; } = new TokyoNightDarkLanguageTheme();

    /// <summary>
    /// Gets or sets the brush for a highlight kind, removing the mapping when set to <c>null</c>.
    /// </summary>
    public Brush? this[HighlightKind kind]
    {
        get => brushes.TryGetValue(kind, out var brush) ? brush : null;
        set
        {
            if (value is null)
            {
                brushes.Remove(kind);
            }
            else
            {
                brushes[kind] = value;
            }
        }
    }

    /// <summary>
    /// Gets the brush assigned to a highlight kind, if any.
    /// </summary>
    public Brush? GetBrush(HighlightKind kind) => this[kind];

    /// <summary>
    /// Creates and freezes a brush from a color string for safe reuse across formatting operations.
    /// </summary>
    protected static Brush Brush(string color)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
