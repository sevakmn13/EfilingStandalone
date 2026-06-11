using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using EFiling.Nop.Services;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="FilingFinalizer"/> — P1 (SF order-record creation).
///
/// <para>
/// Covers the contract documented in <c>docs/EFILING_PAYMENT_FINALIZATION_AUDIT.md</c>:
/// <list type="bullet">
///   <item>Happy path: charges payment, creates EFilingOrderRecord with caller-supplied
///         FilingType (F-7 invariant), inserts document and fee child records.</item>
///   <item>Product SKU missing → returns failure WITHOUT touching cart/payment.</item>
///   <item>AddToCart warnings → returns failure WITHOUT placing order.</item>
///   <item>PlaceOrder !Success → returns failure WITHOUT post-processing payment or
///         creating order record.</item>
///   <item>F-3 invariant: <c>SetProcessPaymentRequestAsync(null)</c> is called even
///         when <c>PlaceOrderAsync</c> THROWS (closes the bleed-through window left
///         open by pre-P1 CC code).</item>
///   <item>Document records correctly built from <c>submission.LeadDocument</c> + 
///         <c>submission.ConnectedDocuments</c> with blob metadata from <c>DocumentsJson</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Mocking framework: <see cref="Mock"/> from Moq 4.20.72 (added 2026-04-28; matches
/// existing usage in <see cref="NfrcPollingTaskTests"/>). Mocks are strict for behavioral
/// services (verify no unexpected calls) and loose for the logger (we only assert on
/// log messages where they're contractually meaningful).
/// </para>
/// </summary>
public class FilingFinalizerTests
{
    // ─── Test scaffolding ────────────────────────────────────────────────

    private readonly Mock<IProductService> _productService = new(MockBehavior.Strict);
    private readonly Mock<IShoppingCartService> _shoppingCartService = new(MockBehavior.Strict);
    private readonly Mock<IGenericAttributeService> _genericAttributeService = new(MockBehavior.Strict);
    private readonly Mock<IOrderProcessingService> _orderProcessingService = new(MockBehavior.Strict);
    private readonly Mock<IPaymentService> _paymentService = new(MockBehavior.Strict);
    private readonly Mock<IOrderService> _orderService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingOrderService> _eFilingOrderService = new(MockBehavior.Strict);
    private readonly Mock<ILogger> _logger = new(MockBehavior.Loose);

    private FilingFinalizer BuildSut() => new(
        _productService.Object,
        _shoppingCartService.Object,
        _genericAttributeService.Object,
        _orderProcessingService.Object,
        _paymentService.Object,
        _orderService.Object,
        _eFilingOrderService.Object,
        _logger.Object);

    // ─── Test fixtures ───────────────────────────────────────────────────

    private static Customer BuildCustomer() => new() { Id = 42, Email = "filer@example.com" };
    private static Store BuildStore() => new() { Id = 1 };
    private static Product BuildFilingProduct() => new() { Id = 99, Sku = "COURT-FILING-SVC", Published = true, Deleted = false };

    private static CreateCaseModel BuildCreateModel(bool isSubsequent = true, string? documentsJson = null) => new()
    {
        CourtId = "madera",
        IsSubsequentFiling = isSubsequent,
        CaseDocketId = "MFL018522",
        ComplaintId = isSubsequent ? "1" : null,
        CaseTitle = "Smith v. Doe",
        SelectedPaymentMethodId = 7,
        DocumentsJson = documentsJson,
    };

