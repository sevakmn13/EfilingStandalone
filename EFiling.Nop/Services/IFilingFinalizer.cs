using EFiling.Core.Models;
using EFiling.Nop.Models;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Stores;

namespace EFiling.Nop.Services;

/// <summary>
/// Result of <see cref="IFilingFinalizer.FinalizeAsync"/>. <see cref="OrderId"/> is the
/// nopCommerce <c>Order.Id</c>; <see cref="OrderRecordId"/> is the <c>EFilingOrderRecord.Id</c>.
/// Both are 0 on failure.
/// </summary>
public sealed record FinalizeFilingResult(bool Success, int OrderId, int OrderRecordId, string? ErrorMessage);

/// <summary>
/// Shared post-JTI-submit finalization (P1, 2026-04-26): runs the Braintree charge,
/// places a nopCommerce order, and creates <c>EFilingOrderRecord</c> + child
/// <c>EFilingDocumentRecord</c>/<c>EFilingFeeRecord</c> rows.
/// <para/>
/// <b>Source-agnostic.</b> Callers pass already-resolved inputs (no draft loading inside
/// the service). Today there are two callers:
/// <list type="bullet">
///   <item><c>EFilingMvcController.SubmitAndPayAjax</c> (CC AJAX) — passes draft-derived inputs.</item>
///   <item><c>EFilingMvcController.CreateCase</c> POST (SF form-post) — passes model-derived inputs.</item>
/// </list>
/// Future P4 (shared payment component) makes this the single canonical surface for
/// finalizing any e-filing submission.
/// <para/>
/// See <c>docs/EFILING_PAYMENT_FINALIZATION_AUDIT.md</c> for the audit trail of
/// pre-existing CC behaviors that this service inherits unchanged.
/// </summary>
public interface IFilingFinalizer
{
    /// <summary>
    /// Charge the user (Braintree), place the nopCommerce order, and create the
    /// <c>EFilingOrderRecord</c> + child rows. Returns failure (without throwing) on
    /// expected business failures (product missing, cart warnings, PlaceOrder errors).
    /// Throws on unexpected infrastructure failures (DB down, Braintree network blip).
    /// </summary>
    /// <param name="customer">The customer whose payment vault + cart are used.</param>
    /// <param name="store">The current nopCommerce store.</param>
    /// <param name="createModel">Caller's view model (used only for <c>CourtId</c> and <c>DocumentsJson</c>).</param>
    /// <param name="submission">The <c>FilingSubmission</c> already accepted by JTI.</param>
    /// <param name="fees">The <c>FeeCalculation</c> already returned by the provider for this submission.</param>
    /// <param name="filingResult">The <c>FilingResult</c> already returned by the provider after submit (carries the EFM ref).</param>
    /// <param name="savedPaymentMethodId">nopCommerce stored-payment-method id for the chosen Braintree saved card.</param>
    /// <param name="filingType">String to write into <c>EFilingOrderRecord.FilingType</c>. Caller's contract — see audit F-7.</param>
    /// <param name="caseTitle">Display-name string for the order record's case.</param>
    /// <param name="caseCategoryText">Optional human-readable case-category label; null is acceptable.</param>
    /// <param name="caseTypeText">Optional human-readable case-type label; null is acceptable.</param>
    /// <param name="submissionJson">JSON blob to persist on the order record (for replay / audit).</param>
    /// <param name="notificationEmails">All emails that should receive status notifications. Stored as comma-separated.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FinalizeFilingResult> FinalizeAsync(
        Customer customer,
        Store store,
        CreateCaseModel createModel,
        FilingSubmission submission,
        FeeCalculation fees,
        FilingResult filingResult,
        int savedPaymentMethodId,
        string filingType,
        string caseTitle,
        string? caseCategoryText,
        string? caseTypeText,
        string submissionJson,
        IReadOnlyList<string> notificationEmails,
        CancellationToken ct);
}
