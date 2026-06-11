using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Creates the UdAccessAttestation table using nopCommerce's entity builder
/// pattern. Column definitions come from
/// <see cref="Builders.UdAccessAttestationBuilder"/>.
///
/// Step #43 — implements UD-2 ("Access Tracking and Data
/// Capture") audit-storage mandate from JTI EFM vendor doc
/// node/436#UnlawfulDetainer. See
/// <c>docs/JTI_SUBSEQUENT_FILING_CATALOG.md §5.6.2</c> for the verbatim
/// requirement.
/// </summary>
[NopMigration("2026/05/21 12:00:00", "EFiling. Create UdAccessAttestation table", MigrationProcessType.Installation)]
public class CreateUdAccessAttestationTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.TableFor<UdAccessAttestation>();
    }
}
