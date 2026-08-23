using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.Services;
using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;
using Microsoft.Win32;

namespace FateTakesYouHome.ViewModels;

/// <summary>One editable colour role in the theme editor.</summary>
public sealed partial class ThemeColorSlot : ObservableObject
{
    private readonly Action<ThemeColorSlot> _onChanged;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string? _error;

    public ThemeColorSlot(string label, string role, string value, Action<ThemeColorSlot> onChanged)
    {
        Label = label;
        Role = role;
        _value = value;
        _onChanged = onChanged;
    }

    /// <summary>Human-readable name, e.g. "Accent · default".</summary>
    public string Label { get; }

    /// <summary>The property name in the theme document, e.g. <c>accentDefault</c>.</summary>
    public string Role { get; }

    /// <summary>A brush of the current value, for the swatch. Transparent when unparseable.</summary>
    public Brush Swatch
    {
        get
        {
            if (!ColorParser.TryParse(Value, out Color colour))
            {
                return Brushes.Transparent;
            }

            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }
    }

    partial void OnValueChanged(string value)
    {
        Error = ColorParser.TryParse(value, out _, out string? problem) ? null : problem;

        OnPropertyChanged(nameof(Swatch));
        _onChanged(this);
    }
}

/// <summary>One switch in the ornament section.</summary>
public sealed partial class ThemeSwitchSlot : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty]
    private bool _value;

    public ThemeSwitchSlot(string label, string description, bool value, Action onChanged)
    {
        Label = label;
        Description = description;
        _value = value;
        _onChanged = onChanged;
    }

    public string Label { get; }

    public string Description { get; }

    partial void OnValueChanged(bool value) => _onChanged();
}

