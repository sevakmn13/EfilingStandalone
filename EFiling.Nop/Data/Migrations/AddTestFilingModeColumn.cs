using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Adds the TestFilingMode column to the CourtConfigurationRecord table.
/// 0=None, 1=AutoAccept, 2=AutoReject. Defaults to 0 (None).
/// </summary>
[NopMigration("2026/03/21 00:01:00", "EFiling. Add TestFilingMode column to CourtConfigurationRecord", MigrationProcessType.Update)]
public class AddTestFilingModeColumn : AutoReversingMigration
{
    public override void Up()
    {
        if (!Schema.Table("CourtConfigurationRecord").Column("TestFilingMode").Exists())
        {
            Alter.Table("CourtConfigurationRecord")
                .AddColumn("TestFilingMode").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
