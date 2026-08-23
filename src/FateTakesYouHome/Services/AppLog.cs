using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace FateTakesYouHome.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>One line in the log, also surfaced in the diagnostics page.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message, string? Detail)
{
    public string Format() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant(),-7}] {Message}")
        + (Detail is null ? string.Empty : Environment.NewLine + Indent(Detail));

    private static string Indent(string text) =>
        string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => "    " + line.TrimEnd('\r')));
}

/// <summary>
/// A small rolling file log, with an in-memory tail for the diagnostics page.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately hand-rolled rather than pulling in a logging framework. The requirements are one
/// file, one rotation rule and a ring buffer; a framework would be more code to configure than
/// this is to write, and it would ship another assembly in the installer.
/// </para>
/// <para>
/// Writes are queued and flushed on a background thread so that a slow or locked disk cannot stall
/// the UI thread mid-animation.
/// </para>
/// </remarks>
public sealed class AppLog : IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxArchivedFiles = 3;
    private const int MemoryTailSize = 500;

    private readonly BlockingCollection<LogEntry> _queue = new(boundedCapacity: 4096);
    private readonly ConcurrentQueue<LogEntry> _tail = new();
    private readonly Thread _writer;
    private readonly string _path;
    private bool _disposed;

    public AppLog(string? directory = null)
    {
        string folder = directory ?? AppPaths.LogsDirectory;
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "fate-takes-you-home.log");

        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "FateTakesYouHome.Log",
            Priority = ThreadPriority.BelowNormal,
        };
        _writer.Start();
    }

    /// <summary>The minimum level actually written. Raised to Debug by the diagnostics page.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public string FilePath => _path;

    /// <summary>Raised for every accepted entry, so the diagnostics view can update live.</summary>
    public event EventHandler<LogEntry>? EntryWritten;

    public void Debug(string message, Exception? error = null) => Write(LogLevel.Debug, message, error);

    public void Info(string message, Exception? error = null) => Write(LogLevel.Info, message, error);

    public void Warning(string message, Exception? error = null) => Write(LogLevel.Warning, message, error);

    public void Error(string message, Exception? error = null) => Write(LogLevel.Error, message, error);

    /// <summary>Adapter matching the callback shape the library projects expect.</summary>
    public Action<string, Exception?> AsCallback(LogLevel level = LogLevel.Info) =>
        (message, error) => Write(error is null ? level : LogLevel.Warning, message, error);

    public void Write(LogLevel level, string message, Exception? error = null)
    {
        if (_disposed || level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, message, error?.ToString());

        _tail.Enqueue(entry);
        while (_tail.Count > MemoryTailSize && _tail.TryDequeue(out _))
        {
            // Trim to the ring size.
        }

        EntryWritten?.Invoke(this, entry);

        // Never block the caller. Dropping a log line is strictly better than stuttering the UI.
        _queue.TryAdd(entry);
    }

    /// <summary>The most recent entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Tail() => _tail.ToArray();

    private void WriteLoop()
    {
        var builder = new StringBuilder();

        try
        {
            foreach (LogEntry entry in _queue.GetConsumingEnumerable())
            {
                builder.Clear();
                builder.AppendLine(entry.Format());

                // Drain anything else already queued so a burst becomes one write.
                while (builder.Length < 32 * 1024 && _queue.TryTake(out LogEntry? extra))
                {
                    builder.AppendLine(extra.Format());
                }

                TryAppend(builder.ToString());
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
        catch (InvalidOperationException)
        {
            // The collection was completed while enumerating. Also shutdown.
        }
    }

    private void TryAppend(string text)
    {
        try
        {
            RollIfOversized();
            File.AppendAllText(_path, text, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot write must not become the reason the app fails.
        }
    }

    private void RollIfOversized()
    {
        var file = new FileInfo(_path);

        if (!file.Exists || file.Length < MaxFileBytes)
        {
            return;
        }

        // Shift .2 to .3, .1 to .2, current to .1, dropping whatever falls off the end.
        for (int index = MaxArchivedFiles - 1; index >= 1; index--)
        {
            string from = $"{_path}.{index}";
            string to = $"{_path}.{index + 1}";

            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _queue.CompleteAdding();

        // Give the writer a moment to flush, but never hang shutdown on it.
        _writer.Join(TimeSpan.FromSeconds(2));

        _queue.Dispose();
    }
}
