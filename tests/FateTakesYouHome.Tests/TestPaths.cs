using System.IO;

namespace FateTakesYouHome.Tests;

/// <summary>
/// Locates repository files from the test output directory.
/// </summary>
/// <remarks>
/// The themes are read from <c>themes/</c> in the repository rather than from a copy in the test
/// output. Testing a copy would prove the copy is valid and say nothing about what actually ships.
/// </remarks>
internal static class TestPaths
{
    /// <summary>The repository root, found by walking up to the solution file.</summary>
    public static string RepositoryRoot { get; } = Locate();

    public static string Themes => Path.Combine(RepositoryRoot, "themes");

    public static string Theme(string fileName) => Path.Combine(Themes, fileName);

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FateTakesYouHome.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the repository root above '{AppContext.BaseDirectory}'.");
    }
}
