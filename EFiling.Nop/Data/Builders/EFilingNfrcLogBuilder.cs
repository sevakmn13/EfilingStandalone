using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="EFilingNfrcLog"/>.
/// Maps columns for the EFilingNfrcLog table via nopCommerce's FluentMigrator integration.
/// </summary>
public class EFilingNfrcLogBuilder : NopEntityBuilder<EFilingNfrcLog>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            // Phase 0 (NFRC audit): nullable so unmatched callbacks can be persisted.
            .WithColumn(nameof(EFilingNfrcLog.EFilingOrderRecordId)).AsInt32().Nullable().Indexed("IX_EFilingNfrcLog_OrderRecordId")
            .WithColumn(nameof(EFilingNfrcLog.NfrcNumber)).AsInt32().NotNullable()
            .WithColumn(nameof(EFilingNfrcLog.RawXml)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(EFilingNfrcLog.ReceivedUtc)).AsDateTime2().NotNullable()
            // Phase 0 forensic columns — see NfrcCallbackTriage.MatchResult for canonical values.
            .WithColumn(nameof(EFilingNfrcLog.MatchAttemptResult)).AsString(50).Nullable().Indexed("IX_EFilingNfrcLog_MatchAttemptResult")
            .WithColumn(nameof(EFilingNfrcLog.EfspReferenceId)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingNfrcLog.EfmReferenceId)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingNfrcLog.ReceivedFromIp)).AsString(64).Nullable()
            .WithColumn(nameof(EFilingNfrcLog.ContentType)).AsString(200).Nullable()
            .WithColumn(nameof(EFilingNfrcLog.RawXmlLength)).AsInt32().NotNullable().WithDefaultValue(0);
    }
}
