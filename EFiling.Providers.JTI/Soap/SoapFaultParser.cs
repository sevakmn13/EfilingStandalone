using System.Xml.Linq;

namespace EFiling.Providers.JTI.Soap;

/// <summary>
/// Detects and parses SOAP Fault responses from JTI.
/// </summary>
public static class SoapFaultParser
{
    private static readonly XNamespace NsSoap = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>
    /// Check if the response contains a SOAP Fault. If so, throw a <see cref="JtiSoapException"/>.
    /// If not a fault, returns normally.
    /// </summary>
    public static void ThrowIfFault(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return;

        // Quick check before parsing
        if (!rawXml.Contains("Fault", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var doc = XDocument.Parse(rawXml);
            var body = doc.Descendants(NsSoap + "Body").FirstOrDefault();
            var fault = body?.Element(NsSoap + "Fault");

            if (fault == null)
                return;

            var faultCode = fault.Element("faultcode")?.Value ?? fault.Element("Code")?.Value;
            var faultString = fault.Element("faultstring")?.Value ?? fault.Element("Reason")?.Value;
            var detail = fault.Element("detail")?.Value ?? fault.Element("Detail")?.Value;

            throw new JtiSoapException(
                $"SOAP Fault: [{faultCode}] {faultString}" + (detail != null ? $" Detail: {detail}" : ""),
                faultCode: faultCode,
                faultString: faultString,
                responseBody: rawXml);
        }
        catch (JtiSoapException)
        {
            throw; // Re-throw our own exception
        }
        catch
        {
            // If we can't parse it, it's probably not a SOAP fault — ignore
        }
    }
}
