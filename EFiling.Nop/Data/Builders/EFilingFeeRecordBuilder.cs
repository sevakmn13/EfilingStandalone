using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="EFilingFeeRecord"/>.
/// Maps columns for the EFilingFeeRecord table via nopCommerce's FluentMigrator integration.
/// </summary>
public class EFilingFeeRecordBuilder : NopEntityBuilder<EFilingFeeRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(EFilingFeeRecord.EFilingOrderRecordId)).AsInt32().NotNullable().Indexed("IX_EFilingFeeRecord_OrderRecordId")
            .WithColumn(nameof(EFilingFeeRecord.Source)).AsString(20).NotNullable().WithDefaultValue("Estimated")
            .WithColumn(nameof(EFilingFeeRecord.Amount)).AsDecimal(18, 2).NotNullable()
            .WithColumn(nameof(EFilingFeeRecord.AccountingCostCode)).AsString(50).NotNullable()
            .WithColumn(nameof(EFilingFeeRecord.Description)).AsString(500).Nullable()
            .WithColumn(nameof(EFilingFeeRecord.CreatedUtc)).AsDateTime2().NotNullable();
    }
}
