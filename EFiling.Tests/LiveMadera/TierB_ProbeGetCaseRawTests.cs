using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;
using EFiling.Tests.SubsequentFilingRoundTrip;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Tier B diagnostic — capture raw GetCase SOAP response for an arbitrary docket ID
/// (by default, the one currently failing the SF flow: MFL018679).
///
/// <para>
/// <b>Why.</b> When <c>JtiEFilingProvider.GetCaseAsync</c> returns null, the controller
/// surfaces "SOAP call succeeded but case '...' not found in response (parser returned null)".
/// That terminal message tells us nothing about which of the three null-return paths in
/// <c>CaseResponseParser.ParseCaseResponse</c> tripped (SOAP fault / typed Error code !=0 /
/// no recognized case element). To diagnose, we need the raw response XML.
/// </para>
///
/// <para>
/// <b>What this probe does.</b> Calls <see cref="JtiSoapClient.SendAsync"/> directly
/// (bypassing the parser), then writes the response to <c>temp/getcase-{docket}.xml</c>
/// at the repo root and re-runs the parser to report which branch returns null.
/// Output goes to xUnit test output AND a stable file path so the user can open it.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class TierB_ProbeGetCaseRawTests
{
    private readonly ITestOutputHelper _output;

    public TierB_ProbeGetCaseRawTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveMaderaTheory]
    [InlineData("MFL018679")]   // user-reported failure — expect 4011 (case not in staging)
    [InlineData("MCV089014")]   // known-good Civil Limited from 2026-04-23 probe (Ron Jackson)
    [InlineData("MFL018636")]   // known-good Family from 2026-04-23 probe
    [InlineData("MCV089020")]   // Step #17 — CIV-SUB-014 target. Verify
                                // Felicia 1101839 + Thomas Jackson 978051 still attached
                                // before Tier B resubmit.
    [InlineData("MCV089019")]   // Step #18 — CIV-SUB-017 target (Proof
                                // of Personal Service on PI case "Stephen Allen vs.
                                // Thompson Medical Group"). Verify Stephen Allen 978049,
                                // Thompson Medical 978050, Felicia 1101838 still attached.
    [InlineData("MCV089021")]   // Step #20 — CIV-SUB-002 target (Substitution
                                // of Attorney on gov-entity case "County of Placer vs.
                                // Steven Jackson"). Verify County of Placer 978057 (PLAIN
                                // gov entity) and Felicia Espinosa 1101840 (ATT) still
                                // attached before Tier B resubmit.
    [InlineData("MCV089018")]   // Step #23 — CIV-SUB-008 target (Cross-Complaint
                                // on Mark Smith vs. Stephen Williams). Verify Felicia's
                                // primaryId on this case (was new-data NEW_ATTORNEY in
                                // historic submission 26MA00004365 — should now be attached
                                // as ATT). Need her primaryId to refactor to existing-data.
    public async Task Probe_GetCase_DumpRawResponse(string docketId)
    {
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, nameof(Probe_GetCase_DumpRawResponse));

        using var soapClient = new JtiSoapClient();

        // Build the same GetCase request that JtiEFilingProvider.GetCaseAsync would build.
        var mdeLocationId = ExtractHostFromEndpoint(config.CourtRecordEndpoint);
        var requestXml = SoapEnvelopeBuilder.BuildGetCaseRequest(
            sendingMdeLocationId: mdeLocationId,
            caseDocketId: docketId,
            caseTrackingId: null,
            includeParticipants: true,
            includeDocketEntries: false);

        _output.WriteLine($"[Probe] Endpoint: {config.CourtRecordEndpoint}");
        _output.WriteLine($"[Probe] mdeLocationId: {mdeLocationId}");
        _output.WriteLine($"[Probe] Request XML length: {requestXml.Length}");

        string responseXml;
        try
        {
            responseXml = await soapClient.SendAsync(config, config.CourtRecordEndpoint, requestXml, CancellationToken.None);
        }
        catch (JtiSoapException ex)
        {
            _output.WriteLine($"[Probe] JtiSoapException: HTTP {ex.HttpStatusCode}");
            _output.WriteLine($"[Probe] ResponseBody length: {ex.ResponseBody?.Length ?? 0}");
            responseXml = ex.ResponseBody ?? string.Empty;
        }

        _output.WriteLine($"[Probe] Response XML length: {responseXml.Length}");

        // Write to a stable on-disk location for visual inspection.
        var repoRoot = SampleLoader.RepoRoot;
        var tempDir = Path.Combine(repoRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir, $"getcase-{docketId}.xml");
        await File.WriteAllTextAsync(outPath, responseXml);
        _output.WriteLine($"[Probe] Raw response written to: {outPath}");

        // Now re-run the parser and report what it does.
        var parsed = CaseResponseParser.ParseCaseResponse(responseXml);
        if (parsed == null)
        {
            _output.WriteLine($"[Probe] Parser returned NULL — diagnosing branch:");
            _output.WriteLine($"[Probe]   - SOAP Fault present:  {responseXml.Contains("Fault", StringComparison.OrdinalIgnoreCase)}");
            _output.WriteLine($"[Probe]   - <Error> present:     {responseXml.Contains("<Error", StringComparison.OrdinalIgnoreCase) || responseXml.Contains(":Error", StringComparison.OrdinalIgnoreCase)}");
            _output.WriteLine($"[Probe]   - <ErrorCode> present: {responseXml.Contains("ErrorCode", StringComparison.OrdinalIgnoreCase)}");
            _output.WriteLine($"[Probe]   - <ErrorText> present: {responseXml.Contains("ErrorText", StringComparison.OrdinalIgnoreCase)}");
            _output.WriteLine($"[Probe]   - <CivilCaseExt>:      {responseXml.Contains("CivilCaseExt", StringComparison.Ordinal)}");
            _output.WriteLine($"[Probe]   - <CivilCase>:         {responseXml.Contains("CivilCase", StringComparison.Ordinal)}");
            _output.WriteLine($"[Probe]   - <DomesticCase>:      {responseXml.Contains("DomesticCase", StringComparison.Ordinal)}");
            _output.WriteLine($"[Probe]   - <CriminalCase>:      {responseXml.Contains("CriminalCase", StringComparison.Ordinal)}");
            _output.WriteLine($"[Probe]   - <AppellateCase>:     {responseXml.Contains("AppellateCase", StringComparison.Ordinal)}");
            _output.WriteLine($"[Probe]   - <Case>:              {responseXml.Contains("<Case", StringComparison.Ordinal) || responseXml.Contains(":Case", StringComparison.Ordinal)}");

            // Dump the first 2KB so we can quickly see the body shape in xUnit output.
            var snippet = responseXml.Length > 2000 ? responseXml[..2000] : responseXml;
            _output.WriteLine($"[Probe] First 2KB of response:");
            _output.WriteLine(snippet);
        }
        else
        {
            _output.WriteLine($"[Probe] Parser SUCCEEDED — case {parsed.CaseDocketId} parsed with {parsed.Parties.Count} parties + {parsed.Complaints.Count} complaints.");
        }

        Assert.True(true, "Diagnostic probe — always passes; inspect xUnit output and temp/ XML.");
    }

    private static string ExtractHostFromEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return string.Empty;
        try
        {
            return new Uri(endpoint).Host;
        }
        catch
        {
            return string.Empty;
        }
    }
}
