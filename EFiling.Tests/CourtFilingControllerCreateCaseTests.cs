using System.Text.Json;
using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using EFiling.Nop.Services;
using Moq;

namespace EFiling.Tests;

/// <summary>
/// Behavioral tests for <see cref="CourtFilingController.CreateCaseAsync"/> — P1.
///
/// <para>
/// Pre-P1 the method returned a 3-tuple <c>(bool, string?, string?)</c>. P1 refactored
/// it to return <see cref="CreateCaseResult"/> so the caller can perform downstream
/// finalization (Braintree charge, EFilingOrderRecord creation) without re-fetching
/// or re-building the submission/fees. These tests pin:
/// </para>
///
/// <list type="bullet">
///   <item>Court not found → Success=false, all richer fields null.</item>
///   <item>Fee calculation error → Success=false, Submission populated, Fees carry the error code, FilingResult null.</item>
///   <item>JTI submit error → Success=false, Submission/Fees/FilingResult all populated (callers can introspect).</item>
///   <item>Happy path → Success=true, all fields populated, EfmReferenceId convenience matches FilingResult.EfmReferenceId.</item>
///   <item>FilingType.Subsequent passed through to the built submission (so the caller's
///         <c>submission.FilingType.ToString()</c> derivation is meaningful).</item>
///   <item>Draft is marked submitted only when DraftId is present AND submission succeeded.</item>
/// </list>
/// </summary>
public class CourtFilingControllerCreateCaseTests
{
    private readonly Mock<IEFilingProvider> _provider = new(MockBehavior.Strict);
    private readonly Mock<ICourtConfigurationService> _configService = new(MockBehavior.Strict);
    private readonly Mock<IEFilingDraftService> _draftService = new(MockBehavior.Strict);

    private CourtFilingController BuildSut() => new(
        _provider.Object,
        _configService.Object,
        _draftService.Object);

    private static CourtConfiguration MaderaConfig() => new()
    {
        CourtId = "madera",
        ProviderType = "JTI",
    };

    private static CreateCaseModel SubsequentModel(int? draftId = null) => new()
    {
        CourtId = "madera",
        IsSubsequentFiling = true,
        CaseDocketId = "MFL018522",
        ComplaintId = "1",
        CaseTitle = "Smith v. Doe",
        LeadDocumentCode = "MOTION_GENERIC",
        LeadDocumentUrl = "https://blob.example.com/motion.pdf",
        DraftId = draftId,
    };

    // ─── 1. Court not found — minimal failure ────────────────────────────

    [Fact]
    public async Task CreateCaseAsync_CourtNotFound_ReturnsFailure_AllRicherFieldsNull()
    {
        _configService.Setup(c => c.GetByCourtIdAsync("unknown-court", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CourtConfiguration?)null);

        var sut = BuildSut();
        var model = SubsequentModel();
        model.CourtId = "unknown-court";

        var result = await sut.CreateCaseAsync(model, customerId: 42);

        Assert.False(result.Success);
        Assert.Contains("unknown-court", result.Error);
        Assert.Null(result.Submission);
        Assert.Null(result.Fees);
        Assert.Null(result.FilingResult);
        Assert.Null(result.Config);
        Assert.Null(result.EfmReferenceId);

        _provider.VerifyNoOtherCalls();
        _draftService.VerifyNoOtherCalls();
    }

    // ─── 2. Fee calc fails — submission built, fees carry error ──────────

