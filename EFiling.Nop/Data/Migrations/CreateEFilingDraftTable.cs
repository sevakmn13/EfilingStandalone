using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Creates the EFilingDraft table using nopCommerce's entity builder pattern.
/// Column definitions come from <see cref="Builders.EFilingDraftBuilder"/>.
/// </summary>
[NopMigration("2026/02/22 00:01:00", "EFiling. Create EFilingDraft table", MigrationProcessType.Installation)]
public class CreateEFilingDraftTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.TableFor<EFilingDraft>();
    }
}
