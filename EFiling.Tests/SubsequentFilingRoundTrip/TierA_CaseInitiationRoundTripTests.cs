using System.Xml.Linq;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Parsers;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — Case Initiation round-trip tests (parse → rebuild → structural diff).
///
/// <para>
/// Activates the T-2 Pass 2 round-trip harness for Case Initiation scenarios, closing the
/// loop that <see cref="TierA_RoundTripTests"/> (subsequent-filing-focused, still skipped)
/// described but couldn't execute without the parser + diff helper. The per-scenario test
/// name IS the scoreboard: each passing test = one baseline scenario whose wire contract
/// is fully round-tripped by our code.
/// </para>
///
/// <para>
/// <b>Diff-output convention</b>: when a scenario fails, the assertion message includes the
/// full <see cref="XmlStructuralDiff.DiffResult.FormatDifferences"/> output — each difference
/// annotated with a slash-delimited XPath location. Reading the message should reveal what
/// to fix (builder divergence, parser gap, or legitimate wire-vs-XSD ambiguity).
/// </para>
/// </summary>
public class TierA_CaseInitiationRoundTripTests
{
    /// <summary>
    /// Court configuration whose envelope-level values match the baseline wire placeholders
    /// for Placer County ("PLA"). The <c>YOUR_ENDPOINT_HERE</c> text is an artifact of the
    /// baseline being a documentation template; we use it verbatim so the rebuild produces
    /// the same SendingMDELocationID contents as the source sample.
    /// </summary>
    private static CourtConfiguration BaselineMatchingConfig => new()
    {
        CourtId = "PLA",
        DisplayName = "Placer County Superior Court",
        SoapEndpoint = "https://example.com/ws/soap/niem/FilingReview/",
        RestBaseUrl = "https://example.com/ws/rest/ecourt",
        NfrcCallbackUrl = "YOUR_ENDPOINT_HERE",
    };

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-001 — simplest baseline — round-trip scoreboard test.
    //
    // This test starts as the "current state of affairs" snapshot. On first green, it becomes
    // a regression guard: any future change to builder or parser that breaks the round-trip
    // produces a clear diff-annotated failure.
    //
    // When this test FAILS (expected on initial commit), the FormatDifferences output reveals
    // which specific wire elements diverge. The next step is one of:
    //   1. Fix a genuine builder bug (same pattern that surfaced D-1 and D-2).
    //   2. Fix a parser gap (field extracted incorrectly or not at all).
    //   3. Document an intentional divergence (e.g., the eFilingCaseFilingType element is
    //      unconditionally emitted by our builder but absent from baseline CI — decided earlier
    //      this audit session to be a deliberate choice, not a bug).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_001_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-001");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-008 — Self-Rep Petition with no respondent. Single party (filedBy0),
    // single-line address, ContactInformation with mailing address + email. No
    // extension fields (no fee exemption, no eService, no interpreter). This is
    // simpler than CIV-INI-001 in some ways (only one party) — good scoreboard
    // candidate for testing the "minimum viable Case Initiation" wire shape.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_008_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-008");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-007 — No Fee Case Petition.
    //
    // Audit F-1 FIXED: <conditionallySealed> is now tri-state on
    // FilingSubmission (bool?). null → omit (matches CIV-INI-001 baseline behavior),
    // false → emit "false" (matches CIV-INI-007 baseline), true → emit "true".
    // Forward-path controllers map CreateCaseModel.ConditionallySealed (bool) → null
    // when false so the default UI path preserves the CIV-INI-001-style omission.
    // Round-trip-parsed submissions that came from a baseline with explicit "false"
    // retain that fidelity.
    //
    // Historical context preserved for reference:
    // SKIPPED: Surfaces Audit F-1 — <conditionallySealed>false</> emitted
    // explicitly in baseline (line 120) but our builder only emits this element when
    // sub.ConditionallySealed is true. Baselines are INCONSISTENT about this: CIV-INI-001
    // does NOT emit it, CIV-INI-007 DOES emit it with false. This inconsistency suggests
    // it's a benign optional element whose presence varies by sample-generation pathway
    // rather than a required wire-contract signal.
    //
    // A quick builder change would be: always emit <conditionallySealed>{bool.ToString()}</>
    // regardless of value. But this would also start emitting it for CIV-INI-001 which
    // currently doesn't have it in baseline, BREAKING that green round-trip. The right
    // resolution needs either:
    //   (a) Extend FilingSubmission to track "was this field explicitly set in the source
    //       wire?" and emit only when set, OR
    //   (b) Accept that baselines are inconsistent and loosen both the builder rule and
    //       the diff helper to ignore this element.
    // Both are non-trivial. Skipping for now keeps the scoreboard honest.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_007_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-007");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-002 — Civil Limited filed with a Motion (ex parte). Tests multi-document
    // submission (lead + connected) where connected documents may have their own
    // party-document associations. Also tests CaseCategoryText/CaseTypeText for a
    // motion-specific case category.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_002_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-002");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-003 — Civil Unlimited / Personal Injury baseline. Includes an attorney party
    // with EntityPerson + EntityOrganization (firm name) + multi-line ContactInformation.
    // Exercises dual-entity parsing and multi-line address round-trip — code paths that
    // CIV-INI-001 / CIV-INI-008 don't reach.
    //
    // PREVIOUSLY SKIPPED (surfacing Audits E-1 and E-2); both now fixed in this commit.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_003_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-003");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-004 — Fee Waiver baseline — exercises efmFeeExemptionRequestType
    // round-trip through the parser's extension-field branch + the builder's
    // CaseParticipantExt emission.
    //
    // PREVIOUSLY SKIPPED: surfaced the "CaseParticipantExt child ordering" ambiguity.
    // Resolved via NormalizeOrderInsensitiveContainerChildren pre-processing
    // in the round-trip harness — baselines themselves are inconsistent about sibling
    // ordering so alphabetical-sort normalization on both sides is the honest fix.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_004_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-004");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-006 — Gov Ent Exempt with organization plaintiff. Combines GOVT_ENTITY
    // fee exemption + EntityOrganization variant — a code-path combination that
    // CIV-INI-004 doesn't hit (person plaintiff, not organization).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_006_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-006");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-010 — eService on self-rep plaintiff. Validates eService extension
    // field and its position in the emitted wire (baseline has it LAST in
    // CaseParticipantExt, whereas efmFeeExemption goes FIRST — confirms the
    // order-insensitive-container normalization handles both positions.)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_010_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-010");

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-011 — Interpreter language + attorney with BAR. Exercises:
    //   • efmInterpreterLanguage extension on plaintiff
    //   • Attorney party with BAR number and firm name (post-E-1 fix)
    //   • Attorney contact information
    //   • Multi-party + multi-participant-type submission
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_011_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-011");

