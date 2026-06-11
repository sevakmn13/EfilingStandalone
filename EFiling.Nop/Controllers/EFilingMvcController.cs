using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.Mapping;
using EFiling.Nop.Models;
using EFiling.Nop.Services;
using EFiling.Nop.UdDisclaimer;
using EFiling.Providers.JTI.Config;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Services.Customers;
using Nop.Services.Orders;

namespace EFiling.Nop.Controllers;

/// <summary>
/// Thin ASP.NET MVC controller for court e-filing UI.
/// Delegates all business logic to <see cref="CourtFilingController"/>.
/// </summary>
[Route("CourtFiling")]
public class EFilingMvcController : Controller
{
    private readonly CourtFilingController _courtFiling;
    private readonly ICourtConfigurationService _courtConfigService;
    private readonly IEFilingProvider _provider;
    private readonly IServiceFeeService _serviceFeeService;
    private readonly IStoreContext _storeContext;
    private readonly IEFilingBlobService _blobService;
    private readonly IEFilingDraftService _draftService;
    private readonly IWorkContext _workContext;
    private readonly IEFilingOrderService _eFilingOrderService;
    private readonly ICustomerService _customerService;
    private readonly IFilingFinalizer _filingFinalizer;
    private readonly global::Nop.Services.Logging.ILogger _logger;
    // Step #43 — UD §1161.2 attestation audit service. Gates
    // CaseDetail + SubsequentFiling GETs on UD cases per JTI EFM doc
    // node/436#UnlawfulDetainer (UD-1 + UD-2 mandate).
    private readonly IUdAccessAttestationService _udAttestationService;

