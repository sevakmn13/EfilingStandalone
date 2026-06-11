using EFiling.Core.Caching;
using EFiling.Providers.JTI;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Tier B diagnostic — one-shot probe to determine whether Madera's GetCase
/// response carries <c>CaseAssignment</c> elements with <c>primaryId</c>/
/// <c>referenceId</c> attributes for attorneys on the case.
///
/// <para>
/// <b>Motivation (H-5 unblock).</b> Subsequent filings that reference an
/// <b>existing</b> attorney (e.g. FILING_ATTORNEY existing-data for a
/// Gov-Entity filer) need the <c>caseAssignment</c> primaryId, which is
/// distinct from the attorney's participant id. Our
/// <c>CaseResponseParser</c> only extracts <c>CaseParticipantExt</c>
/// participants — if CaseAssignment ids are in the response but we're
/// throwing them away, the fix is a parser extension plus an exposure on
/// <c>CaseInfo</c>.
/// </para>
///
/// <para>
/// <b>Probe target.</b> MCV089022 (Stephen Marks vs. Jack Jackson, Civil
/// Limited) — Felicia Espinosa was attached as Jack's attorney via the
/// CIV-SUB-007 curated submission (see MADERA_ACCEPTED_FILINGS.json).
/// Running GetCase against this docket should return a CaseAssignment
/// element for Felicia if Madera surfaces assignment data at all.
/// </para>
///
/// <para>
/// <b>What this does.</b> Pulls GetCase, dumps the raw XML, and greps for
/// <c>CaseAssignment</c> / <c>primaryId</c>. Writes findings to test output
/// and — if present — dumps the surrounding 2KB of XML so the shape can be
/// inspected visually before extending <c>CaseResponseParser</c>.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class TierB_ProbeCaseAssignmentTests
{
    private readonly ITestOutputHelper _output;

    public TierB_ProbeCaseAssignmentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveMaderaFact]
    public async Task Probe_GetCase_MCV089022_ForCaseAssignmentPrimaryIds()
    {
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, nameof(Probe_GetCase_MCV089022_ForCaseAssignmentPrimaryIds));

        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        // Phase A batch 4 probe: all unused Civil + Family cases so we can
        // craft overrides for the remaining 11 pending SF scenarios.
        foreach (var docketId in new[] {
            "MCV089014", // Group B: Felicia associated via SUB-005/013 — need her primaryId for FILING_ATTORNEY
            "MCV089018", // Group B: Felicia associated via SUB-008 — need her primaryId for SUB-006, SUB-012
            "MCV089022", // Group B: Felicia associated via SUB-007 — need her primaryId for rerun
            "MFL018636", // Phase B batch 1 rejection: FAM-SUB-004/006 hardcoded Felicia=1101831 was Invalid CaseAssignment id. Need current Family primaryId.
            "MCV089024", // Phase B batch 1 rejection: CIV-SUB-018 hardcoded Felicia=1101842 was Invalid CaseAssignment id. Need current Civil Limited primaryId.
            "MCV089015", // Phase B batch 1 rejection: CIV-SUB-019 hardcoded Felicia=1101836 was Invalid CaseAssignment id. Need current Civil primaryId (may be James Selth post-substitution).
        })
        {
            _output.WriteLine($"═══════════════════════════════════════════════════════════");
            _output.WriteLine($"[Probe] GetCase({docketId})");
            _output.WriteLine($"═══════════════════════════════════════════════════════════");

            var caseInfo = await provider.GetCaseAsync(
                config,
                caseDocketId: docketId,
                includeParticipants: true,
                includeDocketEntries: false);

            if (caseInfo == null)
            {
                _output.WriteLine($"[Probe] {docketId} → null. Skipping.");
                continue;
            }

            _output.WriteLine($"[Probe] Parsed parties ({caseInfo.Parties.Count}):");
            foreach (var p in caseInfo.Parties)
            {
                var name = !string.IsNullOrEmpty(p.OrganizationName)
                    ? p.OrganizationName
                    : $"{p.FirstName} {p.LastName}".Trim();
                _output.WriteLine(
                    $"[Probe]   [{p.RoleCode}] primaryId={p.PrimaryId ?? "(null)"} " +
                    $"name='{name}'" +
                    (string.IsNullOrEmpty(p.BarNumber) ? "" : $" bar={p.BarNumber}"));
            }

            var raw = caseInfo.RawXml ?? string.Empty;
            _output.WriteLine($"[Probe] Raw XML length: {raw.Length}");

            // Count CaseAssignment occurrences (including the namespace declaration).
            var caOccurrences = 0;
            var idx = 0;
            while ((idx = raw.IndexOf("CaseAssignment", idx, StringComparison.Ordinal)) >= 0)
            {
                caOccurrences++;
                idx += "CaseAssignment".Length;
            }
            _output.WriteLine($"[Probe] CaseAssignment substring count: {caOccurrences}");

            // Look specifically for <...:CaseAssignment ...> element starts (not ns declarations
            // which are attributes of another element).
            var elementStart = -1;
            var searchFrom = 0;
            var hits = new List<int>();
            while ((elementStart = raw.IndexOf(":CaseAssignment ", searchFrom, StringComparison.Ordinal)) >= 0
                || (elementStart = raw.IndexOf("<CaseAssignment", searchFrom, StringComparison.Ordinal)) >= 0)
            {
                // We want element starts, so need the character at elementStart-1 (or 0) to be '<' OR
                // the match to be "<CaseAssignment". For ":CaseAssignment " we need '<' before the ns prefix.
                var backcheck = raw.LastIndexOf('<', elementStart, Math.Min(elementStart, 100));
                if (backcheck >= 0 && backcheck < elementStart)
                {
                    var elSnippet = raw.Substring(backcheck, Math.Min(300, raw.Length - backcheck));
                    // Skip if this is a CaseAssignmentType namespace declaration inside attributes of
                    // another element (e.g. xmlns:ns11="urn:..:CaseAssignmentType").
                    if (!elSnippet.StartsWith("<CaseAssignmentType", StringComparison.Ordinal)
                        && !elSnippet.Contains("xmlns", StringComparison.Ordinal))
                    {
                        hits.Add(backcheck);
                    }
                }
                searchFrom = elementStart + 1;
            }

            if (hits.Count == 0)
            {
                _output.WriteLine($"[Probe] No <CaseAssignment> element starts detected.");
            }
            else
            {
                _output.WriteLine($"[Probe] Found {hits.Count} candidate <CaseAssignment> element start(s).");
                // Dump the first 2KB around each hit.
                foreach (var hit in hits.Take(3))
                {
                    var end = Math.Min(hit + 2000, raw.Length);
                    var snip = raw.Substring(hit, end - hit);
                    _output.WriteLine($"[Probe] --- snippet @ {hit} ---");
                    _output.WriteLine(snip);
                }
            }
        }

        Assert.True(true, "Diagnostic probe — always passes; inspect xUnit output for findings.");
    }
}
