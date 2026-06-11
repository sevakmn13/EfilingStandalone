using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Soap;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Tests;

/// <summary>
/// EXPERIMENTAL — Track B.0 Prototype.
///
/// Purpose: answer empirically whether the otherwise-unused generated WSDL types
/// (src/EFiling/WsdlGenerated/**/Reference.cs) can be used as a type-safe layer
/// for reading JTI responses and/or validating our hand-built requests.
///
/// This test class and the Compile Include + System.ServiceModel.Primitives package
/// in EFiling.Tests.csproj are to be REMOVED after Track B.0 concludes, unless the
/// "hybrid" approach (parse-with-generated, build-with-hand) is adopted.
///
/// Scenarios:
///   1. Deserialize LASC sample Court Policy response via generated types.
///   2. Deserialize Madera real Court Policy response via generated types.
///   3. Deserialize OUR hand-built ReviewFilingRequest XML via generated types (schema self-validation).
///
/// Each test is written to surface failures loudly with full exception context so
/// we can analyze the divergence patterns, not just pass/fail.
/// </summary>
public class Prototype_WsdlGeneratedDeserializationTests
{
    // ─── Namespaces ─────────────────────────────────────────────────────────
    private const string CprmNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyResponseMessage-4.0";
    private const string WsdlNs = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0";
    private static readonly XNamespace SoapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace SoapEnvLegacy = "http://schemas.xmlsoap.org/soap/envelope/";

    // ─── Path helpers ───────────────────────────────────────────────────────
    private static readonly string DocsRoot = FindDocsRoot();
    private static string LascSamplePolicyPath =>
        Path.Combine(DocsRoot, "ECF Operations", "Get Policy", "Sample Response XML.xml");
    private static string MaderaPolicyPath =>
        Path.Combine(DocsRoot, "madera_policy_formatted.xml");

