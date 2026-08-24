using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Models;

namespace FateTakesYouHome.Services;

/// <summary>
/// Loads, holds and persists <see cref="AppSettings"/>.
/// </summary>
/// <remarks>
/// Saves are debounced and written atomically. Dragging a brightness slider produces a change per
/// frame; writing the file on each one would hammer the disk and leave a window where a crash
/// truncates the file.
/// </remarks>
public sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(600);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppLog _log;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _saveTimer;

    private bool _savePending;
    private bool _disposed;

    public SettingsService(AppLog log, string? path = null)
    {
        _log = log;
        _path = path ?? AppPaths.SettingsFile;

        _saveTimer = new System.Threading.Timer(_ => FlushIfPending(), null, Timeout.Infinite, Timeout.Infinite);

        Current = Load();
    }

    /// <summary>The live settings object. Mutate it, then call <see cref="Save"/>.</summary>
    public AppSettings Current { get; private set; }

    public string FilePath => _path;

    /// <summary>Raised after settings are replaced or reloaded from disk.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The most recent save failure, or null while saves are landing. Raised into the UI as a
    /// banner: a settings file that silently cannot be written means every pin, theme choice and
    /// switch flip is quietly lost on exit, which is far worse than an ugly warning.
    /// </summary>
    public string? LastSaveError { get; private set; }

    /// <summary>Raised (on a worker thread) whenever <see cref="LastSaveError"/> changes.</summary>
    public event EventHandler? SaveStateChanged;

    /// <summary>Queues a debounced write.</summary>
    public void Save()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _savePending = true;
        }

        _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes immediately, bypassing the debounce. Used on shutdown.</summary>
    public void SaveNow()
    {
        lock (_gate)
        {
            _savePending = false;
        }

        WriteToDisk(Current);
    }

    /// <summary>Replaces the whole settings object, for example when the settings page commits.</summary>
    public void Replace(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Normalise(settings);
        Current = settings;

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Announces that <see cref="Current"/> was mutated in place, so views rebuild. Used by the
    /// layout editor, whose save is a mutation rather than a replacement.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Decrypts the stored token. Returns null when there is not a usable one.</summary>
    public string? GetAccessToken() => SecretProtector.Unprotect(Current.ProtectedToken);

    /// <summary>Encrypts and stores a token. Passing null or blank clears it.</summary>
    public void SetAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Current.ProtectedToken = null;
            Save();
            return;
        }

        string? protectedToken = SecretProtector.Protect(token.Trim());

        if (protectedToken is null)
        {
            // Better to hold nothing than to hold a token we could not encrypt.
            _log.Error(
                "Windows data protection is unavailable, so the access token was not saved. "
                + "It will need to be entered again next time the app starts.");
        }

        Current.ProtectedToken = protectedToken;
        Save();
    }

    /// <summary>Builds connection options from the stored settings, or null when unconfigured.</summary>
    public HaConnectionOptions? BuildConnectionOptions()
    {
        string? token = GetAccessToken();

        if (string.IsNullOrWhiteSpace(Current.ServerUrl) || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return new HaConnectionOptions
        {
            BaseUrl = Current.ServerUrl.Trim(),
            AccessToken = token,
            AllowInvalidCertificate = Current.AllowInvalidCertificate,
        };
    }

    // ------------------------------------------------------------------ persistence

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            _log.Info("No settings file yet; starting with defaults.");
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(_path);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

            if (loaded is null)
            {
                _log.Warning("The settings file was empty; using defaults.");
                return new AppSettings();
            }

            Normalise(loaded);
            _log.Info($"Loaded settings from {_path}.");
            return loaded;
        }
        catch (JsonException ex)
        {
            // Keep the broken file rather than overwriting it — it may be the only copy of
            // somebody's pin list, and it can be repaired by hand.
            string quarantine = _path + ".invalid";

            try
            {
                File.Copy(_path, quarantine, overwrite: true);
                _log.Error(
                    $"The settings file could not be parsed and was copied to {quarantine}. "
                    + "Starting with defaults.", ex);
            }
            catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
            {
                _log.Error("The settings file could not be parsed and could not be backed up.", ex);
            }

            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("The settings file could not be read. Starting with defaults.", ex);
            return new AppSettings();
        }
    }

    private void FlushIfPending()
    {
        bool shouldWrite;

        lock (_gate)
        {
            shouldWrite = _savePending;
            _savePending = false;
        }

        if (shouldWrite)
        {
            WriteToDisk(Current);
        }
    }

    private void WriteToDisk(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string json = JsonSerializer.Serialize(settings, SerializerOptions);

            // Write to a sibling then move over the top, so a crash mid-write cannot leave a
            // half-written settings file behind.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);

            if (LastSaveError is not null)
            {
                LastSaveError = null;
                _log.Info("Settings saves are landing again.");
                SaveStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bool firstFailure = LastSaveError is null;
            LastSaveError = ex.Message;
            _log.Error("Could not save settings.", ex);

            if (firstFailure)
            {
                SaveStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Repairs anything a hand-edited or older file might have got wrong.</summary>
    private static void Normalise(AppSettings settings)
    {
        settings.Pinned ??= [];

        // Drop pins with no entity id, and collapse duplicates keeping the first.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        settings.Pinned = settings.Pinned
            .Where(p => !string.IsNullOrWhiteSpace(p.EntityId))
            .Where(p => seen.Add(p.EntityId))
            .ToList();

        // At most one default action; the tray gesture has to be unambiguous.
        bool foundDefault = false;
        foreach (PinnedEntity pin in settings.Pinned)
        {
            if (!pin.IsDefaultAction)
            {
                continue;
            }

            if (foundDefault)
            {
                pin.IsDefaultAction = false;
            }

            foundDefault = true;
        }

        if (string.IsNullOrWhiteSpace(settings.ThemeId))
        {
            settings.ThemeId = "fate";
        }

        settings.ServerUrl = settings.ServerUrl?.Trim();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        FlushIfPending();
        _saveTimer.Dispose();
    }
}
