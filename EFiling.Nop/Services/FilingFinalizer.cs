using System.Text.Json;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Customers;

namespace EFiling.Nop.Services;

/// <summary>
/// Default implementation of <see cref="IFilingFinalizer"/>. Extracted from the inline
/// finalization block in <c>EFilingMvcController.SubmitAndPayAjax</c> during P1 so the
/// SF form-post path can reuse it without duplication. Behaviorally 1:1 with the pre-P1
/// CC code path except for one explicit invariant: <see cref="IOrderProcessingService.SetProcessPaymentRequestAsync"/>
/// is now reset inside a <c>finally</c> regardless of <c>PlaceOrderAsync</c> outcome
/// (closes finding F-3 in <c>docs/EFILING_PAYMENT_FINALIZATION_AUDIT.md</c>).
/// </summary>
public class FilingFinalizer : IFilingFinalizer
{
    /// <summary>SKU of the "Court Filing Service" product (CustomerEntersPrice = true).</summary>
    private const string CourtFilingProductSku = "COURT-FILING-SVC";

    private readonly IProductService _productService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;
    private readonly IEFilingOrderService _eFilingOrderService;
    private readonly ILogger _logger;

    public FilingFinalizer(
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IGenericAttributeService genericAttributeService,
        IOrderProcessingService orderProcessingService,
        IPaymentService paymentService,
        IOrderService orderService,
        IEFilingOrderService eFilingOrderService,
        ILogger logger)
    {
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _genericAttributeService = genericAttributeService;
        _orderProcessingService = orderProcessingService;
        _paymentService = paymentService;
        _orderService = orderService;
        _eFilingOrderService = eFilingOrderService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FinalizeFilingResult> FinalizeAsync(
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
        CancellationToken ct)
    {
        // ── 6. Add "Court Filing Service" product to cart + place order ─
        var product = await _productService.GetProductBySkuAsync(CourtFilingProductSku);
        if (product == null || product.Deleted || !product.Published)
            return new FinalizeFilingResult(false, 0, 0, "Court Filing Service product is not configured. Please contact support.");

        // CC AUDIT (F-5, P2b/P4): Cart-clear nukes the user's other shopping-cart items.
        var existingCart = await _shoppingCartService.GetShoppingCartAsync(
            customer, ShoppingCartType.ShoppingCart, store.Id);
        foreach (var item in existingCart)
            await _shoppingCartService.DeleteShoppingCartItemAsync(item);

        var addToCartWarnings = await _shoppingCartService.AddToCartAsync(
            customer, product, ShoppingCartType.ShoppingCart, store.Id,
            customerEnteredPrice: fees.TotalAmount, quantity: 1);

        if (addToCartWarnings.Any())
        {
            // CC AUDIT (F-4, P2b/P4): Currently rejects on any warning; classify fatal vs informational.
            await _logger.ErrorAsync($"AddToCart warnings: {string.Join(", ", addToCartWarnings)}");
            return new FinalizeFilingResult(false, 0, 0, $"Failed to prepare order: {string.Join(", ", addToCartWarnings)}");
        }

        // CC AUDIT (F-9, P2b/P4): Mutates customer's persisted SelectedPaymentMethodAttribute.
        await _genericAttributeService.SaveAttributeAsync(customer,
            NopCustomerDefaults.SelectedPaymentMethodAttribute, "Payments.BrainTree", store.Id);

        var processPaymentRequest = new ProcessPaymentRequest
        {
            StoreId = store.Id,
            CustomerId = customer.Id,
            PaymentMethodSystemName = "Payments.BrainTree"
        };
        processPaymentRequest.CustomValues["SavedPaymentMethodId"] = savedPaymentMethodId.ToString();

        PlaceOrderResult placeOrderResult;
        try
        {
            await _orderProcessingService.SetProcessPaymentRequestAsync(processPaymentRequest);
            placeOrderResult = await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);
        }
        finally
        {
            // F-3 invariant: always reset the per-customer ProcessPaymentRequest so it cannot bleed
            // into a subsequent request on the same scope. Closes the throw-path window that
            // pre-P1 CC code left open.
            await _orderProcessingService.SetProcessPaymentRequestAsync(null);
        }

        if (!placeOrderResult.Success)
        {
            var errors = string.Join(", ", placeOrderResult.Errors);
            await _logger.ErrorAsync($"PlaceOrder failed for filing EfspRef={submission.EfspReferenceId}: {errors}");
            return new FinalizeFilingResult(false, 0, 0, errors);
        }

        // CC AUDIT (F-2, P2b/P4): No try/catch around PostProcessPaymentAsync. If Braintree throws
        // here, the order is left Pending with no payment captured; outer catch returns 500.
        var postProcessRequest = new PostProcessPaymentRequest
        {
            Order = placeOrderResult.PlacedOrder
        };
        await _paymentService.PostProcessPaymentAsync(postProcessRequest);

        var orderId = placeOrderResult.PlacedOrder.Id;
        var placedOrder = placeOrderResult.PlacedOrder;
        if (placedOrder.OrderStatus != OrderStatus.Pending)
        {
            placedOrder.OrderStatusId = (int)OrderStatus.Pending;
            await _orderService.UpdateOrderAsync(placedOrder);
        }

        // ── 7. Create EFilingOrderRecord ─────────────────────────────
        var orderRecord = await _eFilingOrderService.CreateOrderRecordAsync(new EFilingOrderRecord
        {
            OrderId = orderId,
            EfspReferenceId = submission.EfspReferenceId,
            EfmReferenceId = filingResult.EfmReferenceId,
            CourtId = createModel.CourtId,
            FilingStatus = "RECEIVED_UNDER_REVIEW",
            FilingType = filingType,
            CaseTitle = caseTitle,
            CaseCategoryText = caseCategoryText,
            CaseTypeText = caseTypeText,
            SubmissionJson = submissionJson,
            NotificationEmails = string.Join(",", notificationEmails),
        }, ct);

        // ── 7b. Create EFilingDocumentRecords for submitted documents ─
        var docRecords = new List<EFilingDocumentRecord>();

        // Parse document entries from createModel.DocumentsJson for original filenames.
        var docEntryMap = new Dictionary<string, DocumentEntryDto>();
        if (!string.IsNullOrEmpty(createModel.DocumentsJson))
        {
            var docEntries = JsonSerializer.Deserialize<List<DocumentEntryDto>>(createModel.DocumentsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            int idx = 0;
            foreach (var de in docEntries)
                docEntryMap[$"doc{idx++}"] = de;
        }

        if (submission.LeadDocument != null)
        {
            var ld = submission.LeadDocument;
            docEntryMap.TryGetValue(ld.ReferenceId, out var leadEntry);
            docRecords.Add(new EFilingDocumentRecord
            {
                EFilingOrderRecordId = orderRecord.Id,
                DocumentReferenceId = ld.ReferenceId,
                DocumentCode = ld.DocumentCode,
                FileControlId = ld.FileControlId,
                IsLeadDocument = true,
                OriginalFileName = leadEntry?.BlobFileName,
                BlobUrl = leadEntry?.BlobUrl,
                DocumentDescription = leadEntry?.DocumentDescription,
                IsCourtGenerated = false,
            });
        }

        foreach (var cd in submission.ConnectedDocuments)
        {
            docEntryMap.TryGetValue(cd.ReferenceId, out var connEntry);
            docRecords.Add(new EFilingDocumentRecord
            {
                EFilingOrderRecordId = orderRecord.Id,
                DocumentReferenceId = cd.ReferenceId,
                DocumentCode = cd.DocumentCode,
                FileControlId = cd.FileControlId,
                IsLeadDocument = false,
                OriginalFileName = connEntry?.BlobFileName,
                BlobUrl = connEntry?.BlobUrl,
                DocumentDescription = connEntry?.DocumentDescription,
                IsCourtGenerated = false,
            });
        }

        if (docRecords.Count > 0)
            await _eFilingOrderService.InsertDocumentRecordsAsync(docRecords, ct);

        // ── 8. Create EFilingFeeRecords (Source=Estimated) ───────────
        if (fees.LineItems.Count > 0)
        {
            var feeRecords = fees.LineItems.Select(li => new EFilingFeeRecord
            {
                EFilingOrderRecordId = orderRecord.Id,
                Source = "Estimated",
                Amount = li.Amount,
                AccountingCostCode = li.AccountingCostCode,
                Description = li.Description,
            }).ToList();

            await _eFilingOrderService.InsertFeeRecordsAsync(feeRecords, ct);
        }

        return new FinalizeFilingResult(true, orderId, orderRecord.Id, null);
    }
}
