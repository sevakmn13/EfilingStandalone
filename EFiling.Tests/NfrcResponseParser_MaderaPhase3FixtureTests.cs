using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Parsers;
using EFiling.Tests.SubsequentFilingRoundTrip;

namespace EFiling.Tests;

/// <summary>
/// Parser tests against the Madera Phase 3 NFRC fixture set (live captures from the
/// 2026-04-26 / 2026-04-27 audit submissions). Provenance + per-file context documented
/// at <c>docs/fileing files/ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Madera Phase 3/README.md</c>.
///
/// <para>
/// Audit Phase 4 sub-phase 4.1 — locks parser behavior against real Madera traffic
/// across the 4 captured shapes:
/// <list type="bullet">
///   <item><c>CC-Accept-NFRC1-ClerkAction.xml</c> (clerk-driven CC accept, NFRC #1 only)</item>
///   <item><c>CC-Accept-NFRC2-Financials.xml</c> (same filing, NFRC #2 with fees + RECEIPT court-doc)</item>
///   <item><c>CC-Reject-NFRC1-ClerkAction.xml</c> (auto-rejected CC submission)</item>
///   <item><c>SF-Reject-NFRC1-ClerkAction.xml</c> (auto-rejected SF submission)</item>
/// </list>
/// </para>
///
/// <para>
/// Companion to <see cref="NfrcResponseParser_LascFixtureTests"/> (LASC vendor samples).
/// LASC and Madera use distinct doc-control-ID conventions: LASC emits numeric
/// <c>DocumentFileControlID</c> values (<c>29888</c>, <c>390903</c>); Madera CC emits
/// GUID-style values (<c>doc-{guid}</c>) for filer-uploaded docs and short numerics
/// (<c>114582</c>) for court-generated; Madera SF emits short-numerics (<c>3504</c>) for
/// filer-uploaded too. Tests below pin all three shapes so the canonical-handle extractor
/// (B0b fix — Phase 5.3) doesn't accidentally regress on a vendor-shape edge case.
/// </para>
///
/// <para>
/// Privacy guard verification (Q23 fix — Phase 5.4): <b>both</b>
/// <c>CC-Accept-NFRC1</c> and <c>CC-Accept-NFRC2</c> contain a real Madera-injected
/// <c>&lt;messageToClerk&gt;hey wahts up clerk&lt;/messageToClerk&gt;</c> at envelope level
/// (a vendor test message captured live in production traffic — it propagates across
/// every NFRC for the same filing, since the message lives at the filing level not the
/// NFRC stage level). The two reject fixtures (CC-Reject and SF-Reject) emit neither
/// element, consistent with Madera's general auto-reject empty-rejection-reason pattern
/// (Q21 residual). Tests below validate the privacy guard against the real positive value
/// and document the negative shape for the reject paths.
/// </para>
/// </summary>
public class NfrcResponseParser_MaderaPhase3FixtureTests
{
    private const string MaderaPhase3Relative =
        "ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Madera Phase 3";

    private static string Path(string filename) => System.IO.Path.Combine(
        SampleLoader.RepoRoot,
        "docs",
        "fileing files",
        MaderaPhase3Relative.Replace('/', System.IO.Path.DirectorySeparatorChar),
        filename);

    private static string CcAcceptNfrc1Path => Path("CC-Accept-NFRC1-ClerkAction.xml");
    private static string CcAcceptNfrc2Path => Path("CC-Accept-NFRC2-Financials.xml");
    private static string CcRejectNfrc1Path => Path("CC-Reject-NFRC1-ClerkAction.xml");
    private static string SfRejectNfrc1Path => Path("SF-Reject-NFRC1-ClerkAction.xml");

