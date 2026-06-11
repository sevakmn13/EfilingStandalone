using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

public class PolicyResponseParserTests
{
    [Fact]
    public void Parse_ValidMaderaResponse_ExtractsPolicyVersionId()
    {
        var xml = GetSamplePolicyResponse();
        var policy = PolicyResponseParser.Parse(xml);

        Assert.Equal(41, policy.PolicyVersionId);
    }

    [Fact]
    public void Parse_ValidMaderaResponse_ExtractsPolicyLastUpdateDate()
    {
        var xml = GetSamplePolicyResponse();
        var policy = PolicyResponseParser.Parse(xml);

        Assert.True(policy.PolicyLastUpdateDate > DateTime.MinValue);
    }

    [Fact]
    public void Parse_ValidMaderaResponse_ExtractsCodeListUrls()
    {
        var xml = GetSamplePolicyResponse();
        var policy = PolicyResponseParser.Parse(xml);

        // Should have standard code lists
        Assert.True(policy.CodeListUrls.ContainsKey("CASE_CATEGORY"));
        Assert.True(policy.CodeListUrls.ContainsKey("CASE_TYPE"));
        Assert.True(policy.CodeListUrls.ContainsKey("PARTY_TYPE"));
        Assert.True(policy.CodeListUrls.ContainsKey("ADDRESS_TYPE"));
        Assert.True(policy.CodeListUrls.ContainsKey("US_STATE"));
        Assert.True(policy.CodeListUrls.ContainsKey("COUNTRY"));
        Assert.True(policy.CodeListUrls.ContainsKey("LANGUAGE"));

        // URLs should point to REST endpoints
        Assert.Contains("codeList?type=CASE_TYPE", policy.CodeListUrls["CASE_TYPE"]);
    }

    [Fact]
    public void Parse_ValidMaderaResponse_ExtractsSpecialEndpoints()
    {
        var xml = GetSamplePolicyResponse();
        var policy = PolicyResponseParser.Parse(xml);

        Assert.NotNull(policy.DocumentListUrl);
        Assert.Contains("documentList", policy.DocumentListUrl);

        Assert.NotNull(policy.CourtLocationsUrl);
        Assert.Contains("courtLocations", policy.CourtLocationsUrl);

        Assert.NotNull(policy.AttorneyListUrl);
        Assert.Contains("attorneyList", policy.AttorneyListUrl);
    }

    [Fact]
    public void Parse_ValidMaderaResponse_HasCorrectCodeListCount()
    {
        var xml = GetSamplePolicyResponse();
        var policy = PolicyResponseParser.Parse(xml);

        // Madera has 20 standard code list types
        Assert.True(policy.CodeListUrls.Count >= 15, 
            $"Expected at least 15 code lists, got {policy.CodeListUrls.Count}");
    }

    [Fact]
    public void Parse_ResponseWithError_ThrowsJtiSoapException()
    {
        var xml = @"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <cprm:CourtPolicyResponseMessage xmlns:cprm=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyResponseMessage-4.0""
        xmlns:ct=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"">
      <ct:Error>
        <ct:ErrorCode>100</ct:ErrorCode>
        <ct:ErrorText>Authentication Failed</ct:ErrorText>
      </ct:Error>
    </cprm:CourtPolicyResponseMessage>
  </soap:Body>
</soap:Envelope>";

        var ex = Assert.Throws<JtiSoapException>(() => PolicyResponseParser.Parse(xml));
        Assert.Contains("Authentication Failed", ex.Message);
        Assert.Equal("100", ex.FaultCode);
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PolicyResponseParser.Parse(""));
    }

    [Fact]
    public void Parse_MissingSoapBody_ThrowsJtiSoapException()
    {
        var xml = @"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""></soap:Envelope>";
        Assert.Throws<JtiSoapException>(() => PolicyResponseParser.Parse(xml));
    }

    /// <summary>
    /// A representative sample of the actual Madera court GetPolicy response.
    /// </summary>
    private static string GetSamplePolicyResponse()
    {
        return @"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <cprm:CourtPolicyResponseMessage 
        xmlns:cprm=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyResponseMessage-4.0""
        xmlns:ct=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0""
        xmlns:ore=""http://niem.gov/niem/niem-core/2.0"">
      <ct:SendingMDELocationID>
        <ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com</ore:IdentificationID>
      </ct:SendingMDELocationID>
      <ct:Error>
        <ct:ErrorCode>0</ct:ErrorCode>
        <ct:ErrorText>No Error</ct:ErrorText>
      </ct:Error>
      <cprm:PolicyVersionID>
        <ore:IdentificationID>41</ore:IdentificationID>
      </cprm:PolicyVersionID>
      <cprm:PolicyLastUpdateDate>
        <ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-01-26T08:59:36.947-08:00</ore:DateRepresentation>
      </cprm:PolicyLastUpdateDate>
      <cprm:RuntimePolicyParameters>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.179-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=CASE_CATEGORY</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.180-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=ADDRESS_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.180-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=AKA_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.180-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=ADDRESS_COUNTRY</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.181-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=CASE_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.181-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=DEFAULT_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.181-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=DISMISSAL_PREJUDICE_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.182-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=DISMISSAL_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.182-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=JURISDICTIONAL_AMOUNT</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.183-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=LANGUAGE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.183-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=PARTY_DESIGNATION_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.184-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=PARTY_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.184-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=TELEPHONE_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.185-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=US_STATE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.185-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=COUNTRY</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.186-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=CL_MOTION_OSC_DETAIL</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.186-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=DOCUMENT_TRACKING_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.187-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=CL_IEM</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName />
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.187-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=EVENT_TYPE</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName>ore:DocumentIdentification</cprm:ECFElementName>
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.187-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/documentList</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName>CourtName</cprm:ECFElementName>
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.187-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/courtLocations/zipCode/</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
        <cprm:CourtCodelist>
          <cprm:ECFElementName>AttorneyValue</cprm:ECFElementName>
          <cprm:EffectiveDate><ore:DateRepresentation xmlns:xsd=""http://niem.gov/niem/proxy/xsd/2.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:type=""xsd:dateTime"">2026-02-21T20:52:14.187-08:00</ore:DateRepresentation></cprm:EffectiveDate>
          <cprm:CourtCodelistURI><ore:IdentificationID>https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/attorneyList</ore:IdentificationID></cprm:CourtCodelistURI>
        </cprm:CourtCodelist>
      </cprm:RuntimePolicyParameters>
    </cprm:CourtPolicyResponseMessage>
  </soap:Body>
</soap:Envelope>";
    }
}
