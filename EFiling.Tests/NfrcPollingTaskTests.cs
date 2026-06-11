using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.ScheduleTasks;
using EFiling.Nop.Services;
using EFiling.Providers.JTI.Soap;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Services.Orders;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="NfrcPollingTask"/> — audit Phase 4 sub-phase 4.2.
///
/// <para>
/// Covers the polling-task contract documented in audit § 8:
/// <list type="bullet">
///   <item>No-op when no pending records exist.</item>
///   <item>Skip path: <c>NfrcCount &gt;= MaxNfrcRequests</c> (10).</item>
///   <item>Skip path: last activity within <c>MinWaitBeforePoll</c> (5 min).</item>
///   <item>Skip path: no EFM/EFSP reference ID.</item>
///   <item>Skip path: court config missing.</item>
///   <item>Q16 fix verification: <c>IncrementNfrcCountAsync</c> (atomic) is called when
///         <c>RequestNfrcAsync</c> succeeds, NOT in-memory <c>++</c>.</item>
///   <item>Q18 fix verification: <c>previousStatus</c> captured from live
///         <c>record.FilingStatus</c> before mutation, NOT hardcoded to
///         <c>"RECEIVED_UNDER_REVIEW"</c>. Webhook-then-poll ordering produces
///         <c>previous == current</c> so the notification service early-returns.</item>
///   <item><c>GetFilingStatusAsync</c> returns <c>ReceivedUnderReview</c> → record stays
///         under-review (no mutation).</item>
///   <item>JTI 403 → outer loop breaks (IP-whitelist heuristic).</item>
/// </list>
/// </para>
///
/// <para>
/// Mocking framework: <see cref="Mock"/> from Moq 4.20.72 (added 2026-04-28 to support
/// Phase 4 scope; nopCommerce's <see cref="IOrderService"/> has 40 methods, hand-rolling
/// fakes is impractical). Logger is <c>NullLogger&lt;NfrcPollingTask&gt;.Instance</c> —
/// no log assertions in this test class.
/// </para>
/// </summary>
public class NfrcPollingTaskTests
{
    // ─── Test scaffolding ────────────────────────────────────────────────

    private readonly Mock<IEFilingOrderService> _orderService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingProvider> _provider = new(MockBehavior.Strict);
    private readonly Mock<ICourtConfigurationService> _configService = new(MockBehavior.Strict);
    private readonly Mock<IOrderService> _nopOrderService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingNotificationService> _notificationService = new(MockBehavior.Strict);

    private NfrcPollingTask BuildSut() => new(
        _orderService.Object,
        _provider.Object,
        _configService.Object,
        _nopOrderService.Object,
        _notificationService.Object,
        NullLogger<NfrcPollingTask>.Instance);

