using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Draws a syntax-highlighted overlay for a target <see cref="TextBox"/> while keeping the native editor responsible for input, selection, caret, and scrolling.
/// </summary>
public sealed class SyntaxRenderer : FrameworkElement
{
    private const int LineNumberMargin = 10;
    private const int HorizontalPadding = 2;
    private const char IndentChar = ' ';
    private ScrollViewer? scrollViewer;
    private Canvas? lineNumbers;
    private int lineNumberDigits;
    private double lineNumberWidth;
    private readonly Action invalidateVisualAction;
    private bool invalidateQueued;
    private int textVersion;
    private int driverVersion;
    private int cachedInstructionsVersion = -1;
    private int cachedInstructionsDriverVersion = -1;
    private int cachedInstructionsStart;
    private int cachedInstructionsEnd;
    private IReadOnlyList<FormatInstruction> cachedGlobalInstructions = Array.Empty<FormatInstruction>();
    private int cachedLineIndexVersion = -1;
    private TextLineIndex? cachedLineIndex;
    private readonly DispatcherTimer syntaxRefreshTimer;
    private bool deferSyntaxRefresh;
    private PendingSyntaxRefresh? pendingSyntaxRefresh;
    private string lastKnownText = string.Empty;
    private readonly List<RenderedHyperlink> renderedHyperlinks = [];
    private bool hyperlinkCursorActive;

    /// <summary>
    /// Initializes renderer invalidation and the debounce timer used to keep parsing off the synchronous text-change path.
    /// </summary>
    public SyntaxRenderer()
    {
        invalidateVisualAction = FlushPendingInvalidation;
        syntaxRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        syntaxRefreshTimer.Tick += SyntaxRefreshTimerTick;
    }

