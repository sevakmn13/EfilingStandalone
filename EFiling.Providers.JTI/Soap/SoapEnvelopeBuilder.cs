namespace EFiling.Providers.JTI.Soap;

/// <summary>
/// Builds SOAP envelopes with the correct namespaces for JTI ECF 4.0 operations.
/// </summary>
public static class SoapEnvelopeBuilder
{
    // ─── Namespace constants ────────────────────────────────────────

    public const string NsSoapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string NsNiemCore = "http://niem.gov/niem/niem-core/2.0";
    public const string NsCommonTypes = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
    public const string NsPolicyQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyQueryMessage-4.0";
    public const string NsPolicyResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyResponseMessage-4.0";
    public const string NsReviewFiling = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:ReviewFilingRequest-4.0";
    public const string NsCoreFilingMessage = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CoreFilingMessage-4.0";
    public const string NsPaymentMessage = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:PaymentMessage-4.0";
    public const string NsFilingStatusQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusQueryMessage-4.0";
    public const string NsFilingStatusResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusResponseMessage-4.0";
    public const string NsFeesCalcQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationQueryMessage-4.0";
    public const string NsFeesCalcResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationResponseMessage-4.0";
    public const string NsMessageReceipt = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0";
    public const string NsCaseQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseQueryMessage-4.0";
    public const string NsCaseResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseResponseMessage-4.0";
    public const string NsDocumentQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:DocumentQueryMessage-4.0";
    public const string NsXsi = "http://www.w3.org/2001/XMLSchema-instance";

    // JTI extension namespaces
    public const string NsJtiCoreFilingExt = "urn:com.journaltech:ecourt:ecf:extension:CoreFilingMessageExtType";
    public const string NsJtiPaymentExt = "urn:com.journaltech:ecourt:ecf:extension:PaymentMessageExt";
    public const string NsJtiCivilCaseExt = "urn:com.journaltech:ecourt:ecf:extension:CivilCaseTypeExt";
    public const string NsJtiCaseParticipantExt = "urn:com.journaltech:ecourt:ecf:extension:CaseParticipantExt";
    public const string NsJtiFilingListQueryExt = "urn:com.journaltech:ecourt:ecf:extension:FilingListQueryMessageTypeExt";
    public const string NsJtiFilingListResponseExt = "urn:com.journaltech:ecourt:ecf:extension:FilingListResponseMessageTypeExt";
    public const string NsJtiCaseQueryExt = "urn:com.journaltech:ecourt:ecf:extension:CaseQueryMessageTypeExt";
    public const string NsJtiNfrcRequest = "urn:com.journaltech:ecourt:ecf:extension:NFRCRequestType";
    public const string NsJtiNfrcResponse = "urn:com.journaltech:ecourt:ecf:extension:NFRCResponseType";
    public const string NsJtiDocumentFilingMetaData = "urn:com.journaltech:ecourt:ecf:extension:DocumentFilingMetaData";
    public const string NsJtiDocumentValue = "urn:com.journaltech:ecourt:ecf:extension:DocumentValue";
    public const string NsJtiCodeValue = "urn:com.journaltech:ecourt:ecf:extension:CodeValue";
    public const string NsJtiEfmFilingRef = "urn:com.journaltech:ecourt:ecf:extension:EfmFilingReference";
    public const string NsJtiFeesCalcExt = "urn:com.journaltech:ecourt:ecf:extension:FeesCalculationTypeExt";
    public const string NsJtiFeesCalcQueryExt = "urn:com.journaltech:ecourt:ecf:extension:FeesCalculationQueryMessageTypeExt";
    public const string NsJtiDocumentResponseExt = "urn:com.journaltech:ecourt:ecf:extension:DocumentResponseMessageTypeExt";
    public const string NsJtiCaseAssignmentType = "urn:com.journaltech:ecourt:ecf:extension:CaseAssignmentType";
    /// <summary>
    /// JTI extension namespace for <c>CourtEventJudgment</c> content elements (e.g.
    /// <c>&lt;judgmentId&gt;</c>) nested inside the <c>&lt;judgments&gt;</c> wrapper.
    /// Step #15 (judgment classType wire shape, see <c>STEP15_JUDGMENT_AUDIT.md</c>):
    /// the <c>&lt;ns9:judgments&gt;</c> wrapper lives in <see cref="NsJtiDocumentFilingMetaData"/>
    /// but its child <c>&lt;judgmentId&gt;</c> element lives in this namespace per WSDL
    /// <c>CourtEventJudgmentType</c> (<c>FilingReview/Reference.cs:22743</c>) and observed
    /// LASC Writ of Return Sample wire shape. Judgment is the <b>only</b> classType where
    /// wrapper + content element use different namespaces.
    /// </summary>
    public const string NsJtiCourtEventJudgment = "urn:com.journaltech:ecourt:ecf:extension:CourtEventJudgment";

