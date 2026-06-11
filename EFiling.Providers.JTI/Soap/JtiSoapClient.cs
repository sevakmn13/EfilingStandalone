using System.Net.Http.Headers;
using System.Text;
using EFiling.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EFiling.Providers.JTI.Soap;

/// <summary>
/// Low-level SOAP HTTP client for JTI endpoints.
/// Handles HTTP Basic Auth, Content-Type, SOAPAction headers.
/// Includes retry with exponential backoff for transient failures.
///
/// <para>
/// <b>MTOM / XOP: NOT SUPPORTED.</b>
/// This client sends and receives plain SOAP 1.1 XML only (<c>Content-Type: text/xml</c>).
/// It does <i>not</i> produce or parse MTOM-encoded multipart responses
/// (<c>multipart/related; type="application/xop+xml"</c>).
/// </para>
/// <para>
/// This is a deliberate simplification, not an oversight:
/// <list type="bullet">
///   <item>
///     <b>Requests:</b> We upload documents via URL reference
///     (<c>nc:BinaryLocationURI</c> in the filing XML). JTI fetches the document from our
///     blob storage over HTTPS. Binary content is never embedded inline in the SOAP body,
///     so request-side MTOM is not needed.
///   </item>
///   <item>
///     <b>Responses:</b> All currently-used JTI endpoints return pure XML — error codes,
///     references, metadata. Court-generated documents (conformed copies, receipts) are
///     delivered separately via NFRC callbacks that carry <c>BinaryLocationURI</c> pointing
///     to an HTTP endpoint, not inline binary.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Risk / future work:</b> If a JTI court ever returns inline binary (e.g., an MTOM
/// attachment on a NFRC callback or a conformed-copy response), this client will either
/// fail to parse or return the raw multipart bytes as a broken string. That would manifest
/// as a schema-validation error at the parser layer (since the body won't be well-formed
/// XML). When/if that happens, add MTOM handling using <c>System.Xml.XmlReaderSettings</c>
/// or a multipart parser here. Tracked as a documented limitation — see
/// <c>docs/EFILING_AUDIT_TRACK_C_TRANSPORT.md</c> §2.1.
/// </para>
/// </summary>
public class JtiSoapClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;

    /// <summary>Maximum number of retry attempts for transient failures.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between retries (doubles each attempt).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout
    {
        get => _httpClient.Timeout;
        set => _httpClient.Timeout = value;
    }

    /// <summary>
    /// Create a new JtiSoapClient with an internally managed HttpClient.
    /// </summary>
    public JtiSoapClient(ILogger? logger = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _ownsHttpClient = true;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Create a new JtiSoapClient with an externally provided HttpClient (e.g., from IHttpClientFactory).
    /// </summary>
    public JtiSoapClient(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Send a SOAP request to the specified endpoint with Basic Auth from the court configuration.
    /// Returns the raw XML response body. Retries transient failures with exponential backoff.
    /// </summary>
    public async Task<string> SendAsync(CourtConfiguration config, string endpoint, string soapXmlBody, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(soapXmlBody);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(config, endpoint, soapXmlBody, ct);
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt);
                _logger.LogWarning(ex,
                    "SOAP request to {Endpoint} failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    endpoint, attempt + 1, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task<string> SendOnceAsync(CourtConfiguration config, string endpoint, string soapXmlBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        // HTTP Basic Auth
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        // SOAPAction: empty string (JTI requirement)
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");

        // Content — always plain SOAP 1.1 XML.
        // MTOM is deliberately unsupported; see class-level summary and
        // docs/EFILING_AUDIT_TRACK_C_TRANSPORT.md §2.1.
        request.Content = new StringContent(soapXmlBody, Encoding.UTF8, "text/xml");

        _logger.LogDebug("SOAP request to {Endpoint} ({ContentLength} bytes)", endpoint, soapXmlBody.Length);

        var response = await _httpClient.SendAsync(request, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogDebug("SOAP response from {Endpoint}: HTTP {StatusCode} ({ResponseLength} bytes)",
            endpoint, (int)response.StatusCode, responseBody.Length);

        if (!response.IsSuccessStatusCode)
        {
            throw new JtiSoapException(
                $"SOAP request to {endpoint} failed with HTTP {(int)response.StatusCode} {response.StatusCode}",
                (int)response.StatusCode,
                responseBody);
        }

        return responseBody;
    }

    /// <summary>
    /// Determines if an exception represents a transient failure that should be retried.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        // Timeout
        if (ex is TaskCanceledException or OperationCanceledException)
            return true;

        // Network errors
        if (ex is HttpRequestException httpEx)
            return httpEx.StatusCode is null // connection failure
                or System.Net.HttpStatusCode.RequestTimeout
                or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable
                or System.Net.HttpStatusCode.GatewayTimeout;

        // HTTP 500/502/503/504 from our own JtiSoapException
        if (ex is JtiSoapException soapEx)
            return soapEx.HttpStatusCode is 502 or 503 or 504 or 408;

        return false;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
