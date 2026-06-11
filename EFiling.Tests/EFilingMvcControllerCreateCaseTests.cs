using System.Text.Json;
using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using EFiling.Nop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Stores;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="EFilingMvcController.CreateCase"/> POST handler — P1.
///
/// <para>
/// These tests pin the <b>routing logic</b> introduced by P1 (SF branch + payment-method
/// gate). The finalization sequence itself is unit-tested in
/// <see cref="FilingFinalizerTests"/>; here we only verify that the controller:
/// </para>
///
/// <list type="bullet">
///   <item>Gates SF on <c>SelectedPaymentMethodId</c> being present (rejects BEFORE
///         submitting to JTI to avoid orphan filings — see audit F-1).</item>
///   <item>Calls <c>IFilingFinalizer.FinalizeAsync</c> with the correct SF-derived inputs
///         (caseTitle fallback, notification emails = filer email, filingType = submission's
///         enum.ToString(), etc.).</item>
///   <item>Redirects to <c>FilingStatus</c> on SF success and back to <c>SubsequentFiling</c>
///         (with error TempData) on finalization failure.</item>
///   <item>Does NOT call FilingFinalizer for the legacy CC form-post fallback path,
///         preserving pre-P1 behavior for that path.</item>
/// </list>
/// </summary>
public class EFilingMvcControllerCreateCaseTests
{
    // ─── Mocks for CourtFilingController (concrete class instantiated with its 3 deps) ──
    private readonly Mock<IEFilingProvider> _provider = new(MockBehavior.Strict);
    private readonly Mock<ICourtConfigurationService> _courtConfigService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingDraftService> _draftService = new(MockBehavior.Strict);

    // ─── Mocks for EFilingMvcController's other deps ────────────────────
    private readonly Mock<IServiceFeeService> _serviceFeeService = new(MockBehavior.Loose);
    private readonly Mock<IStoreContext> _storeContext = new(MockBehavior.Strict);
    private readonly Mock<IEFilingBlobService> _blobService = new(MockBehavior.Strict);
    private readonly Mock<IWorkContext> _workContext = new(MockBehavior.Strict);
    private readonly Mock<IEFilingOrderService> _eFilingOrderService = new(MockBehavior.Strict);
    private readonly Mock<ICustomerService> _customerService = new(MockBehavior.Strict);
    private readonly Mock<IFilingFinalizer> _filingFinalizer = new(MockBehavior.Strict);
    private readonly Mock<ILogger> _logger = new(MockBehavior.Loose);
    // Step #43 — UD attestation service. Loose mock since
    // CreateCase POST never traverses the UD gate (the gate fires only on
    // GET SubsequentFiling / GET CaseDetail). Default loose-mock behavior
    // returns false from HasValidAttestationAsync, which is irrelevant on
    // this path.
    private readonly Mock<IUdAccessAttestationService> _udAttestationService = new(MockBehavior.Loose);

    private static readonly Customer TestCustomer = new() { Id = 42, Email = "filer@example.com" };
    private static readonly Store TestStore = new() { Id = 1 };
    private static readonly CourtConfiguration MaderaConfig = new() { CourtId = "madera", ProviderType = "JTI" };

