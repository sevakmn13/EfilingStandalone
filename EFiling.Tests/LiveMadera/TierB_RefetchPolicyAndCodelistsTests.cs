using System.Text;
using EFiling.Core.Caching;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Rest;
using EFiling.Tests.SubsequentFilingRoundTrip;
using Xunit;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// CC-audit C0.2 source-currency re-fetch (read-only). Pulls FRESH <c>GetPolicy</c> +
/// key codelists from live Madera staging and diffs them against the ~April-2026
/// snapshots under <c>docs/fileing files/madera_*.xml</c>.
///
/// <para>
/// <b>Why.</b> Codelists + policy are RUNTIME court data that drift over time (courts
/// add/retire categories, doc types, party types, fee rules). The audit must not assume
/// the April snapshots are current — see <c>docs/CASE_INITIATION_E2E_AUDIT.md</c> §1b /
/// Finding F-SRC1. The WSDL cannot substitute (it carries structure, not codelist values).
/// </para>
///
/// <para>
/// <b>Safety.</b> Read-only: <c>GetPolicy</c> + codelist GETs only — never a submission.
/// Opt-in via <c>EFILING_LIVE_MADERA=1</c> (see <see cref="LiveMaderaFactAttribute"/>).
/// Output (fresh dumps + <c>DIFF_SUMMARY.txt</c>) goes to
/// <c>temp/refetch-codelists-2026-06-04/</c> so the repo's April evidence is untouched
/// until a real delta is confirmed.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class TierB_RefetchPolicyAndCodelistsTests
{
    private readonly ITestOutputHelper _output;

    public TierB_RefetchPolicyAndCodelistsTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Codelist types whose April snapshot exists under <c>docs/fileing files/</c>
    /// (file name <c>madera_&lt;TYPE&gt;.xml</c>). The loop also reports any policy
    /// codelist type that has NO April snapshot (a possible new court codelist).
    /// </summary>
    private static readonly string[] PrioritizedCodeListTypes =
    {
        "CASE_CATEGORY", "CASE_TYPE", "PARTY_TYPE", "JURISDICTIONAL_AMOUNT",
        "ADDRESS_TYPE", "PARTY_DESIGNATION_TYPE", "LANGUAGE", "DEFAULT_TYPE",
    };

    [LiveMaderaFact]
    public async Task Refetch_PolicyAndCodelists_DiffVsAprilSnapshots()
    {
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, nameof(Refetch_PolicyAndCodelists_DiffVsAprilSnapshots));

        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        var outDir = Path.Combine(SampleLoader.RepoRoot, "temp", "refetch-codelists-2026-06-04");
        Directory.CreateDirectory(outDir);
        var aprilDir = Path.Combine(SampleLoader.RepoRoot, "docs", "fileing files");

        var summary = new StringBuilder();
        void Line(string s) { _output.WriteLine(s); summary.AppendLine(s); }

        Line($"=== Madera live codelist/policy re-fetch @ {DateTime.UtcNow:O} ===");
        Line($"Endpoint: {config.SoapEndpoint}");

        // ── 1. GetPolicy ─────────────────────────────────────────────────
        var policy = await provider.GetPolicyAsync(config);
        Line($"PolicyVersionId   = {policy.PolicyVersionId}");
        Line($"DocumentListUrl   = {policy.DocumentListUrl}");
        Line($"CourtLocationsUrl = {policy.CourtLocationsUrl}");
        var policyTypes = policy.CodeListUrls.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Line($"CodeListUrls in current policy ({policyTypes.Count}): {string.Join(", ", policyTypes)}");

        // Flag policy codelist types we have NO April snapshot for (possible new lists).
        var unsnapped = policyTypes
            .Where(t => !File.Exists(Path.Combine(aprilDir, $"madera_{t}.xml")))
            .ToList();
        if (unsnapped.Count > 0)
            Line($"⚠️ Policy codelist types with NO April snapshot: {string.Join(", ", unsnapped)}");

        // ── 2. Per-codelist fresh fetch + diff vs April ──────────────────
        foreach (var type in PrioritizedCodeListTypes)
        {
            Line($"\n--- {type} ---");
            if (!policy.CodeListUrls.ContainsKey(type))
            {
                Line($"  NOT present in current policy CodeListUrls.");
                continue;
            }

            List<CodeListItem> fresh;
            try
            {
                fresh = await provider.GetCodeListAsync(config, type);
            }
            catch (Exception ex)
            {
                Line($"  live fetch FAILED: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var freshMap = fresh
                .Where(i => !string.IsNullOrEmpty(i.Code))
                .GroupBy(i => i.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty, StringComparer.Ordinal);

            await File.WriteAllLinesAsync(
                Path.Combine(outDir, $"madera_{type}_fresh.tsv"),
                freshMap.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}\t{kv.Value}"));
            Line($"  fresh : {freshMap.Count} codes");

            var aprilPath = Path.Combine(aprilDir, $"madera_{type}.xml");
            if (!File.Exists(aprilPath))
            {
                Line($"  no April snapshot ({aprilPath}); fresh dump saved, no diff.");
                continue;
            }

            Dictionary<string, string> aprilMap;
            try
            {
                var april = CodeListResponseParser.ParseCodeList(await File.ReadAllTextAsync(aprilPath));
                aprilMap = april
                    .Where(i => !string.IsNullOrEmpty(i.Code))
                    .GroupBy(i => i.Code, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                Line($"  April snapshot parse FAILED: {ex.Message}; fresh saved, manual diff needed.");
                continue;
            }

            Line($"  april : {aprilMap.Count} codes");

            var added = freshMap.Keys.Except(aprilMap.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removed = aprilMap.Keys.Except(freshMap.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var renamed = freshMap.Keys.Intersect(aprilMap.Keys)
                .Where(k => !string.Equals(freshMap[k], aprilMap[k], StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (added.Count == 0 && removed.Count == 0 && renamed.Count == 0)
            {
                Line("  => IDENTICAL to April snapshot. [current]");
            }
            else
            {
                Line($"  => CHANGED: +{added.Count} added, -{removed.Count} removed, ~{renamed.Count} renamed");
                if (added.Count > 0) Line($"     ADDED:   {string.Join(", ", added.Take(80))}");
                if (removed.Count > 0) Line($"     REMOVED: {string.Join(", ", removed.Take(80))}");
                if (renamed.Count > 0)
                    Line($"     RENAMED: {string.Join("; ", renamed.Take(50).Select(k => $"{k}: '{aprilMap[k]}'->'{freshMap[k]}'"))}");
            }
        }

        await File.WriteAllTextAsync(Path.Combine(outDir, "DIFF_SUMMARY.txt"), summary.ToString());
        _output.WriteLine($"\nWrote fresh dumps + DIFF_SUMMARY.txt to {outDir}");

        Assert.True(true, "Read-only currency probe — always passes; inspect DIFF_SUMMARY.txt.");
    }

    /// <summary>
    /// F-SRC2: capture raw-XML snapshots of the CC-relevant codelists that have NO April
    /// snapshot — the party/contact-form lists (country, US state, AKA type, phone type).
    /// The audit (C3/C4) needs these to verify the UI dropdowns against authoritative court
    /// data. Writes <c>docs/fileing files/madera_&lt;TYPE&gt;.xml</c> in the SAME raw REST /
    /// Genericode format the existing snapshots use, so <see cref="CodeListResponseParser"/>
    /// round-trips them. Read-only fetch (GET only, never a submission); opt-in via
    /// <c>EFILING_LIVE_MADERA=1</c>. Targets are matched by name pattern so the right lists are
    /// captured regardless of the exact policy key spelling; anything already snapshotted is skipped.
    /// </summary>
    [LiveMaderaFact]
    public async Task Capture_CcRelevantCodelist_Snapshots()
    {
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, nameof(Capture_CcRelevantCodelist_Snapshots));

        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        using var rest = new JtiRestClient();

        var aprilDir = Path.Combine(SampleLoader.RepoRoot, "docs", "fileing files");

        var policy = await provider.GetPolicyAsync(config);
        var allKeys = policy.CodeListUrls.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        _output.WriteLine($"Policy CodeListUrls ({allKeys.Count}): {string.Join(", ", allKeys)}");

        // CC party/contact-form codelists. Match by substring so we grab the right lists even
        // if the policy key spelling differs (e.g. ADDRESS_COUNTRY vs COUNTRY); skip any that
        // already have a snapshot under docs/.
        string[] patterns = { "COUNTRY", "STATE", "AKA", "PHONE", "TELEPHONE" };
        var targets = allKeys
            .Where(k => patterns.Any(p => k.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Where(k => !File.Exists(Path.Combine(aprilDir, $"madera_{k}.xml")))
            .ToList();

        _output.WriteLine($"CC-relevant unsnapped targets: {(targets.Count == 0 ? "(none)" : string.Join(", ", targets))}");

        var captured = new List<string>();
        foreach (var type in targets)
        {
            var url = policy.CodeListUrls[type];
            string xml;
            try { xml = await rest.GetAsync(config, url); }
            catch (Exception ex) { _output.WriteLine($"  {type}: fetch FAILED {ex.GetType().Name}: {ex.Message}"); continue; }

            // Persist the raw XML (the evidence), then parse-count as a sanity signal — a
            // non-standard format still leaves the raw snapshot for manual inspection.
            var path = Path.Combine(aprilDir, $"madera_{type}.xml");
            await File.WriteAllTextAsync(path, xml);

            int codeCount = -1;
            try { codeCount = CodeListResponseParser.ParseCodeList(xml).Count; } catch { /* raw XML still saved */ }
            captured.Add($"{type}={codeCount}");
            _output.WriteLine($"  {type}: saved {xml.Length} bytes, parsed {codeCount} codes -> {path}");
        }

        _output.WriteLine($"\nCaptured {captured.Count} snapshot(s): {string.Join(", ", captured)}");
        Assert.True(true, "Read-only capture probe — inspect output + docs/fileing files/madera_*.xml.");
    }
}
