using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TreeSitter.WpfSyntaxBox;
using TreeSitter.WpfSyntaxBox.Demo;

namespace TreeSitter.WpfSyntaxBox.Tests;

/// <summary>
/// Covers demo-window interactions that stress WPF resizing, scrolling, and syntax-driver synchronization.
/// </summary>
public sealed class DemoUiTests
{
    /// <summary>
    /// Verifies immediate resizing after startup does not crash and leaves the syntax driver synchronized.
    /// </summary>
    [Fact]
    public void DemoMainWindow_CSharpLanguage_DoesNotCrashWhenResizeStartsImmediately()
    {
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var window = new MainWindow
            {
                Left = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Top = -20_000,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };

            try
            {
                window.Show();

                for (var step = 0; step < 40; step++)
                {
                    window.Width = 920 + (step * 7);
                    window.Height = 560 + (step * 3);
                    WpfScrollSimulation.PumpLayout(window);
                }

                WpfScrollSimulation.PumpFor(window, TimeSpan.FromMilliseconds(250));

                for (var step = 0; step < 40; step++)
                {
                    window.Width = 1_220 - (step * 6);
                    window.Height = 680 - (step * 4);
                    WpfScrollSimulation.PumpLayout(window);
                }

                var editor = FindDescendant<TextBox>(window, "Editor");
                Assert.NotNull(editor);
                Assert.Contains("public sealed class", editor.Text, StringComparison.Ordinal);

                var language = Assert.IsType<SyntaxLanguage>(SyntaxBox.GetSyntaxDriver(editor));
                WpfScrollSimulation.PumpFor(window, TimeSpan.FromMilliseconds(250));
                Assert.True(language.FullParseCount > 0);
                Assert.True(((ISynchronizedSyntaxDriver)language).IsSynchronizedWith(editor.Text));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Verifies resize, maximize, and scroll operations do not trigger extra parses after initial synchronization.
    /// </summary>
    [Fact]
    public void DemoMainWindow_CSharpLanguage_HandlesResizeMaximizeAndScroll()
    {
        WpfScrollSimulation.RunOnWpfThread(() =>
        {
            var window = new MainWindow
            {
                Left = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Top = -20_000,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };

            try
            {
                window.Show();
                WpfScrollSimulation.PumpLayout(window);

                var editor = FindDescendant<TextBox>(window, "Editor");
                Assert.NotNull(editor);
                Assert.Contains("public sealed class", editor.Text, StringComparison.Ordinal);
                Assert.True(editor.LineCount > 100);

                var language = Assert.IsType<SyntaxLanguage>(SyntaxBox.GetSyntaxDriver(editor));
                WpfScrollSimulation.PumpFor(window, TimeSpan.FromMilliseconds(250));
                Assert.True(language.FullParseCount > 0);
                Assert.True(((ISynchronizedSyntaxDriver)language).IsSynchronizedWith(editor.Text));

                var fullParseCount = language.FullParseCount;
                var incrementalParseCount = language.IncrementalParseCount;

                ResizeAndRender(window, editor, 920, 560);
                ResizeAndRender(window, editor, 1280, 720);
                ResizeAndRender(window, editor, 640, 480);
                ResizeAndRender(window, editor, 1600, 900);
                ResizeAndRender(window, editor, 420, 320);
                ResizeAndRender(window, editor, 1024, 768);

                window.WindowState = WindowState.Maximized;
                WpfScrollSimulation.PumpLayout(window);
                editor.ScrollToEnd();
                WpfScrollSimulation.PumpLayout(window);

                window.WindowState = WindowState.Normal;
                WpfScrollSimulation.PumpLayout(window);
                editor.ScrollToHome();
                WpfScrollSimulation.PumpLayout(window);

                Assert.Equal(fullParseCount, language.FullParseCount);
                Assert.Equal(incrementalParseCount, language.IncrementalParseCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Applies a window size, then forces scroll and layout cycles through the syntax renderer.
    /// </summary>
    private static void ResizeAndRender(Window window, TextBox editor, double width, double height)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        WpfScrollSimulation.PumpLayout(window);

        editor.ScrollToEnd();
        WpfScrollSimulation.PumpLayout(window);
        editor.ScrollToHome();
        WpfScrollSimulation.PumpLayout(window);
    }

    /// <summary>
    /// Searches the WPF visual tree for a named descendant of a requested element type.
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            var descendant = FindDescendant<T>(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
