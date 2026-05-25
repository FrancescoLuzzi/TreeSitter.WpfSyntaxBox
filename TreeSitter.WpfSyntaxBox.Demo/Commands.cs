using System.Windows.Input;

namespace TreeSitter.WpfSyntaxBox.Demo;

/// <summary>
/// Exposes routed commands used by the demo editor menu and key bindings.
/// </summary>
public static class Commands
{
    /// <summary>
    /// Gets the command that comments selected lines.
    /// </summary>
    public static ICommand CommentCommand { get; } = new RoutedCommand();

    /// <summary>
    /// Gets the command that uncomments selected lines.
    /// </summary>
    public static ICommand UncommentCommand { get; } = new RoutedCommand();
}
