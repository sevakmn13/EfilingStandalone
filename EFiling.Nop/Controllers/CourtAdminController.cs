using Microsoft.AspNetCore.Mvc;
using EFiling.Core.Interfaces;
using EFiling.Core.Validation;
using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using EFiling.Nop.Services;

namespace EFiling.Nop.Controllers;

/// <summary>
/// Admin controller for managing court configurations.
/// Courts are pre-seeded — admin can edit, toggle, and test connections only.
/// </summary>
[Area("Admin")]
[Route("Admin/CourtConfig")]
public class CourtAdminController : Controller
{
    private readonly DbCourtConfigurationService _configService;
    private readonly IEFilingProvider _provider;

    public CourtAdminController(
        DbCourtConfigurationService configService,
        IEFilingProvider provider)
    {
        _configService = configService;
        _provider = provider;
    }

    // ─── List ────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var records = await _configService.GetAllRecordsAsync(ct);
        return View("Index", records);
    }

    // ─── Edit ────────────────────────────────────────────────────────

    [HttpGet("Edit/{courtId}")]
    public async Task<IActionResult> Edit(string courtId, CancellationToken ct)
    {
        var record = await _configService.GetRecordByCourtIdAsync(courtId, ct);
        if (record == null)
        {
            TempData["ErrorMessage"] = $"Court '{courtId}' not found.";
            return RedirectToAction(nameof(Index));
        }

        var model = MapToModel(record);

        // Show any pre-existing environment-safety issues for this court.
        var draft = new EFiling.Core.Models.CourtConfiguration
        {
            CourtId = record.CourtId,
            DisplayName = record.DisplayName,
            Environment = record.Environment,
            SoapEndpoint = record.SoapEndpoint,
            RestBaseUrl = record.RestBaseUrl,
            CourtRecordEndpoint = record.CourtRecordEndpoint,
            TestFilingMode = (EFiling.Core.Enums.TestFilingMode)record.TestFilingMode,
        };
        var issues = CourtConfigurationValidator.Validate(draft);
        foreach (var err in issues.Where(i => i.Severity == CourtConfigValidationSeverity.Error))
            ModelState.AddModelError(err.Field ?? string.Empty, err.Message);
        model.ValidationWarnings = issues
            .Where(i => i.Severity == CourtConfigValidationSeverity.Warning)
            .Select(i => $"{i.Field}: {i.Message}")
            .ToList();

        return View("Edit", model);
    }

    [HttpPost("Edit/{courtId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string courtId, CourtConfigEditModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Edit", model);

        var existing = await _configService.GetRecordByCourtIdAsync(courtId, ct);
        if (existing == null)
        {
            TempData["ErrorMessage"] = $"Court '{courtId}' not found.";
            return RedirectToAction(nameof(Index));
        }

        // ── Environment-safety validation ────────────────────────────
        // Build a transient CourtConfiguration from the posted model and validate
        // it BEFORE persisting. Errors block the save; warnings are surfaced and
        // persisted (admin has chosen to proceed).
        var draft = new EFiling.Core.Models.CourtConfiguration
        {
            CourtId = courtId,
            DisplayName = model.DisplayName,
            Environment = model.Environment,
            SoapEndpoint = model.SoapEndpoint,
            RestBaseUrl = model.RestBaseUrl,
            CourtRecordEndpoint = model.CourtRecordEndpoint,
            TestFilingMode = (EFiling.Core.Enums.TestFilingMode)model.TestFilingMode,
        };
        var issues = CourtConfigurationValidator.Validate(draft);
        var errors = issues.Where(i => i.Severity == CourtConfigValidationSeverity.Error).ToList();
        if (errors.Any())
        {
            foreach (var err in errors)
                ModelState.AddModelError(err.Field ?? string.Empty, err.Message);
            // Keep warnings visible too
            model.ValidationWarnings = issues
                .Where(i => i.Severity == CourtConfigValidationSeverity.Warning)
                .Select(i => $"{i.Field}: {i.Message}")
                .ToList();
            model.IsEdit = true;
            return View("Edit", model);
        }

        // Update fields on the existing record (preserves Id, CreatedUtc, EncryptedPassword if blank)
        existing.DisplayName = model.DisplayName;
        existing.CountyName = model.CountyName;
        existing.ProviderType = model.ProviderType;
        existing.Environment = model.Environment;
        existing.SoapEndpoint = model.SoapEndpoint;
        existing.RestBaseUrl = model.RestBaseUrl;
        existing.CourtRecordEndpoint = model.CourtRecordEndpoint;
        existing.NfrcCallbackUrl = model.NfrcCallbackUrl;
        existing.Username = model.Username;
        if (!string.IsNullOrEmpty(model.Password))
            existing.EncryptedPassword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(model.Password));
        existing.IsActive = model.IsActive;
        existing.CivilCaseTypeCodesJson = ToJsonArray(model.CivilCaseTypeCodes);
        existing.TestFilingMode = model.TestFilingMode;

        await _configService.SaveRecordAsync(existing, ct);

        // Surface warnings after a successful save so the admin knows to investigate.
        var warnings = issues.Where(i => i.Severity == CourtConfigValidationSeverity.Warning).ToList();
        if (warnings.Any())
        {
            TempData["WarningMessage"] = "Court saved with warnings: " +
                string.Join(" | ", warnings.Select(w => $"[{w.Code}] {w.Message}"));
        }
        else
        {
            TempData["SuccessMessage"] = $"Court '{model.DisplayName}' updated successfully.";
        }
        return RedirectToAction(nameof(Index));
    }

    // ─── Delete ──────────────────────────────────────────────────────

    [HttpPost("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string courtId, CancellationToken ct)
    {
        await _configService.DeleteAsync(courtId, ct);
        TempData["SuccessMessage"] = "Court configuration deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Test Connection ─────────────────────────────────────────────

    [HttpPost("TestConnection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(string courtId, CancellationToken ct)
    {
        var config = await _configService.GetByCourtIdAsync(courtId, ct);
        if (config == null)
            return Json(new { success = false, message = "Court not found." });

        try
        {
            var policy = await _provider.GetPolicyAsync(config, ct);
            return Json(new { success = true, message = $"Connection successful. Policy retrieved." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Connection failed: {ex.Message}" });
        }
    }

    // ─── Mapping ─────────────────────────────────────────────────────

    private static CourtConfigEditModel MapToModel(CourtConfigurationRecord record)
    {
        return new CourtConfigEditModel
        {
            CourtId = record.CourtId,
            DisplayName = record.DisplayName,
            CountyName = record.CountyName,
            ProviderType = record.ProviderType,
            Environment = record.Environment,
            SoapEndpoint = record.SoapEndpoint,
            RestBaseUrl = record.RestBaseUrl,
            CourtRecordEndpoint = record.CourtRecordEndpoint,
            NfrcCallbackUrl = record.NfrcCallbackUrl,
            Username = record.Username,
            Password = "", // Never send password back to client
            IsActive = record.IsActive,
            CivilCaseTypeCodes = string.Join(",", ParseJsonArray(record.CivilCaseTypeCodesJson)),
            TestFilingMode = record.TestFilingMode,
            IsEdit = true,
        };
    }

    private static List<string> ParseJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string ToJsonArray(string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return "[]";
        var items = commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return System.Text.Json.JsonSerializer.Serialize(items);
    }

    private static CourtConfigurationRecord MapFromModel(CourtConfigEditModel model)
    {
        return new CourtConfigurationRecord
        {
            CourtId = model.CourtId,
            DisplayName = model.DisplayName,
            CountyName = model.CountyName,
            ProviderType = model.ProviderType,
            Environment = model.Environment,
            SoapEndpoint = model.SoapEndpoint,
            RestBaseUrl = model.RestBaseUrl,
            CourtRecordEndpoint = model.CourtRecordEndpoint,
            NfrcCallbackUrl = model.NfrcCallbackUrl,
            Username = model.Username,
            EncryptedPassword = !string.IsNullOrEmpty(model.Password)
                ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(model.Password))
                : string.Empty,
            IsActive = model.IsActive,
            CivilCaseTypeCodesJson = ToJsonArray(model.CivilCaseTypeCodes),
            TestFilingMode = model.TestFilingMode,
        };
    }
}