    // NIEM domains
    public const string NsJxdm = "http://niem.gov/niem/domains/jxdm/4.0";
    public const string NsStructures = "http://niem.gov/niem/structures/2.0";

    // UBL
    public const string NsUblCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public const string NsUblCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    // ECF types
    public const string NsCivilCase = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CivilCase-4.0";
    public const string NsDomesticCase = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:DomesticCase-4.0";

    // WSDL / envelope
    public const string NsWsdlProfile = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0";
    public const string NsUnqualifiedDataTypes = "urn:un:unece:uncefact:data:specification:UnqualifiedDataTypesSchemaModule:2";
    public const string NsProxyXsd = "http://niem.gov/niem/proxy/xsd/2.0";

    // JTI additional extensions
    public const string NsJtiStructuredAddressExt = "urn:com.journaltech:ecourt:ecf:extension:StructuredAddressExt";
    public const string NsJtiTelephoneNumberExt = "urn:com.journaltech:ecourt:ecf:extension:TelephoneNumberExt";
    public const string NsJtiContactValue = "urn:com.journaltech:ecourt:ecf:extension:ContactValue";
    public const string NsJtiFeesCalcResponseExt = "urn:com.journaltech:ecourt:ecf:extension:FeesCalculationResponseMessageTypeExt";
    public const string NsJtiFilingIdentity = "urn:com.journaltech:ecourt:ecf:extension:FilingIdentityType";
    public const string NsJtiFilingStatusReason = "urn:com.journaltech:ecourt:ecf:extension:FilingStatusReason";
    public const string NsJtiRecordingStatusQuery = "urn:com.journaltech:ecourt:ecf:extension:RecordingStatusQueryMessage";
    public const string NsJtiRecordingStatusResponse = "urn:com.journaltech:ecourt:ecf:extension:RecordingStatusResponseMessage";

