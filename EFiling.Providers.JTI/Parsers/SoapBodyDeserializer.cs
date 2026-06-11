using System.Xml;
using System.Xml.Serialization;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Shared helper for deserializing a named child element inside a SOAP envelope's Body via
/// a generated-types <see cref="XmlSerializer"/>. Three of the parsers (Filing, Case, Nfrc)
/// previously carried identical copies of this logic; centralizing it here removes the
/// duplication and keeps the schema-fence pattern consistent.
///
/// Uses <see cref="XmlReader"/> rather than <c>XDocument.ToString</c> because inherited
/// <c>xmlns</c> declarations must be preserved for <c>xsi:type</c> prefix references to
/// resolve correctly against the generated types.
///
/// Track B.6 (eFiling audit).
/// </summary>
internal static class SoapBodyDeserializer
{
    /// <summary>
    /// Standard SOAP 1.1 envelope namespace.
    /// </summary>
    public const string SoapEnvelopeNs = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>
    /// Walk the SOAP envelope to the first element inside Body with the given
    /// <paramref name="localName"/> and deserialize via <paramref name="serializer"/>.
    /// Returns <c>null</c> if the element is not found or if the XML is malformed / fails
    /// schema validation. Callers must handle the null result.
    /// </summary>
    /// <typeparam name="T">The generated-types class the body child will deserialize into.</typeparam>
    /// <param name="xml">Raw SOAP envelope XML (must contain <c>soap:Envelope/soap:Body</c>).</param>
    /// <param name="localName">Local name of the body child element (no namespace prefix).</param>
    /// <param name="serializer">A cached <see cref="XmlSerializer"/> for <typeparamref name="T"/>.</param>
    public static T? TryDeserializeBodyChild<T>(string xml, string localName, XmlSerializer serializer)
        where T : class
    {
        if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(localName) || serializer is null)
            return null;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            bool inBody = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!inBody
                    && reader.LocalName == "Body"
                    && reader.NamespaceURI == SoapEnvelopeNs)
                {
                    inBody = true;
                    continue;
                }
                if (inBody && reader.LocalName == localName)
                {
                    return (T?)serializer.Deserialize(reader);
                }
            }
            return null;
        }
        catch (XmlException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Convenience overload for callers that need to probe multiple possible body-child
    /// element names (e.g., NFRC callback variants). Returns the first successful
    /// deserialization, or <c>null</c> if none match.
    /// </summary>
    /// <param name="xml">Raw SOAP envelope XML.</param>
    /// <param name="candidates">Ordered list of <c>(localName, serializer)</c> pairs to try.</param>
    public static object? TryDeserializeAnyBodyChild(
        string xml,
        IEnumerable<(string localName, XmlSerializer serializer)> candidates)
    {
        if (string.IsNullOrEmpty(xml) || candidates is null) return null;

        foreach (var (localName, serializer) in candidates)
        {
            if (string.IsNullOrEmpty(localName) || serializer is null) continue;
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml));
                bool inBody = false;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (!inBody
                        && reader.LocalName == "Body"
                        && reader.NamespaceURI == SoapEnvelopeNs)
                    {
                        inBody = true;
                        continue;
                    }
                    if (inBody && reader.LocalName == localName)
                    {
                        var result = serializer.Deserialize(reader);
                        if (result != null) return result;
                        break; // element found but deserialization returned null — try next wrapper
                    }
                }
            }
            catch (XmlException) { /* try next wrapper */ }
            catch (InvalidOperationException) { /* schema mismatch — try next wrapper */ }
        }
        return null;
    }
}
