using EFiling.Core.Models;
using EFiling.Providers.JTI.Rest;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for <see cref="JtiRestClient.RewriteUrlIfNeeded"/>.
/// Track C follow-up — the transport-layer audit flagged this helper as missing direct coverage.
/// The method is critical for courts (like Madera) where GetPolicy returns internal-only hostnames
/// that must be rewritten to the configured public RestBaseUrl before the request is sent.
/// </summary>
public class JtiRestClientTests
{
    // ── Empty / null config.RestBaseUrl ──

    [Fact]
    public void RewriteUrl_EmptyRestBaseUrl_ReturnsUrlUnchanged()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "" };
        var url = "https://internal.example.com/codelist?type=PARTY_TYPE";

        Assert.Equal(url, JtiRestClient.RewriteUrlIfNeeded(cfg, url));
    }

    // ── Happy path: hostnames differ → rewrite ──

    [Fact]
    public void RewriteUrl_DifferentHost_RewritesSchemeHostPortAndPreservesPathQuery()
    {
        var cfg = new CourtConfiguration
        {
            RestBaseUrl = "https://pub-efm-madera-ca.ecourt.com"
        };
        var url = "http://aux-efm-madera-ca.ecourt.com:8080/efm/v4/niem/codelist?type=PARTY_TYPE&x=1";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        // Scheme, host, and port come from the RestBaseUrl (443 is default for https → omitted).
        // Path, query, and any fragment come from the original URL.
        Assert.Equal("https://pub-efm-madera-ca.ecourt.com/efm/v4/niem/codelist?type=PARTY_TYPE&x=1", result);
    }

    [Fact]
    public void RewriteUrl_BaseHttpsUrl_OmitsDefaultPort()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "http://internal.example.com/api/endpoint";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal("https://public.example.com/api/endpoint", result);
        Assert.DoesNotContain(":443", result);
    }

    [Fact]
    public void RewriteUrl_BaseCustomPort_PreservesBasePort()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com:8443" };
        var url = "http://internal.example.com/api/endpoint";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal("https://public.example.com:8443/api/endpoint", result);
    }

    [Fact]
    public void RewriteUrl_PreservesQueryString()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "http://internal.example.com/codelist?type=CASE_TYPE&courtId=madera&nocache=1";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.EndsWith("/codelist?type=CASE_TYPE&courtId=madera&nocache=1", result);
    }

    [Fact]
    public void RewriteUrl_PreservesFragment()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "http://internal.example.com/path#section";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal("https://public.example.com/path#section", result);
    }

    [Fact]
    public void RewriteUrl_SchemeDowngradeSupported()
    {
        // Rare but valid: if a court deliberately configures http for its public endpoint.
        var cfg = new CourtConfiguration { RestBaseUrl = "http://public.example.com" };
        var url = "https://internal.example.com/api";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal("http://public.example.com/api", result);
    }

    // ── Host already matches → no-op ──

    [Fact]
    public void RewriteUrl_SameHost_ReturnsUrlUnchanged()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "https://public.example.com/codelist?type=PARTY_TYPE";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void RewriteUrl_SameHostDifferentCase_ReturnsUrlUnchanged()
    {
        // RFC 3986: host component is case-insensitive. A case-only difference should NOT
        // trigger a rewrite (it would loop URLs through UriBuilder unnecessarily).
        var cfg = new CourtConfiguration { RestBaseUrl = "https://PUBLIC.EXAMPLE.COM" };
        var url = "https://public.example.com/codelist";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    // ── Unparseable inputs → return unchanged ──

    [Fact]
    public void RewriteUrl_UnparseableInputUrl_ReturnsUnchanged()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "not a url at all";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void RewriteUrl_RelativeInputUrl_ReturnsUnchanged()
    {
        // RewriteUrlIfNeeded only handles absolute URLs; a relative URL gets passed through.
        var cfg = new CourtConfiguration { RestBaseUrl = "https://public.example.com" };
        var url = "/codelist?type=CASE_TYPE";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void RewriteUrl_UnparseableBaseUrl_ReturnsInputUnchanged()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "not a base url" };
        var url = "https://internal.example.com/api";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void RewriteUrl_RelativeBaseUrl_ReturnsInputUnchanged()
    {
        var cfg = new CourtConfiguration { RestBaseUrl = "/some/relative/path" };
        var url = "https://internal.example.com/api";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, url);

        Assert.Equal(url, result);
    }

    // ── Realistic Madera-style scenario ──

    [Fact]
    public void RewriteUrl_MaderaAuxToPub_RewritesCorrectly()
    {
        // Real scenario: Madera's GetPolicy returns URLs like
        //   https://aux-efm-madera-ca.ecourt.com/efm/v4/niem/codelist?type=X
        // but we're configured to hit the public auxiliary endpoint:
        //   https://aux-pub-efm-madera-ca.ecourt.com
        var cfg = new CourtConfiguration
        {
            CourtId = "madera",
            RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com"
        };
        var internalUrl = "https://aux-efm-madera-ca.ecourt.com/efm/v4/niem/codelist?type=PARTY_TYPE";

        var result = JtiRestClient.RewriteUrlIfNeeded(cfg, internalUrl);

        Assert.Equal(
            "https://aux-pub-efm-madera-ca.ecourt.com/efm/v4/niem/codelist?type=PARTY_TYPE",
            result);
    }
}
