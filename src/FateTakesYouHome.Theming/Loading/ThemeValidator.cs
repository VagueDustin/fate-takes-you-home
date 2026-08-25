// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;
using System.Windows.Media;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Loading;

/// <summary>How serious a validation finding is.</summary>
public enum ThemeDiagnosticSeverity
{
    /// <summary>Worth knowing, but the theme is fine.</summary>
    Info,

    /// <summary>The theme will load but something about it is likely a mistake.</summary>
    Warning,

    /// <summary>The theme cannot be used as written.</summary>
    Error,
}

/// <summary>A single validation finding, addressed to whoever wrote the theme.</summary>
public sealed record ThemeDiagnostic(
    ThemeDiagnosticSeverity Severity,
    string Field,
    string Message)
{
    public override string ToString() => $"{Severity}: {Field} — {Message}";
}

/// <summary>The outcome of validating one theme document.</summary>
public sealed class ThemeValidationResult
{
    public required IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; }

    public bool IsValid => !Diagnostics.Any(d => d.Severity == ThemeDiagnosticSeverity.Error);

    public IEnumerable<ThemeDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == ThemeDiagnosticSeverity.Error);

    public IEnumerable<ThemeDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == ThemeDiagnosticSeverity.Warning);

    public static ThemeValidationResult Ok { get; } = new() { Diagnostics = [] };
}

/// <summary>
/// Checks a theme for the mistakes that are easy to make and hard to spot.
/// </summary>
/// <remarks>
/// Beyond structural checks, this enforces the two accessibility rules the brand contract calls
/// out by name: body text must clear WCAG AA against the surface it sits on, and gold is reserved
/// for interactive and brand use rather than status. Both are review rules in the brand repo;
/// making them machine-checkable here means a custom theme cannot quietly break them.
/// </remarks>
public static class ThemeValidator
{
    /// <summary>WCAG 2.1 AA, normal-size text.</summary>
    public const double MinimumBodyContrast = 4.5;

    /// <summary>WCAG 2.1 AA, large text and non-text UI components.</summary>
    public const double MinimumLargeTextContrast = 3.0;

