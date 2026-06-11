using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="CourtConfigurationRecord"/>.
/// Maps columns for the CourtConfiguration table via nopCommerce's FluentMigrator integration.
/// </summary>
public class CourtConfigurationRecordBuilder : NopEntityBuilder<CourtConfigurationRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(CourtConfigurationRecord.CourtId)).AsString(100).NotNullable().Unique("UX_CourtConfiguration_CourtId")
            .WithColumn(nameof(CourtConfigurationRecord.DisplayName)).AsString(500).NotNullable()
            .WithColumn(nameof(CourtConfigurationRecord.CountyName)).AsString(200).NotNullable().WithDefaultValue("")
            .WithColumn(nameof(CourtConfigurationRecord.ProviderType)).AsString(50).NotNullable().WithDefaultValue("JTI")
            .WithColumn(nameof(CourtConfigurationRecord.Environment)).AsString(50).NotNullable().WithDefaultValue("Staging")
            .WithColumn(nameof(CourtConfigurationRecord.SoapEndpoint)).AsString(1000).NotNullable()
            .WithColumn(nameof(CourtConfigurationRecord.RestBaseUrl)).AsString(1000).NotNullable().WithDefaultValue("")
            .WithColumn(nameof(CourtConfigurationRecord.CourtRecordEndpoint)).AsString(1000).NotNullable().WithDefaultValue("")
            .WithColumn(nameof(CourtConfigurationRecord.NfrcCallbackUrl)).AsString(1000).NotNullable().WithDefaultValue("")
            .WithColumn(nameof(CourtConfigurationRecord.Username)).AsString(200).NotNullable()
            .WithColumn(nameof(CourtConfigurationRecord.EncryptedPassword)).AsString(500).NotNullable()
            .WithColumn(nameof(CourtConfigurationRecord.IsActive)).AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn(nameof(CourtConfigurationRecord.CivilCaseTypeCodesJson)).AsString(int.MaxValue).NotNullable().WithDefaultValue("[]")
            .WithColumn(nameof(CourtConfigurationRecord.TestFilingMode)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(CourtConfigurationRecord.ExtraFlagsJson)).AsString(int.MaxValue).NotNullable().WithDefaultValue("{}")
            .WithColumn(nameof(CourtConfigurationRecord.CreatedUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(CourtConfigurationRecord.UpdatedUtc)).AsDateTime2().NotNullable();
    }
}
