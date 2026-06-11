using System.Text.RegularExpressions;

namespace EFiling.Tests;

/// <summary>
/// Plan §8 Bar 5 enforcement — <c>EFiling.Core</c> must be provider-agnostic.
///
/// <para>
/// <b>What this test asserts.</b> No <c>EFiling.Core/**/*.cs</c> file contains a code-level
/// reference to JTI-specific types or SOAP operation names. Specifically we flag:
/// </para>
/// <list type="bullet">
///   <item><c>Jti</c>-prefixed class / namespace identifiers (e.g., <c>JtiEFilingProvider</c>, <c>JtiUrlConvention</c>)</item>
///   <item><c>ReviewFilingRequestMessage</c>, <c>ReviewFilingXmlBuilder</c> (JTI SOAP operation/type names)</item>
///   <item><c>DocValueMetaDataItemType</c> (JTI-specific XSD type)</item>
///   <item><c>using EFiling.Providers.</c> (Core must not depend on a provider)</item>
/// </list>
///
/// <para>
/// <b>What this test allows.</b> Comment-only mentions of "JTI" (e.g., xml-doc saying
/// <c>Maps to JTI's PaymentMessageTypeExt</c>) — these are informational and don't violate the bar.
/// Lines whose trim starts with <c>///</c>, <c>//</c>, or <c>*</c> are treated as comments.
/// </para>
///
/// <para>
/// <b>Known pending-relocation whitelist.</b> Two files currently violate Bar 5 and
/// are tracked under a dedicated residual for relocation to <c>EFiling.Providers.JTI</c>:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Validation/JtiUrlConvention.cs</c> — JTI-specific URL-pattern heuristic. Pending move
///     to <c>EFiling.Providers.JTI.Validation</c>. Tracked as PROGRESS.md residual "H2-followup".
///   </item>
///   <item>
///     <c>Validation/CourtConfigurationValidator.cs</c> line invoking <c>JtiUrlConvention.InferFromUrl</c>.
///     Pending abstraction via an <c>IUrlEnvironmentConvention</c> interface so providers plug in their
///     own URL heuristics. Tracked as same residual.
///   </item>
/// </list>
///
/// <para>
/// The whitelist is deliberately narrow — only the specific existing offenders pass. Any NEW
/// code-level JTI reference in Core fails the test.
/// </para>
/// </summary>
public class EFilingCoreProviderAgnosticTests
{
    /// <summary>Marker sentinel to locate the repo root (same strategy as <c>SampleLoader</c>).</summary>
    private const string RepoRootSentinel = "docs/fileing files";

    /// <summary>Relative path from repo root to the <c>EFiling.Core</c> source directory.</summary>
    private const string EFilingCoreRelative = "src/EFiling/EFiling.Core";

    /// <summary>
    /// Known pre-existing Bar 5 violations pending relocation. Each entry is a
    /// forward-slash relative path from <c>EFiling.Core/</c>. New entries require
    /// explicit audit — do NOT add without a corresponding PROGRESS.md residual.
    /// </summary>
    private static readonly HashSet<string> KnownPendingRelocation = new(StringComparer.OrdinalIgnoreCase)
    {
        "Validation/JtiUrlConvention.cs",
        "Validation/CourtConfigurationValidator.cs",
    };

    /// <summary>Regex patterns that, if matched on a non-comment line, indicate a Bar 5 violation.</summary>
    private static readonly (string Label, Regex Pattern)[] ViolationPatterns =
    {
        ("Jti-prefixed identifier",         new Regex(@"\bJti[A-Z]\w*",                     RegexOptions.Compiled)),
        ("JTI SOAP type ReviewFiling*",     new Regex(@"\bReviewFiling\w+",                 RegexOptions.Compiled)),
        ("JTI XSD type DocValueMetaData",   new Regex(@"\bDocValueMetaDataItemType\b",      RegexOptions.Compiled)),
        ("Core-to-Provider dependency",     new Regex(@"\busing\s+EFiling\.Providers\.",    RegexOptions.Compiled)),
    };

    [Fact]
    public void EFilingCore_ContainsNoJtiSpecificCodeReferences()
    {
        var coreDir = Path.Combine(FindRepoRoot(), EFilingCoreRelative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(coreDir),
            $"EFiling.Core source directory not found at {coreDir}. Update {nameof(EFilingCoreRelative)} if layout changed.");

        var violations = new List<string>();

        foreach (var absPath in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(coreDir, absPath).Replace('\\', '/');

            // Skip build output.
            if (relPath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                relPath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip the whitelisted pending-relocation files in their entirety.
            if (KnownPendingRelocation.Contains(relPath))
                continue;

            var lines = File.ReadAllLines(absPath);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comment-only lines (xml-doc, single-line, continuation of block comment).
                if (trimmed.StartsWith("///", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//",  StringComparison.Ordinal) ||
                    trimmed.StartsWith("*",   StringComparison.Ordinal))
                    continue;

                foreach (var (label, pattern) in ViolationPatterns)
                {
                    if (pattern.IsMatch(line))
                    {
                        violations.Add($"{relPath}:{i + 1}  [{label}]  {line.Trim()}");
                        break; // one label per line is enough
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Plan §8 Bar 5 violation — EFiling.Core must not contain code-level JTI-specific references.\n" +
            $"If a new violation is legitimate (e.g., a new plug-in abstraction), update {nameof(KnownPendingRelocation)} AND add a PROGRESS.md residual.\n\n" +
            $"Violations ({violations.Count}):\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void KnownPendingRelocationList_MatchesActualFileLocations()
    {
        // Guard against a partial relocation silently leaving dead whitelist entries.
        var coreDir = Path.Combine(FindRepoRoot(), EFilingCoreRelative.Replace('/', Path.DirectorySeparatorChar));
        var stale = new List<string>();

        foreach (var relPath in KnownPendingRelocation)
        {
            var abs = Path.Combine(coreDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs))
                stale.Add(relPath);
        }

        Assert.True(stale.Count == 0,
            $"KnownPendingRelocation contains entries that no longer exist on disk. " +
            $"If these files were relocated, remove them from the whitelist.\n" +
            $"Stale entries:\n  {string.Join("\n  ", stale)}");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var sentinel = Path.Combine(dir, RepoRootSentinel.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(sentinel))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root via sentinel '{RepoRootSentinel}' starting from {AppContext.BaseDirectory}.");
    }
}
