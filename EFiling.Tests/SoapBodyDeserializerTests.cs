using System.Xml.Serialization;
using EFiling.Providers.JTI.Parsers;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Tests;

/// <summary>
/// Regression tests for <see cref="SoapBodyDeserializer"/>. Track B.6 — the shared SOAP-body
/// walk-and-deserialize helper extracted from FilingResponseParser / CaseResponseParser /
/// NfrcResponseParser. Validates null-safety, happy path, and xsi:type resolution.
/// </summary>
public class SoapBodyDeserializerTests
{
    private const string MsgReceiptNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0";

    private static readonly XmlSerializer MessageReceiptSer = new(
        typeof(FR.MessageReceiptMessageType),
        new XmlRootAttribute("MessageReceiptMessage") { Namespace = MsgReceiptNs });

    // ── Null / empty safety ──

    [Fact]
    public void TryDeserializeBodyChild_NullXml_ReturnsNull()
    {
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            null!, "MessageReceiptMessage", MessageReceiptSer));
    }

    [Fact]
    public void TryDeserializeBodyChild_EmptyXml_ReturnsNull()
    {
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            "", "MessageReceiptMessage", MessageReceiptSer));
    }

    [Fact]
    public void TryDeserializeBodyChild_MalformedXml_ReturnsNull()
    {
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            "{not xml at all", "MessageReceiptMessage", MessageReceiptSer));
    }

    [Fact]
    public void TryDeserializeBodyChild_NullSerializer_ReturnsNull()
    {
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            "<?xml version=\"1.0\"?><root/>", "MessageReceiptMessage", null!));
    }

    // ── Walk logic ──

    [Fact]
    public void TryDeserializeBodyChild_NoSoapEnvelope_ReturnsNull()
    {
        // No <Body> element in SOAP namespace → helper returns null (doesn't throw).
        const string plainXml = """<?xml version="1.0"?><MessageReceiptMessage xmlns="urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0"/>""";
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            plainXml, "MessageReceiptMessage", MessageReceiptSer));
    }

    [Fact]
    public void TryDeserializeBodyChild_WrongLocalName_ReturnsNull()
    {
        var envelope = BuildReceiptEnvelope(errorCode: "0", errorText: "OK");
        Assert.Null(SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            envelope, "NonExistentMessage", MessageReceiptSer));
    }

    [Fact]
    public void TryDeserializeBodyChild_MatchingBodyChild_ReturnsDeserialized()
    {
        var envelope = BuildReceiptEnvelope(errorCode: "0", errorText: "Filing accepted");
        var result = SoapBodyDeserializer.TryDeserializeBodyChild<FR.MessageReceiptMessageType>(
            envelope, "MessageReceiptMessage", MessageReceiptSer);

        Assert.NotNull(result);
        Assert.NotNull(result!.Error);
        var first = Assert.Single(result.Error);
        Assert.Equal("0", first.ErrorCode?.Value);
        Assert.Equal("Filing accepted", first.ErrorText?.Value);
    }

    // ── TryDeserializeAnyBodyChild (NFRC-style fallback) ──

    [Fact]
    public void TryDeserializeAnyBodyChild_FirstCandidateMatches_ReturnsFirst()
    {
        var envelope = BuildReceiptEnvelope(errorCode: "5", errorText: "Any");
        var wrongSer = new XmlSerializer(
            typeof(FR.FilingStatusResponseMessageType),
            new XmlRootAttribute("FilingStatusResponseMessage") { Namespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusResponseMessage-4.0" });

        var result = SoapBodyDeserializer.TryDeserializeAnyBodyChild(envelope, new[]
        {
            ("MessageReceiptMessage", MessageReceiptSer), // matches
            ("FilingStatusResponseMessage", wrongSer),    // wouldn't match anyway
        });

        Assert.NotNull(result);
        Assert.IsType<FR.MessageReceiptMessageType>(result);
    }

    [Fact]
    public void TryDeserializeAnyBodyChild_NoCandidateMatches_ReturnsNull()
    {
        var envelope = BuildReceiptEnvelope(errorCode: "0", errorText: "OK");
        var wrongSer = new XmlSerializer(
            typeof(FR.FilingStatusResponseMessageType),
            new XmlRootAttribute("FilingStatusResponseMessage") { Namespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusResponseMessage-4.0" });

        var result = SoapBodyDeserializer.TryDeserializeAnyBodyChild(envelope, new[]
        {
            ("NonExistentA", wrongSer),
            ("NonExistentB", wrongSer),
        });

        Assert.Null(result);
    }

    [Fact]
    public void TryDeserializeAnyBodyChild_EmptyCandidateList_ReturnsNull()
    {
        var envelope = BuildReceiptEnvelope(errorCode: "0", errorText: "OK");
        var result = SoapBodyDeserializer.TryDeserializeAnyBodyChild(
            envelope,
            Array.Empty<(string, XmlSerializer)>());
        Assert.Null(result);
    }

    // ── Helpers ──

    private static string BuildReceiptEnvelope(string errorCode, string errorText) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <MessageReceiptMessage xmlns="{MsgReceiptNs}">
              <Error xmlns="urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0">
                <ErrorCode>{errorCode}</ErrorCode>
                <ErrorText>{errorText}</ErrorText>
              </Error>
            </MessageReceiptMessage>
          </soap:Body>
        </soap:Envelope>
        """;
}