    // ──────────────────────────────────────────────────────────────────────────
    // Remaining Civil CI scenarios — exercise additional wire-shape permutations
    // that the first 8 scenarios don't: Unlawful Detainer (premise address),
    // attorney-driven eService, multi-defendant, multi-document transactions.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_005_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-005");

    [Fact]
    public void CIV_INI_009_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-009");

    [Fact]
    public void CIV_INI_012_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-012");

    [Fact]
    public void CIV_INI_013_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("CIV-INI-013");

    // ──────────────────────────────────────────────────────────────────────────
    // Family Law CI (4 scenarios). Uses same CivilCaseTypeExt Case type as Civil
    // (verified earlier) but with different role codes (PET/RES/CONTE) and case
    // categories. Exercises the role-code variance + a few FamilyLaw-specific
    // wire elements (e.g., child support request for DV prevention).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_INI_001_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("FAM-INI-001");

    [Fact]
    public void FAM_INI_002_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("FAM-INI-002");

    [Fact]
    public void FAM_INI_003_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("FAM-INI-003");

    [Fact]
    public void FAM_INI_004_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("FAM-INI-004");

    // ──────────────────────────────────────────────────────────────────────────
    // Probate CI (3 scenarios). Uses CONTE (Conservatee/Contestant) role code
    // distinct from Civil/Family's PLAIN/DEF/PET/RES. Validates role-code-agnostic
    // round-trip machinery.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PRO_INI_001_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("PRO-INI-001");

    [Fact]
    public void PRO_INI_002_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("PRO-INI-002");

    [Fact]
    public void PRO_INI_003_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("PRO-INI-003");

    // ──────────────────────────────────────────────────────────────────────────
    // Mental Health CI (2 scenarios). W&I 8103 Weapons + LPS Conservatorship.
    // The LPS sample is known (from earlier audit) to place eService AFTER
    // ContactInformation — a key piece of evidence for the order-insensitive
    // CaseParticipantExt hypothesis. This scenario CONFIRMS the fix holds when
    // extension fields appear AFTER (not just BEFORE) base fields.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MH_INI_001_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("MH-INI-001");

    [Fact]
    public void MH_INI_002_RoundTrip_ParseBuildAndCompareStructurally()
        => AssertRoundTrip("MH-INI-002");

