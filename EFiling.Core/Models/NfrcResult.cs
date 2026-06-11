using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// Parsed result from an NFRC (NotifyFilingReviewComplete) callback.
/// Contains filing-level status, per-document statuses, fee breakdown, and document URLs.
/// </summary>
public class NfrcResult
{
    /// <summary>Overall filing status: RECEIVED_UNDER_REVIEW, ACCEPTED, PARTIALLY_ACCEPTED, REJECTED.</summary>
    public string FilingStatusCode { get; set; } = string.Empty;

    /// <summary>Parsed filing status enum.</summary>
    public FilingStatus FilingStatus { get; set; } = FilingStatus.Unknown;

    /// <summary>Our EFSP reference ID (FILING_ASSEMBLY_MDE IdentificationID).</summary>
    public string? EfspReferenceId { get; set; }

    /// <summary>EFM reference ID (FILING_REVIEW_MDE IdentificationID).</summary>
    public string? EfmReferenceId { get; set; }

    /// <summary>Court CMS reference ID (COURT_RECORD_MDE IdentificationID).</summary>
    public string? CmsReferenceId { get; set; }

    /// <summary>Court-assigned case number (CaseTrackingID).</summary>
    public string? CaseTrackingId { get; set; }

    /// <summary>Case title (CaseTitleText).</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Case docket ID (CaseDocketID — public case number).</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Per-document statuses and URLs.</summary>
    public List<NfrcDocumentResult> Documents { get; set; } = new();

    /// <summary>Fee line items (from NFRC #2 FeesCalculation).</summary>
    public List<FeeLineItem> FeeLineItems { get; set; } = new();

    /// <summary>Total fees amount from FeesCalculationAmount.</summary>
    public decimal? TotalFees { get; set; }

    /// <summary>URL to receipt PDF (from NFRC #2 RECEIPT document).</summary>
    public string? ReceiptUrl { get; set; }

    /// <summary>Filing-level rejection reason text (from FilingStatus/FilingStatusReason).</summary>
    public string? FilingRejectionReason { get; set; }

    /// <summary>
    /// Envelope-level <c>&lt;messageToFiler&gt;</c> text (WSDL
    /// <c>FilingReviewMDEPort.wsdl:1173</c>, <c>ReviewFilingCallbackMessageExtType</c>:30157).
    /// Vendor docs scope this element to the rejection use case ("end user will read the
    /// reasons for rejection"), but WSDL does not restrict it to rejected status. The
    /// controller folds this value into <c>EFilingOrderRecord.ErrorText</c> for filer-visible
    /// display alongside <see cref="FilingRejectionReason"/> for Rejected / PartiallyAccepted
    /// filings (Q23 fix — Phase 5.4 of NFRC audit).
    /// </summary>
    public string? MessageToFiler { get; set; }

    /// <summary>
    /// Envelope-level <c>&lt;messageToClerk&gt;</c> text (WSDL
    /// <c>FilingReviewMDEPort.wsdl:1172</c>, <c>ReviewFilingCallbackMessageExtType</c>:30155).
    /// <b>Privacy guard:</b> this is internal court / EFSP communication — it is captured for
    /// audit purposes (preserved in <c>EFilingNfrcLog.RawXml</c> + this model property) but
    /// MUST NEVER be merged into any filer-visible field (<c>ErrorText</c>,
    /// <see cref="FilingRejectionReason"/>, etc.). Surfacing it to the filer would leak
    /// clerk-internal context (Q23 fix — Phase 5.4 of NFRC audit).
    /// </summary>
    public string? MessageToClerk { get; set; }

    /// <summary>Raw XML for logging.</summary>
    public string? RawXml { get; set; }
}

/// <summary>
/// Per-document result from an NFRC callback.
/// </summary>
public class NfrcDocumentResult
{
    /// <summary>Document description text (document type code).</summary>
    public string? DocumentDescriptionText { get; set; }

    /// <summary>FILING_ASSEMBLY_MDE document ID (our FileControlId).</summary>
    public string? EfspDocumentId { get; set; }

    /// <summary>
    /// Canonical EFM document handle, sourced from <c>&lt;DocumentFileControlID&gt;</c>
    /// (WSDL <c>FilingReviewMDEPort.wsdl:11311</c>) — this is the unique-per-document
    /// integer string the EFM assigns at submission time (e.g., <c>"29888"</c> for
    /// filer-uploaded, <c>"390903"</c> for court-generated). Falls back to the
    /// <c>FILING_REVIEW_MDE</c>-categorized <c>IdentificationID</c> for backward
    /// compatibility with older message shapes that don't emit <c>DocumentFileControlID</c>.
    /// Used as the canonical match key for filer-uploaded docs (against
    /// <c>EFilingDocumentRecord.FileControlId</c>) and as the canonical dedup key for
    /// court-generated docs.
    /// </summary>
    public string? EfmDocumentId { get; set; }