    /// <summary>Lenient JSON options that handle empty-string numeric fields from form data.</summary>
    private static readonly JsonSerializerOptions _lenientJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new NullableDecimalConverter() }
    };

    public EFilingMvcController(
        CourtFilingController courtFiling,
        ICourtConfigurationService courtConfigService,
        IEFilingProvider provider,
        IServiceFeeService serviceFeeService,
        IStoreContext storeContext,
        IEFilingBlobService blobService,
        IEFilingDraftService draftService,
        IWorkContext workContext,
        IEFilingOrderService eFilingOrderService,
        ICustomerService customerService,
        IFilingFinalizer filingFinalizer,
        global::Nop.Services.Logging.ILogger logger,
        IUdAccessAttestationService udAttestationService)
    {
        _courtFiling = courtFiling;
        _courtConfigService = courtConfigService;
        _provider = provider;
        _serviceFeeService = serviceFeeService;
        _storeContext = storeContext;
        _blobService = blobService;
        _draftService = draftService;
        _workContext = workContext;
        _eFilingOrderService = eFilingOrderService;
        _customerService = customerService;
        _filingFinalizer = filingFinalizer;
        _logger = logger;
        _udAttestationService = udAttestationService;
    }

    // ─── Index is served by Nop.Web's CourtFilingController (Views/CourtFiling/Index.cshtml) ─────

    // ─── Create Case ─────────────────────────────────────────────────

    [HttpGet("CreateCase")]
    public async Task<IActionResult> CreateCase(
        string? courtId, int? draftId,
        int? refileOrderId, bool refileRejectedOnly = false,
        int? subsequentOrderId = null,
        string? subsequentCaseNumber = null,
        string? subsequentComplaintId = null,
        CancellationToken ct = default)
    {
        var courts = await _courtConfigService.GetAllAsync(ct);
        var model = new CreateCaseModel();

        if (!string.IsNullOrEmpty(courtId))
            model.CourtId = courtId;

        if (draftId.HasValue)
            model.DraftId = draftId;

        var store = await _storeContext.GetCurrentStoreAsync();
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        ViewBag.Courts = courts;
        ViewBag.ServiceFee = await _serviceFeeService.GetServiceFeeAmountAsync(store.Id);
        ViewBag.FilerEmail = currentCustomer?.Email ?? "";

        // If a court is already selected, expose its config so the view can render
        // the staging/production badge before the filer starts preparing the filing.
        if (!string.IsNullOrEmpty(model.CourtId))
        {
            ViewBag.CourtConfig = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
        }

        // Load draft data for resume
        if (draftId.HasValue)
        {
            var draft = await _draftService.GetByIdAsync(draftId.Value, ct);
            if (draft != null && !draft.IsSubmitted)
                ViewBag.DraftJson = draft.SubmissionJson;
        }

        // ── Refile from a rejected/partially-accepted filing ──
        if (refileOrderId.HasValue)
        {
            var orderRecord = await _eFilingOrderService.GetByOrderIdAsync(refileOrderId.Value, ct);
            if (orderRecord?.SubmissionJson != null)
            {
                var refileJson = orderRecord.SubmissionJson;

                // For partial acceptance: filter documents to only include rejected ones
                if (refileRejectedOnly)
                {
                    var docRecords = await _eFilingOrderService
                        .GetDocumentsByOrderRecordIdAsync(orderRecord.Id, ct);
                    var rejectedCodes = docRecords
                        .Where(d => string.Equals(d.DocumentFilingStatusCode, "REJECTED",
                            StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.DocumentCode)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Parse the form JSON and filter documents array
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(refileJson);
                        var root = jsonDoc.RootElement;
                        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(refileJson)
                            ?? new Dictionary<string, JsonElement>();

                        if (root.TryGetProperty("documents", out var docsEl)
                            && docsEl.ValueKind == JsonValueKind.Array)
                        {
                            var filteredDocs = docsEl.EnumerateArray()
                                .Where(d => d.TryGetProperty("documentCode", out var dc)
                                    && rejectedCodes.Contains(dc.GetString() ?? ""))
                                .ToList();

                            dict["documents"] = JsonSerializer.Deserialize<JsonElement>(
                                JsonSerializer.Serialize(filteredDocs));
                        }

                        refileJson = JsonSerializer.Serialize(dict);
                    }
                    catch
                    {
                        // If parsing fails, pass the full JSON — user can manually remove accepted docs
                    }

                    // For partial acceptance refile, this becomes a subsequent filing to the assigned case
                    ViewBag.RefileCaseNumber = orderRecord.CaseNumber ?? orderRecord.CaseDocketId;
                }

                ViewBag.DraftJson = refileJson;
                ViewBag.IsRefile = true;
                ViewBag.RefileOrderId = refileOrderId.Value;
            }
        }

        // ── Subsequent filing into an existing case (from order detail) ──
        if (subsequentOrderId.HasValue)
        {
            var orderRecord = await _eFilingOrderService.GetByOrderIdAsync(subsequentOrderId.Value, ct);
            if (orderRecord != null)
            {
                ViewBag.IsSubsequentFiling = true;
                ViewBag.SubsequentCaseNumber = orderRecord.CaseNumber ?? orderRecord.CaseDocketId;
                ViewBag.SubsequentCourtId = orderRecord.CourtId;

                if (!string.IsNullOrEmpty(orderRecord.CourtId))
                    model.CourtId = orderRecord.CourtId;
            }
        }

        // ── Subsequent filing via case search (case number from search results) ──
        if (!string.IsNullOrEmpty(subsequentCaseNumber) && !subsequentOrderId.HasValue)
        {
            ViewBag.IsSubsequentFiling = true;
            ViewBag.SubsequentCaseNumber = subsequentCaseNumber;
            ViewBag.SubsequentCourtId = courtId;
            ViewBag.SubsequentComplaintId = subsequentComplaintId;
        }

        return View("CreateCase", model);
    }

    [HttpPost("CreateCase")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCase(CreateCaseModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var store1 = await _storeContext.GetCurrentStoreAsync();
            ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
            ViewBag.ServiceFee = await _serviceFeeService.GetServiceFeeAmountAsync(store1.Id);
            if (!string.IsNullOrEmpty(model.CourtId))
                ViewBag.CourtConfig = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
            return View("CreateCase", model);
        }

        // P1: Subsequent filings must select a payment method up-front. We gate BEFORE submitting
        // to JTI so a missing payment method never produces an orphan filing on the JTI side.
        // (CC's AJAX path /api/submit-and-pay already gates on this; the legacy CC form-post
        // path remains payment-less and is only reachable as a fallback — see
        // EFILING_PAYMENT_FINALIZATION_AUDIT.md F-1.)
        if (model.IsSubsequentFiling && !model.SelectedPaymentMethodId.HasValue)
        {
            TempData["ErrorMessage"] = "A payment method is required to submit a subsequent filing.";
            return RedirectToAction(nameof(SubsequentFiling), new {
                courtId = model.CourtId,
                caseDocketId = model.CaseDocketId,
                complaintId = model.ComplaintId
            });
        }

        var customerId = await GetCustomerIdAsync();
        var result = await _courtFiling.CreateCaseAsync(model, customerId, ct);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Error ?? "Filing submission failed.";

            // For subsequent filings, redirect back to SubsequentFiling page
            if (model.IsSubsequentFiling)
            {
                return RedirectToAction(nameof(SubsequentFiling), new {
                    courtId = model.CourtId,
                    caseDocketId = model.CaseDocketId,
                    complaintId = model.ComplaintId
                });
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Filing submission failed.");
            var store2 = await _storeContext.GetCurrentStoreAsync();
            ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
            ViewBag.ServiceFee = await _serviceFeeService.GetServiceFeeAmountAsync(store2.Id);
            if (!string.IsNullOrEmpty(model.CourtId))
                ViewBag.CourtConfig = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
            return View("CreateCase", model);
        }

        // ── P1: SF finalization ───────────────────────────────────────
        // Subsequent filings finalize through the same FinalizeFilingAsync helper as CC's AJAX
        // submit-and-pay path. This is the entire point of P1 — close the Q1/Q2 gap from the
        // SUBSEQUENT_FILING_E2E_AUDIT.md (no Braintree charge, no EFilingOrderRecord on SF).
        if (model.IsSubsequentFiling)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            // SF-side finalization inputs:
            //   - notificationEmails: just the filer (SF UI doesn't expose extra recipients today).
            //   - caseTitle: prefer pre-loaded existing-case title; fall back to BuildDisplayName.
            //   - case category/type text: null for SF (per F-6 in EFILING_PAYMENT_FINALIZATION_AUDIT.md;
            //                              SF cshtml leaves caseCategoryName/caseTypeName as TODO).
            //   - submissionJson: re-serialize the model (no draft for SF unless DraftId set).
            //   - filingType: passed from submission.FilingType (per F-7 invariant).
            var notifyEmails = string.IsNullOrWhiteSpace(customer.Email)
                ? Array.Empty<string>()
                : new[] { customer.Email.Trim() };

            var caseTitle = !string.IsNullOrWhiteSpace(model.CaseTitle)
                ? model.CaseTitle!
                : CourtFilingController.BuildDisplayName(model);

            var submissionJson = JsonSerializer.Serialize(model, _lenientJsonOptions);

            var finalize = await _filingFinalizer.FinalizeAsync(
                customer: customer,
                store: store,
                createModel: model,
                submission: result.Submission!,
                fees: result.Fees!,
                filingResult: result.FilingResult!,
                savedPaymentMethodId: model.SelectedPaymentMethodId!.Value,
                filingType: result.Submission!.FilingType.ToString(),
                caseTitle: caseTitle,
                caseCategoryText: null,
                caseTypeText: null,
                submissionJson: submissionJson,
                notificationEmails: notifyEmails,
                ct: ct);

            if (!finalize.Success)
            {
                // CC AUDIT (F-1, P2b/P4): JTI submit succeeded but local finalization failed.
                // The filing exists at JTI without a corresponding EFilingOrderRecord. NFRC will
                // log as orphan (Q15-style). P4 should attempt a CancelFiling here.
                await _logger.ErrorAsync(
                    $"SF finalization failed AFTER JTI submit succeeded. EfspRef={result.Submission!.EfspReferenceId}, EfmRef={result.EfmReferenceId}, Error={finalize.ErrorMessage}");

                TempData["ErrorMessage"] = $"Filing was submitted to the court but post-submit processing failed: {finalize.ErrorMessage}. Reference: {result.EfmReferenceId}. Please contact support.";
                return RedirectToAction(nameof(SubsequentFiling), new {
                    courtId = model.CourtId,
                    caseDocketId = model.CaseDocketId,
                    complaintId = model.ComplaintId
                });
            }

            // Best-effort blob cleanup if this SF was resumed from a draft.
            if (model.DraftId.HasValue)
            {
                try
                {
                    await _blobService.DeleteDraftFolderAsync(customer.Id, model.DraftId.Value.ToString(), ct);
                }
                catch (Exception blobEx)
                {
                    await _logger.WarningAsync($"Failed to clean up blobs for draft {model.DraftId.Value}: {blobEx.Message}");
                }
            }

            TempData["SuccessMessage"] = $"Subsequent filing submitted and payment processed successfully. Reference: {result.EfmReferenceId}";
            return RedirectToAction(nameof(FilingStatus), new {
                courtId = model.CourtId,
                efmReferenceId = result.EfmReferenceId
            });
        }

        // CC legacy form-post fallback (no payment) — unchanged from pre-P1.
        // CC users normally hit /api/submit-and-pay (AJAX) instead of this form-post path.
        TempData["SuccessMessage"] = $"Filing submitted successfully. Reference: {result.EfmReferenceId}";
        return RedirectToAction(nameof(FilingStatus), new { courtId = model.CourtId, efmReferenceId = result.EfmReferenceId });
    }

    // ─── Subsequent Filing (dedicated flow for filing into an existing case) ───

    [HttpGet("SubsequentFiling")]
    public async Task<IActionResult> SubsequentFiling(
        string? courtId, string? caseDocketId, string? complaintId,
        int? subsequentOrderId = null,
        int? draftId = null,
        CancellationToken ct = default)
    {
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        
        // If draftId provided, load draft and extract court/case info
        EFilingDraft? draft = null;
        if (draftId.HasValue)
        {
            draft = await _draftService.GetByIdAsync(draftId.Value, ct);
            if (draft == null || draft.CustomerId != currentCustomer.Id)
            {
                TempData["ErrorMessage"] = "Draft not found or access denied.";
                return RedirectToAction(nameof(SearchCase));
            }
            
            // Extract court and case info from draft
            courtId ??= draft.CourtId;
            caseDocketId ??= draft.CaseDocketId;
            
            // Try to get complaintId from draft JSON if not provided
            if (string.IsNullOrEmpty(complaintId) && !string.IsNullOrEmpty(draft.SubmissionJson))
            {
                try
                {
                    using var draftDoc = JsonDocument.Parse(draft.SubmissionJson);
                    if (draftDoc.RootElement.TryGetProperty("complaintId", out var cid))
                        complaintId = cid.GetString();
                }
                catch { /* Ignore parse errors */ }
            }
        }
        
        // Resolve court + case number from order record if needed
        if (subsequentOrderId.HasValue)
        {
            var orderRecord = await _eFilingOrderService.GetByOrderIdAsync(subsequentOrderId.Value, ct);
            if (orderRecord == null)
            {
                TempData["ErrorMessage"] = $"No filing record found for order #{subsequentOrderId.Value}.";
                return RedirectToAction(nameof(SearchCase));
            }
            courtId ??= orderRecord.CourtId;
            caseDocketId ??= orderRecord.CaseNumber ?? orderRecord.CaseDocketId;

            if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(caseDocketId))
            {
                TempData["ErrorMessage"] = $"Order #{subsequentOrderId.Value} filing record is missing court ('{orderRecord.CourtId}') or case number (CaseNumber='{orderRecord.CaseNumber}', CaseDocketId='{orderRecord.CaseDocketId}'). Filing status: {orderRecord.FilingStatus}";
                return RedirectToAction(nameof(SearchCase));
            }
        }

        if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(caseDocketId))
        {
            TempData["ErrorMessage"] = "Court and case number are required for subsequent filing.";
            return RedirectToAction(nameof(SearchCase));
        }

        // Fetch case info from the court (with diagnostics)
        var (caseInfo, diagError) = await _courtFiling.GetCaseWithDiagnosticsAsync(courtId, caseDocketId, ct);
        if (caseInfo == null)
        {
            TempData["ErrorMessage"] = diagError ?? $"Case '{caseDocketId}' not found at court '{courtId}'.";
            return RedirectToAction(nameof(SearchCase));
        }

        // Step #43 — UD §1161.2 disclaimer gate. Per JTI EFM doc
        // node/436#UnlawfulDetainer (UD-1 + UD-2), Unlawful Detainer case
        // access requires verbatim disclaimer + party-attestation Y/N gate +
        // audit capture BEFORE the user can access case data. If the case
        // category resolves to UD and the current customer has no valid
        // recent attestation, redirect to the attestation interceptor page.
        var udRedirect = await GateUdAccessAsync(courtId, caseDocketId, caseInfo.CaseCategoryCode,
            Url.Action(nameof(SubsequentFiling), new { courtId, caseDocketId, complaintId }) ?? "/CourtFiling/SearchCase", ct);
        if (udRedirect != null)
            return udRedirect;

        var store = await _storeContext.GetCurrentStoreAsync();

        ViewBag.CourtId = courtId;
        ViewBag.CaseDocketId = caseDocketId;
        ViewBag.ComplaintId = complaintId;
        ViewBag.ServiceFee = await _serviceFeeService.GetServiceFeeAmountAsync(store.Id);
        ViewBag.FilerEmail = currentCustomer?.Email ?? "";

        // Expose court config so the view can render the staging/production badge.
        // Filers MUST see whether they are filing into a test or live court.
        ViewBag.CourtConfig = await _courtConfigService.GetByCourtIdAsync(courtId, ct);

        // Pass draft data to view for restoration
        ViewBag.DraftId = draftId;
        ViewBag.DraftJson = draft?.SubmissionJson;

        // Step #42-R → #43 history note:
        // • Step #42 wired UD disclaimer + Family redaction cards into the
        //   SF.cshtml itself — reverted in Step #42-R for source-fidelity
        //   (wrong stage + paraphrased text + no source for Family).
        // • Step #43 (above, lines ~416–425) implements the JTI-mandated
        //   pattern: server-enforced gate via GateUdAccessAsync that
        //   redirects to the UdAttestation interceptor view BEFORE the SF
        //   view ever renders. By the time we reach this point a valid Y
        //   attestation exists for this (customer, court, case) tuple.

        return View("SubsequentFiling", caseInfo);
    }

    [HttpPost("api/draft/save")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveDraftAjax(CancellationToken ct)
    {
        var customerId = await GetCustomerIdAsync();
        using var reader = new StreamReader(Request.Body);
        var formJson = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(formJson);
        var body = doc.RootElement;

        var courtId = body.TryGetProperty("courtId", out var cid) ? cid.GetString() ?? "" : "";
        var draftIdProp = body.TryGetProperty("draftId", out var did) && did.ValueKind == JsonValueKind.Number ? did.GetInt32() : (int?)null;
        
        // Subsequent filing detection: if caseDocketId is present, this is a subsequent filing
        var caseDocketId = body.TryGetProperty("caseDocketId", out var cdid) ? cdid.GetString() : null;
        var isSubsequentFiling = !string.IsNullOrEmpty(caseDocketId);
        var filingType = isSubsequentFiling ? "Subsequent" : "Initial";

        // Build display name from form data
        var displayName = "Draft";
        if (body.TryGetProperty("caseTypeName", out var ctn) && !string.IsNullOrEmpty(ctn.GetString()))
            displayName = ctn.GetString()!;
        else if (body.TryGetProperty("caseTypeText", out var ctt) && !string.IsNullOrEmpty(ctt.GetString()))
            displayName = ctt.GetString()!;
        
        // For subsequent filings, include case title in display name
        if (isSubsequentFiling && body.TryGetProperty("caseTitle", out var ctitle) && !string.IsNullOrEmpty(ctitle.GetString()))
            displayName = $"{ctitle.GetString()} - {displayName}";

        if (draftIdProp.HasValue)
        {
            var existing = await _draftService.GetByIdAsync(draftIdProp.Value, ct);
            if (existing != null && existing.CustomerId == customerId)
            {
                existing.SubmissionJson = formJson;
                existing.DisplayName = displayName;
                existing.FilingType = filingType;
                existing.CaseDocketId = caseDocketId;
                existing.UpdatedUtc = DateTime.UtcNow;
                await _draftService.UpdateAsync(existing, ct);
                return Json(new { success = true, draftId = existing.Id });
            }
        }

        var draft = new EFilingDraft
        {
            CustomerId = customerId,
            CourtId = courtId,
            CaseDocketId = caseDocketId,
            FilingType = filingType,
            SubmissionJson = formJson,
            DisplayName = displayName
        };
        var created = await _draftService.CreateAsync(draft, ct);
        return Json(new { success = true, draftId = created.Id });
    }

    [HttpPost("api/draft/upload-file")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UploadDraftFile(CancellationToken ct)
    {
        var customerId = await GetCustomerIdAsync();
        var draftId = Request.Form["draftId"].FirstOrDefault() ?? "new";
        var file = Request.Form.Files.FirstOrDefault();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        await using var stream = file.OpenReadStream();
        var info = await _blobService.UploadAsync(customerId, draftId, file.FileName, file.ContentType, stream, ct);

        return Json(new
        {
            success = true,
            blobPath = info.BlobPath,
            blobUrl = info.BlobUrl,
            fileName = info.FileName,
            fileSize = file.Length,
            contentType = info.ContentType
        });
    }

    [HttpPost("api/draft/delete-file")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteDraftFile(CancellationToken ct)
    {
        using var reader2 = new StreamReader(Request.Body);
        var json = await reader2.ReadToEndAsync(ct);
        using var doc2 = JsonDocument.Parse(json);
        var body = doc2.RootElement;
        var blobPath = body.TryGetProperty("blobPath", out var bp) ? bp.GetString() : null;
        if (string.IsNullOrEmpty(blobPath))
            return BadRequest(new { error = "No blobPath provided." });

        await _blobService.DeleteAsync(blobPath, ct);
        return Json(new { success = true });
    }

    // ─── Search Case ─────────────────────────────────────────────────

    [HttpGet("SearchCase")]
    public async Task<IActionResult> SearchCase(CancellationToken ct)
    {
        ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
        ViewBag.CategoriesByCourt = BuildCategoryOptionsByCourt();
        return View("SearchCase", new CaseSearchResultModel());
    }

    [HttpPost("SearchCase")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchCase(CaseSearchModel search, CancellationToken ct)
    {
        ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
        ViewBag.CategoriesByCourt = BuildCategoryOptionsByCourt();
        var result = await _courtFiling.SearchCasesAsync(search, ct);
        return View("SearchCase", result);
    }

    /// <summary>
    /// Step #57 — build the per-court category-dropdown options
    /// for SearchCase.cshtml. Returns a dictionary keyed by courtId, where each
    /// value is a list of <c>{ Jccc, JcccLabel, Code }</c> options ordered by
    /// Jccc grouping. The view groups options under <c>&lt;optgroup&gt;</c>
    /// elements per Jccc + uses the JCCC label as the optgroup label.
    /// <para>
    /// Source of truth: <c>JtiCourtCategoryMappings.json</c> (per-court
    /// CASE_CATEGORY → JCCC projections, currently 39 entries for Madera as of
    /// Step #57). The JCCC labels come from <c>JtiCaseCategoryPolicy.json</c>
    /// policies[*].label. Both are loaded via <c>JtiFieldSchemaProvider</c>
    /// static caches — no per-request I/O cost.
    /// </para>
    /// </summary>
    private static Dictionary<string, List<CategoryOption>> BuildCategoryOptionsByCourt()
    {
        var mappings = JtiFieldSchemaProvider.GetCourtCategoryMappings();
        var policy = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var result = new Dictionary<string, List<CategoryOption>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (courtId, courtMapping) in mappings.Courts)
        {
            var options = courtMapping.CategoryCodeToJccc
                .Select(kv => new CategoryOption(
                    Jccc: kv.Value,
                    JcccLabel: policy.Policies.TryGetValue(kv.Value, out var p) ? p.Label : kv.Value,
                    Code: kv.Key,
                    // Step #58 — per-code human-readable label. Falls back
                    // to the bare code when the court hasn't published a label entry.
                    Label: courtMapping.CodelistLabels.TryGetValue(kv.Key, out var lbl) ? lbl : kv.Key))
                .OrderBy(o => o.Jccc, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result[courtId] = options;
        }
        return result;
    }

    /// <summary>
    /// Step #57 — a single category-dropdown option (Jccc + numeric code).
    /// Step #58 — added <see cref="Label"/> for human-readable rendering.
    /// Used by <see cref="BuildCategoryOptionsByCourt"/> to populate <c>SearchCase.cshtml</c>.
    /// </summary>
    public sealed record CategoryOption(string Jccc, string JcccLabel, string Code, string Label);

    [HttpGet("CaseDetail")]
    public async Task<IActionResult> CaseDetail(string courtId, string caseDocketId, CancellationToken ct)
    {
        var caseInfo = await _courtFiling.GetCaseAsync(courtId, caseDocketId, ct);
        if (caseInfo == null)
        {
            TempData["ErrorMessage"] = $"Case '{caseDocketId}' not found.";
            return RedirectToAction(nameof(SearchCase));
        }

        // Step #43 — UD §1161.2 disclaimer gate. Same logic as
        // SubsequentFiling GET. Per JTI EFM doc node/436#UnlawfulDetainer
        // (UD-1 + UD-2), the disclaimer must fire when the user attempts to
        // access a UD case from search results — CaseDetail is one of the
        // two such access paths (the other is SubsequentFiling).
        var udRedirect = await GateUdAccessAsync(courtId, caseDocketId, caseInfo.CaseCategoryCode,
            Url.Action(nameof(CaseDetail), new { courtId, caseDocketId }) ?? "/CourtFiling/SearchCase", ct);
        if (udRedirect != null)
            return udRedirect;

        ViewBag.CourtId = courtId;
        return View("CaseDetail", caseInfo);
    }

    // ─── UD §1161.2 Attestation Interceptor (Step #43, 2026-05-21) ──────
    //
    // Implements UD-1 ("Public Disclaimer") + UD-2 ("Access Tracking and Data
    // Capture") from JTI EFM vendor doc node/436#UnlawfulDetainer. See:
    //   docs/JTI_SUBSEQUENT_FILING_CATALOG.md §5.6.1 – §5.6.2 (verbatim text
    //   + audit-capture requirements)
    //   docs/fileing files/Subsequent Filing/General Concepts/
    //     Subsequent Filing - General Concepts _ EFM Documentation.html
    //     lines 230–263 (the actual vendor doc)
    //
    // GateUdAccessAsync is the server-enforced gate used by CaseDetail GET
    // and SubsequentFiling GET. Returns non-null IActionResult when the
    // caller must redirect the user to the attestation view; returns null
    // when the user has a valid recent attestation (or the case is not UD).

    /// <summary>
    /// Server-enforced gate for UD case access. Returns a redirect
    /// <see cref="IActionResult"/> when the user must complete the
    /// §1161.2 attestation flow before proceeding; returns <c>null</c> when
    /// the case is not UD or the user already has a valid recent affirmative
    /// attestation.
    /// </summary>
    private async Task<IActionResult?> GateUdAccessAsync(
        string courtId, string caseDocketId, string? caseCategoryCode,
        string returnUrl, CancellationToken ct)
    {
        if (!UdDisclaimerPolicy.RequiresDisclaimer(courtId, caseCategoryCode))
            return null;

        var customerId = await GetCustomerIdAsync();
        if (customerId <= 0)
            return null; // Anonymous access — left to the standard auth flow

        var hasValid = await _udAttestationService.HasValidAttestationAsync(
            customerId, courtId, caseDocketId, ct);
        if (hasValid)
            return null;

        return RedirectToAction(nameof(UdAttestation), new
        {
            courtId,
            caseDocketId,
            caseCategoryCode,
            returnUrl
        });
    }

    /// <summary>
    /// JSON-aware variant of <see cref="GateUdAccessAsync"/> for AJAX/API
    /// endpoints that return JSON instead of rendering a view. Returns a
    /// non-null <see cref="JsonResult"/> with HTTP 403 + a structured payload
    /// when attestation is required; returns <c>null</c> when the case is
    /// not UD or the user already has a valid attestation.
    ///
    /// <para>
    /// Step #44 — closes the Step #43 defense-in-depth gap on
    /// <c>/api/efiling/case-detail</c>. The JSON payload shape:
    /// <code>
    /// { success: false, requiresAttestation: true, attestationUrl: "/CourtFiling/UdAttestation?...", error: "..." }
    /// </code>
    /// Client JS detects <c>requiresAttestation</c> and navigates to
    /// <c>attestationUrl</c> to complete the flow.
    /// </para>
    /// </summary>
    private async Task<IActionResult?> GateUdAccessJsonAsync(
        string courtId, string caseDocketId, string? caseCategoryCode,
        string returnUrl, CancellationToken ct)
    {
        if (!UdDisclaimerPolicy.RequiresDisclaimer(courtId, caseCategoryCode))
            return null;

        var customerId = await GetCustomerIdAsync();
        if (customerId <= 0)
            return null;

        var hasValid = await _udAttestationService.HasValidAttestationAsync(
            customerId, courtId, caseDocketId, ct);
        if (hasValid)
            return null;

        var attestationUrl = Url.Action(nameof(UdAttestation), new
        {
            courtId,
            caseDocketId,
            caseCategoryCode,
            returnUrl
        }) ?? "/CourtFiling/SearchCase";

        return new JsonResult(new
        {
            success = false,
            requiresAttestation = true,
            attestationUrl,
            error = "Access to this Unlawful Detainer case requires §1161.2 attestation. "
                  + "Please complete the disclosure before proceeding."
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    /// <summary>
    /// Renders the §1161.2 disclaimer + Y/N party-attestation form. Reached
    /// via <see cref="GateUdAccessAsync"/> when a user attempts to access
    /// a UD case without a valid recent attestation.
    /// </summary>
    [HttpGet("UdAttestation")]
    public IActionResult UdAttestation(string courtId, string caseDocketId,
        string? caseCategoryCode, string? returnUrl)
    {
        // Defense in depth: if the caller hand-crafts a URL with a non-UD
        // category, do not show the disclaimer (it would be confusing).
        if (!UdDisclaimerPolicy.RequiresDisclaimer(courtId, caseCategoryCode))
        {
            TempData["ErrorMessage"] = "This case does not require the Unlawful Detainer disclaimer.";
            return RedirectToAction(nameof(SearchCase));
        }

        var model = new UdAttestationModel
        {
            CourtId = courtId,
            CaseDocketId = caseDocketId,
            CaseCategoryCode = caseCategoryCode,
            ReturnUrl = returnUrl
        };
        return View("UdAttestation", model);
    }

    /// <summary>
    /// Captures the user's Y/N response to the party-attestation question,
    /// writes a permanent audit row, and either (Y) redirects to the
    /// original returnUrl or (N) blocks the user with an error message and
    /// redirects back to SearchCase.
    /// </summary>
    [HttpPost("UdAttestation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UdAttestationPost(UdAttestationModel model, CancellationToken ct)
    {
        if (!UdDisclaimerPolicy.RequiresDisclaimer(model.CourtId, model.CaseCategoryCode))
        {
            TempData["ErrorMessage"] = "This case does not require the Unlawful Detainer disclaimer.";
            return RedirectToAction(nameof(SearchCase));
        }

        var customerId = await GetCustomerIdAsync();
        if (customerId <= 0)
        {
            TempData["ErrorMessage"] = "You must be signed in to attest to a case access.";
            return RedirectToAction(nameof(SearchCase));
        }

        // JTI mandate (UD-2): "The end user's response is required to be
        // captured." — BOTH Y and N responses are persisted. The audit row
        // captures the user, case#, answer, timestamp, and the verbatim
        // disclaimer text shown to them at the moment of attestation.
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var attestation = await _udAttestationService.RecordAsync(new UdAccessAttestation
        {
            CustomerId = customerId,
            CourtId = model.CourtId ?? string.Empty,
            CaseDocketId = model.CaseDocketId ?? string.Empty,
            CaseCategoryCode = model.CaseCategoryCode,
            AttestedAsParty = model.AttestedAsParty,
            AttestedUtc = DateTime.UtcNow,
            DisclaimerTextShown = UdDisclaimerPolicy.LeadInVerbatim + "\n\n" + UdDisclaimerPolicy.DisclaimerVerbatim,
            IpAddress = ipAddress
        }, ct);

        if (!model.AttestedAsParty)
        {
            // JTI mandate (UD-2): "If the user states they are not a party
            // to the case, they should not able to proceed further."
            TempData["ErrorMessage"] =
                "Access denied. California Code of Civil Procedure §1161.2 limits access " +
                "to Unlawful Detainer case records to parties and their attorneys. Your " +
                "attestation indicating you are not a party has been recorded.";
            return RedirectToAction(nameof(SearchCase));
        }

        // Y answer — redirect to the original return URL (or SearchCase as a
        // safe fallback if the returnUrl was missing or invalid).
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        return RedirectToAction(nameof(SearchCase));
    }

    // ─── Filings ─────────────────────────────────────────────────────

    [HttpGet("Filings")]
    public async Task<IActionResult> Filings(CancellationToken ct)
    {
        ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
        return View("Filings", new FilingListModel());
    }

    [HttpPost("Filings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Filings(FilingListModel model, CancellationToken ct)
    {
        ViewBag.Courts = await _courtConfigService.GetAllAsync(ct);
        var result = await _courtFiling.GetFilingListAsync(model, ct);
        return View("Filings", result);
    }

    [HttpGet("FilingStatus")]
    public async Task<IActionResult> FilingStatus(string courtId, string efmReferenceId, CancellationToken ct)
    {
        var status = await _courtFiling.GetFilingStatusAsync(courtId, efmReferenceId, ct);
        if (status == null)
        {
            TempData["ErrorMessage"] = "Filing status not found.";
            return RedirectToAction(nameof(Filings));
        }

        ViewBag.CourtId = courtId;
        return View("FilingStatus", status);
    }

    [HttpGet("ChargedAmount")]
    public async Task<IActionResult> ChargedAmount(string courtId, string efmReferenceId, CancellationToken ct)
    {
        var fees = await _courtFiling.GetChargedAmountAsync(courtId, efmReferenceId, ct);
        if (fees == null)
        {
            TempData["ErrorMessage"] = "Charged amount not found.";
            return RedirectToAction(nameof(Filings));
        }

        ViewBag.CourtId = courtId;
        ViewBag.EfmReferenceId = efmReferenceId;
        return View("ChargedAmount", fees);
    }

    // ─── Drafts (AJAX) ───────────────────────────────────────────────

    [HttpGet("api/drafts-json")]
    public async Task<IActionResult> DraftsJson(int? customerId = null, CancellationToken ct = default)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        // Mirror the firm account pattern from OrderModelFactory
        var firmId = customer.AccountType == AccountType.Firm
            ? customer.Id
            : customer.FirmId;
        var isFirmAccount = firmId.HasValue;
        var isManagedUser = customer.FirmId.HasValue;

        List<EFilingDraft> drafts;

        if (isManagedUser)
        {
            // Managed user — only their own drafts
            drafts = await _draftService.GetByCustomerAsync(customer.Id, ct);
        }
        else if (isFirmAccount)
        {
            // Firm admin — get all firm users' drafts
            var managedUsers = await _customerService.GetAllCustomersAsync(
                customerRoleIds: null, pageIndex: 0, pageSize: 500);
            var allIds = new List<int> { customer.Id };
            allIds.AddRange(managedUsers
                .Where(u => u.FirmId == customer.Id)
                .Select(u => u.Id));

            if (customerId.HasValue && customerId.Value > 0)
                drafts = await _draftService.GetByCustomerAsync(customerId.Value, ct);
            else
                drafts = await _draftService.GetByCustomerIdsAsync(allIds, ct);
        }
        else
        {
            // Individual account
            drafts = await _draftService.GetByCustomerAsync(customer.Id, ct);
        }

        // Enrich with court display names
        var courts = await _courtConfigService.GetAllAsync(ct);
        var courtMap = courts.ToDictionary(c => c.CourtId, c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

        // Enrich with customer names
        var customerIds = drafts.Select(d => d.CustomerId).Distinct().ToList();
        var customerNameMap = new Dictionary<int, string>();
        foreach (var cid in customerIds)
        {
            var c = await _customerService.GetCustomerByIdAsync(cid);
            if (c != null)
            {
                var name = $"{c.FirstName} {c.LastName}".Trim();
                customerNameMap[cid] = string.IsNullOrEmpty(name) ? c.Email ?? $"User #{cid}" : name;
            }
        }

        var result = drafts.Select(d => new
        {
            id = d.Id,
            displayName = d.DisplayName ?? "Untitled",
            courtId = d.CourtId,
            courtName = courtMap.TryGetValue(d.CourtId ?? "", out var cn) ? cn : d.CourtId,
            filingType = d.FilingType,
            customerId = d.CustomerId,
            customerName = customerNameMap.TryGetValue(d.CustomerId, out var cname) ? cname : $"User #{d.CustomerId}",
            createdUtc = d.CreatedUtc.ToString("o"),
            updatedUtc = d.UpdatedUtc.ToString("o")
        }).ToList();

        return Json(result);
    }

    [HttpPost("api/draft/delete-ajax")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteDraftAjax(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var draftId = doc.RootElement.GetProperty("draftId").GetInt32();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var draft = await _draftService.GetByIdAsync(draftId, ct);
        if (draft == null)
            return Json(new { success = false, message = "Draft not found." });

        // Verify ownership (own draft or managed user's draft for firm admin)
        if (draft.CustomerId != customer.Id)
        {
            var isFirmAdmin = customer.AccountType == AccountType.Firm;
            if (!isFirmAdmin)
                return Json(new { success = false, message = "Access denied." });

            // Verify the draft belongs to a managed user of this firm
            var draftOwner = await _customerService.GetCustomerByIdAsync(draft.CustomerId);
            if (draftOwner?.FirmId != customer.Id)
                return Json(new { success = false, message = "Access denied." });
        }

        // Pass the draft's actual owner id so the service layer can double-check (M-7).
        await _courtFiling.DeleteDraftAsync(draftId, draft.CustomerId, ct);
        return Json(new { success = true });
    }

    // ─── AJAX Endpoints (for dynamic wizard steps) ───────────────────

    [HttpGet("api/counties")]
    public async Task<IActionResult> GetCounties(CancellationToken ct)
    {
        var courts = await _courtConfigService.GetAllAsync(ct);
        var counties = courts
            .Where(c => !string.IsNullOrEmpty(c.CountyName))
            .GroupBy(c => c.CountyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                countyName = g.Key,
                courts = g.Select(c => new { courtId = c.CourtId, displayName = c.DisplayName }).ToList()
            })
            .OrderBy(c => c.countyName)
            .ToList();
        return Json(counties);
    }

    [HttpGet("api/courtconfig")]
    public async Task<IActionResult> GetCourtConfig(string courtId, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        // Derive hasCourtLocations from policy (CourtLocationsUrl presence)
        var policy = await _provider.GetPolicyAsync(config, ct);
        var hasCourtLocations = !string.IsNullOrEmpty(policy.CourtLocationsUrl);

        return Json(new
        {
            courtId = config.CourtId,
            displayName = config.DisplayName,
            hasCourtLocations,
            civilCaseTypeCodes = config.CivilCaseTypeCodes,
            extraFlags = config.ExtraFlags,
            // Environment fields consumed by the env-badge JS renderer so the
            // badge stays in sync when the user picks a court after page load.
            environment = config.Environment,
            environmentKind = config.EnvironmentKind.ToString(),
            isStaging = config.IsStaging,
            isProduction = config.IsProduction,
            testFilingMode = config.TestFilingMode.ToString(),
        });
    }

    [HttpGet("api/codelist")]
    public async Task<IActionResult> GetCodeList(string courtId, string codeListType, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        try
        {
            var items = await _provider.GetCodeListAsync(config, codeListType, ct);
            return Json(items);
        }
        catch (InvalidOperationException)
        {
            // Code list type not available in this court's policy — return empty list
            return Json(new List<object>());
        }
    }

    [HttpGet("api/locations")]
    public async Task<IActionResult> GetLocations(string courtId, string? zipCode, string? caseType, string? caseCategory, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        var locations = await _provider.GetCourtLocationsAsync(config, zipCode, caseType, caseCategory, ct);
        return Json(locations);
    }

    [HttpGet("api/documents")]
    public async Task<IActionResult> GetDocuments(string courtId, string? caseType, bool subFiling, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        var docs = await _provider.GetDocumentListAsync(config, caseType, subFiling, ct);
        return Json(docs);
    }

    /// <summary>
    /// Diagnostic endpoint to analyze document list filtering for a specific case type/category.
    /// </summary>
    [HttpGet("api/documents/analyze")]
    public async Task<IActionResult> AnalyzeDocuments(string courtId, string caseTypeCode, string caseCategoryCode, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        // Get all documents for subsequent filing
        var docs = await _provider.GetDocumentListAsync(config, caseTypeCode, subFiling: true, ct);
        
        // Get category to find all related CASE_TYPE codes
        var categories = await _provider.GetCodeListAsync(config, "CASE_CATEGORY", ct);
        var category = categories.FirstOrDefault(c => c.Code == caseTypeCode || c.Code == caseCategoryCode);
        
        var allCodes = new HashSet<string> { caseTypeCode, caseCategoryCode };
        if (category != null)
        {
            foreach (var rel in category.Relationships.Where(r => r.RelatedListName == "CASE_TYPE"))
            {
                allCodes.Add(rel.RelatedCode);
            }
        }

        // Analyze documents
        var analysis = new
        {
            TotalDocuments = docs.Count,
            TargetCodes = allCodes.ToList(),
            
            // By CaseTypes/CaseCategories presence
            WithCaseTypes = docs.Count(d => d.CaseTypes.Count > 0),
            WithCaseCategories = docs.Count(d => d.CaseCategories.Count > 0),
            WithBoth = docs.Count(d => d.CaseTypes.Count > 0 && d.CaseCategories.Count > 0),
            WithNeither = docs.Count(d => d.CaseTypes.Count == 0 && d.CaseCategories.Count == 0),
            
            // Matching target codes
            MatchingCaseTypes = docs.Count(d => d.CaseTypes.Any(t => allCodes.Contains(t))),
            MatchingCaseCategories = docs.Count(d => d.CaseCategories.Any(c => allCodes.Contains(c))),
            MatchingEither = docs.Count(d => 
                d.CaseTypes.Any(t => allCodes.Contains(t)) || 
                d.CaseCategories.Any(c => allCodes.Contains(c))),
            
            // FormGroup analysis for matching docs
            FormGroupAnalysis = docs
                .Where(d => d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c)))
                .GroupBy(d => string.Join("+", d.FormGroups.OrderBy(f => f)))
                .Select(g => new { FormGroups = string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToList(),
            
            // Filtering scenarios
            Scenarios = new
            {
                // Current filter: exclude EFCI_LEAD only, require case match
                ExcludeEfciLeadOnly = docs.Count(d => 
                    (d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))) &&
                    !(d.FormGroups.Contains("EFCI_LEAD") && !d.FormGroups.Contains("EF_LEAD"))),
                
                // Include EF_LEAD only
                EfLeadOnly = docs.Count(d => 
                    (d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))) &&
                    d.FormGroups.Contains("EF_LEAD")),
                
                // Include EF_LEAD + EFNE
                EfLeadPlusEfne = docs.Count(d => 
                    (d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))) &&
                    (d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE"))),
                
                // Include EF_LEAD + EFNE + EFCI
                EfLeadPlusEfnePlusEfci = docs.Count(d => 
                    (d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))) &&
                    (d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE") || d.FormGroups.Contains("EFCI"))),
                
                // Include any with FormGroups (not empty)
                AnyWithFormGroups = docs.Count(d => 
                    (d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))) &&
                    d.FormGroups.Count > 0),
                
                // Include empty FormGroups too
                IncludingEmptyFormGroups = docs.Count(d => 
                    d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c))),
            },
            
            // Sample matching documents
            SampleMatching = docs
                .Where(d => d.CaseTypes.Any(t => allCodes.Contains(t)) || d.CaseCategories.Any(c => allCodes.Contains(c)))
                .Take(20)
                .Select(d => new { d.Code, d.Name, d.CaseTypes, d.CaseCategories, d.FormGroups })
                .ToList()
        };

        return Json(analysis);
    }

    [HttpGet("api/attorney")]
    public async Task<IActionResult> SearchAttorney(string courtId, string searchType, string? barNumber = null,
        string? firstName = null, string? lastName = null, string? firmName = null, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        try
        {
            List<AttorneyInfo> results;
            switch (searchType)
            {
                case "barNumber":
                    if (string.IsNullOrWhiteSpace(barNumber))
                        return Json(new { error = "Bar number is required." });
                    var single = await _provider.LookupAttorneyByBarNumberAsync(config, barNumber, ct);
                    results = single != null ? new List<AttorneyInfo> { single } : new List<AttorneyInfo>();
                    break;
                case "firmName":
                    if (string.IsNullOrWhiteSpace(firmName))
                        return Json(new { error = "Firm name is required." });
                    results = await _provider.SearchAttorneysByFirmAsync(config, firmName, ct);
                    break;
                case "name":
                    if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                        return Json(new { error = "First name and last name are required." });
                    results = await _provider.SearchAttorneysByNameAsync(config, firstName, lastName, ct);
                    break;
                default:
                    return Json(new { error = "Invalid search type." });
            }

            if (results.Count == 0)
                return Json(new { error = "No attorneys found.", results = new List<object>() });

            return Json(new { results });
        }
        catch (Exception ex)
        {
            return Json(new { error = "Attorney search failed: " + ex.Message });
        }
    }

    // ─── AJAX: Subsequent Filing — Case Search + Detail ─────────────

    [HttpGet("api/efiling/search-cases")]
    public async Task<IActionResult> SearchCasesAjax(
        string courtId, string searchMode,
        string? caseDocketId = null, string? firstName = null, string? lastName = null,
        string? organizationName = null, string? caseTitle = null, string? partyName = null,
        string? caseCategoryCode = null,
        CancellationToken ct = default)
    {
        var model = new CaseSearchModel
        {
            CourtId = courtId,
            SearchMode = searchMode,
            CaseDocketId = caseDocketId,
            FirstName = firstName,
            LastName = lastName,
            OrganizationName = organizationName,
            CaseTitle = caseTitle,
            PartyName = partyName,
            CaseCategoryCode = caseCategoryCode,  // Step #57 probe wire-through
        };
        var result = await _courtFiling.SearchCasesAsync(model, ct);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return Json(new { success = false, error = result.ErrorMessage });

        return Json(new
        {
            success = true,
            cases = result.Cases.Select(c => new
            {
                c.CaseDocketId,
                c.CaseTrackingId,
                c.CaseTitle,
                c.CaseTypeCode,
                c.CaseCategoryCode,
                c.LocationCode,
                parties = c.Parties.Select(p => new { p.FirstName, p.LastName, p.OrganizationName, p.RoleCode, p.PrimaryId, p.IsOrganization }),
                complaints = c.Complaints.Select(co => new { co.ComplaintId, co.CaseTitle, co.CaseCategoryCode }),
            })
        });
    }

    [HttpGet("api/efiling/case-detail")]
    public async Task<IActionResult> CaseDetailAjax(string courtId, string caseDocketId, CancellationToken ct)
    {
        var caseInfo = await _courtFiling.GetCaseAsync(courtId, caseDocketId, ct);
        if (caseInfo == null)
            return Json(new { success = false, error = $"Case '{caseDocketId}' not found." });

        // Step #44 — defense-in-depth gate on the AJAX endpoint.
        // Pre-Step-#44 this endpoint returned full UD party + title +
        // complaint data without going through the §1161.2 attestation gate,
        // creating a leak: a client with valid auth could fetch UD data
        // bypassing the disclaimer. Closed by GateUdAccessJsonAsync which
        // returns HTTP 403 + { requiresAttestation: true, attestationUrl }.
        // The only caller (CreateCase.cshtml subsequent-resume AJAX) was
        // updated in parallel to detect requiresAttestation + auto-redirect.
        // ReturnUrl points the post-attestation redirect at SubsequentFiling
        // (the actual SF flow), not back at this JSON endpoint — landing on
        // a JSON blob in the browser would be a confusing UX.
        var postAttestReturnUrl = Url.Action(nameof(SubsequentFiling), new { courtId, caseDocketId })
                                  ?? "/CourtFiling/SearchCase";
        var udGate = await GateUdAccessJsonAsync(courtId, caseDocketId, caseInfo.CaseCategoryCode,
            postAttestReturnUrl, ct);
        if (udGate != null)
            return udGate;

        return Json(new
        {
            success = true,
            caseInfo = new
            {
                caseInfo.CaseDocketId,
                caseInfo.CaseTrackingId,
                caseInfo.CaseTitle,
                caseInfo.CaseTypeCode,
                caseInfo.CaseCategoryCode,
                caseInfo.LocationCode,
                parties = caseInfo.Parties.Select(p => new { p.FirstName, p.LastName, p.OrganizationName, p.RoleCode, p.PrimaryId, p.IsOrganization, p.BarNumber }),
                complaints = caseInfo.Complaints.Select(co => new { co.ComplaintId, co.CaseTitle, co.CaseCategoryCode }),
            }
        });
    }

    [HttpGet("api/efiling/document-metadata")]
    public async Task<IActionResult> GetDocumentMetadata(string courtId, string documentCode, CancellationToken ct)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return NotFound();

        try
        {
            var metadata = await _provider.GetDocumentMetadataAsync(config, documentCode, ct);
            return Json(new { success = true, metadata });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"Failed to get metadata: {ex.Message}" });
        }
    }

    [HttpGet("api/efiling/field-schema")]
    public IActionResult GetFieldSchema(string? classType = null, string? slice = null)
    {
        // T-3a S4: support ?slice=classTypes|tags|policies for rich v2 slice access.
        // Legacy: no params returns the merged legacy FieldSchema; ?classType=X returns
        // a single classType's legacy definition. Both preserved for existing UI callers.
        //
        // T-4-A: the slice endpoints serialize V2 POCOs with PascalCase
        // property names by default (per project-wide Newtonsoft resolver). The T-4
        // shared JS modules consume these slices with camelCase access — matching the
        // JSON source files + the SF view's existing fieldSchema.classTypes convention.
        // Per-call camelCase override below scopes the change to slice endpoints only;
        // legacy paths (`/api/efiling/field-schema` no params + `?classType=`) keep
        // their pre-T-4 serialization untouched for back-compat.
        if (!string.IsNullOrEmpty(slice))
        {
            var sliceKey = slice.Trim().ToLowerInvariant();
            return sliceKey switch
            {
                "classtypes" => JsonCamelCase(new { success = true, slice = "classTypes", data = EFiling.Providers.JTI.Config.JtiFieldSchemaProvider.GetClassTypeSchema() }),
                "tags"       => JsonCamelCase(new { success = true, slice = "tags",       data = EFiling.Providers.JTI.Config.JtiFieldSchemaProvider.GetTagSchema() }),
                "policies"   => JsonCamelCase(new { success = true, slice = "policies",   data = EFiling.Providers.JTI.Config.JtiFieldSchemaProvider.GetCaseCategoryPolicy() }),
                _            => Json(new { success = false, error = $"Unknown slice: {slice}. Valid values: classTypes, tags, policies." })
            };
        }

        if (!string.IsNullOrEmpty(classType))
        {
            var classTypeDef = EFiling.Providers.JTI.Config.JtiFieldSchemaProvider.GetClassType(classType);
            if (classTypeDef == null)
                return Json(new { success = false, error = $"Unknown classType: {classType}" });

            return Json(new { success = true, classType, definition = classTypeDef });
        }

        var schema = EFiling.Providers.JTI.Config.JtiFieldSchemaProvider.GetSchema();
        return Json(new { success = true, schema });
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON with the slice-endpoint conventions
    /// (camelCase properties, verbatim dictionary keys) and returns a raw
    /// <see cref="ContentResult"/>. Delegates to
    /// <see cref="EFiling.Nop.Mapping.SchemaSliceJsonSerializer"/> — see that class for
    /// the full rationale and bug history (Step #13.1 dictionary-key mangling fix).
    ///
    /// <para>
    /// Why <see cref="ContentResult"/> instead of <see cref="JsonResult"/>: nopCommerce's
    /// configured <c>NewtonsoftJsonResultExecutor</c> silently ignores per-call
    /// <see cref="JsonResult.SerializerSettings"/> for the inner POCO graph
    /// (verified empirically 2026-05-17 — outer anonymous-type properties get camelCased
    /// but inner POCO properties stay PascalCase). Direct serialization bypasses the
    /// executor and gives us full control. Scoped to slice endpoints only; legacy paths
    /// remain on the default executor for back-compat.
    /// </para>
    /// </summary>
    private ContentResult JsonCamelCase(object value)
    {
        var json = EFiling.Nop.Mapping.SchemaSliceJsonSerializer.Serialize(value);
        return new ContentResult
        {
            Content = json,
            ContentType = "application/json; charset=utf-8",
            StatusCode = 200
        };
    }

    [HttpPost("api/calculatefees")]
    public async Task<IActionResult> CalculateFees([FromBody] CreateCaseModel model, CancellationToken ct)
    {
        // Server-side validation before hitting the court API
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(model.CourtId)) errors.Add("Court is required.");
        if (string.IsNullOrWhiteSpace(model.CaseTypeCode)) errors.Add("Case type is required.");
        if (string.IsNullOrWhiteSpace(model.CaseCategoryCode)) errors.Add("Case category is required.");
        if (string.IsNullOrWhiteSpace(model.LeadDocumentCode)) errors.Add("Lead document type is required.");
        if (!model.IsSubsequentFiling && string.IsNullOrWhiteSpace(model.LocationCode)) errors.Add("Courthouse/location is required.");
        if (errors.Count > 0)
        {
            return Json(new EFiling.Core.Models.FeeCalculation
            {
                ErrorCode = -1,
                ErrorText = "Validation failed: " + string.Join(" ", errors)
            });
        }

        var config = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
        if (config == null) return NotFound();

        var sub = BuildQuickSubmission(model);

        // DEBUG: trace party data reaching fee calculation
        System.Console.WriteLine($"[FeeCalc DEBUG] PartiesJson = {model.PartiesJson ?? "(null)"}");
        System.Console.WriteLine($"[FeeCalc DEBUG] IsSubsequent={model.IsSubsequentFiling} Parties={sub.Parties.Count} Assocs={sub.PartyDocumentAssociations.Count}");
        if (sub.LeadDocument != null)
            System.Console.WriteLine($"[FeeCalc DEBUG] LeadDoc MetadataValues.Count = {sub.LeadDocument.MetadataValues.Count}");
        foreach (var mv in sub.LeadDocument?.MetadataValues ?? new())
            System.Console.WriteLine($"[FeeCalc DEBUG] Metadata: Code={mv.Code} Class={mv.ClassType} IdRefs=[{string.Join(",", mv.IdReferences)}]");

        var fees = await _provider.CalculateFeesAsync(config, sub, ct);
        return Json(fees);
    }

    // ─── Submit & Pay ──────────────────────────────────────────────────

    /// <summary>
    /// Orchestrates the full submit-and-pay flow:
    /// 1. Load draft → build FilingSubmission
    /// 2. Calculate fees + submit to JTI (nothing charged if JTI fails)
    /// 3. Add "Court Filing Service" product to cart → PlaceOrder via Braintree
    /// 4. Create EFilingOrderRecord + EFilingFeeRecords
    /// 5. Mark draft submitted, delete blobs
    /// </summary>
    [HttpPost("api/submit-and-pay")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitAndPayAjax(CancellationToken ct)
    {
        try
        {
            // ── 1. Parse request ─────────────────────────────────────────
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var body = doc.RootElement;

            var draftId = body.TryGetProperty("draftId", out var did) && did.ValueKind == JsonValueKind.Number
                ? did.GetInt32() : (int?)null;
            var savedPaymentMethodId = body.TryGetProperty("savedPaymentMethodId", out var spm) && spm.ValueKind == JsonValueKind.Number
                ? spm.GetInt32() : (int?)null;

            if (!draftId.HasValue)
                return Json(new { success = false, message = "Draft ID is required." });
            if (!savedPaymentMethodId.HasValue)
                return Json(new { success = false, message = "Payment method is required." });

            // ── 2. Load and validate draft ───────────────────────────────
            var customer = await _workContext.GetCurrentCustomerAsync();
            var draft = await _draftService.GetByIdAsync(draftId.Value, ct);

            if (draft == null || draft.CustomerId != customer.Id)
                return Json(new { success = false, message = "Draft not found." });
            if (draft.IsSubmitted)
                return Json(new { success = false, message = "This draft has already been submitted." });

            // ── 3. Deserialize draft → CreateCaseModel → FilingSubmission ─
            var createModel = JsonSerializer.Deserialize<CreateCaseModel>(
                draft.SubmissionJson, _lenientJsonOptions);

            if (createModel == null || string.IsNullOrWhiteSpace(createModel.CourtId))
                return Json(new { success = false, message = "Invalid draft data." });

            // Draft JSON field names differ from CreateCaseModel properties.
            // Bridge arrays (documents→DocumentsJson) and renamed scalars.
            using (var rawDoc = JsonDocument.Parse(draft.SubmissionJson))
            {
                var root = rawDoc.RootElement;

                // Arrays stored as objects → need re-serialization to JSON strings
                if (string.IsNullOrEmpty(createModel.DocumentsJson)
                    && root.TryGetProperty("documents", out var docsEl)
                    && docsEl.ValueKind == JsonValueKind.Array)
                    createModel.DocumentsJson = docsEl.GetRawText();

                if (string.IsNullOrEmpty(createModel.PartiesJson)
                    && root.TryGetProperty("parties", out var ptEl)
                    && ptEl.ValueKind == JsonValueKind.Array)
                    createModel.PartiesJson = ptEl.GetRawText();

                if (string.IsNullOrEmpty(createModel.AttorneysJson)
                    && root.TryGetProperty("attorneys", out var atEl)
                    && atEl.ValueKind == JsonValueKind.Array)
                    createModel.AttorneysJson = atEl.GetRawText();

                // Scalar field name mismatches: draft name → model property
                if (string.IsNullOrEmpty(createModel.IncidentZipCode)
                    && root.TryGetProperty("zipCode", out var zEl))
                    createModel.IncidentZipCode = zEl.GetString();

                if (!createModel.CaliforniaEnvironmentalQualityAct
                    && root.TryGetProperty("ceqa", out var ceqaEl)
                    && ceqaEl.ValueKind == JsonValueKind.True)
                    createModel.CaliforniaEnvironmentalQualityAct = true;

                if (!createModel.AmountInControversy.HasValue
                    && root.TryGetProperty("demandAmount", out var daEl))
                {
                    var daStr = daEl.ValueKind == JsonValueKind.String ? daEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(daStr) && decimal.TryParse(daStr, out var daVal))
                        createModel.AmountInControversy = daVal;
                }
            }

            var config = await _courtConfigService.GetByCourtIdAsync(createModel.CourtId, ct);
            if (config == null)
                return Json(new { success = false, message = $"Court '{createModel.CourtId}' not configured." });

            // Pass config so court-specific overrides (submitter username, attorney role code)
            // from ExtraFlags are applied to the built submission.
            var submission = CourtFilingController.BuildSubmissionFromCreateModel(createModel, config);

            // F-J1: fail fast on an incomplete submission before the billable JTI round-trip.
            var validationErrors = CourtFilingController.ValidateForSubmission(createModel, submission);
            if (validationErrors.Count > 0)
                return Json(new { success = false, message = "Submission is incomplete: " + string.Join(" ", validationErrors) });

            // ── 4. Calculate fees ────────────────────────────────────────
            var fees = await _provider.CalculateFeesAsync(config, submission, ct);
            if (fees.ErrorCode != 0)
                return Json(new { success = false, message = $"Fee calculation failed: {fees.ErrorText}" });

            // ── 5. Submit to JTI — nothing charged yet ───────────────────
            var filingResult = await _provider.SubmitFilingAsync(config, submission, ct);
            if (!filingResult.Success || filingResult.ErrorCode != 0)
            {
                return Json(new
                {
                    success = false,
                    message = $"Court filing submission failed: {filingResult.ErrorText ?? "Unknown error"}"
                });
            }

            // ── 6-8. Shared finalization (Braintree charge + EFilingOrderRecord/Doc/Fee) ──
            // Extracted into FinalizeFilingAsync (P1, 2026-04-26) so the SF form-post path in
            // CreateCase can reuse the exact same finalization sequence. The helper preserves
            // CC behavior 1:1 except for one explicit invariant: the SetProcessPaymentRequestAsync(null)
            // reset is now wrapped in try/finally (closes F-3 in EFILING_PAYMENT_FINALIZATION_AUDIT.md).
            var store = await _storeContext.GetCurrentStoreAsync();

            // CC-side finalization inputs (from draft JSON):
            //   - notificationEmails: filer email + extra recipients from draft.notificationEmails
            //   - caseCategoryText/caseTypeText: from draft.caseCategoryName/caseTypeName (JS-populated; F-6)
            //   - filingType: from draft.FilingType (F-7 — P2b will switch to submission.FilingType.ToString())
            //   - submissionJson: the raw draft JSON (preserves CC behavior — same as pre-P1)
            //   - caseTitle: BuildDisplayName(createModel) (preserves CC behavior — same as pre-P1)
            var allNotifyEmails = new List<string>();
            if (!string.IsNullOrWhiteSpace(customer.Email))
                allNotifyEmails.Add(customer.Email.Trim());
            using (var rawNotify = JsonDocument.Parse(draft.SubmissionJson))
            {
                if (rawNotify.RootElement.TryGetProperty("notificationEmails", out var neEl)
                    && neEl.ValueKind == JsonValueKind.String)
                {
                    var extra = neEl.GetString() ?? "";
                    foreach (var e in extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!string.IsNullOrWhiteSpace(e) && !allNotifyEmails.Contains(e, StringComparer.OrdinalIgnoreCase))
                            allNotifyEmails.Add(e.Trim());
                }
            }

            string? caseCategoryText = null, caseTypeText = null;
            using (var rawNames = JsonDocument.Parse(draft.SubmissionJson))
            {
                var root = rawNames.RootElement;
                if (root.TryGetProperty("caseCategoryName", out var ccn))
                    caseCategoryText = ccn.GetString();
                if (root.TryGetProperty("caseTypeName", out var ctn))
                    caseTypeText = ctn.GetString();
            }

            var finalize = await _filingFinalizer.FinalizeAsync(
                customer: customer,
                store: store,
                createModel: createModel,
                submission: submission,
                fees: fees,
                filingResult: filingResult,
                savedPaymentMethodId: savedPaymentMethodId.Value,
                filingType: draft.FilingType,
                caseTitle: CourtFilingController.BuildDisplayName(createModel),
                caseCategoryText: caseCategoryText,
                caseTypeText: caseTypeText,
                submissionJson: draft.SubmissionJson,
                notificationEmails: allNotifyEmails,
                ct: ct);

            if (!finalize.Success)
                return Json(new { success = false, message = finalize.ErrorMessage });

            // ── 9. Mark draft submitted + clean up blobs (CC-specific) ───
            // SF doesn't run this code path — CourtFilingController.CreateCaseAsync handles
            // MarkSubmittedAsync internally when DraftId is present (covers both CC form-post
            // and SF form-post). The SF caller in CreateCase does its own blob cleanup.
            await _draftService.MarkSubmittedAsync(draft.Id,
                filingResult.EfmReferenceId ?? submission.EfspReferenceId, ct);

            try
            {
                await _blobService.DeleteDraftFolderAsync(customer.Id, draft.Id.ToString(), ct);
            }
            catch (Exception blobEx)
            {
                // Non-fatal — log and continue
                await _logger.WarningAsync($"Failed to clean up blobs for draft {draft.Id}: {blobEx.Message}");
            }

            // ── 10. Return success ───────────────────────────────────────
            return Json(new
            {
                success = true,
                orderId = finalize.OrderId,
                efmReferenceId = filingResult.EfmReferenceId,
                efspReferenceId = submission.EfspReferenceId,
                message = "Filing submitted and payment processed successfully."
            });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("SubmitAndPayAjax failed", ex);
            return Json(new { success = false, message = $"An unexpected error occurred: {ex.Message}" });
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private async Task<int> GetCustomerIdAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer.Id;
    }

    // P1: Finalization helper extracted to the IFilingFinalizer service.
    // See @c:/Users/sevak/workspace/test/src/EFiling/EFiling.Nop/Services/FilingFinalizer.cs
    // and docs/EFILING_PAYMENT_FINALIZATION_AUDIT.md for rationale.

    private static EFiling.Core.Models.FilingSubmission BuildQuickSubmission(CreateCaseModel model)
    {
        // Reuse the same logic as CourtFilingController.BuildSubmissionFromCreateModel
        // For fee calculation preview. This is a lightweight version.
        var sub = new EFiling.Core.Models.FilingSubmission
        {
            FilingType = model.IsSubsequentFiling
                ? EFiling.Core.Enums.FilingType.Subsequent
                : EFiling.Core.Enums.FilingType.Initial,
            CaseTypeCode = model.CaseTypeCode,
            CaseCategoryCode = model.CaseCategoryCode,
            LocationCode = model.LocationCode,
            IncidentZipCode = model.IncidentZipCode,
            LocationName = model.LocationName,
            ComplexLitigation = model.ComplexLitigation,
            ClassAction = model.ClassAction,
            Asbestos = model.Asbestos,
            CaliforniaEnvironmentalQualityAct = model.CaliforniaEnvironmentalQualityAct,
            // Audit F-1: see CourtFilingController.cs rationale — map bool→bool?
            // so "user didn't check the box" → null (omit) rather than emit false explicitly.
            ConditionallySealed = model.ConditionallySealed ? (bool?)true : null,
            CaseDocketId = model.CaseDocketId,
            CaseTrackingId = model.CaseTrackingId,
            ComplaintId = model.ComplaintId,
        };

        if (!string.IsNullOrEmpty(model.LeadDocumentCode))
        {
            sub.LeadDocument = new EFiling.Core.Models.FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = model.LeadDocumentCode,
                FileControlId = "feecalc-" + Guid.NewGuid().ToString("N")[..8],
                BinaryLocationUri = model.LeadDocumentUrl ?? "https://placeholder.pdf",
                ComplaintRef = model.IsSubsequentFiling ? model.ComplaintId : null,
            };
        }

        // Parse comprehensive metadata from JSON (for subsequent filings)
        if (!string.IsNullOrEmpty(model.MetadataJson) && model.IsSubsequentFiling && sub.LeadDocument != null)
        {
            var metadataItems = System.Text.Json.JsonSerializer.Deserialize<List<MetadataEntryDto>>(
                model.MetadataJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // T-5 consolidation: classType dispatch delegated to the single
            // MetadataValueMapper. Prior to T-5, this site was a lightweight copy of Site 1's
            // switch that silently dropped:
            //   - new-data parties (incomplete caseParticipant branch — no IsNew loop)
            //   - new-data attorneys (audit C-1 root cause — "TODO: Handle new attorneys")
            //   - contact metadata items entirely (audit C-2 — no branch)
            //   - AdditionalInfoTags on everything (audit C-3 — never called BuildAdditionalInfoTags)
            //   - ValueRestriction on simple-value arms (audit D-2)
            // Delegating to the mapper back-fills ALL of these bugs in a single swap, so the
            // fee-calc preview now sees the same metadata shape as the actual submit path.
            foreach (var meta in metadataItems)
            {
                foreach (var mv in MetadataValueMapper.FromDto(meta))
                    sub.LeadDocument.MetadataValues.Add(mv);
            }
        }
        // Legacy: Parse parties from JSON (for initial filings)
        else if (!string.IsNullOrEmpty(model.PartiesJson) && !model.IsSubsequentFiling)
        {
            var partyDtos = System.Text.Json.JsonSerializer.Deserialize<List<PartyEntryDto>>(
                model.PartiesJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            int filedByIdx = 0, filedAsToIdx = 0;
            foreach (var p in partyDtos)
            {
                string refId;
                string assocType;
                if (string.Equals(p.Side, "filing", System.StringComparison.OrdinalIgnoreCase))
                {
                    refId = $"filedBy{filedByIdx++}";
                    assocType = "FILEDBY";
                }
                else
                {
                    refId = $"filedAsTo{filedAsToIdx++}";
                    assocType = "REFERS_TO";
                }

                var fp = new EFiling.Core.Models.FilingParty
                {
                    ReferenceId = refId,
                    PrimaryId = p.PrimaryId,
                    RoleCode = p.PartyType ?? "",
                    IsOrganization = p.IsOrganization,
                    FirstName = p.FirstName,
                    MiddleName = p.MiddleName,
                    LastName = p.LastName,
                    OrganizationName = p.OrganizationName,
                    FirstAppearancePaid = p.FirstAppearancePaid,
                    GovernmentExempt = p.GovernmentExempt,
                    FeeExemptionRequestType = p.FeeExemptionType,
                };
                sub.Parties.Add(fp);

                if (sub.LeadDocument != null)
                {
                    sub.PartyDocumentAssociations.Add(new EFiling.Core.Models.PartyDocumentAssociation
                    {
                        AssociationType = assocType,
                        ParticipantRef = refId,
                        DocumentRef = sub.LeadDocument.ReferenceId,
                    });
                }
            }
        }

        sub.Payment = new EFiling.Core.Models.FilingPayment { PaymentType = "CREDIT" };
        return sub;
    }
}

/// <summary>
/// Handles empty strings as null when deserializing decimal? fields
/// (form inputs send "" for blank numeric fields).
/// </summary>
file class NullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (decimal.TryParse(s, out var v)) return v;
            return null;
        }
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDecimal();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
