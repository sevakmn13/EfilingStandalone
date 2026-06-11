using EFiling.Core.Enums;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

public class NfrcResponseParserTests
{
    static string Wrap(string body) =>
        $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}""><SOAP-ENV:Body>{body}</SOAP-ENV:Body></SOAP-ENV:Envelope>";

    static string Cb(string status, string? efsp = null, string? efm = null, string? cms = null,
        string? caseId = null, string? docketId = null, string? title = null,
        string? docs = null, string? fees = null)
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var ids = "";
        if (efsp != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efsp}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_ASSEMBLY_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        if (efm != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efm}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        if (cms != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{cms}</nc:IdentificationID><nc:IdentificationCategoryText>COURT_RECORD_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        return Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">{ids}
<ecf:FilingStatus><ecf:FilingStatusCode>{status}</ecf:FilingStatusCode></ecf:FilingStatus>
{(caseId != null ? $"<nc:CaseTrackingID>{caseId}</nc:CaseTrackingID>" : "")}
{(docketId != null ? $"<nc:CaseDocketID>{docketId}</nc:CaseDocketID>" : "")}
{(title != null ? $"<nc:CaseTitleText>{title}</nc:CaseTitleText>" : "")}
{docs ?? ""}{fees ?? ""}</ReviewFilingCallbackMessageExt>");
    }

    static string Doc(string desc, string? efspId = null, string? efmId = null, string? cmsId = null,
        string? statusCode = null, string? statusText = null, string? rejection = null,
        string? uri = null, string? disposition = null, string? dispositionDate = null,
        string? dispositionDateXml = null)
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var ids = "";
        if (efspId != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efspId}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_ASSEMBLY_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        if (efmId != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efmId}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        if (cmsId != null) ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{cmsId}</nc:IdentificationID><nc:IdentificationCategoryText>COURT_RECORD_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        var dfs = statusCode != null ? $@"<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>{statusCode}</ecf:DocumentFilingStatusCode>{(rejection != null ? $"<ecf:FilingStatusReason><ecf:ReasonCodeText>{rejection}</ecf:ReasonCodeText></ecf:FilingStatusReason>" : "")}</ecf:DocumentFilingStatus>" : "";
        var st = statusText != null ? $"<nc:DocumentStatus><nc:StatusText>{statusText}</nc:StatusText></nc:DocumentStatus>" : "";
        var disp = disposition != null ? $"<nc:DocumentDispositionType>{disposition}</nc:DocumentDispositionType>" : "";
        // Q22-B (Phase 5.7): two shapes for DocumentDispositionDate. `dispositionDate` is the
        // direct-text shape (parser fallback); `dispositionDateXml` lets the caller inject a
        // pre-built NIEM DateType wrapper (real-wire JTI shape). They're mutually exclusive
        // — `dispositionDateXml` wins if both are set, mirroring the parser's wrapper-first
        // precedence.
        var dispDate = dispositionDateXml != null
            ? $"<nc:DocumentDispositionDate>{dispositionDateXml}</nc:DocumentDispositionDate>"
            : (dispositionDate != null
                ? $"<nc:DocumentDispositionDate>{dispositionDate}</nc:DocumentDispositionDate>"
                : "");
        var loc = uri != null ? $"<nc:BinaryLocationURI>{uri}</nc:BinaryLocationURI>" : "";
        return $@"<ReviewedDocument xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}""><nc:DocumentDescriptionText>{desc}</nc:DocumentDescriptionText>{ids}{dfs}{st}{disp}{dispDate}{loc}</ReviewedDocument>";
    }

    /// <summary>
    /// Builds a NIEM DateType wrapper inner-element string for use as
    /// <c>dispositionDateXml</c> in <see cref="Doc"/>. Mirrors the real-wire JTI shape
    /// (<c>&lt;DateRepresentation xsi:type="ns82:dateTime"&gt;…&lt;/DateRepresentation&gt;</c>)
    /// observed across all 25 captured Madera payloads + the LASC NFRC #2 sample.
    /// </summary>
    static string NiemDateRep(string isoTimestamp) =>
        $@"<nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">{isoTimestamp}</nc:DateRepresentation>";

    // ─── Input validation ─────────────────────────────────────────

    [Fact] public void Parse_Null_Throws() => Assert.Throws<ArgumentNullException>(() => NfrcResponseParser.Parse(null!));
    [Fact] public void Parse_Empty_Throws() => Assert.Throws<ArgumentException>(() => NfrcResponseParser.Parse(""));

    // ─── SOAP Fault / missing element ─────────────────────────────

    [Fact]
    public void Parse_SoapFault_ReturnsError()
    {
        var r = NfrcResponseParser.Parse(Wrap("<SOAP-ENV:Fault><faultstring>err</faultstring></SOAP-ENV:Fault>"));
        Assert.Equal("ERROR", r.FilingStatusCode);
    }

    [Fact]
    public void Parse_NoCallbackElement_ReturnsError()
    {
        var r = NfrcResponseParser.Parse(Wrap("<Other>x</Other>"));
        Assert.Equal("ERROR", r.FilingStatusCode);
    }

    // ─── Status mapping ───────────────────────────────────────────

    [Theory]
    [InlineData("Accepted", FilingStatus.Accepted)]
    [InlineData("ACCEPTED", FilingStatus.Accepted)]
    [InlineData("Reviewed", FilingStatus.Accepted)]
    [InlineData("REJECTED", FilingStatus.Rejected)]
    [InlineData("Cancelled", FilingStatus.Rejected)]
    [InlineData("RECEIVED_UNDER_REVIEW", FilingStatus.ReceivedUnderReview)]
    [InlineData("Received", FilingStatus.ReceivedUnderReview)]
    [InlineData("PARTIALLY_ACCEPTED", FilingStatus.PartiallyAccepted)]
    [InlineData("PartiallyAccepted", FilingStatus.PartiallyAccepted)]
    [InlineData("UnknownCode", FilingStatus.Unknown)]
    public void Parse_StatusMapping(string code, FilingStatus expected)
    {
        var r = NfrcResponseParser.Parse(Cb(code, efm: "E1"));
        Assert.Equal(code, r.FilingStatusCode);
        Assert.Equal(expected, r.FilingStatus);
    }

    // ─── MDE IDs ──────────────────────────────────────────────────

    [Fact]
    public void Parse_MdeIds_AllThree()
    {
        var r = NfrcResponseParser.Parse(Cb("Accepted", efsp: "EFSP-1", efm: "EFM-2", cms: "CMS-3"));
        Assert.Equal("EFSP-1", r.EfspReferenceId);
        Assert.Equal("EFM-2", r.EfmReferenceId);
        Assert.Equal("CMS-3", r.CmsReferenceId);
    }

    [Fact]
    public void Parse_MdeIds_OnlyEfm()
    {
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "EFM-ONLY"));
        Assert.Null(r.EfspReferenceId);
        Assert.Equal("EFM-ONLY", r.EfmReferenceId);
    }

    // ─── Case info ────────────────────────────────────────────────

    [Fact]
    public void Parse_CaseInfo_AllFields()
    {
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", caseId: "24MAD00123", docketId: "24-123", title: "Smith v Doe"));
        Assert.Equal("24MAD00123", r.CaseTrackingId);
        Assert.Equal("24-123", r.CaseDocketId);
        Assert.Equal("Smith v Doe", r.CaseTitle);
    }

    [Fact]
    public void Parse_CaseInfo_NullWhenMissing()
    {
        var r = NfrcResponseParser.Parse(Cb("RECEIVED_UNDER_REVIEW", efm: "E"));
        Assert.Null(r.CaseTrackingId);
        Assert.Null(r.CaseDocketId);
        Assert.Null(r.CaseTitle);
    }

    // ─── Documents ────────────────────────────────────────────────

    [Fact]
    public void Parse_Doc_AcceptedWithConformedCopy()
    {
        var d = Doc("Complaint", efspId: "d0", efmId: "ED0", statusCode: "ACCEPTED", statusText: "F", uri: "https://c.com/c.pdf");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));
        Assert.Single(r.Documents);
        var doc = r.Documents[0];
        Assert.Equal("Complaint", doc.DocumentDescriptionText);
        Assert.Equal("d0", doc.EfspDocumentId);
        Assert.Equal("ED0", doc.EfmDocumentId);
        Assert.Equal("ACCEPTED", doc.DocumentFilingStatusCode);
        Assert.Equal("F", doc.DocumentStatusText);
        Assert.Equal("https://c.com/c.pdf", doc.BinaryLocationUri);
        Assert.False(doc.IsCourtGenerated);
    }

    [Fact]
    public void Parse_Doc_RejectedWithReason()
    {
        var d = Doc("Summons", efspId: "d1", statusCode: "REJECTED", rejection: "Missing signature");
        var r = NfrcResponseParser.Parse(Cb("REJECTED", efm: "E", docs: d));
        Assert.Equal("REJECTED", r.Documents[0].DocumentFilingStatusCode);
        Assert.Equal("Missing signature", r.Documents[0].RejectionReasonText);
    }

    [Fact]
    public void Parse_Doc_CourtGenerated()
    {
        var d = Doc("RECEIPT", cmsId: "CMS-99", uri: "https://c.com/receipt.pdf");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));
        Assert.True(r.Documents[0].IsCourtGenerated);
        Assert.Null(r.Documents[0].EfspDocumentId);
        Assert.Equal("CMS-99", r.Documents[0].CmsDocumentId);
    }

    [Fact]
    public void Parse_Doc_Disposition()
    {
        var d = Doc("Order", efspId: "d3", statusCode: "ACCEPTED", disposition: "GRA");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));
        Assert.Equal("GRA", r.Documents[0].DocumentDispositionType);
    }

    [Fact]
    public void Parse_MultipleDocuments()
    {
        var docs = Doc("Complaint", efspId: "d0", statusCode: "ACCEPTED") +
                   Doc("Summons", efspId: "d1", statusCode: "REJECTED") +
                   Doc("RECEIPT", cmsId: "C1", uri: "https://c.com/r.pdf");
        var r = NfrcResponseParser.Parse(Cb("PARTIALLY_ACCEPTED", efm: "E", docs: docs));
        Assert.Equal(3, r.Documents.Count);
        Assert.False(r.Documents[0].IsCourtGenerated);
        Assert.False(r.Documents[1].IsCourtGenerated);
        Assert.True(r.Documents[2].IsCourtGenerated);
    }

    // ─── Fees ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_Fees_LineItemsAndTotal()
    {
        var fees = @"<FeesCalculationType><FeesCalculationAmount>75.50</FeesCalculationAmount>
<AllowanceCharge><Amount>60.00</Amount><AccountingCostCode>FEE</AccountingCostCode><AllowanceChargeReason>Filing Fee</AllowanceChargeReason></AllowanceCharge>
<AllowanceCharge><Amount>15.50</Amount><AccountingCostCode>TX</AccountingCostCode><AllowanceChargeReason>Court TX</AllowanceChargeReason></AllowanceCharge></FeesCalculationType>";
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", fees: fees));
        Assert.Equal(75.50m, r.TotalFees);
        Assert.Equal(2, r.FeeLineItems.Count);
        Assert.Equal(60m, r.FeeLineItems[0].Amount);
        Assert.Equal("FEE", r.FeeLineItems[0].AccountingCostCode);
        Assert.Equal("Filing Fee", r.FeeLineItems[0].Description);
    }

    [Fact]
    public void Parse_NoFees_TotalIsNull()
    {
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E"));
        Assert.Null(r.TotalFees);
        Assert.Empty(r.FeeLineItems);
    }

    // ─── Receipt detection ────────────────────────────────────────

    [Fact]
    public void Parse_Receipt_Detected()
    {
        var docs = Doc("Complaint", efspId: "d0", statusCode: "ACCEPTED") +
                   Doc("RECEIPT", cmsId: "C1", uri: "https://c.com/receipt.pdf");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: docs));
        Assert.Equal("https://c.com/receipt.pdf", r.ReceiptUrl);
    }

    [Fact]
    public void Parse_Receipt_NotDetectedForUserDoc()
    {
        var docs = Doc("RECEIPT", efspId: "d0", uri: "https://u.com/doc.pdf");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: docs));
        Assert.Null(r.ReceiptUrl);
    }

    [Fact]
    public void Parse_Receipt_NotDetectedWithoutUrl()
    {
        var docs = Doc("RECEIPT", cmsId: "C1");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: docs));
        Assert.Null(r.ReceiptUrl);
    }

    // ─── RawXml preserved ─────────────────────────────────────────

    [Fact]
    public void Parse_RawXml_Preserved()
    {
        var xml = Cb("Accepted", efm: "E");
        Assert.Equal(xml, NfrcResponseParser.Parse(xml).RawXml);
    }

    // ─── Alternative callback element names ───────────────────────

    [Fact]
    public void Parse_ReviewFilingCallbackMessage_Works()
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<ReviewFilingCallbackMessage xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentIdentification><nc:IdentificationID>EFM-ALT</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>
<ecf:FilingStatus><ecf:FilingStatusCode>Accepted</ecf:FilingStatusCode></ecf:FilingStatus>
<nc:CaseTrackingID>CASE-ALT</nc:CaseTrackingID></ReviewFilingCallbackMessage>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal(FilingStatus.Accepted, r.FilingStatus);
        Assert.Equal("EFM-ALT", r.EfmReferenceId);
        Assert.Equal("CASE-ALT", r.CaseTrackingId);
    }

    [Fact]
    public void Parse_NotifyFilingReviewComplete_Works()
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<NotifyFilingReviewCompleteRequestMessage xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentIdentification><nc:IdentificationID>EFM-N</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>
<ecf:FilingStatus><ecf:FilingStatusCode>REJECTED</ecf:FilingStatusCode></ecf:FilingStatus></NotifyFilingReviewCompleteRequestMessage>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal(FilingStatus.Rejected, r.FilingStatus);
        Assert.Equal("EFM-N", r.EfmReferenceId);
    }

    // ─── EFM fallback when no category text ───────────────────────

    [Fact]
    public void Parse_FallbackEfmRef()
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentIdentification><nc:IdentificationID>FALLBACK</nc:IdentificationID></nc:DocumentIdentification>
<ecf:FilingStatus><ecf:FilingStatusCode>Accepted</ecf:FilingStatusCode></ecf:FilingStatus></ReviewFilingCallbackMessageExt>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal("FALLBACK", r.EfmReferenceId);
    }

    // ─── Q23 (Phase 5.4): messageToFiler / messageToClerk extraction ──────────

    [Fact]
    public void Parse_EnvelopeMessageToFiler_Extracted()
    {
        // Verifies envelope-level <messageToFiler> capture per WSDL :1173 /
        // ReviewFilingCallbackMessageExtType:30157. Vendor docs scope this to the
        // rejection use case ("end user will read the reasons for rejection").
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentIdentification><nc:IdentificationID>EFM-Q23-A</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>
<ecf:FilingStatus><ecf:FilingStatusCode>REJECTED</ecf:FilingStatusCode></ecf:FilingStatus>
<messageToFiler>Please correct case caption and resubmit.</messageToFiler>
</ReviewFilingCallbackMessageExt>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal("Please correct case caption and resubmit.", r.MessageToFiler);
        Assert.Null(r.MessageToClerk);
    }

    [Fact]
    public void Parse_EnvelopeMessageToClerk_CapturedButNeverLeaksToFilerVisibleFields()
    {
        // Privacy guard at parser level: messageToClerk is captured for audit but
        // MUST NOT appear in any filer-visible aggregate (FilingRejectionReason).
        // Controller enforces the same guard for ErrorText (separate test).
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<ecf:FilingStatus><ecf:FilingStatusCode>REJECTED</ecf:FilingStatusCode></ecf:FilingStatus>
<messageToClerk>Internal clerk note: route to senior clerk for review.</messageToClerk>
</ReviewFilingCallbackMessageExt>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal("Internal clerk note: route to senior clerk for review.", r.MessageToClerk);
        Assert.Null(r.MessageToFiler);
        // Privacy guard: clerk-only message must not have leaked into FilingRejectionReason.
        Assert.Null(r.FilingRejectionReason);
    }

    [Fact]
    public void Parse_BothEnvelopeMessages_PartitionedCorrectly()
    {
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var xml = Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<ecf:FilingStatus><ecf:FilingStatusCode>REJECTED</ecf:FilingStatusCode></ecf:FilingStatus>
<messageToClerk>Clerk-only context.</messageToClerk>
<messageToFiler>Filer-visible reason.</messageToFiler>
</ReviewFilingCallbackMessageExt>");
        var r = NfrcResponseParser.Parse(xml);
        Assert.Equal("Filer-visible reason.", r.MessageToFiler);
        Assert.Equal("Clerk-only context.", r.MessageToClerk);
        // Parser does not auto-fold either message into FilingRejectionReason — the
        // controller does the folding (only MessageToFiler) when it builds ErrorText.
        Assert.Null(r.FilingRejectionReason);
    }

    [Fact]
    public void Parse_PerDocMessages_Extracted()
    {
        // WSDL ReviewedDocumentTypeExt:23360-23362 — per-doc messageToFiler / messageToClerk.
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var docXml = $@"<ReviewedDocument xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentDescriptionText>Complaint</nc:DocumentDescriptionText>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>REJECTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<messageToFiler>Document missing signature on page 3.</messageToFiler>
<messageToClerk>Notify supervisor of repeat filer.</messageToClerk>
</ReviewedDocument>";
        var r = NfrcResponseParser.Parse(Cb("REJECTED", efm: "E1", docs: docXml));
        Assert.Single(r.Documents);
        var d = r.Documents[0];
        Assert.Equal("Document missing signature on page 3.", d.MessageToFiler);
        Assert.Equal("Notify supervisor of repeat filer.", d.MessageToClerk);
        // Privacy guard: per-doc clerk message must not have leaked into RejectionReasonText.
        // RejectionReasonText is sourced solely from <FilingStatusReason><ReasonCodeText>/<Memo>
        // which are not present in this fixture, so it should be null.
        Assert.Null(d.RejectionReasonText);
    }

    [Fact]
    public void Parse_EnvelopeAndPerDocMessages_NoCrossPollination()
    {
        // Direct-children lookup (ByLocalFirst) must correctly partition envelope-level
        // and per-doc messages — envelope reads only its direct children, per-doc reads
        // only direct children of <ReviewedDocument>.
        var nc = SoapEnvelopeBuilder.NsNiemCore;
        var ecf = SoapEnvelopeBuilder.NsCommonTypes;
        var docXml = $@"<ReviewedDocument xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<nc:DocumentDescriptionText>Complaint</nc:DocumentDescriptionText>
<messageToFiler>per-doc filer msg</messageToFiler>
<messageToClerk>per-doc clerk msg</messageToClerk>
</ReviewedDocument>";
        var xml = Wrap($@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
<ecf:FilingStatus><ecf:FilingStatusCode>REJECTED</ecf:FilingStatusCode></ecf:FilingStatus>
<messageToFiler>envelope filer msg</messageToFiler>
<messageToClerk>envelope clerk msg</messageToClerk>
{docXml}
</ReviewFilingCallbackMessageExt>");
        var r = NfrcResponseParser.Parse(xml);

        Assert.Equal("envelope filer msg", r.MessageToFiler);
        Assert.Equal("envelope clerk msg", r.MessageToClerk);

        Assert.Single(r.Documents);
        Assert.Equal("per-doc filer msg", r.Documents[0].MessageToFiler);
        Assert.Equal("per-doc clerk msg", r.Documents[0].MessageToClerk);
    }

    [Fact]
    public void Parse_NoMessages_BothNull_BackwardCompat()
    {
        // Backward-compat: when neither element is emitted (typical Madera shape today —
        // 0 of 25 captured payloads contain either element per § 15.6 Q23), both fields
        // are null and pre-Q23 behavior is preserved.
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E1"));
        Assert.Null(r.MessageToFiler);
        Assert.Null(r.MessageToClerk);
        Assert.All(r.Documents, d =>
        {
            Assert.Null(d.MessageToFiler);
            Assert.Null(d.MessageToClerk);
        });
    }

    // ─── Q22-B (Phase 5.7): DocumentDispositionDate extraction (NFRC #3) ──────
    //
    // Per WSDL ReviewedDocumentTypeExt:9315 (FilingReviewMDEPort.wsdl). Schema type is
    // nc:DateType (NIEM complex wrapper). On the wire the element contains a
    // <DateRepresentation> child with xsi:type="xs:date" or "xs:dateTime"; parser also
    // tolerates direct-text shape for backward-compat with synthetic fixtures and
    // older message shapes. All values normalized to UTC.

    [Fact]
    public void Parse_DispositionDate_NiemWrapperWithTzOffset_NormalizedToUtc()
    {
        // Real-wire JTI shape: NIEM DateType wrapper with TZ offset (PST = -08:00).
        // Mirrors the format observed in LASC NFRC #2 sample (DocumentFiledDate /
        // ActivityDateRepresentation across all 25 captured Madera payloads).
        var d = Doc("Stipulation and Order", efspId: "d-disp-1", statusCode: "ACCEPTED",
            statusText: "FG", disposition: "GRA",
            dispositionDateXml: NiemDateRep("2026-05-15T14:30:00.000-08:00"));
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Single(r.Documents);
        var doc = r.Documents[0];
        Assert.NotNull(doc.DocumentDispositionDate);
        // -08:00 + 14:30 → 22:30 UTC. Verify Kind is UTC after normalization.
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), doc.DocumentDispositionDate);
        Assert.Equal(DateTimeKind.Utc, doc.DocumentDispositionDate!.Value.Kind);
    }

    [Fact]
    public void Parse_DispositionDate_NiemWrapperWithUtcZ_StaysUtc()
    {
        // ISO-8601 dateTime with explicit Z (UTC) — should pass through unchanged.
        var d = Doc("Order", efspId: "d-disp-2", disposition: "ORD",
            dispositionDateXml: NiemDateRep("2026-05-15T22:30:00Z"));
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), r.Documents[0].DocumentDispositionDate);
    }

    [Fact]
    public void Parse_DispositionDate_DirectTextDateOnly_AssumedUtc()
    {
        // Direct-text shape (no NIEM wrapper) — synthetic-fixture / backward-compat path.
        // No TZ info → AssumeUniversal treats as UTC midnight.
        var d = Doc("Endorsed Stamped Copy", efspId: "d-disp-3", disposition: "OAI",
            dispositionDate: "2026-05-15");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Equal(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), r.Documents[0].DocumentDispositionDate);
    }

    [Fact]
    public void Parse_DispositionDate_Missing_NullPreserved()
    {
        // No <DocumentDispositionDate> element at all — null.
        var d = Doc("Complaint", efspId: "d-no-disp", statusCode: "ACCEPTED");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Null(r.Documents[0].DocumentDispositionDate);
    }

    [Fact]
    public void Parse_DispositionDate_MalformedString_NullPreservedNoCrash()
    {
        // Defensive: malformed timestamp must NOT throw. Parser returns null and
        // preserves the rest of the document fields (graceful degradation).
        var d = Doc("Order", efspId: "d-bad-date", disposition: "GRA",
            dispositionDate: "not-a-real-date");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Single(r.Documents);
        Assert.Null(r.Documents[0].DocumentDispositionDate);
        // Other fields still extracted correctly — disposition string isn't conditional
        // on date parse success.
        Assert.Equal("GRA", r.Documents[0].DocumentDispositionType);
    }

    [Fact]
    public void Parse_DispositionDate_EmptyElement_NullPreserved()
    {
        // Empty element (<DocumentDispositionDate/>) — null, no crash.
        var d = Doc("Order", efspId: "d-empty-date", disposition: "GRA",
            dispositionDate: "");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Null(r.Documents[0].DocumentDispositionDate);
    }

    // ─── Q22-C (Phase 5.7): vendor-convention disposition values + tolerance ──────
    //
    // Schema declares DocumentDispositionType as xs:string (no enum restriction per WSDL
    // :9314). Vendor convention is GRA / DEN / ORD / OAI (Granted / Denied / Ordered /
    // Ordered-and-Issued). Parser must accept any string verbatim — including
    // unknown values from courts that extend the convention.

    [Theory]
    [InlineData("GRA")]
    [InlineData("DEN")]
    [InlineData("ORD")]
    [InlineData("OAI")]
    public void Parse_DispositionType_AllVendorConventionValues(string disposition)
    {
        var d = Doc("Stipulation and Order", efspId: "d-conv", disposition: disposition);
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Equal(disposition, r.Documents[0].DocumentDispositionType);
    }

    [Theory]
    [InlineData("GRA-PT")]   // hypothetical "granted in part"
    [InlineData("MOOT")]     // hypothetical court-specific value
    [InlineData("STAY")]     // hypothetical "stayed"
    public void Parse_DispositionType_UnknownValue_PreservedVerbatim(string unknownValue)
    {
        // Schema is xs:string — parser must NOT silently filter unknown values.
        // Future court-specific dispositions get captured for forensic + UI display.
        var d = Doc("Order", efspId: "d-unknown", disposition: unknownValue);
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Equal(unknownValue, r.Documents[0].DocumentDispositionType);
    }

    [Fact]
    public void Parse_DispositionType_Missing_NullPreserved()
    {
        var d = Doc("Complaint", efspId: "d-no-disp", statusCode: "ACCEPTED");
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Null(r.Documents[0].DocumentDispositionType);
    }

    // ─── Q22-C (Phase 5.7): NFRC #3 StatusText transitions (RP → F / FG / IF) ─────
    //
    // Per vendor prose (audit § 15.1 NFRC #3): "previously RP (Proposed - Received)
    // docs become F (Filed) or FG (Filed and Granted) once the judge rules". Parser
    // already extracts <DocumentStatus><StatusText> as a free string (line :204);
    // these tests pin the contract that any vendor-emitted code is preserved verbatim
    // (no canonicalization, no filtering) — schema is xs:string (no enum).

    [Theory]
    [InlineData("RP")]   // Proposed - Received (pre-NFRC-#3 baseline state)
    [InlineData("F")]    // Filed (post-disposition non-ruled or denied)
    [InlineData("FG")]   // Filed and Granted (post-disposition granted)
    [InlineData("IF")]   // Issued-Filed
    [InlineData("R")]    // Received
    [InlineData("RJ")]   // Rejected
    [InlineData("REJ")]  // Madera-observed extension (per § 15.4.1 A9)
    public void Parse_StatusText_AllVendorConventionValues_PreservedVerbatim(string statusText)
    {
        var d = Doc("Stipulation and Order", efspId: "d-st", statusText: statusText);
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        Assert.Equal(statusText, r.Documents[0].DocumentStatusText);
    }

    [Fact]
    public void Parse_Nfrc3PleadingPaper_RpToFg_TransitionWithFullDispositionContract()
    {
        // Anchor test for the NFRC #3 lead-doc transition: a previously-RP pleading
        // paper (Proposed Order at NFRC #1) gets the judicial decision at NFRC #3.
        // Verifies all four NFRC-#3-specific fields land coherently on the same doc:
        // (a) StatusText FG (transitioned from RP), (b) DocumentDispositionType GRA,
        // (c) DocumentDispositionDate populated + UTC, (d) DocumentFilingStatusCode
        // ACCEPTED (carried from NFRC #2 superset rule).
        var d = Doc("Stipulation and Order", efspId: "d-nfrc3-lead",
            statusCode: "ACCEPTED", statusText: "FG", disposition: "GRA",
            dispositionDateXml: NiemDateRep("2026-05-15T14:30:00.000-08:00"));
        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: d));

        var doc = r.Documents[0];
        Assert.Equal("FG", doc.DocumentStatusText);
        Assert.Equal("GRA", doc.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), doc.DocumentDispositionDate);
        Assert.Equal("ACCEPTED", doc.DocumentFilingStatusCode);
    }

    [Fact]
    public void Parse_Nfrc3MultiDoc_MixedDispositions_EachExtractedIndependently()
    {
        // Real NFRC #3 may carry mixed dispositions: e.g., judge grants one motion
        // (GRA on Stipulation+Order) but denies another (DEN on Order to Show Cause).
        // Verifies parser doesn't cross-pollinate disposition fields across docs.
        var docs = Doc("Stipulation and Order", efspId: "d-mix-1",
                statusText: "FG", disposition: "GRA",
                dispositionDateXml: NiemDateRep("2026-05-15T14:30:00.000-08:00")) +
            Doc("Order to Show Cause", efspId: "d-mix-2",
                statusText: "F", disposition: "DEN",
                dispositionDateXml: NiemDateRep("2026-05-15T14:35:00.000-08:00")) +
            Doc("Memorandum of Points and Authorities", efspId: "d-mix-3", statusText: "F"); // no disposition

        var r = NfrcResponseParser.Parse(Cb("Accepted", efm: "E", docs: docs));

        Assert.Equal(3, r.Documents.Count);

        Assert.Equal("GRA", r.Documents[0].DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), r.Documents[0].DocumentDispositionDate);
        Assert.Equal("FG", r.Documents[0].DocumentStatusText);

        Assert.Equal("DEN", r.Documents[1].DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 35, 0, DateTimeKind.Utc), r.Documents[1].DocumentDispositionDate);
        Assert.Equal("F", r.Documents[1].DocumentStatusText);

        // No-disposition doc must not have leaked from siblings.
        Assert.Null(r.Documents[2].DocumentDispositionType);
        Assert.Null(r.Documents[2].DocumentDispositionDate);
        Assert.Equal("F", r.Documents[2].DocumentStatusText);
    }
}
