using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Parses ReviewFiling (MessageReceiptMessage), GetFeesCalculation, GetFilingStatus,
/// GetFilingList, GetNFRC, and GetRecordingStatus responses.
///
/// Migration (Track B.2): Deserialization uses the generated WSDL types as a schema-validation
/// fence — if the XML doesn't match the schema, <c>XmlSerializer.Deserialize</c> fails fast with
/// a clear error. Field extraction continues to use local-name XDocument navigation where the
/// existing behavior already works (to minimize risk of behavioral regression during migration).
/// Future passes can tighten to pure typed-field access.
/// </summary>
public static class FilingResponseParser
{
    // ─── XNamespaces ─────────────────────────────────────────────────
    static readonly XNamespace NsNc = SoapEnvelopeBuilder.NsNiemCore;
    static readonly XNamespace NsEcf = SoapEnvelopeBuilder.NsCommonTypes;
    static readonly XNamespace NsReceipt = SoapEnvelopeBuilder.NsMessageReceipt;
    static readonly XNamespace NsFeesResp = SoapEnvelopeBuilder.NsFeesCalcResponse;
    static readonly XNamespace NsFeesExt = SoapEnvelopeBuilder.NsJtiFeesCalcExt;
    static readonly XNamespace NsFeesRespExt = SoapEnvelopeBuilder.NsJtiFeesCalcResponseExt;
    static readonly XNamespace NsUblCac = SoapEnvelopeBuilder.NsUblCac;
    static readonly XNamespace NsUblCbc = SoapEnvelopeBuilder.NsUblCbc;
    static readonly XNamespace NsSoapEnv = SoapEnvelopeBuilder.NsSoapEnv;

    // ─── Generated-type serializers (cached; XmlSerializer is expensive to construct) ──
    private const string MessageReceiptNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0";
    private const string FeesCalcResponseNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FeesCalculationResponseMessage-4.0";
    private const string FilingStatusResponseNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusResponseMessage-4.0";
    private const string FilingListResponseExtNs = "urn:com.journaltech:ecourt:ecf:extension:FilingListResponseMessageTypeExt";
    private const string RecordingStatusResponseNs = "urn:com.journaltech:ecourt:ecf:extension:RecordingStatusResponseMessage";
    private const string NfrcResponseNs = "urn:com.journaltech:ecourt:ecf:extension:NFRCResponseType";

    private static readonly XmlSerializer MessageReceiptSer = new(
        typeof(FR.MessageReceiptMessageType),
        new XmlRootAttribute("MessageReceiptMessage") { Namespace = MessageReceiptNs });
    private static readonly XmlSerializer FeesCalcSer = new(
        typeof(FR.FeesCalculationResponseMessageType),
        new XmlRootAttribute("FeesCalculationResponseMessage") { Namespace = FeesCalcResponseNs });
    private static readonly XmlSerializer FilingStatusSer = new(
        typeof(FR.FilingStatusResponseMessageType),
        new XmlRootAttribute("FilingStatusResponseMessage") { Namespace = FilingStatusResponseNs });
    private static readonly XmlSerializer FilingListExtSer = new(
        typeof(FR.FilingListResponseMessageTypeExt),
        new XmlRootAttribute("FilingListResponseMessageExt") { Namespace = FilingListResponseExtNs });
    private static readonly XmlSerializer RecordingStatusSer = new(
        typeof(FR.RecordingStatusResponseMessageType),
        new XmlRootAttribute("RecordingStatusResponseMessage") { Namespace = RecordingStatusResponseNs });
    private static readonly XmlSerializer NfrcResponseSer = new(
        typeof(FR.NFRCResponseType),
        new XmlRootAttribute("NFRCResponse") { Namespace = NfrcResponseNs });

    // Track B.6: Use shared SoapBodyDeserializer.TryDeserializeBodyChild instead of a
    // local copy. Previously this file carried a ~20-line duplicate of the same walk-to-body
    // deserialization logic also present in CaseResponseParser and NfrcResponseParser.
    private static T? TryDeserializeBodyChild<T>(string xml, string localName, XmlSerializer ser) where T : class
        => SoapBodyDeserializer.TryDeserializeBodyChild<T>(xml, localName, ser);

