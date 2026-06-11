using System.Text;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Domain;
using EFiling.Nop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Services.Customers;
using Nop.Services.Orders;
using FilingStatusEnum = EFiling.Core.Enums.FilingStatus;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="EFilingNfrcApiController.RefreshStatus"/> — P3 fix
///.
///
/// <para>
/// <b>Background.</b> P3 Tier B live smoke surfaced that the manual "Refresh Status"
/// button on the order details page advanced <c>FilingStatus</c> and synced the
/// nopCommerce order, but did NOT (a) bump <c>NfrcCount</c>, (b) send the notification
/// email, or (c) use CAS race-protection — the same Q19-style bug the polling task had
/// pre-Phase 5.6. The fix brings <c>RefreshStatus</c> to parity with the polling task's
/// CAS contract so all three status-advancement paths (webhook, polling, refresh button)
/// produce identical side effects.
/// </para>
///
/// <para>
/// These tests pin the new contract:
/// </para>
///
/// <list type="bullet">
///   <item><b>Refresh on still-pending</b> → returns current status, no DB write, no email.</item>
///   <item><b>Refresh on RECEIVED_UNDER_REVIEW that just became ACCEPTED</b> → CAS advances;
///         caller fires nopCommerce sync + notification email; NfrcCount bumped to 1.</item>
///   <item><b>Refresh on RECEIVED_UNDER_REVIEW that just became REJECTED</b> → CAS advances;
///         CaseNumber cleared; ErrorText set; nopCommerce sync + notification email fire.</item>
///   <item><b>Refresh CAS race-lost</b> (concurrent webhook already advanced) → no email,
///         no nopCommerce sync; reload + return current status.</item>
///   <item><b>Refresh on order with no EFM ref</b> → early-return diagnostic.</item>
/// </list>
/// </summary>
public class EFilingNfrcApiController_RefreshStatusTests
{
    // ─── Test scaffolding ────────────────────────────────────────────────

    private readonly Mock<IEFilingOrderService> _orderService = new(MockBehavior.Strict);
    private readonly Mock<IOrderService> _nopOrderService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingNotificationService> _notificationService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingProvider> _provider = new(MockBehavior.Strict);
    private readonly Mock<ICourtConfigurationService> _courtConfigService = new(MockBehavior.Strict);
    private readonly Mock<ICustomerService> _customerService = new(MockBehavior.Strict);

