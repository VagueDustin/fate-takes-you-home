using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FateTakesYouHome.Converters;

/// <summary>Shows an element when a bool is false. The counterpart WPF forgot to ship.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed or Visibility.Hidden;
}

/// <summary>Inverts a bool. Used for "enabled when not busy" bindings.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Collapses an element when its text is null, empty or whitespace.
/// </summary>
/// <remarks>
/// Whitespace counts as empty on purpose: a message that is a single space would otherwise
/// reserve a line of layout for nothing.
/// </remarks>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Collapsed,
            string s => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible,
            _ => Visibility.Visible,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a collection has items.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to invert: visible only when the count is zero.</summary>
    public bool WhenEmpty { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int n => n,
            System.Collections.ICollection collection => collection.Count,
            _ => 0,
        };

        bool hasItems = count > 0;
        return hasItems != WhenEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Compares a value to the converter parameter and returns true when they match.
/// </summary>
/// <remarks>
/// Used to bind a navigation selection or a radio group to a single enum property, which WPF has
/// no built-in way to express.
/// </remarks>
public sealed class EqualityToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return value is null && parameter is null;
        }

        // Compared as strings so an enum binds against a plain XAML literal.
        return string.Equals(
            value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null
            ? Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true)
            : Binding.DoNothing;
}

/// <summary>Formats a number as a percentage with no decimals.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d
            ? d.ToString("0", culture) + "%"
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Multiplies a double by the converter parameter.
/// </summary>
/// <remarks>
/// Lets a template derive one measurement from another — a half-height row, an indent that scales
/// with the tile height — without hard-coding a second value that would drift from the theme.
/// </remarks>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double input)
        {
            return value ?? DependencyProperty.UnsetValue;
        }

        double factor = parameter is null
            ? 1
            : double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double f)
                ? f
                : 1;

        return input * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows an element only when a value equals the converter parameter.
/// </summary>
/// <remarks>
/// Used for page switching. Keeping all pages in the tree and toggling visibility — rather than
/// swapping a ContentControl's content — preserves scroll position and half-finished input when
/// somebody flicks between pages, which matters most on the settings page.
/// </remarks>
public sealed class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a diagnostic severity into the matching status brush key.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value?.ToString()?.ToLowerInvariant() switch
        {
            "error" => Theming.Rendering.ThemeKeys.BrushStatusDanger,
            "warning" => Theming.Rendering.ThemeKeys.BrushStatusWarning,
            _ => Theming.Rendering.ThemeKeys.BrushStatusInfo,
        };

        return System.Windows.Application.Current?.TryFindResource(key)
               ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
