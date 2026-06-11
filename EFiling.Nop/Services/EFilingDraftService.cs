using EFiling.Nop.Domain;
using Nop.Data;

namespace EFiling.Nop.Services;

/// <summary>
/// Database-backed draft service using nopCommerce's IRepository pattern.
/// All data is stored in the EFilingDraft SQL Server table.
/// </summary>
public class EFilingDraftService : IEFilingDraftService
{
    private readonly IRepository<EFilingDraft> _repository;

    public EFilingDraftService(IRepository<EFilingDraft> repository)
    {
        _repository = repository;
    }

    public async Task<EFilingDraft> CreateAsync(EFilingDraft draft, CancellationToken ct = default)
    {
        draft.CreatedUtc = DateTime.UtcNow;
        draft.UpdatedUtc = DateTime.UtcNow;
        await _repository.InsertAsync(draft);
        return draft;
    }

    public async Task<EFilingDraft?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<List<EFilingDraft>> GetByCustomerAsync(int customerId, CancellationToken ct = default)
    {
        var drafts = await _repository.GetAllAsync(q =>
            q.Where(d => d.CustomerId == customerId && !d.IsSubmitted)
             .OrderByDescending(d => d.UpdatedUtc));
        return drafts.ToList();
    }

    public async Task<List<EFilingDraft>> GetByCustomerIdsAsync(IEnumerable<int> customerIds, CancellationToken ct = default)
    {
        var ids = customerIds.ToList();
        var drafts = await _repository.GetAllAsync(q =>
            q.Where(d => ids.Contains(d.CustomerId) && !d.IsSubmitted)
             .OrderByDescending(d => d.UpdatedUtc));
        return drafts.ToList();
    }

    public async Task UpdateAsync(EFilingDraft draft, CancellationToken ct = default)
    {
        draft.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(draft);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var draft = await _repository.GetByIdAsync(id);
        if (draft != null)
            await _repository.DeleteAsync(draft);
    }

    public async Task MarkSubmittedAsync(int id, string efmReferenceId, CancellationToken ct = default)
    {
        var draft = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Draft {id} not found.");
        draft.IsSubmitted = true;
        draft.EfmReferenceId = efmReferenceId;
        draft.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(draft);
    }
}
