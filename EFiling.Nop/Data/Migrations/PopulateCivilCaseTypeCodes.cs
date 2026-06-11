using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Data migration: populates CivilCaseTypeCodesJson for existing court records
/// that were seeded before the column was added.
/// </summary>
[NopMigration("2026/02/26 00:01:00", "EFiling. Populate CivilCaseTypeCodesJson for existing courts", MigrationProcessType.Update)]
public class PopulateCivilCaseTypeCodes : Migration
{
    public override void Up()
    {
        // Madera: Civil Unlimited (411110) + Civil Limited (421110)
        Execute.Sql(
            """UPDATE CourtConfigurationRecord SET CivilCaseTypeCodesJson = '["411110","421110"]' WHERE CourtId = 'madera' AND CivilCaseTypeCodesJson = '[]'""");

        // LASC placeholder: Civil Unlimited (CU) + Civil Limited (LC)
        Execute.Sql(
            """UPDATE CourtConfigurationRecord SET CivilCaseTypeCodesJson = '["CU","LC"]' WHERE CourtId = 'lasc' AND CivilCaseTypeCodesJson = '[]'""");
    }

    public override void Down()
    {
        Execute.Sql(
            """UPDATE CourtConfigurationRecord SET CivilCaseTypeCodesJson = '[]' WHERE CourtId IN ('madera', 'lasc')""");
    }
}
