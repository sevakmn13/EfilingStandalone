using EFiling.Core.Enums;
using EFiling.Providers.JTI.Parsers;
using EFiling.Tests.SubsequentFilingRoundTrip;

namespace EFiling.Tests;

/// <summary>
/// Parser tests against the hand-synthesized NFRC #3 fixture at
/// <c>docs/fileing files/ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Synthetic/NFRC#3-StipulationOrder-Synthetic.xml</c>.
///
/// <para>
/// <b>Why synthetic.</b> Q22-A in the NFRC audit (§ 13 + § 15.4.1 C1) is the
/// vendor-blocked question of whether Madera production fires NFRC #3 at all.
/// 0 of 25 captured Madera payloads contain <c>&lt;DocumentDispositionType&gt;</c> —
/// Madera staging never emitted NFRC #3 across all observed traffic, and the vendor
/// CDN samples (<c>sub_stiporder_nfrc3_*.xml</c>) return HTTP 403 Forbidden. So
/// our NFRC #3 parser/controller paths cannot be empirically verified against
/// real Madera fixtures. This fixture closes the structural gap (Q22-B/C) using
/// the authoritative Tier-1 sources (WSDL + ECF 4.0 + LASC NFRC #2 baseline)
/// plus vendor-prose semantics for NFRC #3 deltas.
/// </para>
///
/// <para>
/// <b>What the fixture exercises.</b> 5 documents covering the full NFRC #3
/// contract surface: (1) filer-submitted lead Stipulation+Order with GRA
/// disposition + FG status transition + NIEM-wrapper date; (2) filer-submitted
/// Memorandum with NO disposition (non-pleading paper baseline); (3) court-issued
/// Signed Order as new court-gen artifact with ORD + court-gen identifiers;
/// (4) RECEIPT carried forward from NFRC #2 superset; (5) Endorsed Filed-Stamped
/// Copy with OAI + direct-text date (parser fallback shape). Envelope-level
/// FilingStatusCode stays at ACCEPTED per § 15.1 design decision.
/// </para>
///
/// <para>
/// Companion to <see cref="NfrcResponseParser_LascFixtureTests"/> (real LASC
/// NFRC #1 + #2 samples) and <see cref="NfrcResponseParser_MaderaPhase3FixtureTests"/>
/// (real Madera staging captures). This class is the only fixture-anchored
/// surface for NFRC #3 contract testing until vendor unblocks Q22-A.
/// </para>
/// </summary>
public class NfrcResponseParser_SyntheticNfrc3FixtureTests
{
    private const string SyntheticNfrcSamplesRelative =
        "ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Synthetic";

    private static string Nfrc3Path => Path.Combine(
        SampleLoader.RepoRoot,
        "docs",
        "fileing files",
        SyntheticNfrcSamplesRelative.Replace('/', Path.DirectorySeparatorChar),
        "NFRC#3-StipulationOrder-Synthetic.xml");

    private static string LoadFixture()
    {
        if (!File.Exists(Nfrc3Path))
        {
            throw new FileNotFoundException(
                $"Synthetic NFRC #3 fixture not found at expected path: {Nfrc3Path}. "
              + $"If the docs/ tree has been restructured, update {nameof(NfrcResponseParser_SyntheticNfrc3FixtureTests)}.",
                Nfrc3Path);
        }
        return File.ReadAllText(Nfrc3Path);
    }

    // ─── Sanity check ──────────────────────────────────────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_FixtureIsLoadable()
    {
        var xml = LoadFixture();
        Assert.False(string.IsNullOrWhiteSpace(xml));
        // Verify the fixture actually carries NFRC #3 -specific markers.
        Assert.Contains("DocumentDispositionType", xml);
        Assert.Contains("DocumentDispositionDate", xml);
        Assert.Contains("ReviewFilingCallbackMessageExt", xml);
        Assert.Contains("ReviewedLeadDocument", xml);
        Assert.Contains("ReviewedConnectedDocument", xml);
        // SyntheticFixture banner present in header comment.
        Assert.Contains("[SyntheticFixture]", xml);
    }

