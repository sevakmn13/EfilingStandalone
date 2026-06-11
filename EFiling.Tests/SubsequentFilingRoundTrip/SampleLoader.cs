using System.Xml;
using System.Xml.Linq;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Loads canonical JTI sample XMLs from the repo's <c>docs/fileing files/</c> tree.
///
/// <para>
/// Tests run from <c>src/EFiling/EFiling.Tests/bin/Debug/net9.0/</c>. To find the canonical
/// samples (which live in <c>docs/fileing files/</c> at the repo root), we walk the directory
/// tree upward from the test binary location looking for a sentinel directory that proves
/// we've reached the repo root.
/// </para>
///
/// <para>
/// This mirrors the pattern used by <see cref="TestConfiguration"/> for locating
/// <c>testsettings.json</c>. The discovered repo root is cached to avoid re-walking on every call.
/// </para>
/// </summary>
public static class SampleLoader
{
    /// <summary>
    /// Marker file we expect to find in the repo root. Using <c>Pi-Pressure.sln</c> or similar
    /// would be brittle; the <c>docs/fileing files</c> directory itself is the domain-relevant
    /// marker — if that directory is gone, this entire test module has nothing to load.
    /// </summary>
    private const string RepoRootSentinel = "docs/fileing files";

    private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);

    /// <summary>Absolute path to the repo root, discovered by walking up from the test bin dir.</summary>
    public static string RepoRoot => _repoRoot.Value;

    /// <summary>Absolute path to the canonical baseline sample root directory.</summary>
    public static string BaselineRootAbsolute =>
        Path.Combine(RepoRoot, CanonicalScenarios.BaselineRoot.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Absolute path to the sample file for the given scenario.</summary>
    public static string GetAbsolutePath(CanonicalScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return Path.Combine(BaselineRootAbsolute, scenario.RelativePath);
    }

    /// <summary>Absolute path to the sample file for the given scenario ID.</summary>
    public static string GetAbsolutePath(string scenarioId) =>
        GetAbsolutePath(CanonicalScenarios.GetById(scenarioId));

    /// <summary>
    /// Load the raw XML text of the canonical sample for the given scenario.
    /// Throws with a detailed diagnostic if the file is missing (indicates either a
    /// broken scenario registry or the sample set has drifted on disk).
    /// </summary>
    public static string LoadXmlText(CanonicalScenario scenario)
    {
        var path = GetAbsolutePath(scenario);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Canonical sample '{scenario.Id}' ({scenario.Description}) not found at expected path.\n"
                + $"Expected path: {path}\n"
                + $"Relative path in registry: {scenario.RelativePath}\n"
                + $"Baseline root resolved to: {BaselineRootAbsolute}\n"
                + $"Repo root resolved to: {RepoRoot}\n"
                + $"If the sample set has been restructured, update CanonicalScenarios.cs.",
                path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>Load and parse the canonical sample as an <see cref="XDocument"/>.</summary>
    public static XDocument LoadXDocument(CanonicalScenario scenario)
    {
        var xml = LoadXmlText(scenario);
        try
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                $"Canonical sample '{scenario.Id}' at '{GetAbsolutePath(scenario)}' is not well-formed XML: {ex.Message}",
                ex);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (string.IsNullOrEmpty(dir)) break;
            var sentinel = Path.Combine(dir, RepoRootSentinel.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(sentinel))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }

        throw new DirectoryNotFoundException(
            $"Could not find repo root by walking up from '{AppContext.BaseDirectory}'. "
            + $"Expected to find sentinel directory '{RepoRootSentinel}' within 10 levels up. "
            + $"This test module requires the canonical JTI sample set to be present in the repo.");
    }
}
