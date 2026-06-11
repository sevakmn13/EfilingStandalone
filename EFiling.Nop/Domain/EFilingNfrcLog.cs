using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Raw NFRC XML log entry. One row per NFRC callback received from JTI —
/// regardless of whether the callback was matched to an order record.
///
/// <para>Phase 0 (NFRC audit): unmatched callbacks are persisted with
/// <see cref="EFilingOrderRecordId"/> = <c>null</c> and a categorized
/// <see cref="MatchAttemptResult"/> for forensic recovery. Matched callbacks
/// continue to populate <see cref="EFilingOrderRecordId"/> as before.</para>
/// </summary>
public class EFilingNfrcLog : BaseEntity
{
    /// <summary>
    /// FK to EFilingOrderRecord.Id. <c>null</c> when the callback could not be
    /// matched to a known filing (today's silent-drop case — see
    /// <see cref="MatchAttemptResult"/> for the reason).
    /// </summary>
    public int? EFilingOrderRecordId { get; set; }

    /// <summary>NFRC sequence number (1, 2, 3) for matched callbacks; 0 for unmatched.</summary>
    public int NfrcNumber { get; set; }

    /// <summary>Raw SOAP XML received from JTI.</summary>
    public string RawXml { get; set; } = string.Empty;

    /// <summary>When this NFRC was received (UTC).</summary>
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Categorized outcome of the match attempt. One of:
    /// <c>Matched</c>, <c>ParseFailed</c>, <c>SoapFault</c>,
    /// <c>Unmatched_NoIds</c>, <c>Unmatched_EfspOnly</c>,
    /// <c>Unmatched_EfmOnly</c>, <c>Unmatched_Both</c>.
    /// See <c>NfrcCallbackTriage.MatchResult</c> for canonical constants.
    /// Nullable so legacy rows (pre-Phase-0) read as "unknown".
    /// </summary>
    public string? MatchAttemptResult { get; set; }

    /// <summary>EFSP reference ID extracted from the callback (snapshot — preserved for unmatched diagnosis).</summary>
    public string? EfspReferenceId { get; set; }

    /// <summary>EFM reference ID extracted from the callback (snapshot — preserved for unmatched diagnosis).</summary>
    public string? EfmReferenceId { get; set; }

    /// <summary>Remote IP of the caller (operational forensics — useful when correlating with JTI's outbound logs).</summary>
    public string? ReceivedFromIp { get; set; }

    /// <summary>HTTP <c>Content-Type</c> header sent with the callback.</summary>
    public string? ContentType { get; set; }

    /// <summary>Length of <see cref="RawXml"/> in chars — quick triage indicator without loading the full body.</summary>
    public int RawXmlLength { get; set; }
}
