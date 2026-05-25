using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TreeSitter.WpfSyntaxBox.Tests;

/// <summary>
/// Runs WPF rendering simulations on a shared STA dispatcher for scroll and resize tests.
/// </summary>
internal static class WpfScrollSimulation
{
    private static readonly Lazy<WpfDispatcherHost> Host = new(() => new WpfDispatcherHost());

    /// <summary>
    /// Creates a syntax-enabled editor and scrolls through representative positions in a generated C# document.
    /// </summary>
    public static void SimulateCSharpUserScroll(string source, int steps = 24)
    {
        Host.Value.Invoke(() =>
        {
            var editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Height = 480,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4),
                Text = source,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Width = 900,
            };

            SyntaxBox.SetSyntaxDriver(editor, new SyntaxLanguage { Language = "CSharp" });
            SyntaxBox.SetEnable(editor, true);

            var window = new Window
            {
                Content = editor,
                Height = 520,
                Left = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Top = -20_000,
                Width = 940,
                WindowStyle = WindowStyle.None,
            };

            try
            {
                window.Show();
                PumpLayout(window, editor);

                var lastLine = Math.Max(0, editor.LineCount - 1);
                for (var step = 0; step < steps; step++)
                {
                    var line = steps <= 1 ? lastLine : (int)Math.Round(lastLine * (step / (double)(steps - 1)));
                    editor.ScrollToLine(line);
                    editor.CaretIndex = Math.Clamp(editor.GetCharacterIndexFromLineIndex(line), 0, editor.Text.Length);
                    PumpLayout(window, editor);

                    Assert.InRange(editor.VerticalOffset, 0, Math.Max(editor.VerticalOffset, editor.ExtentHeight));
                }

                editor.ScrollToHome();
                PumpLayout(window, editor);
                editor.ScrollToEnd();
                PumpLayout(window, editor);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Invokes test code on the shared WPF dispatcher thread.
    /// </summary>
    public static void RunOnWpfThread(Action action)
    {
        Host.Value.Invoke(action);
    }

    /// <summary>
    /// Flushes window layout and pending dispatcher work.
    /// </summary>
    public static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Pumps layout and dispatcher work for a duration so deferred syntax refreshes can complete.
    /// </summary>
    public static void PumpFor(Window window, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        while (frame.Continue)
        {
            window.UpdateLayout();
            Dispatcher.PushFrame(frame);
        }

        window.UpdateLayout();
    }

    /// <summary>
    /// Flushes both window and editor layout after scroll changes.
    /// </summary>
    private static void PumpLayout(Window window, TextBox editor)
    {
        window.UpdateLayout();
        editor.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Owns the STA dispatcher thread used by WPF tests.
    /// </summary>
    private sealed class WpfDispatcherHost
    {
        private readonly ManualResetEventSlim initialized = new();
        private readonly Thread thread;
        private Dispatcher dispatcher = null!;

        /// <summary>
        /// Starts the dispatcher thread and waits until it is ready.
        /// </summary>
        public WpfDispatcherHost()
        {
            thread = new Thread(() =>
            {
                // RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                dispatcher = Dispatcher.CurrentDispatcher;
                initialized.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            initialized.Wait();
        }

        /// <summary>
        /// Invokes an action synchronously on the dispatcher thread.
        /// </summary>
        public void Invoke(Action action)
        {
            dispatcher.Invoke(action);
        }
    }
}
