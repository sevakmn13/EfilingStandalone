using System.Globalization;
using System.Xml.Linq;
using System.Xml.Serialization;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Parses NFRC (NotifyFilingReviewComplete) callback XML from JTI.
///
/// Migration (Track B.3): The inbound callback payload is schema-validated via the generated
/// <see cref="FR.ReviewFilingCallbackMessageExtType"/> (or fallback base / notify-complete wrappers).
/// Field extraction continues to use local-name XDocument navigation — the callback structure
/// is deeply nested with polymorphic document entries, and the existing helpers already handle
/// those paths correctly. The deserialization acts as a schema-validation fence that catches
/// malformed / unexpected payloads before field extraction runs.
/// </summary>
public static class NfrcResponseParser
{
    static readonly XNamespace NsSoapEnv = SoapEnvelopeBuilder.NsSoapEnv;

    private const string ReviewFilingCallbackNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:ReviewFilingCallbackMessage-4.0";
    private const string WsdlProfileNs = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0";

    private static readonly XmlSerializer ReviewFilingCallbackExtSer = new(
        typeof(FR.ReviewFilingCallbackMessageExtType),
        new XmlRootAttribute("ReviewFilingCallbackMessageExt") { Namespace = ReviewFilingCallbackNs });
    private static readonly XmlSerializer ReviewFilingCallbackBaseSer = new(
        typeof(FR.ReviewFilingCallbackMessageType),
        new XmlRootAttribute("ReviewFilingCallbackMessage") { Namespace = ReviewFilingCallbackNs });
    private static readonly XmlSerializer NotifyCompleteSer = new(
        typeof(FR.NotifyFilingReviewCompleteRequestMessageType),
        new XmlRootAttribute("NotifyFilingReviewCompleteRequestMessage") { Namespace = WsdlProfileNs });

