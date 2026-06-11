using EFiling.Core.Caching;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for Phase 7: GetChargedAmount and advanced fee handling.
/// </summary>
public class Phase7FeeTests
{
    // ─── GetChargedAmount Request Builder ──────────────────────────

    [Fact]
    public void BuildGetChargedAmountRequest_ContainsEfmReferenceId()
    {
        var xml = SoapEnvelopeBuilder.BuildGetChargedAmountRequest("12345");

        Assert.Contains("EfmReferenceId", xml);
        Assert.Contains("12345", xml);
        Assert.Contains(SoapEnvelopeBuilder.NsJtiEfmFilingRef, xml);
    }

    // ─── GetChargedAmount Response Parsing (reuses FeesCalc parser) ─

    [Fact]
    public void ParseChargedAmount_Success_ReturnsFees()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns13:FeesCalculationType xmlns:ns13=""{SoapEnvelopeBuilder.NsJtiFeesCalcExt}""
                               xmlns:ns11=""{SoapEnvelopeBuilder.NsUblCac}""
                               xmlns:ns7=""{SoapEnvelopeBuilder.NsUblCbc}"">
      <ns13:FeesCalculationAmount>75.00</ns13:FeesCalculationAmount>
      <ns11:AllowanceCharge>
        <ns7:Amount>50.00</ns7:Amount>
        <ns7:AccountingCostCode>COURT</ns7:AccountingCostCode>
      </ns11:AllowanceCharge>
      <ns11:AllowanceCharge>
        <ns7:Amount>25.00</ns7:Amount>
        <ns7:AccountingCostCode>JOURNAL</ns7:AccountingCostCode>
      </ns11:AllowanceCharge>
    </ns13:FeesCalculationType>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(0, result.ErrorCode);
        Assert.Equal(75.00m, result.TotalAmount);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal("COURT", result.LineItems[0].AccountingCostCode);
        Assert.Equal(50.00m, result.LineItems[0].Amount);
        Assert.Equal("JOURNAL", result.LineItems[1].AccountingCostCode);
        Assert.Equal(25.00m, result.LineItems[1].Amount);
    }

    [Fact]
    public void ParseChargedAmount_WithExemption_ReturnsExemptionType()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns13:FeesCalculationType xmlns:ns13=""{SoapEnvelopeBuilder.NsJtiFeesCalcExt}"">
      <ns13:FeesCalculationAmount>0.00</ns13:FeesCalculationAmount>
      <ns13:Exemption>WAIVED</ns13:Exemption>
    </ns13:FeesCalculationType>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(0.00m, result.TotalAmount);
        Assert.Equal("WAIVED", result.ExemptionType);
    }

    [Fact]
    public void ParseChargedAmount_GovtExemption_ReturnsExempted()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns13:FeesCalculationType xmlns:ns13=""{SoapEnvelopeBuilder.NsJtiFeesCalcExt}"">
      <ns13:FeesCalculationAmount>0.00</ns13:FeesCalculationAmount>
      <ns13:Exemption>EXEMPTED</ns13:Exemption>
    </ns13:FeesCalculationType>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(0.00m, result.TotalAmount);
        Assert.Equal("EXEMPTED", result.ExemptionType);
    }

    [Fact]
    public void ParseChargedAmount_WithError_ReturnsErrorCode()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns13:FeesCalculationType xmlns:ns13=""{SoapEnvelopeBuilder.NsJtiFeesCalcExt}""
                               xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns13:FeesCalculationAmount>0</ns13:FeesCalculationAmount>
      <ns2:Error>
        <ns2:ErrorCode>4112</ns2:ErrorCode>
        <ns2:ErrorText>Filing not found</ns2:ErrorText>
      </ns2:Error>
    </ns13:FeesCalculationType>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(4112, result.ErrorCode);
        Assert.Contains("Filing not found", result.ErrorText);
    }

    [Fact]
    public void ParseChargedAmount_SoapFault_ReturnsError()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Invalid reference ID</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(-1, result.ErrorCode);
        Assert.Contains("Invalid reference ID", result.ErrorText);
    }

    // ─── Fee Waiver / Govt Exemption in Filing ────────────────────

    [Fact]
    public void FeeWaiverParty_HasFeeExemptionRequestType()
    {
        // Verify the model supports fee waiver
        var party = new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "PLAIN",
            FeeExemptionRequestType = "FEE_WAIVER",
            FirstName = "John",
            LastName = "Smith"
        };
        Assert.Equal("FEE_WAIVER", party.FeeExemptionRequestType);
    }

    [Fact]
    public void GovtExemptParty_HasGovtEntityExemption()
    {
        var party = new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "PLAIN",
            IsOrganization = true,
            OrganizationName = "County of Madera",
            FeeExemptionRequestType = "GOVT_ENTITY"
        };
        Assert.Equal("GOVT_ENTITY", party.FeeExemptionRequestType);
    }

    [Fact]
    public void FeeCalculation_ExemptionType_SupportsWaivedAndExempted()
    {
        var calc1 = new FeeCalculation { ExemptionType = "WAIVED", TotalAmount = 0 };
        var calc2 = new FeeCalculation { ExemptionType = "EXEMPTED", TotalAmount = 0 };

        Assert.Equal("WAIVED", calc1.ExemptionType);
        Assert.Equal("EXEMPTED", calc2.ExemptionType);
    }
}

/// <summary>
/// Integration test for GetChargedAmount against live Madera staging.
/// </summary>
[Trait("Category", "Integration")]
public class FeeIntegrationTests
{
    [Fact]
    public async Task GetChargedAmountAsync_LiveMadera_InvalidId_ReturnsResponse()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // Invalid ID should return an error, not crash
        var result = await provider.GetChargedAmountAsync(config, "99999999");

        Assert.NotNull(result);
        Console.WriteLine($"GetChargedAmount: total={result.TotalAmount}, error={result.ErrorCode}: {result.ErrorText}");
        Console.WriteLine($"  Exemption: {result.ExemptionType ?? "(none)"}");
        Console.WriteLine($"  Line items: {result.LineItems.Count}");
    }
}
