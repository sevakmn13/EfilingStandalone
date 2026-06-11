using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Step #48 — T-4 caseJudgments loader.
///
/// <para>
/// Closes catalog §3.19 promotion criteria item (1): "implement frontend
/// caseJudgments loader (backend endpoint calling JTI GetCaseInformation,
/// projecting CaseType.judgments)". The actual implementation chose
/// server-side eager projection via the existing GetCase call that already
/// powers <c>SubsequentFiling.cshtml</c>'s <c>@model CaseInfo</c> — no new
/// AJAX endpoint needed.
/// </para>
///
/// <para>
/// <b>Wire-shape evidence:</b> JTI "Subsequent Filing - Court Specific
/// Concepts" HTML doc §LASC Post Judgment (lines 282-303) shows the
/// canonical <c>&lt;CaseCourtEvent xsi:type="CourtEventJudgmentType"&gt;</c>
/// shape returned by GetCase for cases with judgments. Synthetic XML
/// below is byte-for-byte faithful to that documented sample (party IDs
/// preserved as in the LASC example: 2562247, 2582793, 5503493, etc.).
/// </para>
///
/// <para>
/// <b>Detection strategy lock:</b> The parser uses the structural
/// discriminator "<c>CaseCourtEvent</c> has a <c>judgmentId</c> direct
/// child". These tests verify it correctly distinguishes
/// <c>CourtEventJudgmentType</c> from generic <c>CourtEventType</c> +
/// other CaseCourtEvent variants that share the same element name but
/// have no <c>judgmentId</c>.
/// </para>
/// </summary>
public class Step48_CaseJudgmentsParsingTests
{
    /// <summary>
    /// LASC Writ of Return reference shape: one judgment with full title,
    /// subCaseReferenceId, and JudgmentAward sub-structure. Verifies parser
    /// extracts the three projected fields (id, title, subCaseRef) and
    /// ignores the award sub-structure.
    /// </summary>
    [Fact]
    public void ParseCaseResponse_LascJudgmentShape_ExtractsJudgmentIdAndTitleAndSubCaseRef()
    {
        var xml = BuildCaseResponseWithJudgments(
            new[]
            {
                ("2562247", "4045592",
                 "Judgment entered on 03/19/2020 for Plaintiff BRIGHT YELLOW against Defendant BLUE GREEN on the Complaint filed by BRIGHT YELLOW on 03/19/2020 for the principal amount of $1,500.00 for a total of $1,500.00.",
                 includeAward: true)
            });

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Single(result!.Judgments);

        var j = result.Judgments[0];
        Assert.Equal("2562247", j.JudgmentId);
        Assert.Equal("4045592", j.SubCaseReferenceId);
        Assert.NotNull(j.JudgmentTitle);
        Assert.Contains("BRIGHT YELLOW", j.JudgmentTitle!);
        Assert.Contains("$1,500.00", j.JudgmentTitle!);
    }

