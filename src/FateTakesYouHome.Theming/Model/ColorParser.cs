// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Media;

namespace FateTakesYouHome.Theming.Model;

/// <summary>
/// Parses the colour notations a theme author is likely to type.
/// </summary>
/// <remarks>
/// Hex is read in <em>CSS order</em> — <c>#RRGGBBAA</c>, alpha last — rather than WPF's
/// <c>#AARRGGBB</c>. Theme authors copy values out of the brand repo's CSS, and silently reading
/// <c>#D4AF37</c>-adjacent values in the wrong channel order is the kind of bug nobody finds for
/// a month.
/// </remarks>
public static class ColorParser
{
    /// <summary>Parses a colour, throwing a descriptive exception when it cannot.</summary>
    public static Color Parse(string value)
    {
        if (!TryParse(value, out Color color, out string? error))
        {
            throw new FormatException(error);
        }

        return color;
    }

    public static bool TryParse(string? value, out Color color) =>
        TryParse(value, out color, out _);

    public static bool TryParse(
        string? value,
        out Color color,
        [NotNullWhen(false)] out string? error)
    {
        color = Colors.Transparent;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Colour value is empty.";
            return false;
        }

        string text = value.Trim();

        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Colors.Transparent;
            return true;
        }

        if (text.StartsWith('#'))
        {
            return TryParseHex(text, out color, out error);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgbFunction(text, out color, out error);
        }

        // Fall back to the ~140 named colours WPF knows.
        try
        {
            object? converted = ColorConverter.ConvertFromString(text);
            if (converted is Color named)
            {
                color = named;
                return true;
            }
        }
        catch (FormatException)
        {
            // Handled below.
        }

        error = $"'{value}' is not a colour. Use #RRGGBB, #RRGGBBAA, rgb(...), rgba(...) or a colour name.";
        return false;
    }

    private static bool TryParseHex(
        string text, out Color color, [NotNullWhen(false)] out string? error)
    {
        color = Colors.Transparent;
        error = null;

        ReadOnlySpan<char> digits = text.AsSpan(1);

        foreach (char c in digits)
        {
            if (!Uri.IsHexDigit(c))
            {
                error = $"'{text}' contains '{c}', which is not a hex digit.";
                return false;
            }
        }

        byte r, g, b, a = 255;

        switch (digits.Length)
        {
            case 3: // #RGB
                r = Expand(digits[0]);
                g = Expand(digits[1]);
                b = Expand(digits[2]);
                break;

            case 4: // #RGBA
                r = Expand(digits[0]);
                g = Expand(digits[1]);
                b = Expand(digits[2]);
                a = Expand(digits[3]);
                break;

            case 6: // #RRGGBB
                r = Byte(digits[..2]);
                g = Byte(digits[2..4]);
                b = Byte(digits[4..6]);
                break;

            case 8: // #RRGGBBAA — CSS order, alpha last.
                r = Byte(digits[..2]);
                g = Byte(digits[2..4]);
                b = Byte(digits[4..6]);
                a = Byte(digits[6..8]);
                break;

            default:
                error = $"'{text}' has {digits.Length} hex digits. Expected 3, 4, 6 or 8.";
                return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;

        static byte Expand(char c)
        {
            byte v = (byte)Convert.ToInt32(c.ToString(), 16);
            return (byte)((v << 4) | v);
        }

        static byte Byte(ReadOnlySpan<char> pair) =>
            byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static bool TryParseRgbFunction(
        string text, out Color color, [NotNullWhen(false)] out string? error)
    {
        color = Colors.Transparent;
        error = null;

        int open = text.IndexOf('(');
        int close = text.LastIndexOf(')');

        if (open < 0 || close < open)
        {
            error = $"'{text}' is missing its parentheses.";
            return false;
        }

        // Accept both the legacy comma form and the modern space form, with an optional slash alpha.
        string body = text[(open + 1)..close].Replace('/', ' ').Replace(',', ' ');
        string[] parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is < 3 or > 4)
        {
            error = $"'{text}' has {parts.Length} components. Expected 3 or 4.";
            return false;
        }

        Span<byte> channels = stackalloc byte[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryReadChannel(parts[i], out channels[i]))
            {
                error = $"'{parts[i]}' in '{text}' is not a 0–255 value or a percentage.";
                return false;
            }
        }

        byte alpha = 255;
        if (parts.Length == 4)
        {
            if (!TryReadAlpha(parts[3], out alpha))
            {
                error = $"'{parts[3]}' in '{text}' is not an alpha between 0 and 1.";
                return false;
            }
        }

        color = Color.FromArgb(alpha, channels[0], channels[1], channels[2]);
        return true;
    }

    private static bool TryReadChannel(string part, out byte value)
    {
        value = 0;

        if (part.EndsWith('%'))
        {
            if (!double.TryParse(
                    part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                return false;
            }

            value = (byte)Math.Clamp(Math.Round(pct * 255 / 100), 0, 255);
            return true;
        }

        if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
        {
            return false;
        }

        value = (byte)Math.Clamp(Math.Round(raw), 0, 255);
        return true;
    }

    private static bool TryReadAlpha(string part, out byte value)
    {
        value = 255;

        if (part.EndsWith('%'))
        {
            if (!double.TryParse(
                    part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                return false;
            }

            value = (byte)Math.Clamp(Math.Round(pct * 255 / 100), 0, 255);
            return true;
        }

        if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
        {
            return false;
        }

        value = (byte)Math.Clamp(Math.Round(raw * 255), 0, 255);
        return true;
    }

    /// <summary>Writes a colour back out in CSS hex order, omitting alpha when it is opaque.</summary>
    public static string ToCss(Color color) =>
        color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    /// <summary>Flattens a translucent colour onto an opaque one.</summary>
    public static Color Composite(Color over, Color under)
    {
        double alpha = over.A / 255d;

        return Color.FromRgb(
            (byte)Math.Round((over.R * alpha) + (under.R * (1 - alpha))),
            (byte)Math.Round((over.G * alpha) + (under.G * (1 - alpha))),
            (byte)Math.Round((over.B * alpha) + (under.B * (1 - alpha))));
    }

    /// <summary>Returns the same colour at a different opacity.</summary>
    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B);

    /// <summary>
    /// WCAG 2.1 relative luminance. Used by the validator's contrast check.
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte v)
        {
            double s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R))
             + (0.7152 * Channel(color.G))
             + (0.0722 * Channel(color.B));
    }

    /// <summary>
    /// WCAG 2.1 contrast ratio between two opaque colours, from 1:1 to 21:1.
    /// </summary>
    /// <remarks>
    /// Translucent inputs are flattened onto <paramref name="background"/> first, because a ratio
    /// computed against an alpha colour is meaningless.
    /// </remarks>
    public static double ContrastRatio(Color foreground, Color background)
    {
        Color bg = background.A == 255 ? background : Composite(background, Colors.Black);
        Color fg = foreground.A == 255 ? foreground : Composite(foreground, bg);

        double l1 = RelativeLuminance(fg);
        double l2 = RelativeLuminance(bg);

        (double hi, double lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }
}
