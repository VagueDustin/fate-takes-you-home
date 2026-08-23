using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Loading;

/// <summary>Where a theme came from.</summary>
public enum ThemeOrigin
{
    /// <summary>Shipped with the application. Read-only; restored if deleted.</summary>
    BuiltIn,

    /// <summary>Written by the user into their themes folder. Editable and hot-reloadable.</summary>
    User,
}

/// <summary>One theme known to the repository, with everything needed to list and edit it.</summary>
public sealed class ThemeEntry
{
    public required string Id { get; init; }

    public required ThemeDocument Document { get; init; }

    public required ThemeOrigin Origin { get; init; }

    public required ThemeValidationResult Validation { get; init; }

    public string? SourcePath => Document.SourcePath;

    public string DisplayName => Document.Name ?? Id;

    public bool IsEditable => Origin == ThemeOrigin.User;
}

/// <summary>Raised when the set of themes on disk changed.</summary>
public sealed class ThemesChangedEventArgs : EventArgs
{
    /// <summary>The id that changed, when a single file triggered the reload.</summary>
    public string? ChangedThemeId { get; init; }
}

/// <summary>
/// Discovers, watches, and persists themes.
/// </summary>
/// <remarks>
/// <para>
/// Two sources are merged: the read-only themes shipped beside the executable, and the user's own
/// folder under <c>%APPDATA%</c>. A user theme with the same id as a built-in one shadows it, which
/// is how somebody customises FATE without losing the ability to get it back — deleting their file
/// restores the original.
/// </para>
/// <para>
/// The user folder is watched, and edits are picked up live. Writes are debounced because a single
/// save from an editor typically produces three or four filesystem events.
/// </para>
/// </remarks>
public sealed class ThemeRepository : IDisposable
{
    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentDictionary<string, ThemeEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Action<string, Exception?>? _log;
    private readonly DispatcherTimer? _debounce;
    private FileSystemWatcher? _watcher;
    private string? _pendingChangedId;
    private bool _disposed;

