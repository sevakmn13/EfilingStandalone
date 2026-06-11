using EFiling.Core.Models;
using EFiling.Nop.Domain;
using EFiling.Nop.Services;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for <see cref="NfrcCallbackTriage"/> — the pure-function triage
/// layer introduced in Phase 0 of the NFRC audit. Validates that every NFRC
/// callback gets a categorized <c>MatchAttemptResult</c> and a fully-populated
/// log entity, regardless of whether the callback could be matched.
///
/// <para>Keep these tests free of DI / mocks: <see cref="NfrcCallbackTriage"/>
/// is intentionally static so the controller path stays thin and the decision
/// logic is verifiable without a request pipeline.</para>
/// </summary>
public class NfrcCallbackTriageTests
{
    // ─── DetermineMatchResult: parse / fault precedence ─────────────────

    [Fact]
    public void DetermineMatchResult_NullParsed_ReturnsParseFailed()
    {
        // Parser threw — caller passes null. Should categorize as ParseFailed
        // even before considering the (necessarily-null) matched record.
        var result = NfrcCallbackTriage.DetermineMatchResult(parsed: null, matchedRecord: null);

        Assert.Equal(NfrcCallbackTriage.MatchResult.ParseFailed, result);
    }

    [Fact]
    public void DetermineMatchResult_ErrorStatusCode_ReturnsSoapFault()
    {
        // FilingStatusCode == "ERROR" is the parser's signal for SOAP fault /
        // unrecognized envelope (see NfrcResponseParser fault-detection path).
        var parsed = new NfrcResult { FilingStatusCode = "ERROR" };

        var result = NfrcCallbackTriage.DetermineMatchResult(parsed, matchedRecord: null);

        Assert.Equal(NfrcCallbackTriage.MatchResult.SoapFault, result);
    }

    [Fact]
    public void DetermineMatchResult_SoapFault_TakesPrecedenceOverMatch()
    {
        // Defensive guard: if a controller bug ever paired a SOAP fault with a
        // "matched" record, we must still classify it as SoapFault — a fault
        // body is structurally invalid and any "match" would be meaningless.
        var parsed = new NfrcResult { FilingStatusCode = "ERROR", EfspReferenceId = "EFSP-1" };
        var record = new EFilingOrderRecord { Id = 42 };

        var result = NfrcCallbackTriage.DetermineMatchResult(parsed, record);

        Assert.Equal(NfrcCallbackTriage.MatchResult.SoapFault, result);
    }

    // ─── DetermineMatchResult: happy path ──────────────────────────────

    [Fact]
    public void DetermineMatchResult_WithMatchedRecord_ReturnsMatched()
    {
        var parsed = new NfrcResult
        {
            FilingStatusCode = "ACCEPTED",
            EfspReferenceId = "EFSP-1",
            EfmReferenceId = "EFM-2",
        };
        var record = new EFilingOrderRecord { Id = 7 };

        var result = NfrcCallbackTriage.DetermineMatchResult(parsed, record);

        Assert.Equal(NfrcCallbackTriage.MatchResult.Matched, result);
    }

    // ─── DetermineMatchResult: unmatched categorization ────────────────

    [Theory]
    [InlineData(null, null, "Unmatched_NoIds")]
    [InlineData("", "", "Unmatched_NoIds")]            // empty strings treated as missing
    [InlineData("EFSP-1", null, "Unmatched_EfspOnly")]
    [InlineData(null, "EFM-1", "Unmatched_EfmOnly")]
    [InlineData("EFSP-1", "EFM-1", "Unmatched_Both")]
    public void DetermineMatchResult_UnmatchedCategorization(string? efsp, string? efm, string expected)
    {
        // Parsed callback with no order match — must be categorized by which
        // reference IDs were present so support staff can SQL-filter by
        // 'Unmatched_NoIds' vs 'Unmatched_EfspOnly' vs 'Unmatched_EfmOnly' vs 'Unmatched_Both'.
        var parsed = new NfrcResult
        {
            FilingStatusCode = "ACCEPTED",
            EfspReferenceId = efsp,
            EfmReferenceId = efm,
        };

        var result = NfrcCallbackTriage.DetermineMatchResult(parsed, matchedRecord: null);

        Assert.Equal(expected, result);
    }

    // ─── BuildLog: field population ─────────────────────────────────────

    [Fact]
    public void BuildLog_UnmatchedCallback_PopulatesDiagnosticFields()
    {
        // The whole point of Phase 0: when a callback can't be matched,
        // BuildLog must still snapshot the diagnostic context (refs, IP,
        // content-type, body length, categorized result) so support can
        // recover the filing later.
        var rawXml = "<SOAP-ENV:Envelope>...</SOAP-ENV:Envelope>";
        var parsed = new NfrcResult
        {
            FilingStatusCode = "ACCEPTED",
            EfspReferenceId = "EFSP-LOST",
            EfmReferenceId = "EFM-LOST",
        };

        var log = NfrcCallbackTriage.BuildLog(
            rawXml,
            parsed,
            matchedRecord: null,
            remoteIp: "10.0.0.5",
            contentType: "text/xml; charset=utf-8");

        Assert.Null(log.EFilingOrderRecordId);
        Assert.Equal(0, log.NfrcNumber);
        Assert.Equal(rawXml, log.RawXml);
        Assert.Equal(rawXml.Length, log.RawXmlLength);
        Assert.Equal(NfrcCallbackTriage.MatchResult.UnmatchedBoth, log.MatchAttemptResult);
        Assert.Equal("EFSP-LOST", log.EfspReferenceId);
        Assert.Equal("EFM-LOST", log.EfmReferenceId);
        Assert.Equal("10.0.0.5", log.ReceivedFromIp);
        Assert.Equal("text/xml; charset=utf-8", log.ContentType);
    }

    [Fact]
    public void BuildLog_MatchedCallback_PopulatesFkAndNfrcNumber()
    {
        // Matched path: FK must be set, NfrcNumber must reflect the order's
        // post-increment count (controller increments NfrcCount BEFORE calling
        // BuildLog — see EFilingNfrcApiController.ReceiveNfrc).
        var parsed = new NfrcResult
        {
            FilingStatusCode = "ACCEPTED",
            EfspReferenceId = "EFSP-1",
            EfmReferenceId = "EFM-2",
        };
        var record = new EFilingOrderRecord { Id = 99, NfrcCount = 2 };

        var log = NfrcCallbackTriage.BuildLog("<xml/>", parsed, record);

        Assert.Equal(99, log.EFilingOrderRecordId);
        Assert.Equal(2, log.NfrcNumber);
        Assert.Equal(NfrcCallbackTriage.MatchResult.Matched, log.MatchAttemptResult);
    }

    [Fact]
    public void BuildLog_NullRawXml_StoresEmptyStringAndZeroLength()
    {
        // Defensive: caller may pass null body (read-stream failure). Must not
        // throw, and downstream NOT NULL constraint on RawXml must be satisfied.
        var log = NfrcCallbackTriage.BuildLog(
            rawXml: null,
            parsed: null,
            matchedRecord: null);

        Assert.Equal(string.Empty, log.RawXml);
        Assert.Equal(0, log.RawXmlLength);
        Assert.Equal(NfrcCallbackTriage.MatchResult.ParseFailed, log.MatchAttemptResult);
    }
}
