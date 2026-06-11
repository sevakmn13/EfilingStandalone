using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Parsers;
using EFiling.Tests.SubsequentFilingRoundTrip;

namespace EFiling.Tests;

/// <summary>
/// Parser tests against the canonical LASC NFRC vendor samples shipped under
/// <c>docs/fileing files/ECF Operations/NotifyFilingReviewComplete/NFRC Samples/</c>.
///
/// <para>
/// Companion to <see cref="NfrcResponseParserTests"/> which exercises the parser against
/// hand-built synthetic XML. The synthetic tests build their bodies with
/// <c>&lt;ReviewedDocument&gt;</c> wrappers — but real JTI NFRCs use
/// <c>&lt;ReviewedLeadDocument&gt;</c> + <c>&lt;ReviewedConnectedDocument&gt;</c>
/// (per WSDL <c>ReviewFilingCallbackMessageExtType</c>). Until B0a was fixed
/// (NFRC audit § 15.6 — 2026-04-26), the parser silently produced
/// <c>NfrcResult.Documents.Count == 0</c> on every real callback. These fixture tests
/// pin the post-B0a-fix behavior so the regression cannot recur.
/// </para>
///
/// <para>
/// B0b was closed in Phase 5.3 (NFRC audit § 15.6 — 2026-04-28). The parser now extracts
/// <c>&lt;DocumentFileControlID&gt;</c> into <see cref="NfrcDocumentResult.EfmDocumentId"/>
/// (canonical EFM handle: <c>29888</c>, <c>390903</c>, …); the bare
/// <c>&lt;IdentificationID&gt;</c> with no category goes into the new
/// <see cref="NfrcDocumentResult.DocumentCode"/> field (doc-type code: <c>COM040</c>,
/// <c>EFM001</c>, …). The <see cref="NfrcDocumentResult.IsCourtGenerated"/> heuristic was
/// re-grounded on NIEM <c>structures:id</c> attribute presence (filer-uploaded docs carry
/// it; court-emitted docs don't). Tests below verify the post-fix invariants.
/// </para>
/// </summary>
public class NfrcResponseParser_LascFixtureTests
{
    private const string NfrcSamplesRelative =
        "ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Basic Case Initiation";

    private static string Nfrc1Path => Path.Combine(
        SampleLoader.RepoRoot,
        "docs",
        "fileing files",
        NfrcSamplesRelative.Replace('/', Path.DirectorySeparatorChar),
        "NFRC #1 - Clerk Action Sample.xml");

    private static string Nfrc2Path => Path.Combine(
        SampleLoader.RepoRoot,
        "docs",
        "fileing files",
        NfrcSamplesRelative.Replace('/', Path.DirectorySeparatorChar),
        "NFRC #2 - Financials Sample.xml");

