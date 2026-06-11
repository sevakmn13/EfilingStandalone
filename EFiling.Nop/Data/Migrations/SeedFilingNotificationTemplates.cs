using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Seeds the three filing status notification email templates into the MessageTemplate table.
/// Templates: EFiling.FilingAccepted.Customer, EFiling.FilingRejected.Customer, EFiling.FilingPartiallyAccepted.Customer
/// </summary>
[NopMigration("2026/03/21 00:02:00", "EFiling. Seed filing status notification email templates", MigrationProcessType.Update)]
public class SeedFilingNotificationTemplates : ForwardOnlyMigration
{
    public override void Up()
    {
        // ── Accepted ──
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [MessageTemplate] WHERE [Name] = 'EFiling.FilingAccepted.Customer')
INSERT INTO [MessageTemplate] ([Name],[BccEmailAddresses],[Subject],[Body],[IsActive],[DelayBeforeSend],[DelayPeriodId],[AttachedDownloadId],[AllowDirectReply],[EmailAccountId],[LimitedToStores])
VALUES (
    'EFiling.FilingAccepted.Customer',
    '',
    'Your court filing has been accepted — Order #%EFiling.OrderNumber%',
    '<p>Dear %Customer.FullName%,</p>
<p>Your court filing (Order #%EFiling.OrderNumber%) has been <strong>accepted</strong> by the court.</p>
<table style=""border-collapse:collapse;width:100%;max-width:500px;"">
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Status</td><td style=""padding:6px 12px;"">%EFiling.FilingStatus%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Court</td><td style=""padding:6px 12px;"">%EFiling.CourtName%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Case Number</td><td style=""padding:6px 12px;"">%EFiling.CaseNumber%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Filing Type</td><td style=""padding:6px 12px;"">%EFiling.FilingType%</td></tr>
</table>
<p>You can view the full details on your <a href=""%EFiling.OrderDetailsUrl%"">order details page</a>.</p>
<p>Thank you for using %Store.Name%.</p>',
    1, NULL, 0, 0, 0, 0, 0
);");

        // ── Rejected ──
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [MessageTemplate] WHERE [Name] = 'EFiling.FilingRejected.Customer')
INSERT INTO [MessageTemplate] ([Name],[BccEmailAddresses],[Subject],[Body],[IsActive],[DelayBeforeSend],[DelayPeriodId],[AttachedDownloadId],[AllowDirectReply],[EmailAccountId],[LimitedToStores])
VALUES (
    'EFiling.FilingRejected.Customer',
    '',
    'Your court filing has been rejected — Order #%EFiling.OrderNumber%',
    '<p>Dear %Customer.FullName%,</p>
<p>Unfortunately, your court filing (Order #%EFiling.OrderNumber%) has been <strong>rejected</strong> by the court.</p>
<table style=""border-collapse:collapse;width:100%;max-width:500px;"">
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Status</td><td style=""padding:6px 12px;"">%EFiling.FilingStatus%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Court</td><td style=""padding:6px 12px;"">%EFiling.CourtName%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Filing Type</td><td style=""padding:6px 12px;"">%EFiling.FilingType%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Reason</td><td style=""padding:6px 12px;"">%EFiling.ErrorText%</td></tr>
</table>
<p>You may correct the issues and refile from your <a href=""%EFiling.OrderDetailsUrl%"">order details page</a>.</p>
<p>Thank you for using %Store.Name%.</p>',
    1, NULL, 0, 0, 0, 0, 0
);");

        // ── Partially Accepted ──
        Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [MessageTemplate] WHERE [Name] = 'EFiling.FilingPartiallyAccepted.Customer')
INSERT INTO [MessageTemplate] ([Name],[BccEmailAddresses],[Subject],[Body],[IsActive],[DelayBeforeSend],[DelayPeriodId],[AttachedDownloadId],[AllowDirectReply],[EmailAccountId],[LimitedToStores])
VALUES (
    'EFiling.FilingPartiallyAccepted.Customer',
    '',
    'Your court filing has been partially accepted — Order #%EFiling.OrderNumber%',
    '<p>Dear %Customer.FullName%,</p>
<p>Your court filing (Order #%EFiling.OrderNumber%) has been <strong>partially accepted</strong> by the court. Some documents were accepted, but others were rejected.</p>
<table style=""border-collapse:collapse;width:100%;max-width:500px;"">
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Status</td><td style=""padding:6px 12px;"">%EFiling.FilingStatus%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Court</td><td style=""padding:6px 12px;"">%EFiling.CourtName%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Case Number</td><td style=""padding:6px 12px;"">%EFiling.CaseNumber%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Filing Type</td><td style=""padding:6px 12px;"">%EFiling.FilingType%</td></tr>
  <tr><td style=""padding:6px 12px;font-weight:bold;"">Details</td><td style=""padding:6px 12px;"">%EFiling.ErrorText%</td></tr>
</table>
<p>Please review the rejected documents and refile them from your <a href=""%EFiling.OrderDetailsUrl%"">order details page</a>.</p>
<p>Thank you for using %Store.Name%.</p>',
    1, NULL, 0, 0, 0, 0, 0
);");
    }
}