    // ─── Envelope-level fields ────────────────────────────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_EnvelopeFilingStatusStaysAccepted()
    {
        // Design decision (audit § 15.1): envelope-level FilingStatusCode stays at the
        // NFRC #2 terminal value (ACCEPTED) on NFRC #3. Vendor prose's "filing status
        // is now filed ('F')" describes per-doc StatusText, not envelope status —
        // the envelope's 7-value enum (per ECF 4.0) doesn't include "filed".
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal("ACCEPTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
    }

    [Fact]
    public void Parse_SyntheticNfrc3_MdeIdsAllThree()
    {
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal("EFSP-SYNTH-NFRC3-001", r.EfspReferenceId);
        Assert.Equal("EFM-SYNTH-NFRC3-77777", r.EfmReferenceId);
        Assert.Equal("CMS-CASE-4033996-NFRC3", r.CmsReferenceId);
    }

    [Fact]
    public void Parse_SyntheticNfrc3_CaseInfoExtracted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal("WILLIAM HOLLIDAY vs STARBUCKS", r.CaseTitle);
        Assert.Equal("82683", r.CaseTrackingId);
        Assert.Equal("4033996", r.CaseDocketId);
    }

    // ─── Documents: count + identity ──────────────────────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_AllFiveDocumentsExtracted()
    {
        // Fixture has 1 ReviewedLeadDocument + 4 ReviewedConnectedDocument; parser
        // must merge them into a single Documents list (post-B0a fix behavior).
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal(5, r.Documents.Count);
    }

    [Fact]
    public void Parse_SyntheticNfrc3_DocumentDescriptionsInOrder()
    {
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal("Stipulation and Order", r.Documents[0].DocumentDescriptionText);
        Assert.Equal("Memorandum of Points and Authorities", r.Documents[1].DocumentDescriptionText);
        Assert.Equal("Signed Order Granting Stipulation", r.Documents[2].DocumentDescriptionText);
        Assert.Equal("RECEIPT", r.Documents[3].DocumentDescriptionText);
        Assert.Equal("Endorsed Filed-Stamped Copy", r.Documents[4].DocumentDescriptionText);
    }

