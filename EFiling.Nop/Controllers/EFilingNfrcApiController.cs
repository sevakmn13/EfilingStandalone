using System.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EFiling.Core.Interfaces;
using EFiling.Nop.Domain;
using EFiling.Nop.Services;
using EFiling.Providers.JTI.Parsers;
using Nop.Services.Customers;
using Nop.Services.Orders;
using NfrcResult = global::EFiling.Core.Models.NfrcResult;
using FilingStatusEnum = global::EFiling.Core.Enums.FilingStatus;
using NopOrderStatus = global::Nop.Core.Domain.Orders.OrderStatus;

namespace EFiling.Nop.Controllers;

/// <summary>
/// API endpoint for receiving NFRC (NotifyFilingReviewComplete) callbacks from JTI.
/// POST /api/efiling/nfrc — unauthenticated (JTI sends callbacks without auth headers).
/// </summary>
[Route("api/efiling")]
[ApiController]
public class EFilingNfrcApiController : ControllerBase
{
    private readonly IEFilingOrderService _orderService;
    private readonly IOrderService _nopOrderService;
    private readonly IEFilingNotificationService _notificationService;
    private readonly IEFilingProvider _provider;
    private readonly ICourtConfigurationService _courtConfigService;
    private readonly ICustomerService _customerService;
    private readonly ILogger<EFilingNfrcApiController> _logger;

    public EFilingNfrcApiController(
        IEFilingOrderService orderService,
        IOrderService nopOrderService,
        IEFilingNotificationService notificationService,
        IEFilingProvider provider,
        ICourtConfigurationService courtConfigService,
        ICustomerService customerService,
        ILogger<EFilingNfrcApiController> logger)
    {
        _orderService = orderService;
        _nopOrderService = nopOrderService;
        _notificationService = notificationService;
        _provider = provider;
        _courtConfigService = courtConfigService;
        _customerService = customerService;
        _logger = logger;
    }

    /// <summary>
    /// Receives NFRC SOAP XML from JTI. Parses, persists a forensic log entry
    /// (regardless of whether the callback could be matched — Phase 0 of the
    /// NFRC audit), and on match updates order/document/fee records.
    ///
    /// <para>Persistence policy:</para>
    /// <list type="bullet">
    ///   <item><b>Empty body</b> → fast-fail 400, no persistence (out of Phase 0 scope; nothing forensically useful to store).</item>
    ///   <item><b>Parse failure</b> → persist with <c>MatchAttemptResult = ParseFailed</c>, return 400.</item>
    ///   <item><b>SOAP fault</b> (parser sentinel <c>FilingStatusCode == "ERROR"</c>) → persist with <c>SoapFault</c>, return 400.</item>
    ///   <item><b>Unmatched</b> → persist with one of the <c>Unmatched_*</c> categories, return 200 (JTI must not retry).</item>
    ///   <item><b>Matched</b> → persist with <c>Matched</c>, run downstream order/document/fee/notification updates, return 200.</item>
    /// </list>
    /// </summary>
    [HttpPost("nfrc")]
    [Consumes("text/xml", "application/xml", "application/soap+xml")]
    public async Task<IActionResult> ReceiveNfrc(CancellationToken ct)
    {
        // Read raw SOAP body
        string rawXml;
        using (var reader = new StreamReader(Request.Body))
            rawXml = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(rawXml))
            return BadRequest("Empty request body.");

        // Capture diagnostic context once, up front — survives all branches below.
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var contentType = Request.ContentType;

        _logger.LogInformation("NFRC received ({Length} chars) from {RemoteIp}", rawXml.Length, remoteIp);

