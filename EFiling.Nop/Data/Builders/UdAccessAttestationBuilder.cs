using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Data.Builders;

/// <summary>
/// Entity builder for <see cref="UdAccessAttestation"/>.
/// Maps columns for the UdAccessAttestation table via nopCommerce's
/// FluentMigrator integration. Step #43 — implements UD-2
/// audit-capture mandate from JTI EFM doc node/436#UnlawfulDetainer.
/// </summary>
public class UdAccessAttestationBuilder : NopEntityBuilder<UdAccessAttestation>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(UdAccessAttestation.CustomerId)).AsInt32().NotNullable()
                .Indexed("IX_UdAccessAttestation_CustomerId")
            .WithColumn(nameof(UdAccessAttestation.CourtId)).AsString(100).NotNullable()
            .WithColumn(nameof(UdAccessAttestation.CaseDocketId)).AsString(100).NotNullable()
                .Indexed("IX_UdAccessAttestation_CaseDocketId")
            .WithColumn(nameof(UdAccessAttestation.CaseCategoryCode)).AsString(50).Nullable()
            .WithColumn(nameof(UdAccessAttestation.AttestedAsParty)).AsBoolean().NotNullable()
            .WithColumn(nameof(UdAccessAttestation.AttestedUtc)).AsDateTime2().NotNullable()
                .Indexed("IX_UdAccessAttestation_AttestedUtc")
            .WithColumn(nameof(UdAccessAttestation.DisclaimerTextShown)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(UdAccessAttestation.IpAddress)).AsString(64).Nullable();
    }
}