    private static string LoadFixture(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"NFRC fixture not found at expected path: {path}. "
              + $"If the docs/ tree has been restructured, update {nameof(NfrcResponseParser_LascFixtureTests)}.",
                path);
        }
        return File.ReadAllText(path);
    }

    // ─── NFRC #1 — Clerk Action Sample (no fees, accepted) ──────────────────

    [Fact]
    public void Parse_LascNfrc1_FixtureIsLoadable()
    {
        // Sanity check that the path-resolution + sentinel walk works for these samples.
        var xml = LoadFixture(Nfrc1Path);
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("ReviewFilingCallbackMessageExt", xml);
        Assert.Contains("ReviewedLeadDocument", xml);
        Assert.Contains("ReviewedConnectedDocument", xml);
    }

    [Fact]
    public void Parse_LascNfrc1_FilingStatusIsAccepted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Equal("ACCEPTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
        Assert.Null(r.FilingRejectionReason);
    }

    [Fact]
    public void Parse_LascNfrc1_AllSixDocumentsExtracted_B0aRegression()
    {
        // B0a regression cover. Pre-fix: the parser searched <ReviewedDocument> /
        // <ReviewedDocumentExt> and silently produced Documents.Count == 0 because real
        // NFRCs use <ReviewedLeadDocument> + <ReviewedConnectedDocument>. Post-fix this
        // sample yields exactly 6 docs (1 lead + 5 connected).
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Equal(6, r.Documents.Count);
    }

    [Fact]
    public void Parse_LascNfrc1_LeadDocIsComplaint()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        // Order is preserved: lead emits first, then connected docs in document order.
        Assert.Equal("Complaint", r.Documents[0].DocumentDescriptionText);
        Assert.Equal("Summons on Complaint", r.Documents[1].DocumentDescriptionText);
        Assert.Equal("Civil Case Cover Sheet", r.Documents[2].DocumentDescriptionText);
        Assert.Equal("Notice of E-Filing Confirmation", r.Documents[3].DocumentDescriptionText);
        Assert.Equal("Notice of Case Assignment - Limited Civil Case", r.Documents[4].DocumentDescriptionText);
        Assert.Equal("First Amended Standing Order", r.Documents[5].DocumentDescriptionText);
    }

    [Fact]
    public void Parse_LascNfrc1_AllDocsAcceptedStatus()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.All(r.Documents, d =>
        {
            Assert.Equal("ACCEPTED", d.DocumentFilingStatusCode);
            Assert.Null(d.RejectionReasonText);
        });
    }

    [Fact]
    public void Parse_LascNfrc1_NoReceiptUrlBecauseSampleHasNoReceiptDoc()
    {
        // NFRC #1 has 5 connected docs; none has DocumentDescriptionText="RECEIPT".
        // (The receipt doc shows up in NFRC #2 once fees are involved.)
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Null(r.ReceiptUrl);
    }

    [Fact]
    public void Parse_LascNfrc1_NoFeesInSample()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        // Sample has FeesCalculation/FeesCalculationAmount=0 and no AllowanceCharge children.
        Assert.Equal(0m, r.TotalFees);
        Assert.Empty(r.FeeLineItems);
    }

    [Fact]
    public void Parse_LascNfrc1_BinaryLocationUriExtractedFromEachDoc()
    {
        // The sample uses the placeholder "URL_HERE" for every BinaryLocationURI. We assert
        // that the parser actually extracts it (not null) for every doc — this is the per-doc
        // data path that was 100% silently dropped pre-B0a-fix.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.All(r.Documents, d => Assert.Equal("URL_HERE", d.BinaryLocationUri));
    }

    [Fact]
    public void Parse_LascNfrc1_DocumentStatusTextExtracted()
    {
        // <DocumentStatus><StatusText>F</StatusText></DocumentStatus> on the lead doc.
        // Connected[3] (Notice of E-Filing Confirmation) has empty <StatusText/>.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Equal("F", r.Documents[0].DocumentStatusText);   // Complaint
        Assert.Equal("IF", r.Documents[1].DocumentStatusText);  // Summons
        Assert.Equal("F", r.Documents[2].DocumentStatusText);   // Civil Case Cover Sheet
    }

    [Fact]
    public void Parse_LascNfrc1_B0bClosed_FilerVsCourtDiscriminationCorrect()
    {
        // B0b closed (Phase 5.3): IsCourtGenerated heuristic re-grounded on NIEM
        // structures:id attribute presence (filer-uploaded docs carry the attribute
        // because they're cross-referenced from <DocumentRendition>/<CourtEventDocument>
        // blocks elsewhere in the NFRC envelope; court-emitted docs are not cross-referenced).
        // Pre-fix this test asserted the broken behavior (every doc misclassified as
        // court-generated); post-fix it verifies the discriminator works correctly.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));

        // Filer-uploaded docs (lead + first 2 connected): IsCourtGenerated == false
        Assert.False(r.Documents[0].IsCourtGenerated, "Complaint (filer-uploaded) — should NOT be flagged court-generated");
        Assert.False(r.Documents[1].IsCourtGenerated, "Summons on Complaint (filer-uploaded) — should NOT be flagged court-generated");
        Assert.False(r.Documents[2].IsCourtGenerated, "Civil Case Cover Sheet (filer-uploaded) — should NOT be flagged court-generated");

        // Court-generated docs (last 3 connected): IsCourtGenerated == true
        Assert.True(r.Documents[3].IsCourtGenerated, "Notice of E-Filing Confirmation — should be flagged court-generated");
        Assert.True(r.Documents[4].IsCourtGenerated, "Notice of Case Assignment - Limited Civil Case — should be flagged court-generated");
        Assert.True(r.Documents[5].IsCourtGenerated, "First Amended Standing Order — should be flagged court-generated");

        // EfspDocumentId remains null for all docs in the LASC fixture (no FILING_ASSEMBLY_MDE
        // category text per-doc — the typical real-JTI shape). Documenting this stays so
        // any future change that introduces per-doc category text fires the heuristic
        // backward-compat path instead of silently flipping the discriminator.
        Assert.All(r.Documents, d => Assert.Null(d.EfspDocumentId));
    }

    [Fact]
    public void Parse_LascNfrc1_B0bClosed_EfmDocumentIdHoldsCanonicalFileControlId()
    {
        // B0b closed (Phase 5.3): EfmDocumentId now sourced from <DocumentFileControlID>
        // (canonical EFM per-document handle) rather than the bare <IdentificationID>
        // (which holds the doc-type code). Pre-fix this test asserted the broken behavior
        // (type codes in EfmDocumentId); post-fix it verifies the canonical handles are
        // extracted correctly.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Equal("29888", r.Documents[0].EfmDocumentId);    // Complaint canonical handle
        Assert.Equal("29889", r.Documents[1].EfmDocumentId);    // Summons canonical handle
        Assert.Equal("29890", r.Documents[2].EfmDocumentId);    // Civil Case Cover Sheet canonical handle
        Assert.Equal("390903", r.Documents[3].EfmDocumentId);   // Notice of E-Filing canonical handle
        Assert.Equal("390904", r.Documents[4].EfmDocumentId);   // Notice of Case Assignment canonical handle
        Assert.Equal("390905", r.Documents[5].EfmDocumentId);   // First Amended Standing Order canonical handle
    }

    [Fact]
    public void Parse_LascNfrc1_B0bClosed_DocumentCodeHoldsTypeCodes()
    {
        // B0b closed (Phase 5.3): bare <IdentificationID> values (with no
        // <IdentificationCategoryText>) now populate the new DocumentCode field rather
        // than polluting EfmDocumentId. Real JTI samples emit doc-type codes here:
        // COM040 (Complaint), ISS030 (Summons), MISC020 (Civil Case Cover Sheet),
        // EFM001 (Notice of E-Filing), NTC040L (Notice of Case Assignment),
        // ADM113 (First Amended Standing Order). Verifies the pre-fix-residual values
        // are still captured — just in the right field now.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc1Path));
        Assert.Equal("COM040", r.Documents[0].DocumentCode);
        Assert.Equal("ISS030", r.Documents[1].DocumentCode);
        Assert.Equal("MISC020", r.Documents[2].DocumentCode);
        Assert.Equal("EFM001", r.Documents[3].DocumentCode);
        Assert.Equal("NTC040L", r.Documents[4].DocumentCode);
        Assert.Equal("ADM113", r.Documents[5].DocumentCode);
    }

    // ─── NFRC #2 — Financials Sample (fees + receipt) ───────────────────────

    [Fact]
    public void Parse_LascNfrc2_FilingStatusIsAccepted()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        Assert.Equal("ACCEPTED", r.FilingStatusCode);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
    }

    [Fact]
    public void Parse_LascNfrc2_AllSevenDocumentsExtracted_B0aRegression()
    {
        // NFRC #2 mirrors NFRC #1 structurally and adds a 7th connected doc: RECEIPT.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        Assert.Equal(7, r.Documents.Count);
    }

    [Fact]
    public void Parse_LascNfrc2_LastDocIsReceipt()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        Assert.Equal("Complaint", r.Documents[0].DocumentDescriptionText);
        Assert.Equal("RECEIPT", r.Documents[6].DocumentDescriptionText);
    }

    [Fact]
    public void Parse_LascNfrc2_ReceiptUrlDetected()
    {
        // FindReceipt() requires IsCourtGenerated=true + DocumentDescriptionText="RECEIPT" +
        // non-empty BinaryLocationUri. Post-B0b-fix the RECEIPT doc is correctly classified
        // as court-generated (no NIEM structures:id attribute), so all three conditions hold.
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        Assert.Equal("URL_HERE", r.ReceiptUrl);
    }

    [Fact]
    public void Parse_LascNfrc2_FeesTotalAndLineItems()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        // Sample's FeesCalculationAmount has IEEE-754 precision tail
        // ("386.92000000000001591615728102624416351318359375") — decimal.TryParse
        // accepts long fractional strings up to its 28-29-digit precision and rounds.
        // We assert a tolerance window rather than exact equality.
        Assert.NotNull(r.TotalFees);
        Assert.InRange(r.TotalFees!.Value, 386.91m, 386.93m);

        // Four AllowanceCharge children: filing fee + convenience + court tx + cc tx.
        Assert.Equal(4, r.FeeLineItems.Count);

        // Filing fee — round numeric, exact equality is safe.
        var filingFee = r.FeeLineItems[0];
        Assert.Equal(370m, filingFee.Amount);
        Assert.Equal("Limited Civil Complaint - ($10K up to $25K)-GC 70613(a), 70602.5", filingFee.Description);

        // Convenience fee — has IEEE-754 tail, use tolerance.
        var convFee = r.FeeLineItems[1];
        Assert.InRange(convFee.Amount, 4.94m, 4.96m);
        Assert.Equal("Convenience Fee", convFee.Description);

        // Court Transaction Fee — round numeric.
        var courtTxFee = r.FeeLineItems[2];
        Assert.Equal(1.75m, courtTxFee.Amount);
        Assert.Equal("Court Transaction Fee", courtTxFee.Description);

        // Credit Card Transaction Fee — IEEE-754 tail.
        var ccFee = r.FeeLineItems[3];
        Assert.InRange(ccFee.Amount, 10.21m, 10.23m);
        Assert.Equal("Credit Card Transaction Fee", ccFee.Description);
    }

    [Fact]
    public void Parse_LascNfrc2_AllSevenDocsAcceptedStatus()
    {
        var r = NfrcResponseParser.Parse(LoadFixture(Nfrc2Path));
        Assert.All(r.Documents, d => Assert.Equal("ACCEPTED", d.DocumentFilingStatusCode));
    }

    [Fact]
    public void Parse_LascNfrc2_RawXmlPreserved()
    {
        var xml = LoadFixture(Nfrc2Path);
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal(xml, r.RawXml);
    }
}