    private EFilingNfrcApiController BuildSut()
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
        // Body needs a stream even for non-NFRC endpoints (controller may read it).
        httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static EFilingOrderRecord MakeOrderRecord(
        int id = 100,
        int orderId = 200,
        string filingStatus = "RECEIVED_UNDER_REVIEW",
        string? efmRef = "26MA00009999",
        string? courtId = "madera",
        int nfrcCount = 0,
        string? caseNumber = null) => new()
        {
            Id = id,
            OrderId = orderId,
            EfspReferenceId = $"EFSP-{id:D6}",
            EfmReferenceId = efmRef,
            CourtId = courtId,
            FilingStatus = filingStatus,
            NfrcCount = nfrcCount,
            CaseNumber = caseNumber,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-30),
        };

    private static CourtConfiguration MaderaConfig() => new()
    {
        CourtId = "madera",
        ProviderType = "JTI",
    };

    private void SetupCommonHappyPathBoilerplate(int orderId = 200, int orderRecordId = 100,
        string preStatus = "RECEIVED_UNDER_REVIEW", int nfrcCount = 0, string? caseNumber = null)
    {
        var order = new Order { Id = orderId };
        var orderRecord = MakeOrderRecord(orderRecordId, orderId, preStatus, nfrcCount: nfrcCount, caseNumber: caseNumber);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(orderId)).ReturnsAsync(order);
        _orderService.Setup(s => s.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(orderRecord);
        _courtConfigService.Setup(s => s.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(MaderaConfig());
        // RequestNfrcAsync is best-effort — return false (no re-delivery), keeps the test focused on the GetFilingStatus path.
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    // ─── Test 1: still-pending → no-op ───────────────────────────────────

    [Fact]
    public async Task RefreshStatus_StillReceivedUnderReview_ReturnsCurrentStatus_NoEmailNoCasNoSync()
    {
        SetupCommonHappyPathBoilerplate();
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatusEnum.ReceivedUnderReview });

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        // Critical invariants for the "still pending" path: NO state change.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
        _notificationService.VerifyNoOtherCalls();
        // No nopCommerce sync should happen on no-change either (covered by strict-mode mocks).
    }

    // ─── Test 2: pending → ACCEPTED (full happy path) ────────────────────

    [Fact]
    public async Task RefreshStatus_PendingToAccepted_CasAdvances_FiresEmailAndNopSync_BumpsNfrcCount()
    {
        SetupCommonHappyPathBoilerplate(preStatus: "RECEIVED_UNDER_REVIEW", nfrcCount: 0);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult
            {
                FilingStatus = FilingStatusEnum.Accepted,
                CaseDocketId = "MCV089014",
                EfmReferenceId = "26MA00009999",
            });
        // CAS succeeds.
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                100, "ACCEPTED", "MCV089014", false, "MCV089014", null, "26MA00009999", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // nopCommerce sync — the controller's private SyncNopOrderStatusAsync helper. Mock the calls it makes.
        _nopOrderService.Setup(s => s.UpdateOrderAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);
        // Notification email — must fire with previousStatus=RECEIVED_UNDER_REVIEW, current=ACCEPTED.
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(
                It.Is<EFilingOrderRecord>(r => r.FilingStatus == "ACCEPTED" && r.NfrcCount >= 1),
                "RECEIVED_UNDER_REVIEW",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // CAS contract: bumps NfrcCount, advances status, sends email, syncs nop order.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            100, "ACCEPTED", "MCV089014", false, "MCV089014", null, "26MA00009999", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.SendFilingStatusChangedAsync(
            It.IsAny<EFilingOrderRecord>(), "RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()), Times.Once);
        _nopOrderService.Verify(s => s.UpdateOrderAsync(It.IsAny<Order>()), Times.AtLeastOnce);
    }

    // ─── Test 3: pending → REJECTED (CaseNumber cleared, default error text) ─

    [Fact]
    public async Task RefreshStatus_PendingToRejected_CasAdvancesWithClearCaseNumber_AndFiresEmail()
    {
        SetupCommonHappyPathBoilerplate(preStatus: "RECEIVED_UNDER_REVIEW", caseNumber: "MCV089014");
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult
            {
                FilingStatus = FilingStatusEnum.Rejected,
                Reasons = new() { new FilingStatusReason { ReasonText = "Document mismatch" } },
            });
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                100, "REJECTED", null, true, null, "Document mismatch", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(
                It.Is<EFilingOrderRecord>(r => r.FilingStatus == "REJECTED" && r.ErrorText == "Document mismatch"),
                "RECEIVED_UNDER_REVIEW",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // CAS receives clearCaseNumber=true + ErrorText override extracted from Reasons.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            100, "REJECTED", null, true, null, "Document mismatch", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.SendFilingStatusChangedAsync(
            It.IsAny<EFilingOrderRecord>(), "RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Test 4: REJECTED with no Reasons → default fallback ErrorText ──

    [Fact]
    public async Task RefreshStatus_RejectedWithNoReasons_FallsBackToDefaultErrorText()
    {
        SetupCommonHappyPathBoilerplate();
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatusEnum.Rejected });
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                100, "REJECTED", null, true, null, "Filing rejected by court", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(
                It.IsAny<EFilingOrderRecord>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // The "Filing rejected by court" default kicks in when Reasons is empty.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            100, "REJECTED", null, true, null, "Filing rejected by court", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Test 5: CAS race-lost — no email, reload to surface current status ─

    [Fact]
    public async Task RefreshStatus_CasRaceLost_NoEmailNoSync_RetrievesAndReturnsCurrentStatus()
    {
        SetupCommonHappyPathBoilerplate();
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatusEnum.Accepted, CaseDocketId = "MCV089014" });
        // CAS returns false — concurrent webhook already advanced.
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // After race-loss, the controller re-loads the row by ID to surface the post-race status.
        _orderService.Setup(s => s.GetByIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOrderRecord(filingStatus: "ACCEPTED"));

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // CAS attempt happened.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        // Reload to fetch post-race status.
        _orderService.Verify(s => s.GetByIdAsync(100, It.IsAny<CancellationToken>()), Times.Once);
        // No notification — webhook owns it.
        _notificationService.VerifyNoOtherCalls();
    }

    // ─── Test 6: order with no EFM ref → early-return diagnostic ─────────

    [Fact]
    public async Task RefreshStatus_OrderRecordWithoutEfmRef_ReturnsEarlyDiagnostic_NoProviderCalls()
    {
        var order = new Order { Id = 200 };
        var orderRecord = MakeOrderRecord(efmRef: null);
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(200)).ReturnsAsync(order);
        _orderService.Setup(s => s.GetByOrderIdAsync(200, It.IsAny<CancellationToken>())).ReturnsAsync(orderRecord);

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _provider.VerifyNoOtherCalls();
        _notificationService.VerifyNoOtherCalls();
    }

    // ─── Test 7: ACCEPTED with CaseTrackingId fallback (no CaseDocketId) ──

    [Fact]
    public async Task RefreshStatus_AcceptedWithOnlyCaseTrackingId_AdoptsAsCaseNumber()
    {
        SetupCommonHappyPathBoilerplate(caseNumber: null);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingStatusResult
            {
                FilingStatus = FilingStatusEnum.Accepted,
                CaseTrackingId = "12345",
            });
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                100, "ACCEPTED", "12345", false, null, null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(
                It.IsAny<EFilingOrderRecord>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.RefreshStatus(orderId: 200, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            100, "ACCEPTED", "12345", false, null, null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
