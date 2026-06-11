using FluentMigrator;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Adds BlobUrl column to EFilingDocumentRecord to store the original Azure Blob URL for uploaded documents.
/// </summary>
[NopMigration("2026/03/22 14:50:00", "EFiling. Add BlobUrl column to EFilingDocumentRecord", MigrationProcessType.Update)]
public class AddBlobUrlColumn : AutoReversingMigration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(EFilingDocumentRecord)).Column(nameof(EFilingDocumentRecord.BlobUrl)).Exists())
        {
            Alter.Table(nameof(EFilingDocumentRecord))
                .AddColumn(nameof(EFilingDocumentRecord.BlobUrl)).AsString(2000).Nullable();
        }
    }
}