    public static NfrcResult Parse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var doc = XDocument.Parse(xml);
        var result = new NfrcResult { RawXml = xml };

        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null) { result.FilingStatusCode = "ERROR"; return result; }

        // Schema-validate via generated types — try each of the three wrapper variants the
        // existing parser supports, in the same order. We discard the typed result and fall
        // back to XDocument navigation for field extraction; the deserialization only serves
        // as a schema-sanity check at this stage of migration.
        _ = TryDeserializeAnyCallback(xml);

        var cb = doc.DescByLocal("ReviewFilingCallbackMessageExt").FirstOrDefault()
              ?? doc.DescByLocal("ReviewFilingCallbackMessage").FirstOrDefault()
              ?? doc.DescByLocal("NotifyFilingReviewCompleteRequestMessage").FirstOrDefault();
        if (cb == null) { result.FilingStatusCode = "ERROR"; return result; }

        ParseMdeIds(cb, result);
        ParseFilingStatus(cb, result);
        ParseCaseInfo(cb, result);
        ParseDocuments(cb, result);
        ParseFees(cb, result);
        ParseMessages(cb, result);
        FindReceipt(result);
        return result;
    }

    /// <summary>
    /// Attempt to deserialize the callback body via each of the supported generated wrapper
    /// types. Returns true if any succeeds. The typed result is discarded — this call is a
    /// schema-validity probe only (field extraction uses XDocument navigation below).
    ///
    /// Track B.6: Inner walk-to-body logic now delegates to shared
    /// <see cref="SoapBodyDeserializer.TryDeserializeAnyBodyChild"/>.
    /// </summary>
    private static bool TryDeserializeAnyCallback(string xml)
    {
        var result = SoapBodyDeserializer.TryDeserializeAnyBodyChild(xml, new[]
        {
            ("ReviewFilingCallbackMessageExt", ReviewFilingCallbackExtSer),
            ("ReviewFilingCallbackMessage", ReviewFilingCallbackBaseSer),
            ("NotifyFilingReviewCompleteRequestMessage", NotifyCompleteSer),
        });
        return result != null;
    }

    private static void ParseMdeIds(XElement cb, NfrcResult r)
    {
        foreach (var did in cb.DescByLocal("DocumentIdentification"))
        {
            var id = did.ByLocalFirst("IdentificationID")?.Value;
            var cat = did.ByLocalFirst("IdentificationCategoryText")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(cat)) continue;
            switch (cat.ToUpperInvariant())
            {
                case "FILING_ASSEMBLY_MDE": r.EfspReferenceId = id; break;
                case "FILING_REVIEW_MDE": r.EfmReferenceId = id; break;
                case "COURT_RECORD_MDE": r.CmsReferenceId = id; break;
            }
        }
        if (string.IsNullOrEmpty(r.EfmReferenceId))
        {
            var top = cb.ByLocal("DocumentIdentification").FirstOrDefault();
            if (top != null) r.EfmReferenceId = top.ByLocalFirst("IdentificationID")?.Value;
        }
    }

    private static void ParseFilingStatus(XElement cb, NfrcResult r)
    {
        var el = cb.DescByLocal("FilingStatus").FirstOrDefault();
        if (el != null)
        {
            r.FilingStatusCode = el.ByLocalFirst("FilingStatusCode")?.Value
                              ?? el.ByLocalFirst("StatusText")?.Value ?? string.Empty;

            // Extract filing-level rejection reasons
            var reasons = el.DescByLocal("FilingStatusReason")
                .Concat(el.DescByLocal("FilingStatusReasons"));
            var texts = reasons
                .Select(reason => reason.ByLocalFirst("ReasonCodeText")?.Value
                               ?? reason.ByLocalFirst("Memo")?.Value
                               ?? reason.ByLocalFirst("ReasonCode")?.Value)
                .Where(t => !string.IsNullOrEmpty(t));
            var combined = string.Join("; ", texts);
            if (!string.IsNullOrEmpty(combined))
                r.FilingRejectionReason = combined;
        }
        r.FilingStatus = MapStatus(r.FilingStatusCode);
    }

    private static void ParseCaseInfo(XElement cb, NfrcResult r)
    {
        r.CaseTrackingId = cb.DescByLocal("CaseTrackingID").FirstOrDefault()?.Value;
        r.CaseDocketId = cb.DescByLocal("CaseDocketID").FirstOrDefault()?.Value;
        r.CaseTitle = cb.DescByLocal("CaseTitleText").FirstOrDefault()?.Value;
    }

    private static void ParseDocuments(XElement cb, NfrcResult r)
    {
        // Real JTI NFRCs emit a single <ReviewedLeadDocument> followed by 0..N
        // <ReviewedConnectedDocument> siblings (per WSDL ReviewFilingCallbackMessageExtType,
        // see EFiling.WsdlGenerated/FilingReview/Reference.cs:30163-30292). Both are typed
        // ReviewedDocumentType[Ext] and share the same per-doc shape, so we treat them as
        // a single concatenated stream. Fix for B0a (NFRC audit § 15.2 / § 15.6 — 2026-04-26).
        // Fallbacks preserve compatibility with synthetic test XML and any older message
        // shapes that still use <ReviewedDocument> / <ReviewedDocumentExt>.
        var docs = cb.DescByLocal("ReviewedLeadDocument")
            .Concat(cb.DescByLocal("ReviewedConnectedDocument"))
            .ToList();
        if (docs.Count == 0) docs = cb.DescByLocal("ReviewedDocument").ToList();
        if (docs.Count == 0) docs = cb.DescByLocal("ReviewedDocumentExt").ToList();

        foreach (var rd in docs)
        {
            var d = new NfrcDocumentResult();
            d.DocumentDescriptionText = rd.ByLocalFirst("DocumentDescriptionText")?.Value
                                     ?? rd.ByLocalFirst("DocumentTitleText")?.Value;

            // B0b fix (Phase 5.3): extract the canonical EFM document handle from
            // <DocumentFileControlID> (WSDL element at FilingReviewMDEPort.wsdl:11311).
            // Schema-canonical per-document identifier: integer-string assigned by the
            // EFM at submission time. Real JTI samples populate this for both
            // filer-uploaded ("29888", "29889", …) and court-generated ("390903",
            // "390904", …) docs. Pre-fix, this element was never extracted; the
            // category-dispatch loop below incorrectly populated EfmDocumentId from
            // <IdentificationID> (the doc-type code, e.g. "COM040", "EFM001").
            var fileControlId = rd.ByLocalFirst("DocumentFileControlID")?.Value;
            if (!string.IsNullOrEmpty(fileControlId))
                d.EfmDocumentId = fileControlId;

            foreach (var did in rd.DescByLocal("DocumentIdentification"))
            {
                var id = did.ByLocalFirst("IdentificationID")?.Value;
                var cat = did.ByLocalFirst("IdentificationCategoryText")?.Value;
                if (string.IsNullOrEmpty(id)) continue;
                switch (cat?.ToUpperInvariant())
                {
                    case "FILING_ASSEMBLY_MDE": d.EfspDocumentId = id; break;
                    // FILING_REVIEW_MDE category is the older-shape source for the canonical
                    // EFM handle; only used if <DocumentFileControlID> wasn't present above.
                    case "FILING_REVIEW_MDE": d.EfmDocumentId ??= id; break;
                    case "COURT_RECORD_MDE": d.CmsDocumentId = id; break;
                    // B0b fix (Phase 5.3): bare <IdentificationID> with no category text is
                    // the typical real-JTI shape — it carries the vendor doc-type code
                    // ("COM040", "EFM001", "RECEIPT", "258110"), NOT a per-instance handle.
                    // Captured separately in DocumentCode for semantic categorization;
                    // pre-fix this value polluted EfmDocumentId via `EfmDocumentId ??= id`.
                    default: d.DocumentCode ??= id; break;
                }
            }

            var dfs = rd.DescByLocal("DocumentFilingStatus").FirstOrDefault();
            if (dfs != null)
            {
                d.DocumentFilingStatusCode = dfs.ByLocalFirst("DocumentFilingStatusCode")?.Value;
                foreach (var reason in dfs.DescByLocal("FilingStatusReason"))
                {
                    var txt = reason.ByLocalFirst("ReasonCodeText")?.Value ?? reason.ByLocalFirst("Memo")?.Value;
                    if (!string.IsNullOrEmpty(txt))
                        d.RejectionReasonText = string.IsNullOrEmpty(d.RejectionReasonText) ? txt : $"{d.RejectionReasonText}; {txt}";
                }
            }

            var st = rd.DescByLocal("DocumentStatus").FirstOrDefault();
            if (st != null) d.DocumentStatusText = st.ByLocalFirst("StatusText")?.Value ?? st.Value;

            d.DocumentDispositionType = rd.ByLocalFirst("DocumentDispositionType")?.Value
                                     ?? rd.ByLocalFirst("DocumentDisposition")?.Value;

            // Q22-B fix (Phase 5.7): per-doc DocumentDispositionDate extraction.
            // Per WSDL ReviewedDocumentTypeExt at FilingReviewMDEPort.wsdl:9315 (also
            // EFiling.WsdlGenerated/FilingReview/Reference.cs:23456). Schema type is
            // nc:DateType (NIEM complex wrapper); on the wire the element contains a
            // <DateRepresentation> child with xsi:type="xs:date" or "xs:dateTime"
            // (mirrors the shape PolicyResponseParser.ExtractDate handles for typed
            // DateType.Items). Helper tolerates both NIEM-wrapper and direct-string shapes;
            // returns null on parse failure (defensive — never throws).
            d.DocumentDispositionDate = ParseNiemDate(rd.ByLocalFirst("DocumentDispositionDate"));

            var uri = rd.DescByLocal("BinaryLocationURI").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(uri)) d.BinaryLocationUri = uri;

            // Q23 fix (Phase 5.4): per-doc messageToFiler / messageToClerk capture.
            // Per WSDL ReviewedDocumentTypeExt at FilingReviewMDEPort.wsdl:23360-23362
            // (also DocumentExtType:11099-11101). Both elements are optional TextType.
            // Direct-children lookup (ByLocalFirst, not DescByLocal) so we don't accidentally
            // capture nested values from any embedded structures. The controller folds
            // MessageToFiler into RejectionReasonText for filer-visible display; MessageToClerk
            // is preserved for audit only and is NEVER surfaced to the filer (privacy guard).
            d.MessageToFiler = rd.ByLocalFirst("messageToFiler")?.Value;
            d.MessageToClerk = rd.ByLocalFirst("messageToClerk")?.Value;

            // B0b fix (Phase 5.3): replace the EfspDocumentId-emptiness heuristic with a
            // schema-grounded discriminator. Filer-uploaded docs carry the NIEM
            // structures:id attribute (xmlns: http://niem.gov/niem/structures/2.0) because
            // they're cross-referenced from <DocumentRendition>/<CourtEventDocument> blocks
            // elsewhere in the NFRC envelope; court-emitted docs are not cross-referenced
            // and so don't carry the attribute. The previous heuristic relied on per-doc
            // FILING_ASSEMBLY_MDE category text, which never fires in real JTI NFRCs (see
            // § 15.6 B0b residual). This signal is a hint only — the controller's
            // authoritative discrimination is matching nfrcDoc.EfmDocumentId against the
            // existing FileControlId in our submission record (Q17 fix).
            //
            // Backward-compat: synthetic test fixtures that emit FILING_ASSEMBLY_MDE
            // category continue to use that signal (EfspDocumentId populated → not court-gen).
            var hasStructuresId = rd.Attributes().Any(a => a.Name.LocalName == "id");
            d.IsCourtGenerated = string.IsNullOrEmpty(d.EfspDocumentId) && !hasStructuresId;
            r.Documents.Add(d);
        }
    }

    private static void ParseFees(XElement cb, NfrcResult r)
    {
        var fc = cb.DescByLocal("FeesCalculationType").FirstOrDefault()
              ?? cb.DescByLocal("FeesCalculationTypeExt").FirstOrDefault()
              ?? cb.DescByLocal("FeesCalculation").FirstOrDefault();
        if (fc == null) return;

        var totalEl = fc.ByLocalFirst("FeesCalculationAmount");
        if (totalEl != null && decimal.TryParse(totalEl.Value, out var total)) r.TotalFees = total;

        foreach (var charge in fc.DescByLocal("AllowanceCharge"))
        {
            var item = new FeeLineItem();
            if (decimal.TryParse(charge.ByLocalFirst("Amount")?.Value, out var amt)) item.Amount = amt;
            item.AccountingCostCode = charge.ByLocalFirst("AccountingCostCode")?.Value ?? string.Empty;
            item.Description = charge.ByLocalFirst("AllowanceChargeReason")?.Value;
            r.FeeLineItems.Add(item);
        }
    }

    private static void ParseMessages(XElement cb, NfrcResult r)
    {
        // Q23 fix (Phase 5.4): extract envelope-level <messageToFiler> + <messageToClerk>.
        // Per WSDL FilingReviewMDEPort.wsdl:1172-1173 (also ReviewFilingCallbackMessageExtType
        // at EFiling.WsdlGenerated/FilingReview/Reference.cs:30155-30158), both are optional
        // TextType direct children of the callback envelope. Pre-fix, both elements were
        // silently dropped on the floor — no parsing, no persistence, no UI surface
        // (NFRC audit § 15.6 Q23). Vendor docs scope messageToFiler to rejection context
        // ("end user will read the reasons for rejection"); when Madera (or any EFM) eventually
        // populates this on a clerk-driven reject, the filer needs to see it.
        //
        // Direct-children lookup (ByLocalFirst, not DescByLocal) so we don't accidentally
        // capture per-doc messageToFiler/messageToClerk values that live inside
        // <ReviewedLeadDocument>/<ReviewedConnectedDocument> children — those have their
        // own slots on NfrcDocumentResult populated by ParseDocuments.
        //
        // Privacy guard: the parser populates both fields, but messageToClerk MUST NOT be
        // merged into any filer-visible field (FilingRejectionReason, ErrorText) here or
        // downstream. Controller's UpdateOrderFromNfrcAsync enforces this by folding only
        // MessageToFiler into ErrorText, never MessageToClerk. messageToClerk is preserved
        // for audit only (this property + EFilingNfrcLog.RawXml).
        r.MessageToFiler = cb.ByLocalFirst("messageToFiler")?.Value;
        r.MessageToClerk = cb.ByLocalFirst("messageToClerk")?.Value;
    }

    private static void FindReceipt(NfrcResult r)
    {
        foreach (var d in r.Documents)
        {
            if (d.IsCourtGenerated && string.Equals(d.DocumentDescriptionText, "RECEIPT", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(d.BinaryLocationUri))
            { r.ReceiptUrl = d.BinaryLocationUri; break; }
        }
    }

    private static FilingStatus MapStatus(string? code)
    {
        if (string.IsNullOrEmpty(code)) return FilingStatus.Unknown;
        return code.ToUpperInvariant() switch
        {
            "RECEIVED" or "RECEIVED_UNDER_REVIEW" or "RECEIVEDUNDERREVIEW" => FilingStatus.ReceivedUnderReview,
            "ACCEPTED" or "REVIEWED" => FilingStatus.Accepted,
            "PARTIALLY_ACCEPTED" or "PARTIALLYACCEPTED" => FilingStatus.PartiallyAccepted,
            "REJECTED" or "CANCELLED" => FilingStatus.Rejected,
            _ => FilingStatus.Unknown
        };
    }

    /// <summary>
    /// Parses a NIEM <c>DateType</c> element to a UTC <see cref="DateTime"/>. The NIEM
    /// wire shape is a complex wrapper containing a <c>&lt;DateRepresentation&gt;</c>
    /// (or <c>&lt;Date&gt;</c> / <c>&lt;DateTime&gt;</c>) child whose inner text is the
    /// ISO-8601 lexical timestamp; <c>xsi:type</c> on the child indicates whether it's
    /// <c>xs:date</c> (yyyy-MM-dd) or <c>xs:dateTime</c> (yyyy-MM-ddTHH:mm:ss[.fff][TZ]).
    /// This helper also tolerates the direct-value shape
    /// <c>&lt;DocumentDispositionDate&gt;2026-05-15&lt;/DocumentDispositionDate&gt;</c>
    /// for backward-compat with synthetic test fixtures and any older message shapes.
    /// All parsed values are normalized to UTC via
    /// <see cref="DateTimeStyles.AssumeUniversal"/> + <see cref="DateTimeStyles.AdjustToUniversal"/>:
    /// strings without a TZ offset are treated as UTC, strings with a TZ offset are
    /// converted to UTC. Returns null on parse failure or absent input — defensive,
    /// never throws.
    /// </summary>
    /// <remarks>
    /// XDocument-navigation analogue of <c>PolicyResponseParser.ExtractDate</c>, which
    /// operates on the typed <c>FR.DateType.Items</c> array. Both helpers consume the
    /// same underlying NIEM shape but at different layers of the parsing pipeline
    /// (typed deserialization vs XDocument navigation).
    /// </remarks>
    private static DateTime? ParseNiemDate(XElement? dateTypeElement)
    {
        if (dateTypeElement == null) return null;

        // Try NIEM wrapper shapes first (real-wire JTI / Madera shape).
        // <DocumentDispositionDate>
        //   <DateRepresentation xsi:type="xsd:dateTime">2026-05-15T14:30:00-08:00</DateRepresentation>
        // </DocumentDispositionDate>
        var inner = dateTypeElement.ByLocalFirst("DateRepresentation")?.Value
                 ?? dateTypeElement.ByLocalFirst("DateTime")?.Value
                 ?? dateTypeElement.ByLocalFirst("Date")?.Value;

        // Fallback: direct-value shape <DocumentDispositionDate>2026-05-15</DocumentDispositionDate>.
        // Use the element's own text content only when no wrapper child was found, to avoid
        // accidentally capturing concatenated child text under XML mixed-content rules.
        if (string.IsNullOrWhiteSpace(inner) && !dateTypeElement.HasElements)
            inner = dateTypeElement.Value;

        if (string.IsNullOrWhiteSpace(inner)) return null;

        if (DateTime.TryParse(
                inner,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }
        return null;
    }
}
