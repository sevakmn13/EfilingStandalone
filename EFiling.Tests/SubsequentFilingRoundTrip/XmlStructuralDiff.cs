using System.Xml.Linq;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Structural XML comparison utility for round-trip tests.
///
/// <para>
/// Compares two XML documents for "structural equivalence" rather than byte equality. Two
/// documents are structurally equivalent if they represent the same information content,
/// ignoring superficial serialization differences that both XML 1.0 and the JTI endpoint
/// treat as semantically irrelevant.
/// </para>
///
/// <para>
/// <b>Normalizations applied by default</b>:
/// <list type="bullet">
///   <item><b>Namespace prefix independence.</b> <c>ns1:Foo</c> and <c>a:Foo</c> are equivalent
///     when <c>ns1</c> and <c>a</c> are bound to the same namespace URI. Only the qualified name
///     (URI + local name) matters.</item>
///   <item><b>Attribute order independence.</b> Attributes are compared as sets — position within
///     an element's attribute list is not significant (per XML 1.0 §3.1).</item>
///   <item><b>Whitespace normalization in text content.</b> Leading / trailing whitespace is
///     trimmed and internal runs of whitespace are collapsed. Pure-whitespace text nodes between
///     elements are ignored. This matches the "tolerant equality" the JTI server exhibits in
///     practice (formatted vs non-formatted SOAP bodies both parse identically).</item>
///   <item><b>Comments and processing instructions ignored.</b> XML 1.0 allows these anywhere and
///     they carry no information for ECF 4.0 filings.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>NOT normalized (sibling order is significant)</b>: child element order is compared
/// position-by-position. ECF 4.0 and the JTI extension XSDs use <c>&lt;xs:sequence&gt;</c>,
/// so swapping siblings is a semantic change and must produce a diff. Callers that need
/// order-insensitive comparison for specific element types can preprocess their inputs.
/// </para>
///
/// <para>
/// <b>Intended use</b>: per <see cref="TierA_RoundTripTests"/>, this helper will pair with a
/// forthcoming <c>ReviewFilingRequestParser</c> (XML → FilingSubmission reverse-mapper) to
/// implement the full round-trip: parse baseline → rebuild via <c>ReviewFilingXmlBuilder</c> →
/// <see cref="Compare(string, string)"/> the two. The diff's <see cref="DiffResult.AreEquivalent"/>
/// flag becomes the scenario's pass/fail signal. <see cref="DiffResult.FormatDifferences"/> emits
/// a human-readable report that points directly at each mismatch by XPath-ish location.
/// </para>
///
/// <para>
/// This helper does NOT know about EFSP-generated variability (GUIDs, timestamps). The parser is
/// responsible for preserving source values for fields the builder would otherwise regenerate
/// (<c>EfspReferenceId</c>, <c>DocumentFiledDate</c>, <c>FileControlId</c>). If the parser cannot
/// preserve a field, the test harness must substitute it into the submission before rebuilding.
/// </para>
/// </summary>
public static class XmlStructuralDiff
{
    /// <summary>
    /// A single structural difference between two XML documents. <see cref="Path"/> is a
    /// slash-delimited path from document root to the element in question (prefixes stripped,
    /// only local names) — useful for both human reading and programmatic filtering.
    /// <see cref="Type"/> is a short category ("root-name", "attribute-missing",
    /// "attribute-value", "child-count", "text", "name-namespace", "name-local", "text-only").
    /// </summary>
    public sealed record Diff(string Path, string Type, string Expected, string Actual)
    {
        public override string ToString()
            => $"  [{Type}] at /{Path}\n    expected: {Expected}\n    actual:   {Actual}";
    }

