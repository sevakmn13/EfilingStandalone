using EFiling.Nop.Domain;

namespace EFiling.Nop.Services;

/// <summary>
/// Service for managing EFilingOrderRecord and related child entities
/// (documents, fees, NFRC logs).
/// </summary>
public interface IEFilingOrderService
{
    // ─── Order Record ────────────────────────────────────────────────

    /// <summary>Create a new order record after successful submission.</summary>
    Task<EFilingOrderRecord> CreateOrderRecordAsync(EFilingOrderRecord record, CancellationToken ct = default);

    /// <summary>Get an order record by its ID.</summary>
    Task<EFilingOrderRecord?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Get an order record by nopCommerce Order ID.</summary>
    Task<EFilingOrderRecord?> GetByOrderIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>Get an order record by EFSP reference ID.</summary>
    Task<EFilingOrderRecord?> GetByEfspReferenceIdAsync(string efspReferenceId, CancellationToken ct = default);

    /// <summary>Get an order record by EFM reference ID.</summary>
    Task<EFilingOrderRecord?> GetByEfmReferenceIdAsync(string efmReferenceId, CancellationToken ct = default);

    /// <summary>Update an existing order record (auto-sets UpdatedUtc).</summary>
    Task UpdateOrderRecordAsync(EFilingOrderRecord record, CancellationToken ct = default);

    /// <summary>
    /// Atomically increment <see cref="EFilingOrderRecord.NfrcCount"/> via a single
    /// SQL <c>UPDATE … SET NfrcCount = NfrcCount + 1</c>, eliminating the read-modify-write
    /// race that fires on concurrent NFRC webhooks (notably Madera's vendor-side
    /// double-dispatch of NFRC #2). Returns the new post-increment count.
    /// Throws <see cref="InvalidOperationException"/> if no row matches the given id.
    /// </summary>
    Task<int> IncrementNfrcCountAsync(int orderRecordId, CancellationToken ct = default);

    /// <summary>
    /// Compare-and-swap atomic UPDATE for the polling task's <c>GetFilingStatus</c> path
    /// (closes the residual polling-task clobber window after Q19's webhook-side TX wrapper
    /// — see audit § 15.6 "Polling-task CAS"). Issues a single SQL <c>UPDATE</c> that
    /// touches ONLY the polling-relevant fields (<see cref="EFilingOrderRecord.FilingStatus"/>,
    /// <see cref="EFilingOrderRecord.CaseNumber"/>, <see cref="EFilingOrderRecord.CaseDocketId"/>,
    /// <see cref="EFilingOrderRecord.ErrorText"/>, <see cref="EFilingOrderRecord.EfmReferenceId"/>,
    /// <see cref="EFilingOrderRecord.LastNfrcDateUtc"/>, <see cref="EFilingOrderRecord.NfrcCount"/>)
    /// — leaving webhook-owned fields (<see cref="EFilingOrderRecord.CaseTitle"/>,
    /// <see cref="EFilingOrderRecord.ReceiptUrl"/>, etc.) untouched. The CAS <c>WHERE</c>
    /// clause requires <c>FilingStatus IS NULL OR FilingStatus = 'RECEIVED_UNDER_REVIEW'</c>,
    /// so the UPDATE no-ops if a concurrent webhook has already advanced the row to a
    /// terminal status — preventing both field clobber AND duplicate acceptance emails.
    /// </summary>
    /// <param name="id">EFilingOrderRecord primary key.</param>
    /// <param name="newFilingStatus">Terminal status to advance to ("ACCEPTED",
    /// "PARTIALLY_ACCEPTED", "REJECTED"). Always written.</param>
    /// <param name="caseNumberOverride">If non-null and <paramref name="clearCaseNumber"/>
    /// is false, sets CaseNumber to this value. If null AND <paramref name="clearCaseNumber"/>
    /// is false, leaves CaseNumber unchanged.</param>
    /// <param name="clearCaseNumber">If true, explicitly sets CaseNumber to NULL (used on
    /// REJECTED transitions). Overrides <paramref name="caseNumberOverride"/>.</param>
    /// <param name="caseDocketIdOverride">If non-null, sets CaseDocketId; null leaves it.</param>
    /// <param name="errorTextOverride">If non-null, sets ErrorText; null leaves it.</param>
    /// <param name="efmReferenceIdOverride">If non-null, sets EfmReferenceId; null leaves it.</param>
    /// <param name="lastNfrcDateUtc">Always written.</param>
    /// <returns><c>true</c> if the row was advanced (caller should send notification +
    /// nopCommerce sync); <c>false</c> if a concurrent webhook already won the race
    /// (caller should bail — webhook owns the post-commit side effects).</returns>
    Task<bool> TryAdvanceFilingStatusFromPollAsync(
        int id,
        string newFilingStatus,
        string? caseNumberOverride,
        bool clearCaseNumber,
        string? caseDocketIdOverride,
        string? errorTextOverride,
        string? efmReferenceIdOverride,
        DateTime lastNfrcDateUtc,
        CancellationToken ct = default);

    /// <summary>Get all order records for a list of nopCommerce Order IDs.</summary>
    Task<List<EFilingOrderRecord>> GetByOrderIdsAsync(IEnumerable<int> orderIds, CancellationToken ct = default);

    /// <summary>Get order records with a specific filing status (for polling).</summary>
    Task<List<EFilingOrderRecord>> GetByFilingStatusAsync(string filingStatus, CancellationToken ct = default);

    // ─── Document Records ────────────────────────────────────────────

    /// <summary>Insert document records for a filing.</summary>
    Task InsertDocumentRecordsAsync(IEnumerable<EFilingDocumentRecord> records, CancellationToken ct = default);

    /// <summary>Get all document records for a filing order.</summary>
    Task<List<EFilingDocumentRecord>> GetDocumentsByOrderRecordIdAsync(int eFilingOrderRecordId, CancellationToken ct = default);

    /// <summary>Update a document record (auto-sets UpdatedUtc).</summary>
    Task UpdateDocumentRecordAsync(EFilingDocumentRecord record, CancellationToken ct = default);

    // ─── Fee Records ─────────────────────────────────────────────────

    /// <summary>Insert fee line items for a filing.</summary>
    Task InsertFeeRecordsAsync(IEnumerable<EFilingFeeRecord> records, CancellationToken ct = default);

    /// <summary>Get all fee records for a filing order, optionally filtered by source.</summary>
    Task<List<EFilingFeeRecord>> GetFeesByOrderRecordIdAsync(int eFilingOrderRecordId, string? source = null, CancellationToken ct = default);

    /// <summary>Delete fee records by source (e.g., clear "Charged" before re-inserting from a newer NFRC).</summary>
    Task DeleteFeeRecordsBySourceAsync(int eFilingOrderRecordId, string source, CancellationToken ct = default);

    // ─── NFRC Logs ───────────────────────────────────────────────────

    /// <summary>Insert a raw NFRC log entry.</summary>
    Task InsertNfrcLogAsync(EFilingNfrcLog log, CancellationToken ct = default);

    /// <summary>Get all NFRC logs for a filing order.</summary>
    Task<List<EFilingNfrcLog>> GetNfrcLogsByOrderRecordIdAsync(int eFilingOrderRecordId, CancellationToken ct = default);
}
