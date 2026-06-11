using System.Text.Json;
using System.Text.Json.Nodes;
using EFiling.Core.Caching;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Tier B utility test — enrich every acceptance recorded in
/// <c>docs/MADERA_ACCEPTED_FILINGS.json</c> with (1) the public docket info
/// assigned by the clerk and (2) the real Madera-side party <c>PrimaryId</c>s
/// + complaint IDs needed for Subsequent-Filing curation.
///
/// <para>
/// <b>Two-step enrichment per entry.</b>
/// <list type="number">
///   <item>Call <see cref="JtiEFilingProvider.GetFilingStatusAsync"/> — resolves
///         <c>EfmReferenceId</c> (e.g. <c>26MA00004255</c>) to the public
///         <c>CaseDocketId</c> (e.g. <c>MCV089018</c>) and <c>CaseName</c>
///         caption once the clerk has titled the case.</item>
///   <item>Call <see cref="JtiEFilingProvider.GetCaseAsync"/> with the docket
///         ID — returns every party on the case with its real Madera
///         <c>PrimaryId</c> plus every complaint with its <c>ComplaintId</c>.
///         These are what SF filings need to attach existing-data caseParticipant
///         refs and <c>&lt;Complaint st:id="..."/&gt;</c> references to the right
///         complaint inside the case.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why the second step matters.</b> SF baseline XMLs carry foreign-court
/// <c>PrimaryId</c>s (e.g. Placer <c>1493518</c>) and complaint <c>st:id</c>s
/// (e.g. <c>1108856</c>). Submitting these verbatim to Madera against a real
/// Madera docket would either silently misattach or fail validation. With the
/// enriched JSON the SF curator can copy the correct Madera-side IDs into
/// <see cref="MaderaLiveFixtures"/> overrides.
/// </para>
///
/// <para>
/// <b>Opt-in execution.</b> Class-level <c>Category=LiveMadera</c> trait plus
/// method-level <see cref="LiveMaderaFactAttribute"/> gate. Run explicitly with
/// the opt-in env var:
/// <code>
/// $env:EFILING_LIVE_MADERA = "1"
/// dotnet test --filter "FullyQualifiedName~TierB_ResolveDocketIds"
/// </code>
/// </para>
///
/// <para>
/// <b>Writes to source.</b> This test mutates
/// <c>docs/MADERA_ACCEPTED_FILINGS.json</c>. Review the diff via <c>git diff</c>
/// after running. Only enrichment fields are written (<c>caseDocketId</c>,
/// <c>caseName</c>, <c>filingStatus</c>, <c>docketPulledAt</c>, <c>parties</c>,
/// <c>complaints</c>, <c>caseMetadataPulledAt</c>); existing scenario/category
/// data is never touched.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class TierB_ResolveDocketIdsTests
{
    private readonly ITestOutputHelper _output;

    public TierB_ResolveDocketIdsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveMaderaFact]
    public async Task ResolveDocketIdsAndCaseMetadata_ForAllAcceptedFilings_WritesBackToJson()
    {
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, nameof(ResolveDocketIdsAndCaseMetadata_ForAllAcceptedFilings_WritesBackToJson));

        var jsonPath = LocateAcceptedFilingsJson();
        _output.WriteLine($"[Resolve] Reading from: {jsonPath}");

        var raw = await File.ReadAllTextAsync(jsonPath);
        var root = JsonNode.Parse(raw)?.AsObject()
            ?? throw new InvalidOperationException("Could not parse MADERA_ACCEPTED_FILINGS.json as object.");
        var entries = root["entries"]?.AsArray()
            ?? throw new InvalidOperationException("Missing 'entries' array in JSON.");

        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        int attempted = 0, docketResolved = 0, caseResolved = 0, errors = 0;

        foreach (var entryNode in entries)
        {
            var entry = entryNode?.AsObject();
            if (entry == null) continue;

            var scenarioId = entry["scenarioId"]?.GetValue<string>() ?? "(unknown)";
            var efmId = entry["efmReferenceId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(efmId))
            {
                _output.WriteLine($"[Resolve] Skipping {scenarioId} — no efmReferenceId.");
                continue;
            }

            attempted++;

            // ─── Step 1: GetFilingStatus → docket ID + case caption ─────────
            string? docketId = null;
            try
            {
                var status = await provider.GetFilingStatusAsync(config, efmReferenceId: efmId);

                entry["filingStatus"] = status.FilingStatus.ToString();
                entry["caseDocketId"] = status.CaseDocketId;
                entry["caseName"] = status.CaseName;
                entry["docketPulledAt"] = DateTimeOffset.UtcNow.ToString("O");

                if (!string.IsNullOrEmpty(status.CaseDocketId))
                {
                    docketResolved++;
                    docketId = status.CaseDocketId;
                    _output.WriteLine(
                        $"[Resolve] {scenarioId} {efmId} → docket={status.CaseDocketId} " +
                        $"name={status.CaseName ?? "(null)"} status={status.FilingStatus}");
                }
                else
                {
                    _output.WriteLine(
                        $"[Resolve] {scenarioId} {efmId} → status={status.FilingStatus} " +
                        $"(docket not yet assigned by clerk)");
                }
            }
            catch (Exception ex)
            {
                errors++;
                _output.WriteLine($"[Resolve] {scenarioId} {efmId} → STATUS ERROR: {ex.Message}");
                continue;
            }

            // ─── Step 2: GetCase → parties (PrimaryId) + complaints ─────────
            if (string.IsNullOrEmpty(docketId)) continue;

            try
            {
                var caseInfo = await provider.GetCaseAsync(
                    config,
                    caseDocketId: docketId,
                    includeParticipants: true,
                    includeDocketEntries: false);

                if (caseInfo == null)
                {
                    _output.WriteLine(
                        $"[Resolve]   GetCase({docketId}) returned null — case not found or server error.");
                    continue;
                }

                entry["caseTrackingId"] = caseInfo.CaseTrackingId;
                entry["parties"] = SerializeParties(caseInfo.Parties);
                entry["complaints"] = SerializeComplaints(caseInfo.Complaints);
                entry["caseMetadataPulledAt"] = DateTimeOffset.UtcNow.ToString("O");

                caseResolved++;
                _output.WriteLine(
                    $"[Resolve]   GetCase({docketId}) → parties={caseInfo.Parties.Count} " +
                    $"complaints={caseInfo.Complaints.Count} trackingId={caseInfo.CaseTrackingId ?? "(null)"}");
                foreach (var p in caseInfo.Parties)
                {
                    var name = !string.IsNullOrEmpty(p.OrganizationName)
                        ? p.OrganizationName
                        : $"{p.FirstName} {p.LastName}".Trim();
                    _output.WriteLine(
                        $"[Resolve]     party[{p.RoleCode}] id={p.PrimaryId ?? "(null)"} name='{name}'" +
                        (string.IsNullOrEmpty(p.BarNumber) ? "" : $" bar={p.BarNumber}"));
                }
                foreach (var c in caseInfo.Complaints)
                {
                    _output.WriteLine(
                        $"[Resolve]     complaint id={c.ComplaintId ?? "(null)"} " +
                        $"category={c.CaseCategoryCode ?? "(null)"} title='{c.CaseTitle ?? "(null)"}'");
                }
            }
            catch (Exception ex)
            {
                errors++;
                _output.WriteLine($"[Resolve]   GetCase({docketId}) → ERROR: {ex.Message}");
            }
        }

        root["lastUpdated"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        var writeOpts = new JsonSerializerOptions { WriteIndented = true };
        var newJson = root.ToJsonString(writeOpts);
        await File.WriteAllTextAsync(jsonPath, newJson);

        _output.WriteLine($"[Resolve] Summary: attempted={attempted} docketResolved={docketResolved} " +
                          $"caseResolved={caseResolved} errors={errors}. JSON updated.");

        Assert.True(attempted > 0, "Expected at least one filing entry to attempt.");
    }

    /// <summary>
    /// Project <see cref="CaseParty"/>s to a JSON array with exactly the fields
    /// SF curation needs. Keeps the on-disk schema stable even if the model
    /// grows additional fields (the resolver only writes the subset it knows
    /// about).
    /// </summary>
    private static JsonArray SerializeParties(IEnumerable<CaseParty> parties)
    {
        var arr = new JsonArray();
        foreach (var p in parties)
        {
            arr.Add(new JsonObject
            {
                ["primaryId"] = p.PrimaryId,
                ["referenceId"] = p.ReferenceId,
                ["roleCode"] = p.RoleCode,
                ["isOrganization"] = p.IsOrganization,
                ["firstName"] = p.FirstName,
                ["middleName"] = p.MiddleName,
                ["lastName"] = p.LastName,
                ["nameSuffix"] = p.NameSuffix,
                ["organizationName"] = p.OrganizationName,
                ["barNumber"] = p.BarNumber
            });
        }
        return arr;
    }

    /// <summary>
    /// Project <see cref="CaseComplaint"/>s to a JSON array. For SF filings the
    /// <c>complaintId</c> is what goes into <c>&lt;Complaint st:id="..."/&gt;</c>
    /// on the subsequent filing — so this must survive on disk exactly as
    /// Madera returns it.
    /// </summary>
    private static JsonArray SerializeComplaints(IEnumerable<CaseComplaint> complaints)
    {
        var arr = new JsonArray();
        foreach (var c in complaints)
        {
            arr.Add(new JsonObject
            {
                ["complaintId"] = c.ComplaintId,
                ["caseTitle"] = c.CaseTitle,
                ["caseCategoryCode"] = c.CaseCategoryCode
            });
        }
        return arr;
    }

    /// <summary>
    /// Locate <c>MADERA_ACCEPTED_FILINGS.json</c> by walking upward from the test
    /// binary directory to find the repo root (identified by presence of
    /// <c>docs/MADERA_ACCEPTED_FILINGS.json</c>). Keeps the test robust to both
    /// <c>dotnet test</c> invocations (CWD varies) and IDE-launched runs.
    /// </summary>
    private static string LocateAcceptedFilingsJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "MADERA_ACCEPTED_FILINGS.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate docs/MADERA_ACCEPTED_FILINGS.json by walking up from " +
            AppContext.BaseDirectory);
    }
}
