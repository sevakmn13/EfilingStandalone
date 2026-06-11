using EFiling.Nop.Domain;

namespace EFiling.Nop.Services;

/// <summary>
/// CRUD service for EFilingDraft entities.
/// </summary>
public interface IEFilingDraftService
{
    /// <summary>Create a new draft.</summary>
    Task<EFilingDraft> CreateAsync(EFilingDraft draft, CancellationToken ct = default);

    /// <summary>Get a draft by ID.</summary>
    Task<EFilingDraft?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Get all non-submitted drafts for a customer.</summary>
    Task<List<EFilingDraft>> GetByCustomerAsync(int customerId, CancellationToken ct = default);

    /// <summary>Get all non-submitted drafts for multiple customers (firm accounts).</summary>
    Task<List<EFilingDraft>> GetByCustomerIdsAsync(IEnumerable<int> customerIds, CancellationToken ct = default);

    /// <summary>Update an existing draft (auto-sets UpdatedUtc).</summary>
    Task UpdateAsync(EFilingDraft draft, CancellationToken ct = default);

    /// <summary>Delete a draft permanently.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Mark a draft as submitted with the EFM reference ID.</summary>
    Task MarkSubmittedAsync(int id, string efmReferenceId, CancellationToken ct = default);
}