    /// <summary>
    /// Load a canonical baseline scenario by ID, parse to FilingSubmission via
    /// <see cref="ReviewFilingRequestParser"/>, rebuild via <see cref="ReviewFilingXmlBuilder"/>,
    /// and assert structural equivalence after stripping known non-round-trippable elements
    /// and filtering deliberate Bug #4/#6 UDT→niem-core divergences.
    /// </summary>
    private static void AssertRoundTrip(string scenarioId)
    {
        // 1. Load baseline XML.
        var scenario = CanonicalScenarios.GetById(scenarioId);
        var originalXml = SampleLoader.LoadXmlText(scenario);

        // 2. Parse to FilingSubmission via the new reverse-mapper.
        var submission = ReviewFilingRequestParser.FromXml(originalXml);

        // 3. Rebuild XML via the existing builder.
        var rebuiltXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, BaselineMatchingConfig);

        // 4. Compare structurally. Timestamps (DocumentFiledDate) are known not to round-trip
        //    since they're regenerated by the builder at BuildReviewFilingRequest-time; strip
        //    them from both XMLs before comparison. Same for eFilingCaseFilingType (unconditionally
        //    emitted but absent from baseline) and appClientUsername/appClientParentUsername
        //    (likewise unconditional-when-SubmitterUsername-set but absent in some samples).
        //
        //    These exclusions represent KNOWN non-round-trippable elements under the current
        //    model. Eliminating them one at a time (by adding a preserved-source-value field
        //    to FilingSubmission, or by making builder emission conditional) is the natural
        //    follow-up work.
        var expected = XDocument.Parse(originalXml);
        var actual = XDocument.Parse(rebuiltXml);
        StripKnownNonRoundTrippableElements(expected);
        StripKnownNonRoundTrippableElements(actual);
        NormalizeOrderInsensitiveContainerChildren(expected);
        NormalizeOrderInsensitiveContainerChildren(actual);

        var diff = XmlStructuralDiff.Compare(expected, actual);

        // Filter out deliberate divergences from prior Track A audit fixes — baseline samples
        // use UDT AmountType / TextType for xsi:type, but our builder intentionally emits the
        // niem-core equivalents per "Bug #4 fix" (AmountType) and "Bug #6 fix" (TextType) to
        // produce schema-valid XML. These are WIRE-CORRECT-NOT-BASELINE-EXACT divergences and
        // are NOT regressions. Documented in catalog 2026-04-22 'CI round-trip scoreboard'.
        var remainingDiffs = diff.Differences
            .Where(d => !IsKnownBugFix4Or6Divergence(d))
            .ToList();

        if (remainingDiffs.Count == 0) return; // fully round-tripped modulo known divergences

