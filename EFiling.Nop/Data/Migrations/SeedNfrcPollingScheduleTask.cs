using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Seeds the NfrcPollingTask schedule task into nopCommerce's ScheduleTask table.
/// Runs every 5 minutes to poll JTI for status updates on filings still under review.
/// </summary>
[NopMigration("2026/03/20 00:01:00", "EFiling. Seed NFRC Polling schedule task", MigrationProcessType.Update)]
public class SeedNfrcPollingScheduleTask : Migration
{
    private const string TaskType = "EFiling.Nop.ScheduleTasks.NfrcPollingTask, EFiling.Nop";

    public override void Up()
    {
        Execute.Sql($"""
            IF NOT EXISTS (SELECT 1 FROM ScheduleTask WHERE [Type] = '{TaskType}')
            BEGIN
                INSERT INTO ScheduleTask ([Name], [Seconds], [Type], [Enabled], [LastEnabledUtc], [StopOnError])
                VALUES ('E-Filing NFRC Polling', 300, '{TaskType}', 1, GETUTCDATE(), 0)
            END
        """);
    }

    public override void Down()
    {
        Execute.Sql($"DELETE FROM ScheduleTask WHERE [Type] = '{TaskType}'");
    }
}
