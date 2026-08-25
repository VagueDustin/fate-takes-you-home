// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FateTakesYouHome.Controls;

/// <summary>
/// A text element that supports letter spacing.
/// </summary>
/// <remarks>
/// <para>
/// WPF has no equivalent of CSS <c>letter-spacing</c>. <see cref="System.Windows.Controls.TextBlock"/>
/// cannot do it at all, and the usual workarounds — a <c>Run</c> per character, or padding
/// injected into the string — break text selection, accessibility and justification.
/// </para>
/// <para>
/// The brand specifies <c>--tracking-wordmark: 0.14em</c> and <c>--tracking-label: 0.34em</c>, and
/// widely letterspaced small caps are most of what makes the house style recognisable. So this
/// draws the text itself, advancing by the measured width of each character plus the tracking.
/// </para>
/// <para>
/// Intended for short strings: wordmarks, section labels, buttons. It formats each character
/// separately, which is the wrong trade for a paragraph but immaterial for a dozen glyphs, and it
/// falls back to a single formatted run when tracking is zero.
/// </para>
/// </remarks>
public sealed class TrackedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutInputChanged));

    /// <summary>Letter spacing in ems, matching the CSS unit the brand tokens use.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking),
        typeof(double),
        typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutInputChanged));

    /// <summary>Upper-cases the text before drawing, for small-caps section labels.</summary>
    public static readonly DependencyProperty UpperCaseProperty = DependencyProperty.Register(
        nameof(UpperCase),
        typeof(bool),
        typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutInputChanged));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(TrackedTextBlock),
            new FrameworkPropertyMetadata(
                SystemColors.ControlTextBrush,
                FrameworkPropertyMetadataOptions.AffectsRender
                | FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutInputChanged));

    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(
            typeof(TrackedTextBlock),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender
                | FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutInputChanged));

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(
            typeof(TrackedTextBlock),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontSize,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender
                | FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutInputChanged));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(
            typeof(TrackedTextBlock),
            new FrameworkPropertyMetadata(
                FontWeights.Normal,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender
                | FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutInputChanged));

    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(
            typeof(TrackedTextBlock),
            new FrameworkPropertyMetadata(
                FontStyles.Normal,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender
                | FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutInputChanged));

    /// <summary>Fill for the glyphs themselves. Overrides <see cref="Foreground"/> when set.</summary>
    /// <remarks>
    /// Exists so a gradient can be poured through display type at the ceremonial tier, which is
    /// what the brand's <c>--gradient-gold-text</c> is for.
    /// </remarks>
    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush),
        typeof(Brush),
        typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(
            null, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutInputChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public bool UpperCase
    {
        get => (bool)GetValue(UpperCaseProperty);
        set => SetValue(UpperCaseProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? GlyphBrush
    {
        get => (Brush?)GetValue(GlyphBrushProperty);
        set => SetValue(GlyphBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// The laid-out glyphs, built once and reused by both measure and render.
    /// </summary>
    /// <remarks>
    /// Formatting is not cheap, and WPF calls measure and render separately — and repeatedly, on
    /// any invalidation. Building the run twice per layout pass showed up as real cost once these
    /// labels appeared on every group header in a long list.
    /// </remarks>
    private sealed record Layout(FormattedText[] Glyphs, double[] Offsets, Size Size);

    private Layout? _layout;

    private static void OnLayoutInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TrackedTextBlock)d).InvalidateLayoutCache();

    /// <summary>
    /// Moving to a display with a different scale factor changes the glyph metrics, which the
    /// cached run has already baked in.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateLayoutCache();
    }

    /// <summary>Discards the cached layout. Called whenever an input to it changes.</summary>
    private void InvalidateLayoutCache()
    {
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Layout? layout = EnsureLayout();

        if (layout is null)
        {
            return new Size(0, LineHeight());
        }

        return new Size(Math.Min(layout.Size.Width, availableSize.Width), layout.Size.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        Layout? layout = EnsureLayout();

        if (layout is null)
        {
            return;
        }

        for (int i = 0; i < layout.Glyphs.Length; i++)
        {
            drawingContext.DrawText(layout.Glyphs[i], new Point(layout.Offsets[i], 0));
        }
    }

    /// <summary>
    /// Builds the glyph run if it is not already cached.
    /// </summary>
    /// <remarks>
    /// Measure and render share the result, so the two can never disagree about advance widths —
    /// a disagreement that shows up as text clipped by exactly one character.
    /// </remarks>
    private Layout? EnsureLayout()
    {
        if (_layout is not null)
        {
            return _layout;
        }

        string text = EffectiveText();

        if (text.Length == 0)
        {
            return null;
        }

        double trackingPx = Tracking * FontSize;
        Brush brush = GlyphBrush ?? Foreground;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);

        // Zero tracking is the common case; one formatted run is faster and better shaped, because
        // kerning pairs survive.
        if (Math.Abs(trackingPx) < 0.01)
        {
            FormattedText whole = Format(text, typeface, pixelsPerDip, brush);

            _layout = new Layout(
                [whole],
                [0],
                new Size(whole.WidthIncludingTrailingWhitespace, whole.Height));

            return _layout;
        }

        var glyphs = new List<FormattedText>(text.Length);
        var offsets = new List<double>(text.Length);

        double x = 0;
        double height = 0;

        // By rune, not by char, so an astral-plane character advances as one glyph rather than
        // being split into two broken halves.
        foreach (Rune rune in text.EnumerateRunes())
        {
            FormattedText formatted = Format(rune.ToString(), typeface, pixelsPerDip, brush);

            glyphs.Add(formatted);
            offsets.Add(x);

            x += formatted.WidthIncludingTrailingWhitespace + trackingPx;
            height = Math.Max(height, formatted.Height);
        }

        // Trailing tracking is spacing after the last glyph, which would leave the text looking
        // off-centre inside anything that centres it.
        x -= trackingPx;

        _layout = new Layout(
            [.. glyphs],
            [.. offsets],
            new Size(Math.Max(0, x), height > 0 ? height : LineHeight()));

        return _layout;
    }

    private FormattedText Format(string text, Typeface typeface, double pixelsPerDip, Brush brush) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush,
            pixelsPerDip)
        {
            TextAlignment = TextAlignment.Left,
        };

    private string EffectiveText()
    {
        string text = Text ?? string.Empty;
        return UpperCase ? text.ToUpper(CultureInfo.CurrentUICulture) : text;
    }

    private double LineHeight() => FontSize * 1.4;
}
