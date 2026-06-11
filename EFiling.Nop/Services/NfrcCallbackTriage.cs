using EFiling.Core.Models;
using EFiling.Nop.Domain;

namespace EFiling.Nop.Services;

/// <summary>
/// Pure-function triage layer for NFRC callbacks. Decides the categorized
/// <see cref="MatchResult"/> outcome and builds an <see cref="EFilingNfrcLog"/>
/// for forensic persistence — regardless of whether the callback could be
/// matched to an order record.
///
/// <para>Phase 0 of the NFRC audit: this replaces the pre-Phase-0 silent-drop
/// path where unmatched callbacks were logged to the application logger and
/// then forgotten. By extracting the decision into a static helper, the
/// controller stays thin and the triage logic is unit-testable without DI.</para>
///
/// <para>Idempotency note: this helper is stateless and pure. Same inputs ⇒
/// same output. Idempotency at the request level (same callback delivered
/// twice by JTI) is the controller's responsibility — see Phase 4 of the
/// audit plan for the full controller-level coverage.</para>
/// </summary>
public static class NfrcCallbackTriage
{
    /// <summary>
    /// Canonical string constants for <see cref="EFilingNfrcLog.MatchAttemptResult"/>.
    /// Stored as strings (not enum) so support staff can query by category in SQL
    /// (<c>WHERE MatchAttemptResult LIKE 'Unmatched_%'</c>) and so adding a new
    /// category never requires a schema change.
    /// </summary>
    public static class MatchResult
    {
        /// <summary>Callback parsed and resolved to an existing order record.</summary>
        public const string Matched = "Matched";

        /// <summary>Body could not be parsed as NFRC XML.</summary>
        public const string ParseFailed = "ParseFailed";

        /// <summary>Body parsed but the parser flagged it as a SOAP fault / unrecognized envelope (FilingStatusCode == "ERROR").</summary>
        public const string SoapFault = "SoapFault";

        /// <summary>Callback contained neither EFSP nor EFM reference IDs — no way to look up an order.</summary>
        public const string UnmatchedNoIds = "Unmatched_NoIds";

        /// <summary>Callback had only an EFSP reference ID and lookup failed (no order has that EFSP ID).</summary>
        public const string UnmatchedEfspOnly = "Unmatched_EfspOnly";

        /// <summary>Callback had only an EFM reference ID and lookup failed.</summary>
        public const string UnmatchedEfmOnly = "Unmatched_EfmOnly";

        /// <summary>Callback had both EFSP and EFM reference IDs and neither matched an order.</summary>
        public const string UnmatchedBoth = "Unmatched_Both";
    }

    /// <summary>
    /// Categorize the NFRC callback based on parse outcome and order-lookup outcome.
    /// </summary>
    /// <param name="parsed">Parsed NFRC payload, or <c>null</c> if parsing threw.</param>
    /// <param name="matchedRecord">Matched order record from EFSP/EFM lookup, or <c>null</c> if no match.</param>
    /// <returns>One of the <see cref="MatchResult"/> constants.</returns>
    public static string DetermineMatchResult(NfrcResult? parsed, EFilingOrderRecord? matchedRecord)
    {
        // Order matters: ParseFailed > SoapFault > Matched > Unmatched_*.
        // SoapFault takes precedence over a (defensive) matched record because a SoapFault
        // indicates the callback is structurally invalid; a "match" in that state would be
        // a controller-side bug, not a real match.
        if (parsed == null)
            return MatchResult.ParseFailed;

        if (parsed.FilingStatusCode == "ERROR")
            return MatchResult.SoapFault;

        if (matchedRecord != null)
            return MatchResult.Matched;

        var hasEfsp = !string.IsNullOrEmpty(parsed.EfspReferenceId);
        var hasEfm = !string.IsNullOrEmpty(parsed.EfmReferenceId);

        return (hasEfsp, hasEfm) switch
        {
            (false, false) => MatchResult.UnmatchedNoIds,
            (true, false) => MatchResult.UnmatchedEfspOnly,
            (false, true) => MatchResult.UnmatchedEfmOnly,
            (true, true) => MatchResult.UnmatchedBoth,
        };
    }

    /// <summary>
    /// Build an <see cref="EFilingNfrcLog"/> entity ready for persistence.
    /// Captures the raw body, diagnostic context, and a categorized
    /// <see cref="EFilingNfrcLog.MatchAttemptResult"/> from
    /// <see cref="DetermineMatchResult"/>.
    /// </summary>
    /// <param name="rawXml">Raw HTTP body received from JTI.</param>
    /// <param name="parsed">Parsed NFRC payload, or <c>null</c> if parsing threw.</param>
    /// <param name="matchedRecord">Matched order record, or <c>null</c> if no match.</param>
    /// <param name="remoteIp">Remote IP of the caller (operational forensics).</param>
    /// <param name="contentType">HTTP <c>Content-Type</c> header.</param>
    public static EFilingNfrcLog BuildLog(
        string? rawXml,
        NfrcResult? parsed,
        EFilingOrderRecord? matchedRecord,
        string? remoteIp = null,
        string? contentType = null)
    {
        var matchResult = DetermineMatchResult(parsed, matchedRecord);
        var body = rawXml ?? string.Empty;

        return new EFilingNfrcLog
        {
            EFilingOrderRecordId = matchedRecord?.Id,
            // NfrcNumber is the per-order sequence; only meaningful for matched callbacks.
            // For unmatched, leave at 0 — support tooling should rely on Id ordering instead.
            NfrcNumber = matchedRecord?.NfrcCount ?? 0,
            RawXml = body,
            MatchAttemptResult = matchResult,
            EfspReferenceId = parsed?.EfspReferenceId,
            EfmReferenceId = parsed?.EfmReferenceId,
            ReceivedFromIp = remoteIp,
            ContentType = contentType,
            RawXmlLength = body.Length,
            // ReceivedUtc is set by EFilingOrderService.InsertNfrcLogAsync at insert time.
        };
    }
}
