using System.Xml.Linq;
using EFiling.Providers.JTI.Builders;
using EFiling.Tests.SubsequentFilingRoundTrip;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Pipeline smoke tests that exercise the <i>full build path</i> a Tier B live test
/// would take — parse baseline → apply common overrides → apply scenario overrides
/// → rebuild SOAP request XML via <see cref="ReviewFilingXmlBuilder"/> — WITHOUT
/// touching the network.
///
/// <para>
/// <b>Why.</b> Tier B live tests are opt-in (class-level <c>[Trait("Category", "LiveMadera")]</c>).
/// Without a network-free counterpart, a curator who adds an override entry to
/// <see cref="MaderaLiveFixtures"/> has no way to verify "does my override produce
/// a structurally sane SOAP envelope" before paying the cost of an actual Madera
/// round-trip. These smoke tests fill that gap: they run in the default suite,
/// produce the exact XML that <i>would</i> be submitted, and assert the envelope
/// shape is intact (credentials substituted, case type preserved, etc.).
/// </para>
///
/// <para>
/// <b>Scope.</b> One smoke test per <b>curated</b> scenario in
/// <see cref="MaderaLiveFixtures"/>. As more scenarios get curated, extend this
/// file with additional smoke methods (or promote to a <c>[Theory]</c> over the
/// curated subset). Currently: FAM-INI-001 only, as it's the first scenario
/// curated and the closest match to the known-working
/// <c>AutoAcceptFilingTests.BuildAutoAcceptSubmission</c> template.
/// </para>
/// </summary>
public class TierB_PipelineSmokeTests
{
    private readonly ITestOutputHelper _output;

    public TierB_PipelineSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Full pipeline dry-run for <c>FAM-INI-001</c>: parse → common overrides →
    /// scenario overrides → build SOAP request XML. Asserts the envelope is
    /// well-formed and contains the Madera-specific substitutions that
    /// <see cref="MaderaLiveFixtures"/> injects.
    /// </summary>
    [Fact]
    public void FAM_INI_001_BuildsStructurallyValidXml_WithMaderaOverridesApplied()
    {
        const string scenarioId = "FAM-INI-001";

        // 1. Parse baseline.
        var submission = ScenarioFixtures.LoadSubmission(scenarioId);

        // 2. Apply common overrides (SubmitterUsername, EfspReferenceId).
        MaderaLiveFixtures.ApplyCommonOverrides(submission, scenarioId);

        // 3. Apply scenario-specific overrides (attorney swap, location swap, PDF URL).
        Assert.True(MaderaLiveFixtures.TryGetScenarioOverride(scenarioId, out var scenarioOverride),
            $"{scenarioId} should be in MaderaLiveFixtures.ScenarioOverrides for this smoke test to run. " +
            $"If this fires, either the scenario has been removed from curation or the fixture registry " +
            $"was reset — re-add the entry or remove this smoke test.");
        scenarioOverride!(submission);

        // 4. Build the SOAP request XML as it would be sent to Madera.
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        _output.WriteLine("=== Generated SOAP request XML for FAM-INI-001 (Madera) ===");
        _output.WriteLine(xml);
        _output.WriteLine($"=== Length: {xml.Length} chars ===");

        // 5. Assert the envelope is parseable and contains the key substitutions.
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);

        // Credentials substituted (Madera username, not the baseline placeholder).
        Assert.Contains("legalhub", xml);
        Assert.DoesNotContain("YOUR_USERNAME_HERE", xml);
        Assert.DoesNotContain("YOUR_IDENTIFICATIONID_HERE", xml);
        Assert.DoesNotContain("YOUR_URL_HERE", xml);

        // Case type preserved from baseline (Family Law/Support = 211110 in the Madera codelist).
        Assert.Contains("211110", xml);

        // Attorney bar number swapped to the known-good Madera-registered one (Felicia Espinosa).
        Assert.Contains("267198", xml);

        // Working PDF URL substituted (baseline had YOUR_URL_HERE; overrides swap in the public test PDF).
        Assert.Contains("dummy.pdf", xml);

        // Madera courthouse location substituted (baseline had Placer's GIB).
        Assert.Contains("Madera Courthouse", xml);
        Assert.DoesNotContain(">GIB<", xml); // location name from Placer baseline must be gone