    private static FilingSubmission BuildSubmission(string efspRef = "EFSP-TEST-001", FilingType type = FilingType.Subsequent)
    {
        var sub = new FilingSubmission
        {
            EfspReferenceId = efspRef,
            FilingType = type,
            CaseDocketId = "MFL018522",
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = "MOTION_GENERIC",
                FileControlId = "fc-doc0",
                BinaryLocationUri = "https://blob.example.com/doc0.pdf",
            },
        };
        return sub;
    }

    private static FeeCalculation BuildFees(decimal total = 60m)
    {
        var fees = new FeeCalculation { TotalAmount = total };
        fees.LineItems.Add(new FeeLineItem
        {
            Amount = total,
            AccountingCostCode = "FILING_FEE",
            Description = "Court filing fee"
        });
        return fees;
    }

    private static FilingResult BuildFilingResult() => new()
    {
        Success = true,
        ErrorCode = 0,
        EfmReferenceId = "26MA00000999",
    };

    /// <summary>
    /// Configure all the mocks to stub a happy-path PlaceOrder + cart flow. Tests can
    /// override individual setups before calling <see cref="BuildSut"/>.
    /// </summary>
    private (Order placedOrder, EFilingOrderRecord createdRecord) StubHappyPath()
    {
        var product = BuildFilingProduct();
        _productService.Setup(p => p.GetProductBySkuAsync("COURT-FILING-SVC")).ReturnsAsync(product);

        _shoppingCartService.Setup(s => s.GetShoppingCartAsync(
            It.IsAny<Customer>(), It.IsAny<ShoppingCartType?>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<ShoppingCartItem>());

        _shoppingCartService.Setup(s => s.AddToCartAsync(
            It.IsAny<Customer>(), It.IsAny<Product>(), It.IsAny<ShoppingCartType>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string>());

        _genericAttributeService.Setup(g => g.SaveAttributeAsync(
            It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _orderProcessingService.Setup(o => o.SetProcessPaymentRequestAsync(
            It.IsAny<ProcessPaymentRequest?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var placedOrder = new Order { Id = 555, OrderStatusId = (int)OrderStatus.Processing };
        _orderProcessingService.Setup(o => o.PlaceOrderAsync(It.IsAny<ProcessPaymentRequest>()))
            .ReturnsAsync(new PlaceOrderResult { PlacedOrder = placedOrder });

        _paymentService.Setup(p => p.PostProcessPaymentAsync(It.IsAny<PostProcessPaymentRequest>()))
            .Returns(Task.CompletedTask);

        _orderService.Setup(o => o.UpdateOrderAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

        var createdRecord = new EFilingOrderRecord { Id = 1234 };
        _eFilingOrderService.Setup(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EFilingOrderRecord r, CancellationToken _) => { r.Id = createdRecord.Id; return r; });

        _eFilingOrderService.Setup(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eFilingOrderService.Setup(s => s.InsertFeeRecordsAsync(It.IsAny<IEnumerable<EFilingFeeRecord>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (placedOrder, createdRecord);
    }

    // ─── 1. Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task FinalizeAsync_HappyPath_ChargesPaymentAndCreatesOrderRecord()
    {
        StubHappyPath();
        EFilingOrderRecord? capturedRecord = null;
        _eFilingOrderService.Setup(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EFilingOrderRecord r, CancellationToken _) => { capturedRecord = r; r.Id = 1234; return r; });

        var sut = BuildSut();
        var result = await sut.FinalizeAsync(
            customer: BuildCustomer(),
            store: BuildStore(),
            createModel: BuildCreateModel(),
            submission: BuildSubmission(),
            fees: BuildFees(60m),
            filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7,
            filingType: "Subsequent",
            caseTitle: "Smith v. Doe",
            caseCategoryText: null,
            caseTypeText: null,
            submissionJson: "{\"courtId\":\"madera\"}",
            notificationEmails: new[] { "filer@example.com" },
            ct: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(555, result.OrderId);
        Assert.Equal(1234, result.OrderRecordId);
        Assert.Null(result.ErrorMessage);

        Assert.NotNull(capturedRecord);
        Assert.Equal(555, capturedRecord!.OrderId);
        Assert.Equal("EFSP-TEST-001", capturedRecord.EfspReferenceId);
        Assert.Equal("26MA00000999", capturedRecord.EfmReferenceId);
        Assert.Equal("madera", capturedRecord.CourtId);
        Assert.Equal("RECEIVED_UNDER_REVIEW", capturedRecord.FilingStatus);
        Assert.Equal("Subsequent", capturedRecord.FilingType);
        Assert.Equal("Smith v. Doe", capturedRecord.CaseTitle);
        Assert.Equal("filer@example.com", capturedRecord.NotificationEmails);
        Assert.Equal("{\"courtId\":\"madera\"}", capturedRecord.SubmissionJson);

        // Verify Braintree was actually invoked (PostProcessPayment is the real charge step).
        _paymentService.Verify(p => p.PostProcessPaymentAsync(It.IsAny<PostProcessPaymentRequest>()), Times.Once);
        // Verify fee/doc records inserted.
        _eFilingOrderService.Verify(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()), Times.Once);
        _eFilingOrderService.Verify(s => s.InsertFeeRecordsAsync(It.IsAny<IEnumerable<EFilingFeeRecord>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── 2. Product missing — short-circuit ──────────────────────────────

    [Fact]
    public async Task FinalizeAsync_ProductSkuNotConfigured_ReturnsFailure_NoCartOrPaymentCalls()
    {
        // Strict-mode mocks: any unset method that gets called throws. We only set up the
        // product lookup to return null, then assert the early-return path didn't touch
        // cart/payment/order services.
        _productService.Setup(p => p.GetProductBySkuAsync("COURT-FILING-SVC")).ReturnsAsync((Product?)null);

        var sut = BuildSut();
        var result = await sut.FinalizeAsync(
            customer: BuildCustomer(),
            store: BuildStore(),
            createModel: BuildCreateModel(),
            submission: BuildSubmission(),
            fees: BuildFees(),
            filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7,
            filingType: "Subsequent",
            caseTitle: "Smith v. Doe",
            caseCategoryText: null,
            caseTypeText: null,
            submissionJson: "{}",
            notificationEmails: Array.Empty<string>(),
            ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.OrderId);
        Assert.Equal(0, result.OrderRecordId);
        Assert.Contains("not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Strict mocks: VerifyNoOtherCalls() asserts nothing else was invoked.
        _shoppingCartService.VerifyNoOtherCalls();
        _orderProcessingService.VerifyNoOtherCalls();
        _paymentService.VerifyNoOtherCalls();
        _eFilingOrderService.VerifyNoOtherCalls();
    }

    // ─── 3. AddToCart warnings — short-circuit before PlaceOrder ─────────

    [Fact]
    public async Task FinalizeAsync_AddToCartReturnsWarnings_ReturnsFailure_NoPlaceOrder()
    {
        _productService.Setup(p => p.GetProductBySkuAsync("COURT-FILING-SVC")).ReturnsAsync(BuildFilingProduct());
        _shoppingCartService.Setup(s => s.GetShoppingCartAsync(
            It.IsAny<Customer>(), It.IsAny<ShoppingCartType?>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<ShoppingCartItem>());
        _shoppingCartService.Setup(s => s.AddToCartAsync(
            It.IsAny<Customer>(), It.IsAny<Product>(), It.IsAny<ShoppingCartType>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string> { "Quantity exceeds stock" });

        var sut = BuildSut();
        var result = await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
            submission: BuildSubmission(), fees: BuildFees(), filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: "Subsequent", caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: Array.Empty<string>(), ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Quantity exceeds stock", result.ErrorMessage);
        // Critical: PlaceOrder NEVER called (would have charged the user).
        _orderProcessingService.Verify(o => o.PlaceOrderAsync(It.IsAny<ProcessPaymentRequest>()), Times.Never);
        _paymentService.Verify(p => p.PostProcessPaymentAsync(It.IsAny<PostProcessPaymentRequest>()), Times.Never);
        _eFilingOrderService.Verify(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── 4. PlaceOrder.Success=false — no payment, no record ─────────────

    [Fact]
    public async Task FinalizeAsync_PlaceOrderReturnsFailure_ReturnsFailure_NoPostProcess_NoOrderRecord()
    {
        StubHappyPath();
        // Override PlaceOrderAsync to return Success=false. F-3 reset still runs in finally
        // (and StubHappyPath already configured SetProcessPaymentRequestAsync to be a no-op).
        _orderProcessingService.Setup(o => o.PlaceOrderAsync(It.IsAny<ProcessPaymentRequest>()))
            .ReturnsAsync(new PlaceOrderResult { Errors = { "Card declined" } });

        var sut = BuildSut();
        var result = await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
            submission: BuildSubmission(), fees: BuildFees(), filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: "Subsequent", caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: Array.Empty<string>(), ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Card declined", result.ErrorMessage);

        _paymentService.Verify(p => p.PostProcessPaymentAsync(It.IsAny<PostProcessPaymentRequest>()), Times.Never);
        _eFilingOrderService.Verify(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()), Times.Never);

        // F-3 invariant: SetProcessPaymentRequestAsync(null) called even on failure path
        // (here PlaceOrderAsync returned !Success, the finally block still resets).
        _orderProcessingService.Verify(o => o.SetProcessPaymentRequestAsync(null, It.IsAny<bool>()), Times.Once);
    }

    // ─── 5. F-3 invariant — exception in PlaceOrderAsync still resets ────

    [Fact]
    public async Task FinalizeAsync_PlaceOrderThrows_F3Invariant_SetProcessPaymentRequestNullStillCalled()
    {
        // This is the explicit behavioral improvement P1 introduces over pre-P1 CC code.
        // Pre-P1: SetProcessPaymentRequestAsync(null) was outside try/finally — if PlaceOrderAsync
        // threw, the per-customer ProcessPaymentRequest could bleed into the next request.
        // Post-P1: reset always runs in finally.
        _productService.Setup(p => p.GetProductBySkuAsync("COURT-FILING-SVC")).ReturnsAsync(BuildFilingProduct());
        _shoppingCartService.Setup(s => s.GetShoppingCartAsync(
            It.IsAny<Customer>(), It.IsAny<ShoppingCartType?>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<ShoppingCartItem>());
        _shoppingCartService.Setup(s => s.AddToCartAsync(
            It.IsAny<Customer>(), It.IsAny<Product>(), It.IsAny<ShoppingCartType>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string>());
        _genericAttributeService.Setup(g => g.SaveAttributeAsync(
            It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _orderProcessingService.Setup(o => o.SetProcessPaymentRequestAsync(
            It.IsAny<ProcessPaymentRequest?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        // PlaceOrderAsync throws — should NOT swallow the exception, but finally must run.
        var injectedException = new InvalidOperationException("Braintree gateway timeout");
        _orderProcessingService.Setup(o => o.PlaceOrderAsync(It.IsAny<ProcessPaymentRequest>()))
            .ThrowsAsync(injectedException);

        var sut = BuildSut();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.FinalizeAsync(
                customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
                submission: BuildSubmission(), fees: BuildFees(), filingResult: BuildFilingResult(),
                savedPaymentMethodId: 7, filingType: "Subsequent", caseTitle: "x",
                caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
                notificationEmails: Array.Empty<string>(), ct: CancellationToken.None));

        Assert.Same(injectedException, thrown);

        // Both calls must have been made: one to SET the payment request (before PlaceOrder),
        // one to RESET to null (in finally, even though PlaceOrder threw).
        _orderProcessingService.Verify(o => o.SetProcessPaymentRequestAsync(
            It.Is<ProcessPaymentRequest?>(r => r != null), It.IsAny<bool>()), Times.Once);
        _orderProcessingService.Verify(o => o.SetProcessPaymentRequestAsync(
            null, It.IsAny<bool>()), Times.Once);
    }

    // ─── 6. F-7 invariant — FilingType passed through verbatim ───────────

    [Theory]
    [InlineData("Initial")]
    [InlineData("Subsequent")]
    [InlineData("CustomFutureValue")]
    public async Task FinalizeAsync_FilingType_PassedThroughToOrderRecord_F7Invariant(string filingTypeIn)
    {
        // F-7: the helper takes filingType as an explicit parameter and writes it to
        // EFilingOrderRecord.FilingType verbatim. Caller controls; no derivation inside
        // the helper. CC currently passes draft.FilingType; SF passes
        // submission.FilingType.ToString(); P2b will unify both on the latter.
        StubHappyPath();
        EFilingOrderRecord? captured = null;
        _eFilingOrderService.Setup(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EFilingOrderRecord r, CancellationToken _) => { captured = r; r.Id = 1; return r; });

        var sut = BuildSut();
        await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
            submission: BuildSubmission(), fees: BuildFees(), filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: filingTypeIn, caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: Array.Empty<string>(), ct: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(filingTypeIn, captured!.FilingType);
    }

    // ─── 7. Document records — lead + connected from submission ──────────

    [Fact]
    public async Task FinalizeAsync_DocumentRecords_BuiltFromLeadAndConnected_WithBlobMetadata()
    {
        StubHappyPath();
        var capturedDocRecords = new List<EFilingDocumentRecord>();
        _eFilingOrderService.Setup(s => s.InsertDocumentRecordsAsync(It.IsAny<IEnumerable<EFilingDocumentRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EFilingDocumentRecord>, CancellationToken>((recs, _) => capturedDocRecords.AddRange(recs))
            .Returns(Task.CompletedTask);

        var submission = BuildSubmission();
        submission.ConnectedDocuments.Add(new FilingDocument
        {
            ReferenceId = "doc1",
            DocumentCode = "DECLARATION",
            FileControlId = "fc-doc1",
            BinaryLocationUri = "https://blob.example.com/doc1.pdf",
        });

        // DocumentsJson encodes blob metadata; entries are matched by index → "doc{idx}".
        var documentsJson = """
            [
              {"BlobFileName":"motion.pdf","BlobUrl":"https://blob/motion.pdf","DocumentDescription":"Motion to compel"},
              {"BlobFileName":"declaration.pdf","BlobUrl":"https://blob/declaration.pdf","DocumentDescription":"Smith declaration"}
            ]
            """;

        var sut = BuildSut();
        await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(),
            createModel: BuildCreateModel(documentsJson: documentsJson),
            submission: submission, fees: BuildFees(), filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: "Subsequent", caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: Array.Empty<string>(), ct: CancellationToken.None);

        Assert.Equal(2, capturedDocRecords.Count);

        var lead = capturedDocRecords.Single(d => d.IsLeadDocument);
        Assert.Equal("doc0", lead.DocumentReferenceId);
        Assert.Equal("MOTION_GENERIC", lead.DocumentCode);
        Assert.Equal("motion.pdf", lead.OriginalFileName);
        Assert.Equal("https://blob/motion.pdf", lead.BlobUrl);
        Assert.Equal("Motion to compel", lead.DocumentDescription);
        Assert.False(lead.IsCourtGenerated);

        var connected = capturedDocRecords.Single(d => !d.IsLeadDocument);
        Assert.Equal("doc1", connected.DocumentReferenceId);
        Assert.Equal("DECLARATION", connected.DocumentCode);
        Assert.Equal("declaration.pdf", connected.OriginalFileName);
        Assert.Equal("https://blob/declaration.pdf", connected.BlobUrl);
        Assert.Equal("Smith declaration", connected.DocumentDescription);
    }

    // ─── 8. Fee records — Source=Estimated, mapped from FeeCalculation ───

    [Fact]
    public async Task FinalizeAsync_FeeRecords_BuiltFromFeeCalculation_SourceIsEstimated()
    {
        StubHappyPath();
        var capturedFeeRecords = new List<EFilingFeeRecord>();
        _eFilingOrderService.Setup(s => s.InsertFeeRecordsAsync(It.IsAny<IEnumerable<EFilingFeeRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EFilingFeeRecord>, CancellationToken>((recs, _) => capturedFeeRecords.AddRange(recs))
            .Returns(Task.CompletedTask);

        var fees = new FeeCalculation { TotalAmount = 470m };
        fees.LineItems.Add(new FeeLineItem { Amount = 435m, AccountingCostCode = "FILING_FEE", Description = "First paper filing fee" });
        fees.LineItems.Add(new FeeLineItem { Amount = 35m, AccountingCostCode = "EFILING_FEE", Description = "EFiling service fee" });

        var sut = BuildSut();
        await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
            submission: BuildSubmission(), fees: fees, filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: "Initial", caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: Array.Empty<string>(), ct: CancellationToken.None);

        Assert.Equal(2, capturedFeeRecords.Count);
        Assert.All(capturedFeeRecords, fr => Assert.Equal("Estimated", fr.Source));
        Assert.Contains(capturedFeeRecords, fr => fr.Amount == 435m && fr.AccountingCostCode == "FILING_FEE");
        Assert.Contains(capturedFeeRecords, fr => fr.Amount == 35m && fr.AccountingCostCode == "EFILING_FEE");
    }

    // ─── 9. NotificationEmails join — comma-separated persistence ────────

    [Fact]
    public async Task FinalizeAsync_NotificationEmails_JoinedAsCommaSeparated()
    {
        StubHappyPath();
        EFilingOrderRecord? captured = null;
        _eFilingOrderService.Setup(s => s.CreateOrderRecordAsync(It.IsAny<EFilingOrderRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EFilingOrderRecord r, CancellationToken _) => { captured = r; r.Id = 1; return r; });

        var sut = BuildSut();
        await sut.FinalizeAsync(
            customer: BuildCustomer(), store: BuildStore(), createModel: BuildCreateModel(),
            submission: BuildSubmission(), fees: BuildFees(), filingResult: BuildFilingResult(),
            savedPaymentMethodId: 7, filingType: "Subsequent", caseTitle: "x",
            caseCategoryText: null, caseTypeText: null, submissionJson: "{}",
            notificationEmails: new[] { "filer@example.com", "paralegal@example.com", "client@example.com" },
            ct: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("filer@example.com,paralegal@example.com,client@example.com", captured!.NotificationEmails);
    }
}