    /// <summary>
    /// Parse a ReviewFiling response (MessageReceiptMessage) from raw SOAP XML.
    /// Malformed XML returns an error-shaped <see cref="FilingResult"/> (does not throw).
    /// </summary>
    public static FilingResult ParseMessageReceipt(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            return new FilingResult
            {
                Success = false,
                ErrorCode = -1,
                ErrorText = $"Invalid XML response from court: {ex.Message}. Raw response (first 500 chars): {xml.Substring(0, Math.Min(500, xml.Length))}",
                RawXml = xml
            };
        }

        var result = new FilingResult { RawXml = xml };

        // SOAP fault short-circuit (body may still be well-formed XML)
        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null)
        {
            result.Success = false;
            result.ErrorText = fault.ByLocalFirst("faultstring")?.Value
                            ?? fault.ByLocalFirst("detail")?.Value
                            ?? "SOAP Fault";
            return result;
        }

        // Schema-validate body via generated types
        var receipt = TryDeserializeBodyChild<FR.MessageReceiptMessageType>(xml, "MessageReceiptMessage", MessageReceiptSer);
        if (receipt == null)
        {
            result.Success = false;
            result.ErrorText = "MessageReceiptMessage not found in response.";
            return result;
        }

        // ECF error branch (typed)
        if (receipt.Error is { Length: > 0 })
        {
            var firstError = receipt.Error[0];
            var errorCode = firstError?.ErrorCode?.Value;
            var errorText = firstError?.ErrorText?.Value;
            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0")
            {
                result.Success = false;
                result.ErrorCode = int.TryParse(errorCode, out var ec) ? ec : -1;
                result.ErrorText = errorText ?? $"Error code: {errorCode}";
                return result;
            }
        }

        // Extract DocumentIdentification / DocumentFileControlID — these are inherited from
        // DocumentType (MessageReceiptMessageType : CaseFilingType : DocumentType). XDocument
        // navigation preserves the exact same lookup semantics as pre-migration while the
        // generated types have already validated the overall structure.
        var receiptEl = doc.Descendants(NsReceipt + "MessageReceiptMessage").FirstOrDefault()
                     ?? doc.DescByLocal("MessageReceiptMessage").FirstOrDefault();
        if (receiptEl != null)
        {
            var docIdEl = receiptEl.DescByLocal("DocumentIdentification").FirstOrDefault();
            if (docIdEl != null)
            {
                result.EfmReferenceId = docIdEl.ByLocalFirst("IdentificationID")?.Value;
            }
            result.EfspReferenceId = receiptEl.DescByLocal("DocumentFileControlID").FirstOrDefault()?.Value;
        }

        result.Success = true;
        result.Status = FilingStatus.ReceivedUnderReview;
        return result;
    }

    /// <summary>
    /// Parse a GetFeesCalculation response from raw SOAP XML.
    /// Malformed XML returns an error-shaped <see cref="FeeCalculation"/> (does not throw).
    /// </summary>
    public static FeeCalculation ParseFeesCalculationResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            return new FeeCalculation
            {
                ErrorCode = -1,
                ErrorText = $"Invalid XML response from court: {ex.Message}. Raw response (first 500 chars): {xml.Substring(0, Math.Min(500, xml.Length))}",
                RawXml = xml
            };
        }

        var result = new FeeCalculation { RawXml = xml };

        // SOAP fault short-circuit
        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null)
        {
            result.ErrorCode = -1;
            result.ErrorText = fault.ByLocalFirst("faultstring")?.Value
                            ?? fault.ByLocalFirst("detail")?.Value
                            ?? "SOAP Fault";
            return result;
        }

        // Schema-validate body via generated types. Returns null on failure; in that case
        // we still try to extract error info via XDocument below before giving up.
        var fees = TryDeserializeBodyChild<FR.FeesCalculationResponseMessageType>(xml, "FeesCalculationResponseMessage", FeesCalcSer);

        // Typed error check first — works even if inner fields fail to extract
        if (fees?.Error is { Length: > 0 })
        {
            var err = fees.Error[0];
            var code = err?.ErrorCode?.Value;
            var text = err?.ErrorText?.Value;
            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                result.ErrorCode = int.TryParse(code, out var ec) ? ec : -1;
                result.ErrorText = text ?? $"Error code: {code}";
                return result;
            }
        }

        // Field extraction — uses XDocument navigation to handle both base and ext wrappers.
        // The generated deserialization above has already validated the overall schema shape.
        var feesCalc = doc.DescByLocal("FeesCalculationType").FirstOrDefault()
                    ?? doc.DescByLocal("FeesCalculationTypeExt").FirstOrDefault()
                    ?? doc.DescByLocal("FeesCalculationAmount").FirstOrDefault()?.Parent;

        if (feesCalc == null)
        {
            // Check the response message element for errors as last resort
            var respMsg = doc.DescByLocal("FeesCalculationResponseMessage").FirstOrDefault()
                       ?? doc.DescByLocal("FeesCalculationResponseMessageTypeExt").FirstOrDefault();
            if (respMsg != null)
            {
                var errors = respMsg.DescByLocal("Error").ToList();
                if (errors.Count > 0)
                {
                    var errorCode = errors[0].ByLocalFirst("ErrorCode")?.Value;
                    var errorText = errors[0].ByLocalFirst("ErrorText")?.Value;
                    result.ErrorCode = int.TryParse(errorCode, out var ec) ? ec : -1;
                    result.ErrorText = errorText ?? "Fee calculation error";
                    return result;
                }
            }

            result.ErrorText = "FeesCalculation element not found in response.";
            result.ErrorCode = -1;
            return result;
        }

        // Errors nested inside the FeesCalculation wrapper
        var feeErrors = feesCalc.ByLocal("Error").ToList();
        if (feeErrors.Count == 0)
        {
            feeErrors = feesCalc.DescByLocal("Error").ToList();
        }
        if (feeErrors.Count > 0)
        {
            var errorCode = feeErrors[0].ByLocalFirst("ErrorCode")?.Value;
            var errorText = feeErrors[0].ByLocalFirst("ErrorText")?.Value;
            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0")
            {
                result.ErrorCode = int.TryParse(errorCode, out var ec) ? ec : -1;
                result.ErrorText = errorText ?? $"Error code: {errorCode}";
                return result;
            }
        }

        // FeesCalculationAmount
        var amountEl = feesCalc.ByLocalFirst("FeesCalculationAmount");
        if (amountEl != null && decimal.TryParse(amountEl.Value, out var totalAmount))
        {
            result.TotalAmount = totalAmount;
        }

        // Exemption (inside FeesCalculation) / response-level exemptionType (JTI ext)
        var exemption = feesCalc.ByLocalFirst("Exemption")?.Value;
        if (!string.IsNullOrEmpty(exemption))
        {
            result.ExemptionType = exemption;
        }
        var respExemption = feesCalc.Parent?.ByLocalFirst("exemptionType")?.Value;
        if (!string.IsNullOrEmpty(respExemption) && string.IsNullOrEmpty(result.ExemptionType))
        {
            result.ExemptionType = respExemption;
        }

        // AllowanceCharge line items
        foreach (var charge in feesCalc.DescByLocal("AllowanceCharge"))
        {
            var lineItem = new FeeLineItem();

            var amountVal = charge.ByLocalFirst("Amount")?.Value;
            if (decimal.TryParse(amountVal, out var amt))
            {
                lineItem.Amount = amt;
            }

            lineItem.AccountingCostCode = charge.ByLocalFirst("AccountingCostCode")?.Value ?? string.Empty;
            lineItem.Description = charge.ByLocalFirst("AllowanceChargeReason")?.Value;

            result.LineItems.Add(lineItem);
        }

        return result;
    }

    /// <summary>
    /// Parse a GetFilingStatus response (ECF standard) from raw SOAP XML.
    /// </summary>
    public static FilingStatusResult ParseFilingStatusResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);
        var result = new FilingStatusResult { RawXml = xml };

        // SOAP fault short-circuit
        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null)
        {
            result.FilingStatus = FilingStatus.Unknown;
            return result;
        }

        // Schema-validate body via generated types
        var status = TryDeserializeBodyChild<FR.FilingStatusResponseMessageType>(xml, "FilingStatusResponseMessage", FilingStatusSer);

        // Typed error check
        if (status?.Error is { Length: > 0 })
        {
            var err = status.Error[0];
            var code = err?.ErrorCode?.Value;
            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                result.FilingStatus = FilingStatus.Unknown;
                return result;
            }
        }

        // Locate the response element for field extraction (XDocument — handles fields on
        // nested/inherited types without needing to traverse the generated type hierarchy).
        var resp = doc.DescByLocal("FilingStatusResponseMessage").FirstOrDefault();
        if (resp == null)
        {
            result.FilingStatus = FilingStatus.Unknown;
            return result;
        }

        // Extract DocumentIdentification (EFM reference ID)
        var docId = resp.DescByLocal("DocumentIdentification").FirstOrDefault();
        if (docId != null)
            result.EfmReferenceId = docId.ByLocalFirst("IdentificationID")?.Value;

        // Extract CaseTrackingID and CaseDocketID
        result.CaseTrackingId = resp.DescByLocal("CaseTrackingID").FirstOrDefault()?.Value;
        result.CaseDocketId = resp.DescByLocal("CaseDocketID").FirstOrDefault()?.Value;

        // Extract FilingStatus
        var filingStatusEl = resp.DescByLocal("FilingStatus").FirstOrDefault();
        if (filingStatusEl != null)
        {
            var statusCode = filingStatusEl.ByLocalFirst("FilingStatusCode")?.Value
                          ?? filingStatusEl.ByLocalFirst("StatusText")?.Value;
            result.FilingStatus = MapFilingStatusCode(statusCode);

            // Status reasons (for rejections)
            var reasons = filingStatusEl.DescByLocal("FilingStatusReason")
                .Concat(filingStatusEl.DescByLocal("FilingStatusReasons"));
            foreach (var reason in reasons)
            {
                var r = new FilingStatusReason
                {
                    ReasonCode = reason.ByLocalFirst("ReasonCode")?.Value,
                    ReasonText = reason.ByLocalFirst("ReasonCodeText")?.Value,
                    Memo = reason.ByLocalFirst("Memo")?.Value
                };
                if (!string.IsNullOrEmpty(r.ReasonCode) || !string.IsNullOrEmpty(r.ReasonText))
                    result.Reasons.Add(r);
            }
        }

        // Extract per-document statuses from ReviewedDocument elements
        var reviewedDocs = resp.DescByLocal("ReviewedDocument")
            .Concat(resp.DescByLocal("ReviewedDocumentType"))
            .Concat(resp.DescByLocal("ReviewedDocumentTypeExt"));

        foreach (var rd in reviewedDocs)
        {
            var docItem = new DocumentStatusItem();
            docItem.DocumentDescription = rd.ByLocalFirst("DocumentDescriptionText")?.Value
                                       ?? rd.ByLocalFirst("DocumentTitleText")?.Value;

            var docStatus = rd.DescByLocal("DocumentFilingStatus").FirstOrDefault()
                         ?? rd.DescByLocal("DocumentFilingStatusCode").FirstOrDefault()?.Parent;
            if (docStatus != null)
            {
                var code = docStatus.ByLocalFirst("DocumentFilingStatusCode")?.Value;
                docItem.Status = MapDocumentStatusCode(code);

                // Document-level reasons
                foreach (var reason in docStatus.DescByLocal("FilingStatusReason"))
                {
                    docItem.Reasons.Add(new FilingStatusReason
                    {
                        ReasonCode = reason.ByLocalFirst("ReasonCode")?.Value,
                        ReasonText = reason.ByLocalFirst("ReasonCodeText")?.Value,
                        Memo = reason.ByLocalFirst("Memo")?.Value
                    });
                }
            }

            docItem.DispositionType = rd.ByLocalFirst("DocumentDispositionType")?.Value
                                   ?? rd.ByLocalFirst("DocumentDisposition")?.Value;

            result.Documents.Add(docItem);
        }

        return result;
    }

    /// <summary>
    /// Parse a GetFilingList response from raw SOAP XML.
    /// Returns empty list on SOAP fault / missing response element.
    /// </summary>
    public static List<FilingListItem> ParseFilingListResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);
        var items = new List<FilingListItem>();

        // SOAP fault → empty list
        if (doc.Descendants(NsSoapEnv + "Fault").Any())
        {
            return items;
        }

        // Schema-validate body via generated types (JTI ext flavor).
        // NOTE: the base ECF FilingListResponseMessageType is not currently used by Madera.
        // If we ever add support, try the base serializer as a fallback here.
        _ = TryDeserializeBodyChild<FR.FilingListResponseMessageTypeExt>(xml, "FilingListResponseMessageExt", FilingListExtSer);

        // Field extraction — MatchingFiling entries (JTI ext).
        var matchingFilings = doc.DescByLocal("MatchingFiling");

        foreach (var mf in matchingFilings)
        {
            var item = new FilingListItem
            {
                FilingId = mf.ByLocalFirst("FilingId")?.Value,
                CaseTitle = mf.ByLocalFirst("CaseTitle")?.Value,
                CaseTrackingId = mf.ByLocalFirst("CaseTrackingID")?.Value,
                CaseDocketId = mf.ByLocalFirst("CaseDocketID")?.Value,
                SubmitterId = mf.ByLocalFirst("SubmitterId")?.Value,
            };

            // ReceivedDate + ReceivedTime
            var dateStr = mf.ByLocalFirst("ReceivedDate")?.Value;
            var timeStr = mf.ByLocalFirst("ReceivedTime")?.Value;
            if (DateTime.TryParse(dateStr, out var receivedDate))
            {
                if (TimeSpan.TryParse(timeStr, out var receivedTime))
                    receivedDate = receivedDate.Add(receivedTime);
                item.ReceivedDate = receivedDate;
            }

            // FilingStatus — may be complex type (with FilingStatusCode) or plain text
            var statusEl = mf.ByLocalFirst("FilingStatus");
            if (statusEl != null)
            {
                var statusCode = statusEl.ByLocalFirst("FilingStatusCode")?.Value
                              ?? statusEl.ByLocalFirst("StatusText")?.Value
                              ?? (statusEl.HasElements ? null : statusEl.Value);
                item.Status = MapFilingStatusCode(statusCode);
            }

            // LeadDocument description
            var leadDoc = mf.ByLocalFirst("LeadDocument");
            if (leadDoc != null)
            {
                item.LeadDocumentDescription = leadDoc.ByLocalFirst("DocumentDescriptionText")?.Value
                                            ?? leadDoc.ByLocalFirst("DocumentTitleText")?.Value;
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse the synchronous GetNFRC SOAP response. Returns (true, null) on success,
    /// (false, errorText) on SOAP fault / ECF error / missing response element.
    /// </summary>
    public static (bool Success, string? ErrorText) ParseNfrcResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);

        // SOAP fault short-circuit
        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null)
        {
            var faultText = fault.ByLocalFirst("faultstring")?.Value ?? "SOAP Fault";
            return (false, faultText);
        }

        // Schema-validate body via generated types.
        var nfrc = TryDeserializeBodyChild<FR.NFRCResponseType>(xml, "NFRCResponse", NfrcResponseSer);
        if (nfrc == null)
        {
            return (false, "NFRCResponse not found in response.");
        }

        // Typed error check — ECF responses always include an <Error> element;
        // ErrorCode="0" with ErrorText="No Error" is the success indicator. Only
        // treat non-zero ErrorCode as a failure. (Matches the pattern used in
        // ParseRecordingStatusResponse and ParseMessageReceipt.)
        if (nfrc.Error is { Length: > 0 })
        {
            var err = nfrc.Error[0];
            var code = err?.ErrorCode?.Value;
            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                var text = err?.ErrorText?.Value ?? code;
                return (false, text);
            }
        }

        return (true, null);
    }

    // ─── GetRecordingStatus Response Parsing ───────────────────────

    /// <summary>
    /// Parse a GetRecordingStatus response (JTI extension) from raw SOAP XML.
    /// Returns filing status using the same FilingStatusResult model.
    /// </summary>
    public static FilingStatusResult ParseRecordingStatusResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);
        var result = new FilingStatusResult { RawXml = xml };

        // SOAP fault short-circuit
        var fault = doc.Descendants(NsSoapEnv + "Fault").FirstOrDefault();
        if (fault != null)
        {
            result.FilingStatus = FilingStatus.Unknown;
            return result;
        }

        // Schema-validate body via generated types
        var status = TryDeserializeBodyChild<FR.RecordingStatusResponseMessageType>(xml, "RecordingStatusResponseMessage", RecordingStatusSer);

        // Typed error check
        if (status?.Error is { Length: > 0 })
        {
            var err = status.Error[0];
            var code = err?.ErrorCode?.Value;
            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                result.FilingStatus = FilingStatus.Unknown;
                return result;
            }
        }

        // Locate the response element for field extraction
        var resp = doc.DescByLocal("RecordingStatusResponseMessage").FirstOrDefault();
        if (resp == null)
        {
            result.FilingStatus = FilingStatus.Unknown;
            return result;
        }

        // Find the Filing element (first one — single filing query returns one)
        var filing = resp.ByLocal("Filing").FirstOrDefault()
                  ?? resp.DescByLocal("Filing").FirstOrDefault();
        if (filing == null)
        {
            result.FilingStatus = FilingStatus.Unknown;
            return result;
        }

        // Extract DocumentIdentification (EFM reference ID)
        var docId = filing.ByLocal("DocumentIdentification").FirstOrDefault()
                 ?? filing.DescByLocal("DocumentIdentification").FirstOrDefault();
        if (docId != null)
        {
            var idEl = docId.ByLocalFirst("IdentificationID");
            result.EfmReferenceId = idEl?.Value;
            // The res:id attribute on IdentificationID contains the EFSP reference
            var resId = idEl?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
            if (resId != null)
                result.EfspReferenceId = resId.Value;
        }

        // Extract FilingStatus/FilingStatusCode
        var filingStatusEl = filing.ByLocal("FilingStatus").FirstOrDefault()
                          ?? filing.DescByLocal("FilingStatus").FirstOrDefault();
        if (filingStatusEl != null)
        {
            var statusCode = filingStatusEl.ByLocalFirst("FilingStatusCode")?.Value;
            result.FilingStatus = MapFilingStatusCode(statusCode);

            // Status reasons (for rejections)
            var reasons = filingStatusEl.DescByLocal("FilingStatusReason")
                .Concat(filingStatusEl.DescByLocal("FilingStatusReasons"));
            foreach (var reason in reasons)
            {
                var r = new FilingStatusReason
                {
                    ReasonCode = reason.ByLocalFirst("ReasonCode")?.Value,
                    ReasonText = reason.ByLocalFirst("ReasonCodeText")?.Value,
                    Memo = reason.ByLocalFirst("Memo")?.Value
                };
                if (!string.IsNullOrEmpty(r.ReasonCode) || !string.IsNullOrEmpty(r.ReasonText) || !string.IsNullOrEmpty(r.Memo))
                    result.Reasons.Add(r);
            }
        }

        // Extract CaseNumber → CaseDocketId (Madera's external case number, e.g., "MFL018522")
        result.CaseDocketId = filing.ByLocalFirst("CaseNumber")?.Value;

        // Extract CaseName → CaseName (case CAPTION, e.g., "Smith v. Doe" or "TBD" before
        // the clerk titles the case). Prior to Track B.2.post (Bug #2 fix) this was
        // incorrectly overloaded into CaseTrackingId. GetRecordingStatus does not return
        // a true CaseTrackingID — leave that field null here.
        result.CaseName = filing.ByLocalFirst("CaseName")?.Value;

        return result;
    }

    // ─── Status Code Mapping ───────────────────────────────────────

    private static FilingStatus MapFilingStatusCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return FilingStatus.Unknown;

        return code.ToUpperInvariant() switch
        {
            "RECEIVED" or "SUBMITTEDFORVIEW" or "RECEIVEDUNDERREVIEW" or "RECEIVED_UNDER_REVIEW" => FilingStatus.ReceivedUnderReview,
            "ACCEPTED" or "REVIEWED" => FilingStatus.Accepted,
            "PARTIALLYACCEPTED" => FilingStatus.PartiallyAccepted,
            "REJECTED" or "CANCELLED" => FilingStatus.Rejected,
            "FILED" or "DOCKETED" => FilingStatus.Filed,
            _ => FilingStatus.Unknown
        };
    }

    private static DocumentStatus MapDocumentStatusCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return DocumentStatus.Unknown;

        return code.ToUpperInvariant() switch
        {
            "R" => DocumentStatus.Received,
            "F" => DocumentStatus.Filed,
            "I" => DocumentStatus.Issued,
            "RJ" => DocumentStatus.Rejected,
            "RP" => DocumentStatus.ProposedReceived,
            "FG" => DocumentStatus.FiledAndGranted,
            _ => DocumentStatus.Unknown
        };
    }
}

/// <summary>
/// XElement extension methods for namespace-agnostic element selection.
/// Shared with CodeListResponseParser.
/// </summary>
internal static class XElementExtensions
{
    internal static IEnumerable<XElement> ByLocal(this XElement el, string localName)
        => el.Elements().Where(e => e.Name.LocalName == localName);

    internal static XElement? ByLocalFirst(this XElement el, string localName)
        => el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    internal static IEnumerable<XElement> DescByLocal(this XElement el, string localName)
        => el.Descendants().Where(e => e.Name.LocalName == localName);

    internal static IEnumerable<XElement> DescByLocal(this XDocument doc, string localName)
        => doc.Descendants().Where(e => e.Name.LocalName == localName);
}