    [Fact]
    public async Task CreateCaseAsync_FeeCalculationFails_ReturnsFailure_WithSubmissionAndFeesButNoFilingResult()
    {
        var config = MaderaConfig();
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(config);

        var failedFees = new FeeCalculation { ErrorCode = 1234, ErrorText = "Codelist mismatch" };
        _provider.Setup(p => p.CalculateFeesAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedFees);

        var sut = BuildSut();
        var result = await sut.CreateCaseAsync(SubsequentModel(), customerId: 42);

        Assert.False(result.Success);
        Assert.Contains("Codelist mismatch", result.Error);
        Assert.NotNull(result.Submission);            // built before fee calc, surfaced for caller introspection
        Assert.NotNull(result.Fees);                  // carries the error so caller can log
        Assert.Equal(1234, result.Fees!.ErrorCode);
        Assert.Null(result.FilingResult);             // never reached the submit step
        Assert.NotNull(result.Config);

        _provider.Verify(p => p.SubmitFilingAsync(It.IsAny<CourtConfiguration>(), It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
        _draftService.VerifyNoOtherCalls();
    }

    // ─── 3. JTI submit fails — full data surfaced ────────────────────────

    [Fact]
    public async Task CreateCaseAsync_JtiSubmitFails_ReturnsFailure_WithFilingResultPopulatedForIntrospection()
    {
        var config = MaderaConfig();
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(config);
        _provider.Setup(p => p.CalculateFeesAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 60m });
        _provider.Setup(p => p.SubmitFilingAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult
            {
                Success = false,
                ErrorCode = 4059,
                ErrorText = "Document Definition MetaData not found"
            });

        var sut = BuildSut();
        var result = await sut.CreateCaseAsync(SubsequentModel(), customerId: 42);

        Assert.False(result.Success);
        Assert.Contains("Document Definition MetaData not found", result.Error);
        Assert.NotNull(result.Submission);
        Assert.NotNull(result.Fees);
        Assert.NotNull(result.FilingResult);
        Assert.Equal(4059, result.FilingResult!.ErrorCode);
        // No draft mark-submitted on a failed submit, even if DraftId was present.
        _draftService.Verify(d => d.MarkSubmittedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── 4. Happy path — all fields populated ────────────────────────────

    [Fact]
    public async Task CreateCaseAsync_HappyPath_SubsequentFiling_AllFieldsPopulated()
    {
        var config = MaderaConfig();
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(config);
        _provider.Setup(p => p.CalculateFeesAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 60m });
        _provider.Setup(p => p.SubmitFilingAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult
            {
                Success = true,
                ErrorCode = 0,
                EfmReferenceId = "26MA00000999",
            });

        var sut = BuildSut();
        var result = await sut.CreateCaseAsync(SubsequentModel(), customerId: 42);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Submission);
        Assert.NotNull(result.Fees);
        Assert.NotNull(result.FilingResult);
        Assert.NotNull(result.Config);
        Assert.Equal("26MA00000999", result.EfmReferenceId);
        Assert.Equal("26MA00000999", result.FilingResult!.EfmReferenceId);
        // F-7 invariant: caller derives filingType from submission.FilingType — must be Subsequent here.
        Assert.Equal(FilingType.Subsequent, result.Submission!.FilingType);
    }

    // ─── 5. DraftId present + happy path → MarkSubmittedAsync called ─────

    [Fact]
    public async Task CreateCaseAsync_HappyPathWithDraftId_MarksDraftSubmitted()
    {
        var config = MaderaConfig();
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(config);
        _provider.Setup(p => p.CalculateFeesAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 60m });
        _provider.Setup(p => p.SubmitFilingAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult { Success = true, ErrorCode = 0, EfmReferenceId = "26MA00000999" });
        _draftService.Setup(d => d.MarkSubmittedAsync(77, "26MA00000999", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.CreateCaseAsync(SubsequentModel(draftId: 77), customerId: 42);

        Assert.True(result.Success);
        _draftService.Verify(d => d.MarkSubmittedAsync(77, "26MA00000999", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── 6. Initial filing — FilingType=Initial passed through ───────────

    [Fact]
    public async Task CreateCaseAsync_InitialFiling_SubmissionFilingType_IsInitial()
    {
        // Pins the F-7 invariant from the SF side too: when SF caller code does
        // result.Submission!.FilingType.ToString(), it must produce "Initial" for CC and
        // "Subsequent" for SF. This guards against a future refactor accidentally flipping
        // the source of FilingType in BuildSubmissionFromCreateModel.
        var config = MaderaConfig();
        _configService.Setup(c => c.GetByCourtIdAsync("madera", It.IsAny<CancellationToken>())).ReturnsAsync(config);
        _provider.Setup(p => p.CalculateFeesAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeCalculation { TotalAmount = 435m });
        _provider.Setup(p => p.SubmitFilingAsync(config, It.IsAny<FilingSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilingResult { Success = true, ErrorCode = 0, EfmReferenceId = "26MA00001000" });

        var initialModel = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = false,
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

        var sut = BuildSut();
        var result = await sut.CreateCaseAsync(initialModel, customerId: 42);

        Assert.True(result.Success);
        Assert.Equal(FilingType.Initial, result.Submission!.FilingType);
    }

    // ─── 7. F-B5: DemandAmount → AmountInControversy coalesce ────────────
    // The CreateCase UI captures the claim amount in the "Demand amount" input
    // (CreateCaseModel.DemandAmount). AmountInControversy is the wire/domain field. Pre-fix,
    // BuildSubmissionFromCreateModel only read model.AmountInControversy — null on the legacy
    // form-post path — so the entered amount was silently dropped. These pin the coalesce.

    private static CreateCaseModel InitialModelWithAmounts(decimal? amountInControversy, decimal? demandAmount) => new()
    {
        CourtId = "madera",
        IsSubsequentFiling = false,
        CaseTypeCode = "411110",
        CaseCategoryCode = "402400",
        AmountInControversy = amountInControversy,
        DemandAmount = demandAmount,
    };

    [Fact]
    public void BuildSubmission_DemandAmountOnly_MapsToAmountInControversy()
    {
        var model = InitialModelWithAmounts(amountInControversy: null, demandAmount: 25000m);

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.Equal(25000m, sub.AmountInControversy);
    }

    [Fact]
    public void BuildSubmission_AmountInControversySet_TakesPrecedenceOverDemandAmount()
    {
        var model = InitialModelWithAmounts(amountInControversy: 15000m, demandAmount: 25000m);

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.Equal(15000m, sub.AmountInControversy);
    }

    [Fact]
    public void BuildSubmission_NeitherAmountSet_AmountInControversyNull()
    {
        var model = InitialModelWithAmounts(amountInControversy: null, demandAmount: null);

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.Null(sub.AmountInControversy);
    }

    // ─── 8. F-J1: server-side required-field pre-validation ──────────────
    // ValidateForSubmission fails fast with clear messages before the billable JTI round-trip.
    // Initial filings need case type + category + ≥1 filing party + a lead document; subsequent
    // filings file against an existing case, so only the lead-document check applies.

    [Fact]
    public void ValidateForSubmission_InitialMissingEverything_ReturnsAllErrors()
    {
        var model = new CreateCaseModel { CourtId = "madera", IsSubsequentFiling = false };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        var errors = CourtFilingController.ValidateForSubmission(model, sub);

        Assert.Contains(errors, e => e.Contains("lead document"));
        Assert.Contains(errors, e => e.Contains("Case type"));
        Assert.Contains(errors, e => e.Contains("Case category"));
        Assert.Contains(errors, e => e.Contains("filing party"));
    }

    [Fact]
    public void ValidateForSubmission_CompleteInitial_NoErrors()
    {
        var model = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = false,
            CaseTypeCode = "411110",
            CaseCategoryCode = "402400",
            LeadDocumentCode = "COMPLAINT",
            LeadDocumentUrl = "https://blob.example.com/complaint.pdf",
            PartiesJson = JsonSerializer.Serialize(new[]
            {
                new PartyEntryDto { Side = "filing", PartyType = "PLAIN", FirstName = "Pat", LastName = "Filer" }
            }),
        };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.Empty(CourtFilingController.ValidateForSubmission(model, sub));
    }

    [Fact]
    public void ValidateForSubmission_Subsequent_OnlyLeadDocRequired()
    {
        // SF files against an existing case (CaseDocketId): no case type/category, no new parties.
        var model = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = true,
            CaseDocketId = "MFL018522",
            LeadDocumentCode = "MOTION_GENERIC",
            LeadDocumentUrl = "https://blob.example.com/motion.pdf",
        };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.Empty(CourtFilingController.ValidateForSubmission(model, sub));
    }
}
