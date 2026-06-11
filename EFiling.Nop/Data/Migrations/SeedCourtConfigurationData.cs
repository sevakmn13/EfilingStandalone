using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Seeds initial court configuration data into the CourtConfiguration table.
/// Uses raw SQL to avoid DI dependency (migrations run early in the pipeline).
/// </summary>
[NopMigration("2026/02/22 00:03:00", "EFiling. Seed initial court configurations", MigrationProcessType.Installation)]
public class SeedCourtConfigurationData : Migration
{
    public override void Up()
    {
        // Only insert if table is empty (idempotent)
        Execute.Sql("""
            IF NOT EXISTS (SELECT 1 FROM CourtConfigurationRecord)
            BEGIN
                INSERT INTO CourtConfigurationRecord
                    (CourtId, DisplayName, CountyName, ProviderType, Environment,
                     SoapEndpoint, RestBaseUrl, CourtRecordEndpoint, NfrcCallbackUrl,
                     Username, EncryptedPassword, IsActive,
                     CivilCaseTypeCodesJson, ExtraFlagsJson,
                     CreatedUtc, UpdatedUtc)
                VALUES
                    ('madera', 'Madera Superior Court', 'Madera', 'JTI', 'Staging',
                     'https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/',
                     'https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt',
                     'https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/',
                     'https://overindulgent-finnicky-lavette.ngrok-free.dev/api/efiling/nfrc',
                     'legalhub', 'W3ZhPDg8akM1MFkw', 1,
                     '["411110","421110"]', '{"supportsConditionalSeal":"true"}',
                     GETUTCDATE(), GETUTCDATE());
            END
            """);
    }

    public override void Down()
    {
        Execute.Sql("DELETE FROM CourtConfigurationRecord WHERE CourtId = 'madera'");
    }
}