    /// <summary>
    /// Calls WPF's internal line-height calculation so overlay text aligns with the target control's real layout metrics.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetLineHeight")]
    private static extern double GetLineHeight(TextBox textBox);

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target),
        typeof(TextBox),
        typeof(SyntaxRenderer),
        new PropertyMetadata(null, OnTargetChanged));

    /// <summary>
    /// Gets or sets the text box whose text and viewport are rendered by this overlay.
    /// </summary>
    public TextBox? Target
    {
        get => (TextBox?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static readonly DependencyProperty DefaultForegroundProperty = DependencyProperty.Register(
        nameof(DefaultForeground),
        typeof(Brush),
        typeof(SyntaxRenderer),
        new PropertyMetadata(Brushes.Black));

    /// <summary>
    /// Gets or sets the fallback brush used for text ranges that have no syntax instruction.
    /// </summary>
    public Brush DefaultForeground
    {
        get => (Brush)GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    public static readonly DependencyProperty LineNumbersForegroundProperty = DependencyProperty.Register(
        nameof(LineNumbersForeground),
        typeof(Brush),
        typeof(SyntaxRenderer),
        new PropertyMetadata(Brushes.SlateGray));

    /// <summary>
    /// Gets or sets the brush used when drawing the virtualized line-number gutter.
    /// </summary>
    public Brush LineNumbersForeground
    {
        get => (Brush)GetValue(LineNumbersForegroundProperty);
        set => SetValue(LineNumbersForegroundProperty, value);
    }

    /// <summary>
    /// Opens an absolute hyperlink string if the URI scheme is allowed.
    /// </summary>
    public static bool OpenLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && OpenLink(uri);

    /// <summary>
    /// Opens an allowed URI through the shell and returns whether launch succeeded.
    /// </summary>
    public static bool OpenLink(Uri uri)
    {
        if (!IsOpenableLink(uri))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a routed WPF hyperlink for safe schemes only, preventing rendered source text from launching arbitrary protocols.
    /// </summary>
    internal static bool TryCreateHyperlink(string link, out Hyperlink? hyperlink)
    {
        hyperlink = null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !IsOpenableLink(uri))
        {
            return false;
        }

        hyperlink = new Hyperlink
        {
            NavigateUri = uri,
        };
        hyperlink.RequestNavigate += HyperlinkRequestNavigate;
        return true;
    }

    /// <summary>
    /// Renders only the currently visible line range plus gutter, making scrolling proportional to viewport size rather than document size.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Target is null)
        {
            return;
        }

        AttachTemplateParts();

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(Target.FontFamily, Target.FontStyle, Target.FontWeight, Target.FontStretch);
        var lineHeight = GetLineHeight(Target);
        if (!IsPositiveFinite(lineHeight) || !IsNonNegativeFinite(ActualWidth) || !IsNonNegativeFinite(ActualHeight))
        {
            return;
        }

        var scrollBarSize = GetScrollBarSizes();
        var text = Target.Text ?? string.Empty;
        var verticalOffset = double.IsFinite(Target.VerticalOffset) ? Target.VerticalOffset : 0;
        var (firstVisible, lastVisible) = GetVisibleLineRange(lineHeight, verticalOffset);

        var lineIndex = GetLineIndex(text);
        var visibleLines = lineIndex.GetLineSpans(text, firstVisible, lastVisible);
        if (visibleLines.Count == 0)
        {
            return;
        }

        var totalLines = lineIndex.Count;
        var requiredLineNumberWidth = CalculateLineNumberWidth(totalLines, typeface, dpi);
        if (lineNumbers is not null && Math.Abs(requiredLineNumberWidth - lineNumbers.Width) > 0.1)
        {
            lineNumbers.Width = requiredLineNumberWidth;
        }

        var textOriginY = GetTextOriginY(visibleLines[0].StartIndex, (firstVisible * lineHeight) - verticalOffset);
        DrawLineNumbers(drawingContext, visibleLines, requiredLineNumberWidth, typeface, dpi, lineHeight, textOriginY, scrollBarSize);
        DrawSyntaxText(drawingContext, visibleLines, typeface, dpi, lineHeight, textOriginY, scrollBarSize);
    }

    /// <summary>
    /// Draws the visible text slice and applies syntax instructions in local coordinates for the active viewport.
    /// </summary>
    private void DrawSyntaxText(
        DrawingContext drawingContext,
        IReadOnlyList<TextLineSpan> visibleLines,
        Typeface typeface,
        double dpi,
        double lineHeight,
        double textOriginY,
        global::System.Windows.Size scrollBarSize)
    {
        if (Target is null)
        {
            return;
        }

        renderedHyperlinks.Clear();
        var text = Target.Text ?? string.Empty;
        var visibleText = CreateVisibleText(text, visibleLines);
        var formattedText = CreateFormattedText(visibleText, typeface, DefaultForeground, dpi, lineHeight);
        var availableWidth = ActualWidth - scrollBarSize.Width;
        var availableHeight = ActualHeight - scrollBarSize.Height;
        if (!IsPositiveFinite(availableWidth) || !IsPositiveFinite(availableHeight))
        {
            return;
        }

        var textOriginX = GetTextOriginX(visibleLines[0].StartIndex);
        if (Target.TextWrapping != TextWrapping.NoWrap)
        {
            var maxTextWidth = availableWidth - Math.Max(0, textOriginX);
            if (!IsPositiveFinite(maxTextWidth))
            {
                return;
            }

            formattedText.MaxTextWidth = maxTextWidth;
        }

        ApplyInstructions(formattedText, text, visibleLines, visibleText.Length);

        drawingContext.PushClip(new RectangleGeometry(new global::System.Windows.Rect(0, 0, availableWidth, availableHeight)));
        drawingContext.DrawText(
            formattedText,
            new global::System.Windows.Point(textOriginX, textOriginY));
        drawingContext.Pop();
    }

    /// <summary>
    /// Uses WPF's realized visible-line range when available, falling back to scroll-offset math before layout is ready.
    /// </summary>
    internal (int First, int Last) GetVisibleLineRange(double lineHeight, double verticalOffset)
    {
        if (Target is null)
        {
            return (0, 0);
        }

        var first = Target.GetFirstVisibleLineIndex();
        var last = Target.GetLastVisibleLineIndex();
        if (first >= 0 && last >= first)
        {
            return (first, last + 1);
        }

        first = Math.Max(0, (int)(verticalOffset / lineHeight));
        last = Math.Max(first, (int)((verticalOffset + ActualHeight) / lineHeight) + 1);
        return (first, last);
    }

    /// <summary>
    /// Resolves the overlay X coordinate for a document character while preserving horizontal scrolling alignment.
    /// </summary>
    private double GetTextOriginX(int characterIndex)
    {
        if (Target is null)
        {
            return HorizontalPadding;
        }

        var text = Target.Text ?? string.Empty;
        var index = Math.Clamp(characterIndex, 0, text.Length);
        var rect = Target.GetRectFromCharacterIndex(index);
        if (!rect.IsEmpty && double.IsFinite(rect.X))
        {
            return Target.TranslatePoint(new global::System.Windows.Point(rect.X, 0), this).X;
        }

        return HorizontalPadding - Target.HorizontalOffset;
    }

    /// <summary>
    /// Resolves the overlay Y coordinate from WPF's actual character rectangle to avoid drift from scroll-offset rounding.
    /// </summary>
    private double GetTextOriginY(int characterIndex, double fallback)
    {
        if (Target is null)
        {
            return fallback;
        }

        var text = Target.Text ?? string.Empty;
        var index = Math.Clamp(characterIndex, 0, text.Length);
        var rect = Target.GetRectFromCharacterIndex(index);
        if (!rect.IsEmpty && double.IsFinite(rect.Y))
        {
            return Target.TranslatePoint(new global::System.Windows.Point(0, rect.Y), this).Y;
        }

        return fallback;
    }

    /// <summary>
    /// Clips absolute highlight instructions to the visible slice before applying WPF formatting spans.
    /// </summary>
    private void ApplyInstructions(FormattedText formattedText, string text, IReadOnlyList<TextLineSpan> visibleLines, int visibleLength)
    {
        if (Target is null)
        {
            return;
        }

        var visibleStart = visibleLines[0].StartIndex;
        var visibleEnd = visibleLines[^1].EndIndex;
        renderedHyperlinks.Clear();
        foreach (var instruction in GetInstructions(text, visibleLines))
        {
            var instructionStart = instruction.FromChar;
            var instructionEnd = instruction.FromChar + instruction.Length;
            var start = Math.Max(instructionStart, visibleStart);
            var end = Math.Min(instructionEnd, visibleEnd);
            if (end <= start)
            {
                continue;
            }

            var localStart = Math.Clamp(start - visibleStart, 0, visibleLength);
            var localLength = Math.Clamp(end - start, 0, visibleLength - localStart);
            if (localLength == 0)
            {
                continue;
            }

            if (instruction.Foreground is not null)
            {
                formattedText.SetForegroundBrush(instruction.Foreground, localStart, localLength);
            }

            if (instruction.TextDecorations is not null)
            {
                formattedText.SetTextDecorations(instruction.TextDecorations, localStart, localLength);
            }

            if (instruction.Link is not null && TryCreateHyperlink(instruction.Link, out var hyperlink) && hyperlink is not null)
            {
                renderedHyperlinks.Add(new RenderedHyperlink(start, end, hyperlink));
                if (instruction.TextDecorations is null)
                {
                    formattedText.SetTextDecorations(TextDecorations.Underline, localStart, localLength);
                }
            }
        }
    }

    /// <summary>
    /// Combines global syntax-driver results with line-local rules for the active visible range.
    /// </summary>
    private IReadOnlyList<FormatInstruction> GetInstructions(string text, IReadOnlyList<TextLineSpan> visibleLines)
    {
        var driver = GetSyntaxDriver();
        if (driver is null)
        {
            return Array.Empty<FormatInstruction>();
        }

        var abilities = driver.Abilities;
        var (highlightStart, highlightEnd) = GetHighlightRange(text, visibleLines, GetLineIndex(text));
        var globalInstructions = GetGlobalInstructions(driver, abilities, text, highlightStart, highlightEnd);
        if ((abilities & DriverOperation.Line) == 0)
        {
            return globalInstructions;
        }

        var instructions = new List<FormatInstruction>(globalInstructions.Count);
        instructions.AddRange(globalInstructions);
        foreach (var line in visibleLines)
        {
            foreach (var instruction in driver.Match(DriverOperation.Line, line.GetText(text)))
            {
                instructions.Add(new FormatInstruction(
                    instruction.RuleId,
                    line.StartIndex + instruction.FromChar,
                    instruction.Length,
                    instruction.Foreground,
                    instruction.TextDecorations,
                    instruction.Link));
            }
        }

        instructions.Sort((left, right) => left.RuleId.CompareTo(right.RuleId));
        return instructions;
    }

    /// <summary>
    /// Retrieves cached full-text or block instructions for the viewport-expanded highlight range without reparsing on scroll or resize.
    /// </summary>
    private IReadOnlyList<FormatInstruction> GetGlobalInstructions(ISyntaxDriver driver, DriverOperation abilities, string text, int highlightStart, int highlightEnd)
    {
        if (cachedInstructionsVersion == textVersion
            && cachedInstructionsDriverVersion == driverVersion
            && cachedInstructionsStart == highlightStart
            && cachedInstructionsEnd == highlightEnd)
        {
            return cachedGlobalInstructions;
        }

        if (deferSyntaxRefresh)
        {
            return cachedInstructionsVersion == textVersion && cachedInstructionsDriverVersion == driverVersion
                ? cachedGlobalInstructions
                : Array.Empty<FormatInstruction>();
        }

        var instructions = new List<FormatInstruction>();
        if ((abilities & DriverOperation.FullText) != 0)
        {
            // Resize and scroll only change the viewport range. They re-highlight this slice
            // against the existing parse tree; parsing is queued exclusively from TextChanged.
            AddGlobalInstructions(instructions, driver, DriverOperation.FullText, text, highlightStart, highlightEnd);
        }

        if ((abilities & DriverOperation.Block) != 0)
        {
            AddGlobalInstructions(instructions, driver, DriverOperation.Block, text, highlightStart, highlightEnd);
        }

        instructions.Sort((left, right) => left.RuleId.CompareTo(right.RuleId));
        cachedInstructionsVersion = textVersion;
        cachedInstructionsDriverVersion = driverVersion;
        cachedInstructionsStart = highlightStart;
        cachedInstructionsEnd = highlightEnd;
        cachedGlobalInstructions = instructions;
        return cachedGlobalInstructions;
    }

    /// <summary>
    /// Adds global instructions only from drivers synchronized with the current text, and uses range-aware matching when available.
    /// </summary>
    private static void AddGlobalInstructions(List<FormatInstruction> instructions, ISyntaxDriver driver, DriverOperation operation, string text, int highlightStart, int highlightEnd)
    {
        if (driver is ISynchronizedSyntaxDriver synchronizedDriver && !synchronizedDriver.IsSynchronizedWith(text))
        {
            return;
        }

        instructions.AddRange(driver is IRangeSyntaxDriver rangeDriver
            ? rangeDriver.Match(operation, text, highlightStart, highlightEnd)
            : driver.Match(operation, text));
    }

    /// <summary>
    /// Expands the visible lines by configured context so multi-line grammar constructs are highlighted accurately near viewport edges.
    /// </summary>
    private (int Start, int End) GetHighlightRange(string text, IReadOnlyList<TextLineSpan> visibleLines, TextLineIndex lineIndex)
    {
        if (Target is null)
        {
            return (visibleLines[0].StartIndex, visibleLines[^1].EndIndex);
        }

        var contextLines = Math.Max(0, SyntaxBox.GetHighlightContextLines(Target));
        var firstLine = Math.Max(0, visibleLines[0].LineNumber - contextLines);
        var lastLine = visibleLines[^1].LineNumber + contextLines;
        return lineIndex.GetRangeBounds(firstLine, lastLine, text.Length);
    }

    /// <summary>
    /// Resolves the single attached driver and collection into the effective driver used for rendering.
    /// </summary>
    private ISyntaxDriver? GetSyntaxDriver()
    {
        if (Target is null)
        {
            return null;
        }

        var drivers = SyntaxBox.GetSyntaxDrivers(Target);
        var single = SyntaxBox.GetSyntaxDriver(Target);
        if (single is not null && !drivers.Contains(single))
        {
            return drivers.Count == 0
                ? single
                : new AggregateSyntaxDriver([single, .. drivers]);
        }

        return drivers.Count > 0 ? new AggregateSyntaxDriver(drivers) : single;
    }

    /// <summary>
    /// Draws line numbers for the same virtualized line range as the text overlay.
    /// </summary>
    private void DrawLineNumbers(
        DrawingContext drawingContext,
        IReadOnlyList<TextLineSpan> visibleLines,
        double width,
        Typeface typeface,
        double dpi,
        double lineHeight,
        double textOriginY,
        global::System.Windows.Size scrollBarSize)
    {
        if (Target is null || width <= 0)
        {
            return;
        }

        var availableHeight = ActualHeight - scrollBarSize.Height;
        if (!IsPositiveFinite(availableHeight))
        {
            return;
        }

        var clip = new global::System.Windows.Rect(
            new global::System.Windows.Point(-(width + Target.Padding.Left), 0),
            new global::System.Windows.Size(width + Target.Padding.Left, availableHeight));

        drawingContext.PushClip(new RectangleGeometry(clip));
        var numbers = CreateLineNumberText(visibleLines);
        var formattedText = CreateFormattedText(numbers, typeface, LineNumbersForeground, dpi, lineHeight);
        formattedText.TextAlignment = TextAlignment.Right;
        drawingContext.DrawText(
            formattedText,
            new global::System.Windows.Point(-(LineNumberMargin + Target.Padding.Left), textOriginY));
        drawingContext.Pop();
    }

    /// <summary>
    /// Gets the cached line-start index for the current text version, rebuilding it only after text changes.
    /// </summary>
    private TextLineIndex GetLineIndex(string text)
    {
        if (cachedLineIndexVersion == textVersion && cachedLineIndex is not null)
        {
            return cachedLineIndex;
        }

        cachedLineIndex = TextLineIndex.Build(text);
        cachedLineIndexVersion = textVersion;
        return cachedLineIndex;
    }

    /// <summary>
    /// Calculates and caches gutter width by digit count so the layout only changes when the number of digits changes.
    /// </summary>
    private double CalculateLineNumberWidth(int lineCount, Typeface typeface, double dpi)
    {
        if (Target is null || !SyntaxBox.GetShowLineNumbers(Target))
        {
            return 0;
        }

        var digits = (int)Math.Floor(Math.Log10(Math.Max(lineCount, 1)) + 1);
        if (digits == lineNumberDigits)
        {
            return lineNumberWidth;
        }

        lineNumberDigits = digits;
        var text = new string('0', digits);
        var formattedText = CreateFormattedText(text, typeface, Brushes.Black, dpi, GetLineHeight(Target));
        lineNumberWidth = formattedText.Width + (LineNumberMargin * 2);
        return lineNumberWidth;
    }

    /// <summary>
    /// Creates WPF formatted text with the exact line height and formatting mode used by the target text box.
    /// </summary>
    private FormattedText CreateFormattedText(string text, Typeface typeface, Brush brush, double dpi, double lineHeight) => new(
        text,
        System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        typeface,
        Target?.FontSize ?? 12,
        brush,
        numberSubstitution: null,
        Target is null ? TextOptions.GetTextFormattingMode(this) : TextOptions.GetTextFormattingMode(Target),
        dpi)
    {
        LineHeight = lineHeight,
        Trimming = TextTrimming.None,
    };

    /// <summary>
    /// Lazily attaches to template parts required for scrollbar sizes, scroll invalidation, and line-number placement.
    /// </summary>
    private void AttachTemplateParts()
    {
        if (Target is null)
        {
            return;
        }

        if (scrollViewer is null)
        {
            scrollViewer = Target.Template.FindName("PART_ContentHost", Target) as ScrollViewer;
            if (scrollViewer is not null)
            {
                scrollViewer.ScrollChanged += (_, _) => InvalidateVisual();
            }
        }

        lineNumbers ??= Target.Template.FindName("PART_LineNumbers", Target) as Canvas;
    }

    /// <summary>
    /// Flushes any queued syntax source update and invalidates cached instructions after driver configuration changes.
    /// </summary>
    internal void InvalidateDriver()
    {
        syntaxRefreshTimer.Stop();
        if (pendingSyntaxRefresh is not null)
        {
            ApplyPendingSyntaxSource();
        }

        deferSyntaxRefresh = false;
        driverVersion++;
        ResetInstructionCache();
        InvalidateVisual();
    }

    /// <summary>
    /// Coalesces render invalidations so rapid text edits queue one UI-thread redraw instead of one redraw per event.
    /// </summary>
    private void RequestInvalidateVisual()
    {
        if (invalidateQueued || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        invalidateQueued = true;
        Dispatcher.BeginInvoke(invalidateVisualAction, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Executes a previously queued render invalidation on the dispatcher.
    /// </summary>
    private void FlushPendingInvalidation()
    {
        invalidateQueued = false;
        InvalidateVisual();
    }

    /// <summary>
    /// Clears cached syntax instructions whenever text, driver, or highlight range assumptions become invalid.
    /// </summary>
    private void ResetInstructionCache()
    {
        cachedInstructionsVersion = -1;
        cachedInstructionsDriverVersion = -1;
        cachedInstructionsStart = 0;
        cachedInstructionsEnd = 0;
        cachedGlobalInstructions = Array.Empty<FormatInstruction>();
    }

    /// <summary>
    /// Applies deferred syntax source changes after the debounce window and redraws using the updated parse tree.
    /// </summary>
    private void SyntaxRefreshTimerTick(object? sender, EventArgs e)
    {
        syntaxRefreshTimer.Stop();
        ApplyPendingSyntaxSource();
        deferSyntaxRefresh = false;
        ResetInstructionCache();
        InvalidateVisual();
    }

    /// <summary>
    /// Concatenates only visible line text so <see cref="FormattedText"/> allocation size follows the viewport.
    /// </summary>
    private static string CreateVisibleText(string text, IReadOnlyList<TextLineSpan> visibleLines)
    {
        if (visibleLines.Count == 0)
        {
            return string.Empty;
        }

        var start = visibleLines[0].StartIndex;
        var end = visibleLines[^1].EndIndex;
        return text.Substring(start, end - start);
    }

    /// <summary>
    /// Builds the virtualized line-number text block corresponding to the currently visible lines.
    /// </summary>
    private static string CreateLineNumberText(IReadOnlyList<TextLineSpan> visibleLines)
    {
        var builder = new StringBuilder(visibleLines.Count * 4);
        for (var i = 0; i < visibleLines.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(visibleLines[i].LineNumber + 1);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns whether a layout measurement is finite and positive.
    /// </summary>
    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    /// <summary>
    /// Returns whether a layout measurement is finite and non-negative.
    /// </summary>
    private static bool IsNonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;

    /// <summary>
    /// Allows only URI schemes that are expected to be safe for editor hyperlinks.
    /// </summary>
    private static bool IsOpenableLink(Uri uri) => uri.Scheme is "http" or "https" or "mailto" or "ftp";

    /// <summary>
    /// Returns whether the hyperlink activation modifier is currently pressed.
    /// </summary>
    private static bool IsControlPressed() => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    /// <summary>
    /// Handles routed hyperlink navigation from rendered syntax instructions.
    /// </summary>
    private static void HyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenLink(e.Uri);
        e.Handled = true;
    }

    /// <summary>
    /// Returns visible scrollbar dimensions so the overlay clips before scrollbar chrome.
    /// </summary>
    private global::System.Windows.Size GetScrollBarSizes()
    {
        return new global::System.Windows.Size(
            scrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0,
            scrollViewer?.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? SystemParameters.HorizontalScrollBarHeight : 0);
    }

    /// <summary>
    /// Rebinds event handlers and resets renderer state when the overlay is attached to a different text box.
    /// </summary>
    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var renderer = (SyntaxRenderer)sender;
        if (e.OldValue is TextBox oldTextBox)
        {
            oldTextBox.TextChanged -= renderer.TargetTextChanged;
            oldTextBox.PreviewKeyDown -= renderer.TargetPreviewKeyDown;
            oldTextBox.PreviewMouseMove -= renderer.TargetPreviewMouseMove;
            oldTextBox.PreviewMouseLeftButtonUp -= renderer.TargetPreviewMouseLeftButtonUp;
            oldTextBox.MouseLeave -= renderer.TargetMouseLeave;
        }

        if (e.NewValue is TextBox newTextBox)
        {
            newTextBox.TextChanged += renderer.TargetTextChanged;
            newTextBox.PreviewKeyDown += renderer.TargetPreviewKeyDown;
            newTextBox.PreviewMouseMove += renderer.TargetPreviewMouseMove;
            newTextBox.PreviewMouseLeftButtonUp += renderer.TargetPreviewMouseLeftButtonUp;
            newTextBox.MouseLeave += renderer.TargetMouseLeave;
            renderer.lastKnownText = newTextBox.Text ?? string.Empty;
            renderer.SynchronizeDriversIfNeeded(renderer.lastKnownText);
        }
        else
        {
            renderer.lastKnownText = string.Empty;
        }

        renderer.scrollViewer = null;
        renderer.lineNumbers = null;
        renderer.syntaxRefreshTimer.Stop();
        renderer.deferSyntaxRefresh = false;
        renderer.pendingSyntaxRefresh = null;
        renderer.renderedHyperlinks.Clear();
        renderer.SetHyperlinkCursor(false);
        renderer.textVersion++;
        renderer.driverVersion++;
        renderer.cachedLineIndexVersion = -1;
        renderer.cachedLineIndex = null;
        renderer.ResetInstructionCache();
    }

    /// <summary>
    /// Ensures incremental drivers have a parse tree for the current text before rendering begins.
    /// </summary>
    private void SynchronizeDriversIfNeeded(string text)
    {
        ForEachIncrementalDriver(driver =>
        {
            if (driver is ISynchronizedSyntaxDriver synchronizedDriver && synchronizedDriver.IsSynchronizedWith(text))
            {
                return;
            }

            driver.SetSource(text);
        });
    }

    /// <summary>
    /// Captures text edits, adjusts cached spans immediately, and schedules incremental parsing outside the input event.
    /// </summary>
    private void TargetTextChanged(object sender, TextChangedEventArgs e)
    {
        if (Target is null || sender != Target)
        {
            return;
        }

        var text = Target.Text ?? string.Empty;
        if (string.Equals(text, lastKnownText, StringComparison.Ordinal))
        {
            return;
        }

        lastKnownText = text;
        QueueSyntaxRefresh(e, text);
        AdjustCachedInstructionsForTextChange(e);
        AdjustLineIndexForTextChange(e, text);
        textVersion++;
        deferSyntaxRefresh = true;
        syntaxRefreshTimer.Stop();
        syntaxRefreshTimer.Start();
        RequestInvalidateVisual();
    }

    /// <summary>
    /// Updates the cursor when Ctrl-hover is over a rendered hyperlink span.
    /// </summary>
    private void TargetPreviewMouseMove(object sender, MouseEventArgs e)
    {
        SetHyperlinkCursor(IsControlPressed() && FindRenderedHyperlink(e) is not null);
    }

    /// <summary>
    /// Activates a rendered hyperlink when the configured modifier is held during mouse release.
    /// </summary>
    private void TargetPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsControlPressed())
        {
            return;
        }

        var hyperlink = FindRenderedHyperlink(e)?.Hyperlink;
        if (hyperlink?.NavigateUri is null)
        {
            return;
        }

        hyperlink.RaiseEvent(new RequestNavigateEventArgs(hyperlink.NavigateUri, target: null)
        {
            RoutedEvent = Hyperlink.RequestNavigateEvent,
        });
        e.Handled = true;
    }

    /// <summary>
    /// Restores the default cursor when the pointer leaves the target editor.
    /// </summary>
    private void TargetMouseLeave(object sender, MouseEventArgs e)
    {
        SetHyperlinkCursor(false);
    }

    /// <summary>
    /// Maps a mouse position to the rendered hyperlink span under the text caret position.
    /// </summary>
    private RenderedHyperlink? FindRenderedHyperlink(MouseEventArgs e)
    {
        if (Target is null || renderedHyperlinks.Count == 0)
        {
            return null;
        }

        var characterIndex = Target.GetCharacterIndexFromPoint(e.GetPosition(Target), snapToText: false);
        if (characterIndex < 0)
        {
            return null;
        }

        foreach (var hyperlink in renderedHyperlinks)
        {
            if (characterIndex >= hyperlink.Start && characterIndex < hyperlink.End)
            {
                return hyperlink;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets the global mouse override only when the hyperlink cursor state actually changes.
    /// </summary>
    private void SetHyperlinkCursor(bool enabled)
    {
        if (hyperlinkCursorActive == enabled)
        {
            return;
        }

        hyperlinkCursorActive = enabled;
        Mouse.OverrideCursor = enabled ? Cursors.Hand : null;
    }

    /// <summary>
    /// Translates cached highlight spans across a single edit so the UI can repaint immediately while parsing is deferred.
    /// </summary>
    private void AdjustCachedInstructionsForTextChange(TextChangedEventArgs e)
    {
        if (cachedGlobalInstructions.Count == 0 || cachedInstructionsVersion != textVersion)
        {
            return;
        }

        TextChange? singleChange = null;
        var changeCount = 0;
        foreach (var change in e.Changes)
        {
            singleChange = change;
            changeCount++;
            if (changeCount > 1)
            {
                break;
            }
        }

        if (changeCount != 1 || singleChange is null)
        {
            ResetInstructionCache();
            return;
        }

        var editStart = singleChange.Offset;
        var removedLength = singleChange.RemovedLength;
        var addedLength = singleChange.AddedLength;
        var oldEnd = editStart + removedLength;
        var delta = addedLength - removedLength;
        var adjusted = new List<FormatInstruction>(cachedGlobalInstructions.Count);

        foreach (var instruction in cachedGlobalInstructions)
        {
            var instructionStart = instruction.FromChar;
            var instructionEnd = instruction.FromChar + instruction.Length;
            if (instructionEnd <= editStart)
            {
                adjusted.Add(instruction);
                continue;
            }

            if (instructionStart >= oldEnd)
            {
                adjusted.Add(new FormatInstruction(
                    instruction.RuleId,
                    instructionStart + delta,
                    instruction.Length,
                    instruction.Foreground,
                    instruction.TextDecorations,
                    instruction.Link));
                continue;
            }

            var newStart = Math.Min(instructionStart, editStart);
            var newEnd = Math.Max(newStart, instructionEnd + delta);
            if (newEnd > newStart)
            {
                adjusted.Add(new FormatInstruction(
                    instruction.RuleId,
                    newStart,
                    newEnd - newStart,
                    instruction.Foreground,
                    instruction.TextDecorations,
                    instruction.Link));
            }
        }

        cachedInstructionsVersion = textVersion + 1;
        cachedInstructionsStart = AdjustBoundary(cachedInstructionsStart, editStart, oldEnd, addedLength, delta);
        cachedInstructionsEnd = AdjustBoundary(cachedInstructionsEnd, editStart, oldEnd, addedLength, delta);
        cachedGlobalInstructions = adjusted;
    }

    /// <summary>
    /// Updates the cached line-start index for a single text edit so typing does not force a full line-index rebuild.
    /// </summary>
    private void AdjustLineIndexForTextChange(TextChangedEventArgs e, string text)
    {
        if (cachedLineIndexVersion != textVersion || cachedLineIndex is null)
        {
            return;
        }

        TextChange? singleChange = null;
        var changeCount = 0;
        foreach (var change in e.Changes)
        {
            singleChange = change;
            changeCount++;
            if (changeCount > 1)
            {
                break;
            }
        }

        if (changeCount != 1 || singleChange is null)
        {
            cachedLineIndex = null;
            cachedLineIndexVersion = -1;
            return;
        }

        cachedLineIndex = cachedLineIndex.ApplyChange(
            text,
            singleChange.Offset,
            singleChange.RemovedLength,
            singleChange.AddedLength);
        cachedLineIndexVersion = textVersion + 1;
    }

    /// <summary>
    /// Moves a cached range boundary across an edit while clamping boundaries that fell inside the replaced text.
    /// </summary>
    private static int AdjustBoundary(int position, int editStart, int oldEnd, int addedLength, int delta)
    {
        if (position <= editStart)
        {
            return position;
        }

        return position >= oldEnd ? position + delta : editStart + addedLength;
    }

    /// <summary>
    /// Queues the exact WPF text changes for incremental parsing, falling back to a full refresh if edits are coalesced.
    /// </summary>
    private void QueueSyntaxRefresh(TextChangedEventArgs e, string text)
    {
        if (Target is null)
        {
            return;
        }

        if (pendingSyntaxRefresh is not null)
        {
            pendingSyntaxRefresh = PendingSyntaxRefresh.Full(text);
            return;
        }

        var changes = new List<TextChange>();
        foreach (var change in e.Changes)
        {
            changes.Add(change);
        }

        pendingSyntaxRefresh = changes.Count == 0
            ? PendingSyntaxRefresh.Full(text)
            : PendingSyntaxRefresh.Incremental(text, changes);
    }

    /// <summary>
    /// Applies the pending source update to every incremental driver using tree-sitter edit deltas when possible.
    /// </summary>
    private void ApplyPendingSyntaxSource()
    {
        var refresh = pendingSyntaxRefresh;
        pendingSyntaxRefresh = null;
        if (refresh is null)
        {
            return;
        }

        if (refresh.Changes is { Count: > 0 } changes)
        {
            ForEachIncrementalDriver(driver => driver.ApplyTextChanges(changes, refresh.Text));
            return;
        }

        ForEachIncrementalDriver(driver => driver.SetSource(refresh.Text));
    }

    /// <summary>
    /// Stores a deferred syntax refresh as either a full source replacement or an ordered set of incremental text changes.
    /// </summary>
    private sealed record PendingSyntaxRefresh(string Text, IReadOnlyList<TextChange>? Changes)
    {
        /// <summary>
        /// Creates a refresh that reparses the complete source.
        /// </summary>
        public static PendingSyntaxRefresh Full(string text) => new(text, null);

        /// <summary>
        /// Creates a refresh that attempts to edit the existing parse tree incrementally.
        /// </summary>
        public static PendingSyntaxRefresh Incremental(string text, IReadOnlyList<TextChange> changes) => new(text, changes);
    }

    /// <summary>
    /// Executes an action for the single attached incremental driver and any collection entries without double-invoking the same instance.
    /// </summary>
    private void ForEachIncrementalDriver(Action<IIncrementalSyntaxDriver> action)
    {
        if (Target is null)
        {
            return;
        }

        var single = SyntaxBox.GetSyntaxDriver(Target);
        if (single is IIncrementalSyntaxDriver incrementalSingle)
        {
            action(incrementalSingle);
        }

        foreach (var driver in SyntaxBox.GetSyntaxDrivers(Target))
        {
            if (!ReferenceEquals(driver, single) && driver is IIncrementalSyntaxDriver incrementalDriver)
            {
                action(incrementalDriver);
            }
        }
    }

    /// <summary>
    /// Handles editor conveniences that must run before the native text box inserts text.
    /// </summary>
    private void TargetPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Target is null || sender != Target)
        {
            return;
        }

        if (e.Key == Key.Tab && SyntaxBox.GetExpandTabs(Target))
        {
            InsertIndent(e.KeyboardDevice.IsKeyDown(Key.LeftShift) || e.KeyboardDevice.IsKeyDown(Key.RightShift));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SyntaxBox.GetAutoIndent(Target))
        {
            var line = Target.Text.GetLineAtPosition(Target.CaretIndex);
            if (line is not null)
            {
                var prefixLength = GetIndentPrefixLength(line.Value.Text);
                var prefix = prefixLength == 0 ? string.Empty : line.Value.Text[..prefixLength];
                System.Windows.Documents.EditingCommands.EnterLineBreak.Execute(null, Target);
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, Target, prefix));
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Inserts or removes the configured indentation width at the current caret position.
    /// </summary>
    private void InsertIndent(bool decrease)
    {
        if (Target is null)
        {
            return;
        }

        var count = Math.Max(1, SyntaxBox.GetIndentCount(Target));
        if (decrease)
        {
            var start = Math.Max(0, Target.CaretIndex - count);
            var length = Target.CaretIndex - start;
            if (length > 0 && IsIndent(Target.Text.AsSpan(start, length)))
            {
                Target.Select(start, length);
                Target.SelectedText = string.Empty;
                Target.CaretIndex = start;
            }

            return;
        }

        var indent = new string(IndentChar, count);
        Target.SelectedText = indent;
        Target.CaretIndex += indent.Length;
    }

    /// <summary>
    /// Returns the leading whitespace width copied during auto-indent.
    /// </summary>
    private static int GetIndentPrefixLength(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value is '\r' or '\n' || !char.IsWhiteSpace(value))
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Returns whether a span consists entirely of indentation characters removable by Shift+Tab.
    /// </summary>
    private static bool IsIndent(ReadOnlySpan<char> text)
    {
        foreach (var value in text)
        {
            if (value != IndentChar)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tracks an absolute text range whose rendered span can be activated as a hyperlink.
    /// </summary>
    private sealed record RenderedHyperlink(int Start, int End, Hyperlink Hyperlink);
}