    private EFilingMvcController BuildSut()
    {
        var courtFiling = new CourtFilingController(_provider.Object, _courtConfigService.Object, _draftService.Object);

        var sut = new EFilingMvcController(
            courtFiling: courtFiling,
            courtConfigService: _courtConfigService.Object,
            provider: _provider.Object,
            serviceFeeService: _serviceFeeService.Object,
            storeContext: _storeContext.Object,
            blobService: _blobService.Object,
            draftService: _draftService.Object,
            workContext: _workContext.Object,
            eFilingOrderService: _eFilingOrderService.Object,
            customerService: _customerService.Object,
            filingFinalizer: _filingFinalizer.Object,
            logger: _logger.Object,
            udAttestationService: _udAttestationService.Object);

        // Wire HttpContext + TempData so RedirectToAction and TempData["ErrorMessage"] work.
        var httpContext = new DefaultHttpContext();
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
        };
        sut.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return sut;
    }

    private static CreateCaseModel SubsequentModel(int? paymentMethodId = 7) => new()
    {
        CourtId = "madera",
        IsSubsequentFiling = true,
        CaseDocketId = "MFL018522",
        ComplaintId = "1",
        CaseTitle = "Smith v. Doe",
        LeadDocumentCode = "MOTION_GENERIC",
        LeadDocumentUrl = "https://blob.example.com/motion.pdf",
        SelectedPaymentMethodId = paymentMethodId,
    };

    /// <summary>
    /// Configure provider/config mocks for a successful JTI submit so the SF code path
    /// reaches the finalization step.
    /// </summary>
    private void StubSuccessfulJtiSubmit(string efmRef = "26MA00000999")
    {
        _courtConfigService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(MaderaConfig);
        _provider.Setup(p => p.CalculateFeesAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 60m });
        _provider.Setup(p => p.SubmitFilingAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult { Success = true, ErrorCode = 0, EfmReferenceId = efmRef });
        _workContext.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(TestCustomer);
        _storeContext.Setup(s => s.GetCurrentStoreAsync()).ReturnsAsync(TestStore);
    }

    // ─── 1. SF gate — missing payment method short-circuits BEFORE JTI ───

    [Fact]
    public async Task CreateCase_Subsequent_NoPaymentMethod_RedirectsBack_DoesNotCallJti()
    {
        // F-1 protective gate: with no SelectedPaymentMethodId we must NOT submit to JTI
        // (which would create an orphan filing with no payment record). Strict mocks on
        // _provider verify nothing was called.
        _workContext.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(TestCustomer);

        var sut = BuildSut();
        var model = SubsequentModel(paymentMethodId: null);

        var result = await sut.CreateCase(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SubsequentFiling", redirect.ActionName);
        Assert.Equal("madera", redirect.RouteValues!["courtId"]);
        Assert.Equal("MFL018522", redirect.RouteValues["caseDocketId"]);
        Assert.Contains("payment method", (sut.TempData["ErrorMessage"] as string)!, StringComparison.OrdinalIgnoreCase);

        // Critical invariant: NO JTI calls.
        _provider.VerifyNoOtherCalls();
        _filingFinalizer.VerifyNoOtherCalls();
    }

    // ─── 2. SF happy path — finalizer called with correct inputs ──────────

    [Fact]
    public async Task CreateCase_Subsequent_HappyPath_CallsFinalizeAsyncWithCorrectInputs_RedirectsToFilingStatus()
    {
        StubSuccessfulJtiSubmit();

        // Capture the inputs FilingFinalizer.FinalizeAsync receives. This is the heart of P1:
        // the SF branch in CreateCase must derive the right inputs from the model + customer.
        FilingFinalizerCapturedArgs? captured = null;
        _filingFinalizer.Setup(f => f.FinalizeAsync(
                It.IsAny<Customer>(), It.IsAny<Store>(), It.IsAny<CreateCaseModel>(),
                It.IsAny<FilingSubmission>(), It.IsAny<FeeCalculation>(), It.IsAny<FilingResult>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer cust, Store st, CreateCaseModel m, FilingSubmission sub, FeeCalculation fees,
                FilingResult fr, int pmId, string ft, string ct, string? cct, string? ctt, string sj,
                IReadOnlyList<string> ne, CancellationToken _) =>
            {
                captured = new FilingFinalizerCapturedArgs
                {
                    Customer = cust, Store = st, Model = m, Submission = sub, Fees = fees, FilingResult = fr,
                    PaymentMethodId = pmId, FilingType = ft, CaseTitle = ct,
                    CaseCategoryText = cct, CaseTypeText = ctt, SubmissionJson = sj, NotifyEmails = ne,
                };
                return new FinalizeFilingResult(true, OrderId: 555, OrderRecordId: 1234, ErrorMessage: null);
            });

        var sut = BuildSut();
        var model = SubsequentModel(paymentMethodId: 7);

        var result = await sut.CreateCase(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("FilingStatus", redirect.ActionName);
        Assert.Equal("madera", redirect.RouteValues!["courtId"]);
        Assert.Equal("26MA00000999", redirect.RouteValues["efmReferenceId"]);
        Assert.Contains("submitted and payment processed", (sut.TempData["SuccessMessage"] as string)!, StringComparison.OrdinalIgnoreCase);

        // SF input contract — pin the wiring documented in P1 plan.
        Assert.NotNull(captured);
        Assert.Equal(42, captured!.Customer.Id);
        Assert.Equal(7, captured.PaymentMethodId);
        Assert.Equal("Subsequent", captured.FilingType);                         // submission.FilingType.ToString()
        Assert.Equal("Smith v. Doe", captured.CaseTitle);                        // model.CaseTitle (preferred when set)
        Assert.Null(captured.CaseCategoryText);                                  // F-6: SF leaves this null until codelist lookup is added
        Assert.Null(captured.CaseTypeText);
        Assert.Single(captured.NotifyEmails);
        Assert.Equal("filer@example.com", captured.NotifyEmails[0]);             // SF: just filer email today
        Assert.Equal(FilingType.Subsequent, captured.Submission.FilingType);
        Assert.Equal("26MA00000999", captured.FilingResult.EfmReferenceId);
        // SubmissionJson is the re-serialized model — must contain CourtId.
        Assert.Contains("\"madera\"", captured.SubmissionJson, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 3. SF — caseTitle falls back to BuildDisplayName when model.CaseTitle blank ─

    [Fact]
    public async Task CreateCase_Subsequent_BlankCaseTitle_FallsBackToBuildDisplayName()
    {
        StubSuccessfulJtiSubmit();
        string? capturedCaseTitle = null;
        _filingFinalizer.Setup(f => f.FinalizeAsync(
                It.IsAny<Customer>(), It.IsAny<Store>(), It.IsAny<CreateCaseModel>(),
                It.IsAny<FilingSubmission>(), It.IsAny<FeeCalculation>(), It.IsAny<FilingResult>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer _, Store __, CreateCaseModel ___, FilingSubmission ____, FeeCalculation _____,
                FilingResult ______, int _______, string ________, string caseTitle, string? _________,
                string? __________, string ___________, IReadOnlyList<string> ____________, CancellationToken _____________) =>
            {
                capturedCaseTitle = caseTitle;
                return new FinalizeFilingResult(true, 555, 1234, null);
            });

        var sut = BuildSut();
        var model = SubsequentModel();
        model.CaseTitle = null;
        // BuildDisplayName uses PartiesJson; without it returns "Unknown v. Unknown".
        // SF flow generally doesn't post PartiesJson, so the fallback hits the "Unknown" sentinel.
        // This test pins THAT specific behavior so a future change (e.g., codelist lookup) is intentional.

        await sut.CreateCase(model, CancellationToken.None);

        Assert.Equal("Unknown v. Unknown", capturedCaseTitle);
    }

    // ─── 4. SF — finalize fails → redirect back with error TempData ──────

    [Fact]
    public async Task CreateCase_Subsequent_FinalizeFails_RedirectsToSubsequentFiling_WithErrorMessage()
    {
        StubSuccessfulJtiSubmit();
        _filingFinalizer.Setup(f => f.FinalizeAsync(
                It.IsAny<Customer>(), It.IsAny<Store>(), It.IsAny<CreateCaseModel>(),
                It.IsAny<FilingSubmission>(), It.IsAny<FeeCalculation>(), It.IsAny<FilingResult>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinalizeFilingResult(false, 0, 0, "Card declined"));

        var sut = BuildSut();
        var model = SubsequentModel();

        var result = await sut.CreateCase(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SubsequentFiling", redirect.ActionName);
        var tempDataMsg = sut.TempData["ErrorMessage"] as string;
        Assert.NotNull(tempDataMsg);
        // Contract: error message must surface the reason AND the EFM ref so support can investigate.
        // F-1 (audit): JTI succeeded, finalize failed → orphan filing on JTI side. The user-facing
        // message must NOT silently swallow this — they need to know to contact support.
        Assert.Contains("Card declined", tempDataMsg);
        Assert.Contains("26MA00000999", tempDataMsg);
        Assert.Contains("contact support", tempDataMsg, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 5. SF — JTI submit fails → no finalize call ─────────────────────

    [Fact]
    public async Task CreateCase_Subsequent_JtiSubmitFails_NoFinalizeCall_RedirectsBack()
    {
        _workContext.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(TestCustomer);
        _courtConfigService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(MaderaConfig);
        _provider.Setup(p => p.CalculateFeesAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 60m });
        _provider.Setup(p => p.SubmitFilingAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult { Success = false, ErrorCode = 4059, ErrorText = "Document Definition MetaData not found" });

        var sut = BuildSut();
        var result = await sut.CreateCase(SubsequentModel(), CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SubsequentFiling", redirect.ActionName);
        Assert.Contains("Document Definition MetaData not found", (sut.TempData["ErrorMessage"] as string)!);

        _filingFinalizer.VerifyNoOtherCalls();   // never called when JTI failed
    }

    // ─── 6. CC fallback path — finalizer NOT called (preserves pre-P1) ───

    [Fact]
    public async Task CreateCase_Initial_FormPost_DoesNotCallFinalizeAsync_RedirectsToFilingStatus()
    {
        // Regression: the legacy CC form-post fallback path is unchanged from pre-P1.
        // It does NOT invoke FilingFinalizer (CC users normally hit /api/submit-and-pay AJAX,
        // which does call the finalizer). This pin prevents accidentally adding the SF
        // payment-finalize sequence to the CC form-post path before P2b is ready.
        _workContext.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(TestCustomer);
        _courtConfigService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(MaderaConfig);
        _provider.Setup(p => p.CalculateFeesAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 435m });
        _provider.Setup(p => p.SubmitFilingAsync(MaderaConfig, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult { Success = true, ErrorCode = 0, EfmReferenceId = "26MA00001000" });

        var sut = BuildSut();
        var model = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = false,            // CC, not SF
            CaseTypeCode = "FAM",
            CaseCategoryCode = "FAMSUP",
            LeadDocumentCode = "PETITION",
            LeadDocumentUrl = "https://blob.example.com/petition.pdf",
            // F-J1: an initial filing requires at least one filing party to pass pre-validation.
            PartiesJson = JsonSerializer.Serialize(new[]
            {
                new PartyEntryDto { Side = "filing", PartyType = "PET", FirstName = "Pat", LastName = "Filer" }
            }),
        };

        var result = await sut.CreateCase(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("FilingStatus", redirect.ActionName);
        _filingFinalizer.VerifyNoOtherCalls();   // CC form-post never invokes the finalizer
    }

    // ─── Helper record to capture finalizer args in test 2 ───────────────

    private sealed class FilingFinalizerCapturedArgs
    {
        public Customer Customer { get; set; } = default!;
        public Store Store { get; set; } = default!;
        public CreateCaseModel Model { get; set; } = default!;
        public FilingSubmission Submission { get; set; } = default!;
        public FeeCalculation Fees { get; set; } = default!;
        public FilingResult FilingResult { get; set; } = default!;
        public int PaymentMethodId { get; set; }
        public string FilingType { get; set; } = "";
        public string CaseTitle { get; set; } = "";
        public string? CaseCategoryText { get; set; }
        public string? CaseTypeText { get; set; }
        public string SubmissionJson { get; set; } = "";
        public IReadOnlyList<string> NotifyEmails { get; set; } = Array.Empty<string>();
    }
}
