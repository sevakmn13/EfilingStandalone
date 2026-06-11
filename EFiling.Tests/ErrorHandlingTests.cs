using System.Net;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Tests for error handling: timeouts, connection failures, invalid XML,
/// retry logic with exponential backoff, and SOAP fault handling.
/// </summary>
public class ErrorHandlingTests
{
    private static CourtConfiguration TestConfig => new()
    {
        CourtId = "test",
        SoapEndpoint = "https://fake.example.com/soap",
        Username = "user",
        Password = "pass"
    };

    // ─── JtiSoapException ──────────────────────────────────────────

    [Fact]
    public void JtiSoapException_HttpStatusCode_IsPreserved()
    {
        var ex = new JtiSoapException("test error", 503, "<fault/>");
        Assert.Equal(503, ex.HttpStatusCode);
        Assert.Equal("<fault/>", ex.ResponseBody);
        Assert.Equal("test error", ex.Message);
    }

    [Fact]
    public void JtiSoapException_FaultCode_IsPreserved()
    {
        var ex = new JtiSoapException("test", "Server", "Internal error", "<xml/>");
        Assert.Equal("Server", ex.FaultCode);
        Assert.Equal("Internal error", ex.FaultString);
        Assert.Equal("<xml/>", ex.ResponseBody);
    }

    // ─── SoapFaultParser ───────────────────────────────────────────

    [Fact]
    public void SoapFaultParser_ValidFault_ThrowsJtiSoapException()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <SOAP-ENV:Fault>
      <faultcode>Server</faultcode>
      <faultstring>Authentication required</faultstring>
    </SOAP-ENV:Fault>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var ex = Assert.Throws<JtiSoapException>(() => SoapFaultParser.ThrowIfFault(xml));
        Assert.Contains("Authentication required", ex.Message);
    }

    [Fact]
    public void SoapFaultParser_NoFault_DoesNotThrow()
    {
        var xml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
  <SOAP-ENV:Body>
    <Response>OK</Response>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        SoapFaultParser.ThrowIfFault(xml); // should not throw
    }

    // ─── Retry Logic Tests ─────────────────────────────────────────

    [Fact]
    public async Task SoapClient_RetriesOnTransientFailure()
    {
        var callCount = 0;
        var handler = new TestMessageHandler(req =>
        {
            callCount++;
            if (callCount < 3)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("<error/>")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<success/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10) // fast for tests
        };

        var result = await soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>", CancellationToken.None);

        Assert.Equal("<success/>", result);
        Assert.Equal(3, callCount); // 2 failures + 1 success
    }

    [Fact]
    public async Task SoapClient_ThrowsAfterMaxRetries()
    {
        var callCount = 0;
        var handler = new TestMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<error/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        await Assert.ThrowsAsync<JtiSoapException>(
            () => soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>"));

        Assert.Equal(3, callCount); // initial + 2 retries
    }

    [Fact]
    public async Task SoapClient_DoesNotRetryOnNonTransientError()
    {
        var callCount = 0;
        var handler = new TestMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("<bad-request/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        await Assert.ThrowsAsync<JtiSoapException>(
            () => soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>"));

        Assert.Equal(1, callCount); // no retries for 400
    }

    [Fact]
    public async Task SoapClient_DoesNotRetryOnHttp500()
    {
        // HTTP 500 is NOT transient for us — the JTI provider deliberately catches 500
        // to parse SOAP faults from the response body
        var callCount = 0;
        var handler = new TestMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("<fault/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        await Assert.ThrowsAsync<JtiSoapException>(
            () => soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>"));

        Assert.Equal(1, callCount); // no retries for 500
    }

    [Fact]
    public async Task SoapClient_RetriesOn502BadGateway()
    {
        var callCount = 0;
        var handler = new TestMessageHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
                return new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("<error/>")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<ok/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        var result = await soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>");
        Assert.Equal("<ok/>", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SoapClient_RetriesOn504GatewayTimeout()
    {
        var callCount = 0;
        var handler = new TestMessageHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                {
                    Content = new StringContent("<timeout/>")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<ok/>")
            };
        });

        using var httpClient = new HttpClient(handler);
        var soapClient = new JtiSoapClient(httpClient)
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        var result = await soapClient.SendAsync(TestConfig, "https://fake.example.com/soap", "<xml/>");
        Assert.Equal("<ok/>", result);
    }

    [Fact]
    public void SoapClient_DefaultTimeout_Is120Seconds()
    {
        var client = new JtiSoapClient();
        Assert.Equal(TimeSpan.FromSeconds(120), client.Timeout);
        client.Dispose();
    }

    [Fact]
    public void SoapClient_DefaultMaxRetries_Is3()
    {
        var client = new JtiSoapClient();
        Assert.Equal(3, client.MaxRetries);
        client.Dispose();
    }

    // ─── Invalid XML Response Tests ────────────────────────────────
    // NOTE: ParseMessageReceipt and ParseFeesCalculationResponse intentionally do NOT throw
    // on malformed XML — they return an error-shaped result (Success=false with ErrorText
    // containing the XML parse message and a snippet of the raw response). This is a deliberate
    // design decision: production code calling these parsers needs a debuggable result object
    // rather than an exception when the court returns corrupt XML. The tests below match this
    // actual behavior.

    [Fact]
    public void ParseMessageReceipt_MalformedXml_ReturnsErrorResult()
    {
        var result = EFiling.Providers.JTI.Parsers.FilingResponseParser.ParseMessageReceipt("not xml at all");

        Assert.False(result.Success);
        Assert.Equal(-1, result.ErrorCode);
        Assert.NotNull(result.ErrorText);
        Assert.Contains("Invalid XML response", result.ErrorText);
    }

    [Fact]
    public void ParseFeesCalcResponse_MalformedXml_ReturnsErrorResult()
    {
        var result = EFiling.Providers.JTI.Parsers.FilingResponseParser.ParseFeesCalculationResponse("<<invalid>>");

        Assert.Equal(-1, result.ErrorCode);
        Assert.NotNull(result.ErrorText);
        Assert.Contains("Invalid XML response", result.ErrorText);
    }

    [Fact]
    public void ParseCaseResponse_MalformedXml_Throws()
    {
        Assert.ThrowsAny<Exception>(
            () => EFiling.Providers.JTI.Parsers.CaseResponseParser.ParseCaseResponse("not xml"));
    }

    [Fact]
    public void ParseCodeList_MalformedXml_Throws()
    {
        Assert.ThrowsAny<Exception>(
            () => EFiling.Providers.JTI.Parsers.CodeListResponseParser.ParseCodeList("<<bad xml>>"));
    }

    // ─── Test Message Handler ──────────────────────────────────────

    private class TestMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