    private static string FindDocsRoot()
    {
        // Walk up from working dir until we find 'docs/fileing files'
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !Directory.Exists(Path.Combine(dir, "docs", "fileing files")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new DirectoryNotFoundException(
                "Could not locate 'docs/fileing files' by walking up from " + Directory.GetCurrentDirectory());
        return Path.Combine(dir, "docs", "fileing files");
    }

    private static XElement ExtractBodyChild(string soapXml, string localName)
    {
        var doc = XDocument.Parse(soapXml);
        var body = doc.Descendants(SoapEnv + "Body").FirstOrDefault()
                ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body")
                ?? throw new InvalidOperationException("SOAP Body not found");
        var target = body.Elements().FirstOrDefault(e => e.Name.LocalName == localName)
                ?? throw new InvalidOperationException(
                    $"Element '{localName}' not found as child of SOAP Body. Found children: "
                    + string.Join(", ", body.Elements().Select(e => e.Name.ToString())));
        return target;
    }

    /// <summary>
    /// Deserialize a body child via XmlReader positioned at it — preserves inherited
    /// xmlns declarations from ancestor elements (critical for xsi:type prefix references
    /// like "ns9:CoreFilingMessageExtType" that depend on envelope-level xmlns:ns9).
    /// </summary>
    private static T? DeserializeBodyChild<T>(string soapXml, string targetLocalName, XmlSerializer serializer) where T : class
    {
        using var reader = XmlReader.Create(new StringReader(soapXml));
        // Walk to first Element named targetLocalName inside SOAP Body
        bool inBody = false;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!inBody && reader.LocalName == "Body") { inBody = true; continue; }
            if (inBody && reader.LocalName == targetLocalName)
            {
                return (T?)serializer.Deserialize(reader);
            }
        }
        throw new InvalidOperationException($"Element '{targetLocalName}' not found inside SOAP Body");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCENARIO 1 — Deserialize LASC sample Court Policy response
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario1_Deserialize_LascCourtPolicyResponse()
    {
        var soapXml = File.ReadAllText(LascSamplePolicyPath);

        var serializer = new XmlSerializer(
            typeof(FR.CourtPolicyResponseMessageType),
            new XmlRootAttribute("CourtPolicyResponseMessage") { Namespace = CprmNs });

        FR.CourtPolicyResponseMessageType? result = null;
        Exception? error = null;
        try
        {
            result = DeserializeBodyChild<FR.CourtPolicyResponseMessageType>(
                soapXml, "CourtPolicyResponseMessage", serializer);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error != null)
        {
            Assert.Fail($"LASC Court Policy deserialization FAILED.\n" +
                        $"Exception type: {error.GetType().FullName}\n" +
                        $"Message: {error.Message}\n" +
                        $"Inner: {error.InnerException?.Message}\n" +
                        $"Deepest inner: {GetDeepestInner(error).Message}");
        }

        Assert.NotNull(result);
        Assert.NotNull(result!.PolicyVersionID);
        Assert.NotNull(result.PolicyLastUpdateDate);
        Assert.NotNull(result.RuntimePolicyParameters);
        Assert.NotNull(result.RuntimePolicyParameters.CourtCodelist);
        Assert.NotEmpty(result.RuntimePolicyParameters.CourtCodelist);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCENARIO 2 — Deserialize Madera real Court Policy response
    // (Madera has empty ECFElementName values — tests handling of edge cases)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario2_Deserialize_MaderaCourtPolicyResponse()
    {
        if (!File.Exists(MaderaPolicyPath))
        {
            Assert.Fail($"Madera policy sample not found at {MaderaPolicyPath}");
        }

        var soapXml = File.ReadAllText(MaderaPolicyPath);

        var serializer = new XmlSerializer(
            typeof(FR.CourtPolicyResponseMessageType),
            new XmlRootAttribute("CourtPolicyResponseMessage") { Namespace = CprmNs });

        FR.CourtPolicyResponseMessageType? result = null;
        Exception? error = null;
        try
        {
            result = DeserializeBodyChild<FR.CourtPolicyResponseMessageType>(
                soapXml, "CourtPolicyResponseMessage", serializer);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error != null)
        {
            Assert.Fail($"Madera Court Policy deserialization FAILED.\n" +
                        $"Exception type: {error.GetType().FullName}\n" +
                        $"Message: {error.Message}\n" +
                        $"Inner: {error.InnerException?.Message}\n" +
                        $"Deepest inner: {GetDeepestInner(error).Message}");
        }

        Assert.NotNull(result);
        Assert.NotNull(result!.PolicyVersionID);
        Assert.NotNull(result.RuntimePolicyParameters);
        Assert.NotEmpty(result.RuntimePolicyParameters.CourtCodelist);

        // Record coverage for the writeup
        WriteCoverageDump("Madera_CourtPolicy", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCENARIO 3 — Self-validation: deserialize OUR hand-built
    //              ReviewFilingRequestMessage via generated types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario3_SelfValidate_OurBuiltReviewFilingRequest()
    {
        var submission = BuildMinimalCaseInit();
        var config = TestCourtConfig();

        string soapXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        // Dump the built XML for manual review
        var dumpPath = Path.Combine(
            Path.GetTempPath(), "track_b0_built_reviewfiling.xml");
        File.WriteAllText(dumpPath, soapXml);

        var serializer = new XmlSerializer(
            typeof(FR.ReviewFilingRequestMessageType),
            new XmlRootAttribute("ReviewFilingRequestMessage") { Namespace = WsdlNs });

        FR.ReviewFilingRequestMessageType? result = null;
        Exception? error = null;
        try
        {
            result = DeserializeBodyChild<FR.ReviewFilingRequestMessageType>(
                soapXml, "ReviewFilingRequestMessage", serializer);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error != null)
        {
            Assert.Fail(
                $"Self-validation FAILED: our hand-built ReviewFilingRequest XML does NOT deserialize " +
                $"through the generated types. This means either (a) our output is not ECF/JTI-schema-compliant " +
                $"in a way the generated types detect, or (b) the generated types have a known limitation " +
                $"with ECF polymorphism. Full XML dumped to: {dumpPath}\n\n" +
                $"Exception type: {error.GetType().FullName}\n" +
                $"Message: {error.Message}\n" +
                $"Inner: {error.InnerException?.Message}\n" +
                $"Deepest inner: {GetDeepestInner(error).Message}");
        }

        Assert.NotNull(result);
        Assert.NotNull(result!.Item);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static Exception GetDeepestInner(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    private static void WriteCoverageDump(string label, FR.CourtPolicyResponseMessageType result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Track B.0 Coverage Dump: {label} ===");
        sb.AppendLine($"PolicyVersionID present: {result.PolicyVersionID != null}");
        sb.AppendLine($"PolicyLastUpdateDate present: {result.PolicyLastUpdateDate != null}");
        sb.AppendLine($"RuntimePolicyParameters present: {result.RuntimePolicyParameters != null}");
        sb.AppendLine($"DevelopmentPolicyParameters present: {result.DevelopmentPolicyParameters != null}");
        if (result.RuntimePolicyParameters?.CourtCodelist != null)
        {
            sb.AppendLine($"CourtCodelist count: {result.RuntimePolicyParameters.CourtCodelist.Length}");
            for (int i = 0; i < Math.Min(3, result.RuntimePolicyParameters.CourtCodelist.Length); i++)
            {
                var cl = result.RuntimePolicyParameters.CourtCodelist[i];
                sb.AppendLine($"  [{i}] ECFElementName={cl.ECFElementName}");
            }
        }
        var dumpPath = Path.Combine(Path.GetTempPath(), $"track_b0_coverage_{label}.txt");
        File.WriteAllText(dumpPath, sb.ToString());
    }

    private static CourtConfiguration TestCourtConfig() => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc"
    };

    private static FilingSubmission BuildMinimalCaseInit()
    {
        // Mirrors ReviewFilingXmlBuilderTests.BuildMinimalCaseInit()
        return new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = "TEST-REF-001",
            SubmitterUsername = "testuser",
            CaseTypeCode = "CV",
            CaseCategoryCode = "3701",
            JurisdictionalGroundsCode = "O10K",
            AmountInControversy = 15000m,
            LocationName = "MAD",
            Parties = new List<FilingParty>
            {
                new() { ReferenceId = "filedBy0", RoleCode = "PLAIN",
                        FirstName = "John", LastName = "Smith" },
                new() { ReferenceId = "filedAsTo0", RoleCode = "DEF",
                        IsOrganization = true, OrganizationName = "Acme Corp" },
                new() { ReferenceId = "attorney0", RoleCode = "ATT",
                        FirstName = "Jane", LastName = "Doe", BarNumber = "123456",
                        Contact = new ContactInfo {
                            MailingAddress = new StructuredAddress {
                                Address1 = "100 Main St", City = "Madera",
                                State = "CA", Zip = "93637", Country = "US", AddressType = "M"
                            },
                            PhoneNumber = "559-555-1234", PhoneType = "W",
                            Email = "jane@lawfirm.com"
                        } }
            },
            PartyAssociations = new List<PartyAssociation>
            {
                new() { AssociationType = "REPRESENTEDBY",
                        ParticipantRef = "filedBy0", RelatedParticipantRef = "attorney0" }
            },
            PartyDocumentAssociations = new List<PartyDocumentAssociation>
            {
                new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" },
                new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" }
            },
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0", DocumentCode = "COM040", SequenceNumber = 0,
                BinaryLocationUri = "https://example.com/docs/complaint.pdf",
                FileControlId = "FC001"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0", CustomerPaymentProfileId = "0", PaymentType = "ACH"
            }
        };
    }
}
