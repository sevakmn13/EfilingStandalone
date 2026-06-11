using FluentMigrator;
using Nop.Data.Migrations;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Phase 0 of the NFRC audit: persist unmatched JTI callbacks instead of silently dropping them.
///
/// <para>Schema changes on <see cref="EFilingNfrcLog"/>:</para>
/// <list type="bullet">
///   <item>Make <c>EFilingOrderRecordId</c> nullable (was <c>NOT NULL</c>) so unmatched callbacks can be persisted with no FK.</item>
///   <item>Add <c>MatchAttemptResult</c> (categorized triage outcome) + index for support queries.</item>
///   <item>Add <c>EfspReferenceId</c> + <c>EfmReferenceId</c> snapshots (preserved even when no order matches).</item>
///   <item>Add <c>ReceivedFromIp</c> + <c>ContentType</c> + <c>RawXmlLength</c> diagnostic fields.</item>
/// </list>
///
/// <para>Backfill: legacy rows (created before Phase 0) all had a non-null FK and a successful parse,
/// so their <c>MatchAttemptResult</c> is set to <c>"Matched"</c>.</para>
///
/// <para>This migration uses an explicit <see cref="Down"/> because <see cref="AlterColumnExpressionBuilder"/>
/// changes are not auto-reversible — FluentMigrator can't infer the original column type.</para>
/// </summary>
[NopMigration("2026/04/27 00:01:00", "EFiling. Phase 0 — make EFilingNfrcLog support unmatched callbacks", MigrationProcessType.Update)]
public class AddNfrcLogUnmatchedColumns : Migration
{
    public override void Up()
    {
        // 1. Add new columns (idempotent existence checks).
        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.MatchAttemptResult)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.MatchAttemptResult)).AsString(50).Nullable();

            // Index for support-tooling queries: WHERE MatchAttemptResult LIKE 'Unmatched_%'
            Create.Index("IX_EFilingNfrcLog_MatchAttemptResult")
                .OnTable(nameof(EFilingNfrcLog))
                .OnColumn(nameof(EFilingNfrcLog.MatchAttemptResult)).Ascending();
        }

        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.EfspReferenceId)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.EfspReferenceId)).AsString(200).Nullable();
        }

        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.EfmReferenceId)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.EfmReferenceId)).AsString(200).Nullable();
        }

        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.ReceivedFromIp)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.ReceivedFromIp)).AsString(64).Nullable();
        }

        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.ContentType)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.ContentType)).AsString(200).Nullable();
        }

        if (!Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.RawXmlLength)).Exists())
        {
            Alter.Table(nameof(EFilingNfrcLog))
                .AddColumn(nameof(EFilingNfrcLog.RawXmlLength)).AsInt32().NotNullable().WithDefaultValue(0);
        }

        // 2. Backfill MatchAttemptResult for legacy rows. All pre-Phase-0 rows had a non-null FK
        //    and were stored only after a successful match → tag them as "Matched".
        Execute.Sql(
            $"UPDATE [{nameof(EFilingNfrcLog)}] " +
            $"SET [{nameof(EFilingNfrcLog.MatchAttemptResult)}] = 'Matched', " +
            $"    [{nameof(EFilingNfrcLog.RawXmlLength)}] = LEN([{nameof(EFilingNfrcLog.RawXml)}]) " +
            $"WHERE [{nameof(EFilingNfrcLog.MatchAttemptResult)}] IS NULL");

        // 3. Make EFilingOrderRecordId nullable. Done last so the backfill above
        //    runs against the original NOT NULL state (no risk of orphan rows mid-migration).
        Alter.Column(nameof(EFilingNfrcLog.EFilingOrderRecordId))
            .OnTable(nameof(EFilingNfrcLog))
            .AsInt32().Nullable();
    }

    public override void Down()
    {
        // Reverse order of Up() to keep the schema consistent at every step.

        // 1. Restore NOT NULL on EFilingOrderRecordId. This will FAIL if any unmatched
        //    rows exist — that is intentional: a downgrade should not silently destroy
        //    forensic data. Operators must purge unmatched rows manually before downgrading.
        Alter.Column(nameof(EFilingNfrcLog.EFilingOrderRecordId))
            .OnTable(nameof(EFilingNfrcLog))
            .AsInt32().NotNullable();

        // 2. Drop forensic columns + index in reverse order.
        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.RawXmlLength)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.RawXmlLength)).FromTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.ContentType)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.ContentType)).FromTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.ReceivedFromIp)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.ReceivedFromIp)).FromTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.EfmReferenceId)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.EfmReferenceId)).FromTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.EfspReferenceId)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.EfspReferenceId)).FromTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Index("IX_EFilingNfrcLog_MatchAttemptResult").Exists())
            Delete.Index("IX_EFilingNfrcLog_MatchAttemptResult").OnTable(nameof(EFilingNfrcLog));

        if (Schema.Table(nameof(EFilingNfrcLog)).Column(nameof(EFilingNfrcLog.MatchAttemptResult)).Exists())
            Delete.Column(nameof(EFilingNfrcLog.MatchAttemptResult)).FromTable(nameof(EFilingNfrcLog));
    }
}
