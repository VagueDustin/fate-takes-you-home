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

/// <summary>Turns a <see cref="Models.TrayAction"/> into the words a person would use for it.</summary>
/// <remarks>
/// Raw enum names in a dropdown — "OpenMainWindow", "RunDefaultAction" — read as a debug build.
/// The names are UI copy and belong in exactly one place, which is here.
/// </remarks>
public sealed class TrayActionToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Models.TrayAction.None => "Do nothing",
            Models.TrayAction.ToggleFlyout => "Open the panel",
            Models.TrayAction.OpenMainWindow => "Open the full window",
            Models.TrayAction.RunDefaultAction => "Run the default pin",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Words for the entity browser's grouping choices.</summary>
public sealed class GroupingToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Models.EntityGrouping.Area => "By room",
            Models.EntityGrouping.Domain => "By kind",
            Models.EntityGrouping.Floor => "By floor",
            Models.EntityGrouping.None => "No grouping",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Resolves a theme list entry to one of its colours, for the swatch strip in the theme picker.
/// </summary>
/// <remarks>
/// Themes are patches, so the entry's own document usually declares only a few colours — the
/// swatch has to come from the fully resolved theme, inheritance and all. Resolution is a few
/// dictionary merges; doing it per swatch keeps the picker stateless.
/// </remarks>
public sealed class ThemeSwatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Theming.Loading.ThemeEntry entry
            || App.Current?.Themes.Repository is not { } repository)
        {
            return System.Windows.Media.Brushes.Transparent;
        }

        Theming.Model.Theme theme = repository.Resolve(entry.Id);

        System.Windows.Media.Color color = parameter?.ToString() switch
        {
            "surface" => theme.Colors.SurfaceBase,
            "raised" => theme.Colors.SurfaceRaised,
            "overlay" => theme.Colors.SurfaceOverlay,
            "accent" => theme.Colors.AccentDefault,
            "text" => theme.Colors.TextPrimary,
            "muted" => theme.Colors.TextMuted,
            "faint" => theme.Colors.TextFaint,
            "border" => theme.Colors.BorderDefault,
            _ => theme.Colors.AccentDefault,
        };

        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns an available width into a sensible column count for the pinned grid.</summary>
/// <remarks>
/// The thresholds are content-driven: a tile needs roughly 320px to breathe, so one column below
/// 660, two on a normal window, three once the window is generously wide. This is what lets the
/// same layout hold together from a 1280×720 laptop panel to an ultrawide.
/// </remarks>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width
            ? width switch
            {
                < 660 => 1,
                < 1120 => 2,
                _ => 3,
            }
            : 2;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when a width is below the threshold given as the parameter.</summary>
public sealed class IsNarrowerThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width
        && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
        && width < threshold;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
