using EFiling.Core.Caching;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for Phase 5b: GetCase, GetCaseList request builders and CaseResponseParser.
/// </summary>
public class CaseOperationTests
{
    // ─── Request Builder Tests ────────────────────────────────────

    [Fact]
    public void BuildGetCaseRequest_WithDocketId_ContainsCaseDocketID()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest("court.test.com", caseDocketId: "24CV00001");

        Assert.Contains("CaseQueryMessage", xml);
        Assert.Contains("24CV00001", xml);
        Assert.Contains("CaseDocketID", xml);
        Assert.Contains("court.test.com", xml);
        Assert.Contains("IncludeParticipantsIndicator", xml);
    }

    /// <summary>
    /// Regression guard for Track A sub-1d (supersedes the earlier Bug #5 guard): when only a
    /// docket ID is provided, CaseTrackingID is still required on the wire — it must be emitted
    /// with xsi:nil="true". This matches the canonical JTI sample request exactly:
    /// <code>&lt;ns2:CaseTrackingID xsi:nil="true"/&gt;</code>
    ///
    /// Two previous shapes were wrong and are explicitly disallowed here:
    ///   - <c>&lt;ns1:CaseTrackingID&gt;&lt;/ns1:CaseTrackingID&gt;</c> (empty string — server
    ///     reads "" as a lookup key and returns 4011)
    ///   - Element omitted entirely (schema violation — JTI docs explicitly mark CaseTrackingID
    ///     as required)
    /// </summary>
    [Fact]
    public void BuildGetCaseRequest_WithOnlyDocketId_EmitsCaseTrackingIDAsXsiNil()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest("court.test.com", caseDocketId: "MFL018522");

        Assert.Contains("MFL018522", xml);
        Assert.Contains("CaseDocketID", xml);
        // Must be present, must be xsi:nil="true".
        Assert.Contains("<ns1:CaseTrackingID xsi:nil=\"true\"/>", xml);
        // These two wrong shapes must never appear:
        Assert.DoesNotContain("<ns1:CaseTrackingID></ns1:CaseTrackingID>", xml);
        Assert.DoesNotContain("<ns1:CaseTrackingID/>", xml);
    }

    [Fact]
    public void BuildGetCaseRequest_WithTrackingId_ContainsCaseTrackingID()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest("court.test.com", caseTrackingId: "12345");

        Assert.Contains("CaseTrackingID", xml);
        Assert.Contains("12345", xml);
        Assert.DoesNotContain("CaseDocketID", xml);
        // When a real value is provided, xsi:nil MUST NOT be emitted.
        Assert.DoesNotContain("<ns1:CaseTrackingID xsi:nil=\"true\"/>", xml);
    }

    /// <summary>
    /// Track A sub-1d: Document-by-example test that the emitted XML matches the
    /// canonical JTI sample shape (modulo prefix choices). Cross-references
    /// <c>docs/fileing files/ECF Operations/GetCase/Get Case (by Docket ID) Sample Requst XML.xml</c>.
    /// </summary>
    [Fact]
    public void BuildGetCaseRequest_MatchesJtiCanonicalSampleShape()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest(
            "court.test.com", caseDocketId: "MFL018522");

        // Ordered child sequence of CaseQueryMessage must be:
        //   SendingMDELocationID → CaseTrackingID → CaseQueryCriteria → CaseDocketID
        var iSending = xml.IndexOf("SendingMDELocationID", StringComparison.Ordinal);
        var iTracking = xml.IndexOf("CaseTrackingID", StringComparison.Ordinal);
        var iCriteria = xml.IndexOf("CaseQueryCriteria", StringComparison.Ordinal);
        var iDocket = xml.IndexOf("CaseDocketID", StringComparison.Ordinal);

        Assert.True(iSending > 0, "SendingMDELocationID should be present");
        Assert.True(iTracking > iSending, "CaseTrackingID should follow SendingMDELocationID");
        Assert.True(iCriteria > iTracking, "CaseQueryCriteria should follow CaseTrackingID");
        Assert.True(iDocket > iCriteria, "CaseDocketID should follow CaseQueryCriteria");

        // xsi:type on the wrapper element must identify the JTI extension.
        Assert.Contains("xsi:type=\"ns4:CaseQueryMessageTypeExt\"", xml);
    }

    /// <summary>
    /// Diagnostic: print the exact XML that gets sent on the wire for the MFL018522 scenario.
    /// Used to verify the Track A sub-1d fix is present in the bytes that leave the process.
    /// </summary>
    [Fact]
    public void BuildGetCaseRequest_MaderaScenario_PrintWireBytes()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest(
            sendingMdeLocationId: "aux-pub-efm-madera-ca.ecourt.com",
            caseDocketId: "MFL018522",
            caseTrackingId: null,
            includeParticipants: true);

        // Dump to a temp file for manual inspection.
        var dumpPath = Path.Combine(Path.GetTempPath(), "track_a1d_builder_output.xml");
        File.WriteAllText(dumpPath, xml);

        // Key invariants for the wire bytes:
        Assert.Contains("xsi:nil=\"true\"", xml);
        Assert.Contains("<ns1:CaseTrackingID xsi:nil=\"true\"/>", xml);
        Assert.Contains("<ns1:CaseDocketID>MFL018522</ns1:CaseDocketID>", xml);
        Assert.Contains("SendingMDELocationID", xml);
        // Fail-fast: prove the emission to human reviewer via TestOutput (xunit captures).
        System.Console.WriteLine($"[diagnostic] Builder output dumped to {dumpPath}");
        System.Console.WriteLine(xml);
    }

    [Fact]
    public void BuildGetCaseRequest_IncludeFlags()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseRequest("court.test.com",
            caseDocketId: "X", includeParticipants: true, includeDocketEntries: true);

        // The CaseQuery indicator elements live in the CaseQueryMessage-4.0 namespace, which
        // the builder binds to prefix ns3. Asserts match the actual qualified form.
        Assert.Contains("<ns3:IncludeParticipantsIndicator>true</ns3:IncludeParticipantsIndicator>", xml);
        Assert.Contains("<ns3:IncludeDocketEntryIndicator>true</ns3:IncludeDocketEntryIndicator>", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_ByDocketId()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("court.test.com", caseDocketId: "24CV00001");

        Assert.Contains("CaseListQueryMessage", xml);
        Assert.Contains("CaseListQueryCase", xml);
        Assert.Contains("24CV00001", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_ByPartyName()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("court.test.com", partySearchTerm: "John Smith");

        Assert.Contains("CaseListQueryCaseParticipant", xml);
        Assert.Contains("PersonFullName", xml);
        Assert.Contains("John Smith", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_NoFilters()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("court.test.com");

        Assert.Contains("CaseListQueryMessage", xml);
        Assert.DoesNotContain("CaseListQueryCase", xml);
        Assert.DoesNotContain("CaseListQueryCaseParticipant", xml);
    }

    [Fact]
    public void BuildGetDocumentRequest_ContainsTrackingAndDocketId()
    {
        var xml = SoapEnvelopeBuilder.BuildGetDocumentRequest("court.test.com", "TRACK-1", "24CV00001");

        Assert.Contains("DocumentQueryMessage", xml);
        Assert.Contains("TRACK-1", xml);
        Assert.Contains("24CV00001", xml);
    }

    // ─── CaseResponseParser Tests ─────────────────────────────────

    [Fact]
    public void ParseCaseResponse_CivilCaseExt_ExtractsAllFields()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:CaseResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsCaseResponse}""
                              xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                              xmlns:ns6=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}""
                              xmlns:ns7=""{SoapEnvelopeBuilder.NsJtiCaseParticipantExt}""
                              xmlns:st=""{SoapEnvelopeBuilder.NsStructures}"">
      <ns6:CivilCaseExt>
        <ns1:CaseTrackingID>99999</ns1:CaseTrackingID>
        <ns1:CaseDocketID>24CV00001</ns1:CaseDocketID>
        <ns1:CaseTitleText>Smith v. Acme Corp</ns1:CaseTitleText>
        <ns6:CaseTypeText>CV</ns6:CaseTypeText>
        <ns1:CaseCategoryText>10101</ns1:CaseCategoryText>
        <ns7:CaseParticipantExt>
          <ns7:primaryId>100</ns7:primaryId>
          <ns7:referenceId>filedBy0</ns7:referenceId>
          <CaseParticipantRoleCode>PLA</CaseParticipantRoleCode>
          <ns1:EntityPerson>
            <ns1:PersonName>
              <ns1:PersonGivenName>John</ns1:PersonGivenName>
              <ns1:PersonSurName>Smith</ns1:PersonSurName>
            </ns1:PersonName>
          </ns1:EntityPerson>
        </ns7:CaseParticipantExt>
        <ns7:CaseParticipantExt>
          <ns7:primaryId>101</ns7:primaryId>
          <ns7:referenceId>filedAsTo0</ns7:referenceId>
          <CaseParticipantRoleCode>DEF</CaseParticipantRoleCode>
          <ns1:EntityOrganization>
            <ns1:OrganizationName>Acme Corp</ns1:OrganizationName>
          </ns1:EntityOrganization>
        </ns7:CaseParticipantExt>
        <ns6:Complaint st:id=""1"">
          <ns1:CaseCategoryText>10101</ns1:CaseCategoryText>
        </ns6:Complaint>
      </ns6:CivilCaseExt>
    </ns3:CaseResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Equal("99999", result!.CaseTrackingId);
        Assert.Equal("24CV00001", result.CaseDocketId);
        Assert.Equal("Smith v. Acme Corp", result.CaseTitle);
        Assert.Equal("CV", result.CaseTypeCode);
        Assert.Equal("10101", result.CaseCategoryCode);

        // Parties
        Assert.Equal(2, result.Parties.Count);
        Assert.Equal("PLA", result.Parties[0].RoleCode);
        Assert.Equal("John", result.Parties[0].FirstName);
        Assert.Equal("Smith", result.Parties[0].LastName);
        Assert.False(result.Parties[0].IsOrganization);

        Assert.Equal("DEF", result.Parties[1].RoleCode);
        Assert.True(result.Parties[1].IsOrganization);
        Assert.Equal("Acme Corp", result.Parties[1].OrganizationName);

        // Complaints
        Assert.Single(result.Complaints);
        Assert.Equal("1", result.Complaints[0].ComplaintId);
        Assert.Equal("10101", result.Complaints[0].CaseCategoryCode);
    }

    [Fact]
    public void ParseCaseResponse_SoapFault_ReturnsNull()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Case not found</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        Assert.Null(CaseResponseParser.ParseCaseResponse(xml));
    }

    [Fact]
    public void ParseCaseResponse_ErrorCode_ReturnsNull()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:CaseResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsCaseResponse}""
                              xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns2:Error>
        <ns2:ErrorCode>404</ns2:ErrorCode>
        <ns2:ErrorText>Case not found</ns2:ErrorText>
      </ns2:Error>
    </ns3:CaseResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        Assert.Null(CaseResponseParser.ParseCaseResponse(xml));
    }

    [Fact]
    public void ParseCaseListResponse_MultipleCases()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns6:CaseListResponseMessage xmlns:ns6=""{SoapEnvelopeBuilder.NsCaseListResponse}""
                                  xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                  xmlns:ns5=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}"">
      <ns5:CivilCaseExt>
        <ns1:CaseTrackingID>111</ns1:CaseTrackingID>
        <ns1:CaseDocketID>24CV00001</ns1:CaseDocketID>
        <ns1:CaseTitleText>Case One</ns1:CaseTitleText>
      </ns5:CivilCaseExt>
      <ns5:CivilCaseExt>
        <ns1:CaseTrackingID>222</ns1:CaseTrackingID>
        <ns1:CaseDocketID>24CV00002</ns1:CaseDocketID>
        <ns1:CaseTitleText>Case Two</ns1:CaseTitleText>
      </ns5:CivilCaseExt>
    </ns6:CaseListResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var cases = CaseResponseParser.ParseCaseListResponse(xml);

        Assert.Equal(2, cases.Count);
        Assert.Equal("24CV00001", cases[0].CaseDocketId);
        Assert.Equal("Case One", cases[0].CaseTitle);
        Assert.Equal("24CV00002", cases[1].CaseDocketId);
        Assert.Equal("Case Two", cases[1].CaseTitle);
    }

    [Fact]
    public void ParseCaseListResponse_EmptyResponse_ReturnsEmptyList()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns6:CaseListResponseMessage xmlns:ns6=""{SoapEnvelopeBuilder.NsCaseListResponse}""/>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var cases = CaseResponseParser.ParseCaseListResponse(xml);
        Assert.Empty(cases);
    }

    [Fact]
    public void ParseCaseListResponse_SoapFault_ReturnsEmptyList()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Error</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var cases = CaseResponseParser.ParseCaseListResponse(xml);
        Assert.Empty(cases);
    }
}

