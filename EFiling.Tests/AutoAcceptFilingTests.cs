using EFiling.Core.Caching;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;
using EFiling.Tests.LiveMadera;
using EFiling.Tests.SubsequentFilingRoundTrip;
using Xunit.Abstractions;

namespace EFiling.Tests;

/// <summary>
/// Test to submit a filing with the JTI "Auto Accept" SOAP header to Madera staging.
/// This bypasses clerk review — JTI auto-accepts the filing and sends an NFRC immediately.
/// Per JTI docs: Add <status xmlns="com.journaltech.niem.test">This is a test filing</status> to SOAP Header.
/// WARNING: Creates a REAL filing in staging.
///
/// <para>
/// <b>Live-Madera gating:</b> All tests except <see cref="GenerateAutoAcceptXml_LogOutput"/>
/// issue real SOAP calls to Madera staging. They use <see cref="LiveMaderaFactAttribute"/> so they skip
/// unless the opt-in env var is set. See <see cref="LiveMaderaOptIn"/>.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class AutoAcceptFilingTests
{
    private readonly ITestOutputHelper _output;

    public AutoAcceptFilingTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    private static FilingSubmission BuildAutoAcceptSubmission()
    {
        var sub = new FilingSubmission
        {
            FilingType = EFiling.Core.Enums.FilingType.Initial,
            EfspReferenceId = $"AUTOTEST-{Guid.NewGuid():N}",
            SubmitterUsername = "legalhub",

            // Family Law/Support > Legal Separation w/o Minor Child
            CaseTypeCode = "211110",
            CaseCategoryCode = "212120",

            LocationCode = "M",
            LocationName = "Madera Courthouse",
            IncidentZipCode = "93637",
        };

        // Attorney
        sub.Parties.Add(new FilingParty
        {
            ReferenceId = "attorney0",
            RoleCode = "ATT",
            FirstName = "Felicia",
            MiddleName = "A",
            LastName = "Espinosa",
            BarNumber = "267198",
            Contact = new ContactInfo
            {
                MailingAddress = new StructuredAddress
                {
                    AddressType = "ML",
                    Address1 = "2115",
                    Address2 = "Kern St",
                    City = "Fresno",
                    State = "CA",
                    Zip = "93721"
                },
                PhoneNumber = "5594418721",
                PhoneType = "UNK",
                Email = "test@mail.com"
            }
        });

        // Filing party
        sub.Parties.Add(new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "APLNT",
            FirstName = "AutoAccept",
            LastName = "TestPerson"
        });

        // Opposing party
        sub.Parties.Add(new FilingParty
        {
            ReferenceId = "filedAsTo0",
            RoleCode = "AGENCY",
            FirstName = "Opposing",
            LastName = "TestPerson"
        });

        // Attorney-party association
        sub.PartyAssociations.Add(new PartyAssociation
        {
            AssociationType = "REPRESENTEDBY",
            ParticipantRef = "filedBy0",
            RelatedParticipantRef = "attorney0"
        });

        // Lead document
        sub.LeadDocument = new FilingDocument
        {
            ReferenceId = "doc0",
            DocumentCode = "218620",
            FileControlId = $"doc-{Guid.NewGuid():N}",
            SequenceNumber = 0,
            BinaryLocationUri = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"
        };

        // Party-document associations
        sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
        {
            AssociationType = "FILEDBY",
            ParticipantRef = "filedBy0",
            DocumentRef = "doc0"
        });
        sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
        {
            AssociationType = "REFERS_TO",
            ParticipantRef = "filedAsTo0",
            DocumentRef = "doc0"
        });

        // Payment
        sub.Payment = new FilingPayment
        {
            CustomerProfileId = "0",
            CustomerPaymentProfileId = "0",
            PaymentType = "ACH"
        };

        return sub;
    }

    /// <summary>
    /// Inject the JTI auto-accept header into the SOAP envelope XML.
    /// Replaces empty <S:Header/> or <SOAP-ENV:Header/> with the test status element.
    /// </summary>
    private static string InjectAutoAcceptHeader(string xml)
    {
        // The builder produces: <SOAP-ENV:Header />
        // Replace with the auto-accept header
        var autoAcceptHeader =
            """<SOAP-ENV:Header><status xmlns="com.journaltech.niem.test">This is a test filing</status></SOAP-ENV:Header>""";

        // Handle both self-closing and open/close forms
        xml = xml.Replace("<SOAP-ENV:Header />", autoAcceptHeader);
        xml = xml.Replace("<SOAP-ENV:Header/>", autoAcceptHeader);
        xml = xml.Replace("<SOAP-ENV:Header></SOAP-ENV:Header>", autoAcceptHeader);

        return xml;
    }

    /// <summary>
    /// Inject the JTI auto-reject header into the SOAP envelope XML. Mirror of
    /// <see cref="InjectAutoAcceptHeader"/> for symmetric SF auto-reject tests.
    /// <para>
    /// <b>R2 caveat (NFRC audit plan §13).</b> Vendor docs only document the
    /// auto-reject SOAP header for case-initiation filings. JTI staging behavior
    /// for SF auto-reject is unverified. If Madera ignores the header for SF, the
    /// filing will sit in <c>RECEIVED_UNDER_REVIEW</c> and no rejected NFRC will
    /// arrive — observational outcome, not a test failure.
    /// </para>
    /// </summary>
    private static string InjectAutoRejectHeader(string xml)
    {
        var autoRejectHeader =
            """<SOAP-ENV:Header><status xmlns="com.journaltech.niem.test">Auto Reject Filing</status></SOAP-ENV:Header>""";
        xml = xml.Replace("<SOAP-ENV:Header />", autoRejectHeader);
        xml = xml.Replace("<SOAP-ENV:Header/>", autoRejectHeader);
        xml = xml.Replace("<SOAP-ENV:Header></SOAP-ENV:Header>", autoRejectHeader);
        return xml;
    }

    /// <summary>
    /// Build a Subsequent Filing submission for live Madera capture by reusing the
    /// curated <c>FAM-SUB-001</c> scenario. Pipeline:
    /// <list type="number">
    ///   <item>Parse the Placer baseline XML via <see cref="ScenarioFixtures.LoadSubmission(string)"/>.</item>
    ///   <item>Apply common Madera substitutions (username, EFSP ref) via <see cref="MaderaLiveFixtures.ApplyCommonOverrides"/>.</item>
    ///   <item>Apply the FAM-SUB-001 scenario-specific override (CaseDocketId=MFL018634,
    ///         ComplaintId=782712, RES filer remap to Mark Williams 978019, attorney
    ///         remap to Felicia Espinosa 267198, doc code 258110 Response).</item>
    /// </list>
    /// <para>
    /// Throws if FAM-SUB-001 has no MaderaLiveFixtures override registered — without
    /// the curated remaps the submission's idReferences and party identities will not
    /// match Madera's MFL018634 case and the filing would be rejected for unrelated reasons.
    /// </para>
    /// </summary>
    private static FilingSubmission BuildAutoAcceptSubsequentFilingSubmission()
    {
        const string scenarioId = "FAM-SUB-001";
        var sub = ScenarioFixtures.LoadSubmission(scenarioId);
        MaderaLiveFixtures.ApplyCommonOverrides(sub, scenarioId);
        if (!MaderaLiveFixtures.TryGetScenarioOverride(scenarioId, out var scenarioOverride) || scenarioOverride is null)
            throw new InvalidOperationException(
                $"Phase 3 SF fixture capture cannot run: scenario {scenarioId} has no MaderaLiveFixtures override. " +
                "Without the curated remaps the SF submission cannot attach to MFL018634. Curate FAM-SUB-001 first.");
        scenarioOverride(sub);
        return sub;
    }

    /// <summary>
    /// Test 1: Generate the auto-accept XML and log it — verify the header is present.
    /// </summary>
    [Fact]
    public void GenerateAutoAcceptXml_LogOutput()
    {
        var config = MaderaConfig;
        var submission = BuildAutoAcceptSubmission();

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        xml = InjectAutoAcceptHeader(xml);

        _output.WriteLine("=== AUTO-ACCEPT REVIEW FILING XML ===");
        _output.WriteLine(xml);
        _output.WriteLine($"\n=== XML Length: {xml.Length} chars ===");

        Assert.Contains("This is a test filing", xml);
        Assert.Contains("com.journaltech.niem.test", xml);
        Assert.Contains("ReviewFilingRequestMessage", xml);
    }

    /// <summary>
    /// Test 2: Submit with auto-accept header to Madera staging.
    /// If Madera supports auto-accept, we should get an immediate ACCEPTED status
    /// and an NFRC should be sent to our callback URL.
    /// </summary>
    [LiveMaderaFact]
    public async Task SubmitAutoAcceptFiling_LiveMadera()
    {
        var config = MaderaConfig;
        var submission = BuildAutoAcceptSubmission();

        // Build XML and inject auto-accept header
        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        requestXml = InjectAutoAcceptHeader(requestXml);

        _output.WriteLine("=== REQUEST XML (with auto-accept header) ===");
        _output.WriteLine(requestXml);
        _output.WriteLine("");

        // Send directly via SOAP client (bypassing provider since we modified the XML)
        using var soapClient = new JtiSoapClient();

        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.SoapEndpoint, requestXml);
        }
        catch (JtiSoapException soapEx)
        {
            _output.WriteLine($"=== SOAP EXCEPTION ===");
            _output.WriteLine($"HTTP Status: {soapEx.HttpStatusCode}");
            _output.WriteLine($"Response Body:\n{soapEx.ResponseBody}");
            throw;
        }

        _output.WriteLine("=== RESPONSE XML ===");
        _output.WriteLine(responseXml);

        // Parse the response
        var result = FilingResponseParser.ParseMessageReceipt(responseXml);
        _output.WriteLine("");
        _output.WriteLine("=== PARSED RESULT ===");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"ErrorCode: {result.ErrorCode}");
        _output.WriteLine($"ErrorText: {result.ErrorText ?? "(null)"}");
        _output.WriteLine("");
        _output.WriteLine($">>> NFRC callback URL: {config.NfrcCallbackUrl}");
        _output.WriteLine($">>> If auto-accept works, check your app logs for an incoming NFRC POST within seconds.");

        Assert.True(result.Success, $"Filing failed: {result.ErrorText}");
    }

    /// <summary>
    /// Test 3: Check filing status via provider (now uses GetRecordingStatus internally).
    /// </summary>
    [LiveMaderaFact]
    public async Task CheckAutoAcceptFilingStatus_ViaProvider()
    {
        var config = MaderaConfig;
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        var result = await provider.GetFilingStatusAsync(config, efmReferenceId: "26MA00003739");

        _output.WriteLine("=== FILING STATUS (via GetRecordingStatus) ===");
        _output.WriteLine($"FilingStatus: {result.FilingStatus}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"CaseDocketId (CaseNumber): {result.CaseDocketId ?? "(null)"}");
        _output.WriteLine($"CaseTrackingId (CaseName): {result.CaseTrackingId ?? "(null)"}");

        Assert.Equal(FilingStatus.Accepted, result.FilingStatus);
        Assert.Equal("26MA00003739", result.EfmReferenceId);
        Assert.NotNull(result.CaseDocketId);
    }

    /// <summary>
    /// Test: Check status of latest filing from the app (26MA00003742).
    /// </summary>
    [LiveMaderaFact]
    public async Task CheckLatestFiling_26MA00003742()
    {
        var config = MaderaConfig;
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        var result = await provider.GetFilingStatusAsync(config, efmReferenceId: "26MA00003742");

        _output.WriteLine("=== FILING STATUS for 26MA00003742 ===");
        _output.WriteLine($"FilingStatus: {result.FilingStatus}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"CaseDocketId: {result.CaseDocketId ?? "(null)"}");
        _output.WriteLine($"CaseTrackingId: {result.CaseTrackingId ?? "(null)"}");
        _output.WriteLine($"RawXml snippet: {result.RawXml?.Substring(0, Math.Min(500, result.RawXml?.Length ?? 0))}");
    }

    /// <summary>
    /// Test 4: Submit with auto-REJECT header to Madera staging.
    /// Tests the rejection flow end-to-end.
    /// </summary>
    [LiveMaderaFact]
    public async Task SubmitAutoRejectFiling_LiveMadera()
    {
        var config = MaderaConfig;
        var submission = BuildAutoAcceptSubmission();
        submission.EfspReferenceId = $"AUTOREJECT-{Guid.NewGuid():N}";

        // Build XML and inject auto-reject header
        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        var autoRejectHeader =
            """<SOAP-ENV:Header><status xmlns="com.journaltech.niem.test">Auto Reject Filing</status></SOAP-ENV:Header>""";
        requestXml = requestXml.Replace("<SOAP-ENV:Header />", autoRejectHeader);
        requestXml = requestXml.Replace("<SOAP-ENV:Header/>", autoRejectHeader);
        requestXml = requestXml.Replace("<SOAP-ENV:Header></SOAP-ENV:Header>", autoRejectHeader);

        _output.WriteLine("=== REQUEST XML (with auto-reject header) ===");
        _output.WriteLine(requestXml);
        _output.WriteLine("");

        using var soapClient = new JtiSoapClient();

        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.SoapEndpoint, requestXml);
        }
        catch (JtiSoapException soapEx)
        {
            _output.WriteLine($"=== SOAP EXCEPTION ===");
            _output.WriteLine($"HTTP Status: {soapEx.HttpStatusCode}");
            _output.WriteLine($"Response Body:\n{soapEx.ResponseBody}");
            throw;
        }

        _output.WriteLine("=== RESPONSE XML ===");
        _output.WriteLine(responseXml);

        var result = FilingResponseParser.ParseMessageReceipt(responseXml);
        _output.WriteLine("");
        _output.WriteLine("=== PARSED RESULT ===");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"ErrorCode: {result.ErrorCode}");
        _output.WriteLine($"ErrorText: {result.ErrorText ?? "(null)"}");
        _output.WriteLine("");
        _output.WriteLine($">>> NFRC callback URL: {config.NfrcCallbackUrl}");
        _output.WriteLine($">>> If auto-reject works, check your app logs for an incoming NFRC POST with REJECTED status.");

        Assert.True(result.Success, $"Filing failed: {result.ErrorText}");
    }

    /// <summary>
    /// Test 5: Check filing status of the auto-reject filing.
    /// </summary>
    [LiveMaderaFact]
    public async Task CheckAutoRejectFilingStatus_ViaProvider()
    {
        var config = MaderaConfig;
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        var result = await provider.GetFilingStatusAsync(config, efmReferenceId: "26MA00003740");

        _output.WriteLine("=== FILING STATUS (auto-reject via GetRecordingStatus) ===");
        _output.WriteLine($"FilingStatus: {result.FilingStatus}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"CaseDocketId (CaseNumber): {result.CaseDocketId ?? "(null)"}");
        _output.WriteLine($"CaseTrackingId (CaseName): {result.CaseTrackingId ?? "(null)"}");
        _output.WriteLine($"RawXml snippet: {result.RawXml?.Substring(0, Math.Min(500, result.RawXml?.Length ?? 0))}");
    }

    // ─── NFRC audit Phase 3 — Subsequent Filing live captures ─────────────
    //
    // Two new live tests that mirror the CC accept/reject pattern but use the
    // curated FAM-SUB-001 scenario to attach an SF to existing Madera case
    // MFL018634 (FAM-INI-001 / Jessica Williams vs. Mark Williams). Submission
    // path goes through raw JtiSoapClient.SendAsync (bypassing the controller's
    // order-record-creation path) so the JTI test SOAP header can be injected
    // before the wire send. Resulting NFRCs land at the ngrok callback URL,
    // get triaged by Phase 0 instrumentation, and persist to EFilingNfrcLog
    // regardless of match status — the raw XML is what we extract as fixtures.
    //
    // Audit posture: NO assumptions about NFRC arrival timing, content, or
    // matching outcome. Only floor-level assertion (SOAP submit returned a
    // parseable success response). Everything else is observational and gets
    // logged for post-hoc fixture-mining.
    //
    // R2 risk for SF reject: vendor docs only confirm auto-reject for CC.
    // If Madera ignores the SF auto-reject header, the filing will accept and
    // sit in RECEIVED_UNDER_REVIEW — observational outcome, captured in logs.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 3 fixture capture: SF auto-accept against curated FAM-SUB-001 →
    /// MFL018634. Submits via raw SOAP with the JTI auto-accept header injected.
    /// </summary>
    [LiveMaderaFact]
    public async Task SubmitAutoAcceptSubsequentFiling_LiveMadera()
    {
        var config = MaderaConfig;
        var submission = BuildAutoAcceptSubsequentFilingSubmission();
        submission.EfspReferenceId = $"SFAUTOTEST-{Guid.NewGuid():N}";

        _output.WriteLine("=== SF AUTO-ACCEPT — FAM-SUB-001 → MFL018634 ===");
        _output.WriteLine($"EfspReferenceId: {submission.EfspReferenceId}");
        _output.WriteLine($"FilingType: {submission.FilingType}");
        _output.WriteLine($"CaseDocketId: {submission.CaseDocketId ?? "(null)"}");
        _output.WriteLine($"ComplaintId: {submission.ComplaintId ?? "(null)"}");
        _output.WriteLine($"LeadDocument.DocumentCode: {submission.LeadDocument?.DocumentCode ?? "(null)"}");
        _output.WriteLine("");

        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        requestXml = InjectAutoAcceptHeader(requestXml);

        _output.WriteLine("=== REQUEST XML (SF + auto-accept header) ===");
        _output.WriteLine(requestXml);
        _output.WriteLine("");

        using var soapClient = new JtiSoapClient();

        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.SoapEndpoint, requestXml);
        }
        catch (JtiSoapException soapEx)
        {
            _output.WriteLine($"=== SOAP EXCEPTION ===");
            _output.WriteLine($"HTTP Status: {soapEx.HttpStatusCode}");
            _output.WriteLine($"Response Body:\n{soapEx.ResponseBody}");
            throw;
        }

        _output.WriteLine("=== RESPONSE XML ===");
        _output.WriteLine(responseXml);

        var result = FilingResponseParser.ParseMessageReceipt(responseXml);
        _output.WriteLine("");
        _output.WriteLine("=== PARSED RESULT (observational) ===");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"ErrorCode: {result.ErrorCode}");
        _output.WriteLine($"ErrorText: {result.ErrorText ?? "(null)"}");
        _output.WriteLine("");
        _output.WriteLine($">>> NFRC callback URL: {config.NfrcCallbackUrl}");
        _output.WriteLine($">>> Phase 3 mining query: SELECT * FROM EFilingNfrcLog WHERE EfspReferenceId='{submission.EfspReferenceId}' OR EfmReferenceId='{result.EfmReferenceId}' ORDER BY ReceivedUtc;");
        _output.WriteLine($">>> Audit posture: NFRC arrival timing, content, match outcome are observational. Wait minutes-to-hours.");

        // Floor assertion only — anything beyond this is observational. If the SOAP
        // submit fails, fixture capture is impossible and Phase 3 must stop here.
        Assert.True(result.Success,
            $"SF auto-accept submit failed at the SOAP level: ErrorCode={result.ErrorCode}, ErrorText={result.ErrorText}. " +
            $"Phase 3 SF accept fixture cannot be captured without a successful submit. Investigate via the request/response XML logged above.");
    }

    /// <summary>
    /// Phase 3 diagnostic: query Madera's <c>GetRecordingStatus</c> for the four
    /// EFM refs produced by the Phase 3 live submissions to learn what state
    /// each is in — useful when expected NFRCs don't arrive in a timely window.
    /// Pure observation: dumps each filing's status, docket id, and case-tracking
    /// id without asserting anything beyond the SOAP call succeeding.
    /// <para>EFM refs are passed via the <c>PHASE3_EFM_REFS</c> env var as a
    /// comma-separated list, e.g. <c>26MA00004474,26MA00004475,26MA00004476,26MA00004477</c>.
    /// Falls back to a default comma-separated set if unset.</para>
    /// </summary>
    [LiveMaderaFact]
    public async Task DiagnoseFilingStatuses_Phase3()
    {
        var config = MaderaConfig;
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());

        var raw = Environment.GetEnvironmentVariable("PHASE3_EFM_REFS");
        var efmRefs = !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "26MA00004474", "26MA00004475", "26MA00004476", "26MA00004477" };

        _output.WriteLine($"=== PHASE 3 DIAGNOSTIC — querying GetFilingStatus for {efmRefs.Length} EFM refs ===");
        foreach (var efm in efmRefs)
        {
            try
            {
                var result = await provider.GetFilingStatusAsync(config, efmReferenceId: efm);
                _output.WriteLine($"EFM={efm}: FilingStatus={result.FilingStatus}, " +
                                  $"CaseDocketId={result.CaseDocketId ?? "(null)"}, " +
                                  $"CaseTrackingId={result.CaseTrackingId ?? "(null)"}, " +
                                  $"EfspReferenceId={result.EfspReferenceId ?? "(null)"}");
            }
            catch (JtiSoapException soapEx)
            {
                _output.WriteLine($"EFM={efm}: SOAP EXCEPTION HTTP={soapEx.HttpStatusCode}");
                _output.WriteLine($"   ResponseBody snippet: {soapEx.ResponseBody?.Substring(0, Math.Min(400, soapEx.ResponseBody?.Length ?? 0))}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"EFM={efm}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Phase 3 diagnostic: ask Madera to RE-SEND NFRC #1 for the supplied EFM refs
    /// via the raw <c>GetNFRC</c> SOAP call. Captures and prints the raw response
    /// XML so we can see exactly what Madera returned, bypassing any parser quirk.
    /// Also reports what <see cref="FilingResponseParser.ParseNfrcResponse"/>
    /// interpreted that response as (Success/ErrorText).
    /// <para>EFM refs supplied via <c>PHASE3_EFM_REFS</c> env var; defaults to the
    /// 2 accept refs that didn't arrive in the initial 13-min window.</para>
    /// </summary>
    [LiveMaderaFact]
    public async Task RequestNfrcRedelivery_Phase3()
    {
        var config = MaderaConfig;
        using var soapClient = new JtiSoapClient();

        var raw = Environment.GetEnvironmentVariable("PHASE3_EFM_REFS");
        var efmRefs = !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "26MA00004474", "26MA00004475" };

        _output.WriteLine($"=== PHASE 3 DIAGNOSTIC — RequestNfrc (raw) for {efmRefs.Length} EFM refs ===");
        foreach (var efm in efmRefs)
        {
            _output.WriteLine($"--- EFM={efm} ---");
            var requestXml = SoapEnvelopeBuilder.BuildGetNfrcRequest(efmReferenceId: efm, efspReferenceId: null);
            try
            {
                var responseXml = await soapClient.SendAsync(config, config.SoapEndpoint, requestXml);
                // Extract NFRCResponseMessage text — Madera's semantic signal
                string responseMsg = "(missing)";
                var msgIdx = responseXml.IndexOf("NFRCResponseMessage>", StringComparison.Ordinal);
                if (msgIdx >= 0)
                {
                    var msgEnd = responseXml.IndexOf("</", msgIdx);
                    if (msgEnd > msgIdx)
                        responseMsg = responseXml.Substring(msgIdx + 20, msgEnd - msgIdx - 20);
                }
                // Extract Error code+text
                string errCode = "(missing)", errMsg = "(missing)";
                var codeIdx = responseXml.IndexOf("ErrorCode>", StringComparison.Ordinal);
                if (codeIdx >= 0)
                {
                    var codeEnd = responseXml.IndexOf("</", codeIdx);
                    if (codeEnd > codeIdx) errCode = responseXml.Substring(codeIdx + 10, codeEnd - codeIdx - 10);
                }
                var textIdx = responseXml.IndexOf("ErrorText>", StringComparison.Ordinal);
                if (textIdx >= 0)
                {
                    var textEnd = responseXml.IndexOf("</", textIdx);
                    if (textEnd > textIdx) errMsg = responseXml.Substring(textIdx + 10, textEnd - textIdx - 10);
                }
                var (success, errText) = FilingResponseParser.ParseNfrcResponse(responseXml);
                _output.WriteLine($"EFM={efm}: parser={success} | ErrorCode={errCode} | ErrorText={errMsg} | Message=\"{responseMsg}\"");
            }
            catch (JtiSoapException soapEx)
            {
                _output.WriteLine($"EFM={efm}: SOAP EXCEPTION HTTP={soapEx.HttpStatusCode}");
                var body = soapEx.ResponseBody ?? "";
                _output.WriteLine($"   ResponseBody ({body.Length} bytes): {body.Substring(0, Math.Min(2000, body.Length))}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"EFM={efm}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
            _output.WriteLine("");
        }
    }

    /// <summary>
    /// Phase 3 fixture capture: SF auto-reject against curated FAM-SUB-001 →
    /// MFL018634. Same payload as the auto-accept variant but with the auto-reject
    /// JTI test header. <b>R2 caveat:</b> JTI staging behavior for SF auto-reject
    /// is unverified — if Madera ignores the header, this test still passes the
    /// floor assertion (SOAP succeeded) but no rejected NFRC will arrive.
    /// </summary>
    [LiveMaderaFact]
    public async Task SubmitAutoRejectSubsequentFiling_LiveMadera()
    {
        var config = MaderaConfig;
        var submission = BuildAutoAcceptSubsequentFilingSubmission();
        submission.EfspReferenceId = $"SFAUTOREJECT-{Guid.NewGuid():N}";

        _output.WriteLine("=== SF AUTO-REJECT — FAM-SUB-001 → MFL018634 ===");
        _output.WriteLine($"EfspReferenceId: {submission.EfspReferenceId}");
        _output.WriteLine($"FilingType: {submission.FilingType}");
        _output.WriteLine($"CaseDocketId: {submission.CaseDocketId ?? "(null)"}");
        _output.WriteLine($"ComplaintId: {submission.ComplaintId ?? "(null)"}");
        _output.WriteLine("");

        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        requestXml = InjectAutoRejectHeader(requestXml);

        _output.WriteLine("=== REQUEST XML (SF + auto-reject header) ===");
        _output.WriteLine(requestXml);
        _output.WriteLine("");

        using var soapClient = new JtiSoapClient();

        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.SoapEndpoint, requestXml);
        }
        catch (JtiSoapException soapEx)
        {
            _output.WriteLine($"=== SOAP EXCEPTION ===");
            _output.WriteLine($"HTTP Status: {soapEx.HttpStatusCode}");
            _output.WriteLine($"Response Body:\n{soapEx.ResponseBody}");
            throw;
        }

        _output.WriteLine("=== RESPONSE XML ===");
        _output.WriteLine(responseXml);

        var result = FilingResponseParser.ParseMessageReceipt(responseXml);
        _output.WriteLine("");
        _output.WriteLine("=== PARSED RESULT (observational) ===");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"ErrorCode: {result.ErrorCode}");
        _output.WriteLine($"ErrorText: {result.ErrorText ?? "(null)"}");
        _output.WriteLine("");
        _output.WriteLine($">>> NFRC callback URL: {config.NfrcCallbackUrl}");
        _output.WriteLine($">>> Phase 3 mining query: SELECT * FROM EFilingNfrcLog WHERE EfspReferenceId='{submission.EfspReferenceId}' OR EfmReferenceId='{result.EfmReferenceId}' ORDER BY ReceivedUtc;");
        _output.WriteLine($">>> R2 risk: vendor only documents auto-reject for CC. If Madera ignores SF auto-reject, NO rejected NFRC will arrive — file will sit in RECEIVED_UNDER_REVIEW. Observe and document.");

        Assert.True(result.Success,
            $"SF auto-reject submit failed at the SOAP level: ErrorCode={result.ErrorCode}, ErrorText={result.ErrorText}. " +
            $"Phase 3 SF reject fixture cannot be captured without a successful submit. Investigate via the request/response XML logged above.");
    }
}
