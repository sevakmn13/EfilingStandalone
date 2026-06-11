using System.Globalization;
using System.Xml;
using System.Xml.Serialization;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Parses a CourtPolicyResponseMessage SOAP response into a typed <see cref="CourtPolicy"/> model.
/// Uses the generated WSDL types (<see cref="FR.CourtPolicyResponseMessageType"/>) as the
/// schema-typed deserializer; the output shape is preserved for backward compatibility.
/// </summary>
public static class PolicyResponseParser
{
    private const string CprmNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyResponseMessage-4.0";
    private const string SoapEnvelopeNs = "http://schemas.xmlsoap.org/soap/envelope/";

    // ECFElementName values that designate special (non-code-list) endpoints.
    private const string DocumentListElementName = "ore:DocumentIdentification";
    private const string CourtLocationsElementName = "CourtName";
    private const string AttorneyListElementName = "AttorneyValue";

    // XmlSerializer caches its generated assembly per (type, XmlRootAttribute) combo;
    // keep a single instance to avoid the first-call compilation cost on every Parse.
    private static readonly XmlSerializer Serializer = new(
        typeof(FR.CourtPolicyResponseMessageType),
        new XmlRootAttribute("CourtPolicyResponseMessage") { Namespace = CprmNamespace });

    /// <summary>
    /// Parse the raw SOAP XML response into a <see cref="CourtPolicy"/> model.
    /// </summary>
    public static CourtPolicy Parse(string rawXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);

        var policyMsg = DeserializeCourtPolicyResponseMessage(rawXml);

        // Error branch (ErrorCode != 0) — preserves original JtiSoapException contract.
        CheckForErrors(policyMsg, rawXml);

        var policy = new CourtPolicy { RawXml = rawXml };

