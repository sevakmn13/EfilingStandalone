using System.Text.Json;
using EFiling.Core.Enums;
using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.Mapping;
using EFiling.Nop.Models;
using EFiling.Nop.Services;

namespace EFiling.Nop.Controllers;

/// <summary>
/// Result of <see cref="CourtFilingController.CreateCaseAsync"/>: the JTI submission outcome
/// plus the inputs needed by the caller to perform downstream finalization
/// (Braintree charge, EFilingOrderRecord creation). Surfacing <see cref="Submission"/>,
/// <see cref="Fees"/>, <see cref="FilingResult"/>, and <see cref="Config"/> avoids a
/// second round-trip to <c>CalculateFees</c> in the caller — these are the exact values
/// computed during the submission and are the canonical inputs for downstream steps.
///
/// Refactored from a 3-tuple in P1 (SF order-record creation) so the SF form-post path
/// in <c>EFilingMvcController.CreateCase</c> can perform the same finalization as the
/// CC AJAX path in <c>EFilingMvcController.SubmitAndPayAjax</c> via the shared
/// <c>FinalizeFilingAsync</c> helper. Per <c>EFILING_PAYMENT_FINALIZATION_AUDIT.md</c>
/// invariant #2, both call sites pass the same finalization inputs.
/// </summary>
public sealed record CreateCaseResult(
    bool Success,
    string? Error,
    FilingSubmission? Submission,
    FeeCalculation? Fees,
    FilingResult? FilingResult,
    CourtConfiguration? Config)
{
    /// <summary>Convenience: EFM reference id from the provider response, or null if submission failed before that step.</summary>
    public string? EfmReferenceId => FilingResult?.EfmReferenceId;
}

/// <summary>
/// Controller logic for court e-filing operations.
/// Not an MVC controller itself — this is a service layer that nopCommerce controllers delegate to.
/// In nopCommerce, create a thin MVC controller that injects this and calls these methods.
/// </summary>
public class CourtFilingController
{
    private readonly IEFilingProvider _provider;
    private readonly ICourtConfigurationService _courtConfigService;
    private readonly IEFilingDraftService _draftService;

    // Track D.post fix L-4: Shared JSON options with case-insensitive read for round-trip safety.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // ── Protocol-level defaults (overridable per court via CourtConfiguration.ExtraFlags) ─
    /// <summary>Fallback attorney role code when the court does not override.</summary>
    private const string DefaultAttorneyRoleCode = "ATT";
    /// <summary>Fallback EFSP submitter username when the court does not override.</summary>
    private const string DefaultSubmitterUsername = "legalhub";
    /// <summary>ExtraFlags key for the per-court attorney role code override.</summary>
    internal const string ExtraFlagKey_AttorneyRoleCode = "attorneyRoleCode";
    /// <summary>ExtraFlags key for the per-court submitter/EFSP username override.</summary>
    internal const string ExtraFlagKey_SubmitterUsername = "submitterUsername";

    /// <summary>Resolves the submitter username, preferring the per-court override if set.</summary>
    internal static string ResolveSubmitterUsername(CourtConfiguration? config) =>
        config != null && config.ExtraFlags != null && config.ExtraFlags.TryGetValue(ExtraFlagKey_SubmitterUsername, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : DefaultSubmitterUsername;

    /// <summary>Resolves the attorney role code, preferring the per-court override if set.</summary>
    internal static string ResolveAttorneyRoleCode(CourtConfiguration? config) =>
        config != null && config.ExtraFlags != null && config.ExtraFlags.TryGetValue(ExtraFlagKey_AttorneyRoleCode, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : DefaultAttorneyRoleCode;

    // ── Input validation helpers (M-4 + M-6 + M-2) ────────────────────

    /// <summary>Size limit for a single JSON field posted from the UI (1 MB).</summary>
    internal const int MaxJsonFieldChars = 1_000_000;

    private static readonly JsonSerializerOptions LenientJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Safely deserialize a list of T from a JSON string sent by the UI.
    /// Track D.post fix M-4: wraps JsonException with a field-identifying ArgumentException.
    /// Track D.post fix M-6: rejects payloads exceeding <see cref="MaxJsonFieldChars"/>.
    /// Returns empty list when <paramref name="json"/> is null/empty.
    /// </summary>
    internal static List<T> SafeDeserializeList<T>(string? json, string fieldName)
    {
        if (string.IsNullOrEmpty(json)) return new List<T>();
        if (json.Length > MaxJsonFieldChars)
            throw new ArgumentException(
                $"{fieldName} payload too large ({json.Length:n0} chars; max {MaxJsonFieldChars:n0}).",
                fieldName);
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, LenientJsonOpts) ?? new List<T>();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON in {fieldName}: {ex.Message}", fieldName, ex);
        }
    }