    private static string LoadFixture(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Madera Phase 3 NFRC fixture not found at expected path: {path}. "
              + $"If the docs/ tree has been restructured, update {nameof(NfrcResponseParser_MaderaPhase3FixtureTests)}.",
                path);
        }
        return File.ReadAllText(path);
    }

    // ─── CC-Accept NFRC #1 (clerk action — accepts, no fees, no court-gen docs) ──

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_FixtureLoadable()
    {
        var xml = LoadFixture(CcAcceptNfrc1Path);
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("ReviewFilingCallbackMessageExt", xml);
        Assert.Contains("ReviewedLeadDocument", xml);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_FilingStatusIsAccepted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal("ACCEPTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
        Assert.Null(r.FilingRejectionReason);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_TwoFilerDocsExtracted_B0aRegression()
    {
        // 1 lead doc (Complaint) + 1 connected doc (Affidavit). NFRC #1 (clerk action)
        // does NOT include court-gen docs in Madera CC traffic — those arrive in NFRC #2
        // (Financials), see CC-Accept-NFRC2 fixture below for the expanded shape.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal(2, r.Documents.Count);
        Assert.Equal("Complaint", r.Documents[0].DocumentDescriptionText);
        Assert.Equal("Affidavit: 170.1 Disqualification ", r.Documents[1].DocumentDescriptionText);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_BothDocsFlaggedFilerUploaded_B0bDiscriminator()
    {
        // B0b fix (Phase 5.3): IsCourtGenerated discriminator re-grounded on NIEM
        // structures:id attribute presence. Madera CC NFRC #1 has both docs as filer-
        // uploaded (each carries ns1:id="114580" / "114581"). Pre-fix every doc was
        // misclassified as court-generated due to the never-firing FILING_ASSEMBLY_MDE
        // category check; this test verifies the post-fix discriminator works correctly
        // against real Madera traffic.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.All(r.Documents, d => Assert.False(d.IsCourtGenerated,
            $"Doc '{d.DocumentDescriptionText}' should be filer-uploaded (has NIEM structures:id)."));
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_CanonicalEfmDocumentIdHoldsGuidStyleHandle()
    {
        // B0b fix: EfmDocumentId now sourced from <DocumentFileControlID> (canonical EFM
        // handle). Madera CC uses GUID-style FileControlIDs ("doc-{guid}") for filer-uploaded
        // docs — distinct from LASC's numeric handles. Pin both observed shapes so the
        // extractor doesn't regress on either vendor convention.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal("doc-5d66e48e4b174f23bc5af69515ccc7ba", r.Documents[0].EfmDocumentId);
        Assert.Equal("doc-8b02ebf700244cf68e41ab81cb77fe31", r.Documents[1].EfmDocumentId);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_DocumentCodeHoldsNumericTypeCodes()
    {
        // B0b fix: bare <IdentificationID> values (no category) populate the new
        // DocumentCode field. Madera uses numeric-string doc-type codes — distinct from
        // LASC's alpha-prefix codes (COM040, EFM001). Document the observed Madera codes
        // for traceability:
        //   425110 = Complaint (Madera CC initial pleading code)
        //   479420 = Affidavit: 170.1 Disqualification (Madera CC supporting affidavit code)
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal("425110", r.Documents[0].DocumentCode);    // Complaint
        Assert.Equal("479420", r.Documents[1].DocumentCode);    // Affidavit: 170.1 Disqualification
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_MessageToClerkPropagated_PrivacyGuardHolds()
    {
        // Q23 fix (Phase 5.4) verified against REAL Madera traffic at NFRC #1 stage.
        //
        // Empirical finding: Madera propagates the Madera-injected <messageToClerk>hey
        // wahts up clerk</messageToClerk> across BOTH NFRC #1 and NFRC #2 for the same
        // filing — the message lives at the filing level (CoreFilingMessageExtType
        // submission element) and the EFM mirrors it to every NFRC envelope. This is
        // operationally important: messageToClerk is NOT an NFRC-stage-specific value;
        // it's a per-filing context that surfaces in every callback for that filing.
        //
        // Privacy guard contract: parser captures messageToClerk into NfrcResult.MessageToClerk
        // for audit, but it MUST NOT have leaked into FilingRejectionReason or any other
        // filer-visible aggregate at parser level. Controller-level guard (folding only
        // MessageToFiler into ErrorText) is verified separately in controller tests.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal("hey wahts up clerk", r.MessageToClerk);
        Assert.Null(r.MessageToFiler);

        // Privacy guard at parser level — clerk message must not have polluted any
        // filer-visible aggregate.
        Assert.Null(r.FilingRejectionReason);

        // Per-doc: NFRC #1 docs do not carry per-doc messages (the test message is at
        // envelope level only).
        Assert.All(r.Documents, d =>
        {
            Assert.Null(d.MessageToFiler);
            Assert.Null(d.MessageToClerk);
        });
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_EfmReferenceIdExtracted()
    {
        // Envelope-level FILING_REVIEW_MDE category — Madera populates this with the
        // 26MA00003990 reference per README provenance.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal("26MA00003990", r.EfmReferenceId);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc1_NoLineItemsInClerkActionStage()
    {
        // NFRC #1 is the clerk-action stage; fee calculation runs in Madera's
        // post-clerk pipeline and arrives in NFRC #2. Empirically Madera DOES emit
        // <FeesCalculationAmount>0</FeesCalculationAmount> at NFRC #1 (zero, not absent),
        // with no AllowanceCharge children. Mirrors the LASC NFRC #1 shape exactly
        // (see Parse_LascNfrc1_NoFeesInSample). Parser correctly extracts TotalFees=0m
        // (not null) and an empty FeeLineItems list.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        Assert.Equal(0m, r.TotalFees);
        Assert.Empty(r.FeeLineItems);
    }

    // ─── CC-Accept NFRC #2 (financials — accepts with fees + RECEIPT court-gen) ──

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_FilingStatusIsAccepted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.Equal("ACCEPTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_ThreeDocsExtracted()
    {
        // 1 lead (Complaint) + 2 connected (Affidavit + RECEIPT court-gen) = 3 docs.
        // Same filing as NFRC #1; the RECEIPT is the post-fees court-generated artifact.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.Equal(3, r.Documents.Count);
        Assert.Equal("Complaint", r.Documents[0].DocumentDescriptionText);
        Assert.Equal("Affidavit: 170.1 Disqualification ", r.Documents[1].DocumentDescriptionText);
        Assert.Equal("RECEIPT", r.Documents[2].DocumentDescriptionText);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_FilerVsCourtDiscriminationCorrect_B0bDiscriminator()
    {
        // B0b fix (Phase 5.3): NIEM structures:id attribute presence discriminates
        // filer-uploaded from court-generated. NFRC #2 has 2 filer-uploaded (Complaint +
        // Affidavit, both carry ns1:id from NFRC #1) plus 1 court-generated RECEIPT
        // (no ns1:id). Verifies the discriminator handles the mixed-shape case correctly.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.False(r.Documents[0].IsCourtGenerated, "Complaint (filer-uploaded)");
        Assert.False(r.Documents[1].IsCourtGenerated, "Affidavit (filer-uploaded)");
        Assert.True(r.Documents[2].IsCourtGenerated, "RECEIPT (court-generated)");
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_FilerDocsRetainSameFileControlIdAsNfrc1()
    {
        // Cross-NFRC stability invariant: filer-uploaded docs keep the same canonical
        // DocumentFileControlID across NFRC #1 → NFRC #2 stages. This is what makes Q17's
        // primary-match (nfrcDoc.EfmDocumentId → existing.FileControlId) work for
        // multi-stage NFRC processing on the same filing — without stable IDs, every NFRC
        // would treat filer docs as new court-gen rows.
        var nfrc1 = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc1Path));
        var nfrc2 = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.Equal(nfrc1.Documents[0].EfmDocumentId, nfrc2.Documents[0].EfmDocumentId);
        Assert.Equal(nfrc1.Documents[1].EfmDocumentId, nfrc2.Documents[1].EfmDocumentId);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_CourtGenReceiptHasNumericFileControlId()
    {
        // Court-generated docs use a different ID space than filer-uploaded ones in
        // Madera CC. RECEIPT here has FileControlID "114582" (short numeric), distinct
        // from the filer GUIDs ("doc-{...}"). Q17's dedup keying on canonical
        // EfmDocumentId works because these spaces don't collide.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.Equal("114582", r.Documents[2].EfmDocumentId);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_ReceiptUrlExtracted()
    {
        // FindReceipt() requires IsCourtGenerated=true + DocumentDescriptionText="RECEIPT"
        // + non-empty BinaryLocationUri. All three hold for Madera RECEIPT post-B0b-fix.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.False(string.IsNullOrEmpty(r.ReceiptUrl),
            "Madera CC NFRC #2 RECEIPT URL must be extracted post-B0b-fix.");
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_FeesAggregateExtracted()
    {
        // Madera CC accept charges $1438.50 in this fixture. Tests TotalFees aggregate
        // extraction (independent of per-line items, though both should be present).
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));
        Assert.NotNull(r.TotalFees);
        Assert.Equal(1438.50m, r.TotalFees!.Value);
    }

    [Fact]
    public void Parse_MaderaCcAcceptNfrc2_MessageToClerkExtracted_NotLeakedToFilerVisibleFields()
    {
        // Q23 fix (Phase 5.4) verified against REAL Madera traffic.
        //
        // CC-Accept-NFRC2 contains a Madera-injected test message
        // <messageToClerk>hey wahts up clerk</messageToClerk> at envelope level (this is
        // a vendor-side test value preserved in our captured fixture — proves Madera DOES
        // populate messageToClerk in some traffic, contrary to the 0/25 historical-corpus
        // statistic in § 15.6 Q23).
        //
        // Privacy guard contract: parser captures messageToClerk into NfrcResult.MessageToClerk
        // for audit purposes, but it MUST NOT have leaked into FilingRejectionReason or any
        // other filer-visible aggregate at parser level. Controller-level guard (folding
        // only MessageToFiler into ErrorText) is verified separately in controller tests.
        var r = NfrcResponseParser.Parse(LoadFixture(CcAcceptNfrc2Path));

        Assert.Equal("hey wahts up clerk", r.MessageToClerk);
        Assert.Null(r.MessageToFiler);

        // Privacy guard at parser level — clerk message must not have polluted any
        // filer-visible aggregate.
        Assert.Null(r.FilingRejectionReason);
    }

    // ─── CC-Reject NFRC #1 (auto-rejected CC submission) ────────────────────────

    [Fact]
    public void Parse_MaderaCcRejectNfrc1_FilingStatusIsRejected()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(CcRejectNfrc1Path));
        Assert.Equal("REJECTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Rejected, r.FilingStatus);
    }

    [Fact]
    public void Parse_MaderaCcRejectNfrc1_OneFilerDocExtracted()
    {
        // Auto-rejected CC submission with a single TRO doc. Note: the lead doc
        // description has a trailing space ("Application/Declaration: TRO ") in the
        // raw Madera XML — verifying we don't accidentally trim it (consumers may rely
        // on byte-exact preservation).
        var r = NfrcResponseParser.Parse(LoadFixture(CcRejectNfrc1Path));
        Assert.Single(r.Documents);
        Assert.Equal("Application/Declaration: TRO ", r.Documents[0].DocumentDescriptionText);
        Assert.False(r.Documents[0].IsCourtGenerated, "TRO doc was filer-uploaded");
    }

    [Fact]
    public void Parse_MaderaCcRejectNfrc1_FilerDocCanonicalIdExtracted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(CcRejectNfrc1Path));
        Assert.Equal("doc-586990e33d97453e88dffa8f71107ea6", r.Documents[0].EfmDocumentId);
    }

    [Fact]
    public void Parse_MaderaCcRejectNfrc1_NoFilingRejectionReason_Q21Residual()
    {
        // Q21 residual marker: Madera auto-rejects emit empty <FilingStatus> blocks
        // (no <RejectReasonCode>, no <FilingStatusReason>, no <ReasonText>) and empty
        // <messageToFiler>. Parser correctly extracts nothing — the absence is a
        // Madera-side population gap, not a parser gap (audit § 15.6 Q21).
        //
        // When Madera UI access becomes available and we capture a clerk-driven
        // (non-auto) reject NFRC, this test should be revisited: the new fixture will
        // likely have FilingStatusReason/messageToFiler populated, at which point the
        // Q21 residual closes and we'd add a positive-assertion test instead.
        var r = NfrcResponseParser.Parse(LoadFixture(CcRejectNfrc1Path));
        Assert.Null(r.FilingRejectionReason);
        Assert.Null(r.MessageToFiler);
        Assert.Null(r.MessageToClerk);
    }

    [Fact]
    public void Parse_MaderaCcRejectNfrc1_EfmReferenceIdExtracted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(CcRejectNfrc1Path));
        Assert.Equal("26MA00004477", r.EfmReferenceId);
    }

    // ─── SF-Reject NFRC #1 (auto-rejected SF submission) ────────────────────────

    [Fact]
    public void Parse_MaderaSfRejectNfrc1_FilingStatusIsRejected()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(SfRejectNfrc1Path));
        Assert.Equal("REJECTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Rejected, r.FilingStatus);
    }

    [Fact]
    public void Parse_MaderaSfRejectNfrc1_OneFilerDocWithNumericFileControlId()
    {
        // SF submissions on Madera use SHORT NUMERIC FileControlIDs (e.g., "3504") for
        // filer-uploaded docs — distinct from CC's GUID-style ("doc-{guid}"). This
        // confirms B0b's DocumentFileControlID extraction handles both Madera shapes
        // (and LASC's longer numerics like "29888"). Doc-type code "258110" = "Response"
        // per § 15.4.1 A6.
        var r = NfrcResponseParser.Parse(LoadFixture(SfRejectNfrc1Path));
        Assert.Single(r.Documents);
        var d = r.Documents[0];
        Assert.Equal("Response ", d.DocumentDescriptionText);
        Assert.Equal("3504", d.EfmDocumentId);
        Assert.Equal("258110", d.DocumentCode);
        Assert.False(d.IsCourtGenerated, "Response doc was filer-uploaded (has ns1:id)");
    }

    [Fact]
    public void Parse_MaderaSfRejectNfrc1_NoFilingRejectionReason_Q21Residual()
    {
        // Same Q21 residual as CC-Reject — Madera auto-rejects don't populate filing-
        // level rejection reasons regardless of submission shape (CC vs SF).
        var r = NfrcResponseParser.Parse(LoadFixture(SfRejectNfrc1Path));
        Assert.Null(r.FilingRejectionReason);
        Assert.Null(r.MessageToFiler);
        Assert.Null(r.MessageToClerk);
    }

    [Fact]
    public void Parse_MaderaSfRejectNfrc1_EfmReferenceIdExtracted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(SfRejectNfrc1Path));
        Assert.Equal("26MA00004476", r.EfmReferenceId);
    }
}