        // PolicyVersionID → int
        var versionIdStr = policyMsg.PolicyVersionID?.IdentificationID?.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(versionIdStr) && int.TryParse(versionIdStr, out var versionId))
        {
            policy.PolicyVersionId = versionId;
        }

        // PolicyLastUpdateDate (NIEM DateType with nested DateRepresentation) → DateTime
        policy.PolicyLastUpdateDate = ExtractDate(policyMsg.PolicyLastUpdateDate) ?? DateTime.MinValue;

        // RuntimePolicyParameters.CourtCodelist[] → CodeListUrls / DocumentListUrl / CourtLocationsUrl / AttorneyListUrl
        var codelists = policyMsg.RuntimePolicyParameters?.CourtCodelist;
        if (codelists != null)
        {
            foreach (var cl in codelists)
            {
                if (cl != null)
                {
                    ParseCodeListEntry(cl, policy);
                }
            }
        }

        return policy;
    }

    // ─── Deserialization (generated types) ─────────────────────────────────

    /// <summary>
    /// Locate the CourtPolicyResponseMessage element inside the SOAP Body and deserialize
    /// it via the generated types. Uses XmlReader (not XDocument.ToString) to preserve
    /// inherited xmlns declarations — needed for xsi:type prefix references to resolve.
    /// </summary>
    private static FR.CourtPolicyResponseMessageType DeserializeCourtPolicyResponseMessage(string rawXml)
    {
        using var reader = XmlReader.Create(new StringReader(rawXml));

        bool seenBody = false;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (!seenBody
                && reader.LocalName == "Body"
                && reader.NamespaceURI == SoapEnvelopeNs)
            {
                seenBody = true;
                continue;
            }

            if (seenBody && reader.LocalName == "CourtPolicyResponseMessage")
            {
                try
                {
                    return (FR.CourtPolicyResponseMessageType?)Serializer.Deserialize(reader)
                        ?? throw new JtiSoapException(
                            "CourtPolicyResponseMessage deserialization returned null",
                            responseBody: rawXml);
                }
                catch (InvalidOperationException ex)
                {
                    throw new JtiSoapException(
                        $"Failed to deserialize CourtPolicyResponseMessage: {ex.Message}",
                        responseBody: rawXml);
                }
            }
        }

        if (!seenBody)
        {
            throw new JtiSoapException("SOAP Body not found in response", responseBody: rawXml);
        }
        throw new JtiSoapException("CourtPolicyResponseMessage not found in response", responseBody: rawXml);
    }

    // ─── Field extractors ──────────────────────────────────────────────────

    /// <summary>
    /// A NIEM <see cref="FR.DateType"/> has an <c>Items</c> array that holds the actual
    /// <c>&lt;Date&gt;</c> / <c>&lt;DateTime&gt;</c> / <c>&lt;DateRepresentation&gt;</c> / <c>&lt;Year&gt;</c>
    /// payload. Real Madera responses use <c>&lt;DateRepresentation xsi:type="xsd:dateTime"&gt;</c>,
    /// where the <c>xsd</c> prefix resolves to <c>http://niem.gov/niem/proxy/xsd/2.0</c> — so
    /// <c>XmlSerializer</c> instantiates the NIEM <see cref="FR.dateTime"/> proxy class (whose
    /// <c>Value</c> property holds the lexical timestamp string). This helper tolerates all the
    /// plausible runtime shapes: DateTime boxed directly, any of the NIEM proxies
    /// (<c>dateTime</c>/<c>date</c>/<c>gYear</c>) via their <c>Value</c> property, raw strings,
    /// and XmlNode[] fallback.
    /// </summary>
    private static DateTime? ExtractDate(FR.DateType? date)
    {
        var items = date?.Items;
        if (items == null) return null;

        foreach (var item in items)
        {
            var text = ExtractDateText(item);
            if (!string.IsNullOrWhiteSpace(text)
                && DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static string? ExtractDateText(object? item) => item switch
    {
        null => null,
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        FR.dateTime dtProxy => dtProxy.Value,
        FR.date dateProxy => dateProxy.Value,
        FR.gYear yearProxy => yearProxy.Value,
        string s => s,
        System.Xml.XmlNode[] nodes => nodes.Length > 0 ? nodes[0].InnerText : null,
        _ => item.ToString()
    };

    private static void ParseCodeListEntry(FR.CourtCodelistType cl, CourtPolicy policy)
    {
        var ecfElementName = cl.ECFElementName?.Value?.Trim() ?? string.Empty;
        var url = cl.CourtCodelistURI?.IdentificationID?.FirstOrDefault()?.Value?.Trim();

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (ecfElementName.Equals(DocumentListElementName, StringComparison.OrdinalIgnoreCase))
        {
            policy.DocumentListUrl = url;
        }
        else if (ecfElementName.Equals(CourtLocationsElementName, StringComparison.OrdinalIgnoreCase))
        {
            policy.CourtLocationsUrl = url;
        }
        else if (ecfElementName.Equals(AttorneyListElementName, StringComparison.OrdinalIgnoreCase))
        {
            policy.AttorneyListUrl = url;
        }
        else
        {
            var codeListType = ExtractCodeListType(url);
            if (!string.IsNullOrEmpty(codeListType))
            {
                policy.CodeListUrls[codeListType] = url;
            }
        }
    }

    /// <summary>
    /// Extract the code list type from a URL like <c>.../codeList?type=CASE_TYPE</c>.
    /// </summary>
    private static string? ExtractCodeListType(string url)
    {
        var idx = url.IndexOf("type=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + 5; // length of "type="
        var end = url.IndexOf('&', start);
        return end < 0 ? url[start..] : url[start..end];
    }

    /// <summary>
    /// Throws <see cref="JtiSoapException"/> if any Error element has a non-zero ErrorCode.
    /// </summary>
    private static void CheckForErrors(FR.CourtPolicyResponseMessageType msg, string rawXml)
    {
        if (msg.Error == null) return;

        foreach (var err in msg.Error)
        {
            if (err == null) continue;
            var code = err.ErrorCode?.Value;
            var text = err.ErrorText?.Value;

            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                throw new JtiSoapException(
                    $"GetPolicy returned error: [{code}] {text}",
                    faultCode: code,
                    faultString: text,
                    responseBody: rawXml);
            }
        }
    }
}
