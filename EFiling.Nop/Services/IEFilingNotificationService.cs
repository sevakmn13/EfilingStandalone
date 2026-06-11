using EFiling.Nop.Domain;

namespace EFiling.Nop.Services;

/// <summary>
/// Sends email notifications to customers when their court filing status changes.
/// Uses nopCommerce's message template infrastructure.
/// </summary>
public interface IEFilingNotificationService
{
    /// <summary>
    /// Send a filing status change notification to the customer who placed the order.
    /// Only sends if the status actually changed (caller should check before calling).
    /// </summary>
    /// <param name="orderRecord">The EFiling order record with updated status.</param>
    /// <param name="previousStatus">The status before the change (to avoid duplicate emails).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendFilingStatusChangedAsync(EFilingOrderRecord orderRecord, string? previousStatus, CancellationToken ct = default);
}
