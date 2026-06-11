using EFiling.Core.Interfaces;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Rest;
using EFiling.Providers.JTI.Soap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EFiling.Providers.JTI;

/// <summary>
/// JTI (Journal Technologies) eFiling provider implementation.
/// Handles SOAP/REST communication with JTI's ECF 4.0 + extension endpoints.
/// </summary>
public class JtiEFilingProvider : IEFilingProvider, IDisposable
{
    private readonly IEFilingCache _cache;
    private readonly JtiSoapClient _soapClient;
    private readonly JtiRestClient _restClient;
    private readonly ILogger _logger;
    private readonly bool _ownsClients;

    private const string PolicyCacheKeyPrefix = "efiling:policy:";
    private const string CodeListCacheKeyPrefix = "efiling:codelist:";
    private const string DocListCacheKeyPrefix = "efiling:doclist:";
    private static readonly TimeSpan PolicyCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan CodeListCacheDuration = TimeSpan.FromHours(12);

    public string ProviderName => "JTI";

    public JtiEFilingProvider(IEFilingCache cache, ILogger<JtiEFilingProvider>? logger = null)
        : this(cache, new JtiSoapClient(logger), new JtiRestClient(logger), logger)
    {
        _ownsClients = true;
    }

    public JtiEFilingProvider(IEFilingCache cache, JtiSoapClient soapClient, JtiRestClient restClient, ILogger? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _soapClient = soapClient ?? throw new ArgumentNullException(nameof(soapClient));
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        _logger = logger ?? NullLogger.Instance;
        _ownsClients = false;
    }

    // ─── Court Policy ───────────────────────────────────────────────

    public async Task<CourtPolicy> GetPolicyAsync(CourtConfiguration config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Check cache first
        var cacheKey = $"{PolicyCacheKeyPrefix}{config.CourtId}";
        var cached = await _cache.GetAsync<CourtPolicy>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogDebug("GetPolicy cache hit for court {CourtId}", config.CourtId);
            return cached;
        }

        _logger.LogInformation("GetPolicy for court {CourtId} at {Endpoint}", config.CourtId, config.SoapEndpoint);

        // Build SOAP request
        var sendingMdeId = ExtractHostFromEndpoint(config.SoapEndpoint);
        var requestXml = SoapEnvelopeBuilder.BuildGetPolicyRequest(sendingMdeId);

