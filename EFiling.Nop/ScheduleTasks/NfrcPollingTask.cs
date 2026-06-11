using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Services;
using EFiling.Providers.JTI.Soap;
using Microsoft.Extensions.Logging;
using Nop.Services.Orders;
using Nop.Services.ScheduleTasks;
using NopOrderStatus = Nop.Core.Domain.Orders.OrderStatus;

namespace EFiling.Nop.ScheduleTasks;

/// <summary>
/// Scheduled task that polls JTI for NFRC updates on filings still in RECEIVED_UNDER_REVIEW status.
/// Two-pronged approach:
///   1. GetNFRC (async) — requests JTI to re-send the latest NFRC to our webhook.
///   2. GetFilingStatus (sync) — secondary check that returns status inline. If a terminal
///      status is detected, the order record and nopCommerce order are updated immediately
///      without waiting for the NFRC webhook.
/// Max 10 GetNFRC calls per filing.
///
/// Register in nopCommerce Admin → Schedule Tasks:
///   Type: EFiling.Nop.ScheduleTasks.NfrcPollingTask, EFiling.Nop
///   Run period: 300 (seconds = 5 minutes)
/// </summary>
public class NfrcPollingTask : IScheduleTask
{
    private readonly IEFilingOrderService _orderService;
    private readonly IEFilingProvider _provider;
    private readonly ICourtConfigurationService _configService;
    private readonly IOrderService _nopOrderService;
    private readonly IEFilingNotificationService _notificationService;
    private readonly ILogger<NfrcPollingTask> _logger;

    /// <summary>Max GetNFRC requests per filing (JTI limit is 10).</summary>
    private const int MaxNfrcRequests = 10;

    /// <summary>Only poll filings that have been waiting longer than this.</summary>
    private static readonly TimeSpan MinWaitBeforePoll = TimeSpan.FromMinutes(5);

    public NfrcPollingTask(
        IEFilingOrderService orderService,
        IEFilingProvider provider,
        ICourtConfigurationService configService,
        IOrderService nopOrderService,
        IEFilingNotificationService notificationService,
        ILogger<NfrcPollingTask> logger)
    {
        _orderService = orderService;
        _provider = provider;
        _configService = configService;
        _nopOrderService = nopOrderService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // Find all filings still under review
        var pendingRecords = await _orderService.GetByFilingStatusAsync("RECEIVED_UNDER_REVIEW");

        if (pendingRecords.Count == 0)
            return;

        var cutoff = DateTime.UtcNow - MinWaitBeforePoll;

        foreach (var record in pendingRecords)
        {
            try
            {
                // Skip if we've already hit the GetNFRC limit
                if (record.NfrcCount >= MaxNfrcRequests)
                {
                    _logger.LogWarning("Filing {Id} (EFM={Efm}) has reached max NFRC requests ({Max})",
                        record.Id, record.EfmReferenceId, MaxNfrcRequests);
                    continue;
                }

                // Skip if the filing was submitted/updated very recently
                var lastActivity = record.LastNfrcDateUtc ?? record.CreatedUtc;
                if (lastActivity > cutoff)
                    continue;

                // Need either EFM or EFSP reference ID
                if (string.IsNullOrEmpty(record.EfmReferenceId) && string.IsNullOrEmpty(record.EfspReferenceId))
                {
                    _logger.LogWarning("Filing {Id} has no EFM or EFSP reference ID — cannot poll", record.Id);
                    continue;
                }

                // Load court config
                var config = await _configService.GetByCourtIdAsync(record.CourtId);
                if (config == null)
                {
                    _logger.LogWarning("Court config not found for {CourtId} — skipping filing {Id}", record.CourtId, record.Id);
                    continue;
                }

                // 1. Request NFRC re-delivery (async — JTI will re-send to our webhook)
                var success = await _provider.RequestNfrcAsync(
                    config,
                    efmReferenceId: record.EfmReferenceId,
                    efspReferenceId: record.EfspReferenceId);

                if (success)
                {
                    // Q16 fix (Phase 5.2): atomic SQL UPDATE for NfrcCount increment.
                    // Eliminates the read-modify-write race that fires when a webhook
                    // arrives concurrently with the polling task's GetNFRC call.
                    record.NfrcCount = await _orderService.IncrementNfrcCountAsync(record.Id);
                    _logger.LogInformation("GetNFRC requested for filing {Id} (EFM={Efm}), count={Count}",
                        record.Id, record.EfmReferenceId, record.NfrcCount);
                }
                else
                    _logger.LogWarning("GetNFRC failed for filing {Id} (EFM={Efm})", record.Id, record.EfmReferenceId);

                // 2. GetFilingStatus — synchronous secondary check
                await CheckFilingStatusAsync(record, config);
            }
            catch (JtiSoapException soapEx) when (soapEx.HttpStatusCode == 403)
            {
                _logger.LogWarning("JTI returned 403 for court {CourtId} — IP may not be whitelisted. Stopping poll cycle.",
                    record.CourtId);
                break; // Stop polling all filings — network/IP issue affects everything
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling NFRC for filing {Id}", record.Id);
            }
        }
    }

