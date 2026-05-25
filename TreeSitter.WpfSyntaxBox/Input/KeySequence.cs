using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Input binding that accepts comma-separated key sequences through XAML type conversion.
/// </summary>
public sealed class KeySequenceBinding : InputBinding
{
    /// <summary>
    /// Gets or sets the converted key gesture or key sequence gesture.
    /// </summary>
    [TypeConverter(typeof(KeySequenceConverter))]
    public override InputGesture Gesture
    {
        get => base.Gesture;
        set => base.Gesture = value;
    }
}

/// <summary>
/// Matches a fixed sequence of key presses that all share the same modifier state.
/// </summary>
public sealed class KeySequenceGesture : KeyGesture
{
    private readonly ModifierKeys modifiers;
    private readonly IList<Key> keys;
    private int pointer;

    /// <summary>
    /// Creates a sequence gesture from required modifiers and ordered keys.
    /// </summary>
    public KeySequenceGesture(ModifierKeys modifiers, IList<Key> keys, string displayString)
        : base(Key.None, ModifierKeys.None, displayString)
    {
        this.modifiers = modifiers;
        this.keys = keys;
    }

    /// <summary>
    /// Advances through the sequence for non-repeated key events and matches once all keys are seen in order.
    /// </summary>
    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        if (inputEventArgs is not KeyEventArgs keyArgs || keyArgs.IsRepeat)
        {
            return false;
        }

        if (Keyboard.Modifiers != modifiers || keyArgs.Key != keys[pointer])
        {
            pointer = 0;
            return false;
        }

        keyArgs.Handled = true;
        pointer++;

        if (pointer < keys.Count)
        {
            return false;
        }

        pointer = 0;
        return true;
    }
}

/// <summary>
/// Converts strings such as <c>Ctrl+K,C</c> into WPF input gestures.
/// </summary>
public sealed class KeySequenceConverter : TypeConverter
{
    /// <summary>
    /// Returns whether the converter accepts the source type.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

    /// <summary>
    /// Converts a string into either a regular key gesture or a multi-key sequence gesture.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not string input)
        {
            throw new ArgumentException("Argument must be of string type", nameof(value));
        }

        var lastSplit = input.LastIndexOf('+');
        var modifiers = ModifierKeys.None;
        if (lastSplit > 1)
        {
            modifiers = (ModifierKeys)new ModifierKeysConverter().ConvertFromString(context, culture, input[..(lastSplit + 1)])!;
        }

        var keyConverter = new KeyConverter();
        var keys = input[(lastSplit + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(key => (Key)keyConverter.ConvertFromString(context, culture, key)!)
            .ToList();

        return keys.Count == 1
            ? new KeyGesture(keys[0], modifiers, input)
            : new KeySequenceGesture(modifiers, keys, input);
    }
}