        // ── Branch 1: parse failure ──
        NfrcResult nfrc;
        try
        {
            nfrc = NfrcResponseParser.Parse(rawXml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse NFRC XML — persisting as ParseFailed");
            await _orderService.InsertNfrcLogAsync(
                NfrcCallbackTriage.BuildLog(rawXml, parsed: null, matchedRecord: null, remoteIp, contentType),
                ct);
            return BadRequest("Invalid NFRC XML.");
        }

        // ── Branch 2: SOAP fault / unrecognized structure ──
        if (nfrc.FilingStatusCode == "ERROR")
        {
            _logger.LogWarning("NFRC parse returned ERROR (SOAP fault or missing callback element) — persisting as SoapFault");
            await _orderService.InsertNfrcLogAsync(
                NfrcCallbackTriage.BuildLog(rawXml, nfrc, matchedRecord: null, remoteIp, contentType),
                ct);
            return BadRequest("NFRC contained SOAP fault or unrecognized structure.");
        }

        // ── Branch 3: unmatched (parse OK, no order found) ──
        var orderRecord = await ResolveOrderRecordAsync(nfrc, ct);
        if (orderRecord == null)
        {
            _logger.LogWarning("No EFilingOrderRecord found for EFSP={Efsp} EFM={Efm} — persisting as unmatched",
                nfrc.EfspReferenceId, nfrc.EfmReferenceId);
            await _orderService.InsertNfrcLogAsync(
                NfrcCallbackTriage.BuildLog(rawXml, nfrc, matchedRecord: null, remoteIp, contentType),
                ct);
            // Return 200 anyway to prevent JTI from retrying endlessly
            return Ok("Filing not found — NFRC logged but not processed.");
        }

        // ── Branch 4: matched — full downstream processing ──
        //
        // Q19 fix (Phase 5.5): wrap the EFiling-side orchestration (steps 1-5 below) in
        // a single TransactionScope. Without this wrapper a process crash or transient DB
        // failure between steps could leave the record in a partially-updated state
        // (e.g., FilingStatus=ACCEPTED but fee records not yet inserted; documents updated
        // but order record not). Per audit § 13 Q19 the boundary is drawn between the
        // EFiling-side aggregate (TX-protected) and the cross-aggregate side effects
        // (nopCommerce Order sync + outbound email) which intentionally run AFTER commit:
        //   - SyncNopOrderStatusAsync touches a separate aggregate root (nopCommerce
        //     `Order`) whose lifecycle is owned by another team's code path — pulling it
        //     into the same TX would produce lock contention with order-related queries
        //     elsewhere in the application, and a failure there should NOT roll back the
        //     EFiling-side state we've already committed (NFRC log, increment, status).
        //   - SendFilingStatusChangedAsync has external side effects (SMTP send) that
        //     CANNOT be rolled back — must run after the DB commit so we don't email about
        //     a state that ended up reverted.
        //
        // IsolationLevel.ReadCommitted (not the default Serializable): we don't need
        // phantom-read protection here — the atomic IncrementNfrcCountAsync at step 1 is
        // already protected by row-lock semantics, and the rest of the orchestration reads
        // through repository calls that don't depend on serializable consistency. Using
        // ReadCommitted reduces lock contention under the (rare but observed) burst of
        // concurrent NFRC webhooks for the same court.
        //
        // TransactionScopeAsyncFlowOption.Enabled is required because we await across the
        // scope — without it the ambient transaction is lost on the first await.
        // previousStatus must be declared OUTSIDE the using block so it's still in scope
        // at step 7 (notification) which runs after commit.
        string? previousStatus;
        using (var transaction = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            // Step 1: atomic SQL UPDATE for the NfrcCount increment (Q16 fix — Phase 5.2).
            // Eliminates the lost-update race that fires on concurrent NFRC webhooks
            // (notably Madera's vendor-side double-dispatch of NFRC #2). Inside this TX
            // the row lock is held until Complete(), serializing concurrent webhooks for
            // the same filing — strictly stronger than Q16's atomic increment alone.
            // Increment BEFORE building the log so NfrcNumber on the persisted log matches
            // the post-increment count (1 for the first NFRC, 2 for the second, …).
            orderRecord.NfrcCount = await _orderService.IncrementNfrcCountAsync(orderRecord.Id, ct);

            // Step 2: persist the raw NFRC log row (forensic — every callback preserved).
            await _orderService.InsertNfrcLogAsync(
                NfrcCallbackTriage.BuildLog(rawXml, nfrc, orderRecord, remoteIp, contentType),
                ct);

            // Capture previous status for notification comparison (used at step 8 below).
            previousStatus = orderRecord.FilingStatus;

            // Step 3: update order record with filing-level data (status, case number, etc.).
            await UpdateOrderFromNfrcAsync(orderRecord, nfrc, ct);

            // Step 4: update per-document statuses (filer-uploaded UPDATEs + court-gen INSERTs).
            await UpdateDocumentsFromNfrcAsync(orderRecord, nfrc, ct);

            // Step 5: update fee records (NFRC #2+: replace "Charged" rows with new line items).
            if (nfrc.FeeLineItems.Count > 0)
                await UpdateFeesFromNfrcAsync(orderRecord, nfrc, ct);

            // Commit. If any step above threw, this never runs and the TX rolls back —
            // the order record, NFRC log, document updates, and fee updates are all
            // discarded as a single atomic unit.
            transaction.Complete();
        }

        // ── Post-commit side effects (steps 6-7) ──
        // These run OUTSIDE the TX: cross-aggregate (nopCommerce Order) and external
        // (email send) side effects must not block the EFiling commit, and a failure
        // here must not roll back the EFiling state. Logged-but-tolerated failures are
        // the right semantic: the customer's filing IS accepted/rejected by the court
        // and our DB reflects that; an email failure or order-sync failure is a
        // recoverable secondary issue.

        // Step 6: update nopCommerce OrderStatus based on court filing status.
        await SyncNopOrderStatusAsync(orderRecord, ct);

        // Step 7: send email notification if status changed to a terminal state.
        await _notificationService.SendFilingStatusChangedAsync(orderRecord, previousStatus, ct);

        _logger.LogInformation("NFRC #{Num} processed for OrderRecord {Id} — status={Status}",
            orderRecord.NfrcCount, orderRecord.Id, orderRecord.FilingStatus);

        return Ok("NFRC processed.");
    }