    private static readonly Regex IdPattern =
        new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Validates the raw document — the checks that do not need inheritance resolved.</summary>
    public static ThemeValidationResult ValidateDocument(ThemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var findings = new List<ThemeDiagnostic>();

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error, "id",
                "Every theme needs an id. Use lower-case words joined by hyphens, e.g. \"midnight-brass\"."));
        }
        else if (!IdPattern.IsMatch(document.Id))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error, "id",
                $"\"{document.Id}\" is not a valid id. Use lower-case letters, digits and single hyphens."));
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "name",
                "No display name. The picker will fall back to the id."));
        }

        if (!string.IsNullOrEmpty(document.BasedOn)
            && string.Equals(document.BasedOn, document.Id, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error, "basedOn",
                "A theme cannot inherit from itself."));
        }

        ValidateColourStrings(document, findings);

        if (document.Motion?.SpeedScale is { } scale && (scale < 0.1 || scale > 4))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "motion.speedScale",
                $"{scale} is outside the usable 0.1–4 range and will be clamped."));
        }

        if (document.Typography?.Scale is { } typeScale && (typeScale < 0.75 || typeScale > 2))
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "typography.scale",
                $"{typeScale} is outside the usable 0.75–2 range and will be clamped."));
        }

        return new ThemeValidationResult { Diagnostics = findings };
    }

    /// <summary>Validates a resolved theme — contrast, tier coherence, and brand rules.</summary>
    public static ThemeValidationResult ValidateResolved(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var findings = new List<ThemeDiagnostic>();
        ThemeColors c = theme.Colors;

        // Text has to be readable on the surfaces it actually sits on.
        CheckContrast(findings, "colors.textPrimary", c.TextPrimary, c.SurfaceBase, MinimumBodyContrast,
            ThemeDiagnosticSeverity.Error);
        CheckContrast(findings, "colors.textPrimary", c.TextPrimary, c.SurfaceRaised, MinimumBodyContrast,
            ThemeDiagnosticSeverity.Error);
        CheckContrast(findings, "colors.textMuted", c.TextMuted, c.SurfaceBase, MinimumBodyContrast,
            ThemeDiagnosticSeverity.Warning);
        CheckContrast(findings, "colors.textFaint", c.TextFaint, c.SurfaceBase, MinimumBodyContrast,
            ThemeDiagnosticSeverity.Warning);

        // Accent is an interactive affordance, so it is held to the non-text component threshold.
        CheckContrast(findings, "colors.accentDefault", c.AccentDefault, c.SurfaceBase,
            MinimumLargeTextContrast, ThemeDiagnosticSeverity.Warning);

        // Text placed on top of a filled accent button.
        CheckContrast(findings, "colors.textInverse", c.TextInverse, c.AccentDefault,
            MinimumBodyContrast, ThemeDiagnosticSeverity.Warning);

        // The brand reserves gold for interactive and brand use. Status must stay distinguishable
        // from it, or a warning badge starts reading as something you can press.
        WarnIfIndistinct(findings, "colors.statusWarning", c.StatusWarning, c.AccentDefault);
        WarnIfIndistinct(findings, "colors.statusSuccess", c.StatusSuccess, c.AccentDefault);

        if (theme.Appearance == ThemeAppearance.Dark
            && ColorParser.RelativeLuminance(c.SurfaceBase) > 0.5)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "appearance",
                "Declared as a dark theme but the base surface is light. Window chrome will not match."));
        }

        if (theme.Appearance == ThemeAppearance.Light
            && ColorParser.RelativeLuminance(c.SurfaceBase) < 0.5)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "appearance",
                "Declared as a light theme but the base surface is dark. Window chrome will not match."));
        }

        // Tier coherence: the brand treats mixing tiers as a review failure.
        if (theme.Tier == OrnamentTier.Utility && theme.Ornament.AmbientMotion)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "ornament.ambientMotion",
                "Ambient motion is a ceremonial-tier device. In a utility theme it reads as noise."));
        }

        if (theme.Tier == OrnamentTier.Utility && theme.Ornament.OrnateDividers)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "ornament.ornateDividers",
                "Ornate dividers are a ceremonial-tier device and will look out of place here."));
        }

        if (theme.Ornament.MaxConcurrentAnimations > 5 && theme.Tier != OrnamentTier.Ceremonial)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Info, "ornament.maxConcurrentAnimations",
                "Above five simultaneous animations, even the ceremonial tier starts to feel busy."));
        }

        if (!theme.Motion.RespectSystemReducedMotion)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, "motion.respectSystemReducedMotion",
                "Ignoring the system reduced-motion setting. Every animation here is decorative, so "
                + "this will affect people who disabled animations for a reason."));
        }

        if (theme.Colors.DepthWash.Count == 0 && theme.Ornament.DepthWash)
        {
            findings.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Info, "colors.depthWash",
                "Depth wash is enabled but no layers are defined, so the background will be a flat fill."));
        }

        return new ThemeValidationResult { Diagnostics = findings };
    }

    private static void ValidateColourStrings(ThemeDocument document, List<ThemeDiagnostic> findings)
    {
        if (document.Colors is not { } colors)
        {
            return;
        }

        foreach ((string field, string? value) in EnumerateColourFields(colors))
        {
            if (value is null)
            {
                continue;
            }

            if (!ColorParser.TryParse(value, out _, out string? error))
            {
                findings.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error, $"colors.{field}", error));
            }
        }

        if (colors.DepthWash is null)
        {
            return;
        }

        for (int i = 0; i < colors.DepthWash.Count; i++)
        {
            RadialWashDocument layer = colors.DepthWash[i];

            if (layer.Color is null)
            {
                findings.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error, $"colors.depthWash[{i}].color",
                    "Each depth wash layer needs a colour."));
            }
            else if (!ColorParser.TryParse(layer.Color, out _, out string? error))
            {
                findings.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error, $"colors.depthWash[{i}].color", error));
            }

            if (layer.Falloff is { } falloff && (falloff <= 0 || falloff > 1))
            {
                findings.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Warning, $"colors.depthWash[{i}].falloff",
                    $"{falloff} is outside 0–1 and will be clamped."));
            }
        }
    }

    private static IEnumerable<(string Field, string? Value)> EnumerateColourFields(ThemeColorsDocument c)
    {
        yield return (nameof(c.SurfaceBase), c.SurfaceBase);
        yield return (nameof(c.SurfaceRaised), c.SurfaceRaised);
        yield return (nameof(c.SurfaceOverlay), c.SurfaceOverlay);
        yield return (nameof(c.SurfaceSunken), c.SurfaceSunken);
        yield return (nameof(c.SurfaceHighest), c.SurfaceHighest);
        yield return (nameof(c.BorderSubtle), c.BorderSubtle);
        yield return (nameof(c.BorderDefault), c.BorderDefault);
        yield return (nameof(c.BorderEmphasis), c.BorderEmphasis);
        yield return (nameof(c.TextPrimary), c.TextPrimary);
        yield return (nameof(c.TextMuted), c.TextMuted);
        yield return (nameof(c.TextFaint), c.TextFaint);
        yield return (nameof(c.TextInverse), c.TextInverse);
        yield return (nameof(c.TextAccent), c.TextAccent);
        yield return (nameof(c.AccentDefault), c.AccentDefault);
        yield return (nameof(c.AccentHover), c.AccentHover);
        yield return (nameof(c.AccentPressed), c.AccentPressed);
        yield return (nameof(c.AccentSubtle), c.AccentSubtle);
        yield return (nameof(c.AccentGlow), c.AccentGlow);
        yield return (nameof(c.StatusLive), c.StatusLive);
        yield return (nameof(c.StatusSuccess), c.StatusSuccess);
        yield return (nameof(c.StatusWarning), c.StatusWarning);
        yield return (nameof(c.StatusDanger), c.StatusDanger);
        yield return (nameof(c.StatusInfo), c.StatusInfo);
    }

    private static void CheckContrast(
        List<ThemeDiagnostic> findings,
        string field,
        Color foreground,
        Color background,
        double minimum,
        ThemeDiagnosticSeverity severity)
    {
        double ratio = ColorParser.ContrastRatio(foreground, background);

        if (ratio >= minimum)
        {
            return;
        }

        findings.Add(new ThemeDiagnostic(
            severity,
            field,
            $"Contrast against {ColorParser.ToCss(background)} is {ratio:0.0}:1, below the "
            + $"{minimum:0.0}:1 needed for WCAG AA."));
    }

    private static void WarnIfIndistinct(
        List<ThemeDiagnostic> findings, string field, Color status, Color accent)
    {
        // Two colours this close will read as the same swatch at 12px on a dark panel.
        int distance = Math.Abs(status.R - accent.R)
                     + Math.Abs(status.G - accent.G)
                     + Math.Abs(status.B - accent.B);

        if (distance >= 60)
        {
            return;
        }

        findings.Add(new ThemeDiagnostic(
            ThemeDiagnosticSeverity.Warning,
            field,
            $"{ColorParser.ToCss(status)} is nearly identical to the accent colour. The accent means "
            + "\"you can press this\"; a status that looks the same will be misread as interactive."));
    }
}