    /// <summary>
    /// Canonicalizes the various classType strings the UI may send to a consistent casing
    /// (PascalCase/camelCase per the ECF/JTI convention) so the builder emits a stable shape.
    /// Track D.post fix M-2. Unknown class types are passed through unchanged (for forward
    /// compatibility with new JTI extensions).
    /// </summary>
    internal static string CanonicalizeClassType(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant() switch
        {
            "caseparticipant" => "caseParticipant",
            "caseassignment" => "caseAssignment",
            "codelist" => "codeList",
            "attorney" => "attorney",
            "contact" => "contact",
            "document" => "document",
            "judgment" => "judgment",
            "text" or "string" => "text",
            "number" => "number",
            "currency" => "currency",
            "date" => "date",
            "boolean" => "boolean",
            _ => raw // preserve unknown class types unchanged
        };
    }

    // ── Metadata value mapping helpers moved to EFiling.Nop.Mapping.MetadataValueMapper ──
    // T-5 consolidation. The 3 helpers `HasAnyContactField`, `DetermineTagValue`,
    // `BuildAdditionalInfoTags` formerly lived here alongside the inline metadata-switch
    // duplicated between Site 1 (this controller's `BuildSubmissionFromCreateModel`) and Site 2
    // (`EFilingMvcController.BuildQuickSubmission`). They now live on the single mapper.

    public CourtFilingController(
        IEFilingProvider provider,
        ICourtConfigurationService courtConfigService,
        IEFilingDraftService draftService)
    {
        _provider = provider;
        _courtConfigService = courtConfigService;
        _draftService = draftService;
    }

    // ─── Search Cases ─────────────────────────────────────────────

    public async Task<CaseSearchResultModel> SearchCasesAsync(CaseSearchModel model, CancellationToken ct = default)
    {
        var result = new CaseSearchResultModel { Search = model };

        var config = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
        if (config == null)
        {
            result.ErrorMessage = $"Court '{model.CourtId}' not found.";
            return result;
        }

        try
        {
            var criteria = new CaseSearchCriteria();

            switch (model.SearchMode?.ToLowerInvariant())
            {
                case "partyindividual":
                    criteria.FirstName = model.FirstName;
                    criteria.LastName = model.LastName;
                    break;
                case "partybusiness":
                    criteria.OrganizationName = model.OrganizationName;
                    break;
                case "title":
                    criteria.CaseTitle = model.CaseTitle;
                    break;
                case "category":
                    // Step #57 — category-search wire-through.
                    // The backend (SoapEnvelopeBuilder.BuildGetCaseListRequest) has supported
                    // <ns1:CaseCategoryText> since the original GetCaseList implementation but
                    // no UI/controller surface exposed it until this probe. Flowing through
                    // model.CaseCategoryCode → criteria.CaseCategoryCode → SOAP element.
                    criteria.CaseCategoryCode = model.CaseCategoryCode;
                    break;
                case "casenumber":
                default:
                    criteria.CaseDocketId = model.CaseDocketId;
                    break;
            }

            // Legacy fallback: if PartyName is set but no SearchMode, use full-name search
            if (string.IsNullOrEmpty(model.SearchMode) && !string.IsNullOrEmpty(model.PartyName))
                criteria.PartySearchTerm = model.PartyName;

            result.Cases = await _provider.SearchCasesAsync(config, criteria, ct);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Search failed: {ex.Message}";
        }

        return result;
    }

    // ─── Get Case Detail ──────────────────────────────────────────

    public async Task<CaseInfo?> GetCaseAsync(string courtId, string caseDocketId, CancellationToken ct = default)
    {
        var (caseInfo, _) = await GetCaseWithDiagnosticsAsync(courtId, caseDocketId, ct);
        return caseInfo;
    }

    public async Task<(CaseInfo? Case, string? DiagnosticError)> GetCaseWithDiagnosticsAsync(string courtId, string caseDocketId, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null)
            return (null, $"Court config not found for '{courtId}'.");

        if (string.IsNullOrEmpty(config.CourtRecordEndpoint))
            return (null, $"CourtRecordEndpoint is empty for court '{courtId}'.");