    /// <summary>
    /// Multi-judgment case. WSDL declares <c>CourtEventJudgmentType[]</c>
    /// (array) so a single case can carry multiple judgments. Order
    /// preservation matters for the SF picker UX (judgments rendered in
    /// the order the court returns them, typically chronological).
    /// </summary>
    [Fact]
    public void ParseCaseResponse_MultipleJudgments_ExtractsAllInOrder()
    {
        var xml = BuildCaseResponseWithJudgments(
            new[]
            {
                ("100", "1001", "First judgment entered 01/15/2020", includeAward: false),
                ("200", "1002", "Second judgment entered 06/30/2020", includeAward: false),
                ("300", "1003", "Third judgment entered 12/01/2020", includeAward: false)
            });

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Judgments.Count);
        Assert.Equal("100", result.Judgments[0].JudgmentId);
        Assert.Equal("200", result.Judgments[1].JudgmentId);
        Assert.Equal("300", result.Judgments[2].JudgmentId);
        Assert.Equal("First judgment entered 01/15/2020", result.Judgments[0].JudgmentTitle);
        Assert.Equal("Third judgment entered 12/01/2020", result.Judgments[2].JudgmentTitle);
    }

    /// <summary>
    /// Most cases have no judgments. Parser must return an empty list
    /// (NOT null) so the SF view can render the "No judgments on this
    /// case" empty state without null-checks.
    /// </summary>
    [Fact]
    public void ParseCaseResponse_NoJudgments_ReturnsEmptyList()
    {
        var xml = BuildCaseResponseWithJudgments(Array.Empty<(string, string, string, bool)>());

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.NotNull(result!.Judgments);
        Assert.Empty(result.Judgments);
    }

    /// <summary>
    /// Discriminator test: a <c>CaseCourtEvent</c> that is NOT a judgment
    /// (e.g., a hearing or generic court event) should NOT be projected as
    /// a judgment. Only events with a <c>judgmentId</c> child qualify.
    /// </summary>
    [Fact]
    public void ParseCaseResponse_NonJudgmentCourtEvents_AreIgnored()
    {
        // Mix: one real judgment + one hearing event (no judgmentId) + one generic event.
        // Only the judgment should be projected.
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:CaseResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsCaseResponse}""
                              xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                              xmlns:ns5=""{SoapEnvelopeBuilder.NsCommonTypes}""
                              xmlns:ns6=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}""
                              xmlns:ns18=""{SoapEnvelopeBuilder.NsJtiCourtEventJudgment}""
                              xmlns:xsi=""{SoapEnvelopeBuilder.NsXsi}"">
      <ns6:CivilCaseExt>
        <ns1:CaseTrackingID>77777</ns1:CaseTrackingID>
        <ns1:CaseDocketID>24CV99999</ns1:CaseDocketID>

        <!-- Real judgment (has judgmentId): SHOULD be projected -->
        <ns5:CaseCourtEvent xsi:type=""ns18:CourtEventJudgmentType"">
          <ns18:judgmentId>500</ns18:judgmentId>
          <ns18:judgmentTitle>Real judgment</ns18:judgmentTitle>
        </ns5:CaseCourtEvent>

        <!-- Hearing event (no judgmentId): MUST be ignored -->
        <ns5:CaseCourtEvent>
          <ns1:ActivityDate>
            <ns1:DateTime>2020-08-15T10:00:00</ns1:DateTime>
          </ns1:ActivityDate>
          <ns1:ActivityDescriptionText>Trial Setting Conference</ns1:ActivityDescriptionText>
        </ns5:CaseCourtEvent>

        <!-- Generic court event (no judgmentId): MUST be ignored -->
        <ns5:CaseCourtEvent>
          <ns1:ActivityDescriptionText>Status Conference</ns1:ActivityDescriptionText>
        </ns5:CaseCourtEvent>
      </ns6:CivilCaseExt>
    </ns3:CaseResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Single(result!.Judgments);
        Assert.Equal("500", result.Judgments[0].JudgmentId);
        Assert.Equal("Real judgment", result.Judgments[0].JudgmentTitle);
    }

    /// <summary>
    /// Defensive: a <c>CourtEventJudgmentType</c> with an empty
    /// <c>&lt;judgmentId/&gt;</c> child should NOT be projected. An empty
    /// id is unusable (would fail wire-side as <c>&lt;judgmentId&gt;&lt;/judgmentId&gt;</c>).
    /// </summary>
    [Fact]
    public void ParseCaseResponse_JudgmentWithEmptyId_IsSkipped()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:CaseResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsCaseResponse}""
                              xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                              xmlns:ns5=""{SoapEnvelopeBuilder.NsCommonTypes}""
                              xmlns:ns6=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}""
                              xmlns:ns18=""{SoapEnvelopeBuilder.NsJtiCourtEventJudgment}""
                              xmlns:xsi=""{SoapEnvelopeBuilder.NsXsi}"">
      <ns6:CivilCaseExt>
        <ns1:CaseTrackingID>1</ns1:CaseTrackingID>
        <ns1:CaseDocketID>EMPTY-ID</ns1:CaseDocketID>
        <ns5:CaseCourtEvent xsi:type=""ns18:CourtEventJudgmentType"">
          <ns18:judgmentId></ns18:judgmentId>
          <ns18:judgmentTitle>Should be skipped</ns18:judgmentTitle>
        </ns5:CaseCourtEvent>
        <ns5:CaseCourtEvent xsi:type=""ns18:CourtEventJudgmentType"">
          <ns18:judgmentId>999</ns18:judgmentId>
          <ns18:judgmentTitle>Should be kept</ns18:judgmentTitle>
        </ns5:CaseCourtEvent>
      </ns6:CivilCaseExt>
    </ns3:CaseResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Single(result!.Judgments);
        Assert.Equal("999", result.Judgments[0].JudgmentId);
    }

    /// <summary>
    /// Defensive: duplicate judgmentIds (shouldn't happen in well-formed
    /// responses, but defensive de-dup prevents UI duplicate-option bugs).
    /// </summary>
    [Fact]
    public void ParseCaseResponse_DuplicateJudgmentIds_AreDeduplicated()
    {
        var xml = BuildCaseResponseWithJudgments(
            new[]
            {
                ("777", "1", "First copy", includeAward: false),
                ("777", "1", "Second copy (dup)", includeAward: false),
                ("888", "2", "Different judgment", includeAward: false)
            });

        var result = CaseResponseParser.ParseCaseResponse(xml);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Judgments.Count);
        Assert.Equal("777", result.Judgments[0].JudgmentId);
        Assert.Equal("First copy", result.Judgments[0].JudgmentTitle);
        Assert.Equal("888", result.Judgments[1].JudgmentId);
    }

    // ─── Test helpers ──────────────────────────────────────────────

    /// <summary>
    /// Build a synthetic GetCase response with the given judgments
    /// embedded as <c>&lt;CaseCourtEvent xsi:type="CourtEventJudgmentType"&gt;</c>
    /// children. Mirrors the JTI HTML LASC Post Judgment doc shape
    /// (lines 282-303).
    /// </summary>
    private static string BuildCaseResponseWithJudgments(
        IEnumerable<(string id, string subCaseRef, string title, bool includeAward)> judgments)
    {
        var courtEventsXml = string.Join("\n        ", judgments.Select(j =>
        {
            var award = j.includeAward
                ? $@"
          <ns18:JudgmentAward>
            <ns18:JudgmentAwardId>2582793</ns18:JudgmentAwardId>
            <ns18:JudgmentAwardParty>
              <ns18:JudgmentAwardPartyId>5503493</ns18:JudgmentAwardPartyId>
              <ns18:PartyId>14999745</ns18:PartyId>
            </ns18:JudgmentAwardParty>
          </ns18:JudgmentAward>"
                : string.Empty;

            return $@"<ns5:CaseCourtEvent xsi:type=""ns18:CourtEventJudgmentType"">
          <ns18:judgmentId>{System.Security.SecurityElement.Escape(j.id)}</ns18:judgmentId>
          <ns18:subCaseReferenceId>{System.Security.SecurityElement.Escape(j.subCaseRef)}</ns18:subCaseReferenceId>
          <ns18:judgmentTitle>{System.Security.SecurityElement.Escape(j.title)}</ns18:judgmentTitle>{award}
        </ns5:CaseCourtEvent>";
        }));

        return $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:CaseResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsCaseResponse}""
                              xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                              xmlns:ns5=""{SoapEnvelopeBuilder.NsCommonTypes}""
                              xmlns:ns6=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}""
                              xmlns:ns18=""{SoapEnvelopeBuilder.NsJtiCourtEventJudgment}""
                              xmlns:xsi=""{SoapEnvelopeBuilder.NsXsi}"">
      <ns6:CivilCaseExt>
        <ns1:CaseTrackingID>99999</ns1:CaseTrackingID>
        <ns1:CaseDocketID>TEST-JUDGMENT-CASE</ns1:CaseDocketID>
        <ns1:CaseTitleText>Test v. Test</ns1:CaseTitleText>
        {courtEventsXml}
      </ns6:CivilCaseExt>
    </ns3:CaseResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }
}
