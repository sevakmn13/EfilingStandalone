using FluentMigrator;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Adds DocumentDescription + IsCourtGenerated to EFilingDocumentRecord,
/// and CaseCategoryText + CaseTypeText to EFilingOrderRecord.
/// </summary>
[NopMigration("2026/03/22 12:00:00", "EFiling. Add document description, court-generated flag, and case info columns", MigrationProcessType.Update)]
public class AddDocumentAndCaseInfoColumns : AutoReversingMigration
{
    public override void Up()
    {
        // EFilingDocumentRecord — new columns
        if (!Schema.Table(nameof(EFilingDocumentRecord)).Column(nameof(EFilingDocumentRecord.DocumentDescription)).Exists())
        {
            Alter.Table(nameof(EFilingDocumentRecord))
                .AddColumn(nameof(EFilingDocumentRecord.DocumentDescription)).AsString(500).Nullable();
        }

        if (!Schema.Table(nameof(EFilingDocumentRecord)).Column(nameof(EFilingDocumentRecord.IsCourtGenerated)).Exists())
        {
            Alter.Table(nameof(EFilingDocumentRecord))
                .AddColumn(nameof(EFilingDocumentRecord.IsCourtGenerated)).AsBoolean().NotNullable().WithDefaultValue(false);
        }

        // EFilingOrderRecord — new columns
        if (!Schema.Table(nameof(EFilingOrderRecord)).Column(nameof(EFilingOrderRecord.CaseCategoryText)).Exists())
        {
            Alter.Table(nameof(EFilingOrderRecord))
                .AddColumn(nameof(EFilingOrderRecord.CaseCategoryText)).AsString(500).Nullable();
        }

        if (!Schema.Table(nameof(EFilingOrderRecord)).Column(nameof(EFilingOrderRecord.CaseTypeText)).Exists())
        {
            Alter.Table(nameof(EFilingOrderRecord))
                .AddColumn(nameof(EFilingOrderRecord.CaseTypeText)).AsString(200).Nullable();
        }
    }
}
