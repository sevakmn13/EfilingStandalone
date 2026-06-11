using EFiling.Nop.Domain;
using LinqToDB;
using Nop.Data;

namespace EFiling.Nop.Services;

/// <summary>
/// Database-backed service for EFilingOrderRecord and related child entities.
/// Uses nopCommerce's IRepository pattern.
/// </summary>
public class EFilingOrderService : IEFilingOrderService
{
    private readonly IRepository<EFilingOrderRecord> _orderRepo;
    private readonly IRepository<EFilingDocumentRecord> _docRepo;
    private readonly IRepository<EFilingFeeRecord> _feeRepo;
    private readonly IRepository<EFilingNfrcLog> _nfrcLogRepo;

    public EFilingOrderService(
        IRepository<EFilingOrderRecord> orderRepo,
        IRepository<EFilingDocumentRecord> docRepo,
        IRepository<EFilingFeeRecord> feeRepo,
        IRepository<EFilingNfrcLog> nfrcLogRepo)
    {
        _orderRepo = orderRepo;
        _docRepo = docRepo;
        _feeRepo = feeRepo;
        _nfrcLogRepo = nfrcLogRepo;
    }

    // ─── Order Record ────────────────────────────────────────────────

    public async Task<EFilingOrderRecord> CreateOrderRecordAsync(EFilingOrderRecord record, CancellationToken ct = default)
    {
        record.CreatedUtc = DateTime.UtcNow;
        record.UpdatedUtc = DateTime.UtcNow;
        await _orderRepo.InsertAsync(record);
        return record;
    }

    public async Task<EFilingOrderRecord?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _orderRepo.GetByIdAsync(id);

    public async Task<EFilingOrderRecord?> GetByOrderIdAsync(int orderId, CancellationToken ct = default)
    {
        var records = await _orderRepo.GetAllAsync(q =>
            q.Where(r => r.OrderId == orderId));
        return records.FirstOrDefault();
    }

    public async Task<EFilingOrderRecord?> GetByEfspReferenceIdAsync(string efspReferenceId, CancellationToken ct = default)
    {
        var records = await _orderRepo.GetAllAsync(q =>
            q.Where(r => r.EfspReferenceId == efspReferenceId));
        return records.FirstOrDefault();
    }

    public async Task<EFilingOrderRecord?> GetByEfmReferenceIdAsync(string efmReferenceId, CancellationToken ct = default)
    {
        var records = await _orderRepo.GetAllAsync(q =>
            q.Where(r => r.EfmReferenceId == efmReferenceId));
        return records.FirstOrDefault();
    }

    public async Task UpdateOrderRecordAsync(EFilingOrderRecord record, CancellationToken ct = default)
    {
        record.UpdatedUtc = DateTime.UtcNow;
        await _orderRepo.UpdateAsync(record);
    }