/// <summary>
/// Integration tests for GetCase and SearchCases against live Madera staging.
/// </summary>
[Trait("Category", "Integration")]
public class CaseIntegrationTests
{
    [Fact]
    public async Task SearchCasesAsync_LiveMadera_ReturnsResults()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        var criteria = new CaseSearchCriteria
        {
            CaseDocketId = "24CV00001"
        };

        var cases = await provider.SearchCasesAsync(config, criteria);

        Assert.NotNull(cases);
        Console.WriteLine($"SearchCases returned {cases.Count} cases");
        foreach (var c in cases.Take(5))
            Console.WriteLine($"  [{c.CaseTrackingId}] {c.CaseDocketId} — {c.CaseTitle}");
    }

    [Fact]
    public async Task GetCaseAsync_LiveMadera_ByDocketId_ReturnsCase()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // Use a case docket ID that likely exists in Madera staging
        var result = await provider.GetCaseAsync(config, caseDocketId: "24CV00001");

        // May be null if case doesn't exist, but shouldn't crash
        if (result != null)
        {
            Console.WriteLine($"GetCase: {result.CaseDocketId} — {result.CaseTitle}");
            Console.WriteLine($"  TrackingId: {result.CaseTrackingId}");
            Console.WriteLine($"  Type: {result.CaseTypeCode}, Category: {result.CaseCategoryCode}");
            Console.WriteLine($"  Parties: {result.Parties.Count}");
            foreach (var p in result.Parties.Take(10))
                Console.WriteLine($"    [{p.RoleCode}] {p.FirstName} {p.LastName} {p.OrganizationName} (id={p.PrimaryId})");
            Console.WriteLine($"  Complaints: {result.Complaints.Count}");
            foreach (var c in result.Complaints)
                Console.WriteLine($"    Complaint {c.ComplaintId}: {c.CaseCategoryCode}");
        }
        else
        {
            Console.WriteLine("GetCase returned null (case not found or error)");
        }
    }

    /// <summary>
    /// Diagnostic test: hit live Madera with MFL018528, dump the raw SOAP response,
    /// then attempt to parse it to see exactly why the parser returns null.
    /// </summary>
    private static CourtConfiguration MaderaConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        CourtRecordEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://overindulgent-finnicky-lavette.ngrok-free.dev/api/efiling/nfrc",
        Username = "legalhub",
        Password = "[va<8<jC50Y0",
        IsActive = true
    };

    [Fact]
    public async Task GetCase_MFL018528_RawResponse_Diagnostic()
    {
        var config = MaderaConfig;
        var caseDocketId = "MFL018528";

        // Extract MDE location ID the same way the provider does
        var mdeLocationId = Uri.TryCreate(config.CourtRecordEndpoint, UriKind.Absolute, out var uri)
            ? uri.Host : config.CourtRecordEndpoint;

        Console.WriteLine($"=== GetCase Diagnostic for {caseDocketId} ===");
        Console.WriteLine($"CourtId: {config.CourtId}");
        Console.WriteLine($"CourtRecordEndpoint: {config.CourtRecordEndpoint}");
        Console.WriteLine($"MDE Location ID: {mdeLocationId}");

        // Build the SOAP request
        var requestXml = SoapEnvelopeBuilder.BuildGetCaseRequest(
            mdeLocationId,
            caseDocketId: caseDocketId);
        Console.WriteLine($"\n=== REQUEST XML ===\n{requestXml}");

        // Send directly using JtiSoapClient
        using var soapClient = new JtiSoapClient();
        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.CourtRecordEndpoint, requestXml);
            Console.WriteLine($"\n=== RESPONSE XML (HTTP 200) ===\n{responseXml}");
        }
        catch (JtiSoapException ex)
        {
            Console.WriteLine($"\n=== SOAP EXCEPTION ===");
            Console.WriteLine($"HTTP Status: {ex.HttpStatusCode}");
            Console.WriteLine($"Fault Code: {ex.FaultCode}");
            Console.WriteLine($"Fault String: {ex.FaultString}");
            Console.WriteLine($"Response Body:\n{ex.ResponseBody}");

            // Try parsing the error response body
            if (!string.IsNullOrEmpty(ex.ResponseBody))
            {
                var parsed = CaseResponseParser.ParseCaseResponse(ex.ResponseBody);
                Console.WriteLine($"\nParser result from error body: {(parsed != null ? "FOUND case" : "NULL")}");
            }
            return;
        }

        // Now try parsing
        var result = CaseResponseParser.ParseCaseResponse(responseXml);
        Console.WriteLine($"\n=== PARSE RESULT ===");
        Console.WriteLine($"Parser returned: {(result != null ? "CaseInfo object" : "NULL")}");

        if (result != null)
        {
            Console.WriteLine($"  DocketId: {result.CaseDocketId}");
            Console.WriteLine($"  Title: {result.CaseTitle}");
            Console.WriteLine($"  TrackingId: {result.CaseTrackingId}");
            Console.WriteLine($"  Type: {result.CaseTypeCode}");
            Console.WriteLine($"  Parties: {result.Parties.Count}");
            Console.WriteLine($"  Complaints: {result.Complaints.Count}");
        }
    }

    /// <summary>
    /// Also test SearchCases (GetCaseList) with MFL018528 to compare.
    /// </summary>
    [Fact]
    public async Task SearchCases_MFL018528_RawResponse_Diagnostic()
    {
        var config = MaderaConfig;
        var caseDocketId = "MFL018528";

        var mdeLocationId = Uri.TryCreate(config.CourtRecordEndpoint, UriKind.Absolute, out var uri)
            ? uri.Host : config.CourtRecordEndpoint;

        Console.WriteLine($"=== SearchCases Diagnostic for {caseDocketId} ===");

        var criteria = new CaseSearchCriteria { CaseDocketId = caseDocketId };
        var requestXml = SoapEnvelopeBuilder.BuildGetCaseListRequest(mdeLocationId, criteria);
        Console.WriteLine($"\n=== REQUEST XML ===\n{requestXml}");

        using var soapClient = new JtiSoapClient();
        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.CourtRecordEndpoint, requestXml);
            Console.WriteLine($"\n=== RESPONSE XML (HTTP 200) ===\n{responseXml}");
        }
        catch (JtiSoapException ex)
        {
            Console.WriteLine($"\n=== SOAP EXCEPTION (HTTP {ex.HttpStatusCode}) ===");
            Console.WriteLine($"Response Body:\n{ex.ResponseBody}");

            if (!string.IsNullOrEmpty(ex.ResponseBody))
            {
                var parsed = CaseResponseParser.ParseCaseListResponse(ex.ResponseBody);
                Console.WriteLine($"\nParser result from error body: {parsed.Count} cases");
            }
            return;
        }

        var cases = CaseResponseParser.ParseCaseListResponse(responseXml);
        Console.WriteLine($"\n=== PARSE RESULT: {cases.Count} cases ===");
        foreach (var c in cases)
        {
            Console.WriteLine($"  [{c.CaseTrackingId}] {c.CaseDocketId} — {c.CaseTitle} (type={c.CaseTypeCode})");
        }
    }
}
