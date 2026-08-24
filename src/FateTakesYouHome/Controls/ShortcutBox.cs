using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.Controls;

/// <summary>
/// A box that records a keyboard shortcut: focus it, press the combination, done.
/// </summary>
/// <remarks>
/// Built on <see cref="TextBox"/> for the focus visuals and accessibility plumbing, with all
/// typing intercepted — the text is always the formatted gesture, never keystrokes. Escape,
/// Backspace and Delete clear the shortcut, which is also the documented convention everywhere
/// else on Windows.
/// </remarks>
public sealed class ShortcutBox : TextBox
{
    public static readonly DependencyProperty GestureTextProperty = DependencyProperty.Register(
        nameof(GestureText),
        typeof(string),
        typeof(ShortcutBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnGestureTextChanged));

    public ShortcutBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        Text = Placeholder;
    }

    private static string Placeholder => "Click, then press keys";

    /// <summary>The recorded gesture as text ("Ctrl+Alt+H"), or null when unset.</summary>
    public string? GestureText
    {
        get => (string?)GetValue(GestureTextProperty);
        set => SetValue(GestureTextProperty, value);
    }

    private static void OnGestureTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (ShortcutBox)d;
        box.Text = e.NewValue is string { Length: > 0 } text ? text : Placeholder;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab keeps its meaning so a keyboard user is never trapped inside the recorder.
        if (key == Key.Tab)
        {
            e.Handled = false;
            return;
        }

        if (key is Key.Escape or Key.Back or Key.Delete)
        {
            GestureText = null;
            return;
        }

        // A modifier alone is a chord in progress, not a shortcut.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        var candidate = new HotkeyGesture(Keyboard.Modifiers, key);

        // Round-trip through the parser so the recorder refuses exactly what the registrar would.
        if (HotkeyGesture.Parse(candidate.ToString()) is { } accepted)
        {
            GestureText = accepted.ToString();
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);

        if (string.IsNullOrEmpty(GestureText))
        {
            Text = "Press a combination…";
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);

        if (string.IsNullOrEmpty(GestureText))
        {
            Text = Placeholder;
        }
    }
}