    public ThemeRepository(
        string builtInDirectory,
        string userDirectory,
        Action<string, Exception?>? log = null,
        Dispatcher? dispatcher = null)
    {
        BuiltInDirectory = builtInDirectory;
        UserDirectory = userDirectory;
        _log = log;

        // Hot reload marshals onto the UI thread, because applying a theme touches WPF resources.
        Dispatcher target = dispatcher ?? Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, target)
        {
            Interval = ReloadDebounce,
        };
        _debounce.Tick += OnDebounceElapsed;
    }

    /// <summary>Themes compiled into the install. Read-only.</summary>
    public string BuiltInDirectory { get; }

    /// <summary>The user's own themes folder.</summary>
    public string UserDirectory { get; }

    /// <summary>Raised after a hot reload changed the set of available themes.</summary>
    public event EventHandler<ThemesChangedEventArgs>? ThemesChanged;

    public IReadOnlyCollection<ThemeEntry> Entries => _entries.Values.ToArray();

    /// <summary>All themes, built-ins first, then the user's, each alphabetical.</summary>
    public IReadOnlyList<ThemeEntry> ListForDisplay() =>
        _entries.Values
            .OrderBy(e => e.Origin == ThemeOrigin.User)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    /// <summary>Scans both directories. Safe to call repeatedly.</summary>
    public void Reload()
    {
        var found = new Dictionary<string, ThemeEntry>(StringComparer.OrdinalIgnoreCase);

        LoadDirectory(BuiltInDirectory, ThemeOrigin.BuiltIn, found);

        // Loaded second so a user theme shadows a built-in of the same id.
        LoadDirectory(UserDirectory, ThemeOrigin.User, found);

        // The compiled FATE baseline is always available, even with no files on disk at all.
        if (!found.ContainsKey(ThemeDefaults.FateThemeId))
        {
            ThemeDocument fallback = BuildCompiledFallback();
            found[ThemeDefaults.FateThemeId] = new ThemeEntry
            {
                Id = ThemeDefaults.FateThemeId,
                Document = fallback,
                Origin = ThemeOrigin.BuiltIn,
                Validation = ThemeValidationResult.Ok,
            };
        }

        _entries.Clear();
        foreach (KeyValuePair<string, ThemeEntry> pair in found)
        {
            _entries[pair.Key] = pair.Value;
        }
    }

    /// <summary>Starts watching the user folder for edits.</summary>
    public void StartWatching()
    {
        if (_watcher is not null || _disposed)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(UserDirectory);

            _watcher = new FileSystemWatcher(UserDirectory, "*" + ThemeLoader.FileExtension)
            {
                NotifyFilter = NotifyFilters.LastWrite
                             | NotifyFilters.FileName
                             | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Live editing is a convenience. Losing it should not stop the app from running.
            _log?.Invoke("Could not watch the themes folder; hot reload is unavailable.", ex);
        }
    }

    public void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileEvent;
        _watcher.Created -= OnFileEvent;
        _watcher.Deleted -= OnFileEvent;
        _watcher.Renamed -= OnFileEvent;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    public ThemeEntry? Find(string id) =>
        id is not null && _entries.TryGetValue(id, out ThemeEntry? entry) ? entry : null;

    /// <summary>
    /// Resolves a theme by id, collapsing its inheritance. Falls back to FATE when the id is
    /// unknown, so a deleted theme leaves the app looking right rather than blank.
    /// </summary>
    public Theme Resolve(string? id)
    {
        ThemeEntry? entry = id is null ? null : Find(id);

        entry ??= Find(ThemeDefaults.FateThemeId);

        if (entry is null)
        {
            return ThemeResolver.Resolve(BuildCompiledFallback());
        }

        return ThemeResolver.Resolve(entry.Document, LookupDocument);
    }

    /// <summary>Resolves a document that is not in the repository — used for live editor previews.</summary>
    public Theme ResolveDraft(ThemeDocument draft) =>
        ThemeResolver.Resolve(draft, LookupDocument);

    /// <summary>Writes a theme into the user folder, creating or replacing it.</summary>
    public string SaveUserTheme(ThemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new InvalidOperationException("A theme needs an id before it can be saved.");
        }

        Directory.CreateDirectory(UserDirectory);
        string path = Path.Combine(UserDirectory, document.Id + ThemeLoader.FileExtension);

        ThemeLoader.Save(document, path);

        document.SourcePath = path;
        document.IsBuiltIn = false;

        _entries[document.Id] = new ThemeEntry
        {
            Id = document.Id,
            Document = document,
            Origin = ThemeOrigin.User,
            Validation = ThemeValidator.ValidateDocument(document),
        };

        return path;
    }

    /// <summary>
    /// Deletes a user theme. Built-ins cannot be deleted; a user theme shadowing a built-in reverts
    /// to the built-in rather than disappearing.
    /// </summary>
    public bool DeleteUserTheme(string id)
    {
        ThemeEntry? entry = Find(id);

        if (entry is null || entry.Origin != ThemeOrigin.User || entry.SourcePath is null)
        {
            return false;
        }

        try
        {
            File.Delete(entry.SourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Could not delete the theme file for '{id}'.", ex);
            return false;
        }

        Reload();
        return true;
    }

    /// <summary>Copies a theme file out to somewhere the user chose.</summary>
    public void Export(string id, string destinationPath)
    {
        ThemeEntry entry = Find(id)
            ?? throw new InvalidOperationException($"No theme with id '{id}'.");

        ThemeLoader.Save(entry.Document, destinationPath);
    }

    /// <summary>
    /// Copies a theme file in. The id is made unique first, so importing something that clashes
    /// with an existing theme adds it rather than silently overwriting.
    /// </summary>
    public ThemeLoadResult Import(string sourcePath)
    {
        ThemeLoadResult result = ThemeLoader.Load(sourcePath);

        if (!result.Succeeded || result.Document is null)
        {
            return result;
        }

        ThemeDocument document = result.Document;
        document.Id = MakeUniqueId(document.Id ?? "imported-theme");
        document.IsBuiltIn = false;

        SaveUserTheme(document);
        return result;
    }

    /// <summary>Appends a numeric suffix until the id is free.</summary>
    public string MakeUniqueId(string desired)
    {
        string basis = Sanitise(desired);

        if (!_entries.ContainsKey(basis))
        {
            return basis;
        }

        for (int n = 2; n < 1000; n++)
        {
            string candidate = $"{basis}-{n}";
            if (!_entries.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return $"{basis}-{Guid.NewGuid():N}"[..40];
    }

    /// <summary>Turns arbitrary text into a valid kebab-case theme id.</summary>
    public static string Sanitise(string text)
    {
        Span<char> buffer = stackalloc char[Math.Min(text.Length, 64)];
        int length = 0;
        bool lastWasHyphen = true; // Suppresses a leading hyphen.

        foreach (char raw in text)
        {
            if (length == buffer.Length)
            {
                break;
            }

            char c = char.ToLowerInvariant(raw);

            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                buffer[length++] = c;
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                buffer[length++] = '-';
                lastWasHyphen = true;
            }
        }

        while (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return length == 0 ? "theme" : new string(buffer[..length]);
    }

    // ------------------------------------------------------------------ internals

    private ThemeDocument? LookupDocument(string id) => Find(id)?.Document;

    private void LoadDirectory(
        string directory, ThemeOrigin origin, Dictionary<string, ThemeEntry> into)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*" + ThemeLoader.FileExtension);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Could not list themes in '{directory}'.", ex);
            return;
        }

        foreach (string file in files)
        {
            // The schema lives alongside the themes but is not one.
            if (Path.GetFileName(file).Equals("theme.schema.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ThemeLoadResult result = ThemeLoader.Load(file, origin == ThemeOrigin.BuiltIn);

            if (result.Document is null || string.IsNullOrWhiteSpace(result.Document.Id))
            {
                _log?.Invoke(
                    $"Skipped '{Path.GetFileName(file)}': "
                    + string.Join("; ", result.Validation.Errors.Select(e => e.Message)),
                    null);
                continue;
            }

            if (!result.Validation.IsValid)
            {
                // Keep it listed so the editor can show the user what is wrong with their file.
                _log?.Invoke(
                    $"Theme '{result.Document.Id}' has errors: "
                    + string.Join("; ", result.Validation.Errors.Select(e => e.Message)),
                    null);
            }

            into[result.Document.Id] = new ThemeEntry
            {
                Id = result.Document.Id,
                Document = result.Document,
                Origin = origin,
                Validation = result.Validation,
            };
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // An editor save fires several events; collapse them into one reload.
        _pendingChangedId = Path.GetFileNameWithoutExtension(e.Name ?? string.Empty);

        _debounce?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || _debounce is null)
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The watcher's internal buffer overflowed, or the folder went away. Rebuild it.
        _log?.Invoke("The theme watcher failed; restarting it.", e.GetException());

        StopWatching();
        StartWatching();
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce?.Stop();

        if (_disposed)
        {
            return;
        }

        string? changed = _pendingChangedId;
        _pendingChangedId = null;

        try
        {
            Reload();
        }
        catch (Exception ex)
        {
            _log?.Invoke("Reloading themes failed.", ex);
            return;
        }

        ThemesChanged?.Invoke(this, new ThemesChangedEventArgs { ChangedThemeId = changed });
    }

    /// <summary>
    /// The FATE theme built from compiled constants, for when no theme files exist at all.
    /// </summary>
    private static ThemeDocument BuildCompiledFallback() => new()
    {
        Id = ThemeDefaults.FateThemeId,
        Name = "FATE",
        Author = "VagueDustin Enterprises",
        Version = "1.0.0",
        Description = "Navy and gold. Inscribed, not printed.",
        Tier = OrnamentTier.Charted,
        Appearance = ThemeAppearance.Dark,
        IsBuiltIn = true,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopWatching();

        if (_debounce is not null)
        {
            _debounce.Stop();
            _debounce.Tick -= OnDebounceElapsed;
        }
    }
}
