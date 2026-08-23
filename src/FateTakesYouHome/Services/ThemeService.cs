using System.IO;
using System.Windows;
using System.Windows.Threading;
using FateTakesYouHome.Interop;
using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.Theming.Rendering;

namespace FateTakesYouHome.Services;

/// <summary>Raised when the live theme changed.</summary>
public sealed class ThemeAppliedEventArgs(Theme theme, bool backdropModeChanged) : EventArgs
{
    public Theme Theme { get; } = theme;

    /// <summary>
    /// True when the backdrop mode changed, which windows cannot adopt in place — a WPF window's
    /// transparency is fixed once its handle exists, so the flyout has to be rebuilt.
    /// </summary>
    public bool BackdropModeChanged { get; } = backdropModeChanged;
}

/// <summary>
/// Owns the theme repository and publishes the live theme into the application's resources.
/// </summary>
/// <remarks>
/// <para>
/// The whole UI binds theme values with <c>DynamicResource</c>, so applying a theme is a matter of
/// swapping one merged dictionary. Every window repaints; nothing is rebuilt.
/// </para>
/// <para>
/// Two things can override what a theme asks for, and both win: the user's own "disable
/// animations" setting, and the Windows accessibility setting for animation. The brand contract
/// requires the second, and there is nothing in this app whose animation is load-bearing.
/// </para>
/// </remarks>
public sealed class ThemeService : IDisposable
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly ResourceDictionary _applicationResources;

    private ResourceDictionary? _live;
    private bool _disposed;

    public ThemeService(
        AppLog log,
        SettingsService settings,
        ResourceDictionary applicationResources,
        Dispatcher dispatcher)
    {
        _log = log;
        _settings = settings;
        _applicationResources = applicationResources;

        Repository = new ThemeRepository(
            AppPaths.BundledThemes,
            AppPaths.UserThemes,
            log.AsCallback(),
            dispatcher);

        Repository.ThemesChanged += OnThemesChanged;
    }

    public ThemeRepository Repository { get; }

    /// <summary>The theme currently painted. Never null after <see cref="Initialise"/>.</summary>
    public Theme Current { get; private set; } = null!;

    /// <summary>Raised after a new theme has been published into the resources.</summary>
    public event EventHandler<ThemeAppliedEventArgs>? Applied;

    /// <summary>Loads themes from disk and applies the one the settings name.</summary>
    public void Initialise()
    {
        SeedUserThemesFolder();

        Repository.Reload();
        Repository.StartWatching();

        Apply(_settings.Current.ThemeId);
    }

    /// <summary>Switches to a theme by id and persists the choice.</summary>
    public void Select(string themeId)
    {
        if (string.Equals(_settings.Current.ThemeId, themeId, StringComparison.OrdinalIgnoreCase)
            && Current is not null)
        {
            return;
        }

        _settings.Current.ThemeId = themeId;
        _settings.Save();

        Apply(themeId);
    }

    /// <summary>
    /// Paints a theme that is not saved anywhere. Used for live preview in the theme editor.
    /// </summary>
    public void PreviewDraft(ThemeDocument draft)
    {
        Theme resolved = Repository.ResolveDraft(draft);
        Publish(resolved);
    }

    /// <summary>Discards a preview and repaints whatever the settings actually say.</summary>
    public void CancelPreview() => Apply(_settings.Current.ThemeId);

    /// <summary>Re-applies the current theme, picking up changed motion settings.</summary>
    public void Refresh() => Apply(_settings.Current.ThemeId);

    private void Apply(string? themeId)
    {
        Theme resolved = Repository.Resolve(themeId);
        Publish(resolved);
    }

    private void Publish(Theme theme)
    {
        Theme effective = ApplyMotionOverrides(theme);

        bool backdropChanged = Current is not null && Current.Backdrop.Mode != effective.Backdrop.Mode;

        ResourceDictionary built = ThemeResourceBuilder.Build(effective);

        // Swap in place: replacing the entry keeps the theme dictionary at a known index, so it
        // never fights with the control styles merged after it.
        if (_live is not null)
        {
            int index = _applicationResources.MergedDictionaries.IndexOf(_live);

            if (index >= 0)
            {
                _applicationResources.MergedDictionaries[index] = built;
            }
            else
            {
                _applicationResources.MergedDictionaries.Insert(0, built);
            }
        }
        else
        {
            _applicationResources.MergedDictionaries.Insert(0, built);
        }

        _live = built;
        Current = effective;

        _log.Info(
            $"Applied theme '{effective.Name}' ({effective.Id}), {effective.Tier} tier, "
            + $"{effective.Backdrop.Mode} backdrop.");

        Applied?.Invoke(this, new ThemeAppliedEventArgs(effective, backdropChanged));
    }

    /// <summary>
    /// Returns the theme with motion forced off when the user or Windows asked for that.
    /// </summary>
    /// <remarks>
    /// Rewriting the theme rather than special-casing at each animation site means every consumer
    /// of <c>Fate.Duration.*</c> gets zero-length durations automatically, and no animation can be
    /// forgotten.
    /// </remarks>
    private Theme ApplyMotionOverrides(Theme theme)
    {
        bool userDisabled = _settings.Current.DisableAnimations;

        bool systemDisabled = theme.Motion.RespectSystemReducedMotion
                              && WindowEffects.IsReducedMotionPreferred();

        if (!userDisabled && !systemDisabled)
        {
            return theme;
        }

        if (systemDisabled && !userDisabled)
        {
            _log.Info("Windows is set to reduce animation, so the theme's motion has been disabled.");
        }

        return new Theme
        {
            Id = theme.Id,
            Name = theme.Name,
            Author = theme.Author,
            Version = theme.Version,
            Description = theme.Description,
            Homepage = theme.Homepage,
            IsBuiltIn = theme.IsBuiltIn,
            SourcePath = theme.SourcePath,
            InheritanceChain = theme.InheritanceChain,
            Appearance = theme.Appearance,
            Tier = theme.Tier,
            Colors = theme.Colors,
            Typography = theme.Typography,
            Shape = theme.Shape,
            Ornament = theme.Ornament,
            Buttons = theme.Buttons,
            Backdrop = theme.Backdrop,
            Motion = new ThemeMotion
            {
                Enabled = false,
                SpeedScale = theme.Motion.SpeedScale,
                FlyoutOpen = TimeSpan.Zero,
                FlyoutClose = TimeSpan.Zero,
                Hover = TimeSpan.Zero,
                Press = TimeSpan.Zero,
                PageTransition = TimeSpan.Zero,
                StaggerStep = TimeSpan.Zero,
                StaggerMaxItems = 0,
                FlyoutEasing = theme.Motion.FlyoutEasing,
                StandardEasing = theme.Motion.StandardEasing,

                // No travel and no zoom: with zero duration these would otherwise leave the
                // flyout permanently offset by its start transform.
                FlyoutTravel = 0,
                FlyoutScaleFrom = 1,
                RespectSystemReducedMotion = theme.Motion.RespectSystemReducedMotion,
            },
        };
    }

    /// <summary>
    /// Prepares the user themes folder on first run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the README and the JSON schema are written. Copying the shipped themes in as well
    /// would be friendlier at first glance but is wrong: a user copy shadows the built-in of the
    /// same id, so every shipped theme would immediately present itself as editable and
    /// deletable, and none would ever show as built in. Duplicate in the editor is the deliberate
    /// way to get an editable copy.
    /// </para>
    /// <para>
    /// The schema is copied rather than merely referenced by URL so that an editor can resolve
    /// <c>$schema</c> with no network.
    /// </para>
    /// </remarks>
    private void SeedUserThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserThemes);

            string readme = Path.Combine(AppPaths.UserThemes, "README.txt");

            if (!File.Exists(readme))
            {
                File.WriteAllText(readme, UserThemesReadme);
            }

            if (!Directory.Exists(AppPaths.BundledThemes))
            {
                return;
            }

            const string schemaName = "theme.schema.json";
            string schemaSource = Path.Combine(AppPaths.BundledThemes, schemaName);
            string schemaDestination = Path.Combine(AppPaths.UserThemes, schemaName);

            // Overwritten on purpose: an upgrade that adds a theme option should update the
            // schema, and nobody hand-edits this file.
            if (File.Exists(schemaSource))
            {
                File.Copy(schemaSource, schemaDestination, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Could not prepare the user themes folder.", ex);
        }
    }

    private void OnThemesChanged(object? sender, ThemesChangedEventArgs e)
    {
        _log.Info(
            e.ChangedThemeId is { Length: > 0 } id
                ? $"Theme file '{id}' changed on disk; reloading."
                : "Themes changed on disk; reloading.");

        // Re-apply unconditionally. A change to an ancestor theme affects everything that
        // inherits from it, so narrowing this to "only if the active theme changed" would miss
        // exactly the case the editor cares about.
        Apply(_settings.Current.ThemeId);
    }

    private const string UserThemesReadme =
        """
        Fate Takes You Home — themes
        ============================

        Drop a .json theme file in this folder and it appears in the theme picker within a
        second. Edit one while the app is running and the interface repaints as you save.

        A theme is a patch, not a whole document. Anything you leave out is inherited, so the
        smallest useful theme is about six lines:

            {
              "id": "my-theme",
              "name": "My Theme",
              "basedOn": "fate",
              "colors": { "accentDefault": "#7FD4C1" }
            }

        The shipped themes are not copied here. To start from one, open Themes in the app and
        press Duplicate — that writes a small file into this folder that inherits from the
        original. You can also copy a shipped theme by hand from the Themes folder inside the
        installation directory.

        Files here take priority over the ones shipped with the app, so a file named fate.json
        replaces the built-in FATE theme entirely. Delete it to get the original back.

        Colours accept #RGB, #RRGGBB, #RRGGBBAA (CSS order, alpha last), rgb(), rgba() and the
        standard colour names. Durations are milliseconds. Easing accepts a preset name or a
        literal cubic-bezier(x1, y1, x2, y2).

        The full reference is in docs/THEMING.md in the repository.
        """;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Repository.ThemesChanged -= OnThemesChanged;
        Repository.Dispose();
    }
}