    /// <summary>
    /// Result of a <see cref="XmlStructuralDiff.Compare(string, string)"/> call.
    /// <see cref="AreEquivalent"/> is <c>true</c> when <see cref="Differences"/> is empty.
    /// </summary>
    public sealed record DiffResult(bool AreEquivalent, IReadOnlyList<Diff> Differences)
    {
        /// <summary>
        /// Emit a human-readable multi-line report suitable for an xUnit assertion message.
        /// First line summarizes the count; each difference follows as a three-line stanza.
        /// </summary>
        public string FormatDifferences()
        {
            if (AreEquivalent) return "(no differences)";
            var lines = new List<string> { $"{Differences.Count} structural difference(s):" };
            lines.AddRange(Differences.Select(d => d.ToString()));
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Compare two XML document strings for structural equivalence. Either may be any
    /// well-formed XML — if parsing fails, the exception propagates (callers are expected
    /// to pass already-validated XML).
    /// </summary>
    public static DiffResult Compare(string expectedXml, string actualXml)
    {
        var expected = XDocument.Parse(expectedXml, LoadOptions.PreserveWhitespace);
        var actual = XDocument.Parse(actualXml, LoadOptions.PreserveWhitespace);
        return Compare(expected, actual);
    }

    /// <summary>
    /// Compare two parsed <see cref="XDocument"/>s for structural equivalence.
    /// </summary>
    public static DiffResult Compare(XDocument expected, XDocument actual)
    {
        var diffs = new List<Diff>();

        var expectedRoot = expected.Root;
        var actualRoot = actual.Root;

        if (expectedRoot is null && actualRoot is null)
            return new DiffResult(true, diffs);

        if (expectedRoot is null || actualRoot is null)
        {
            diffs.Add(new Diff(
                Path: "",
                Type: "document-empty",
                Expected: expectedRoot?.Name.ToString() ?? "(empty)",
                Actual: actualRoot?.Name.ToString() ?? "(empty)"));
            return new DiffResult(false, diffs);
        }

        CompareElement(expectedRoot, actualRoot, path: expectedRoot.Name.LocalName, diffs);
        return new DiffResult(diffs.Count == 0, diffs);
    }

    private static void CompareElement(XElement expected, XElement actual, string path, List<Diff> diffs)
    {
        // 1. Qualified name (namespace URI + local name). Prefixes ignored.
        if (expected.Name.NamespaceName != actual.Name.NamespaceName)
        {
            diffs.Add(new Diff(path, "name-namespace",
                expected.Name.NamespaceName, actual.Name.NamespaceName));
        }
        if (expected.Name.LocalName != actual.Name.LocalName)
        {
            diffs.Add(new Diff(path, "name-local",
                expected.Name.LocalName, actual.Name.LocalName));
        }

        // 2. Attributes — compared as sets by qualified name.
        CompareAttributes(expected, actual, path, diffs);

        // 3. Children. Elements are compared position-by-position (order-sensitive).
        //    Pure-whitespace text between elements is skipped. Text content is compared
        //    only when an element has NO element children (leaf semantics).
        CompareChildren(expected, actual, path, diffs);
    }

    private static void CompareAttributes(XElement expected, XElement actual, string path, List<Diff> diffs)
    {
        // Skip xmlns declarations — they're namespace scope mechanics, not information.
        // XDocument's Attributes() includes them as XAttribute with IsNamespaceDeclaration=true.
        var expectedAttrs = expected.Attributes()
            .Where(a => !a.IsNamespaceDeclaration)
            .ToDictionary(a => a.Name, a => a);
        var actualAttrs = actual.Attributes()
            .Where(a => !a.IsNamespaceDeclaration)
            .ToDictionary(a => a.Name, a => a);

        foreach (var (name, expectedAttr) in expectedAttrs)
        {
            if (!actualAttrs.TryGetValue(name, out var actualAttr))
            {
                diffs.Add(new Diff(path, "attribute-missing",
                    $"@{name.LocalName}='{expectedAttr.Value}'", "(not present)"));
                continue;
            }

            if (!AttributeValuesEquivalent(expectedAttr, actualAttr, expected, actual))
            {
                diffs.Add(new Diff(path, "attribute-value",
                    $"@{name.LocalName}='{expectedAttr.Value}'",
                    $"@{name.LocalName}='{actualAttr.Value}'"));
            }
        }

        foreach (var (name, actualAttr) in actualAttrs)
        {
            if (!expectedAttrs.ContainsKey(name))
            {
                diffs.Add(new Diff(path, "attribute-unexpected",
                    "(not present)",
                    $"@{name.LocalName}='{actualAttr.Value}'"));
            }
        }
    }

    /// <summary>
    /// Test whether two attribute values are semantically equivalent. For most attributes this
    /// is literal string equality. For <c>xsi:type</c> and other known QName-valued attributes,
    /// the values are parsed as <c>prefix:LocalName</c> and the prefixes are resolved against
    /// their respective elements' in-scope namespace declarations — two QName attribute values
    /// are equivalent if they bind to the same (namespace URI, local name) tuple regardless of
    /// the prefix used.
    ///
    /// <para>
    /// This matches how XML Schema Part 1 §3.15 describes QName types — the information content
    /// is the <c>{namespace URI} + local name</c> tuple, not the lexical prefix. Failing to
    /// normalize here is equivalent to a string-equality check on <c>{"ns1"}</c> vs <c>{"a"}</c>
    /// when both bind to the same namespace — they'd compare unequal despite representing the
    /// same schema type.
    /// </para>
    /// </summary>
    private static bool AttributeValuesEquivalent(
        XAttribute expected, XAttribute actual, XElement expectedParent, XElement actualParent)
    {
        // Fast path — literal equality covers ~all non-QName-typed attributes.
        if (expected.Value == actual.Value) return true;

        // QName-typed attributes where prefix-sensitivity matters. The canonical example is
        // xsi:type. XSD schemas also define xsi:schemaLocation as a list of (namespace, uri)
        // pairs but that appears at envelope level only and rarely varies between samples.
        if (IsQNameAttribute(expected.Name))
        {
            return ResolveQName(expected.Value, expectedParent) == ResolveQName(actual.Value, actualParent);
        }

        return false;
    }

    private static bool IsQNameAttribute(XName name)
    {
        // xsi:type — the most common QName-valued attribute in ECF 4.0 wire.
        return name.NamespaceName == "http://www.w3.org/2001/XMLSchema-instance"
            && name.LocalName == "type";
    }

    /// <summary>
    /// Resolve a QName string ("<c>prefix:LocalName</c>" or just "<c>LocalName</c>") against an
    /// element's in-scope namespace declarations and return a stable string representation of
    /// the form <c>{namespace-uri}LocalName</c>. When the prefix isn't declared, falls back to
    /// treating the value literally (which will cause a mismatch in the comparison — the right
    /// behavior when the input XML is malformed).
    /// </summary>
    private static string ResolveQName(string value, XElement scope)
    {
        var colonIdx = value.IndexOf(':');
        if (colonIdx < 0)
        {
            // Unprefixed — binds to the default namespace in scope.
            var defaultNs = scope.GetDefaultNamespace();
            return defaultNs == XNamespace.None ? value : $"{{{defaultNs.NamespaceName}}}{value}";
        }

        var prefix = value.Substring(0, colonIdx);
        var localName = value.Substring(colonIdx + 1);
        var ns = scope.GetNamespaceOfPrefix(prefix);
        return ns == null ? value : $"{{{ns.NamespaceName}}}{localName}";
    }

    private static void CompareChildren(XElement expected, XElement actual, string path, List<Diff> diffs)
    {
        var expectedChildren = expected.Elements().ToList();
        var actualChildren = actual.Elements().ToList();

        // Leaf case — neither has element children → compare text.
        if (expectedChildren.Count == 0 && actualChildren.Count == 0)
        {
            var expectedText = NormalizeText(expected.Value);
            var actualText = NormalizeText(actual.Value);
            if (expectedText != actualText)
            {
                diffs.Add(new Diff(path, "text",
                    expectedText, actualText));
            }
            return;
        }

        // If one side is a leaf and the other isn't, that's a structural shape mismatch.
        if (expectedChildren.Count == 0 || actualChildren.Count == 0)
        {
            diffs.Add(new Diff(path, "child-count",
                expectedChildren.Count.ToString(),
                actualChildren.Count.ToString()));
            return;
        }

        // Both have element children — count and sibling-order must match.
        if (expectedChildren.Count != actualChildren.Count)
        {
            diffs.Add(new Diff(path, "child-count",
                $"{expectedChildren.Count} children: [{string.Join(", ", expectedChildren.Select(e => e.Name.LocalName))}]",
                $"{actualChildren.Count} children: [{string.Join(", ", actualChildren.Select(e => e.Name.LocalName))}]"));
            return;
        }

        for (int i = 0; i < expectedChildren.Count; i++)
        {
            var expectedChild = expectedChildren[i];
            var actualChild = actualChildren[i];
            var childPath = $"{path}/{expectedChild.Name.LocalName}[{i}]";
            CompareElement(expectedChild, actualChild, childPath, diffs);
        }
    }

    /// <summary>
    /// Collapse internal whitespace runs to single spaces and trim leading/trailing whitespace.
    /// Preserves NON-whitespace content exactly (so leading/trailing spaces in legitimate
    /// text content — rare in ECF — are lost, but that matches what JTI's XML parsers do
    /// when the xs:string simple type has its default whitespace facet of "collapse").
    /// </summary>
    private static string NormalizeText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Fast path — most values have no whitespace issues.
        if (value.Length == value.Trim().Length && !value.Contains("  ") && !value.Contains('\t') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        // Collapse whitespace runs.
        var chars = new List<char>(value.Length);
        bool lastWasSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && chars.Count > 0) chars.Add(' ');
                lastWasSpace = true;
            }
            else
            {
                chars.Add(c);
                lastWasSpace = false;
            }
        }
        // Trim trailing space if any.
        if (chars.Count > 0 && chars[^1] == ' ') chars.RemoveAt(chars.Count - 1);
        return new string(chars.ToArray());
    }
}