    // ─── Lead doc: full NFRC #3 disposition contract ──────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_LeadDocStipulationOrder_HasFullDispositionContract()
    {
        // The lead doc is the previously-RP pleading paper that the judge ruled on.
        // Verifies all NFRC-#3-specific fields land coherently:
        //   - StatusText FG (transitioned from RP at NFRC #1)
        //   - DocumentDispositionType GRA
        //   - DocumentDispositionDate 2026-05-15T14:30 PST → 22:30 UTC
        //   - DocumentFilingStatusCode ACCEPTED (carried from NFRC #2 superset)
        //   - Canonical EfmDocumentId = "500001" (matches DocumentFileControlID)
        var r = NfrcResponseParser.Parse(LoadFixture());
        var lead = r.Documents[0];
        Assert.Equal("Stipulation and Order", lead.DocumentDescriptionText);
        Assert.Equal("500001", lead.EfmDocumentId);
        Assert.Equal("FG", lead.DocumentStatusText);
        Assert.Equal("GRA", lead.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), lead.DocumentDispositionDate);
        Assert.Equal(DateTimeKind.Utc, lead.DocumentDispositionDate!.Value.Kind);
        Assert.Equal("ACCEPTED", lead.DocumentFilingStatusCode);
    }

    // ─── Non-pleading-paper doc: no disposition fields ────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_NonPleadingPaperDoc_NoDispositionFields()
    {
        // The Memorandum is a supporting brief, not a pleading paper requiring
        // judicial decision. Parser must NOT pollute its disposition fields when
        // the source XML omits them.
        var r = NfrcResponseParser.Parse(LoadFixture());
        var memo = r.Documents[1];
        Assert.Equal("Memorandum of Points and Authorities", memo.DocumentDescriptionText);
        Assert.Equal("F", memo.DocumentStatusText);
        Assert.Null(memo.DocumentDispositionType);
        Assert.Null(memo.DocumentDispositionDate);
    }

    // ─── Court-issued judicial doc: new NFRC #3 court-gen artifact ────

    [Fact]
    public void Parse_SyntheticNfrc3_CourtIssuedSignedOrder_HasOrdDisposition()
    {
        // The judge's signed Order PDF is a new court-generated artifact introduced
        // at NFRC #3 (it didn't exist at NFRC #1 or #2). It carries:
        //   - Disposition type ORD (Ordered)
        //   - Court-gen identifiers (no NIEM structures:id attribute)
        //   - Canonical EfmDocumentId from <DocumentFileControlID>
        var r = NfrcResponseParser.Parse(LoadFixture());
        var signed = r.Documents[2];
        Assert.Equal("Signed Order Granting Stipulation", signed.DocumentDescriptionText);
        Assert.Equal("500900", signed.EfmDocumentId);
        Assert.True(signed.IsCourtGenerated);
        Assert.Equal("ORD", signed.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), signed.DocumentDispositionDate);
    }

    // ─── Receipt: carried forward from NFRC #2 superset ───────────────

    [Fact]
    public void Parse_SyntheticNfrc3_ReceiptDocPreservedFromNfrc2Superset()
    {
        // RECEIPT doc exists in NFRC #2 and must persist into NFRC #3 per the
        // superset rule. Parser identifies it via DocumentDescriptionText="RECEIPT"
        // + IsCourtGenerated=true, sets r.ReceiptUrl from BinaryLocationURI.
        var r = NfrcResponseParser.Parse(LoadFixture());
        var receipt = r.Documents[3];
        Assert.Equal("RECEIPT", receipt.DocumentDescriptionText);
        Assert.True(receipt.IsCourtGenerated);
        Assert.Null(receipt.DocumentDispositionType);  // RECEIPT doesn't carry dispositions
        Assert.Null(receipt.DocumentDispositionDate);
        Assert.NotNull(r.ReceiptUrl);
        Assert.Contains("receipt.pdf", r.ReceiptUrl);
    }

    // ─── Direct-text disposition date fallback shape ──────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_DirectTextDispositionDate_ParsedCorrectly()
    {
        // The Endorsed Filed-Stamped Copy uses the direct-text date shape
        // (<DocumentDispositionDate>2026-05-15</DocumentDispositionDate>) — no NIEM
        // wrapper. Exercises the parser's backward-compat fallback path.
        // 2026-05-15 without TZ → AssumeUniversal → midnight UTC.
        var r = NfrcResponseParser.Parse(LoadFixture());
        var stamped = r.Documents[4];
        Assert.Equal("Endorsed Filed-Stamped Copy", stamped.DocumentDescriptionText);
        Assert.Equal("OAI", stamped.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), stamped.DocumentDispositionDate);
        Assert.Equal(DateTimeKind.Utc, stamped.DocumentDispositionDate!.Value.Kind);
    }

    // ─── Fees: NFRC #2 superset preservation ──────────────────────────

    [Fact]
    public void Parse_SyntheticNfrc3_FeesCarriedForwardFromNfrc2Superset()
    {
        // NFRC #3 doesn't add new fees in typical Stipulation+Order workflows; the
        // NFRC #2 FeesCalculation block is preserved verbatim per superset rule.
        var r = NfrcResponseParser.Parse(LoadFixture());
        Assert.Equal(386.92m, r.TotalFees);
        Assert.Single(r.FeeLineItems);
        Assert.Equal(370m, r.FeeLineItems[0].Amount);
    }

    // ─── Cross-doc independence: dispositions don't bleed ─────────────

    [Fact]
    public void Parse_SyntheticNfrc3_DispositionFieldsAreDocLocal()
    {
        // The 5 docs have a mix of GRA / null / ORD / null / OAI dispositions.
        // Parser's ByLocalFirst(<DocumentDispositionType>) must read only the
        // current doc's direct child — never a sibling or descendant.
        var r = NfrcResponseParser.Parse(LoadFixture());
        var dispositions = r.Documents.Select(d => d.DocumentDispositionType).ToArray();
        Assert.Equal(new[] { "GRA", null, "ORD", null, "OAI" }, dispositions);
    }
}
