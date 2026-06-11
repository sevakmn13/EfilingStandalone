using FluentMigrator;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Adds DocumentDispositionDate column to EFilingDocumentRecord (Q22-B fix — Phase 5.7
/// of NFRC audit). Captures the judicial-disposition timestamp from NFRC #3 callbacks
/// per WSDL ReviewedDocumentTypeExt at FilingReviewMDEPort.wsdl:9315 (schema type
/// nc:DateType). Pre-fix the field was silently dropped on the floor; this column closes
/// the data-loss surface so when any JTI court (Madera production or otherwise) emits
/// NFRC #3 with the disposition date populated, the value is persisted alongside the
/// existing DocumentDispositionType field.
/// </summary>
[NopMigration("2026/05/16 21:42:00", "EFiling. Add DocumentDispositionDate column to EFilingDocumentRecord (Q22-B)", MigrationProcessType.Update)]
public class AddDocumentDispositionDateColumn : AutoReversingMigration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(EFilingDocumentRecord)).Column(nameof(EFilingDocumentRecord.DocumentDispositionDate)).Exists())
        {
            Alter.Table(nameof(EFilingDocumentRecord))
                .AddColumn(nameof(EFilingDocumentRecord.DocumentDispositionDate)).AsDateTime2().Nullable();
        }
    }
}
