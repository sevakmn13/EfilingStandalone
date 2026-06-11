using System.Text;
using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Nop.Controllers;
using EFiling.Nop.Domain;
using EFiling.Nop.Services;
using EFiling.Providers.JTI.Soap;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Services.Customers;
using Nop.Services.Orders;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="EFilingNfrcApiController.ReceiveNfrc"/> — audit
/// Phase 4 sub-phase 4.3.
///
/// <para>
/// Covers the controller contract documented in audit § 8 plus explicit verification of
/// the Phase 5 fixes (Q16 atomic increment, B0b + Q17 canonical-ID matching, Q23 message
/// folding + privacy guard).
/// </para>
///
/// <para>
/// XML inputs use real LASC + Madera Phase 3 fixtures where the test exercises a documented
/// vendor shape; synthetic XML is built via the helpers in <see cref="NfrcResponseParserTests"/>
/// for edge cases (rejection, partial acceptance, etc.) where no captured fixture exists.
/// </para>
///
/// <para>
/// Mocking framework: Moq 4.20.72 (added 2026-04-28 to support Phase 4 controller +
/// polling-task integration tests). Strict-mode mocks make implicit "no-other-call"
/// assertions cheap.
/// </para>
/// </summary>
public class EFilingNfrcApiControllerTests
{
    // ─── Test scaffolding ────────────────────────────────────────────────

    private readonly Mock<IEFilingOrderService> _orderService = new(MockBehavior.Strict);
    private readonly Mock<IOrderService> _nopOrderService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingNotificationService> _notificationService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingProvider> _provider = new(MockBehavior.Strict);
    private readonly Mock<ICourtConfigurationService> _courtConfigService = new(MockBehavior.Strict);
    private readonly Mock<ICustomerService> _customerService = new(MockBehavior.Strict);

