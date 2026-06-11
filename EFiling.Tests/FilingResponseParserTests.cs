using EFiling.Core.Enums;
using EFiling.Providers.JTI.Parsers;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for FilingResponseParser — verifies parsing of
/// MessageReceiptMessage and FeesCalculationResponse XML.
/// </summary>
public class FilingResponseParserTests
{
    // ─── MessageReceipt Tests ────────────────────────────────────────

    [Fact]
    public void ParseMessageReceipt_Success_ExtractsEfmReferenceId()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns7:MessageReceiptMessage xmlns:ns7=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0""
                                xmlns:ns1=""http://niem.gov/niem/niem-core/2.0""
                                xmlns:ns2=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>EFM-12345</ns1:IdentificationID>
      </ns1:DocumentIdentification>
      <ns1:DocumentFileControlID>EFSP-REF-001</ns1:DocumentFileControlID>
    </ns7:MessageReceiptMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseMessageReceipt(xml);

        Assert.True(result.Success);
        Assert.Equal("EFM-12345", result.EfmReferenceId);
        Assert.Equal("EFSP-REF-001", result.EfspReferenceId);
        Assert.Equal(FilingStatus.ReceivedUnderReview, result.Status);
    }

    [Fact]
    public void ParseMessageReceipt_WithError_ReturnsFailed()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns7:MessageReceiptMessage xmlns:ns7=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0""
                                xmlns:ns2=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
      <ns2:Error>
        <ns2:ErrorCode>100</ns2:ErrorCode>
        <ns2:ErrorText>Invalid case category</ns2:ErrorText>
      </ns2:Error>
    </ns7:MessageReceiptMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseMessageReceipt(xml);

        Assert.False(result.Success);
        Assert.Equal(100, result.ErrorCode);
        Assert.Equal("Invalid case category", result.ErrorText);
    }

    [Fact]
    public void ParseMessageReceipt_SoapFault_ReturnsFailed()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultcode>Server</faultcode>
      <faultstring>Internal server error</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseMessageReceipt(xml);

        Assert.False(result.Success);
        Assert.Contains("Internal server error", result.ErrorText);
    }

    [Fact]
    public void ParseMessageReceipt_NoReceiptElement_ReturnsFailed()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <SomethingElse/>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseMessageReceipt(xml);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorText);
    }

    [Fact]
    public void ParseMessageReceipt_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => FilingResponseParser.ParseMessageReceipt(""));
    }

    [Fact]
    public void ParseMessageReceipt_ErrorCodeZero_IsSuccess()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns7:MessageReceiptMessage xmlns:ns7=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0""
                                xmlns:ns1=""http://niem.gov/niem/niem-core/2.0""
                                xmlns:ns2=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>EFM-99</ns1:IdentificationID>
      </ns1:DocumentIdentification>
      <ns2:Error>
        <ns2:ErrorCode>0</ns2:ErrorCode>
        <ns2:ErrorText>Success</ns2:ErrorText>
      </ns2:Error>
    </ns7:MessageReceiptMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseMessageReceipt(xml);

        Assert.True(result.Success);
        Assert.Equal("EFM-99", result.EfmReferenceId);
    }

    // ─── FeesCalculation Tests ───────────────────────────────────────

    [Fact]
    public void ParseFeesCalc_Success_ExtractsTotalAndLineItems()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns19:FeesCalculationResponseMessage xmlns:ns19=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationResponseMessage-4.0"">
      <ns13:FeesCalculationType xmlns:ns13=""urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt"">
        <ns13:FeesCalculationAmount>435.00</ns13:FeesCalculationAmount>
        <cac:AllowanceCharge xmlns:cac=""urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"">
          <cbc:Amount xmlns:cbc=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">395.00</cbc:Amount>
          <cac:AccountingCostCode>COURT</cac:AccountingCostCode>
          <cbc:AllowanceChargeReason xmlns:cbc=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">First Paper Filing Fee</cbc:AllowanceChargeReason>
        </cac:AllowanceCharge>
        <cac:AllowanceCharge xmlns:cac=""urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"">
          <cbc:Amount xmlns:cbc=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">40.00</cbc:Amount>
          <cac:AccountingCostCode>JOURNAL</cac:AccountingCostCode>
          <cbc:AllowanceChargeReason xmlns:cbc=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">EFSP Convenience Fee</cbc:AllowanceChargeReason>
        </cac:AllowanceCharge>
      </ns13:FeesCalculationType>
    </ns19:FeesCalculationResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(0, result.ErrorCode);
        Assert.Null(result.ErrorText);
        Assert.Equal(435.00m, result.TotalAmount);
        Assert.Equal(2, result.LineItems.Count);

        Assert.Equal(395.00m, result.LineItems[0].Amount);
        Assert.Equal("COURT", result.LineItems[0].AccountingCostCode);
        Assert.Equal("First Paper Filing Fee", result.LineItems[0].Description);

        Assert.Equal(40.00m, result.LineItems[1].Amount);
        Assert.Equal("JOURNAL", result.LineItems[1].AccountingCostCode);
    }

    [Fact]
    public void ParseFeesCalc_WithExemption_ExtractsExemptionType()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns19:FeesCalculationResponseMessage xmlns:ns19=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationResponseMessage-4.0"">
      <ns13:FeesCalculationType xmlns:ns13=""urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt"">
        <ns13:FeesCalculationAmount>0.00</ns13:FeesCalculationAmount>
        <ns13:Exemption>WAIVED</ns13:Exemption>
      </ns13:FeesCalculationType>
    </ns19:FeesCalculationResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(0.00m, result.TotalAmount);
        Assert.Equal("WAIVED", result.ExemptionType);
    }

    [Fact]
    public void ParseFeesCalc_WithError_ReturnsError()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <ns19:FeesCalculationResponseMessage xmlns:ns19=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationResponseMessage-4.0"">
      <ns13:FeesCalculationType xmlns:ns13=""urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt"">
        <ns13:FeesCalculationAmount>0</ns13:FeesCalculationAmount>
        <ns2:Error xmlns:ns2=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
          <ns2:ErrorCode>500</ns2:ErrorCode>
          <ns2:ErrorText>Invalid document type for case category</ns2:ErrorText>
        </ns2:Error>
      </ns13:FeesCalculationType>
    </ns19:FeesCalculationResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(500, result.ErrorCode);
        Assert.Equal("Invalid document type for case category", result.ErrorText);
    }

    [Fact]
    public void ParseFeesCalc_SoapFault_ReturnsError()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Service unavailable</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFeesCalculationResponse(xml);

        Assert.Equal(-1, result.ErrorCode);
        Assert.Contains("Service unavailable", result.ErrorText);
    }

    [Fact]
    public void ParseFeesCalc_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => FilingResponseParser.ParseFeesCalculationResponse(""));
    }
}