        // SOAP envelope integrity — must have Body + ReviewFilingRequestMessage + CoreFilingMessage + PaymentMessage.
        Assert.Contains("ReviewFilingRequestMessage", xml);
        Assert.Contains("CoreFilingMessage", xml);
        Assert.Contains("PaymentMessage", xml);
    }

    /// <summary>
    /// Step #16 — diagnostic dump for forensic triage of Tier B
    /// scenarios that have been migrated to <see cref="FilingMetadataValue.ReplaceWithSingleId"/>
    /// (silent-drop scoreboard #15 fix). Builds the exact XML the Tier B live test
    /// would submit and writes it to <c>temp/tier-b-soap-{scenarioId}.xml</c> for
    /// visual inspection. Asserts only that the build succeeded — Madera-acceptance
    /// validation is the live test's job. Companion to the FAM-INI-001 smoke above.
    ///
    /// <para>
    /// Origin: 2026-05-20 FAM-SUB-004 Tier B run failed live with ErrorCode=4013
    /// "Invalid CaseParticipant id: 1494948" even though the iteration-5 fixture
    /// override explicitly cleared 1494948. Diagnostic dump revealed the
    /// pre-Step-#14 mutation idiom (mutate `IdReferences` only) silently leaves
    /// `TaggedReferences` at its parser-populated baseline state, which Step #14's
    /// builder reads instead. Step #16 closed the silent-drop class with the
    /// `ReplaceWithSingleId` helper. This Theory covers each migrated scenario so
    /// regressions on the helper-call sites surface in the default suite (no live
    /// network call needed).
    /// </para>
    ///
    /// <para>
    /// <b>How to add a new scenario:</b> after migrating its fixture override to
    /// the helper (Step #16 pattern, see <c>MaderaLiveFixtures.cs</c> for FAM-SUB-004
    /// and CIV-SUB-014 as canonical examples), add an <c>[InlineData]</c> entry
    /// here. The dump file lands at <c>temp/tier-b-soap-{scenarioId}.xml</c> and
    /// the per-mv state line in xUnit output makes the migration easy to verify.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("FAM-SUB-004")] // Step #16 — first migration; canonical example
    [InlineData("CIV-SUB-014")] // Step #17 — Motion + eService tag preservation
    [InlineData("CIV-SUB-017")] // Step #18 — POS on PI case; 3 metadata code mutations
                                // (FILING_PARTY + PARTY_SERVED + FILING_ATTORNEY) — first
                                // migration to broaden coverage to PARTY_SERVED code
    [InlineData("CIV-SUB-015")] // Step #19 — Notice of Appeal; canonical migration of
                                // the "classType+ValueRestriction filter loop with Code
                                // branch" idiom shared by CIV-SUB-002/005/007/008. First
                                // RESPONDING_PARTY coverage. Pure 2-id mutation, no add/remove.
    [InlineData("CIV-SUB-002")] // Step #20 — Substitution of Attorney on gov-entity case;
                                // first migration of the "filter-loop with UNIFORM id"
                                // shape (no Code branch — every match gets same id) +
                                // first FILING_ATTORNEY constructor migration to use
                                // TaggedReferences post-Step-#14.
    [InlineData("CIV-SUB-005")] // Step #21 — First Paper with new representation; same
                                // shape as CIV-SUB-002 (uniform-id loop + FILING_ATTORNEY
                                // ctor); 2nd application of the canonical pattern.
    [InlineData("CIV-SUB-007")] // Step #22 — Association of Attorney; same shape as #20/#21;
                                // 3rd application of the canonical pattern.
    [InlineData("CIV-SUB-008")] // Step #23 — Cross-Complaint; filter-loop with Code branch
                                // (#19 pattern) + structural refactor to existing-data
                                // FILING_ATTORNEY (drop NEW_ATTORNEY+REPRESENTING).
    [InlineData("CIV-SUB-006")] // Step #24 — Defendant first paper on MCV089018; 4th
                                // application of the uniform-id-loop + FILING_ATTORNEY
                                // ctor canonical pattern (#20/#21/#22). Reuses MCV089018
                                // Felicia 1101868 from Step #23 probe.
    [InlineData("CIV-SUB-012")] // Step #25 — same shape/case as #24 (MCV089018, Felicia
                                // 1101868); 5th application of canonical pattern.
    [InlineData("CIV-SUB-001")] // Step #26 — Code-keyed if/else-if (Step #17 family).
    [InlineData("CIV-SUB-003")] // Step #27 — FILING_PARTY mutation + FILING_ATTORNEY ctor;
                                // FEE_EXEMPTION=FEE_WAIVER tag preserved from baseline.
    [InlineData("CIV-SUB-010")] // Step #28 — gov entity FILING_PARTY with explicit tag-drop;
                                // first migration using empty preservedTags as "clear all".
    [InlineData("CIV-SUB-009")] // Step #29 — Code-keyed if/else-if; doc 439110 Motion.
    [InlineData("CIV-SUB-016")] // Step #30 — 3-code Code-keyed (FILING_PARTY + PARTY_SERVED
                                // + FILING_ATTORNEY); same shape as #18 on different case.
    [InlineData("FAM-SUB-001")] // Step #31 — uniform-id loop + CaseAssignmentValue mutation.
    [InlineData("PRO-SUB-001")] // Step #32 — same shape as FAM-SUB-001 on Probate case.
    [InlineData("CIV-SUB-013")] // Step #33 — same shape as FAM-SUB-001 on Civil case.
    [InlineData("CIV-SUB-004")] // Step #34 — uniform-id loop + FILING_PARTY_ADDRESS contact ctor.
    [InlineData("CIV-SUB-011")] // Step #35 — same shape on No-Fee case.
    [InlineData("FAM-SUB-005")] // Step #37 — filter-loop with Code branch (Family case).
    [InlineData("FAM-SUB-002")] // Step #36 — same shape as #34 on Family case.
    [InlineData("FAM-SUB-003")] // Step #38 — final legacy-idiom fixture; same shape as
                                // CIV-SUB-004. H-6 known-failing per old fixture comment
                                // ("Madera 258130 doesn't declare MOTION_OSC_DETAIL").
    public void TierB_MigratedScenario_DumpFinalSoapXml_ForForensicInspection(string scenarioId)
    {
        var submission = ScenarioFixtures.LoadSubmission(scenarioId);
        MaderaLiveFixtures.ApplyCommonOverrides(submission, scenarioId);

        Assert.True(MaderaLiveFixtures.TryGetScenarioOverride(scenarioId, out var scenarioOverride),
            $"{scenarioId} must be curated for this dump test to run.");
        scenarioOverride!(submission);

        var config = MaderaLiveFixtures.MaderaStagingConfig;
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        var repoRoot = SampleLoader.RepoRoot;
        var tempDir = Path.Combine(repoRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir, $"tier-b-soap-{scenarioId}.xml");
        File.WriteAllText(outPath, xml);

        _output.WriteLine($"=== Dumped {scenarioId} final SOAP XML to: {outPath} ===");
        _output.WriteLine($"=== Length: {xml.Length} chars ===");
        _output.WriteLine($"=== Parties count post-override: {submission.Parties.Count} ===");
        _output.WriteLine($"=== ConnectedDocuments count post-override: {submission.ConnectedDocuments.Count} ===");
        _output.WriteLine($"=== Lead doc metadata count: {submission.LeadDocument?.MetadataValues.Count ?? -1} ===");
        if (submission.LeadDocument != null)
        {
            foreach (var mv in submission.LeadDocument.MetadataValues)
            {
                var idsCsv = string.Join(",", mv.IdReferences);
                var taggedRefIdsCsv = string.Join(",", mv.TaggedReferences.Select(tr => tr.Id));
                var taggedTagTypesCsv = string.Join(",", mv.TaggedReferences.SelectMany(tr => tr.Tags).Select(t => t.TagType));
                _output.WriteLine($"  metadata[{mv.Code}/{mv.ClassType}/{mv.SubType}/{mv.ValueRestriction}] " +
                                  $"idRefs=[{idsCsv}] taggedRefs.Ids=[{taggedRefIdsCsv}] " +
                                  $"taggedRefs.Tags=[{taggedTagTypesCsv}] " +
                                  $"legacyTags={mv.AdditionalInfoTags.Count}");
            }
        }

        Assert.NotNull(xml);
        Assert.NotEmpty(xml);
    }

    /// <summary>
    /// Cross-check: the SubmitterUsername override is idempotent across repeated
    /// <see cref="MaderaLiveFixtures.ApplyCommonOverrides"/> calls and produces a
    /// unique <c>EfspReferenceId</c> on every invocation (to avoid EFSP-side
    /// deduplication collisions when a curator re-runs a scenario).
    /// </summary>
    [Fact]
    public void ApplyCommonOverrides_ProducesUniqueEfspReferenceId_PerCall()
    {
        var sub1 = ScenarioFixtures.LoadSubmission("FAM-INI-001");
        var sub2 = ScenarioFixtures.LoadSubmission("FAM-INI-001");

        MaderaLiveFixtures.ApplyCommonOverrides(sub1, "FAM-INI-001");
        MaderaLiveFixtures.ApplyCommonOverrides(sub2, "FAM-INI-001");

        Assert.Equal("legalhub", sub1.SubmitterUsername);
        Assert.Equal("legalhub", sub2.SubmitterUsername);
        Assert.NotEqual(sub1.EfspReferenceId, sub2.EfspReferenceId);
        Assert.StartsWith("TIERB-FAM-INI-001-", sub1.EfspReferenceId);
        Assert.StartsWith("TIERB-FAM-INI-001-", sub2.EfspReferenceId);
    }
}
