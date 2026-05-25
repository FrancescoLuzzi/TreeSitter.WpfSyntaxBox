using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Provides attached properties that turn a standard <see cref="TextBox"/> into a syntax-highlighted editor.
/// </summary>
public sealed class SyntaxBox : DependencyObject
{
    private static readonly Uri TemplateResourceUri = new($"/{typeof(SyntaxBox).Assembly.GetName().Name};component/Rendering/Resources.xaml", UriKind.RelativeOrAbsolute);
    private static readonly DependencyProperty IsUpdatingForegroundProperty = DependencyProperty.RegisterAttached(
        "IsUpdatingForeground",
        typeof(bool),
        typeof(SyntaxBox));

    /// <summary>
    /// Forces the renderer attached to a text box to refresh its syntax-driver state and cached instructions.
    /// </summary>
    public static void InvalidateDriver(TextBox textBox)
    {
        if (textBox.Template?.FindName("PART_SyntaxRenderer", textBox) is SyntaxRenderer renderer)
        {
            renderer.InvalidateDriver();
        }
    }

    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnEnableChanged));

    /// <summary>
    /// Sets whether syntax highlighting is enabled for a text box.
    /// </summary>
    public static void SetEnable(TextBox target, bool value) => target.SetValue(EnableProperty, value);

    /// <summary>
    /// Gets whether syntax highlighting is enabled for a text box.
    /// </summary>
    public static bool GetEnable(TextBox target) => (bool)target.GetValue(EnableProperty);

    public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.RegisterAttached(
        "ShowLineNumbers",
        typeof(bool),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Sets whether the syntax box displays virtualized line numbers.
    /// </summary>
    public static void SetShowLineNumbers(TextBox target, bool value) => target.SetValue(ShowLineNumbersProperty, value);

    /// <summary>
    /// Gets whether the syntax box displays line numbers.
    /// </summary>
    public static bool GetShowLineNumbers(TextBox target) => (bool)target.GetValue(ShowLineNumbersProperty);

    public static readonly DependencyProperty LineNumbersBackgroundProperty = DependencyProperty.RegisterAttached(
        "LineNumbersBackground",
        typeof(Brush),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(Brushes.WhiteSmoke, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Sets the line-number gutter background brush.
    /// </summary>
    public static void SetLineNumbersBackground(TextBox target, Brush value) => target.SetValue(LineNumbersBackgroundProperty, value);

    /// <summary>
    /// Gets the line-number gutter background brush.
    /// </summary>
    public static Brush GetLineNumbersBackground(TextBox target) => (Brush)target.GetValue(LineNumbersBackgroundProperty);

    public static readonly DependencyProperty LineNumbersForegroundProperty = DependencyProperty.RegisterAttached(
        "LineNumbersForeground",
        typeof(Brush),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(Brushes.SlateGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Sets the line-number text brush.
    /// </summary>
    public static void SetLineNumbersForeground(TextBox target, Brush value) => target.SetValue(LineNumbersForegroundProperty, value);

    /// <summary>
    /// Gets the line-number text brush.
    /// </summary>
    public static Brush GetLineNumbersForeground(TextBox target) => (Brush)target.GetValue(LineNumbersForegroundProperty);

    private static readonly DependencyPropertyKey OriginalForegroundPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "OriginalForeground",
        typeof(Brush),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty OriginalForegroundProperty = OriginalForegroundPropertyKey.DependencyProperty;

    /// <summary>
    /// Stores the original foreground so the transparent editor text can be restored when highlighting is disabled.
    /// </summary>
    internal static void SetOriginalForeground(TextBox target, Brush value) => target.SetValue(OriginalForegroundPropertyKey, value);

    /// <summary>
    /// Gets the foreground brush captured before syntax highlighting hid the native text.
    /// </summary>
    public static Brush GetOriginalForeground(TextBox target) => (Brush)target.GetValue(OriginalForegroundProperty);

    private static readonly DependencyPropertyKey OriginalTemplatePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "OriginalTemplate",
        typeof(ControlTemplate),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty OriginalTemplateProperty = OriginalTemplatePropertyKey.DependencyProperty;

    /// <summary>
    /// Stores the template that was active before the syntax overlay template was installed.
    /// </summary>
    internal static void SetOriginalTemplate(TextBox target, ControlTemplate? value) => target.SetValue(OriginalTemplatePropertyKey, value);

    /// <summary>
    /// Gets the original text-box template captured before enabling highlighting.
    /// </summary>
    public static ControlTemplate? GetOriginalTemplate(TextBox target) => (ControlTemplate?)target.GetValue(OriginalTemplateProperty);

    public static readonly DependencyProperty SyntaxDriverProperty = DependencyProperty.RegisterAttached(
        "SyntaxDriver",
        typeof(ISyntaxDriver),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSyntaxDriverChanged));

    /// <summary>
    /// Sets the primary syntax driver used by the renderer.
    /// </summary>
    public static void SetSyntaxDriver(TextBox target, ISyntaxDriver value) => target.SetValue(SyntaxDriverProperty, value);

    /// <summary>
    /// Gets the primary syntax driver used by the renderer.
    /// </summary>
    public static ISyntaxDriver? GetSyntaxDriver(TextBox target) => (ISyntaxDriver?)target.GetValue(SyntaxDriverProperty);

    private static readonly DependencyProperty SyntaxDriversProperty = DependencyProperty.RegisterAttached(
        "SyntaxDrivers_",
        typeof(SyntaxDriverCollection),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(null, OnSyntaxDriverChanged));

    /// <summary>
    /// Gets the additional syntax drivers associated with a text box, creating the collection on demand.
    /// </summary>
    public static SyntaxDriverCollection GetSyntaxDrivers(TextBox target)
    {
        if (target.GetValue(SyntaxDriversProperty) is SyntaxDriverCollection collection)
        {
            return collection;
        }

        collection = [];
        target.SetValue(SyntaxDriversProperty, collection);
        return collection;
    }

    public static readonly DependencyProperty ExpandTabsProperty = DependencyProperty.RegisterAttached(
        "ExpandTabs",
        typeof(bool),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(true));

    /// <summary>
    /// Sets whether Tab input inserts spaces rather than a tab character.
    /// </summary>
    public static void SetExpandTabs(TextBox target, bool value) => target.SetValue(ExpandTabsProperty, value);

    /// <summary>
    /// Gets whether Tab input inserts spaces.
    /// </summary>
    public static bool GetExpandTabs(TextBox target) => (bool)target.GetValue(ExpandTabsProperty);

    public static readonly DependencyProperty AutoIndentProperty = DependencyProperty.RegisterAttached(
        "AutoIndent",
        typeof(bool),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(true));

    /// <summary>
    /// Sets whether Enter copies the current line indentation to the new line.
    /// </summary>
    public static void SetAutoIndent(TextBox target, bool value) => target.SetValue(AutoIndentProperty, value);

    /// <summary>
    /// Gets whether Enter copies the current line indentation.
    /// </summary>
    public static bool GetAutoIndent(TextBox target) => (bool)target.GetValue(AutoIndentProperty);

    public static readonly DependencyProperty IndentCountProperty = DependencyProperty.RegisterAttached(
        "IndentCount",
        typeof(int),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(4));

    /// <summary>
    /// Sets the number of spaces inserted for indentation.
    /// </summary>
    public static void SetIndentCount(TextBox target, int value) => target.SetValue(IndentCountProperty, value);

    /// <summary>
    /// Gets the number of spaces inserted for indentation.
    /// </summary>
    public static int GetIndentCount(TextBox target) => (int)target.GetValue(IndentCountProperty);

    public static readonly DependencyProperty HighlightContextLinesProperty = DependencyProperty.RegisterAttached(
        "HighlightContextLines",
        typeof(int),
        typeof(SyntaxBox),
        new FrameworkPropertyMetadata(200, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Sets the number of lines outside the viewport included for syntax context.
    /// </summary>
    public static void SetHighlightContextLines(TextBox target, int value) => target.SetValue(HighlightContextLinesProperty, value);

    /// <summary>
    /// Gets the number of lines outside the viewport included for syntax context.
    /// </summary>
    public static int GetHighlightContextLines(TextBox target) => (int)target.GetValue(HighlightContextLinesProperty);

    /// <summary>
    /// Installs or removes the overlay template and foreground management when highlighting is toggled.
    /// </summary>
    private static void OnEnableChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox target)
        {
            return;
        }

        if (e.NewValue is true)
        {
            if (target.Foreground != Brushes.Transparent)
            {
                SetOriginalForeground(target, CloneBrush(target.Foreground));
            }

            SetOriginalTemplate(target, target.Template);
            target.Template = LoadTemplate("SyntaxTextBoxTemplate");
            HookForegroundManagement(target);
            EnsureTextBoxTextIsHidden(target);
            TextBlock.SetLineStackingStrategy(target, LineStackingStrategy.MaxHeight);
        }
        else
        {
            UnhookForegroundManagement(target);
            RestoreOriginalTemplate(target);
            target.Foreground = GetOriginalForeground(target);
        }
    }

    /// <summary>
    /// Restores the original text-box template or clears the local template override when none was set.
    /// </summary>
    private static void RestoreOriginalTemplate(TextBox target)
    {
        var originalTemplate = GetOriginalTemplate(target);
        if (originalTemplate is null)
        {
            target.ClearValue(Control.TemplateProperty);
            return;
        }

        target.Template = originalTemplate;
    }

    /// <summary>
    /// Loads a control template from the packaged syntax-box resource dictionary.
    /// </summary>
    private static ControlTemplate LoadTemplate(string key)
    {
        var dictionary = new ResourceDictionary
        {
            Source = TemplateResourceUri,
        };

        return (ControlTemplate)dictionary[key];
    }

    /// <summary>
    /// Invalidates the renderer when attached syntax-driver properties change.
    /// </summary>
    private static void OnSyntaxDriverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            InvalidateDriver(textBox);
        }
    }

    /// <summary>
    /// Hooks foreground changes so user-provided colors are preserved while native text stays transparent.
    /// </summary>
    private static void HookForegroundManagement(TextBox target)
    {
        target.Loaded -= OnTargetLoaded;
        target.Loaded += OnTargetLoaded;

        var descriptor = DependencyPropertyDescriptor.FromProperty(Control.ForegroundProperty, typeof(TextBox));
        descriptor?.RemoveValueChanged(target, OnTargetForegroundChanged);
        descriptor?.AddValueChanged(target, OnTargetForegroundChanged);
    }

    /// <summary>
    /// Removes foreground-management hooks when highlighting is disabled.
    /// </summary>
    private static void UnhookForegroundManagement(TextBox target)
    {
        target.Loaded -= OnTargetLoaded;

        var descriptor = DependencyPropertyDescriptor.FromProperty(Control.ForegroundProperty, typeof(TextBox));
        descriptor?.RemoveValueChanged(target, OnTargetForegroundChanged);
    }

    /// <summary>
    /// Ensures the native text remains hidden and syntax state is current after the target loads.
    /// </summary>
    private static void OnTargetLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox target && GetEnable(target))
        {
            EnsureTextBoxTextIsHidden(target);
            InvalidateDriver(target);
        }
    }

    /// <summary>
    /// Captures external foreground changes and re-hides native text after WPF finishes applying them.
    /// </summary>
    private static void OnTargetForegroundChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBox target || !GetEnable(target) || IsUpdatingForeground(target))
        {
            return;
        }

        if (target.Foreground != Brushes.Transparent)
        {
            SetOriginalForeground(target, CloneBrush(target.Foreground));
            target.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GetEnable(target))
                {
                    EnsureTextBoxTextIsHidden(target);
                    InvalidateDriver(target);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Makes the native text transparent so the overlay is the only visible text layer.
    /// </summary>
    private static void EnsureTextBoxTextIsHidden(TextBox target)
    {
        if (target.Foreground == Brushes.Transparent)
        {
            return;
        }

        SetIsUpdatingForeground(target, true);
        try
        {
            target.Foreground = Brushes.Transparent;
        }
        finally
        {
            SetIsUpdatingForeground(target, false);
        }
    }

    /// <summary>
    /// Returns whether the control is currently changing foreground internally.
    /// </summary>
    private static bool IsUpdatingForeground(TextBox target) => (bool)target.GetValue(IsUpdatingForegroundProperty);

    /// <summary>
    /// Marks an internal foreground change to avoid recursively treating it as user input.
    /// </summary>
    private static void SetIsUpdatingForeground(TextBox target, bool value) => target.SetValue(IsUpdatingForegroundProperty, value);

    /// <summary>
    /// Clones and freezes mutable brushes before storing them in attached state.
    /// </summary>
    private static Brush CloneBrush(Brush brush)
    {
        if (brush.IsFrozen)
        {
            return brush;
        }

        var clone = brush.CloneCurrentValue();
        if (clone.CanFreeze)
        {
            clone.Freeze();
        }

        return clone;
    }
}