    /// <summary>
    /// Refresh filing status by querying JTI directly (user-initiated).
    /// POST /api/efiling/refresh-status/{orderId}
    /// </summary>
    [HttpPost("refresh-status/{orderId:int}")]
    public async Task<IActionResult> RefreshStatus(int orderId, CancellationToken ct)
    {
        // Verify the order belongs to the current customer
        var order = await _nopOrderService.GetOrderByIdAsync(orderId);
        if (order == null)
            return Ok(new { success = false, message = "Order not found." });

        var orderRecord = await _orderService.GetByOrderIdAsync(orderId, ct);
        if (orderRecord == null)
            return Ok(new { success = false, message = "No filing record found for this order." });

        if (string.IsNullOrEmpty(orderRecord.EfmReferenceId))
            return Ok(new { success = false, message = "Filing has no EFM reference — status cannot be refreshed yet." });

        // Load court config
        var config = await _courtConfigService.GetByCourtIdAsync(orderRecord.CourtId ?? "", ct);
        if (config == null)
            return Ok(new { success = false, message = "Court configuration not found." });

        // Strategy: Request NFRC re-delivery from JTI (async — JTI will send the
        // latest NFRC to our callback URL). Then try direct status query as fallback.
        var nfrcRequested = false;
        try
        {
            nfrcRequested = await _provider.RequestNfrcAsync(config, efmReferenceId: orderRecord.EfmReferenceId, ct: ct);
            _logger.LogInformation("NFRC re-delivery requested for Order {OrderId} (EFM={Efm})", orderId, orderRecord.EfmReferenceId);
        }
        catch (EFiling.Providers.JTI.Soap.JtiSoapException soapEx)
        {
            _logger.LogWarning("RequestNfrc failed for Order {OrderId}: HTTP {Code} — {Body}",
                orderId, soapEx.HttpStatusCode, soapEx.ResponseBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RequestNfrc failed for Order {OrderId}, trying GetFilingStatus", orderId);
        }

        // Try direct status query.
        //
        // P3 fix: brought to parity with the polling task's CAS contract
        // (Phase 5.6 / Q19). Pre-fix this branch did a full-entity UpdateOrderRecordAsync
        // that (a) clobbered webhook-owned fields if a webhook landed concurrently,
        // (b) did not bump NfrcCount, and (c) NEVER fired the notification email — so
        // a customer who clicked "Refresh Status" and saw their filing accepted got no
        // email and the order record stayed at NfrcCount=0 forever (surfaced live during
        // P3 Tier B smoke against EFM 26MA00004641). The fix uses the same shared
        // CAS path the polling task uses (`TryAdvanceFilingStatusFromPollAsync`) so all
        // three status-advancement paths (webhook, polling, refresh button) produce
        // identical side effects: status update + NfrcCount bump + nopCommerce sync +
        // notification email + race-clobber protection.
        try
        {
            var status = await _provider.GetFilingStatusAsync(config, efmReferenceId: orderRecord.EfmReferenceId, ct: ct);

            // No-op path: still under review. Polling-task semantics — do NOT touch
            // LastNfrcDateUtc on no-change (that field tracks NFRC activity, not status
            // checks). Surface the current status to the user without any DB write.
            if (status.FilingStatus == FilingStatusEnum.ReceivedUnderReview)
            {
                _logger.LogInformation(
                    "Refresh status for Order {OrderId}: still ReceivedUnderReview (no change)", orderId);
                return Ok(new { success = true, status = orderRecord.FilingStatus ?? "RECEIVED_UNDER_REVIEW" });
            }

            // Capture previous status BEFORE mutating the in-memory record (used for
            // notification de-dup in SendFilingStatusChangedAsync).
            var previousStatus = orderRecord.FilingStatus;

            // Map the terminal-status enum to the status string we persist.
            string newFilingStatus = status.FilingStatus switch
            {
                FilingStatusEnum.Accepted => "ACCEPTED",
                FilingStatusEnum.PartiallyAccepted => "PARTIALLY_ACCEPTED",
                FilingStatusEnum.Rejected => "REJECTED",
                _ => throw new InvalidOperationException(
                    $"RefreshStatus reached non-terminal status {status.FilingStatus} after early-return guard")
            };

            // Compute the narrow set of polling-relevant field overrides (passed to CAS).
            // Webhook-owned fields (CaseTitle, ReceiptUrl, etc.) are NOT in this set —
            // CAS leaves them untouched at the SQL level.
            string? caseNumberOverride = null;
            string? caseDocketIdOverride = null;
            var clearCaseNumber = false;

            if (status.FilingStatus != FilingStatusEnum.Rejected)
            {
                if (!string.IsNullOrEmpty(status.CaseDocketId))
                {
                    caseNumberOverride = status.CaseDocketId;
                    caseDocketIdOverride = status.CaseDocketId;
                }
                else if (!string.IsNullOrEmpty(status.CaseTrackingId)
                         && string.IsNullOrEmpty(orderRecord.CaseNumber))
                {
                    caseNumberOverride = status.CaseTrackingId;
                }
            }
            else
            {
                clearCaseNumber = true;
            }

            string? errorTextOverride = null;
            if (status.Reasons != null && status.Reasons.Count > 0)
            {
                var reasons = status.Reasons
                    .Select(r => r.ReasonText ?? r.Memo ?? r.ReasonCode)
                    .Where(t => !string.IsNullOrEmpty(t));
                var combined = string.Join("; ", reasons);
                if (!string.IsNullOrEmpty(combined))
                    errorTextOverride = combined;
            }
            // Default message if rejected but no specific reason found
            if (status.FilingStatus == FilingStatusEnum.Rejected && errorTextOverride == null)
                errorTextOverride = "Filing rejected by court";

            string? efmReferenceIdOverride = !string.IsNullOrEmpty(status.EfmReferenceId)
                ? status.EfmReferenceId
                : null;

            var lastNfrcDateUtc = DateTime.UtcNow;

            // Atomic CAS UPDATE — narrow field-level write + WHERE-clause race guard.
            // Returns false if a concurrent webhook already advanced the row to a
            // terminal status (in which case the webhook owns the email + nopCommerce sync).
            var advanced = await _orderService.TryAdvanceFilingStatusFromPollAsync(
                orderRecord.Id,
                newFilingStatus,
                caseNumberOverride,
                clearCaseNumber,
                caseDocketIdOverride,
                errorTextOverride,
                efmReferenceIdOverride,
                lastNfrcDateUtc);

            if (!advanced)
            {
                // Race lost — webhook (or polling task) already committed terminal state.
                // The other path's post-commit pipeline already ran or will run with the
                // correct previousStatus. Refresh button entering the email path here would
                // produce a duplicate notification. Surface the row's current status to user.
                _logger.LogInformation(
                    "RefreshStatus CAS race-lost for Order {OrderId} (filing {Id}, EFM={Efm}) — concurrent webhook/polling already advanced status. Skipping email + nopCommerce sync.",
                    orderId, orderRecord.Id, orderRecord.EfmReferenceId);
                // Re-load to get the post-race terminal status for the response.
                var reloaded = await _orderService.GetByIdAsync(orderRecord.Id, ct);
                return Ok(new { success = true, status = reloaded?.FilingStatus ?? newFilingStatus });
            }

            // CAS succeeded. Reconcile in-memory record so SyncNopOrderStatusAsync +
            // SendFilingStatusChangedAsync see the post-CAS view of the world.
            orderRecord.FilingStatus = newFilingStatus;
            if (clearCaseNumber)
                orderRecord.CaseNumber = null;
            else if (caseNumberOverride != null)
                orderRecord.CaseNumber = caseNumberOverride;
            if (caseDocketIdOverride != null)
                orderRecord.CaseDocketId = caseDocketIdOverride;
            if (errorTextOverride != null)
                orderRecord.ErrorText = errorTextOverride;
            if (efmReferenceIdOverride != null)
                orderRecord.EfmReferenceId = efmReferenceIdOverride;
            orderRecord.NfrcCount = Math.Max(1, orderRecord.NfrcCount);
            orderRecord.LastNfrcDateUtc = lastNfrcDateUtc;

            // Sync nopCommerce order status (cross-aggregate — outside CAS, consistent
            // with webhook + polling-task patterns where post-commit side effects run
            // after the TX/CAS).
            await SyncNopOrderStatusAsync(orderRecord, ct);

            // Send email notification. Idempotent — the service early-returns when
            // previousStatus == current status (covers refresh-on-already-terminal).
            await _notificationService.SendFilingStatusChangedAsync(orderRecord, previousStatus, ct);

            _logger.LogInformation("RefreshStatus advanced Order {OrderId} (filing {Id}) {Prev} -> {New}",
                orderId, orderRecord.Id, previousStatus ?? "(null)", newFilingStatus);
            return Ok(new { success = true, status = newFilingStatus });
        }
        catch (EFiling.Providers.JTI.Soap.JtiSoapException soapEx2)
        {
            _logger.LogWarning("GetFilingStatus failed for Order {OrderId}: HTTP {Code} — {Body}",
                orderId, soapEx2.HttpStatusCode, soapEx2.ResponseBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFilingStatus failed for Order {OrderId}", orderId);
        }

        // Both calls failed — if NFRC was requested, tell user to wait; otherwise report error
        if (nfrcRequested)
            return Ok(new { success = true, message = "Status refresh requested. The page will update when the court responds." });

        return Ok(new { success = false, message = "Unable to refresh status at this time. Please try again later." });
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private async Task<EFilingOrderRecord?> ResolveOrderRecordAsync(NfrcResult nfrc, CancellationToken ct)
    {
        EFilingOrderRecord? record = null;

        if (!string.IsNullOrEmpty(nfrc.EfspReferenceId))
            record = await _orderService.GetByEfspReferenceIdAsync(nfrc.EfspReferenceId, ct);

        if (record == null && !string.IsNullOrEmpty(nfrc.EfmReferenceId))
            record = await _orderService.GetByEfmReferenceIdAsync(nfrc.EfmReferenceId, ct);

        return record;
    }

    private async Task UpdateOrderFromNfrcAsync(EFilingOrderRecord record, NfrcResult nfrc, CancellationToken ct)
    {
        record.FilingStatus = nfrc.FilingStatusCode;
        record.LastNfrcDateUtc = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(nfrc.EfmReferenceId))
            record.EfmReferenceId = nfrc.EfmReferenceId;

        // Only assign case number for non-rejected filings
        if (nfrc.FilingStatus != FilingStatusEnum.Rejected)
        {
            if (!string.IsNullOrEmpty(nfrc.CaseDocketId))
                record.CaseNumber = nfrc.CaseDocketId;
            else if (!string.IsNullOrEmpty(nfrc.CaseTrackingId))
                record.CaseNumber = nfrc.CaseTrackingId;

            if (!string.IsNullOrEmpty(nfrc.CaseDocketId))
                record.CaseDocketId = nfrc.CaseDocketId;
        }
        else
        {
            // Clear any case number that may have been set previously
            record.CaseNumber = null;
        }

        if (!string.IsNullOrEmpty(nfrc.CaseTitle))
            record.CaseTitle = nfrc.CaseTitle;

        if (!string.IsNullOrEmpty(nfrc.ReceiptUrl))
            record.ReceiptUrl = nfrc.ReceiptUrl;

        // Aggregate rejection reasons + filer-targeted messages (Q23 fix — Phase 5.4).
        // Privacy guard: MessageToClerk is NEVER folded into ErrorText here or anywhere
        // downstream — it is internal court / EFSP communication preserved only in
        // EFilingNfrcLog.RawXml + the in-memory NfrcResult for audit. Including it in the
        // filer-visible ErrorText would leak clerk-internal context. Only MessageToFiler
        // (per-doc + envelope) is folded in alongside the structured rejection reasons.
        if (nfrc.FilingStatus == FilingStatusEnum.Rejected ||
            nfrc.FilingStatus == FilingStatusEnum.PartiallyAccepted)
        {
            // 1. Per-doc: rejection reasons + filer-targeted messages, concatenated.
            //    Both fields are filer-visible by design; merging them gives the filer
            //    the full per-document context (vendor's structured reason + clerk's
            //    free-form message).
            var docMessages = nfrc.Documents
                .SelectMany(d => new[] { d.RejectionReasonText, d.MessageToFiler })
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var docCombined = string.Join("; ", docMessages);
            if (!string.IsNullOrEmpty(docCombined))
                record.ErrorText = docCombined;

            // 2. Fallback: envelope-level rejection reason + filer-targeted message.
            //    Used when no per-doc rejection text is available — typical for filing-level
            //    rejects (no per-doc context) and Madera's empty-FilingStatus auto-reject
            //    pattern (Q21 dependency: messageToFiler may eventually carry the
            //    clerk-driven rejection reason that the structured FilingStatusReason block
            //    fails to populate).
            if (string.IsNullOrEmpty(record.ErrorText))
            {
                var envMessages = new[] { nfrc.FilingRejectionReason, nfrc.MessageToFiler }
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var envCombined = string.Join("; ", envMessages);
                if (!string.IsNullOrEmpty(envCombined))
                    record.ErrorText = envCombined;
            }

            // 3. Default message if no specific reason found
            if (string.IsNullOrEmpty(record.ErrorText))
                record.ErrorText = "Filing rejected by court";
        }

        await _orderService.UpdateOrderRecordAsync(record, ct);
    }

    private async Task UpdateDocumentsFromNfrcAsync(EFilingOrderRecord record, NfrcResult nfrc, CancellationToken ct)
    {
        var existingDocs = await _orderService.GetDocumentsByOrderRecordIdAsync(record.Id, ct);
        var newCourtDocs = new List<EFilingDocumentRecord>();

        foreach (var nfrcDoc in nfrc.Documents)
        {
            // Q17 + B0b fix (Phase 5.3): schema-grounded discrimination of filer-uploaded
            // vs court-generated docs. Primary match is the canonical EFM handle
            // (DocumentFileControlID, post-B0b lives in nfrcDoc.EfmDocumentId) against
            // EFilingDocumentRecord.FileControlId set at submission time. If the match
            // succeeds, this is a filer-uploaded doc we already track → update existing.
            // If no match, fall through to court-doc insert path.
            //
            // Description text is NEVER used for matching/dedup (no-guess principle —
            // schema declares DocumentDescriptionText as xs:string with no uniqueness
            // contract; multiple docs may share a description across NFRCs of the same filing).
            EFilingDocumentRecord? existing = null;

            if (!string.IsNullOrEmpty(nfrcDoc.EfmDocumentId))
            {
                existing = existingDocs.FirstOrDefault(d =>
                    !d.IsCourtGenerated &&
                    d.FileControlId == nfrcDoc.EfmDocumentId);
            }

            // Backward-compat for older message shapes that DO emit FILING_ASSEMBLY_MDE
            // category per-doc (synthetic test fixtures + any legacy JTI deployment shapes).
            if (existing == null && !string.IsNullOrEmpty(nfrcDoc.EfspDocumentId))
            {
                existing = existingDocs.FirstOrDefault(d =>
                    !d.IsCourtGenerated &&
                    (d.FileControlId == nfrcDoc.EfspDocumentId ||
                     d.DocumentReferenceId == nfrcDoc.EfspDocumentId));
            }

            if (existing != null)
            {
                // Filer-uploaded doc — update existing record with NFRC outcomes
                existing.DocumentFilingStatusCode = nfrcDoc.DocumentFilingStatusCode ?? existing.DocumentFilingStatusCode;
                existing.DocumentStatusText = nfrcDoc.DocumentStatusText ?? existing.DocumentStatusText;
                existing.DocumentDispositionType = nfrcDoc.DocumentDispositionType ?? existing.DocumentDispositionType;
                // Q22-B fix (Phase 5.7): propagate judicial-disposition timestamp from NFRC #3.
                // The `??` semantic respects Q20 (no clear-on-empty) — a re-emitted NFRC #3 that
                // omits the date doesn't clobber an earlier persisted value.
                existing.DocumentDispositionDate = nfrcDoc.DocumentDispositionDate ?? existing.DocumentDispositionDate;
                existing.RejectionReasonText = nfrcDoc.RejectionReasonText ?? existing.RejectionReasonText;
                existing.CourtDocumentId = nfrcDoc.CmsDocumentId ?? existing.CourtDocumentId;

                if (!string.IsNullOrEmpty(nfrcDoc.DocumentDescriptionText))
                    existing.DocumentDescription = nfrcDoc.DocumentDescriptionText;

                if (!string.IsNullOrEmpty(nfrcDoc.BinaryLocationUri))
                    existing.ConformedCopyUrl = nfrcDoc.BinaryLocationUri;

                await _orderService.UpdateDocumentRecordAsync(existing, ct);
                continue;
            }

            // No match against our submission records → court-generated or unknown doc.
            // Q17 fix (Phase 5.3): dedup against existing court-gen rows by canonical
            // EfmDocumentId (DocumentFileControlID). Pre-fix this keyed against the
            // doc-type code accidentally living in EfmDocumentId (e.g., "EFM001"); post-B0b
            // it correctly keys against the per-instance handle (e.g., "390903").
            var alreadyStored = !string.IsNullOrEmpty(nfrcDoc.EfmDocumentId) && existingDocs.Any(d =>
                d.IsCourtGenerated &&
                d.CourtDocumentId == nfrcDoc.EfmDocumentId);

            if (alreadyStored)
                continue;

            // Q17 no-guess fail-loud: when both schema identifiers are absent
            // (DocumentFileControlID empty AND no IdentificationID — per WSDL both are 0..N
            // optional, FilingReviewMDEPort.wsdl:12251 + :12253), we have no canonical
            // identifier. Insert with a synthetic GUID + structured warning rather than
            // guessing a composite key from description text. The "court-anomaly-" prefix
            // makes the row forensically identifiable for manual review.
            string documentReferenceId;
            if (!string.IsNullOrEmpty(nfrcDoc.EfmDocumentId))
                documentReferenceId = nfrcDoc.EfmDocumentId;
            else if (!string.IsNullOrEmpty(nfrcDoc.DocumentCode))
                documentReferenceId = nfrcDoc.DocumentCode;
            else
            {
                documentReferenceId = $"court-anomaly-{Guid.NewGuid():N}";
                _logger.LogWarning(
                    "NFRC court-gen doc has no canonical identifier (DocumentFileControlID + IdentificationID both empty) — inserting with synthetic ID {SyntheticId} for filing {FilingId}, description '{Desc}'. Manual review recommended (Q17 fail-loud branch).",
                    documentReferenceId, record.Id, nfrcDoc.DocumentDescriptionText);
            }

            newCourtDocs.Add(new EFilingDocumentRecord
            {
                EFilingOrderRecordId = record.Id,
                DocumentReferenceId = documentReferenceId,
                DocumentCode = nfrcDoc.DocumentCode ?? nfrcDoc.DocumentDescriptionText ?? "COURT_DOC",
                IsLeadDocument = false,
                IsCourtGenerated = true,
                DocumentDescription = nfrcDoc.DocumentDescriptionText,
                OriginalFileName = nfrcDoc.DocumentDescriptionText != null
                    ? $"{nfrcDoc.DocumentDescriptionText}.pdf" : null,
                DocumentFilingStatusCode = nfrcDoc.DocumentFilingStatusCode,
                DocumentStatusText = nfrcDoc.DocumentStatusText,
                // Q22-B fix (Phase 5.7): court-issued judicial docs (e.g., judge's signed Order
                // introduced as a new court-generated artifact in NFRC #3) carry disposition
                // fields directly. Pre-fix the court-gen insert path silently dropped both —
                // mirror the filer-uploaded update path above so the disposition is captured.
                DocumentDispositionType = nfrcDoc.DocumentDispositionType,
                DocumentDispositionDate = nfrcDoc.DocumentDispositionDate,
                CourtDocumentId = nfrcDoc.CmsDocumentId ?? nfrcDoc.EfmDocumentId,
                ConformedCopyUrl = nfrcDoc.BinaryLocationUri,
            });
        }

        if (newCourtDocs.Count > 0)
            await _orderService.InsertDocumentRecordsAsync(newCourtDocs, ct);
    }

    private async Task UpdateFeesFromNfrcAsync(EFilingOrderRecord record, NfrcResult nfrc, CancellationToken ct)
    {
        // Replace existing "Charged" fees with the latest from NFRC
        await _orderService.DeleteFeeRecordsBySourceAsync(record.Id, "Charged", ct);

        var feeRecords = nfrc.FeeLineItems.Select(li => new EFilingFeeRecord
        {
            EFilingOrderRecordId = record.Id,
            Source = "Charged",
            Amount = li.Amount,
            AccountingCostCode = li.AccountingCostCode,
            Description = li.Description
        });

        await _orderService.InsertFeeRecordsAsync(feeRecords, ct);
    }

    /// <summary>
    /// Download all available documents (conformed copies + court-returned) as a ZIP file.
    /// GET /api/efiling/download-all/{orderId}
    /// </summary>
    [HttpGet("download-all/{orderId:int}")]
    public async Task<IActionResult> DownloadAllDocuments(int orderId, [FromQuery] string? type, CancellationToken ct)
    {
        var record = await _orderService.GetByOrderIdAsync(orderId, ct);
        if (record == null)
            return NotFound(new { error = "Filing record not found for this order." });

        var docs = await _orderService.GetDocumentsByOrderRecordIdAsync(record.Id, ct);

        // Filter by tab type if specified
        if (string.Equals(type, "uploaded", StringComparison.OrdinalIgnoreCase))
            docs = docs.Where(d => !d.IsCourtGenerated).ToList();
        else if (string.Equals(type, "court", StringComparison.OrdinalIgnoreCase))
            docs = docs.Where(d => d.IsCourtGenerated).ToList();

        // For each doc, prefer ConformedCopyUrl (court-stamped), fall back to BlobUrl (original upload)
        var downloadable = docs
            .Select(d => new { Doc = d, Url = !string.IsNullOrEmpty(d.ConformedCopyUrl) ? d.ConformedCopyUrl : d.BlobUrl })
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .ToList();

        if (downloadable.Count == 0)
            return NotFound(new { error = "No downloadable documents available yet." });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var zipStream = new System.IO.MemoryStream();

        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            int idx = 0;
            foreach (var item in downloadable)
            {
                var doc = item.Doc;
                var downloadUrl = item.Url;
                try
                {
                    var bytes = await httpClient.GetByteArrayAsync(downloadUrl, ct);
                    var fileName = !string.IsNullOrEmpty(doc.OriginalFileName)
                        ? doc.OriginalFileName
                        : $"{doc.DocumentDescription ?? doc.DocumentCode ?? "document"}_{++idx}.pdf";

                    // Sanitize filename
                    foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                        fileName = fileName.Replace(c, '_');

                    var entry = archive.CreateEntry(fileName, System.IO.Compression.CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download document {DocId} from {Url}", doc.Id, downloadUrl);
                }
            }
        }

        zipStream.Position = 0;
        var zipFileName = $"Filing_Documents_Order_{orderId}.zip";
        return File(zipStream.ToArray(), "application/zip", zipFileName);
    }

    private async Task SyncNopOrderStatusAsync(EFilingOrderRecord record, CancellationToken ct)
    {
        var order = await _nopOrderService.GetOrderByIdAsync(record.OrderId);
        if (order == null) return;

        // Map court status → nopCommerce OrderStatus
        var targetStatus = record.FilingStatus?.ToUpperInvariant() switch
        {
            "ACCEPTED" or "REVIEWED" => NopOrderStatus.Complete,
            "PARTIALLY_ACCEPTED" or "PARTIALLYACCEPTED" => NopOrderStatus.Processing,
            "REJECTED" or "CANCELLED" => NopOrderStatus.Cancelled,
            _ => NopOrderStatus.Pending // RECEIVED_UNDER_REVIEW
        };

        if (order.OrderStatus != targetStatus)
        {
            order.OrderStatusId = (int)targetStatus;
            await _nopOrderService.UpdateOrderAsync(order);
            _logger.LogInformation("Order {OrderId} status updated to {Status}", order.Id, targetStatus);
        }
    }
}
