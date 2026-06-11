using System.Text.Json;
using EFiling.Core.Models;
using EFiling.Nop.Domain;
using Nop.Data;

namespace EFiling.Nop.Services;

/// <summary>
/// Database-backed court configuration service using nopCommerce's IRepository pattern.
/// All data is stored in the CourtConfiguration SQL Server table.
/// </summary>
public class DbCourtConfigurationService : ICourtConfigurationService
{
    private readonly IRepository<CourtConfigurationRecord> _repository;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DbCourtConfigurationService(IRepository<CourtConfigurationRecord> repository)
    {
        _repository = repository;
    }

    public async Task<List<CourtConfiguration>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _repository.GetAllAsync(q => q.Where(r => r.IsActive));
        return records.Select(MapToConfig).ToList();
    }

    public async Task<CourtConfiguration?> GetByCourtIdAsync(string courtId, CancellationToken ct = default)
    {
        var records = await _repository.GetAllAsync(q =>
            q.Where(r => r.CourtId == courtId));
        var record = records.FirstOrDefault();
        return record == null ? null : MapToConfig(record);
    }

    public async Task SaveAsync(CourtConfiguration config, CancellationToken ct = default)
    {
        var existing = (await _repository.GetAllAsync(q =>
            q.Where(r => r.CourtId == config.CourtId))).FirstOrDefault();

        if (existing != null)
        {
            // Update existing record
            existing.DisplayName = config.DisplayName;
            existing.CountyName = config.CountyName;
            existing.ProviderType = config.ProviderType;
            existing.Environment = config.Environment;
            existing.SoapEndpoint = config.SoapEndpoint;
            existing.RestBaseUrl = config.RestBaseUrl;
            existing.CourtRecordEndpoint = config.CourtRecordEndpoint;
            existing.NfrcCallbackUrl = config.NfrcCallbackUrl;
            existing.Username = config.Username;
            existing.EncryptedPassword = EncryptPassword(config.Password);
            existing.IsActive = config.IsActive;
            existing.CivilCaseTypeCodesJson = JsonSerializer.Serialize(config.CivilCaseTypeCodes, JsonOpts);
            existing.ExtraFlagsJson = JsonSerializer.Serialize(config.ExtraFlags, JsonOpts);
            existing.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpdateAsync(existing);
        }
        else
        {
            var record = MapToRecord(config);
            record.CreatedUtc = DateTime.UtcNow;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertAsync(record);
        }
    }

    public async Task DeleteAsync(string courtId, CancellationToken ct = default)
    {
        var records = await _repository.GetAllAsync(q =>
            q.Where(r => r.CourtId == courtId));
        if (records.Any())
            await _repository.DeleteAsync(records);
    }

    // ─── Additional methods for admin UI ─────────────────────────────

    /// <summary>Get all records including inactive (for admin listing).</summary>
    public async Task<List<CourtConfigurationRecord>> GetAllRecordsAsync(CancellationToken ct = default)
    {
        var records = await _repository.GetAllAsync(
            func: (Func<IQueryable<CourtConfigurationRecord>, IQueryable<CourtConfigurationRecord>>?)null);
        return records.ToList();
    }

    /// <summary>Get a single record by court ID (for admin edit).</summary>
    public async Task<CourtConfigurationRecord?> GetRecordByCourtIdAsync(string courtId, CancellationToken ct = default)
    {
        var records = await _repository.GetAllAsync(q =>
            q.Where(r => r.CourtId == courtId));
        return records.FirstOrDefault();
    }

    /// <summary>Save a record directly (for admin create/edit).</summary>
    public async Task SaveRecordAsync(CourtConfigurationRecord record, CancellationToken ct = default)
    {
        if (record.Id > 0)
        {
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpdateAsync(record);
        }
        else
        {
            record.CreatedUtc = DateTime.UtcNow;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertAsync(record);
        }
    }

    // ─── Mapping helpers ─────────────────────────────────────────────

    private static CourtConfiguration MapToConfig(CourtConfigurationRecord record)
    {
        return new CourtConfiguration
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
            Password = DecryptPassword(record.EncryptedPassword),
            IsActive = record.IsActive,
            CivilCaseTypeCodes = ParseJsonArray(record.CivilCaseTypeCodesJson),
            TestFilingMode = (EFiling.Core.Enums.TestFilingMode)record.TestFilingMode,
            ExtraFlags = ParseExtraFlags(record.ExtraFlagsJson),
        };
    }

    private static CourtConfigurationRecord MapToRecord(CourtConfiguration config)
    {
        return new CourtConfigurationRecord
        {
            CourtId = config.CourtId,
            DisplayName = config.DisplayName,
            CountyName = config.CountyName,
            ProviderType = config.ProviderType,
            Environment = config.Environment,
            SoapEndpoint = config.SoapEndpoint,
            RestBaseUrl = config.RestBaseUrl,
            CourtRecordEndpoint = config.CourtRecordEndpoint,
            NfrcCallbackUrl = config.NfrcCallbackUrl,
            Username = config.Username,
            EncryptedPassword = EncryptPassword(config.Password),
            IsActive = config.IsActive,
            CivilCaseTypeCodesJson = JsonSerializer.Serialize(config.CivilCaseTypeCodes, JsonOpts),
            TestFilingMode = (int)config.TestFilingMode,
            ExtraFlagsJson = JsonSerializer.Serialize(config.ExtraFlags, JsonOpts),
        };
    }

    private static List<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, string> ParseExtraFlags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Simple symmetric encryption for passwords at rest.
    /// In production, replace with nopCommerce's IEncryptionService or DPAPI.
    /// Uses Base64 as a placeholder — NOT secure for production.
    /// </summary>
    private static string EncryptPassword(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
    }

    private static string DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
        }
        catch
        {
            return encrypted;
        }
    }
}