        // Send SOAP request
        var responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);

        // Check for SOAP faults
        SoapFaultParser.ThrowIfFault(responseXml);

        // Parse response
        var policy = PolicyResponseParser.Parse(responseXml);

        _logger.LogInformation("GetPolicy for {CourtId}: version={Version}, codeLists={CodeListCount}",
            config.CourtId, policy.PolicyVersionId, policy.CodeListUrls.Count);

        // Cache the result
        await _cache.SetAsync(cacheKey, policy, PolicyCacheDuration, ct);

        return policy;
    }

    /// <summary>
    /// Extract the hostname from a SOAP endpoint URL for use as SendingMDELocationID.
    /// e.g., "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/..." → "aux-pub-efm-madera-ca.ecourt.com"
    /// </summary>
    private static string ExtractHostFromEndpoint(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return uri.Host;
        return endpoint;
    }

    // ─── Code Lists ─────────────────────────────────────────────────

    public async Task<List<CodeListItem>> GetCodeListAsync(CourtConfiguration config, string codeListType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeListType);

        var cacheKey = $"{CodeListCacheKeyPrefix}{config.CourtId}:{codeListType}";
        var cached = await _cache.GetAsync<List<CodeListItem>>(cacheKey, ct);
        if (cached != null)
            return cached;

        // Get policy to find the code list URL
        var policy = await GetPolicyAsync(config, ct);
        if (!policy.CodeListUrls.TryGetValue(codeListType, out var url))
            throw new InvalidOperationException($"Code list type '{codeListType}' not found in court policy for {config.CourtId}");

        var xml = await _restClient.GetAsync(config, url, ct);
        var items = CodeListResponseParser.ParseCodeList(xml);

        await _cache.SetAsync(cacheKey, items, CodeListCacheDuration, ct);
        return items;
    }

    public async Task<List<DocumentListItem>> GetDocumentListAsync(CourtConfiguration config, string? caseType = null, bool subFiling = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var cacheKey = $"{DocListCacheKeyPrefix}{config.CourtId}:{caseType ?? "all"}:{subFiling}";
        var cached = await _cache.GetAsync<List<DocumentListItem>>(cacheKey, ct);
        if (cached != null)
            return cached;

        var policy = await GetPolicyAsync(config, ct);
        if (string.IsNullOrEmpty(policy.DocumentListUrl))
            throw new InvalidOperationException($"Document list URL not found in court policy for {config.CourtId}");

        // Build URL with query params (per JTI docs: /documentList?caseType=CL&subFiling=true)
        var url = policy.DocumentListUrl;
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(caseType))
            queryParams.Add($"caseType={Uri.EscapeDataString(caseType)}");
        if (subFiling)
            queryParams.Add("subFiling=true");
        if (queryParams.Count > 0)
            url += (url.Contains('?') ? "&" : "?") + string.Join("&", queryParams);

        var xml = await _restClient.GetAsync(config, url, ct);
        var items = CodeListResponseParser.ParseDocumentList(xml);

        await _cache.SetAsync(cacheKey, items, CodeListCacheDuration, ct);
        return items;
    }

    public async Task<List<DocumentMetadataItem>> GetDocumentMetadataAsync(CourtConfiguration config, string documentCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(documentCode);

        // Fetch all documents (cached) and find the matching one
        var allDocs = await GetDocumentListAsync(config, caseType: null, subFiling: false, ct);
        var doc = allDocs.FirstOrDefault(d => string.Equals(d.Code, documentCode, StringComparison.OrdinalIgnoreCase));
        if (doc == null)
        {
            // Try sub-filing documents
            var subDocs = await GetDocumentListAsync(config, caseType: null, subFiling: true, ct);
            doc = subDocs.FirstOrDefault(d => string.Equals(d.Code, documentCode, StringComparison.OrdinalIgnoreCase));
        }

        return doc?.MetadataItems ?? new List<DocumentMetadataItem>();
    }

    public async Task<List<CourtLocation>> GetCourtLocationsAsync(CourtConfiguration config, string? zipCode = null, string? caseType = null, string? caseCategory = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var policy = await GetPolicyAsync(config, ct);
        if (string.IsNullOrEmpty(policy.CourtLocationsUrl))
            throw new InvalidOperationException($"Court locations URL not found in court policy for {config.CourtId}");

        // Build URL: /courtLocations/zipCode/{zip} or extended /zipCode/{zip}/caseType/{ct}/caseCategory/{cc}
        // Policy URL already ends with "/zipCode/" so we just append the zip value
        var url = policy.CourtLocationsUrl;
        if (!string.IsNullOrEmpty(zipCode))
        {
            url = url.TrimEnd('/') + "/" + Uri.EscapeDataString(zipCode);
            if (!string.IsNullOrEmpty(caseType))
            {
                url += $"/caseType/{Uri.EscapeDataString(caseType)}";
                if (!string.IsNullOrEmpty(caseCategory))
                    url += $"/caseCategory/{Uri.EscapeDataString(caseCategory)}";
            }
        }

        var xml = await _restClient.GetAsync(config, url, ct);
        return CodeListResponseParser.ParseCourtLocations(xml);
    }

    public async Task<AttorneyInfo?> LookupAttorneyByBarNumberAsync(CourtConfiguration config, string barNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(barNumber);

        var policy = await GetPolicyAsync(config, ct);
        if (string.IsNullOrEmpty(policy.AttorneyListUrl))
            throw new InvalidOperationException($"Attorney list URL not found in court policy for {config.CourtId}");

        var url = $"{policy.AttorneyListUrl.TrimEnd('/')}/barNumber/{Uri.EscapeDataString(barNumber)}";
        var xml = await _restClient.GetAsync(config, url, ct);
        var attorneys = CodeListResponseParser.ParseAttorneyList(xml);
        return attorneys.FirstOrDefault();
    }

    public async Task<List<AttorneyInfo>> SearchAttorneysByNameAsync(CourtConfiguration config, string firstName, string lastName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        var policy = await GetPolicyAsync(config, ct);
        if (string.IsNullOrEmpty(policy.AttorneyListUrl))
            throw new InvalidOperationException($"Attorney list URL not found in court policy for {config.CourtId}");

        var url = $"{policy.AttorneyListUrl.TrimEnd('/')}/firstName/{Uri.EscapeDataString(firstName)}/lastName/{Uri.EscapeDataString(lastName)}";
        var xml = await _restClient.GetAsync(config, url, ct);
        return CodeListResponseParser.ParseAttorneyList(xml);
    }

    public async Task<List<AttorneyInfo>> SearchAttorneysByFirmAsync(CourtConfiguration config, string firmName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(firmName);

        var policy = await GetPolicyAsync(config, ct);
        if (string.IsNullOrEmpty(policy.AttorneyListUrl))
            throw new InvalidOperationException($"Attorney list URL not found in court policy for {config.CourtId}");

        var url = $"{policy.AttorneyListUrl.TrimEnd('/')}/firmName/{Uri.EscapeDataString(firmName)}";
        var xml = await _restClient.GetAsync(config, url, ct);
        return CodeListResponseParser.ParseAttorneyList(xml);
    }

    // ─── Case Operations ────────────────────────────────────────────

    public async Task<List<CaseInfo>> SearchCasesAsync(CourtConfiguration config, CaseSearchCriteria criteria, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(criteria);

        var mdeLocationId = ExtractHostFromEndpoint(config.CourtRecordEndpoint);
        var requestXml = SoapEnvelopeBuilder.BuildGetCaseListRequest(
            mdeLocationId,
            criteria);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.CourtRecordEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            return CaseResponseParser.ParseCaseListResponse(ex.ResponseBody);
        }

        return CaseResponseParser.ParseCaseListResponse(responseXml);
    }

    public async Task<CaseInfo?> GetCaseAsync(CourtConfiguration config, string? caseDocketId = null, string? caseTrackingId = null, bool includeParticipants = true, bool includeDocketEntries = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrEmpty(caseDocketId) && string.IsNullOrEmpty(caseTrackingId))
            throw new ArgumentException("Either caseDocketId or caseTrackingId is required.");

        var mdeLocationId = ExtractHostFromEndpoint(config.CourtRecordEndpoint);
        var requestXml = SoapEnvelopeBuilder.BuildGetCaseRequest(
            mdeLocationId,
            caseDocketId,
            caseTrackingId,
            includeParticipants,
            includeDocketEntries);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.CourtRecordEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            return CaseResponseParser.ParseCaseResponse(ex.ResponseBody);
        }

        return CaseResponseParser.ParseCaseResponse(responseXml);
    }

    // ─── Filing Operations ──────────────────────────────────────────

    public async Task<FeeCalculation> CalculateFeesAsync(CourtConfiguration config, FilingSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(submission);

        var requestXml = ReviewFilingXmlBuilder.BuildFeesCalculationRequest(submission, config);

        _logger.LogWarning("FEE_CALC_REQUEST_XML:\n{Xml}", requestXml);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            // HTTP 500 often contains a SOAP fault with useful error details
            return FilingResponseParser.ParseFeesCalculationResponse(ex.ResponseBody);
        }

        return FilingResponseParser.ParseFeesCalculationResponse(responseXml);
    }

    public async Task<FilingResult> SubmitFilingAsync(CourtConfiguration config, FilingSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(submission);

        // Safety guard: test headers must never be sent to production courts.
        // This throws rather than silently stripping the header so misconfiguration
        // is surfaced immediately instead of producing a real filing that looks like a test.
        config.RequireTestModeAllowedForEnvironment();

        _logger.LogInformation("SubmitFiling to {CourtId}: type={FilingType}, ref={EfspRef}, env={Env}, testMode={TestFilingMode}",
            config.CourtId, submission.FilingType, submission.EfspReferenceId, config.EnvironmentKind, config.TestFilingMode);

        // Surface production submissions prominently in logs so accidental prod filings are auditable.
        if (config.IsProduction)
            _logger.LogWarning("PRODUCTION_FILING_SUBMISSION: court={CourtId}, filingType={FilingType}, efspRef={EfspRef}. " +
                "This creates a real court record.", config.CourtId, submission.FilingType, submission.EfspReferenceId);

        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        // Log whether test header was injected
        if (config.TestFilingMode != Core.Enums.TestFilingMode.None)
            _logger.LogWarning("TEST_FILING_MODE={Mode} — test header injected for {CourtId}\nSOAP_HEADER_SNIPPET: {Snippet}",
                config.TestFilingMode, config.CourtId,
                requestXml.Substring(0, Math.Min(600, requestXml.Length)));
        else
            _logger.LogWarning("TEST_FILING_MODE=None for {CourtId}. SOAP_HEADER_SNIPPET: {Snippet}",
                config.CourtId,
                requestXml.Substring(0, Math.Min(600, requestXml.Length)));

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            // HTTP 500 often contains a SOAP fault with useful error details
            return FilingResponseParser.ParseMessageReceipt(ex.ResponseBody);
        }

        var result = FilingResponseParser.ParseMessageReceipt(responseXml);

        _logger.LogInformation("SubmitFiling result for {CourtId}: success={Success}, efmRef={EfmRef}, error={Error}",
            config.CourtId, result.Success, result.EfmReferenceId, result.ErrorText);

        return result;
    }

    // ─── Status Operations ──────────────────────────────────────────

    public async Task<FilingStatusResult> GetFilingStatusAsync(CourtConfiguration config, string? efmReferenceId = null, string? efspReferenceId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Prefer GetRecordingStatus (JTI documented operation) over GetFilingStatus (ECF standard but returns 4116).
        // IdentificationCategory: "efm" for EFM reference, "efsp" for EFSP reference.
        var id = efmReferenceId ?? efspReferenceId
            ?? throw new ArgumentException("Either efmReferenceId or efspReferenceId is required.");
        var category = !string.IsNullOrEmpty(efmReferenceId) ? "efm" : "efsp";

        var requestXml = SoapEnvelopeBuilder.BuildGetRecordingStatusRequest(id, category);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            return FilingResponseParser.ParseRecordingStatusResponse(ex.ResponseBody);
        }

        return FilingResponseParser.ParseRecordingStatusResponse(responseXml);
    }

    public async Task<List<FilingListItem>> GetFilingListAsync(CourtConfiguration config, FilingListCriteria criteria, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(criteria);

        var filingTypeStr = criteria.FilingType?.ToString().ToUpperInvariant();
        var statusStr = criteria.Status?.ToString().ToUpperInvariant();

        var requestXml = SoapEnvelopeBuilder.BuildGetFilingListRequest(
            config.CourtId ?? string.Empty,
            criteria.CaseDocketId,
            filingTypeStr,
            criteria.CaseType,
            statusStr,
            criteria.FromDate,
            criteria.ToDate);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            return FilingResponseParser.ParseFilingListResponse(ex.ResponseBody);
        }

        return FilingResponseParser.ParseFilingListResponse(responseXml);
    }

    public async Task<bool> RequestNfrcAsync(CourtConfiguration config, string? efmReferenceId = null, string? efspReferenceId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrEmpty(efmReferenceId) && string.IsNullOrEmpty(efspReferenceId))
            throw new ArgumentException("Either efmReferenceId or efspReferenceId is required.");

        var requestXml = SoapEnvelopeBuilder.BuildGetNfrcRequest(efmReferenceId, efspReferenceId);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            var (_, _) = FilingResponseParser.ParseNfrcResponse(ex.ResponseBody);
            return false;
        }

        var (success, _) = FilingResponseParser.ParseNfrcResponse(responseXml);
        return success;
    }

    // ─── Fee Operations ──────────────────────────────────────────

    public async Task<FeeCalculation> GetChargedAmountAsync(CourtConfiguration config, string efmReferenceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(efmReferenceId);

        _logger.LogInformation("GetChargedAmount for {CourtId}: efmRef={EfmRef}", config.CourtId, efmReferenceId);

        var requestXml = SoapEnvelopeBuilder.BuildGetChargedAmountRequest(efmReferenceId);

        string responseXml;
        try
        {
            responseXml = await _soapClient.SendAsync(config, config.SoapEndpoint, requestXml, ct);
        }
        catch (JtiSoapException ex) when (ex.HttpStatusCode == 500 && !string.IsNullOrEmpty(ex.ResponseBody))
        {
            return FilingResponseParser.ParseFeesCalculationResponse(ex.ResponseBody);
        }

        return FilingResponseParser.ParseFeesCalculationResponse(responseXml);
    }

    public void Dispose()
    {
        if (_ownsClients)
        {
            _soapClient.Dispose();
            _restClient.Dispose();
        }
    }
}