    /// <summary>
    /// Calls GetFilingStatus and updates the order record if the court has moved the filing
    /// to a terminal status. This catches status changes even when the NFRC webhook is missed.
    /// Note: GetFilingStatus does NOT return fees or receipt URLs — those only come via NFRC.
    /// </summary>
    private async Task CheckFilingStatusAsync(Domain.EFilingOrderRecord record, CourtConfiguration config)
    {
        try
        {
            var statusResult = await _provider.GetFilingStatusAsync(
                config,
                efmReferenceId: record.EfmReferenceId,
                efspReferenceId: record.EfspReferenceId);

            // Only act if we got a definitive terminal status
            if (statusResult.FilingStatus == FilingStatus.Unknown ||
                statusResult.FilingStatus == FilingStatus.ReceivedUnderReview)
                return;

            _logger.LogInformation(
                "GetFilingStatus returned {Status} for filing {Id} (EFM={Efm}) — updating record",
                statusResult.FilingStatus, record.Id, record.EfmReferenceId);

            // Q18 fix (Phase 5.1): capture the live previous status BEFORE mutating record.FilingStatus.
            // Previously hardcoded as "RECEIVED_UNDER_REVIEW", which caused duplicate acceptance emails
            // under poll-after-webhook ordering: webhook flips status to ACCEPTED + sends email #1,
            // poll fires later, treats previous as RECEIVED_UNDER_REVIEW ≠ ACCEPTED, sends email #2.
            var previousStatus = record.FilingStatus ?? "RECEIVED_UNDER_REVIEW";

            // Map enum back to the status string we store. The early-return guard above
            // ensures only terminal statuses reach here; a non-terminal arm here would
            // be a logic bug, so throw rather than emit a possibly-null fallback.
            string newFilingStatus = statusResult.FilingStatus switch
            {
                FilingStatus.Accepted => "ACCEPTED",
                FilingStatus.PartiallyAccepted => "PARTIALLY_ACCEPTED",
                FilingStatus.Rejected => "REJECTED",
                _ => throw new InvalidOperationException(
                    $"CheckFilingStatusAsync reached non-terminal status {statusResult.FilingStatus} after early-return guard")
            };

            // Compute the narrow set of polling-relevant field overrides (passed to CAS).
            // Webhook-owned fields (CaseTitle, ReceiptUrl, etc.) are NOT in this set —
            // CAS leaves them untouched at the SQL level (closes the polling-task clobber
            // window after Q19's webhook-side TX wrapper). See audit § 15.6 "Polling-task CAS".
            string? caseNumberOverride = null;
            string? caseDocketIdOverride = null;
            var clearCaseNumber = false;

            if (statusResult.FilingStatus != FilingStatus.Rejected)
            {
                if (!string.IsNullOrEmpty(statusResult.CaseDocketId))
                {
                    caseNumberOverride = statusResult.CaseDocketId;
                    caseDocketIdOverride = statusResult.CaseDocketId;
                }
                else if (!string.IsNullOrEmpty(statusResult.CaseTrackingId)
                         && string.IsNullOrEmpty(record.CaseNumber))
                {
                    // Preserve `??=` semantic from pre-CAS code: only adopt CaseTrackingId
                    // as CaseNumber if there's no existing CaseNumber. The in-memory
                    // record.CaseNumber view is authoritative here because CAS WHERE
                    // guarantees no concurrent webhook has committed in this row's
                    // timeline (a webhook commit would flip status to terminal, failing
                    // the WHERE and causing CAS to no-op).
                    caseNumberOverride = statusResult.CaseTrackingId;
                }
            }
            else
            {
                clearCaseNumber = true;
            }

            string? errorTextOverride = null;
            if (statusResult.Reasons.Count > 0)
            {
                var reasons = statusResult.Reasons
                    .Select(r => r.ReasonText ?? r.Memo ?? r.ReasonCode)
                    .Where(t => !string.IsNullOrEmpty(t));
                var combined = string.Join("; ", reasons);
                if (!string.IsNullOrEmpty(combined))
                    errorTextOverride = combined;
            }
            if (errorTextOverride == null && statusResult.Documents.Count > 0)
            {
                var docReasons = statusResult.Documents
                    .SelectMany(d => d.Reasons)
                    .Select(r => r.ReasonText ?? r.Memo ?? r.ReasonCode)
                    .Where(t => !string.IsNullOrEmpty(t));
                var combined = string.Join("; ", docReasons);
                if (!string.IsNullOrEmpty(combined))
                    errorTextOverride = combined;
            }
            if (statusResult.FilingStatus == FilingStatus.Rejected && errorTextOverride == null)
                errorTextOverride = "Filing rejected by court";

            string? efmReferenceIdOverride = !string.IsNullOrEmpty(statusResult.EfmReferenceId)
                ? statusResult.EfmReferenceId
                : null;

            var lastNfrcDateUtc = DateTime.UtcNow;

            // Atomic CAS UPDATE — narrow field-level write + WHERE-clause race guard.
            // Returns false if a concurrent webhook already advanced the row to a
            // terminal status (in which case the webhook owns the email + nopCommerce sync).
            var advanced = await _orderService.TryAdvanceFilingStatusFromPollAsync(
                record.Id,
                newFilingStatus,
                caseNumberOverride,
                clearCaseNumber,
                caseDocketIdOverride,
                errorTextOverride,
                efmReferenceIdOverride,
                lastNfrcDateUtc);

            if (!advanced)
            {
                // Race lost — webhook already committed terminal state. Bail entirely:
                // webhook's post-commit pipeline (SyncNopOrderStatus + SendFilingStatusChanged)
                // already ran or will run with the correct previousStatus. Polling task
                // entering the email path here would produce a duplicate notification.
                _logger.LogInformation(
                    "Polling-task CAS lost race to webhook for filing {Id} (EFM={Efm}) — webhook already advanced status. Skipping email + nopCommerce sync.",
                    record.Id, record.EfmReferenceId);
                return;
            }

            // CAS succeeded. Reconcile in-memory record so SyncNopOrderStatusAsync +
            // SendFilingStatusChangedAsync see the post-CAS view of the world.
            record.FilingStatus = newFilingStatus;
            if (clearCaseNumber)
                record.CaseNumber = null;
            else if (caseNumberOverride != null)
                record.CaseNumber = caseNumberOverride;
            if (caseDocketIdOverride != null)
                record.CaseDocketId = caseDocketIdOverride;
            if (errorTextOverride != null)
                record.ErrorText = errorTextOverride;
            if (efmReferenceIdOverride != null)
                record.EfmReferenceId = efmReferenceIdOverride;
            record.NfrcCount = Math.Max(1, record.NfrcCount);
            record.LastNfrcDateUtc = lastNfrcDateUtc;

            // Sync nopCommerce order status (cross-aggregate — outside CAS, consistent
            // with Q19 webhook pattern where post-commit side effects run after the TX).
            await SyncNopOrderStatusAsync(record);

            // Send email notification
            await _notificationService.SendFilingStatusChangedAsync(record, previousStatus);
        }
        catch (Exception ex)
        {
            // Non-fatal — the NFRC webhook is the primary mechanism
            _logger.LogWarning(ex, "GetFilingStatus secondary check failed for filing {Id}", record.Id);
        }
    }

    private async Task SyncNopOrderStatusAsync(Domain.EFilingOrderRecord record)
    {
        var order = await _nopOrderService.GetOrderByIdAsync(record.OrderId);
        if (order == null) return;

        var targetStatus = record.FilingStatus?.ToUpperInvariant() switch
        {
            "ACCEPTED" or "REVIEWED" => NopOrderStatus.Complete,
            "PARTIALLY_ACCEPTED" or "PARTIALLYACCEPTED" => NopOrderStatus.Processing,
            "REJECTED" or "CANCELLED" => NopOrderStatus.Cancelled,
            _ => NopOrderStatus.Pending
        };

        if (order.OrderStatus != targetStatus)
        {
            order.OrderStatusId = (int)targetStatus;
            await _nopOrderService.UpdateOrderAsync(order);
            _logger.LogInformation("Order {OrderId} status synced to {Status} via GetFilingStatus",
                order.Id, targetStatus);
        }
    }
}