/// <summary>
/// The theme picker and the theme editor.
/// </summary>
/// <remarks>
/// <para>
/// Editing previews live: every keystroke re-resolves the draft and repaints the whole application,
/// which is the only honest way to judge a colour. Nothing is written to disk until Save, and
/// leaving the editor without saving restores the theme that was actually selected.
/// </para>
/// <para>
/// Built-in themes cannot be edited in place. Choosing Edit on one duplicates it into the user's
/// folder first, so the shipped themes are always recoverable.
/// </para>
/// </remarks>
public sealed partial class ThemesViewModel : ObservableObject, IDisposable
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;

    private ThemeDocument? _draft;
    private bool _suppressPreview;
    private bool _disposed;

    [ObservableProperty]
    private ThemeEntry? _selected;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // -- Draft identity ------------------------------------------------------------------------

    [ObservableProperty]
    private string _draftName = string.Empty;

    [ObservableProperty]
    private string _draftDescription = string.Empty;

    [ObservableProperty]
    private OrnamentTier _draftTier = OrnamentTier.Charted;

    [ObservableProperty]
    private ThemeAppearance _draftAppearance = ThemeAppearance.Dark;

    [ObservableProperty]
    private ButtonStyle _draftButtonStyle = ButtonStyle.Engraved;

    [ObservableProperty]
    private BackdropMode _draftBackdrop = BackdropMode.Composited;

    // -- Draft motion --------------------------------------------------------------------------

    [ObservableProperty]
    private bool _draftMotionEnabled = true;

    [ObservableProperty]
    private double _draftSpeedScale = 1;

    [ObservableProperty]
    private double _draftFlyoutOpenMs = 220;

    [ObservableProperty]
    private double _draftFlyoutTravel = 14;

    [ObservableProperty]
    private double _draftFlyoutScaleFrom = 0.97;

    [ObservableProperty]
    private string _draftFlyoutEasing = "fate-spring";

    // -- Draft shape ---------------------------------------------------------------------------

    [ObservableProperty]
    private double _draftRadiusMd = 10;

    [ObservableProperty]
    private double _draftFlyoutWidth = 368;

    [ObservableProperty]
    private double _draftTileHeight = 52;

    public ThemesViewModel(AppLog log, SettingsService settings, ThemeService themes)
    {
        _log = log;
        _settings = settings;
        _themes = themes;

        _themes.Repository.ThemesChanged += OnRepositoryChanged;

        RefreshList();
    }

    public ObservableCollection<ThemeEntry> Available { get; } = [];

    public ObservableCollection<ThemeColorSlot> Colors { get; } = [];

    public ObservableCollection<ThemeSwitchSlot> Ornaments { get; } = [];

    public ObservableCollection<ThemeDiagnostic> Diagnostics { get; } = [];

    public IReadOnlyList<OrnamentTier> Tiers { get; } = Enum.GetValues<OrnamentTier>();

    public IReadOnlyList<ThemeAppearance> Appearances { get; } = Enum.GetValues<ThemeAppearance>();

    public IReadOnlyList<ButtonStyle> ButtonStyles { get; } = Enum.GetValues<ButtonStyle>();

    public IReadOnlyList<BackdropMode> BackdropModes { get; } = Enum.GetValues<BackdropMode>();

    public IReadOnlyList<string> EasingPresets { get; } =
        ["fate", "fate-spring", "linear", "ease-out", "ease-in", "ease-in-out"];

    /// <summary>Where user themes live. Shown so people can find the folder.</summary>
    public static string UserThemesPath => AppPaths.UserThemes;

    public bool CanEditSelected => Selected is not null;

    public bool CanDeleteSelected => Selected is { Origin: ThemeOrigin.User };

    public bool HasDiagnostics => Diagnostics.Count > 0;

    // ------------------------------------------------------------------ list

    private void RefreshList()
    {
        string? previous = Selected?.Id ?? _settings.Current.ThemeId;

        Available.Clear();

        foreach (ThemeEntry entry in _themes.Repository.ListForDisplay())
        {
            Available.Add(entry);
        }

        _suppressPreview = true;
        Selected = Available.FirstOrDefault(
                       e => string.Equals(e.Id, previous, StringComparison.OrdinalIgnoreCase))
                   ?? Available.FirstOrDefault();
        _suppressPreview = false;
    }

    partial void OnSelectedChanged(ThemeEntry? value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));

        if (value is null || _suppressPreview)
        {
            return;
        }

        if (IsEditing)
        {
            // Switching themes abandons an unsaved edit rather than silently carrying it across.
            CancelEdit();
        }

        _themes.Select(value.Id);
        StatusMessage = $"Applied {value.DisplayName}.";
    }

    // ------------------------------------------------------------------ editing

    [RelayCommand]
    private void Edit()
    {
        if (Selected is null)
        {
            return;
        }

        ThemeEntry entry = Selected;

        // A built-in is duplicated first: the shipped themes must stay recoverable.
        if (entry.Origin == ThemeOrigin.BuiltIn)
        {
            Duplicate();
            return;
        }

        BeginEditing(entry.Document);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null)
        {
            return;
        }

        string newId = _themes.Repository.MakeUniqueId(Selected.Id + "-copy");

        // A duplicate inherits rather than copying every value. That way retuning the original
        // still flows through, and the copy stays a short, readable patch.
        var document = new ThemeDocument
        {
            Id = newId,
            Name = Selected.DisplayName + " (copy)",
            Author = Environment.UserName,
            Version = "1.0.0",
            Description = $"Based on {Selected.DisplayName}.",
            BasedOn = Selected.Id,
        };

        _themes.Repository.SaveUserTheme(document);
        RefreshList();

        Selected = Available.FirstOrDefault(e => e.Id == newId);
        BeginEditing(document);

        StatusMessage = $"Created {document.Name}. It is yours to change.";
    }

    [RelayCommand]
    private void NewTheme()
    {
        string newId = _themes.Repository.MakeUniqueId("my-theme");

        var document = new ThemeDocument
        {
            Id = newId,
            Name = "My Theme",
            Author = Environment.UserName,
            Version = "1.0.0",
            Description = "A theme of my own.",
            BasedOn = ThemeDefaults.FateThemeId,
        };

        _themes.Repository.SaveUserTheme(document);
        RefreshList();

        Selected = Available.FirstOrDefault(e => e.Id == newId);
        BeginEditing(document);

        StatusMessage = "New theme created from FATE.";
    }

    private void BeginEditing(ThemeDocument source)
    {
        // Edit a clone. The document in the repository must not change until Save.
        _draft = Clone(source);

        _suppressPreview = true;

        DraftName = _draft.Name ?? _draft.Id ?? string.Empty;
        DraftDescription = _draft.Description ?? string.Empty;

        Theme resolved = _themes.Repository.ResolveDraft(_draft);

        DraftTier = resolved.Tier;
        DraftAppearance = resolved.Appearance;
        DraftButtonStyle = resolved.Buttons.Style;
        DraftBackdrop = resolved.Backdrop.Mode;

        DraftMotionEnabled = resolved.Motion.Enabled;
        DraftSpeedScale = resolved.Motion.SpeedScale;
        DraftFlyoutOpenMs = resolved.Motion.FlyoutOpen.TotalMilliseconds;
        DraftFlyoutTravel = resolved.Motion.FlyoutTravel;
        DraftFlyoutScaleFrom = resolved.Motion.FlyoutScaleFrom;
        DraftFlyoutEasing = resolved.Motion.FlyoutEasing.ToString();

        DraftRadiusMd = resolved.Shape.RadiusMd;
        DraftFlyoutWidth = resolved.Shape.FlyoutWidth;
        DraftTileHeight = resolved.Shape.TileHeight;

        BuildColourSlots(resolved);
        BuildOrnamentSlots(resolved);

        _suppressPreview = false;

        IsEditing = true;
        HasUnsavedChanges = false;

        Validate();
    }

    private void BuildColourSlots(Theme resolved)
    {
        Colors.Clear();

        void Add(string label, string role, Color value) =>
            Colors.Add(new ThemeColorSlot(label, role, ColorParser.ToCss(value), OnColourEdited));

        ThemeColors c = resolved.Colors;

        Add("Surface · base", "surfaceBase", c.SurfaceBase);
        Add("Surface · raised", "surfaceRaised", c.SurfaceRaised);
        Add("Surface · overlay", "surfaceOverlay", c.SurfaceOverlay);
        Add("Surface · sunken", "surfaceSunken", c.SurfaceSunken);
        Add("Surface · highest", "surfaceHighest", c.SurfaceHighest);

        Add("Border · subtle", "borderSubtle", c.BorderSubtle);
        Add("Border · default", "borderDefault", c.BorderDefault);
        Add("Border · emphasis", "borderEmphasis", c.BorderEmphasis);

        Add("Text · primary", "textPrimary", c.TextPrimary);
        Add("Text · muted", "textMuted", c.TextMuted);
        Add("Text · faint", "textFaint", c.TextFaint);
        Add("Text · inverse", "textInverse", c.TextInverse);
        Add("Text · accent", "textAccent", c.TextAccent);

        Add("Accent · default", "accentDefault", c.AccentDefault);
        Add("Accent · hover", "accentHover", c.AccentHover);
        Add("Accent · pressed", "accentPressed", c.AccentPressed);
        Add("Accent · subtle", "accentSubtle", c.AccentSubtle);
        Add("Accent · glow", "accentGlow", c.AccentGlow);

        Add("Status · live", "statusLive", c.StatusLive);
        Add("Status · success", "statusSuccess", c.StatusSuccess);
        Add("Status · warning", "statusWarning", c.StatusWarning);
        Add("Status · danger", "statusDanger", c.StatusDanger);
        Add("Status · info", "statusInfo", c.StatusInfo);
    }

    private void BuildOrnamentSlots(Theme resolved)
    {
        Ornaments.Clear();

        ThemeOrnament o = resolved.Ornament;

        Ornaments.Add(new ThemeSwitchSlot(
            "Corner brackets",
            "Short rules at the corners of panels. Charted and ceremonial tiers.",
            o.CornerBrackets, OnOrnamentEdited));

        Ornaments.Add(new ThemeSwitchSlot(
            "Film grain",
            "A faint noise overlay that stops large dark surfaces looking flat.",
            o.FilmGrain, OnOrnamentEdited));

        Ornaments.Add(new ThemeSwitchSlot(
            "Depth wash",
            "Radial light over the base surface. The brand forbids a flat fill.",
            o.DepthWash, OnOrnamentEdited));

        Ornaments.Add(new ThemeSwitchSlot(
            "Stagger entrances",
            "Items appear one after another rather than all at once.",
            o.StaggerEntrances, OnOrnamentEdited));

        Ornaments.Add(new ThemeSwitchSlot(
            "Gradient display type",
            "Pour the metallic gradient through page titles. Ceremonial tier.",
            o.GradientDisplayFill, OnOrnamentEdited));

        Ornaments.Add(new ThemeSwitchSlot(
            "Ambient motion",
            "Slow continuous movement in the background. Ceremonial tier only.",
            o.AmbientMotion, OnOrnamentEdited));
    }

    // ------------------------------------------------------------------ live preview

    private void OnColourEdited(ThemeColorSlot slot)
    {
        if (_draft is null || _suppressPreview)
        {
            return;
        }

        if (slot.Error is not null)
        {
            // An unparseable colour is a half-typed one. Leave the last good value painted.
            return;
        }

        _draft.Colors ??= new ThemeColorsDocument();
        SetColourRole(_draft.Colors, slot.Role, slot.Value);

        Preview();
    }

    private void OnOrnamentEdited()
    {
        if (_draft is null || _suppressPreview)
        {
            return;
        }

        _draft.Ornament ??= new ThemeOrnamentDocument();

        _draft.Ornament.CornerBrackets = Ornaments[0].Value;
        _draft.Ornament.FilmGrain = Ornaments[1].Value;
        _draft.Ornament.DepthWash = Ornaments[2].Value;
        _draft.Ornament.StaggerEntrances = Ornaments[3].Value;
        _draft.Ornament.GradientDisplayFill = Ornaments[4].Value;
        _draft.Ornament.AmbientMotion = Ornaments[5].Value;

        Preview();
    }

    /// <summary>Pushes the draft into the live application resources.</summary>
    private void Preview()
    {
        if (_draft is null)
        {
            return;
        }

        HasUnsavedChanges = true;

        try
        {
            _themes.PreviewDraft(_draft);
        }
        catch (Exception ex)
        {
            _log.Warning("Could not preview the theme draft.", ex);
        }

        Validate();
    }

    private void Validate()
    {
        Diagnostics.Clear();

        if (_draft is null)
        {
            OnPropertyChanged(nameof(HasDiagnostics));
            return;
        }

        foreach (ThemeDiagnostic diagnostic in ThemeValidator.ValidateDocument(_draft).Diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }

        Theme resolved = _themes.Repository.ResolveDraft(_draft);

        foreach (ThemeDiagnostic diagnostic in ThemeValidator.ValidateResolved(resolved).Diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }

        OnPropertyChanged(nameof(HasDiagnostics));
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void Save()
    {
        if (_draft is null)
        {
            return;
        }

        _draft.Name = DraftName;
        _draft.Description = DraftDescription;

        try
        {
            string path = _themes.Repository.SaveUserTheme(_draft);

            HasUnsavedChanges = false;
            IsEditing = false;

            RefreshList();

            Selected = Available.FirstOrDefault(e => e.Id == _draft.Id);
            _themes.Select(_draft.Id!);

            StatusMessage = $"Saved to {Path.GetFileName(path)}.";
            _log.Info($"Saved theme '{_draft.Id}' to {path}.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save: {ex.Message}";
            _log.Error("Could not save the theme.", ex);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _draft = null;
        IsEditing = false;
        HasUnsavedChanges = false;

        Colors.Clear();
        Ornaments.Clear();
        Diagnostics.Clear();

        _themes.CancelPreview();
        StatusMessage = "Changes discarded.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { Origin: ThemeOrigin.User } entry)
        {
            return;
        }

        string name = entry.DisplayName;

        if (_themes.Repository.DeleteUserTheme(entry.Id))
        {
            IsEditing = false;
            _draft = null;

            RefreshList();
            _themes.Select(_settings.Current.ThemeId);

            StatusMessage = $"Deleted {name}.";
        }
        else
        {
            StatusMessage = $"Could not delete {name}.";
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (Selected is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export theme",
            FileName = Selected.Id + ".json",
            Filter = "Theme files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _themes.Repository.Export(Selected.Id, dialog.FileName);
            StatusMessage = $"Exported to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export: {ex.Message}";
            _log.Error("Could not export the theme.", ex);
        }
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import theme",
            Filter = "Theme files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ThemeLoadResult result = _themes.Repository.Import(dialog.FileName);

        if (!result.Succeeded)
        {
            StatusMessage = "That file is not a valid theme: "
                          + string.Join(" ", result.Validation.Errors.Select(d => d.Message));
            return;
        }

        RefreshList();
        Selected = Available.FirstOrDefault(e => e.Id == result.Document!.Id);

        StatusMessage = $"Imported {result.Document!.Name ?? result.Document.Id}.";
    }

    [RelayCommand]
    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserThemes);
            Process.Start(new ProcessStartInfo(AppPaths.UserThemes) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning("Could not open the themes folder.", ex);
            StatusMessage = "Could not open the folder.";
        }
    }

    // ------------------------------------------------------------------ draft reactions

    partial void OnDraftTierChanged(OrnamentTier value) => ApplyScalarEdit(d => d.Tier = value);

    partial void OnDraftAppearanceChanged(ThemeAppearance value) =>
        ApplyScalarEdit(d => d.Appearance = value);

    partial void OnDraftButtonStyleChanged(ButtonStyle value) =>
        ApplyScalarEdit(d => (d.Buttons ??= new ThemeButtonsDocument()).Style = value);

    partial void OnDraftBackdropChanged(BackdropMode value) =>
        ApplyScalarEdit(d => (d.Backdrop ??= new ThemeBackdropDocument()).Mode = value);

    partial void OnDraftMotionEnabledChanged(bool value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).Enabled = value);

    partial void OnDraftSpeedScaleChanged(double value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).SpeedScale = Math.Round(value, 2));

    partial void OnDraftFlyoutOpenMsChanged(double value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).FlyoutOpenMs = Math.Round(value));

    partial void OnDraftFlyoutTravelChanged(double value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).FlyoutTravel = Math.Round(value));

    partial void OnDraftFlyoutScaleFromChanged(double value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).FlyoutScaleFrom = Math.Round(value, 3));

    partial void OnDraftFlyoutEasingChanged(string value) =>
        ApplyScalarEdit(d => (d.Motion ??= new ThemeMotionDocument()).FlyoutEasing = value);

    partial void OnDraftRadiusMdChanged(double value) =>
        ApplyScalarEdit(d => (d.Shape ??= new ThemeShapeDocument()).RadiusMd = Math.Round(value));

    partial void OnDraftFlyoutWidthChanged(double value) =>
        ApplyScalarEdit(d => (d.Shape ??= new ThemeShapeDocument()).FlyoutWidth = Math.Round(value));

    partial void OnDraftTileHeightChanged(double value) =>
        ApplyScalarEdit(d => (d.Shape ??= new ThemeShapeDocument()).TileHeight = Math.Round(value));

    partial void OnDraftNameChanged(string value) => ApplyScalarEdit(d => d.Name = value);

    private void ApplyScalarEdit(Action<ThemeDocument> edit)
    {
        if (_draft is null || _suppressPreview || !IsEditing)
        {
            return;
        }

        edit(_draft);
        Preview();
    }

    private static void SetColourRole(ThemeColorsDocument colors, string role, string value)
    {
        switch (role)
        {
            case "surfaceBase": colors.SurfaceBase = value; break;
            case "surfaceRaised": colors.SurfaceRaised = value; break;
            case "surfaceOverlay": colors.SurfaceOverlay = value; break;
            case "surfaceSunken": colors.SurfaceSunken = value; break;
            case "surfaceHighest": colors.SurfaceHighest = value; break;
            case "borderSubtle": colors.BorderSubtle = value; break;
            case "borderDefault": colors.BorderDefault = value; break;
            case "borderEmphasis": colors.BorderEmphasis = value; break;
            case "textPrimary": colors.TextPrimary = value; break;
            case "textMuted": colors.TextMuted = value; break;
            case "textFaint": colors.TextFaint = value; break;
            case "textInverse": colors.TextInverse = value; break;
            case "textAccent": colors.TextAccent = value; break;
            case "accentDefault": colors.AccentDefault = value; break;
            case "accentHover": colors.AccentHover = value; break;
            case "accentPressed": colors.AccentPressed = value; break;
            case "accentSubtle": colors.AccentSubtle = value; break;
            case "accentGlow": colors.AccentGlow = value; break;
            case "statusLive": colors.StatusLive = value; break;
            case "statusSuccess": colors.StatusSuccess = value; break;
            case "statusWarning": colors.StatusWarning = value; break;
            case "statusDanger": colors.StatusDanger = value; break;
            case "statusInfo": colors.StatusInfo = value; break;
        }
    }

    /// <summary>Round-trips through JSON. Fewer moving parts than a hand-written deep copy.</summary>
    private static ThemeDocument Clone(ThemeDocument source)
    {
        string json = ThemeLoader.Serialise(source);
        ThemeLoadResult result = ThemeLoader.Parse(json, source.SourcePath);

        ThemeDocument clone = result.Document ?? new ThemeDocument { Id = source.Id };
        clone.IsBuiltIn = false;
        return clone;
    }

    private void OnRepositoryChanged(object? sender, ThemesChangedEventArgs e)
    {
        // A file changed underneath us. Refresh the list, but do not stamp on an open editor.
        if (IsEditing)
        {
            StatusMessage = "A theme file changed on disk while you were editing.";
            return;
        }

        RefreshList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _themes.Repository.ThemesChanged -= OnRepositoryChanged;
    }
}
