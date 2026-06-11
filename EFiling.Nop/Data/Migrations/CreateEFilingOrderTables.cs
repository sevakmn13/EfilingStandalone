using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Creates the four Step 7/8 tables: EFilingOrderRecord, EFilingDocumentRecord,
/// EFilingFeeRecord, EFilingNfrcLog. Column definitions come from their respective builders.
/// </summary>
[NopMigration("2026/03/19 00:01:00", "EFiling. Create order tracking tables (OrderRecord, DocumentRecord, FeeRecord, NfrcLog)", MigrationProcessType.Installation)]
public class CreateEFilingOrderTables : AutoReversingMigration
{
    public override void Up()
    {
        Create.TableFor<EFilingOrderRecord>();
        Create.TableFor<EFilingDocumentRecord>();
        Create.TableFor<EFilingFeeRecord>();
        Create.TableFor<EFilingNfrcLog>();
    }
}