    /// <summary>
    /// Build an EFilingOrderRecord representing a filing that's eligible for polling
    /// (status RECEIVED_UNDER_REVIEW, has EFM ref, NfrcCount &lt; 10, last activity
    /// older than MinWaitBeforePoll).
    /// </summary>
    private static EFilingOrderRecord PollEligibleRecord(int id = 100, int orderId = 200)
    {
        return new EFilingOrderRecord
        {
            Id = id,
            OrderId = orderId,
            EfspReferenceId = $"EFSP-{id:D6}",
            EfmReferenceId = $"26MA{id:D8}",
            CourtId = "madera",
            FilingStatus = "RECEIVED_UNDER_REVIEW",
            NfrcCount = 0,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-30),     // > 5 min ago → eligible
            LastNfrcDateUtc = DateTime.UtcNow.AddMinutes(-10), // > 5 min ago → eligible
        };
    }

    private static CourtConfiguration MaderaConfig() => new()
    {
        CourtId = "madera",
        ProviderType = "JTI",
    };

    // ─── No-op when no pending records ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoPendingRecords_NoOp()
    {
        // Empty list from GetByFilingStatusAsync → polling task returns immediately
        // without calling provider, config, or notification services. Strict-mode mocks
        // implicitly verify that no other methods were called (any unexpected call throws).
        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord>());

        await BuildSut().ExecuteAsync();

        _orderService.Verify(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()), Times.Once);
        _provider.VerifyNoOtherCalls();
        _configService.VerifyNoOtherCalls();
        _notificationService.VerifyNoOtherCalls();
    }

    // ─── Skip-condition: NfrcCount >= 10 ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RecordAtMaxNfrcCount_SkipsWithoutCallingProvider()
    {
        // MaxNfrcRequests = 10 (JTI hard limit). Records that have already received 10
        // GetNFRC calls must be skipped — further calls would be rejected by JTI.
        var record = PollEligibleRecord();
        record.NfrcCount = 10;

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });

        await BuildSut().ExecuteAsync();

        // Strict mocks: any call to provider would throw. Verifying explicit no-calls
        // here documents the contract clearly.
        _provider.VerifyNoOtherCalls();
        _configService.VerifyNoOtherCalls();
        _orderService.Verify(s => s.IncrementNfrcCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Skip-condition: last activity within MinWaitBeforePoll ──────────

    [Fact]
    public async Task ExecuteAsync_RecordLastActivityWithinMinWait_Skipped()
    {
        // MinWaitBeforePoll = 5 min. Records updated within the last 5 min are
        // skipped to avoid hammering JTI on freshly-submitted filings.
        var record = PollEligibleRecord();
        record.LastNfrcDateUtc = DateTime.UtcNow.AddMinutes(-2); // < 5 min → skipped

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });

        await BuildSut().ExecuteAsync();

        _provider.VerifyNoOtherCalls();
        _configService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_RecordLastActivityFallsBackToCreatedUtc_Skipped()
    {
        // When LastNfrcDateUtc is null (no NFRC arrived yet), the polling task uses
        // CreatedUtc as the activity timestamp. Verifies the fallback semantics.
        var record = PollEligibleRecord();
        record.LastNfrcDateUtc = null;
        record.CreatedUtc = DateTime.UtcNow.AddMinutes(-2); // < 5 min → skipped

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });

        await BuildSut().ExecuteAsync();

        _provider.VerifyNoOtherCalls();
    }

    // ─── Skip-condition: no EFM/EFSP reference ID ────────────────────────

    [Fact]
    public async Task ExecuteAsync_RecordWithNoReferenceIds_Skipped()
    {
        // GetNFRC requires either EFM or EFSP reference. A record with neither
        // (e.g., submission failed before MessageReceipt) cannot be polled.
        var record = PollEligibleRecord();
        record.EfmReferenceId = null;
        record.EfspReferenceId = string.Empty;

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });

        await BuildSut().ExecuteAsync();

        _provider.VerifyNoOtherCalls();
        _configService.VerifyNoOtherCalls();
    }

    // ─── Skip-condition: court config missing ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CourtConfigMissing_Skipped()
    {
        var record = PollEligibleRecord();

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync((CourtConfiguration?)null);

        await BuildSut().ExecuteAsync();

        // Provider must not be called when config is missing — we have no endpoint to hit.
        _provider.VerifyNoOtherCalls();
    }

    // ─── Q16 fix verification: atomic increment ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_RequestNfrcSucceeds_CallsAtomicIncrementNotInMemoryPlusPlus()
    {
        // Q16 fix (Phase 5.2): when RequestNfrcAsync succeeds, the polling task must call
        // IEFilingOrderService.IncrementNfrcCountAsync — the new atomic SQL UPDATE path —
        // NOT the pre-fix `record.NfrcCount++; await UpdateOrderRecordAsync(record)`
        // pattern that was vulnerable to the read-modify-write race under concurrent
        // webhook delivery. Strict-mode mock implicitly verifies UpdateOrderRecordAsync
        // is NOT called in the success-increment path (only IncrementNfrcCountAsync is).
        var record = PollEligibleRecord();
        record.NfrcCount = 2;

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record.EfmReferenceId, record.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        _orderService.Setup(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(3); // post-increment value
        // GetFilingStatusAsync (called by CheckFilingStatusAsync after increment)
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), record.EfmReferenceId, record.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatus.ReceivedUnderReview });

        await BuildSut().ExecuteAsync();

        // Atomic increment was called exactly once with the record's id.
        _orderService.Verify(s => s.IncrementNfrcCountAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);

        // Caller assigned the post-increment value back to the in-memory record.
        Assert.Equal(3, record.NfrcCount);

        // No full-entity update fired in the increment-success path (Q16 invariant).
        _orderService.Verify(s => s.UpdateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RequestNfrcFails_DoesNotIncrement()
    {
        // RequestNfrcAsync returns false (e.g., JTI returned a fault, vendor said no
        // NFRC available). NfrcCount must stay unchanged — only successful re-delivery
        // requests count toward MaxNfrcRequests = 10.
        var record = PollEligibleRecord();
        record.NfrcCount = 5;

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatus.ReceivedUnderReview });

        await BuildSut().ExecuteAsync();

        _orderService.Verify(s => s.IncrementNfrcCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(5, record.NfrcCount);
    }

    // ─── Polling-task CAS (Phase 5.6) — webhook-concurrent path ──────────

    [Fact]
    public async Task ExecuteAsync_WebhookConcurrentlyCommitted_PollingCasReturnsFalse_NoEmailNoNopSync()
    {
        // Polling-task CAS fix (Phase 5.6): under polling-then-webhook ordering OR
        // webhook-then-polling ordering, the CAS UPDATE's WHERE clause
        // (FilingStatus IS NULL OR = 'RECEIVED_UNDER_REVIEW') prevents the polling
        // task from clobbering webhook-owned fields AND prevents duplicate emails.
        // When CAS returns false (race lost), the polling task must NOT call
        // SendFilingStatusChangedAsync or SyncNopOrderStatusAsync — webhook owns
        // those side effects.
        //
        // Pre-fix behavior: full-entity UpdateOrderRecordAsync clobbered webhook's
        // CaseTitle/ReceiptUrl/etc.; relied solely on notification service's
        // previous==current early-return (Q18) to dedup the email — defense-in-depth
        // only, not a structural guarantee.
        //
        // Post-fix behavior: CAS returns false → polling bails entirely. The
        // notification's de-dup is now defense-in-depth, not the primary mechanism.
        var record = PollEligibleRecord();

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult
                 {
                     FilingStatus = FilingStatus.Accepted,
                     CaseDocketId = "26MA00000100",
                 });

        // CAS mock: returns false (webhook already advanced the row in DB).
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                            record.Id,
                            "ACCEPTED",
                            "26MA00000100", // caseNumberOverride
                            false,           // clearCaseNumber
                            "26MA00000100", // caseDocketIdOverride
                            null,            // errorTextOverride
                            It.IsAny<string?>(), // efmReferenceIdOverride (record's existing EFM)
                            It.IsAny<DateTime>(),
                            It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);

        await BuildSut().ExecuteAsync();

        // CAS was attempted exactly once.
        _orderService.Verify(s => s.TryAdvanceFilingStatusFromPollAsync(
            record.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        // CAS returned false → no nopCommerce sync (GetOrderByIdAsync never called).
        _nopOrderService.Verify(s => s.GetOrderByIdAsync(It.IsAny<int>()), Times.Never);

        // CAS returned false → no notification email (webhook owns it).
        _notificationService.Verify(n => n.SendFilingStatusChangedAsync(
            It.IsAny<EFilingOrderRecord>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Critical invariant: full-entity UpdateOrderRecordAsync NEVER fires from the
        // polling-task GetFilingStatus path (CAS replaced it). Closes the residual
        // clobber window after Q19's webhook-side TX wrapper.
        _orderService.Verify(s => s.UpdateOrderRecordAsync(
            It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GetFilingStatusReturnsAccepted_PollingCasSucceeds_EmailSentWithCorrectPreviousStatus()
    {
        // Polling-task CAS happy path: record starts RECEIVED_UNDER_REVIEW, GetFilingStatus
        // returns Accepted with CaseDocketId, CAS returns true (no concurrent webhook),
        // notification fires with previousStatus="RECEIVED_UNDER_REVIEW", current="ACCEPTED".
        // Verifies the new contract:
        //   - CAS is called with the narrow field set (FilingStatus, CaseNumber, CaseDocketId,
        //     LastNfrcDateUtc) and clearCaseNumber=false.
        //   - In-memory record reconciled post-CAS.
        //   - Cross-aggregate side effects (nopCommerce sync + notification) fire OUTSIDE the
        //     CAS — consistent with Q19 webhook pattern where post-commit side effects run
        //     after the TX completes.
        var record = PollEligibleRecord();
        Assert.Equal("RECEIVED_UNDER_REVIEW", record.FilingStatus);

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult
                 {
                     FilingStatus = FilingStatus.Accepted,
                     CaseDocketId = "26MA00000100",
                 });

        // Capture CAS call to verify exact narrow-field arguments.
        string? capturedNewFilingStatus = null;
        string? capturedCaseNumberOverride = null;
        bool capturedClearCaseNumber = false;
        string? capturedCaseDocketIdOverride = null;
        string? capturedErrorTextOverride = null;
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                            record.Id,
                            It.IsAny<string>(),
                            It.IsAny<string?>(),
                            It.IsAny<bool>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<DateTime>(),
                            It.IsAny<CancellationToken>()))
                     .Callback<int, string, string?, bool, string?, string?, string?, DateTime, CancellationToken>(
                        (_, status, caseNo, clear, docket, err, _, _, _) =>
                        {
                            capturedNewFilingStatus = status;
                            capturedCaseNumberOverride = caseNo;
                            capturedClearCaseNumber = clear;
                            capturedCaseDocketIdOverride = docket;
                            capturedErrorTextOverride = err;
                        })
                     .ReturnsAsync(true);

        // nopCommerce order — minimal stub for SyncNopOrderStatusAsync.
        var nopOrder = new Order { Id = record.OrderId, OrderStatusId = (int)OrderStatus.Pending };
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync(nopOrder);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(nopOrder)).Returns(Task.CompletedTask);

        // Capture previousStatus passed to notification.
        string? capturedPreviousStatus = null;
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Callback<EFilingOrderRecord, string?, CancellationToken>((_, prev, _) => capturedPreviousStatus = prev)
                            .Returns(Task.CompletedTask);

        await BuildSut().ExecuteAsync();

        // CAS arguments: narrow field set, no clearCaseNumber for non-rejected.
        Assert.Equal("ACCEPTED", capturedNewFilingStatus);
        Assert.Equal("26MA00000100", capturedCaseNumberOverride);
        Assert.False(capturedClearCaseNumber);
        Assert.Equal("26MA00000100", capturedCaseDocketIdOverride);
        Assert.Null(capturedErrorTextOverride); // no rejection reasons for ACCEPTED

        // In-memory reconciliation post-CAS so notification + nopCommerce sync see the
        // post-CAS view.
        Assert.Equal("ACCEPTED", record.FilingStatus);
        Assert.Equal("26MA00000100", record.CaseNumber);
        Assert.Equal("26MA00000100", record.CaseDocketId);

        // previousStatus captured BEFORE CAS — original "RECEIVED_UNDER_REVIEW".
        Assert.Equal("RECEIVED_UNDER_REVIEW", capturedPreviousStatus);

        // nopCommerce order status synced to Complete.
        Assert.Equal((int)OrderStatus.Complete, nopOrder.OrderStatusId);
    }

    [Fact]
    public async Task ExecuteAsync_GetFilingStatusReturnsRejected_PollingCasUsesClearCaseNumber_AndAggregatesReasons()
    {
        // REJECTED path: CAS clearCaseNumber=true, errorTextOverride=aggregated reasons,
        // nopCommerce OrderStatus → Cancelled. Verifies clear-on-reject + reason aggregation
        // matches the original behavior (just via the narrow CAS interface now).
        var record = PollEligibleRecord();
        record.CaseNumber = "26MA00000100"; // existing case number must be cleared

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult
                 {
                     FilingStatus = FilingStatus.Rejected,
                     Reasons = new List<FilingStatusReason>
                     {
                         new() { ReasonText = "Caption mismatch" },
                         new() { ReasonText = "Missing signature" },
                     },
                 });

        bool capturedClearCaseNumber = false;
        string? capturedErrorText = null;
        string? capturedNewFilingStatus = null;
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                            record.Id,
                            It.IsAny<string>(),
                            It.IsAny<string?>(),
                            It.IsAny<bool>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<string?>(),
                            It.IsAny<DateTime>(),
                            It.IsAny<CancellationToken>()))
                     .Callback<int, string, string?, bool, string?, string?, string?, DateTime, CancellationToken>(
                        (_, status, _, clear, _, err, _, _, _) =>
                        {
                            capturedNewFilingStatus = status;
                            capturedClearCaseNumber = clear;
                            capturedErrorText = err;
                        })
                     .ReturnsAsync(true);

        var nopOrder = new Order { Id = record.OrderId, OrderStatusId = (int)OrderStatus.Pending };
        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync(nopOrder);
        _nopOrderService.Setup(s => s.UpdateOrderAsync(nopOrder)).Returns(Task.CompletedTask);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut().ExecuteAsync();

        Assert.Equal("REJECTED", capturedNewFilingStatus);
        Assert.True(capturedClearCaseNumber); // explicit NULL for CaseNumber on REJECTED
        Assert.Equal("Caption mismatch; Missing signature", capturedErrorText);

        // In-memory reconciliation: CaseNumber cleared, ErrorText set, status updated.
        Assert.Null(record.CaseNumber);
        Assert.Equal("Caption mismatch; Missing signature", record.ErrorText);
        Assert.Equal("REJECTED", record.FilingStatus);

        // nopCommerce → Cancelled per SyncNopOrderStatus map.
        Assert.Equal((int)OrderStatus.Cancelled, nopOrder.OrderStatusId);
    }

    [Fact]
    public async Task ExecuteAsync_GetFilingStatusReturnsRejectedWithNoReasons_FallsBackToDefaultMessage()
    {
        // Edge case: REJECTED but no envelope-level Reasons AND no per-doc reasons.
        // The polling task's fallback message "Filing rejected by court" must be passed
        // as errorTextOverride so the user sees a meaningful message.
        var record = PollEligibleRecord();

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatus.Rejected });

        string? capturedErrorText = null;
        _orderService.Setup(s => s.TryAdvanceFilingStatusFromPollAsync(
                            record.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(),
                            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                     .Callback<int, string, string?, bool, string?, string?, string?, DateTime, CancellationToken>(
                        (_, _, _, _, _, err, _, _, _) => capturedErrorText = err)
                     .ReturnsAsync(true);

        _nopOrderService.Setup(s => s.GetOrderByIdAsync(record.OrderId)).ReturnsAsync((Order?)null);
        _notificationService.Setup(n => n.SendFilingStatusChangedAsync(record, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        await BuildSut().ExecuteAsync();

        Assert.Equal("Filing rejected by court", capturedErrorText);
    }

    // ─── GetFilingStatus returns ReceivedUnderReview → no record mutation ─

    [Fact]
    public async Task ExecuteAsync_GetFilingStatusReturnsReceivedUnderReview_RecordNotUpdated()
    {
        // Polling task's CheckFilingStatusAsync early-returns when GetFilingStatus reports
        // a non-terminal status (Unknown or ReceivedUnderReview). UpdateOrderRecordAsync
        // and SendFilingStatusChangedAsync must not fire — record stays under-review.
        var record = PollEligibleRecord();

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatus.ReceivedUnderReview });

        await BuildSut().ExecuteAsync();

        // No record update + no notification — pre-terminal status is a no-op
        _orderService.Verify(s => s.UpdateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationService.VerifyNoOtherCalls();
    }

    // ─── JTI 403 → outer loop breaks ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_JtiReturns403_BreaksOuterLoopAfterFirstFailure()
    {
        // The 403-handler catches JtiSoapException with HttpStatusCode == 403 and breaks
        // the outer foreach. Heuristic: 403 from JTI typically means our IP isn't
        // whitelisted, which affects every filing in the cycle — no point continuing.
        // Test: 2 records returned; provider throws 403 on the 1st; the 2nd's
        // RequestNfrcAsync must not be called.
        var record1 = PollEligibleRecord(id: 100);
        var record2 = PollEligibleRecord(id: 200);

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record1, record2 });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record1.EfmReferenceId, record1.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new JtiSoapException("forbidden", httpStatusCode: 403));

        await BuildSut().ExecuteAsync();

        // record1's RequestNfrcAsync threw; the loop must break before record2's
        // RequestNfrcAsync is invoked.
        _provider.Verify(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record2.EfmReferenceId, record2.EfspReferenceId, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NonJtiException_ContinuesToNextRecord()
    {
        // Non-403 exceptions (e.g., 500, transient network error, parser exception) are
        // logged at error level but the outer loop continues — one filing's failure
        // shouldn't prevent the others from being polled. Contrast with the 403 short-
        // circuit above (which IS treated as "affects all filings").
        var record1 = PollEligibleRecord(id: 100);
        var record2 = PollEligibleRecord(id: 200);

        _orderService.Setup(s => s.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<EFilingOrderRecord> { record1, record2 });
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(MaderaConfig());

        // record1: throws InvalidOperationException
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record1.EfmReferenceId, record1.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("transient"));

        // record2: succeeds (RequestNfrc returns false for simplicity, then GetFilingStatus
        // returns ReceivedUnderReview to short-circuit the rest of the path)
        _provider.Setup(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record2.EfmReferenceId, record2.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _provider.Setup(p => p.GetFilingStatusAsync(It.IsAny<CourtConfiguration>(), record2.EfmReferenceId, record2.EfspReferenceId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FilingStatusResult { FilingStatus = FilingStatus.ReceivedUnderReview });

        await BuildSut().ExecuteAsync();

        // Both records' RequestNfrcAsync must have been called — the InvalidOperationException
        // on record1 should not break the loop.
        _provider.Verify(p => p.RequestNfrcAsync(It.IsAny<CourtConfiguration>(), record2.EfmReferenceId, record2.EfspReferenceId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
