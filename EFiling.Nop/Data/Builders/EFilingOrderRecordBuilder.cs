using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="EFilingOrderRecord"/>.
/// Maps columns for the EFilingOrderRecord table via nopCommerce's FluentMigrator integration.
/// </summary>
public class EFilingOrderRecordBuilder : NopEntityBuilder<EFilingOrderRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(EFilingOrderRecord.OrderId)).AsInt32().NotNullable().Indexed("IX_EFilingOrderRecord_OrderId")
            .WithColumn(nameof(EFilingOrderRecord.EfspReferenceId)).AsString(200).NotNullable().Indexed("IX_EFilingOrderRecord_EfspReferenceId")
            .WithColumn(nameof(EFilingOrderRecord.EfmReferenceId)).AsString(200).Nullable().Indexed("IX_EFilingOrderRecord_EfmReferenceId")
            .WithColumn(nameof(EFilingOrderRecord.CourtId)).AsString(100).NotNullable()
            .WithColumn(nameof(EFilingOrderRecord.CaseNumber)).AsString(100).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.CaseTitle)).AsString(1000).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.CaseDocketId)).AsString(100).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.FilingStatus)).AsString(50).NotNullable().WithDefaultValue("RECEIVED_UNDER_REVIEW")
            .WithColumn(nameof(EFilingOrderRecord.FilingType)).AsString(20).NotNullable().WithDefaultValue("Initial")
            .WithColumn(nameof(EFilingOrderRecord.CaseCategoryText)).AsString(500).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.CaseTypeText)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.SubmissionJson)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.NfrcCount)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(EFilingOrderRecord.LastNfrcDateUtc)).AsDateTime2().Nullable()
            .WithColumn(nameof(EFilingOrderRecord.ErrorText)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.ReceiptUrl)).AsString(2000).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.NotificationEmails)).AsString(2000).Nullable()
            .WithColumn(nameof(EFilingOrderRecord.CreatedUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(EFilingOrderRecord.UpdatedUtc)).AsDateTime2().NotNullable();
    }
}
