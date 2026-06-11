using EFiling.Core.Enums;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for Phase 5 response parsers: GetFilingStatus, GetFilingList, GetNFRC.
/// </summary>
public class Phase5ParserTests
{
    // ─── GetFilingStatus ─────────────────────────────────────────

    [Fact]
    public void ParseFilingStatusResponse_Accepted_ReturnsAccepted()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>EFM-99999</ns1:IdentificationID>
      </ns1:DocumentIdentification>
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>Accepted</ns2:FilingStatusCode>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(FilingStatus.Accepted, result.FilingStatus);
        Assert.Equal("EFM-99999", result.EfmReferenceId);
    }

    [Fact]
    public void ParseFilingStatusResponse_Rejected_WithReasons()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}""
                                      xmlns:ns4=""{SoapEnvelopeBuilder.NsJtiFilingStatusReason}"">
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>EFM-11111</ns1:IdentificationID>
      </ns1:DocumentIdentification>
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>Rejected</ns2:FilingStatusCode>
        <ns4:FilingStatusReason>
          <ns4:ReasonCode>DOC_INVALID</ns4:ReasonCode>
          <ns4:ReasonCodeText>Document format invalid</ns4:ReasonCodeText>
          <ns4:Memo>PDF is corrupt</ns4:Memo>
        </ns4:FilingStatusReason>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(FilingStatus.Rejected, result.FilingStatus);
        Assert.Single(result.Reasons);
        Assert.Equal("DOC_INVALID", result.Reasons[0].ReasonCode);
        Assert.Equal("Document format invalid", result.Reasons[0].ReasonText);
        Assert.Equal("PDF is corrupt", result.Reasons[0].Memo);
    }

    [Fact]
    public void ParseFilingStatusResponse_WithCaseIds()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns1:CaseTrackingID>TRACK-123</ns1:CaseTrackingID>
      <ns1:CaseDocketID>24CV00001</ns1:CaseDocketID>
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>Filed</ns2:FilingStatusCode>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(FilingStatus.Filed, result.FilingStatus);
        Assert.Equal("TRACK-123", result.CaseTrackingId);
        Assert.Equal("24CV00001", result.CaseDocketId);
    }

    [Fact]
    public void ParseFilingStatusResponse_SoapFault_ReturnsUnknown()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultcode>Server</faultcode>
      <faultstring>Internal error</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);
        Assert.Equal(FilingStatus.Unknown, result.FilingStatus);
    }

    [Fact]
    public void ParseFilingStatusResponse_WithDocumentStatuses()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>Accepted</ns2:FilingStatusCode>
      </ns2:FilingStatus>
      <ReviewedDocument>
        <ns1:DocumentDescriptionText>Complaint</ns1:DocumentDescriptionText>
        <DocumentFilingStatus>
          <DocumentFilingStatusCode>F</DocumentFilingStatusCode>
        </DocumentFilingStatus>
      </ReviewedDocument>
      <ReviewedDocument>
        <ns1:DocumentDescriptionText>Civil Cover Sheet</ns1:DocumentDescriptionText>
        <DocumentFilingStatus>
          <DocumentFilingStatusCode>RJ</DocumentFilingStatusCode>
        </DocumentFilingStatus>
      </ReviewedDocument>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("Complaint", result.Documents[0].DocumentDescription);
        Assert.Equal(DocumentStatus.Filed, result.Documents[0].Status);
        Assert.Equal("Civil Cover Sheet", result.Documents[1].DocumentDescription);
        Assert.Equal(DocumentStatus.Rejected, result.Documents[1].Status);
    }

    // ─── GetFilingList ───────────────────────────────────────────

    [Fact]
    public void ParseFilingListResponse_MultipleFilings()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns4:FilingListResponseMessageExt xmlns:ns4=""{SoapEnvelopeBuilder.NsJtiFilingListResponseExt}""
                                       xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                       xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns4:MatchingFilings>
        <ns4:MatchingFiling>
          <ns4:CaseTitle>Smith v. Acme Corp</ns4:CaseTitle>
          <ns4:FilingId>12345</ns4:FilingId>
          <ns4:SubmitterId>user@example.com</ns4:SubmitterId>
          <ns4:ReceivedDate>2025-01-15</ns4:ReceivedDate>
          <ns4:ReceivedTime>10:30:00</ns4:ReceivedTime>
          <ns1:CaseTrackingID>TRACK-001</ns1:CaseTrackingID>
          <ns1:CaseDocketID>25CV00001</ns1:CaseDocketID>
          <ns2:FilingStatus>
            <ns2:FilingStatusCode>Accepted</ns2:FilingStatusCode>
          </ns2:FilingStatus>
          <ns4:LeadDocument>
            <ns1:DocumentDescriptionText>Complaint</ns1:DocumentDescriptionText>
          </ns4:LeadDocument>
        </ns4:MatchingFiling>
        <ns4:MatchingFiling>
          <ns4:CaseTitle>Doe v. Roe</ns4:CaseTitle>
          <ns4:FilingId>12346</ns4:FilingId>
          <ns4:ReceivedDate>2025-01-16</ns4:ReceivedDate>
          <ns2:FilingStatus>
            <ns2:FilingStatusCode>Rejected</ns2:FilingStatusCode>
          </ns2:FilingStatus>
        </ns4:MatchingFiling>
      </ns4:MatchingFilings>
    </ns4:FilingListResponseMessageExt>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var items = FilingResponseParser.ParseFilingListResponse(xml);

        Assert.Equal(2, items.Count);

        Assert.Equal("Smith v. Acme Corp", items[0].CaseTitle);
        Assert.Equal("12345", items[0].FilingId);
        Assert.Equal("user@example.com", items[0].SubmitterId);
        Assert.Equal("TRACK-001", items[0].CaseTrackingId);
        Assert.Equal("25CV00001", items[0].CaseDocketId);
        Assert.Equal(FilingStatus.Accepted, items[0].Status);
        Assert.Equal("Complaint", items[0].LeadDocumentDescription);
        Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 0), items[0].ReceivedDate);

        Assert.Equal("Doe v. Roe", items[1].CaseTitle);
        Assert.Equal(FilingStatus.Rejected, items[1].Status);
    }

    [Fact]
    public void ParseFilingListResponse_EmptyList()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns4:FilingListResponseMessageExt xmlns:ns4=""{SoapEnvelopeBuilder.NsJtiFilingListResponseExt}"">
      <ns4:MatchingFilings/>
    </ns4:FilingListResponseMessageExt>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var items = FilingResponseParser.ParseFilingListResponse(xml);
        Assert.Empty(items);
    }

    [Fact]
    public void ParseFilingListResponse_SoapFault_ReturnsEmpty()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Error</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var items = FilingResponseParser.ParseFilingListResponse(xml);
        Assert.Empty(items);
    }

    // ─── GetNFRC ─────────────────────────────────────────────────

    [Fact]
    public void ParseNfrcResponse_Success()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns1:NFRCResponse xmlns:ns1=""{SoapEnvelopeBuilder.NsJtiNfrcResponse}"">
      <ns1:NFRCResponseMessage>Success</ns1:NFRCResponseMessage>
    </ns1:NFRCResponse>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var (success, errorText) = FilingResponseParser.ParseNfrcResponse(xml);
        Assert.True(success);
        Assert.Null(errorText);
    }

    [Fact]
    public void ParseNfrcResponse_WithError()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns1:NFRCResponse xmlns:ns1=""{SoapEnvelopeBuilder.NsJtiNfrcResponse}""
                       xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns2:Error>
        <ns2:ErrorCode>4001</ns2:ErrorCode>
        <ns2:ErrorText>Filing not found</ns2:ErrorText>
      </ns2:Error>
    </ns1:NFRCResponse>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var (success, errorText) = FilingResponseParser.ParseNfrcResponse(xml);
        Assert.False(success);
        Assert.Equal("Filing not found", errorText);
    }

    /// <summary>
    /// Phase 3 regression guard: ECF-style success responses always include
    /// <c>&lt;Error&gt;&lt;ErrorCode&gt;0&lt;/ErrorCode&gt;&lt;ErrorText&gt;NoError&lt;/ErrorText&gt;&lt;/Error&gt;</c>
    /// — the parser must treat this as success, not failure. The previous
    /// implementation returned <c>(false, "NoError")</c> for every Madera
    /// RequestNfrc response, which broke the polling task and made all
    /// re-delivery requests look like failures even when Madera was
    /// acknowledging the request correctly. See
    /// <see cref="EFiling.Providers.JTI.Parsers.FilingResponseParser.ParseNfrcResponse"/>.
    /// </summary>
    [Fact]
    public void ParseNfrcResponse_WithErrorCodeZero_IsSuccess()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns1:NFRCResponse xmlns:ns1=""{SoapEnvelopeBuilder.NsJtiNfrcResponse}""
                       xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns1:NFRCResponseMessage>Transaction not fully processed by the court system</ns1:NFRCResponseMessage>
      <ns2:Error>
        <ns2:ErrorCode>0</ns2:ErrorCode>
        <ns2:ErrorText>NoError</ns2:ErrorText>
      </ns2:Error>
    </ns1:NFRCResponse>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var (success, errorText) = FilingResponseParser.ParseNfrcResponse(xml);
        Assert.True(success);
        Assert.Null(errorText);
    }

    [Fact]
    public void ParseNfrcResponse_SoapFault()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultstring>Service unavailable</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var (success, errorText) = FilingResponseParser.ParseNfrcResponse(xml);
        Assert.False(success);
        Assert.Equal("Service unavailable", errorText);
    }

    // ─── Request Builder Tests ────────────────────────────────────

    [Fact]
    public void BuildGetFilingStatusRequest_ContainsDocumentIdentification()
    {
        var xml = SoapEnvelopeBuilder.BuildGetFilingStatusRequest("court.test.com", "EFM-12345");

        Assert.Contains("FilingStatusQueryMessage", xml);
        Assert.Contains("EFM-12345", xml);
        Assert.Contains("court.test.com", xml);
        Assert.Contains("DocumentIdentification", xml);
    }

    [Fact]
    public void BuildGetFilingListRequest_WithAllFilters()
    {
        var xml = SoapEnvelopeBuilder.BuildGetFilingListRequest(
            "court.test.com",
            caseDocketId: "24CV00001",
            filingType: "INITIAL",
            caseType: "CV",
            filingStatus: "ACCEPTED",
            fromDate: new DateTime(2025, 1, 1),
            toDate: new DateTime(2025, 12, 31));

        Assert.Contains("FilingListQueryMessageExt", xml);
        Assert.Contains("24CV00001", xml);
        Assert.Contains("INITIAL", xml);
        Assert.Contains("CV", xml);
        Assert.Contains("ACCEPTED", xml);
        Assert.Contains("2025-01-01", xml);
        Assert.Contains("2025-12-31", xml);
    }

    [Fact]
    public void BuildGetFilingListRequest_NoFilters()
    {
        var xml = SoapEnvelopeBuilder.BuildGetFilingListRequest("court.test.com");

        Assert.Contains("FilingListQueryMessageExt", xml);
        Assert.Contains("court.test.com", xml);
        Assert.DoesNotContain("CaseDocketID", xml);
        Assert.DoesNotContain("FilingType", xml);
    }

    [Fact]
    public void BuildGetNfrcRequest_WithEfmId()
    {
        var xml = SoapEnvelopeBuilder.BuildGetNfrcRequest(efmReferenceId: "EFM-99999");

        Assert.Contains("NFRCRequest", xml);
        Assert.Contains("EfmReferenceId", xml);
        Assert.Contains("EFM-99999", xml);
    }

    [Fact]
    public void BuildGetNfrcRequest_WithBothIds()
    {
        var xml = SoapEnvelopeBuilder.BuildGetNfrcRequest("EFM-99999", "EFSP-11111");

        Assert.Contains("EfmReferenceId", xml);
        Assert.Contains("EFM-99999", xml);
        Assert.Contains("EfspReferenceId", xml);
        Assert.Contains("EFSP-11111", xml);
    }

    // ─── Status Mapping Tests ────────────────────────────────────

    [Theory]
    [InlineData("Received", FilingStatus.ReceivedUnderReview)]
    [InlineData("Accepted", FilingStatus.Accepted)]
    [InlineData("Rejected", FilingStatus.Rejected)]
    [InlineData("Filed", FilingStatus.Filed)]
    [InlineData("Reviewed", FilingStatus.Accepted)]
    [InlineData("PartiallyAccepted", FilingStatus.PartiallyAccepted)]
    [InlineData("UnknownValue", FilingStatus.Unknown)]
    public void ParseFilingStatusResponse_MapsStatusCodes(string statusCode, FilingStatus expected)
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>{statusCode}</ns2:FilingStatusCode>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);
        Assert.Equal(expected, result.FilingStatus);
    }
}
