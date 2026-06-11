using System.Net.Http.Headers;
using System.Text;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EFiling.Providers.JTI.Rest;

/// <summary>
/// Low-level REST HTTP client for JTI code list / lookup endpoints.
/// Uses HTTP Basic Auth with the same credentials as SOAP.
/// Includes retry with exponential backoff for transient failures.
/// </summary>
public class JtiRestClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;

    /// <summary>Maximum number of retry attempts for transient failures.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between retries (doubles each attempt).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public JtiRestClient(ILogger? logger = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _ownsHttpClient = true;
        _logger = logger ?? NullLogger.Instance;
    }

    public JtiRestClient(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Send an authenticated GET request and return the raw XML body.
    /// Retries transient failures with exponential backoff.
    /// </summary>
    public async Task<string> GetAsync(CourtConfiguration config, string url, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        url = RewriteUrlIfNeeded(config, url);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await GetOnceAsync(config, url, ct);
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt);
                _logger.LogWarning(ex,
                    "REST request to {Url} failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    url, attempt + 1, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task<string> GetOnceAsync(CourtConfiguration config, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _logger.LogDebug("REST GET {Url}", url);

        var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        _logger.LogDebug("REST response from {Url}: HTTP {StatusCode} ({ResponseLength} bytes)",
            url, (int)response.StatusCode, responseBody.Length);

        if (!response.IsSuccessStatusCode)
        {
            throw new JtiSoapException(
                $"REST request to {url} failed with HTTP {(int)response.StatusCode} {response.StatusCode}",
                (int)response.StatusCode,
                responseBody);
        }

        return responseBody;
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is TaskCanceledException or OperationCanceledException)
            return true;

        if (ex is HttpRequestException httpEx)
            return httpEx.StatusCode is null
                or System.Net.HttpStatusCode.RequestTimeout
                or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable
                or System.Net.HttpStatusCode.GatewayTimeout;

        if (ex is JtiSoapException soapEx)
            return soapEx.HttpStatusCode is 502 or 503 or 504 or 408;

        return false;
    }

    /// <summary>
    /// The GetPolicy response may return REST URLs with internal hostnames
    /// (e.g., aux-efm-madera-ca.ecourt.com) that aren't externally reachable.
    /// If a RestBaseUrl is configured, rewrite the scheme/host/port portion to match
    /// the configured public endpoint while preserving the original path + query + fragment.
    ///
    /// Behavior (see `JtiRestClientTests` for full coverage):
    /// <list type="bullet">
    ///   <item>Empty/null <c>config.RestBaseUrl</c> → return <paramref name="url"/> unchanged</item>
    ///   <item>Unparseable <paramref name="url"/> or <c>RestBaseUrl</c> → return unchanged</item>
    ///   <item>Hostnames already match (case-insensitive) → return unchanged</item>
    ///   <item>Hostnames differ → rewrite scheme/host/port from <c>RestBaseUrl</c>;
    ///         keep original path, query, and fragment</item>
    /// </list>
    /// Exposed as <c>internal</c> (not <c>private</c>) only to allow unit testing via
    /// <c>InternalsVisibleTo("EFiling.Tests")</c>. Do not call from outside the assembly.
    /// </summary>
    internal static string RewriteUrlIfNeeded(CourtConfiguration config, string url)
    {
        if (string.IsNullOrEmpty(config.RestBaseUrl))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var originalUri))
            return url;

        if (!Uri.TryCreate(config.RestBaseUrl, UriKind.Absolute, out var baseUri))
            return url;

        // Only rewrite if hostnames differ (internal vs public)
        if (string.Equals(originalUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return url;

        // Rebuild URL with the public host but keep the original path + query
        var builder = new UriBuilder(originalUri)
        {
            Scheme = baseUri.Scheme,
            Host = baseUri.Host,
            Port = baseUri.Port
        };

        return builder.Uri.ToString();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
