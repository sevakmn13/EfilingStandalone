using FluentMigrator;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Adds NotificationEmails column to EFilingOrderRecord table.
/// </summary>
[NopMigration("2026/03/21 00:01:00", "EFiling. Add NotificationEmails column to EFilingOrderRecord", MigrationProcessType.Update)]
public class AddNotificationEmailsColumn : AutoReversingMigration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(EFilingOrderRecord)).Column(nameof(EFilingOrderRecord.NotificationEmails)).Exists())
        {
            Alter.Table(nameof(EFilingOrderRecord))
                .AddColumn(nameof(EFilingOrderRecord.NotificationEmails)).AsString(2000).Nullable();
        }
    }
}