    private EFilingNfrcApiController BuildSut(string requestBodyXml, string? contentType = "application/xml", string? remoteIp = "127.0.0.1")
    {
        var controller = new EFilingNfrcApiController(
            _orderService.Object,
            _nopOrderService.Object,
            _notificationService.Object,
            _provider.Object,
            _courtConfigService.Object,
            _customerService.Object,
            NullLogger<EFilingNfrcApiController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBodyXml));
        httpContext.Request.ContentType = contentType;
        if (!string.IsNullOrEmpty(remoteIp))
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static EFilingOrderRecord MakeOrderRecord(
        int id = 100,
        int orderId = 200,
        string? efspRef = "EFSP-100",
        string? efmRef = "26MA00003990",
        string filingStatus = "RECEIVED_UNDER_REVIEW",
        int nfrcCount = 0)
    {
        return new EFilingOrderRecord
        {
            Id = id,
            OrderId = orderId,
            EfspReferenceId = efspRef ?? string.Empty,
            EfmReferenceId = efmRef,
            CourtId = "madera",
            FilingStatus = filingStatus,
            NfrcCount = nfrcCount,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-30),
        };
    }

    /// <summary>
    /// Build a synthetic NFRC envelope. Mirrors the helper in <see cref="NfrcResponseParserTests"/>
    /// but is duplicated here to keep this test class self-contained (controller tests
    /// shouldn't reach into the parser test infrastructure).
    /// </summary>
    private static string BuildNfrc(
        string filingStatus,
        string? efsp = null,
        string? efm = null,
        string? caseDocketId = null,
        string? caseTitle = null,
        string? messageToFiler = null,
        string? messageToClerk = null,
        string? docs = null,
        string? fees = null,
        string? filingRejectionReason = null)
    {
        const string nc = "http://niem.gov/niem/niem-core/2.0";
        const string ecf = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
        var ids = string.Empty;
        if (efsp != null)
            ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efsp}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_ASSEMBLY_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";
        if (efm != null)
            ids += $@"<nc:DocumentIdentification><nc:IdentificationID>{efm}</nc:IdentificationID><nc:IdentificationCategoryText>FILING_REVIEW_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>";

        var rejBlock = filingRejectionReason != null
            ? $@"<ecf:FilingStatusReason><ecf:ReasonCodeText>{filingRejectionReason}</ecf:ReasonCodeText></ecf:FilingStatusReason>"
            : string.Empty;

        var body = $@"<ReviewFilingCallbackMessageExt xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}"">
{ids}
<ecf:FilingStatus><ecf:FilingStatusCode>{filingStatus}</ecf:FilingStatusCode>{rejBlock}</ecf:FilingStatus>
{(caseDocketId != null ? $"<nc:CaseDocketID>{caseDocketId}</nc:CaseDocketID>" : "")}
{(caseTitle != null ? $"<nc:CaseTitleText>{caseTitle}</nc:CaseTitleText>" : "")}
{(messageToFiler != null ? $"<messageToFiler>{messageToFiler}</messageToFiler>" : "")}
{(messageToClerk != null ? $"<messageToClerk>{messageToClerk}</messageToClerk>" : "")}
{docs ?? ""}{fees ?? ""}</ReviewFilingCallbackMessageExt>";

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/""><SOAP-ENV:Body>{body}</SOAP-ENV:Body></SOAP-ENV:Envelope>";
    }

    private static string BuildDoc(
        string desc,
        string? fileControlId = null,
        string? identificationId = null,
        bool filerUploaded = true,
        string? filingStatusCode = null,
        string? rejectionReason = null,
        string? messageToFiler = null,
        string? messageToClerk = null)
    {
        const string nc = "http://niem.gov/niem/niem-core/2.0";
        const string ecf = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
        const string structuresNs = "http://niem.gov/niem/structures/2.0";
        var idAttr = filerUploaded ? $@" xmlns:s=""{structuresNs}"" s:id=""auto-{Guid.NewGuid():N}""" : string.Empty;
        var fcid = fileControlId != null ? $"<nc:DocumentFileControlID>{fileControlId}</nc:DocumentFileControlID>" : string.Empty;
        var idBlock = identificationId != null
            ? $@"<nc:DocumentIdentification><nc:IdentificationID>{identificationId}</nc:IdentificationID></nc:DocumentIdentification>"
            : string.Empty;
        var dfs = filingStatusCode != null
            ? $@"<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>{filingStatusCode}</ecf:DocumentFilingStatusCode>{(rejectionReason != null ? $"<ecf:FilingStatusReason><ecf:ReasonCodeText>{rejectionReason}</ecf:ReasonCodeText></ecf:FilingStatusReason>" : "")}</ecf:DocumentFilingStatus>"
            : string.Empty;
        var msgF = messageToFiler != null ? $"<messageToFiler>{messageToFiler}</messageToFiler>" : string.Empty;
        var msgC = messageToClerk != null ? $"<messageToClerk>{messageToClerk}</messageToClerk>" : string.Empty;

        return $@"<ReviewedDocument xmlns:nc=""{nc}"" xmlns:ecf=""{ecf}""{idAttr}>
<nc:DocumentDescriptionText>{desc}</nc:DocumentDescriptionText>
{fcid}{idBlock}{dfs}{msgF}{msgC}
</ReviewedDocument>";
    }

    // ─── Empty body / parse failure / SOAP fault ─────────────────────────

    [Fact]
    public async Task ReceiveNfrc_EmptyBody_Returns400_NoPersistence()
    {
        // Empty body fast-fails without persisting (out of Phase 0 scope — nothing
        // forensically useful to log). All mocks stay as MockBehavior.Strict with no
        // setups → any call would throw.
        var result = await BuildSut("").ReceiveNfrc(CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Empty request body.", bad.Value);
        _orderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReceiveNfrc_MalformedXml_PersistsParseFailedLog_Returns400()
    {
        // Malformed XML triggers an exception in NfrcResponseParser.Parse → controller
        // catches, persists log with MatchAttemptResult=ParseFailed, returns 400.
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await BuildSut("not-actually-xml-just-text").ReceiveNfrc(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _orderService.Verify(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveNfrc_SoapFault_PersistsSoapFaultLog_Returns400()
    {
        // SOAP fault triggers parser sentinel FilingStatusCode == "ERROR".
        var soapFault = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/""><SOAP-ENV:Body><SOAP-ENV:Fault><faultstring>Internal error</faultstring></SOAP-ENV:Fault></SOAP-ENV:Body></SOAP-ENV:Envelope>";
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await BuildSut(soapFault).ReceiveNfrc(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _orderService.Verify(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()), Times.Once);
        // No order-record lookup, no downstream processing — SOAP fault short-circuits before resolution.
        _orderService.Verify(s => s.GetByEfspReferenceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveNfrc_UnmatchedFiling_PersistsLog_Returns200()
    {
        // Parse-OK callback whose EFSP/EFM refs don't match any order record.
        // Returns 200 (not 400) so JTI doesn't retry endlessly. Persists with
        // MatchAttemptResult=Unmatched.
        var xml = BuildNfrc("ACCEPTED", efsp: "UNKNOWN-EFSP", efm: "UNKNOWN-EFM");

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync("UNKNOWN-EFSP", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((EFilingOrderRecord?)null);
        _orderService.Setup(s => s.GetByEfmReferenceIdAsync("UNKNOWN-EFM", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((EFilingOrderRecord?)null);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _orderService.Verify(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()), Times.Once);
        // No downstream processing fired
        _orderService.Verify(s => s.IncrementNfrcCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _orderService.Verify(s => s.UpdateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationService.VerifyNoOtherCalls();
    }

    // ─── Matched accept happy path (Q16 atomic increment verification) ───

    [Fact]
    public async Task ReceiveNfrc_MatchedAcceptedNfrc_FullDownstreamProcessing_AtomicIncrement()
    {
        // Q16 fix verification (Phase 5.2): controller calls IncrementNfrcCountAsync
        // (atomic SQL UPDATE) — NOT the pre-fix in-memory `record.NfrcCount++` pattern.
        // Verifies the post-increment count is assigned back to the in-memory record so
        // downstream NFRC log persistence uses the correct NfrcNumber.
        var record = MakeOrderRecord(efspRef: "EFSP-MATCH", efmRef: "EFM-MATCH", nfrcCount: 0);
        var xml = BuildNfrc("ACCEPTED", efsp: "EFSP-MATCH", efm: "EFM-MATCH",
            caseDocketId: "26CV001234", caseTitle: "Smith v. Jones",
            docs: BuildDoc("Complaint", fileControlId: "doc-abc", identificationId: "COM040", filerUploaded: true, filingStatusCode: "ACCEPTED"));

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync("EFSP-MATCH", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId))
                        .ReturnsAsync((Order?)null); // skip OrderStatus sync (out of focus)
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        var result = await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        // Q16: atomic increment was called (NOT in-memory ++)
        _orderService.Verify(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, record.NfrcCount);

        // Filing-level state mutations on the record object
        Assert.Equal("ACCEPTED", record.FilingStatus);
        Assert.Equal("26CV001234", record.CaseNumber);
        Assert.Equal("Smith v. Jones", record.CaseTitle);
        Assert.NotNull(record.LastNfrcDateUtc);

        // Notification fired (status changed RECEIVED_UNDER_REVIEW → ACCEPTED)
        _notificationService.Verify(n => n.SendFilingStatusChangedAsync(record, "RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Q23: messageToFiler folded into ErrorText; messageToClerk privacy guard ─

    [Fact]
    public async Task ReceiveNfrc_RejectedWithMessageToFiler_FoldedIntoErrorText()
    {
        // Q23 fix verification (Phase 5.4): on REJECTED, the controller folds envelope-
        // level <messageToFiler> into ErrorText alongside <FilingStatusReason>. Pre-fix
        // messageToFiler was silently dropped — when Madera eventually populates it on a
        // clerk-driven reject, the filer would lose the message.
        var record = MakeOrderRecord();
        var xml = BuildNfrc("REJECTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            messageToFiler: "Please correct case caption and resubmit.",
            filingRejectionReason: "MISSING_FIELD");

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.NotNull(record.ErrorText);
        // ErrorText must contain both the structured rejection reason AND the messageToFiler
        Assert.Contains("MISSING_FIELD", record.ErrorText!);
        Assert.Contains("Please correct case caption and resubmit.", record.ErrorText);
        Assert.Equal("REJECTED", record.FilingStatus);
        Assert.Null(record.CaseNumber); // cleared on rejection
    }

    [Fact]
    public async Task ReceiveNfrc_RejectedWithMessageToClerk_NeverLeaksToErrorText_PrivacyGuard()
    {
        // Q23 privacy guard: messageToClerk MUST NOT be folded into ErrorText. It is
        // captured in NfrcResult.MessageToClerk + EFilingNfrcLog.RawXml for audit only.
        // This test verifies the controller-level guard against a real-Madera scenario:
        // a REJECTED filing with ONLY messageToClerk populated (no messageToFiler, no
        // FilingStatusReason).
        var record = MakeOrderRecord();
        var xml = BuildNfrc("REJECTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            messageToClerk: "Internal: reassign to senior clerk for review.");

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.NotNull(record.ErrorText);
        // Privacy guard: clerk message MUST NOT have leaked
        Assert.DoesNotContain("Internal", record.ErrorText!);
        Assert.DoesNotContain("senior clerk", record.ErrorText!);
        // No filer-visible reason → falls through to default
        Assert.Equal("Filing rejected by court", record.ErrorText);
    }

    // ─── B0b + Q17: canonical-ID matching (no description guessing) ──────

    [Fact]
    public async Task ReceiveNfrc_FilerUploadedDocMatchedByCanonicalFileControlId_UpdatesExistingRow()
    {
        // Q17 + B0b fix verification: filer-uploaded docs match against existing
        // EFilingDocumentRecord.FileControlId via the canonical EfmDocumentId
        // (DocumentFileControlID, post-B0b). Pre-fix matching was based on description-
        // text fallbacks which produced false-positives across docs of the same type.
        var record = MakeOrderRecord();
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 500,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc0",
            DocumentCode = "COM040",
            FileControlId = "doc-canonical-abc", // matches DocumentFileControlID in the NFRC below
            IsLeadDocument = true,
            IsCourtGenerated = false,
        };

        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            docs: BuildDoc("Complaint", fileControlId: "doc-canonical-abc", identificationId: "COM040", filerUploaded: true, filingStatusCode: "ACCEPTED"));

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Existing doc was matched + updated; no new court-doc rows inserted
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()), Times.Once);
        _orderService.Verify(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("ACCEPTED", existingDoc.DocumentFilingStatusCode);
    }

    [Fact]
    public async Task ReceiveNfrc_CourtGenDocDedupedByCanonicalEfmDocumentId_NoDuplicateOnReplay()
    {
        // Q17 fix verification: court-generated docs (no NIEM structures:id) dedup
        // against existing EFilingDocumentRecord rows by canonical EfmDocumentId. On
        // replay of the same NFRC, no duplicate row is inserted.
        var record = MakeOrderRecord();
        var existingCourtDoc = new EFilingDocumentRecord
        {
            Id = 600,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "390903",
            DocumentCode = "EFM001",
            CourtDocumentId = "390903", // matches DocumentFileControlID in the NFRC below
            IsCourtGenerated = true,
        };

        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            docs: BuildDoc("Notice of E-Filing Confirmation",
                fileControlId: "390903",
                identificationId: "EFM001",
                filerUploaded: false,
                filingStatusCode: "ACCEPTED"));

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(2); // replay → second NFRC
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingCourtDoc });
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Q17: replay of same court-doc should NOT insert a duplicate row
        _orderService.Verify(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        // Existing court-doc was not "updated" either (court docs are insert-or-skip, no update path)
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(It.IsAny<EFilingDocumentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveNfrc_NewCourtGenDoc_InsertedWithCanonicalIdentifier()
    {
        // Q17 fix: when a court-gen doc arrives that we DON'T have yet, insert a new row
        // with the canonical EfmDocumentId as DocumentReferenceId (NOT a synthesized GUID
        // — the canonical ID enables idempotent replay handling).
        var record = MakeOrderRecord();

        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            docs: BuildDoc("RECEIPT",
                fileControlId: "390906",
                identificationId: "RECEIPT",
                filerUploaded: false,
                filingStatusCode: "ACCEPTED"));

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());

        IEnumerable<EFilingDocumentRecord>? insertedDocs = null;
        _orderService.Setup(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()))
                     .Callback<IEnumerable<EFilingDocumentRecord>, CancellationToken>((docs, _) => insertedDocs = docs.ToList())
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.NotNull(insertedDocs);
        var insertedList = insertedDocs!.ToList();
        Assert.Single(insertedList);
        var doc = insertedList[0];
        Assert.True(doc.IsCourtGenerated);
        // Q17: DocumentReferenceId = canonical EfmDocumentId (390906), not a synthetic GUID
        Assert.Equal("390906", doc.DocumentReferenceId);
        Assert.Equal("390906", doc.CourtDocumentId);
    }

    // ─── Fee replacement (idempotent re-arrival of NFRC #2) ──────────────

    [Fact]
    public async Task ReceiveNfrc_WithFees_DeletesOldChargedAndInsertsNew()
    {
        // NFRC #2 carries fee line items. Controller's fee-update path:
        //   1. Delete existing "Charged" fees for the order
        //   2. Insert new "Charged" line items from the NFRC payload
        // Idempotency: re-receiving the same NFRC #2 produces the same final fee state
        // because step 1 is unconditional ("estimated" rows from submission survive).
        var record = MakeOrderRecord();
        var feesXml = $@"<ns32:FeesCalculation xmlns:ns32=""urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt"" xmlns:ns34=""urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"" xmlns:ns33=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">
<ns32:FeesCalculationAmount>435.00</ns32:FeesCalculationAmount>
<ns34:AllowanceCharge>
  <ns33:Amount>425.00</ns33:Amount>
  <ns33:AccountingCostCode>FILING_FEE</ns33:AccountingCostCode>
  <ns33:AllowanceChargeReason>Civil filing fee</ns33:AllowanceChargeReason>
</ns34:AllowanceCharge>
<ns34:AllowanceCharge>
  <ns33:Amount>10.00</ns33:Amount>
  <ns33:AccountingCostCode>CONV_FEE</ns33:AccountingCostCode>
  <ns33:AllowanceChargeReason>Convenience fee</ns33:AllowanceChargeReason>
</ns34:AllowanceCharge>
</ns32:FeesCalculation>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, fees: feesXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(2);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        IEnumerable<EFilingFeeRecord>? insertedFees = null;
        _orderService.Setup(s => s.DeleteFeeRecordsBySourceAsync(record.Id, "Charged", It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.InsertFeeRecordsAsync(It.IsAny<IEnumerable<EFilingFeeRecord>>(), It.IsAny<CancellationToken>()))
                     .Callback<IEnumerable<EFilingFeeRecord>, CancellationToken>((fees, _) => insertedFees = fees.ToList())
                     .Returns(Task.CompletedTask);

        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Old "Charged" fees deleted, new line items inserted with Source=Charged
        _orderService.Verify(s => s.DeleteFeeRecordsBySourceAsync(record.Id, "Charged", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(insertedFees);
        var feeList = insertedFees!.ToList();
        Assert.Equal(2, feeList.Count);
        Assert.All(feeList, f => Assert.Equal("Charged", f.Source));
        Assert.Contains(feeList, f => f.Amount == 425.00m && f.AccountingCostCode == "FILING_FEE");
        Assert.Contains(feeList, f => f.Amount == 10.00m && f.AccountingCostCode == "CONV_FEE");
    }

    // ─── nopCommerce OrderStatus sync ────────────────────────────────────

    [Fact]
    public async Task ReceiveNfrc_AcceptedNfrc_SyncsOrderStatusToComplete()
    {
        // FilingStatus=ACCEPTED → nopCommerce OrderStatus=Complete. Uses the order's
        // current OrderStatus to short-circuit the update if already correct (idempotent).
        var record = MakeOrderRecord();
        var nopOrder = new Order { Id = record.OrderId, OrderStatusId = (int)OrderStatus.Pending };

        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync(nopOrder);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(nopOrder)).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // OrderStatus was synced to Complete (mapped from ACCEPTED)
        Assert.Equal((int)OrderStatus.Complete, nopOrder.OrderStatusId);
        _nopOrderService.Verify(s => s.UpdateOrderAsync(nopOrder), Times.Once);
    }

    // ─── Q19 (Phase 5.5): TransactionScope wrapping orchestration steps 1-5 ───

    [Fact]
    public async Task ReceiveNfrc_StepFailure_DoesNotRunPostCommitSideEffects()
    {
        // Q19 fix verification (Phase 5.5): when any step inside the TransactionScope
        // throws, transaction.Complete() is NEVER called, so the TX rolls back at the DB
        // level. Post-commit side effects (nopCommerce sync + notification email) MUST
        // NOT run because they would observe a state that just got rolled back.
        //
        // Test: simulate a failure in step 5 (UpdateFeesFromNfrcAsync) by having
        // InsertFeeRecordsAsync throw. Assert:
        //   - exception propagates out of ReceiveNfrc (the TX scope rethrows after rollback)
        //   - SyncNopOrderStatusAsync (step 6) is NOT called — it's after Complete()
        //   - SendFilingStatusChangedAsync (step 7) is NOT called — it's after Complete()
        //
        // Note: TransactionScope rollback at the DB level is not directly observable in a
        // unit test (it requires a real DB). What we CAN observe is that the post-commit
        // side effects don't fire — which is the contract we care about for not surfacing
        // a rolled-back state to the customer (no spurious "your filing was accepted!"
        // email when the DB write actually rolled back).
        var record = MakeOrderRecord();
        var feesXml = $@"<ns32:FeesCalculation xmlns:ns32=""urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt"" xmlns:ns34=""urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"" xmlns:ns33=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">
<ns32:FeesCalculationAmount>10.00</ns32:FeesCalculationAmount>
<ns34:AllowanceCharge><ns33:Amount>10.00</ns33:Amount><ns33:AccountingCostCode>CONV</ns33:AccountingCostCode><ns33:AllowanceChargeReason>Convenience</ns33:AllowanceChargeReason></ns34:AllowanceCharge>
</ns32:FeesCalculation>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, fees: feesXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.DeleteFeeRecordsBySourceAsync(record.Id, "Charged", It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        // Step 5 simulated failure
        _orderService.Setup(s => s.InsertFeeRecordsAsync(It.IsAny<IEnumerable<EFilingFeeRecord>>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new InvalidOperationException("DB connection lost mid-fee-insert"));

        var sut = BuildSut(xml);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReceiveNfrc(CancellationToken.None));

        // Q19 contract: post-commit side effects MUST NOT have fired.
        _nopOrderService.Verify(s => s.GetOrderByIdAsync(It.IsAny<int>()), Times.Never);
        _nopOrderService.Verify(s => s.UpdateOrderAsync(It.IsAny<Order>()), Times.Never);
        _notificationService.Verify(n => n.SendFilingStatusChangedAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Phase 4 residual hardening tests ───────────────────────────────

    [Fact]
    public async Task ReceiveNfrc_Nfrc3WithDocumentDisposition_PropagatesToDocumentRecord()
    {
        // Phase 4 residual #1 (audit § 8 item 3): NFRC #3 carries <DocumentDispositionType>
        // on per-doc blocks (judicial ruling — GRA / DEN / ORD / OAI per vendor convention,
        // schema-level xs:string with no enum restriction per § 15.4.4).
        //
        // Synthetic test (no Madera NFRC #3 fixture exists — Q22 dependency). Pre-existing
        // doc carries no disposition; NFRC #3 arrives with DocumentDispositionType=GRA
        // (Granted); test verifies the disposition propagates onto the existing
        // EFilingDocumentRecord via UpdateDocumentRecordAsync.
        var record = MakeOrderRecord();
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 700,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc1",
            DocumentCode = "PROPOSED_ORDER",
            FileControlId = "doc-canonical-xyz",
            IsLeadDocument = true,
            IsCourtGenerated = false,
            DocumentFilingStatusCode = "ACCEPTED", // NFRC #2 already accepted it
        };

        // Synthesize NFRC #3 — same shape as NFRC #2 plus DocumentDispositionType.
        // Build the doc XML with an explicit disposition element.
        var docXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""doc-disp-1"">
<nc:DocumentDescriptionText>Proposed Order</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>doc-canonical-xyz</nc:DocumentFileControlID>
<nc:DocumentIdentification><nc:IdentificationID>PROPOSED_ORDER</nc:IdentificationID></nc:DocumentIdentification>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentDispositionType>GRA</nc:DocumentDispositionType>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3); // NFRC #3
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Disposition propagated from NFRC payload onto the existing doc record
        Assert.Equal("GRA", existingDoc.DocumentDispositionType);
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveNfrc_PartiallyAccepted_AggregatesPerDocReasons_OrderStatusProcessing()
    {
        // Phase 4 residual #3 (audit § 8 item 6): PARTIALLY_ACCEPTED status.
        // Synthetic test (no Madera fixture exists for partial). Two docs: one accepted,
        // one rejected. Verify:
        //   - ErrorText aggregates the rejected doc's reason text
        //   - FilingStatus is PARTIALLY_ACCEPTED
        //   - nopCommerce OrderStatus → Processing (per SyncNopOrderStatusAsync map)
        var record = MakeOrderRecord();
        var nopOrder = new Order { Id = record.OrderId, OrderStatusId = (int)OrderStatus.Pending };

        var existingDoc1 = new EFilingDocumentRecord
        {
            Id = 800, EFilingOrderRecordId = record.Id, DocumentReferenceId = "doc0",
            FileControlId = "doc-accept-1", IsLeadDocument = true, IsCourtGenerated = false,
        };
        var existingDoc2 = new EFilingDocumentRecord
        {
            Id = 801, EFilingOrderRecordId = record.Id, DocumentReferenceId = "doc1",
            FileControlId = "doc-reject-2", IsLeadDocument = false, IsCourtGenerated = false,
        };

        var docXml = BuildDoc("Complaint", fileControlId: "doc-accept-1", identificationId: "COM040", filerUploaded: true, filingStatusCode: "ACCEPTED")
                   + BuildDoc("Cover Sheet", fileControlId: "doc-reject-2", identificationId: "MISC020", filerUploaded: true, filingStatusCode: "REJECTED", rejectionReason: "ILLEGIBLE");
        var xml = BuildNfrc("PARTIALLY_ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc1, existingDoc2 });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(It.IsAny<EFilingDocumentRecord>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync(nopOrder);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(nopOrder)).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Filing-level state
        Assert.Equal("PARTIALLY_ACCEPTED", record.FilingStatus);
        Assert.NotNull(record.ErrorText);
        Assert.Contains("ILLEGIBLE", record.ErrorText!);

        // Per-doc state
        Assert.Equal("ACCEPTED", existingDoc1.DocumentFilingStatusCode);
        Assert.Equal("REJECTED", existingDoc2.DocumentFilingStatusCode);
        Assert.Equal("ILLEGIBLE", existingDoc2.RejectionReasonText);

        // OrderStatus mapped to Processing (PARTIALLY_ACCEPTED → Processing per SyncNopOrderStatusAsync)
        Assert.Equal((int)OrderStatus.Processing, nopOrder.OrderStatusId);
    }

    [Theory]
    [InlineData("ACCEPTED", OrderStatus.Complete)]
    [InlineData("REVIEWED", OrderStatus.Complete)]
    [InlineData("PARTIALLY_ACCEPTED", OrderStatus.Processing)]
    [InlineData("REJECTED", OrderStatus.Cancelled)]
    [InlineData("CANCELLED", OrderStatus.Cancelled)]
    [InlineData("RECEIVED_UNDER_REVIEW", OrderStatus.Pending)]
    public async Task SyncNopOrderStatus_MapMatrix(string filingStatusCode, OrderStatus expectedNopStatus)
    {
        // Phase 4 residual #4 (audit § 8 item 9): full FilingStatus → nopCommerce OrderStatus
        // map matrix. Pre-Phase-4-residual only ACCEPTED→Complete was tested. Here we
        // parameterize over all 6 documented FilingStatusCode values and confirm the
        // mapping defined at SyncNopOrderStatusAsync (`@EFilingNfrcApiController.cs:604-610`):
        //   ACCEPTED / REVIEWED         → Complete
        //   PARTIALLY_ACCEPTED          → Processing
        //   REJECTED / CANCELLED        → Cancelled
        //   anything else (incl. URER)  → Pending
        var record = MakeOrderRecord(filingStatus: "RECEIVED_UNDER_REVIEW");
        var nopOrder = new Order { Id = record.OrderId, OrderStatusId = (int)OrderStatus.Pending };

        var xml = BuildNfrc(filingStatusCode, efsp: record.EfspReferenceId, efm: record.EfmReferenceId);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord>());
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync(nopOrder);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(nopOrder)).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        Assert.Equal((int)expectedNopStatus, nopOrder.OrderStatusId);
    }

    [Fact]
    public async Task ReceiveNfrc_SameNfrcReplayed_FinalStateIdempotent_ExceptForensicNfrcCount()
    {
        // Phase 4 residual #2 (audit § 8 item 4): full end-to-end idempotency check.
        // Two consecutive calls of ReceiveNfrc with the same XML body produce the same
        // final state for FilingStatus / CaseNumber / CaseTitle / ErrorText / per-doc rows /
        // fees — EXCEPT NfrcCount, which intentionally increments on every callback per
        // Q16 Option A's forensic-first preservation contract (every callback is a
        // distinct EFilingNfrcLog row, including vendor double-fires; NfrcCount reflects
        // total callbacks received, not unique payloads).
        var record = MakeOrderRecord();
        var docXml = BuildDoc("Complaint", fileControlId: "doc-replay-1", identificationId: "COM040", filerUploaded: true, filingStatusCode: "ACCEPTED");
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId,
            caseDocketId: "26CV9999", caseTitle: "Replay v. Test", docs: docXml);

        // Pre-create the existing doc so the doc-matching path UPDATES instead of inserting
        // (mirrors NFRC #1 → NFRC #1-replay where the doc was already tracked).
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 900, EFilingOrderRecordId = record.Id, DocumentReferenceId = "doc0",
            FileControlId = "doc-replay-1", IsLeadDocument = true, IsCourtGenerated = false,
        };

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        // Sequence: 1st call → IncrementNfrcCountAsync returns 1, 2nd call returns 2
        _orderService.SetupSequence(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1)
                     .ReturnsAsync(2);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        // First call
        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        var snapshotAfterFirst = new
        {
            FilingStatus = record.FilingStatus,
            CaseNumber = record.CaseNumber,
            CaseTitle = record.CaseTitle,
            DocStatus = existingDoc.DocumentFilingStatusCode,
            NfrcCount = record.NfrcCount,
        };

        // Second call (replay) — fresh SUT but same mock setups + same in-memory record
        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Idempotent invariants: filing-level state and per-doc state stable
        Assert.Equal(snapshotAfterFirst.FilingStatus, record.FilingStatus);
        Assert.Equal(snapshotAfterFirst.CaseNumber, record.CaseNumber);
        Assert.Equal(snapshotAfterFirst.CaseTitle, record.CaseTitle);
        Assert.Equal(snapshotAfterFirst.DocStatus, existingDoc.DocumentFilingStatusCode);

        // Forensic invariant: NfrcCount intentionally increments per call (Q16 Option A —
        // every callback is preserved as a distinct log row, including replays/double-fires).
        Assert.Equal(1, snapshotAfterFirst.NfrcCount);
        Assert.Equal(2, record.NfrcCount);

        // Doc was UPDATED (not inserted) on both calls — Q17 canonical-ID match works idempotently
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()), Times.Exactly(2));
        // No new court-doc rows inserted — replay is dedup-safe
        _orderService.Verify(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveNfrc_SyntheticFixtureWithFilingAssemblyMdeCategory_BackwardCompatMatch()
    {
        // Phase 4 residual #5 (audit § 8 item 8): backward-compat fallback in
        // UpdateDocumentsFromNfrcAsync. Real JTI NFRCs don't emit per-doc
        // <IdentificationCategoryText>FILING_ASSEMBLY_MDE</IdentificationCategoryText> —
        // see § 15.6 B0b — but synthetic test fixtures DO. The controller's match path
        // tries `EfmDocumentId == FileControlId` first (post-B0b canonical), and falls
        // back to `EfspDocumentId == FileControlId || DocumentReferenceId == EfspDocumentId`
        // for the synthetic-test shape. This test exercises the fallback.
        //
        // Setup: nfrcDoc.EfspDocumentId = "EFSP-DOC-1" (from FILING_ASSEMBLY_MDE category in
        // the synthetic XML), nfrcDoc.EfmDocumentId = null (no <DocumentFileControlID>).
        // Existing doc has FileControlId = "EFSP-DOC-1". Primary match (EfmDocumentId)
        // fails because both are null/empty; fallback (EfspDocumentId == FileControlId)
        // matches; UpdateDocumentRecordAsync is called.
        var record = MakeOrderRecord();
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 1000, EFilingOrderRecordId = record.Id, DocumentReferenceId = "doc0",
            FileControlId = "EFSP-DOC-1", IsLeadDocument = true, IsCourtGenerated = false,
        };

        // Build doc XML WITHOUT DocumentFileControlID (so EfmDocumentId stays null) but
        // WITH FILING_ASSEMBLY_MDE category (so EfspDocumentId is populated). This matches
        // the older synthetic-fixture shape.
        var docXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
<nc:DocumentDescriptionText>Complaint</nc:DocumentDescriptionText>
<nc:DocumentIdentification><nc:IdentificationID>EFSP-DOC-1</nc:IdentificationID><nc:IdentificationCategoryText>FILING_ASSEMBLY_MDE</nc:IdentificationCategoryText></nc:DocumentIdentification>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        // Note: parser's ParseMdeIds uses descendant search and picks up per-doc
        // FILING_ASSEMBLY_MDE category text into envelope-level EfspReferenceId. So the
        // controller will try GetByEfspReferenceIdAsync("EFSP-DOC-1") first (per-doc value
        // overwriting the envelope-level "EFSP-100"). That returns null → controller falls
        // through to GetByEfmReferenceIdAsync which resolves the record by EfmReferenceId.
        _orderService.Setup(s => s.GetByEfspReferenceIdAsync("EFSP-DOC-1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((EFilingOrderRecord?)null);
        _orderService.Setup(s => s.GetByEfmReferenceIdAsync(record.EfmReferenceId!, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Backward-compat fallback matched → existing doc was updated, NOT treated as court-gen
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()), Times.Once);
        _orderService.Verify(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("ACCEPTED", existingDoc.DocumentFilingStatusCode);
    }

    // ─── Q22-B/C (Phase 5.7): NFRC #3 DocumentDispositionDate end-to-end ──────

    [Fact]
    public async Task ReceiveNfrc_Nfrc3DispositionDate_NiemWrapper_PropagatesToDocumentRecord()
    {
        // Q22-B end-to-end: NFRC #3 carries DocumentDispositionDate in NIEM-wrapper
        // shape (real-wire JTI). Pre-existing doc has no disposition fields. After
        // controller processes NFRC #3, both DocumentDispositionType AND
        // DocumentDispositionDate land on the existing record via UpdateDocumentRecordAsync.
        var record = MakeOrderRecord();
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 750,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc-disp-date",
            DocumentCode = "STIP030",
            FileControlId = "500001",
            IsLeadDocument = true,
            IsCourtGenerated = false,
            DocumentFilingStatusCode = "ACCEPTED",
        };

        // Build NFRC #3 lead doc with NIEM-wrapper DocumentDispositionDate.
        // 2026-05-15T14:30:00-08:00 (PST) → expected UTC 2026-05-15T22:30:00Z.
        var docXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""disp-date-1"">
<nc:DocumentDescriptionText>Stipulation and Order</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500001</nc:DocumentFileControlID>
<nc:DocumentIdentification><nc:IdentificationID>STIP030</nc:IdentificationID></nc:DocumentIdentification>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentDispositionType>GRA</nc:DocumentDispositionType>
<nc:DocumentDispositionDate>
  <nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">2026-05-15T14:30:00.000-08:00</nc:DateRepresentation>
</nc:DocumentDispositionDate>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Both disposition fields propagated onto the existing doc record
        Assert.Equal("GRA", existingDoc.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), existingDoc.DocumentDispositionDate);
        Assert.Equal(DateTimeKind.Utc, existingDoc.DocumentDispositionDate!.Value.Kind);
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveNfrc_Nfrc3MultiDocMixedDispositions_EachPropagatedIndependently()
    {
        // Q22-C: a single NFRC #3 carries mixed dispositions across multiple docs —
        // judge granted one motion (GRA) and denied another (DEN) in the same callback.
        // Each doc's disposition + date must propagate to the correct record, no
        // cross-pollination.
        var record = MakeOrderRecord();

        var docGra = new EFilingDocumentRecord
        {
            Id = 760,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc-gra",
            DocumentCode = "STIP030",
            FileControlId = "500001",
            IsLeadDocument = true,
            IsCourtGenerated = false,
            DocumentFilingStatusCode = "ACCEPTED",
        };
        var docDen = new EFilingDocumentRecord
        {
            Id = 761,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc-den",
            DocumentCode = "OSC010",
            FileControlId = "500002",
            IsLeadDocument = false,
            IsCourtGenerated = false,
            DocumentFilingStatusCode = "ACCEPTED",
        };
        var docNoDisp = new EFilingDocumentRecord
        {
            Id = 762,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc-no-disp",
            DocumentCode = "MISC020",
            FileControlId = "500003",
            IsLeadDocument = false,
            IsCourtGenerated = false,
            DocumentFilingStatusCode = "ACCEPTED",
        };

        var docsXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""mix-gra"">
<nc:DocumentDescriptionText>Stipulation and Order</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500001</nc:DocumentFileControlID>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentDispositionType>GRA</nc:DocumentDispositionType>
<nc:DocumentDispositionDate>
  <nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">2026-05-15T14:30:00.000-08:00</nc:DateRepresentation>
</nc:DocumentDispositionDate>
</ReviewedDocument>
<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""mix-den"">
<nc:DocumentDescriptionText>Order to Show Cause</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500002</nc:DocumentFileControlID>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentDispositionType>DEN</nc:DocumentDispositionType>
<nc:DocumentDispositionDate>
  <nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">2026-05-15T14:35:00.000-08:00</nc:DateRepresentation>
</nc:DocumentDispositionDate>
</ReviewedDocument>
<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""mix-no-disp"">
<nc:DocumentDescriptionText>Memorandum</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500003</nc:DocumentFileControlID>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docsXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { docGra, docDen, docNoDisp });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(It.IsAny<EFilingDocumentRecord>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Each doc got its OWN disposition — no cross-pollination
        Assert.Equal("GRA", docGra.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), docGra.DocumentDispositionDate);

        Assert.Equal("DEN", docDen.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 35, 0, DateTimeKind.Utc), docDen.DocumentDispositionDate);

        // Non-pleading-paper doc preserved its (null) disposition; no leakage from siblings
        Assert.Null(docNoDisp.DocumentDispositionType);
        Assert.Null(docNoDisp.DocumentDispositionDate);

        // All three docs updated exactly once
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(docGra, It.IsAny<CancellationToken>()), Times.Once);
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(docDen, It.IsAny<CancellationToken>()), Times.Once);
        _orderService.Verify(s => s.UpdateDocumentRecordAsync(docNoDisp, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveNfrc_Nfrc2ThenNfrc3Sequenced_DispositionAddedWithoutClobberingPriorFields()
    {
        // Q22-B + Q20 semantics: NFRC #3 follows NFRC #2 for the same filing.
        // The doc record has NFRC #2 state (ACCEPTED, status text F, CourtDocumentId
        // populated, ConformedCopyUrl set). NFRC #3 arrives with the judicial
        // disposition fields and an updated status text (RP → FG) but does NOT
        // re-emit the CourtDocumentId or ConformedCopyUrl. The `??` semantic in the
        // controller's update path must preserve the prior values.
        var record = MakeOrderRecord();
        var existingDoc = new EFilingDocumentRecord
        {
            Id = 770,
            EFilingOrderRecordId = record.Id,
            DocumentReferenceId = "doc-seq",
            DocumentCode = "STIP030",
            FileControlId = "500001",
            IsLeadDocument = true,
            IsCourtGenerated = false,
            // NFRC #2 state pre-existing on the record:
            DocumentFilingStatusCode = "ACCEPTED",
            DocumentStatusText = "RP",                                  // Proposed - Received
            CourtDocumentId = "CMS-DOC-9999",                           // set by NFRC #2
            ConformedCopyUrl = "https://blob.example.com/orig.pdf",     // set by NFRC #2
        };

        // NFRC #3 — adds disposition + advances StatusText, but does NOT re-emit
        // CmsDocumentId or BinaryLocationURI. The `??` controller semantics should
        // preserve the existing values.
        var docXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"" xmlns:s=""http://niem.gov/niem/structures/2.0"" s:id=""seq-1"">
<nc:DocumentDescriptionText>Stipulation and Order</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500001</nc:DocumentFileControlID>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentStatus><nc:StatusText>FG</nc:StatusText></nc:DocumentStatus>
<nc:DocumentDispositionType>GRA</nc:DocumentDispositionType>
<nc:DocumentDispositionDate>
  <nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">2026-05-15T14:30:00.000-08:00</nc:DateRepresentation>
</nc:DocumentDispositionDate>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingDocumentRecord> { existingDoc });
        _orderService.Setup(s => s.UpdateDocumentRecordAsync(existingDoc, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Newly-added NFRC #3 fields land on the record
        Assert.Equal("GRA", existingDoc.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), existingDoc.DocumentDispositionDate);
        Assert.Equal("FG", existingDoc.DocumentStatusText);  // transitioned from RP

        // Prior NFRC #2 fields preserved (Q20 semantics: ?? preserves when NFRC #3 doesn't re-emit)
        Assert.Equal("CMS-DOC-9999", existingDoc.CourtDocumentId);
        Assert.Equal("https://blob.example.com/orig.pdf", existingDoc.ConformedCopyUrl);
        Assert.Equal("ACCEPTED", existingDoc.DocumentFilingStatusCode);
    }

    [Fact]
    public async Task ReceiveNfrc_Nfrc3CourtIssuedJudicialDoc_InsertedWithDispositionFields()
    {
        // Q22-B: NFRC #3 introduces a new court-issued judicial doc (e.g., judge's
        // signed Order PDF). This hits the controller's INSERT path (no existing record
        // match against the canonical EfmDocumentId). Pre-Q22-B-fix the court-gen
        // insert path silently dropped BOTH DocumentDispositionType AND
        // DocumentDispositionDate. Verifies the post-fix insert path captures both.
        var record = MakeOrderRecord();
        // No existing docs — the NFRC #3 court-issued doc is brand new.
        var existingDocs = new List<EFilingDocumentRecord>();

        // Court-issued Signed Order doc — no NIEM structures:id (signals court-gen
        // per the B0b heuristic), distinct DocumentFileControlID.
        var docXml = $@"<ReviewedDocument xmlns:nc=""http://niem.gov/niem/niem-core/2.0"" xmlns:ecf=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
<nc:DocumentDescriptionText>Signed Order Granting Stipulation</nc:DocumentDescriptionText>
<nc:DocumentFileControlID>500900</nc:DocumentFileControlID>
<nc:DocumentIdentification><nc:IdentificationID>ORD001</nc:IdentificationID></nc:DocumentIdentification>
<ecf:DocumentFilingStatus><ecf:DocumentFilingStatusCode>ACCEPTED</ecf:DocumentFilingStatusCode></ecf:DocumentFilingStatus>
<nc:DocumentStatus><nc:StatusText>F</nc:StatusText></nc:DocumentStatus>
<nc:DocumentDispositionType>ORD</nc:DocumentDispositionType>
<nc:DocumentDispositionDate>
  <nc:DateRepresentation xmlns:ns82=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""ns82:dateTime"">2026-05-15T14:30:00.000-08:00</nc:DateRepresentation>
</nc:DocumentDispositionDate>
</ReviewedDocument>";
        var xml = BuildNfrc("ACCEPTED", efsp: record.EfspReferenceId, efm: record.EfmReferenceId, docs: docXml);

        IEnumerable<EFilingDocumentRecord>? insertedDocs = null;
        _orderService.Setup(s => s.GetByEfspReferenceIdAsync(record.EfspReferenceId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(record);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3);
        _orderService.Setup(s => s.InsertNfrcLogAsync(It.IsAny<EFilingNfrcLog>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.GetDocumentsByOrderRecordIdAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(existingDocs);
        _orderService.Setup(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()))
                     .Callback<IEnumerable<EFilingDocumentRecord>, CancellationToken>((docs, _) => insertedDocs = docs.ToList())
                     .Returns(Task.CompletedTask);
        _orderService.Setup(s => s.UpdateOrderRecordAsync(record, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut(xml).ReceiveNfrc(CancellationToken.None);

        // Verify the new court-issued doc was inserted with BOTH disposition fields
        Assert.NotNull(insertedDocs);
        var inserted = Assert.Single(insertedDocs!);
        Assert.True(inserted.IsCourtGenerated);
        Assert.Equal("Signed Order Granting Stipulation", inserted.DocumentDescription);
        Assert.Equal("ORD", inserted.DocumentDispositionType);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 30, 0, DateTimeKind.Utc), inserted.DocumentDispositionDate);
        Assert.Equal(DateTimeKind.Utc, inserted.DocumentDispositionDate!.Value.Kind);
    }
}
