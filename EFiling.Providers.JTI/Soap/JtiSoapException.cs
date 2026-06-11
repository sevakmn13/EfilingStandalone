namespace EFiling.Providers.JTI.Soap;

/// <summary>
/// Exception thrown when a SOAP call to JTI fails at the HTTP or SOAP fault level.
/// </summary>
public class JtiSoapException : Exception
{
    /// <summary>HTTP status code (0 if not an HTTP error).</summary>
    public int HttpStatusCode { get; }

    /// <summary>Raw response body for debugging.</summary>
    public string? ResponseBody { get; }

    /// <summary>SOAP fault code if parsed from response.</summary>
    public string? FaultCode { get; }

    /// <summary>SOAP fault string if parsed from response.</summary>
    public string? FaultString { get; }

    public JtiSoapException(string message, int httpStatusCode = 0, string? responseBody = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ResponseBody = responseBody;
    }

    public JtiSoapException(string message, string? faultCode, string? faultString, string? responseBody = null)
        : base(message)
    {
        FaultCode = faultCode;
        FaultString = faultString;
        ResponseBody = responseBody;
    }
}