        var filteredResult = new XmlStructuralDiff.DiffResult(false, remainingDiffs);
        Assert.Fail(
            $"{scenarioId} round-trip produced structurally-different XML after stripping known "
            + "non-round-trippable elements (DateTime/eFilingCaseFilingType/appClient*Username) "
            + "and deliberate Bug #4/#6 fix divergences (UDT→niem-core xsi:type values):\n"
            + filteredResult.FormatDifferences());
    }

    /// <summary>
    /// Recognize the deliberate UDT→niem-core xsi:type divergences introduced by prior
    /// Track A audit fixes. These are documented design decisions, not regressions:
    /// <list type="bullet">
    ///   <item>Bug #4 fix: <c>AmountInControversy</c>/<c>@xsi:type</c> changed from
    ///         <c>ns4:AmountType</c> (UDT namespace) to <c>nc:AmountType</c> (niem-core).</item>
    ///   <item>Bug #6 fix: <c>BinaryFormatStandardName</c>/<c>@xsi:type</c> changed from
    ///         <c>ns4:TextType</c> (UDT namespace) to <c>nc:TextType</c> (niem-core).</item>
    /// </list>
    /// </summary>
    private static bool IsKnownBugFix4Or6Divergence(XmlStructuralDiff.Diff d)
    {
        if (d.Type != "attribute-value") return false;
        // The diff's Expected and Actual strings have the form "@type='ns4:AmountType'" etc.
        var isAmountDivergence = d.Expected.Contains("ns4:AmountType") && d.Actual.Contains("AmountType");
        var isTextDivergence = d.Expected.Contains("ns4:TextType") && d.Actual.Contains("TextType");
        return isAmountDivergence || isTextDivergence;
    }

    /// <summary>
    /// Strip elements that are known not to round-trip under the current FilingSubmission model.
    /// These are either (a) regenerated by the builder from UtcNow / Guid.NewGuid at emission
    /// time, or (b) emitted unconditionally by the builder but absent from certain baseline
    /// samples. The test asserts equivalence of everything ELSE — i.e., the wire-shape payload
    /// that carries semantic information.
    /// </summary>
    private static void StripKnownNonRoundTrippableElements(XDocument doc)
    {
        // DateTime inside DocumentFiledDate — baseline uses 2020-PST, builder uses current-UTC.
        foreach (var dt in doc.Descendants().Where(e => e.Name.LocalName == "DateTime").ToList())
        {
            dt.Remove();
        }

        // eFilingCaseFilingType — unconditional builder emission; absent from CIV-INI-001 baseline.
        foreach (var ft in doc.Descendants().Where(e => e.Name.LocalName == "eFilingCaseFilingType").ToList())
        {
            ft.Remove();
        }

        // appClientUsername / appClientParentUsername — unconditional when SubmitterUsername set;
        // absent from CIV-INI-001 baseline but present in ~8 other baseline CI samples.
        foreach (var uc in doc.Descendants()
            .Where(e => e.Name.LocalName is "appClientUsername" or "appClientParentUsername")
            .ToList())
        {
            uc.Remove();
        }

        // Empty <ContactInformation/> elements — baselines are inconsistent about emitting
        // these on parties without contact data (CIV-INI-001 defendant omits; CIV-INI-010
        // defendant emits empty). Our parser skips contacts with no children (HasElements=false),
        // and the builder doesn't emit an element for a null Contact. This is semantically
        // correct ("no contact data") but produces wire-shape mismatches with baselines that
        // emit the empty shell. Strip empty ContactInformation nodes from BOTH sides so the
        // presence-or-absence of the shell doesn't block otherwise-equivalent documents from
        // round-tripping. (Audit F-2 pattern — same "baseline inconsistency" as F-1.)
        // Note: HasAttributes includes xmlns declarations, which WILL be present on the empty
        // <ContactInformation xmlns:ci1="..."/> shell. Test for "no real attributes" by checking
        // that all attributes are namespace declarations.
        foreach (var ci in doc.Descendants()
            .Where(e => e.Name.LocalName == "ContactInformation"
                        && !e.HasElements
                        && e.Attributes().All(a => a.IsNamespaceDeclaration))
            .ToList())
        {
            ci.Remove();
        }
    }

    /// <summary>
    /// Normalize the child ordering of "order-insensitive containers" — element types whose
    /// children can appear in ANY order without changing the wire's semantic meaning.
    ///
    /// <para>
    /// Baselines themselves are INCONSISTENT about child ordering within <c>CaseParticipantExt</c>:
    /// some samples place <c>efmFeeExemptionRequestType</c> / <c>efmInterpreterLanguage</c> BEFORE
    /// <c>CaseParticipantRoleCode</c> (e.g., CIV-INI-004, -011, DCSS samples), while others place
    /// <c>eService</c> AFTER <c>ContactInformation</c> (e.g., MH LPS Conservatorship). This rules
    /// out the "all extensions first" or "XSD-canonical base-first" hypotheses — the JTI server
    /// evidently accepts arbitrary ordering, and baseline samples merely reflect the varying order
    /// in which the sample-generator's code happened to emit elements.
    /// </para>
    ///
    /// <para>
    /// Sorting both documents' <c>CaseParticipantExt</c> children alphabetically by local name
    /// before structural diff produces a canonical order that both sides will match, without
    /// requiring either a speculative builder change or an opt-in knob on <see cref="XmlStructuralDiff"/>.
    /// This is safer than forcing a specific order in the builder because the JTI live server
    /// is already known to accept both orderings (both produce successful Madera Tier B filings).
    /// </para>
    /// </summary>
    private static void NormalizeOrderInsensitiveContainerChildren(XDocument doc)
    {
        foreach (var container in doc.Descendants().Where(IsOrderInsensitiveContainer).ToList())
        {
            var sortedChildren = container.Elements()
                .OrderBy(e => e.Name.LocalName, StringComparer.Ordinal)
                .ToList();

            // Capture preserved-order attributes before clearing children, to preserve the
            // element itself (attributes stay attached to container; only element children move).
            foreach (var child in sortedChildren)
                child.Remove();

            foreach (var child in sortedChildren)
                container.Add(child);
        }
    }

    private static bool IsOrderInsensitiveContainer(XElement e)
    {
        // Empirically-verified order-insensitive containers. Expand as new scenarios reveal more.
        return e.Name.LocalName == "CaseParticipantExt";
    }
}
