namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Unit tests for <see cref="XmlStructuralDiff"/>.
///
/// <para>
/// Tests are organized by normalization concern — one group per rule the diff implements.
/// Each group contains (a) a POSITIVE test proving equivalence when the rule applies, and
/// (b) a NEGATIVE test proving difference detection when the rule shouldn't trigger.
/// </para>
///
/// <para>
/// These tests establish the contract for when round-trip tests SHOULD and SHOULDN'T pass
/// once the reverse parser lands. If the rules change (e.g., add "ignore specific element
/// names"), new test cases must be added here first before the behavior changes in the
/// implementation.
/// </para>
/// </summary>
public class XmlStructuralDiffTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Identical documents — baseline positive case.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalDocuments_AreEquivalent()
    {
        var xml = "<root><child>value</child></root>";
        var result = XmlStructuralDiff.Compare(xml, xml);
        Assert.True(result.AreEquivalent, result.FormatDifferences());
        Assert.Empty(result.Differences);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 1: Namespace prefix independence — different prefixes, same URI = equivalent.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_DifferentPrefixes_SameNamespace_AreEquivalent()
    {
        var expected = "<ns1:root xmlns:ns1=\"http://example.com/foo\"><ns1:child>v</ns1:child></ns1:root>";
        var actual = "<a:root xmlns:a=\"http://example.com/foo\"><a:child>v</a:child></a:root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    [Fact]
    public void Compare_DifferentNamespaces_AreNotEquivalent()
    {
        var expected = "<ns1:root xmlns:ns1=\"http://example.com/foo\"/>";
        var actual = "<ns1:root xmlns:ns1=\"http://example.com/bar\"/>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Contains(result.Differences, d => d.Type == "name-namespace");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 2: Attribute order independence.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_AttributesDifferentOrder_AreEquivalent()
    {
        var expected = "<root a=\"1\" b=\"2\" c=\"3\"/>";
        var actual = "<root c=\"3\" a=\"1\" b=\"2\"/>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    [Fact]
    public void Compare_MissingAttribute_IsReportedAsDifference()
    {
        var expected = "<root a=\"1\" b=\"2\"/>";
        var actual = "<root a=\"1\"/>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Contains(result.Differences, d => d.Type == "attribute-missing" && d.Expected.Contains("@b"));
    }

    [Fact]
    public void Compare_AttributeValueDiffers_IsReportedAsDifference()
    {
        var expected = "<root a=\"1\"/>";
        var actual = "<root a=\"2\"/>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        var diff = Assert.Single(result.Differences);
        Assert.Equal("attribute-value", diff.Type);
    }

    [Fact]
    public void Compare_UnexpectedAttribute_IsReportedAsDifference()
    {
        var expected = "<root a=\"1\"/>";
        var actual = "<root a=\"1\" b=\"2\"/>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Contains(result.Differences, d => d.Type == "attribute-unexpected");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 3: Whitespace normalization in text content.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_WhitespaceOnlyDifferencesInText_AreEquivalent()
    {
        var expected = "<root>  hello   world  </root>";
        var actual = "<root>hello world</root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    [Fact]
    public void Compare_SubstantiveTextDifference_IsReportedAsDifference()
    {
        var expected = "<root>hello</root>";
        var actual = "<root>world</root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        var diff = Assert.Single(result.Differences);
        Assert.Equal("text", diff.Type);
    }

    [Fact]
    public void Compare_FormattedVsCompactXml_AreEquivalent()
    {
        var formatted = @"<root>
    <child>
        <inner>value</inner>
    </child>
</root>";
        var compact = "<root><child><inner>value</inner></child></root>";

        var result = XmlStructuralDiff.Compare(formatted, compact);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 4: Sibling order IS significant (element sequences are XSD-sequence-ordered).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SwappedSiblings_AreNotEquivalent()
    {
        var expected = "<root><a>1</a><b>2</b></root>";
        var actual = "<root><b>2</b><a>1</a></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        // The sibling-order mismatch surfaces as a name-local difference at each position.
        Assert.Contains(result.Differences, d => d.Type == "name-local");
    }

    [Fact]
    public void Compare_DifferentChildCount_IsReportedAsDifference()
    {
        var expected = "<root><a/><b/><c/></root>";
        var actual = "<root><a/><b/></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Contains(result.Differences, d => d.Type == "child-count");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 5: xmlns declarations are scope mechanics, not information — ignored.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_DifferentXmlnsDeclarationsButSameEffectiveNamespaces_AreEquivalent()
    {
        // Same effective namespace binding, different declaration style (root-level vs per-element).
        var expected = "<root xmlns:a=\"http://example.com/\"><a:child>v</a:child></root>";
        var actual = "<root><b:child xmlns:b=\"http://example.com/\">v</b:child></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    [Fact]
    public void Compare_ExtraXmlnsDeclarationWithNoEffectOnUsedNamespaces_AreEquivalent()
    {
        // Adding an unused xmlns shouldn't change the semantic content.
        var expected = "<root><child>v</child></root>";
        var actual = "<root xmlns:unused=\"http://example.com/unused\"><child>v</child></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 6: Nested structures — diff path tracks location for human readability.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_DifferenceDeepInTree_PathReflectsLocation()
    {
        var expected = "<root><a><b><c>expected</c></b></a></root>";
        var actual = "<root><a><b><c>actual</c></b></a></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        var diff = Assert.Single(result.Differences);
        Assert.Contains("a", diff.Path);
        Assert.Contains("b", diff.Path);
        Assert.Contains("c", diff.Path);
    }

    [Fact]
    public void Compare_MultipleDifferencesCollected_NotShortCircuited()
    {
        var expected = "<root><a>1</a><b>2</b></root>";
        var actual = "<root><a>X</a><b>Y</b></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Equal(2, result.Differences.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FormatDifferences — reports readable output for xUnit failure messages.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatDifferences_NoDifferences_ReturnsSentinel()
    {
        var result = XmlStructuralDiff.Compare("<x/>", "<x/>");
        Assert.Equal("(no differences)", result.FormatDifferences());
    }

    [Fact]
    public void FormatDifferences_MultipleDifferences_IncludesCountAndEach()
    {
        var expected = "<root><a>1</a><b>2</b></root>";
        var actual = "<root><a>X</a><b>Y</b></root>";

        var result = XmlStructuralDiff.Compare(expected, actual);
        var message = result.FormatDifferences();

        Assert.Contains("2 structural difference", message);
        Assert.Contains("[text]", message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 7: QName-typed attribute values (xsi:type) are prefix-insensitive when the
    // prefix binds to the same namespace URI. This matches XML Schema Part 1 §3.15.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_XsiTypeWithDifferentPrefixes_SameNamespace_AreEquivalent()
    {
        // Both xsi:type values point to http://niem.gov/niem/niem-core/2.0#PersonType but
        // use different prefixes (ps1 vs ns2) bound to the same URI.
        var expected = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                                xmlns:ps1=""http://niem.gov/niem/niem-core/2.0"">
                            <child xsi:type=""ps1:PersonType""/>
                         </root>";
        var actual = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                              xmlns:ns2=""http://niem.gov/niem/niem-core/2.0"">
                          <child xsi:type=""ns2:PersonType""/>
                       </root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }

    [Fact]
    public void Compare_XsiTypeWithDifferentNamespaces_AreNotEquivalent()
    {
        // ns4:AmountType (UDT namespace) vs nc:AmountType (niem-core namespace) — genuinely
        // different types per XSD, and the diff must report it.
        var expected = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                                xmlns:ns4=""urn:un:unece:uncefact:data:specification:UnqualifiedDataTypesSchemaModule:2"">
                            <child xsi:type=""ns4:AmountType""/>
                         </root>";
        var actual = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                              xmlns:nc=""http://niem.gov/niem/niem-core/2.0"">
                          <child xsi:type=""nc:AmountType""/>
                       </root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
        Assert.Contains(result.Differences, d => d.Type == "attribute-value");
    }

    [Fact]
    public void Compare_XsiTypeWithDifferentLocalNames_AreNotEquivalent()
    {
        var expected = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                                xmlns:nc=""http://niem.gov/niem/niem-core/2.0"">
                            <child xsi:type=""nc:PersonType""/>
                         </root>";
        var actual = @"<root xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                              xmlns:nc=""http://niem.gov/niem/niem-core/2.0"">
                          <child xsi:type=""nc:OrganizationType""/>
                       </root>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.False(result.AreEquivalent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Integration-shape preview — a realistic SOAP-envelope-style comparison. Exercises
    // multiple normalizations at once (prefix independence + attribute order + formatting).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SoapEnvelopeStyleDocuments_WithPrefixAndFormattingDifferences_AreEquivalent()
    {
        var expected = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/""
                                             xmlns:ns1=""http://niem.gov/niem/niem-core/2.0"">
                            <SOAP-ENV:Body>
                                <ns1:Case>
                                    <ns1:CaseCategoryText>411900</ns1:CaseCategoryText>
                                </ns1:Case>
                            </SOAP-ENV:Body>
                         </SOAP-ENV:Envelope>";

        // Same document, different prefix names + compact formatting.
        var actual = "<env:Envelope xmlns:env=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:nc=\"http://niem.gov/niem/niem-core/2.0\">"
                   + "<env:Body><nc:Case><nc:CaseCategoryText>411900</nc:CaseCategoryText></nc:Case></env:Body></env:Envelope>";

        var result = XmlStructuralDiff.Compare(expected, actual);

        Assert.True(result.AreEquivalent, result.FormatDifferences());
    }
}
