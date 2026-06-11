using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="EFilingDraft"/>.
/// Maps columns for the EFilingDraft table via nopCommerce's FluentMigrator integration.
/// </summary>
public class EFilingDraftBuilder : NopEntityBuilder<EFilingDraft>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(EFilingDraft.CustomerId)).AsInt32().NotNullable().Indexed("IX_EFilingDraft_CustomerId")
            .WithColumn(nameof(EFilingDraft.CourtId)).AsString(100).NotNullable()
            .WithColumn(nameof(EFilingDraft.CaseDocketId)).AsString(100).Nullable()
            .WithColumn(nameof(EFilingDraft.FilingType)).AsString(20).NotNullable().WithDefaultValue("Initial")
            .WithColumn(nameof(EFilingDraft.SubmissionJson)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(EFilingDraft.DisplayName)).AsString(500).Nullable()
            .WithColumn(nameof(EFilingDraft.SchemaVersion)).AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn(nameof(EFilingDraft.CreatedUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(EFilingDraft.UpdatedUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(EFilingDraft.IsSubmitted)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(EFilingDraft.EfmReferenceId)).AsString(100).Nullable();
    }
}