    /// <summary>
    /// Build a GetPolicy SOAP request envelope.
    /// </summary>
    public static string BuildGetPolicyRequest(string sendingMdeLocationId)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}"" 
                   xmlns:ns1=""{NsNiemCore}"" 
                   xmlns:ns2=""{NsCommonTypes}"" 
                   xmlns:ns3=""{NsPolicyResponse}"" 
                   xmlns:xsi=""{NsXsi}"" 
                   xmlns:ns4=""{NsPolicyQuery}"">
  <SOAP-ENV:Header/>
  <SOAP-ENV:Body>
    <ns4:CourtPolicyQueryMessage>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>
    </ns4:CourtPolicyQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetPolicy SOAP request using a test header for auto-accept/reject (UAT only).
    /// </summary>
    public static string BuildGetPolicyRequestWithTestHeader(string sendingMdeLocationId, string testStatus)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}"" 
                   xmlns:ns1=""{NsNiemCore}"" 
                   xmlns:ns2=""{NsCommonTypes}"" 
                   xmlns:ns3=""{NsPolicyResponse}"" 
                   xmlns:xsi=""{NsXsi}"" 
                   xmlns:ns4=""{NsPolicyQuery}"">
  <SOAP-ENV:Header>
    <status xmlns=""com.journaltech.niem.test"">{EscapeXml(testStatus)}</status>
  </SOAP-ENV:Header>
  <SOAP-ENV:Body>
    <ns4:CourtPolicyQueryMessage>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>
    </ns4:CourtPolicyQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    // ─── Filing List namespaces ─────────────────────────────────────
    public const string NsFilingListQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingListQueryMessage-4.0";
    public const string NsFilingListResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingListResponseMessage-4.0";

    /// <summary>
    /// Build a GetFilingStatus SOAP request (ECF standard — may not work on all JTI courts).
    /// Prefer <see cref="BuildGetRecordingStatusRequest"/> for JTI courts.
    /// </summary>
    public static string BuildGetFilingStatusRequest(string sendingMdeLocationId, string efmReferenceId)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:ns2=""{NsCommonTypes}""
                   xmlns:ns3=""{NsFilingStatusQuery}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusQueryMessage>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>{EscapeXml(efmReferenceId)}</ns1:IdentificationID>
        <ns1:IdentificationCategoryText>FILING_ASSEMBLY_MDE</ns1:IdentificationCategoryText>
      </ns1:DocumentIdentification>
    </ns3:FilingStatusQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetRecordingStatus SOAP request (JTI custom extension).
    /// This is the documented JTI operation for checking filing and recording status.
    /// IdentificationCategory: "efm" for EFM reference ID, "efsp" for EFSP reference ID.
    /// </summary>
    public static string BuildGetRecordingStatusRequest(string referenceId, string identificationCategory = "efm")
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:rsqm=""{NsJtiRecordingStatusQuery}"">
  <SOAP-ENV:Header/>
  <SOAP-ENV:Body>
    <rsqm:RecordingStatusQueryMessage>
      <ns1:DocumentIdentification>
        <ns1:IdentificationID>{EscapeXml(referenceId)}</ns1:IdentificationID>
        <ns1:IdentificationCategory>{EscapeXml(identificationCategory)}</ns1:IdentificationCategory>
      </ns1:DocumentIdentification>
    </rsqm:RecordingStatusQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetRecordingStatus SOAP request by date range.
    /// </summary>
    public static string BuildGetRecordingStatusByDateRequest(DateTime fromDate, DateTime toDate)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:rsqm=""{NsJtiRecordingStatusQuery}"">
  <SOAP-ENV:Header/>
  <SOAP-ENV:Body>
    <rsqm:RecordingStatusQueryMessage>
      <rsqm:FromDate>{fromDate:yyyy-MM-ddTHH:mm:ss}</rsqm:FromDate>
      <rsqm:ToDate>{toDate:yyyy-MM-ddTHH:mm:ss}</rsqm:ToDate>
    </rsqm:RecordingStatusQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetFilingList SOAP request with JTI extension filters.
    /// </summary>
    public static string BuildGetFilingListRequest(
        string sendingMdeLocationId,
        string? caseDocketId = null,
        string? filingType = null,
        string? caseType = null,
        string? filingStatus = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var filters = "";
        if (!string.IsNullOrEmpty(caseDocketId))
            filters += $"\n      <ns1:CaseDocketID>{EscapeXml(caseDocketId)}</ns1:CaseDocketID>";
        if (!string.IsNullOrEmpty(filingType))
            filters += $"\n      <ns4:FilingType>{EscapeXml(filingType)}</ns4:FilingType>";
        if (!string.IsNullOrEmpty(caseType))
            filters += $"\n      <ns4:CaseType>{EscapeXml(caseType)}</ns4:CaseType>";
        if (!string.IsNullOrEmpty(filingStatus))
            filters += $"\n      <ns4:FilingStatus>{EscapeXml(filingStatus)}</ns4:FilingStatus>";
        if (fromDate.HasValue)
            filters += $"\n      <ns4:FromDate>{fromDate.Value:yyyy-MM-ddTHH:mm:ss}</ns4:FromDate>";
        if (toDate.HasValue)
            filters += $"\n      <ns4:ToDate>{toDate.Value:yyyy-MM-ddTHH:mm:ss}</ns4:ToDate>";

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:ns2=""{NsCommonTypes}""
                   xmlns:ns3=""{NsFilingListQuery}""
                   xmlns:ns4=""{NsJtiFilingListQueryExt}"">
  <SOAP-ENV:Body>
    <ns4:FilingListQueryMessageExt>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>{filters}
    </ns4:FilingListQueryMessageExt>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetNFRC SOAP request.
    /// </summary>
    public static string BuildGetNfrcRequest(string? efmReferenceId = null, string? efspReferenceId = null)
    {
        var body = "";
        if (!string.IsNullOrEmpty(efmReferenceId))
            body += $"\n      <ns1:EfmReferenceId>{EscapeXml(efmReferenceId)}</ns1:EfmReferenceId>";
        if (!string.IsNullOrEmpty(efspReferenceId))
            body += $"\n      <ns1:EfspReferenceId>{EscapeXml(efspReferenceId)}</ns1:EfspReferenceId>";

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsJtiNfrcRequest}"">
  <SOAP-ENV:Body>
    <ns1:NFRCRequest>{body}
    </ns1:NFRCRequest>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetChargedAmount SOAP request (FilingReview endpoint).
    /// Input is a simple EfmReferenceId string element.
    /// </summary>
    public static string BuildGetChargedAmountRequest(string efmReferenceId)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsJtiEfmFilingRef}"">
  <SOAP-ENV:Body>
    <ns1:EfmReferenceId>{EscapeXml(efmReferenceId)}</ns1:EfmReferenceId>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    // ─── CourtRecord namespaces ────────────────────────────────────
    public const string NsCaseListQuery = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0";
    public const string NsCaseListResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListResponseMessage-4.0";
    public const string NsDocumentResponse = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:DocumentResponseMessage-4.0";

    /// <summary>
    /// Build a GetCase SOAP request (CourtRecord endpoint).
    /// Uses CaseQueryMessageTypeExt with CaseDocketID and optional flags.
    /// </summary>
    /// <param name="sendingMdeLocationId">EFSP sender identifier — typically the endpoint host.</param>
    /// <param name="caseDocketId">The court's case number (e.g., "MFL018522"). Required when <paramref name="caseTrackingId"/> is null.</param>
    /// <param name="caseTrackingId">Internal CMS tracking ID (integer string). When null, the CaseTrackingID element is emitted as xsi:nil="true" per JTI docs.</param>
    /// <param name="includeParticipants">Include case participants in the response.</param>
    /// <param name="includeDocketEntries">Include docket entries in the response.</param>
    /// <param name="courtIdentifier">
    /// Court identifier for the <c>&lt;j:CaseCourt&gt;</c> element. When supplied, the builder
    /// emits a <c>CaseCourt/OrganizationIdentification/IdentificationID</c> block matching the JTI
    /// canonical sample (`docs/fileing files/ECF Operations/GetCase/Get Case (by Docket ID) Sample Requst XML.xml`).
    /// Madera's server appears to require this for docket-ID lookups. Default: same as <paramref name="sendingMdeLocationId"/>.
    /// </param>
    public static string BuildGetCaseRequest(
        string sendingMdeLocationId,
        string? caseDocketId = null,
        string? caseTrackingId = null,
        bool includeParticipants = true,
        bool includeDocketEntries = false,
        string? courtIdentifier = null)
    {
        // Track A sub-1d fix (supersedes Bug #5): CaseTrackingID is schema-REQUIRED on
        // CaseQueryMessage per the official JTI documentation
        // (docs/fileing files/ECF Operations/GetCase/GetCase _ EFM Documentation.html, lines
        // 204-206). When querying by CaseDocketID you must still emit the element — marked
        // xsi:nil="true" — like the canonical sample request:
        //     <ns2:CaseTrackingID xsi:nil="true"/>
        //
        // History (preserved for future maintainers):
        //   • Pre-Bug #5: builder always emitted <ns1:CaseTrackingID></ns1:CaseTrackingID>
        //     (empty-string content). Server read the empty string as a lookup key and
        //     returned "4011: Invalid case Reference ID:  <docket>" (double-space stems from
        //     the server concatenating the empty tracking ID into its error template).
        //   • Bug #5 fix: element omitted entirely when empty. This violated the schema —
        //     CaseTrackingID is a required element per the JTI docs — and Madera continued
        //     returning 4011 because the server's validator expects the element to be present.
        //   • Track A sub-1d (first attempt): emit <ns1:CaseTrackingID xsi:nil="true"/>.
        //     Schema-correct but still 4011 on Madera — not sufficient by itself.
        //   • Track A sub-1d (second attempt, current): also add <j:CaseCourt>/<nc:OrganizationIdentification>/<nc:IdentificationID>.
        //     Matches the JTI canonical sample request (LASC, 19STLC00568) exactly. CaseCourt
        //     identifies which court's case database to look up. Without it, Madera's server
        //     can't resolve the docket ID and falls back to the 4011 error path.
        var trackingBlock = string.IsNullOrEmpty(caseTrackingId)
            ? @"
      <ns1:CaseTrackingID xsi:nil=""true""/>"
            : $@"
      <ns1:CaseTrackingID>{EscapeXml(caseTrackingId)}</ns1:CaseTrackingID>";
        var docketBlock = string.IsNullOrEmpty(caseDocketId)
            ? string.Empty
            : $@"
      <ns1:CaseDocketID>{EscapeXml(caseDocketId)}</ns1:CaseDocketID>";

        // CaseCourt: default to the same identifier as SendingMDELocationID (typically the
        // endpoint host). Callers may override when the court's internal identifier differs
        // from the public endpoint host (e.g., Alameda uses an internal CMS hostname).
        var effectiveCourtId = courtIdentifier ?? sendingMdeLocationId;
        var caseCourtBlock = string.IsNullOrEmpty(effectiveCourtId)
            ? string.Empty
            : $@"
      <ns6:CaseCourt>
        <ns1:OrganizationIdentification>
          <ns1:IdentificationID>{EscapeXml(effectiveCourtId)}</ns1:IdentificationID>
        </ns1:OrganizationIdentification>
      </ns6:CaseCourt>";

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:ns2=""{NsCommonTypes}""
                   xmlns:ns3=""{NsCaseQuery}""
                   xmlns:ns4=""{NsJtiCaseQueryExt}""
                   xmlns:ns5=""{NsProxyXsd}""
                   xmlns:ns6=""{NsJxdm}""
                   xmlns:xsi=""{NsXsi}"">
  <SOAP-ENV:Body>
    <ns3:CaseQueryMessage xsi:type=""ns4:CaseQueryMessageTypeExt"">
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>{caseCourtBlock}{trackingBlock}
      <ns3:CaseQueryCriteria>
        <ns3:IncludeParticipantsIndicator>{(includeParticipants ? "true" : "false")}</ns3:IncludeParticipantsIndicator>
        <ns3:IncludeDocketEntryIndicator>{(includeDocketEntries ? "true" : "false")}</ns3:IncludeDocketEntryIndicator>
        <ns3:IncludeCalendarEventIndicator>false</ns3:IncludeCalendarEventIndicator>
        <ns3:DocketEntryTypeCodeFilterText/>
        <ns3:CalendarEventTypeCodeFilterText/>
      </ns3:CaseQueryCriteria>{docketBlock}
    </ns3:CaseQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetCaseList (SearchCases) SOAP request (CourtRecord endpoint).
    /// Uses CaseListQueryMessage with CaseListQueryCaseParticipant for party search.
    /// Overload kept for backward compatibility.
    /// </summary>
    public static string BuildGetCaseListRequest(
        string sendingMdeLocationId,
        string? caseDocketId = null,
        string? partySearchTerm = null,
        string? partyRoleCode = null,
        int? pageSize = null,
        int? offset = null)
    {
        return BuildGetCaseListRequest(sendingMdeLocationId, new Core.Models.CaseSearchCriteria
        {
            CaseDocketId = caseDocketId,
            PartySearchTerm = partySearchTerm,
            PartyRoleCode = partyRoleCode,
            PageSize = pageSize,
            Offset = offset,
        });
    }

    /// <summary>
    /// Build a GetCaseList (SearchCases) SOAP request (CourtRecord endpoint).
    /// Supports search by: case number, party individual (first/last), party business (org name),
    /// title, category, or legacy full-name party search.
    /// </summary>
    public static string BuildGetCaseListRequest(
        string sendingMdeLocationId,
        Core.Models.CaseSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // ── CaseListQueryCase block (docket ID, title, category) ──
        var caseElements = new List<string>();
        if (!string.IsNullOrEmpty(criteria.CaseDocketId))
            caseElements.Add($"<ns1:CaseDocketID>{EscapeXml(criteria.CaseDocketId)}</ns1:CaseDocketID>");
        if (!string.IsNullOrEmpty(criteria.CaseTitle))
            caseElements.Add($"<ns1:CaseTitleText>{EscapeXml(criteria.CaseTitle)}</ns1:CaseTitleText>");
        if (!string.IsNullOrEmpty(criteria.CaseCategoryCode))
            caseElements.Add($"<ns1:CaseCategoryText>{EscapeXml(criteria.CaseCategoryCode)}</ns1:CaseCategoryText>");

        var caseBlock = caseElements.Count > 0
            ? $@"
      <ns4:CaseListQueryCase>
        {string.Join("\n        ", caseElements)}
      </ns4:CaseListQueryCase>"
            : "";

        // ── CaseListQueryCaseParticipant block (person or org) ──
        var participantBlock = "";

        if (!string.IsNullOrEmpty(criteria.FirstName) || !string.IsNullOrEmpty(criteria.LastName))
        {
            // Individual party search — use PersonGivenName + PersonSurName
            var nameElements = new List<string>();
            if (!string.IsNullOrEmpty(criteria.FirstName))
                nameElements.Add($"<ns1:PersonGivenName>{EscapeXml(criteria.FirstName)}</ns1:PersonGivenName>");
            if (!string.IsNullOrEmpty(criteria.LastName))
                nameElements.Add($"<ns1:PersonSurName>{EscapeXml(criteria.LastName)}</ns1:PersonSurName>");

            participantBlock = $@"
      <ns4:CaseListQueryCaseParticipant>
        <ns3:CaseParticipantExt>
          <ns3:referenceId/>
          <ns3:primaryId/>
          <ns1:EntityPerson>
            <ns1:PersonName>
              {string.Join("\n              ", nameElements)}
            </ns1:PersonName>
          </ns1:EntityPerson>
        </ns3:CaseParticipantExt>
      </ns4:CaseListQueryCaseParticipant>";
        }
        else if (!string.IsNullOrEmpty(criteria.OrganizationName))
        {
            // Business party search — use EntityOrganization
            participantBlock = $@"
      <ns4:CaseListQueryCaseParticipant>
        <ns3:CaseParticipantExt>
          <ns3:referenceId/>
          <ns3:primaryId/>
          <ns1:EntityOrganization>
            <ns1:OrganizationName>{EscapeXml(criteria.OrganizationName)}</ns1:OrganizationName>
          </ns1:EntityOrganization>
        </ns3:CaseParticipantExt>
      </ns4:CaseListQueryCaseParticipant>";
        }
        else if (!string.IsNullOrEmpty(criteria.PartySearchTerm))
        {
            // Legacy full-name search
            participantBlock = $@"
      <ns4:CaseListQueryCaseParticipant>
        <ns3:CaseParticipantExt>
          <ns3:referenceId/>
          <ns3:primaryId/>
          <ns1:EntityPerson>
            <ns1:PersonName>
              <ns1:PersonFullName>{EscapeXml(criteria.PartySearchTerm)}</ns1:PersonFullName>
            </ns1:PersonName>
          </ns1:EntityPerson>
        </ns3:CaseParticipantExt>
      </ns4:CaseListQueryCaseParticipant>";
        }

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:ns2=""{NsCommonTypes}""
                   xmlns:ns3=""{NsJtiCaseParticipantExt}""
                   xmlns:ns4=""{NsCaseListQuery}"">
  <SOAP-ENV:Body>
    <ns4:CaseListQueryMessage>{caseBlock}
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>{participantBlock}
    </ns4:CaseListQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Build a GetDocument SOAP request (CourtRecord endpoint).
    /// </summary>
    public static string BuildGetDocumentRequest(
        string sendingMdeLocationId,
        string caseTrackingId,
        string caseDocketId)
    {
        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{NsSoapEnv}""
                   xmlns:ns1=""{NsNiemCore}""
                   xmlns:ns2=""{NsCommonTypes}""
                   xmlns:ns3=""{NsDocumentQuery}"">
  <SOAP-ENV:Body>
    <ns3:DocumentQueryMessage>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>{EscapeXml(sendingMdeLocationId)}</ns1:IdentificationID>
      </ns2:SendingMDELocationID>
      <ns1:CaseTrackingID>{EscapeXml(caseTrackingId)}</ns1:CaseTrackingID>
      <ns1:CaseDocketID>{EscapeXml(caseDocketId)}</ns1:CaseDocketID>
    </ns3:DocumentQueryMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    /// <summary>
    /// Escape special XML characters in a string value.
    /// </summary>
    public static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