    /// <summary>
    /// Bare <c>&lt;IdentificationID&gt;</c> value from the per-doc
    /// <c>&lt;DocumentIdentification&gt;</c> when no <c>IdentificationCategoryText</c>
    /// child is present (the typical real-JTI shape — see § 15.6 B0b). Holds the
    /// vendor doc-type code (e.g., <c>"COM040"</c>, <c>"EFM001"</c>, <c>"RECEIPT"</c>,
    /// <c>"258110"</c>). Pre-Phase-5.3, this value was incorrectly stored in
    /// <see cref="EfmDocumentId"/>; B0b separates the doc-type code (semantic) from the
    /// canonical document handle (per-instance). Used as a tertiary fallback for
    /// <c>DocumentReferenceId</c> generation when <see cref="EfmDocumentId"/> is empty.
    /// </summary>
    public string? DocumentCode { get; set; }

    /// <summary>COURT_RECORD_MDE document ID (CMS doc ID).</summary>
    public string? CmsDocumentId { get; set; }

    /// <summary>Document-level filing status: ACCEPTED, REJECTED, RECEIVED_UNDER_REVIEW, RECEIVED.</summary>
    public string? DocumentFilingStatusCode { get; set; }

    /// <summary>Document status text: R, F, I, RJ, RP, FG, IF.</summary>
    public string? DocumentStatusText { get; set; }

    /// <summary>Document disposition type (NFRC #3): GRA, DEN, ORD, OAI.</summary>
    public string? DocumentDispositionType { get; set; }

    /// <summary>
    /// Document disposition date (NFRC #3): the timestamp at which the judicial officer
    /// rendered the disposition recorded in <see cref="DocumentDispositionType"/>. Optional
    /// per WSDL <c>ReviewedDocumentTypeExt</c> at <c>FilingReviewMDEPort.wsdl:9315</c>
    /// (schema type <c>nc:DateType</c>). On the wire the element is a NIEM
    /// <c>DateType</c> complex wrapper containing a <c>DateRepresentation</c> child whose
    /// <c>xsi:type</c> indicates either <c>xs:date</c> (yyyy-MM-dd) or <c>xs:dateTime</c>
    /// (yyyy-MM-ddTHH:mm:ss[.fff][TZ]). The parser also tolerates the direct-value shape
    /// <c>&lt;DocumentDispositionDate&gt;2026-05-15&lt;/DocumentDispositionDate&gt;</c>
    /// for backward compatibility with older message shapes and synthetic test fixtures.
    /// All parsed values are normalized to UTC; null on parse failure or absence
    /// (defensive — never throws on malformed input). Q22-B fix — Phase 5.7 of NFRC audit.
    /// </summary>
    public DateTime? DocumentDispositionDate { get; set; }

    /// <summary>Rejection reason text.</summary>
    public string? RejectionReasonText { get; set; }

    /// <summary>URL to conformed copy or court-generated document (7-day expiry).</summary>
    public string? BinaryLocationUri { get; set; }

    /// <summary>Whether this is a court-generated document (notice, receipt, etc.).</summary>
    public bool IsCourtGenerated { get; set; }

    /// <summary>
    /// Per-doc <c>&lt;messageToFiler&gt;</c> text (WSDL <c>ReviewedDocumentTypeExt</c> at
    /// <c>FilingReviewMDEPort.wsdl:23362</c>). Same scope semantics as the envelope-level
    /// counterpart — folded into <see cref="RejectionReasonText"/> by the controller for
    /// filer-visible display when the doc has a rejected status (Q23 fix — Phase 5.4).
    /// </summary>
    public string? MessageToFiler { get; set; }

    /// <summary>
    /// Per-doc <c>&lt;messageToClerk&gt;</c> text (WSDL <c>ReviewedDocumentTypeExt</c> at
    /// <c>FilingReviewMDEPort.wsdl:23360</c>). <b>Privacy guard:</b> clerk-internal
    /// communication — captured for audit only; never merged into filer-visible fields
    /// (<see cref="RejectionReasonText"/>, etc.). Q23 fix — Phase 5.4.
    /// </summary>
    public string? MessageToClerk { get; set; }
}