        try
        {
            var result = await _provider.GetCaseAsync(config, caseDocketId: caseDocketId, ct: ct);
            if (result == null)
                return (null, $"SOAP call succeeded but case '{caseDocketId}' not found in response (parser returned null). Endpoint: {config.CourtRecordEndpoint}");
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, $"GetCase exception: {ex.Message}");
        }
    }

    // ─── Create Case (Case Initiation) ────────────────────────────

    /// <summary>
    /// Submits a case (initial or subsequent) to the JTI provider and returns the artifacts
    /// the caller needs for downstream finalization (Braintree charge, EFilingOrderRecord).
    /// Does NOT charge payment, place a nopCommerce order, or create EFilingOrderRecord rows
    /// — those steps live in <c>EFilingMvcController.FinalizeFilingAsync</c> so they can be
    /// shared by both the CC AJAX submit-and-pay path and the SF form-post path.
    /// </summary>
    public async Task<CreateCaseResult> CreateCaseAsync(
        CreateCaseModel model, int customerId, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
        if (config == null)
            return new CreateCaseResult(false, $"Court '{model.CourtId}' not found.", null, null, null, null);

        var sub = BuildSubmissionFromCreateModel(model, config);

        // F-J1: fail fast on an incomplete submission before the billable JTI round-trip.
        var validationErrors = ValidateForSubmission(model, sub);
        if (validationErrors.Count > 0)
            return new CreateCaseResult(false,
                "Submission is incomplete: " + string.Join(" ", validationErrors), sub, null, null, config);

        // Calculate fees first
        var fees = await _provider.CalculateFeesAsync(config, sub, ct);
        if (fees.ErrorCode != 0)
            return new CreateCaseResult(false, $"Fee calculation failed: {fees.ErrorText}", sub, fees, null, config);

        // Submit filing
        var receipt = await _provider.SubmitFilingAsync(config, sub, ct);
        if (receipt.ErrorCode != 0)
            return new CreateCaseResult(false, $"Filing failed: {receipt.ErrorText}", sub, fees, receipt, config);

        // Mark draft as submitted if applicable
        if (model.DraftId.HasValue)
            await _draftService.MarkSubmittedAsync(model.DraftId.Value, receipt.EfmReferenceId ?? "unknown", ct);

        return new CreateCaseResult(true, null, sub, fees, receipt, config);
    }

    // ─── Save Draft ───────────────────────────────────────────────

    public async Task<int> SaveDraftAsync(CreateCaseModel model, int customerId, CancellationToken ct = default)
    {
        // Track D.post fix M-9: Drafts must persist the RAW form state (CreateCaseModel),
        // not the built FilingSubmission. The restore path in EFilingMvcController.CreateCase
        // deserializes draft.SubmissionJson back into a CreateCaseModel, so they must match
        // in shape. Previously this method stored the built submission, which would produce
        // a draft that could not be correctly restored. (In practice this method is not
        // wired to an MVC endpoint yet — the live save-draft path in EFilingMvcController
        // already stores the raw form JSON — but we fix the shape here for correctness.)
        var json = JsonSerializer.Serialize(model, JsonOpts);

        if (model.DraftId.HasValue)
        {
            var existing = await _draftService.GetByIdAsync(model.DraftId.Value, ct);
            // Track D.post fix M-7: Defense in depth — only update the draft if it belongs
            // to the supplied customer. The MVC layer is expected to resolve firm-admin
            // delegation before calling and to pass the effective owner id. Any mismatch
            // here means either (a) a bug in the caller, or (b) a security issue — fall
            // through to create a NEW draft rather than silently clobbering someone else's.
            if (existing != null && existing.CustomerId == customerId)
            {
                existing.SubmissionJson = json;
                existing.DisplayName = BuildDisplayName(model);
                await _draftService.UpdateAsync(existing, ct);
                return existing.Id;
            }
        }

        var draft = new EFilingDraft
        {
            CustomerId = customerId,
            CourtId = model.CourtId,
            FilingType = "Initial",
            SubmissionJson = json,
            DisplayName = BuildDisplayName(model)
        };

        var created = await _draftService.CreateAsync(draft, ct);
        return created.Id;
    }

    // ─── Get Filing List ──────────────────────────────────────────

    public async Task<FilingListModel> GetFilingListAsync(FilingListModel model, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(model.CourtId, ct);
        if (config == null)
        {
            model.ErrorMessage = $"Court '{model.CourtId}' not found.";
            return model;
        }

        try
        {
            var criteria = new FilingListCriteria
            {
                CaseDocketId = model.CaseDocketId,
                FromDate = string.IsNullOrEmpty(model.FromDate) ? null : DateTime.TryParse(model.FromDate, out var fd) ? fd : null,
                ToDate = string.IsNullOrEmpty(model.ToDate) ? null : DateTime.TryParse(model.ToDate, out var td) ? td : null
            };

            model.Filings = await _provider.GetFilingListAsync(config, criteria, ct);
        }
        catch (Exception ex)
        {
            model.ErrorMessage = $"Failed to load filings: {ex.Message}";
        }

        return model;
    }

    // ─── Get Filing Status ────────────────────────────────────────

    public async Task<FilingStatusResult?> GetFilingStatusAsync(
        string courtId, string efmReferenceId, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return null;

        return await _provider.GetFilingStatusAsync(config, efmReferenceId: efmReferenceId, ct: ct);
    }

    // ─── Get Charged Amount ───────────────────────────────────────

    public async Task<FeeCalculation?> GetChargedAmountAsync(
        string courtId, string efmReferenceId, CancellationToken ct = default)
    {
        var config = await _courtConfigService.GetByCourtIdAsync(courtId, ct);
        if (config == null) return null;

        return await _provider.GetChargedAmountAsync(config, efmReferenceId, ct);
    }

    // ─── Drafts ───────────────────────────────────────────────────

    public Task<List<EFilingDraft>> GetDraftsAsync(int customerId, CancellationToken ct = default)
        => _draftService.GetByCustomerAsync(customerId, ct);

    /// <summary>
    /// Deletes a draft after verifying it belongs to <paramref name="effectiveOwnerId"/>.
    /// Track D.post fix M-7: Defense in depth. The MVC layer is expected to resolve
    /// firm-admin delegation before calling; the effective owner id is the customer
    /// that owns (or is authorized to act on) the draft. Throws
    /// <see cref="UnauthorizedAccessException"/> on ownership mismatch.
    /// </summary>
    public async Task DeleteDraftAsync(int draftId, int effectiveOwnerId, CancellationToken ct = default)
    {
        var draft = await _draftService.GetByIdAsync(draftId, ct);
        if (draft == null) return; // idempotent delete
        if (draft.CustomerId != effectiveOwnerId)
            throw new UnauthorizedAccessException(
                $"Draft {draftId} does not belong to customer {effectiveOwnerId}.");
        await _draftService.DeleteAsync(draftId, ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// F-J1: server-side completeness check, run on the real submit paths (<see cref="CreateCaseAsync"/>
    /// and <c>EFilingMvcController.SubmitAndPayAjax</c>) before the billable JTI round-trip. Returns
    /// human-readable errors; an empty list means OK. JTI remains the authoritative validator — this
    /// only fails fast with clearer, field-specific messages and avoids a wasted round-trip on bad
    /// input. Initial filings require a case type + category + at least one filing party; every filing
    /// requires a lead document. Subsequent filings file against an existing case (CaseDocketId) and
    /// legitimately carry neither a case type/category nor new parties, so those checks are initial-only.
    /// </summary>
    public static IReadOnlyList<string> ValidateForSubmission(CreateCaseModel model, FilingSubmission sub)
    {
        var errors = new List<string>();

        if (sub.LeadDocument == null)
            errors.Add("A lead document is required before submitting.");

        if (!model.IsSubsequentFiling)
        {
            if (string.IsNullOrWhiteSpace(model.CaseTypeCode))
                errors.Add("Case type is required for case initiation.");
            if (string.IsNullOrWhiteSpace(model.CaseCategoryCode))
                errors.Add("Case category is required for case initiation.");
            if (!sub.Parties.Exists(p => p.ReferenceId != null
                    && p.ReferenceId.StartsWith("filedBy", StringComparison.Ordinal)))
                errors.Add("At least one filing party is required for case initiation.");
        }

        return errors;
    }

    /// <summary>
    /// Builds the provider-layer <see cref="FilingSubmission"/> from the UI's <see cref="CreateCaseModel"/>.
    /// </summary>
    /// <param name="model">Form payload from the view.</param>
    /// <param name="config">Court configuration (for per-court overrides). Optional for testing.</param>
    /// <param name="validateForSubmission">
    /// When true (default), throw <see cref="ArgumentException"/> if any document is missing a
    /// BlobUrl (per Track D.post fix M-8). Set false during draft save, where incomplete
    /// document uploads are expected.
    /// </param>
    public static FilingSubmission BuildSubmissionFromCreateModel(
        CreateCaseModel model,
        CourtConfiguration? config = null,
        bool validateForSubmission = true)
    {
        // H-2: Prefer per-court submitter username; fall back to DefaultSubmitterUsername.
        var submitterUsername = ResolveSubmitterUsername(config);
        // H-3: Prefer per-court attorney role code; fall back to DefaultAttorneyRoleCode.
        var attorneyRoleCode = ResolveAttorneyRoleCode(config);

        var sub = new FilingSubmission
        {
            FilingType = model.IsSubsequentFiling ? FilingType.Subsequent : FilingType.Initial,
            EfspReferenceId = $"EFSP-{Guid.NewGuid():N}",
            SubmitterUsername = submitterUsername,
            CaseTypeCode = model.CaseTypeCode,
            CaseCategoryCode = model.CaseCategoryCode,
            JurisdictionalGroundsCode = model.JurisdictionalGroundsCode,
            LocationCode = model.LocationCode,
            LocationName = model.LocationName,
            IncidentZipCode = model.IncidentZipCode,
            // F-B5 fix: the UI captures the claim amount in DemandAmount ("Demand amount" input);
            // AmountInControversy is the wire field. Coalesce so the legacy form-post path (which,
            // unlike SubmitAndPayAjax, has no demandAmount→AmountInControversy remap) still reaches the wire.
            AmountInControversy = model.AmountInControversy ?? model.DemandAmount,
            MessageToClerk = model.MessageToClerk,
            ComplexLitigation = model.ComplexLitigation,
            ClassAction = model.ClassAction,
            Asbestos = model.Asbestos,
            CaliforniaEnvironmentalQualityAct = model.CaliforniaEnvironmentalQualityAct,
            // Audit F-1: CreateCaseModel is a web form (bool checkbox, default
            // false). Map to tri-state so that "user didn't check the box" → null (omit
            // element) rather than "emit false explicitly". This keeps forward-path behavior
            // consistent with the majority of baselines (CIV-INI-001 etc.) which omit
            // <conditionallySealed> entirely. Round-trip-parsed submissions that came from a
            // baseline with <conditionallySealed>false</> will have sub.ConditionallySealed =
            // false and round-trip correctly.
            ConditionallySealed = model.ConditionallySealed ? (bool?)true : null,
            CaseDocketId = model.CaseDocketId,
            CaseTrackingId = model.CaseTrackingId,
            ComplaintId = model.ComplaintId,
        };

        // Parties (from JSON array)
        int filingIdx = 0, opposingIdx = 0;
        var partyEntries = SafeDeserializeList<PartyEntryDto>(model.PartiesJson, nameof(model.PartiesJson));

        foreach (var p in partyEntries)
        {
            var isFiling = string.Equals(p.Side, "filing", StringComparison.OrdinalIgnoreCase);
            var idx = isFiling ? filingIdx++ : opposingIdx++;
            var refId = isFiling ? $"filedBy{idx}" : $"filedAsTo{idx}";

            // Track D.post fix C-3: Build ContactInfo whenever ANY contact field is populated
            // for a self-represented party. Previously required Address1 which silently
            // dropped phone/email when the user provided no mailing address.
            ContactInfo? partyContact = null;
            if (p.SelfRepresented)
            {
                var hasAddress = !string.IsNullOrEmpty(p.Address1);
                var hasPhone = !string.IsNullOrEmpty(p.Phone);
                var hasEmail = !string.IsNullOrEmpty(p.Email);
                if (hasAddress || hasPhone || hasEmail)
                {
                    partyContact = new ContactInfo
                    {
                        MailingAddress = hasAddress ? new StructuredAddress
                        {
                            AddressType = p.AddressType,
                            Address1 = p.Address1,
                            Address2 = p.Address2,
                            City = p.City,
                            State = p.State,
                            Zip = p.Zip,
                            Country = p.Country ?? string.Empty
                        } : null,
                        PhoneType = p.PhoneType,
                        PhoneNumber = p.Phone,
                        Email = p.Email
                    };
                }
            }

            var party = new FilingParty
            {
                ReferenceId = refId,
                RoleCode = p.PartyType ?? string.Empty,
                IsOrganization = p.IsOrganization,
                FirstName = p.FirstName,
                MiddleName = p.MiddleName,
                LastName = p.LastName,
                NameSuffix = p.Suffix,
                OrganizationName = p.OrganizationName,
                FeeExemptionRequestType = !string.IsNullOrEmpty(p.FeeExemptionType) ? p.FeeExemptionType : null,
                // Track D.post fix C-1: propagate "first appearance fee already paid" flag
                FirstAppearancePaid = p.FirstAppearancePaid,
                // Track D.post fix C-2: propagate "government entity exempt" flag
                GovernmentExempt = p.GovernmentExempt,
                InterpreterLanguage = !string.IsNullOrEmpty(p.InterpreterLanguage) ? p.InterpreterLanguage : null,
                EService = p.EService,
                Contact = partyContact
            };

            // Map AKA/DBA alternate names
            if (p.Akas != null)
            {
                foreach (var aka in p.Akas)
                {
                    if (string.IsNullOrEmpty(aka.Type)) continue;
                    party.AlternateNames.Add(new AlternateName
                    {
                        Type = aka.Type,
                        FirstName = aka.IsOrganization ? null : aka.FirstName,
                        MiddleName = aka.IsOrganization ? null : aka.MiddleName,
                        LastName = aka.IsOrganization ? null : aka.LastName,
                        NameSuffix = aka.IsOrganization ? null : aka.Suffix, // 2026-05-17: pre-fix silently dropped
                        OrganizationName = aka.IsOrganization ? aka.OrganizationName : null
                    });
                }
            }

            sub.Parties.Add(party);
        }

        // Attorneys (from JSON array)
        var attorneyEntries = SafeDeserializeList<AttorneyEntryDto>(model.AttorneysJson, nameof(model.AttorneysJson));

        for (int i = 0; i < attorneyEntries.Count; i++)
        {
            var a = attorneyEntries[i];
            var refId = "attorney" + i;

            // Track D.post fix M-1: Build ContactInfo / MailingAddress only when at least one
            // field is populated. Previously we unconditionally built an empty ContactInfo with
            // an empty StructuredAddress, which would emit empty XML elements to the wire
            // (echoing the Bug #5 empty-CaseTrackingID pattern that server rejects with 4011).
            var attHasAddress = !string.IsNullOrEmpty(a.Address1);
            var attHasPhone = !string.IsNullOrEmpty(a.Phone);
            var attHasEmail = !string.IsNullOrEmpty(a.Email);
            ContactInfo? attorneyContact = null;
            if (attHasAddress || attHasPhone || attHasEmail)
            {
                attorneyContact = new ContactInfo
                {
                    MailingAddress = attHasAddress ? new StructuredAddress
                    {
                        AddressType = a.AddressType,
                        Address1 = a.Address1,
                        Address2 = a.Address2,
                        City = a.City,
                        State = a.State,
                        Zip = a.Zip,
                        Country = a.Country ?? string.Empty
                    } : null,
                    PhoneType = a.PhoneType,
                    PhoneNumber = a.Phone,
                    Email = a.Email
                };
            }

            var attorney = new FilingParty
            {
                ReferenceId = refId,
                RoleCode = attorneyRoleCode,
                FirstName = a.FirstName,
                MiddleName = a.MiddleName,
                LastName = a.LastName,
                NameSuffix = a.Suffix,
                BarNumber = a.BarNumber,
                OrganizationName = a.FirmName,
                Contact = attorneyContact
            };
            sub.Parties.Add(attorney);
        }

        // Build REPRESENTEDBY associations using each filing party's LeadAttorneyIdx
        int filingPartyIdx = 0;
        foreach (var p in partyEntries)
        {
            if (!string.Equals(p.Side, "filing", StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.LeadAttorneyIdx >= 0 && p.LeadAttorneyIdx < attorneyEntries.Count)
            {
                sub.PartyAssociations.Add(new PartyAssociation
                {
                    AssociationType = "REPRESENTEDBY",
                    ParticipantRef = $"filedBy{filingPartyIdx}",
                    RelatedParticipantRef = $"attorney{p.LeadAttorneyIdx}"
                });
            }
            filingPartyIdx++;
        }

        // Documents (from JSON array)
        if (!string.IsNullOrEmpty(model.DocumentsJson))
        {
            var docEntries = SafeDeserializeList<DocumentEntryDto>(model.DocumentsJson, nameof(model.DocumentsJson));

            int docSeq = 0;
            foreach (var d in docEntries)
            {
                var refId = $"doc{docSeq}";
                // Track D.post fix M-8: Fail fast instead of silently substituting
                // "https://placeholder.pdf" for a missing BlobUrl. Previously we'd send the
                // placeholder URL to JTI, which would attempt to fetch it and fail with a
                // confusing downstream error. Skipped for draft save, where incomplete
                // uploads are expected.
                if (validateForSubmission && string.IsNullOrWhiteSpace(d.BlobUrl))
                {
                    throw new ArgumentException(
                        $"Document #{docSeq + 1} ({d.DocumentCode ?? "unknown"}) is missing a valid file URL (BlobUrl). "
                            + "Upload the document before submitting.",
                        nameof(model.DocumentsJson));
                }

                var doc = new FilingDocument
                {
                    ReferenceId = refId,
                    DocumentCode = d.DocumentCode ?? string.Empty,
                    FileControlId = $"doc-{Guid.NewGuid():N}",
                    SequenceNumber = docSeq,
                    NameExtension = d.NameExtension,
                    // When draft-saving without an uploaded file, emit empty string so the draft
                    // payload is still structurally complete. Submission path rejects this above.
                    BinaryLocationUri = d.BlobUrl ?? string.Empty,
                    ComplaintRef = model.IsSubsequentFiling ? model.ComplaintId : null,
                    IdentificationSourceText = d.IdentificationSourceText,
                };

                // Map metadata values
                if (d.Metadata != null)
                {
                    foreach (var m in d.Metadata)
                    {
                        var mv = new FilingMetadataValue
                        {
                            Code = m.Code ?? string.Empty,
                            // Track D.post fix M-2: canonicalize classType casing before emission
                            ClassType = CanonicalizeClassType(m.ClassType),
                            SubType = m.SubType,
                            ValueRestriction = m.ValueRestriction,
                        };

                        // Map additionalInfoTags
                        if (m.AdditionalInfoTags != null)
                        {
                            foreach (var tag in m.AdditionalInfoTags)
                                mv.AdditionalInfoTags.Add(new AdditionalInfoTag
                                {
                                    TagType = tag.TagType ?? string.Empty,
                                    TagValue = tag.TagValue ?? string.Empty
                                });
                        }

                        switch (m.ClassType?.ToLowerInvariant())
                        {
                            case "boolean":
                                mv.BooleanValue = m.Value is JsonElement je && je.ValueKind == JsonValueKind.True;
                                break;
                            case "currency":
                                if (m.Value is JsonElement ce && ce.TryGetDecimal(out var dec))
                                    mv.CurrencyValue = dec;
                                else if (m.Value is string cs && decimal.TryParse(cs, out var dp))
                                    mv.CurrencyValue = dp;
                                break;
                            case "date":
                                if (m.Value is JsonElement dateEl)
                                {
                                    var dateStr = dateEl.GetString();
                                    if (DateTime.TryParse(dateStr, out var dt))
                                        mv.DateValue = dt;
                                    else
                                        mv.TextValue = dateStr;
                                }
                                else if (m.Value is string ds)
                                {
                                    if (DateTime.TryParse(ds, out var dt2))
                                        mv.DateValue = dt2;
                                    else
                                        mv.TextValue = ds;
                                }
                                break;
                            case "codelist":
                                if (m.Value is JsonElement cle)
                                    mv.CodeValue = cle.GetString();
                                else if (m.Value is string cls)
                                    mv.CodeValue = cls;
                                break;
                            case "caseparticipant":
                            case "attorney":
                            case "caseassignment":
                                // Value can be a string (single ID) or array (multiple IDs)
                                if (m.Value is JsonElement pe)
                                {
                                    if (pe.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in pe.EnumerateArray())
                                            mv.IdReferences.Add(item.GetString() ?? string.Empty);
                                    }
                                    else if (pe.ValueKind == JsonValueKind.String)
                                    {
                                        mv.IdReferences.Add(pe.GetString() ?? string.Empty);
                                    }
                                }
                                else if (m.Value is string ps)
                                {
                                    mv.IdReferences.Add(ps);
                                }
                                break;
                            default:
                                if (m.Value is JsonElement te)
                                    mv.TextValue = te.GetString();
                                else if (m.Value is string ts)
                                    mv.TextValue = ts;
                                break;
                        }

                        doc.MetadataValues.Add(mv);
                    }
                }

                if (string.Equals(d.Role, "lead", StringComparison.OrdinalIgnoreCase))
                    sub.LeadDocument = doc;
                else
                    sub.ConnectedDocuments.Add(doc);

                // Auto-associate FILEDBY with all filing-side parties
                for (int fi = 0; fi < filingIdx; fi++)
                {
                    sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
                    {
                        AssociationType = "FILEDBY",
                        ParticipantRef = $"filedBy{fi}",
                        DocumentRef = refId
                    });
                }

                // Auto-associate REFERS_TO with all opposing-side parties
                for (int oi = 0; oi < opposingIdx; oi++)
                {
                    sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
                    {
                        AssociationType = "REFERS_TO",
                        ParticipantRef = $"filedAsTo{oi}",
                        DocumentRef = refId
                    });
                }

                docSeq++;
            }
        }
        else if (!string.IsNullOrEmpty(model.LeadDocumentCode))
        {
            // Legacy fallback: single lead document without JSON
            sub.LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = model.LeadDocumentCode,
                FileControlId = $"doc-{Guid.NewGuid():N}",
                BinaryLocationUri = model.LeadDocumentUrl ?? string.Empty,
                SequenceNumber = 0
            };

            for (int fi = 0; fi < filingIdx; fi++)
            {
                sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
                {
                    AssociationType = "FILEDBY",
                    ParticipantRef = $"filedBy{fi}",
                    DocumentRef = "doc0"
                });
            }
            for (int oi = 0; oi < opposingIdx; oi++)
            {
                sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
                {
                    AssociationType = "REFERS_TO",
                    ParticipantRef = $"filedAsTo{oi}",
                    DocumentRef = "doc0"
                });
            }
        }

        // For subsequent filings, process MetadataJson to add metadata to lead document
        if (model.IsSubsequentFiling && sub.LeadDocument != null && !string.IsNullOrEmpty(model.MetadataJson))
        {
            var metadataItems = SafeDeserializeList<MetadataEntryDto>(model.MetadataJson, nameof(model.MetadataJson));

            // T-5 consolidation: classType dispatch delegated to the single
            // MetadataValueMapper. Previously this was a ~270-line inline switch duplicated
            // (in partial form) at EFilingMvcController.BuildQuickSubmission. See
            // `@c:/Users/sevak/workspace/test/src/EFiling/EFiling.Nop/Mapping/MetadataValueMapper.cs`
            // for the per-classType semantics (incl. audit C-1, C-2, C-3, D-2 fixes).
            foreach (var meta in metadataItems)
            {
                foreach (var mv in MetadataValueMapper.FromDto(meta))
                    sub.LeadDocument.MetadataValues.Add(mv);
            }
        }

        // Payment: EFSP-handled billing (0/0/ACH) — we collect payment ourselves via Braintree
        sub.Payment = new FilingPayment
        {
            CustomerProfileId = "0",
            CustomerPaymentProfileId = "0",
            PaymentType = "ACH"
        };

        return sub;
    }

    internal static string BuildDisplayName(CreateCaseModel model)
    {
        string plaintiff = "Unknown", defendant = "Unknown";
        var hasOpposing = false;

        if (!string.IsNullOrEmpty(model.PartiesJson))
        {
            // Use SafeDeserializeList defensively; swallow malformed JSON here because draft naming
            // must not fail the whole save flow — fall back to "Unknown v. Unknown".
            List<PartyEntryDto> parties;
            try
            {
                parties = SafeDeserializeList<PartyEntryDto>(model.PartiesJson, nameof(model.PartiesJson));
            }
            catch (ArgumentException)
            {
                parties = new List<PartyEntryDto>();
            }

            var filing = parties.FirstOrDefault(p => string.Equals(p.Side, "filing", StringComparison.OrdinalIgnoreCase));
            var opposing = parties.FirstOrDefault(p => string.Equals(p.Side, "opposing", StringComparison.OrdinalIgnoreCase));

            if (filing != null)
                plaintiff = filing.IsOrganization
                    ? filing.OrganizationName ?? "Unknown"
                    : $"{filing.FirstName} {filing.LastName}".Trim();

            if (opposing != null)
            {
                hasOpposing = true;
                defendant = opposing.IsOrganization
                    ? opposing.OrganizationName ?? "Unknown"
                    : $"{opposing.FirstName} {opposing.LastName}".Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(plaintiff)) plaintiff = "Unknown";
        if (string.IsNullOrWhiteSpace(defendant)) defendant = "Unknown";

        // F-E1: single-party initiations (Probate estate/guardianship/conservatorship, ex-parte)
        // carry no opposing party — render "In re <party>" instead of a misleading
        // "<party> v. Unknown". Local label only: CI requests omit CaseTitleText, so the court
        // derives the authoritative caption; this affects just our draft / order-record string.
        if (!hasOpposing && !string.Equals(plaintiff, "Unknown", StringComparison.Ordinal))
            return $"In re {plaintiff}";

        return $"{plaintiff} v. {defendant}";
    }
}
