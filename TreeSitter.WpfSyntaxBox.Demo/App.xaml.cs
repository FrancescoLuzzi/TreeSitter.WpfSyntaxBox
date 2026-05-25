using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox.Demo;

/// <summary>
/// Application entry point for the syntax-box demo.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Performs startup initialization before the demo window is shown.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);
    }
}