    public async Task<int> IncrementNfrcCountAsync(int orderRecordId, CancellationToken ct = default)
    {
        // Q16 fix (Phase 5.2): atomic field-level UPDATE via LinqToDB. This emits
        //   UPDATE EFilingOrderRecord SET NfrcCount = NfrcCount + 1, UpdatedUtc = @now WHERE Id = @id
        // as a single SQL statement, so concurrent webhooks can't lose increments
        // (the previous in-memory `record.NfrcCount++` followed by full-entity UpdateAsync
        // allowed two readers to both see N and both write N+1 — observed in steady state
        // for every Madera CC accept, where vendor-side double-dispatch of NFRC #2 produced
        // NfrcCount=3 for what were only 2 unique payloads).
        var rowsAffected = await _orderRepo.Table
            .Where(r => r.Id == orderRecordId)
            .Set(r => r.NfrcCount, r => r.NfrcCount + 1)
            .Set(r => r.UpdatedUtc, DateTime.UtcNow)
            .UpdateAsync(ct);

        if (rowsAffected == 0)
            throw new InvalidOperationException(
                $"EFilingOrderRecord {orderRecordId} not found for atomic NfrcCount increment");

        // Re-read just the updated NfrcCount column. Reads direct from DB
        // (skips entity cache) so the returned value reflects all concurrent increments.
        return await _orderRepo.Table
            .Where(r => r.Id == orderRecordId)
            .Select(r => r.NfrcCount)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryAdvanceFilingStatusFromPollAsync(
        int id,
        string newFilingStatus,
        string? caseNumberOverride,
        bool clearCaseNumber,
        string? caseDocketIdOverride,
        string? errorTextOverride,
        string? efmReferenceIdOverride,
        DateTime lastNfrcDateUtc,
        CancellationToken ct = default)
    {
        // Polling-task CAS (closes the residual after Q19's webhook-side TX wrapper —
        // see audit § 15.6 "Polling-task CAS"). Single SQL UPDATE that atomically:
        //   1. Checks status is still null/RECEIVED_UNDER_REVIEW (CAS WHERE — prevents
        //      duplicate emails when a webhook concurrently advanced the row).
        //   2. Sets ONLY polling-relevant fields — webhook-owned fields (CaseTitle,
        //      ReceiptUrl, etc.) are not touched, eliminating the full-entity clobber
        //      that the prior `UpdateOrderRecordAsync(record)` flow had.
        //   3. Bumps NfrcCount to ≥ 1 (preserves the original Math.Max(1, ...) semantic
        //      so the timeline UI shows at least one NFRC interaction).
        // Returns true if the row was advanced (rowsAffected > 0), false if the WHERE
        // clause failed because a webhook already committed terminal state.
        //
        // Lock semantics under SQL Server ReadCommitted (the default LinqToDB isolation):
        // an UPDATE statement takes a U-lock on candidate rows, re-reads the WHERE under
        // the lock, then converts to X-lock for the SET. So if a webhook holds an X-lock
        // (mid-TX from IncrementNfrcCountAsync inside Q19's TransactionScope), this CAS
        // blocks until webhook commits, then re-evaluates the WHERE — at which point
        // FilingStatus is the webhook's terminal value and the WHERE fails.

        var updatable = _orderRepo.Table
            .Where(r => r.Id == id
                     && (r.FilingStatus == null || r.FilingStatus == "RECEIVED_UNDER_REVIEW"))
            .AsUpdatable()
            .Set(r => r.FilingStatus, newFilingStatus)
            .Set(r => r.LastNfrcDateUtc, (DateTime?)lastNfrcDateUtc)
            .Set(r => r.NfrcCount, r => r.NfrcCount < 1 ? 1 : r.NfrcCount)
            .Set(r => r.UpdatedUtc, DateTime.UtcNow);

        // CaseNumber: clear (REJECTED) > explicit override > leave unchanged
        if (clearCaseNumber)
            updatable = updatable.Set(r => r.CaseNumber, (string?)null);
        else if (caseNumberOverride != null)
            updatable = updatable.Set(r => r.CaseNumber, caseNumberOverride);

        if (caseDocketIdOverride != null)
            updatable = updatable.Set(r => r.CaseDocketId, caseDocketIdOverride);

        if (errorTextOverride != null)
            updatable = updatable.Set(r => r.ErrorText, errorTextOverride);

        if (efmReferenceIdOverride != null)
            updatable = updatable.Set(r => r.EfmReferenceId, efmReferenceIdOverride);

        var rowsAffected = await updatable.UpdateAsync(ct);
        return rowsAffected > 0;
    }

    public async Task<List<EFilingOrderRecord>> GetByOrderIdsAsync(IEnumerable<int> orderIds, CancellationToken ct = default)
    {
        var ids = orderIds.ToList();
        var records = await _orderRepo.GetAllAsync(q =>
            q.Where(r => ids.Contains(r.OrderId)));
        return records.ToList();
    }

    public async Task<List<EFilingOrderRecord>> GetByFilingStatusAsync(string filingStatus, CancellationToken ct = default)
    {
        var records = await _orderRepo.GetAllAsync(q =>
            q.Where(r => r.FilingStatus == filingStatus));
        return records.ToList();
    }

    // ─── Document Records ────────────────────────────────────────────

    public async Task InsertDocumentRecordsAsync(IEnumerable<EFilingDocumentRecord> records, CancellationToken ct = default)
    {
        var list = records.ToList();
        var now = DateTime.UtcNow;
        foreach (var r in list) { r.CreatedUtc = now; r.UpdatedUtc = now; }
        await _docRepo.InsertAsync(list);
    }

    public async Task<List<EFilingDocumentRecord>> GetDocumentsByOrderRecordIdAsync(int eFilingOrderRecordId, CancellationToken ct = default)
    {
        var docs = await _docRepo.GetAllAsync(q =>
            q.Where(d => d.EFilingOrderRecordId == eFilingOrderRecordId));
        return docs.ToList();
    }

    public async Task UpdateDocumentRecordAsync(EFilingDocumentRecord record, CancellationToken ct = default)
    {
        record.UpdatedUtc = DateTime.UtcNow;
        await _docRepo.UpdateAsync(record);
    }

    // ─── Fee Records ─────────────────────────────────────────────────

    public async Task InsertFeeRecordsAsync(IEnumerable<EFilingFeeRecord> records, CancellationToken ct = default)
    {
        var list = records.ToList();
        var now = DateTime.UtcNow;
        foreach (var r in list) r.CreatedUtc = now;
        await _feeRepo.InsertAsync(list);
    }

    public async Task<List<EFilingFeeRecord>> GetFeesByOrderRecordIdAsync(int eFilingOrderRecordId, string? source = null, CancellationToken ct = default)
    {
        var fees = await _feeRepo.GetAllAsync(q =>
        {
            var query = q.Where(f => f.EFilingOrderRecordId == eFilingOrderRecordId);
            if (!string.IsNullOrEmpty(source))
                query = query.Where(f => f.Source == source);
            return query;
        });
        return fees.ToList();
    }

    public async Task DeleteFeeRecordsBySourceAsync(int eFilingOrderRecordId, string source, CancellationToken ct = default)
    {
        var toDelete = await _feeRepo.GetAllAsync(q =>
            q.Where(f => f.EFilingOrderRecordId == eFilingOrderRecordId && f.Source == source));
        if (toDelete.Any())
            await _feeRepo.DeleteAsync(toDelete);
    }

    // ─── NFRC Logs ───────────────────────────────────────────────────

    public async Task InsertNfrcLogAsync(EFilingNfrcLog log, CancellationToken ct = default)
    {
        log.ReceivedUtc = DateTime.UtcNow;
        await _nfrcLogRepo.InsertAsync(log);
    }

    public async Task<List<EFilingNfrcLog>> GetNfrcLogsByOrderRecordIdAsync(int eFilingOrderRecordId, CancellationToken ct = default)
    {
        var logs = await _nfrcLogRepo.GetAllAsync(q =>
            q.Where(l => l.EFilingOrderRecordId == eFilingOrderRecordId)
             .OrderByDescending(l => l.ReceivedUtc));
        return logs.ToList();
    }
}
