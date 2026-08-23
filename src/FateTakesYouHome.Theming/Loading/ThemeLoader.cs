using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Loading;

/// <summary>The result of reading one theme file.</summary>
public sealed class ThemeLoadResult
{
    public ThemeDocument? Document { get; init; }

    public required ThemeValidationResult Validation { get; init; }

    public string? SourcePath { get; init; }

    /// <summary>True when a usable document came out, even if it carried warnings.</summary>
    public bool Succeeded => Document is not null && Validation.IsValid;
}

/// <summary>
/// Reads and writes theme files.
/// </summary>
/// <remarks>
/// The parser deliberately accepts comments and trailing commas. Theme files are hand-authored, and
/// rejecting a file because somebody left a trailing comma after the last colour is a hostile way
/// to treat the person extending your app.
/// </remarks>
public static class ThemeLoader
{
    public const string FileExtension = ".json";

    /// <summary>Options for reading. Lenient, because humans write these files.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Options for writing. Indented and readable, because humans then edit them.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses theme JSON that is already in memory.</summary>
    public static ThemeLoadResult Parse(string json, string? sourcePath = null, bool isBuiltIn = false)
    {
        ThemeDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ThemeDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new ThemeLoadResult
            {
                SourcePath = sourcePath,
                Validation = new ThemeValidationResult
                {
                    Diagnostics =
                    [
                        new ThemeDiagnostic(
                            ThemeDiagnosticSeverity.Error,
                            ex.Path ?? "(document)",
                            FormatJsonError(ex)),
                    ],
                },
            };
        }

        if (document is null)
        {
            return new ThemeLoadResult
            {
                SourcePath = sourcePath,
                Validation = new ThemeValidationResult
                {
                    Diagnostics =
                    [
                        new ThemeDiagnostic(
                            ThemeDiagnosticSeverity.Error, "(document)", "The file is empty."),
                    ],
                },
            };
        }

        document.SourcePath = sourcePath;
        document.IsBuiltIn = isBuiltIn;

        // A file named midnight-brass.json that forgot its id still has an obvious id.
        if (string.IsNullOrWhiteSpace(document.Id) && sourcePath is not null)
        {
            document.Id = Path.GetFileNameWithoutExtension(sourcePath).ToLowerInvariant();
        }

        return new ThemeLoadResult
        {
            Document = document,
            SourcePath = sourcePath,
            Validation = ThemeValidator.ValidateDocument(document),
        };
    }

    /// <summary>Reads a theme from disk.</summary>
    public static ThemeLoadResult Load(string path, bool isBuiltIn = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return Parse(File.ReadAllText(path), path, isBuiltIn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ThemeLoadResult
            {
                SourcePath = path,
                Validation = new ThemeValidationResult
                {
                    Diagnostics =
                    [
                        new ThemeDiagnostic(
                            ThemeDiagnosticSeverity.Error, "(file)", $"Could not read the file: {ex.Message}"),
                    ],
                },
            };
        }
    }

    /// <summary>Serialises a theme document, with the schema reference restored.</summary>
    public static string Serialise(ThemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Schema ??= SchemaReference;
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    /// <summary>Writes a theme to disk atomically, so a crash cannot leave a half-written file.</summary>
    public static void Save(ThemeDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialise(document));

        // File.Move with overwrite is atomic enough on NTFS for our purposes, and unlike
        // File.Replace it does not fail when the destination does not exist yet.
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The published JSON Schema, referenced by generated theme files for editor support.</summary>
    public const string SchemaReference =
        "https://raw.githubusercontent.com/VagueDustin/fate-takes-you-home/main/themes/theme.schema.json";

    private static string FormatJsonError(JsonException ex)
    {
        string where = ex.LineNumber is { } line
            ? $"Line {line + 1}, position {ex.BytePositionInLine + 1}: "
            : string.Empty;

        // The framework message is accurate but mentions internal paths. Trim to the useful clause.
        string message = ex.Message;
        int marker = message.IndexOf(" Path: ", StringComparison.Ordinal);
        if (marker > 0)
        {
            message = message[..marker];
        }

        return where + message;
    }
}
