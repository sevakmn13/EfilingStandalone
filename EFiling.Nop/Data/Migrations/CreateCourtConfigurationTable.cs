using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Creates the CourtConfiguration table using nopCommerce's entity builder pattern.
/// Column definitions come from <see cref="Builders.CourtConfigurationRecordBuilder"/>.
/// </summary>
[NopMigration("2026/02/22 00:02:00", "EFiling. Create CourtConfiguration table", MigrationProcessType.Installation)]
public class CreateCourtConfigurationTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.TableFor<CourtConfigurationRecord>();
    }
}
