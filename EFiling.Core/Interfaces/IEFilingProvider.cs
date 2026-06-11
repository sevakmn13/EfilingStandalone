using EFiling.Core.Models;

namespace EFiling.Core.Interfaces;

/// <summary>
/// Core interface that every eFiling provider (JTI, future vendors) must implement.
/// All operations are async and accept a <see cref="CourtConfiguration"/> to identify the target court.
/// </summary>
public interface IEFilingProvider
{
    /// <summary>Provider identifier (e.g., "JTI").</summary>
    string ProviderName { get; }

    // ─── Court Policy ───────────────────────────────────────────────

    /// <summary>Retrieve the court's machine-readable policy.</summary>
    Task<CourtPolicy> GetPolicyAsync(CourtConfiguration config, CancellationToken ct = default);

    // ─── Code Lists ─────────────────────────────────────────────────

    /// <summary>Fetch a specific code list by type (e.g., "CASE_TYPE", "CASE_CATEGORY").</summary>
    Task<List<CodeListItem>> GetCodeListAsync(CourtConfiguration config, string codeListType, CancellationToken ct = default);

    /// <summary>Fetch the document list, optionally filtered by case type and sub-filing flag.</summary>
    Task<List<DocumentListItem>> GetDocumentListAsync(CourtConfiguration config, string? caseType = null, bool subFiling = false, CancellationToken ct = default);

    /// <summary>Look up court locations by zip code (and optionally case type / category).</summary>
    Task<List<CourtLocation>> GetCourtLocationsAsync(CourtConfiguration config, string? zipCode = null, string? caseType = null, string? caseCategory = null, CancellationToken ct = default);

    /// <summary>Search attorneys by bar number.</summary>
    Task<AttorneyInfo?> LookupAttorneyByBarNumberAsync(CourtConfiguration config, string barNumber, CancellationToken ct = default);

    /// <summary>Search attorneys by name.</summary>
    Task<List<AttorneyInfo>> SearchAttorneysByNameAsync(CourtConfiguration config, string firstName, string lastName, CancellationToken ct = default);

    /// <summary>Search attorneys by firm name.</summary>
    Task<List<AttorneyInfo>> SearchAttorneysByFirmAsync(CourtConfiguration config, string firmName, CancellationToken ct = default);

    /// <summary>Get metadata items for a specific document type code.</summary>
    Task<List<DocumentMetadataItem>> GetDocumentMetadataAsync(CourtConfiguration config, string documentCode, CancellationToken ct = default);

    // ─── Case Operations ────────────────────────────────────────────

    /// <summary>Search for cases (GetCaseList).</summary>
    Task<List<CaseInfo>> SearchCasesAsync(CourtConfiguration config, CaseSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Get full case details (GetCase) by docket ID or tracking ID.</summary>
    Task<CaseInfo?> GetCaseAsync(CourtConfiguration config, string? caseDocketId = null, string? caseTrackingId = null, bool includeParticipants = true, bool includeDocketEntries = false, CancellationToken ct = default);

    // ─── Filing Operations ──────────────────────────────────────────

    /// <summary>Calculate fees for a filing (GetFeesCalculation).</summary>
    Task<FeeCalculation> CalculateFeesAsync(CourtConfiguration config, FilingSubmission submission, CancellationToken ct = default);

    /// <summary>Submit a filing (ReviewFiling).</summary>
    Task<FilingResult> SubmitFilingAsync(CourtConfiguration config, FilingSubmission submission, CancellationToken ct = default);

    // ─── Status Operations ──────────────────────────────────────────

    /// <summary>Get filing status by EFM reference ID or EFSP reference ID.</summary>
    Task<FilingStatusResult> GetFilingStatusAsync(CourtConfiguration config, string? efmReferenceId = null, string? efspReferenceId = null, CancellationToken ct = default);

    /// <summary>Get list of filings matching filter criteria.</summary>
    Task<List<FilingListItem>> GetFilingListAsync(CourtConfiguration config, FilingListCriteria criteria, CancellationToken ct = default);

    /// <summary>Request re-delivery of the last NFRC for a filing (GetNFRC).</summary>
    Task<bool> RequestNfrcAsync(CourtConfiguration config, string? efmReferenceId = null, string? efspReferenceId = null, CancellationToken ct = default);

    // ─── Fee Operations ───────────────────────────────────────────

    /// <summary>Get the actual charged amount for a filed filing (GetChargedAmount).</summary>
    Task<FeeCalculation> GetChargedAmountAsync(CourtConfiguration config, string efmReferenceId, CancellationToken ct = default);
}
