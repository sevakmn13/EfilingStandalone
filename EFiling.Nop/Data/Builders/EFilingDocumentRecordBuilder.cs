using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="EFilingDocumentRecord"/>.
/// Maps columns for the EFilingDocumentRecord table via nopCommerce's FluentMigrator integration.
/// </summary>
public class EFilingDocumentRecordBuilder : NopEntityBuilder<EFilingDocumentRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(EFilingDocumentRecord.EFilingOrderRecordId)).AsInt32().NotNullable().Indexed("IX_EFilingDocumentRecord_OrderRecordId")
            .WithColumn(nameof(EFilingDocumentRecord.DocumentReferenceId)).AsString(100).NotNullable()
            .WithColumn(nameof(EFilingDocumentRecord.DocumentCode)).AsString(100).NotNullable()
            .WithColumn(nameof(EFilingDocumentRecord.FileControlId)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.IsLeadDocument)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(EFilingDocumentRecord.DocumentFilingStatusCode)).AsString(50).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.DocumentStatusText)).AsString(10).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.DocumentDispositionType)).AsString(10).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.DocumentDispositionDate)).AsDateTime2().Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.RejectionReasonText)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.ConformedCopyUrl)).AsString(2000).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.CourtDocumentId)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.OriginalFileName)).AsString(500).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.BlobUrl)).AsString(2000).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.DocumentDescription)).AsString(500).Nullable()
            .WithColumn(nameof(EFilingDocumentRecord.IsCourtGenerated)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(EFilingDocumentRecord.CreatedUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(EFilingDocumentRecord.UpdatedUtc)).AsDateTime2().NotNullable();
    }
}
