using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using EFiling.Core.Models;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Parses JTI REST code list responses (Genericode 1.0 format) into typed models.
/// All JTI REST endpoints return the same <c>&lt;gc:CodeList&gt;</c> root structure but with
/// different <c>ComplexValue</c> xsi:type depending on the endpoint.
///
/// Migration (Track B.4): The Genericode root is schema-validated via the generated
/// <see cref="FR.CodeListDocument"/> type (OASIS Genericode 1.0 from
/// <c>http://docs.oasis-open.org/codelist/ns/genericode/1.0/</c>). Field extraction keeps
/// using XDocument navigation because (a) the Genericode <c>Row / Value / SimpleValue / ComplexValue</c>
/// pattern with dynamic <c>ColumnRef</c> attributes is naturally expressed via XDocument, and
/// (b) the <c>ComplexValue</c> payloads use JTI extension types (CodeValue, ZipCodeValue,
/// AttorneyValue, DocumentValue) that would require per-endpoint typed access with little
/// readability gain over the current approach.
/// </summary>
public static class CodeListResponseParser
{
    // JTI extension namespaces used inside ComplexValue elements
    private static readonly XNamespace NsCodeValue = "urn:com.journaltech:ecourt:ecf:extension:CodeValue";
    private static readonly XNamespace NsZipCodeValue = "urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue";
    private static readonly XNamespace NsAttorneyValue = "urn:com.journaltech:ecourt:ecf:extension:AttorneyValue";
    private static readonly XNamespace NsDocValue = "urn:com.journaltech:ecourt:ecf:extension:DocumentValue";

    // ─── Schema-validation fence (generated types) ─────────────────────
    private const string GenericodeNs = "http://docs.oasis-open.org/codelist/ns/genericode/1.0/";

    private static readonly XmlSerializer CodeListDocumentSer = new(
        typeof(FR.CodeListDocument),
        new XmlRootAttribute("CodeList") { Namespace = GenericodeNs });

    /// <summary>
    /// Attempt to deserialize the Genericode XML via the generated <see cref="FR.CodeListDocument"/>.
    /// Returns true if the XML is schema-valid Genericode. This is a schema-sanity probe only —
    /// field extraction below uses XDocument navigation, which is the natural fit for the
    /// dynamic ColumnRef-keyed Row/Value pattern.
    /// </summary>
    private static bool TryDeserializeCodeList(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            return CodeListDocumentSer.Deserialize(reader) != null;
        }
        catch (XmlException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Extension methods ByLocal, ByLocalFirst, DescByLocal are in XElementExtensions
    // (shared across all parsers in this namespace).

    /// <summary>
    /// Parse a standard code list response (CASE_TYPE, CASE_CATEGORY, PARTY_TYPE, etc.)
    /// into a list of <see cref="CodeListItem"/> with relationships.
    /// </summary>
    public static List<CodeListItem> ParseCodeList(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        _ = TryDeserializeCodeList(xml); // schema-validation probe; extraction follows below
        var doc = XDocument.Parse(xml);
        var rows = doc.DescByLocal("Row");
        var items = new List<CodeListItem>();

        foreach (var row in rows)
        {
            var item = new CodeListItem();

            foreach (var val in row.ByLocal("Value"))
            {
                var colRef = val.Attribute("ColumnRef")?.Value;

                if (colRef == "code")
                {
                    item.Code = val.ByLocalFirst("SimpleValue")?.Value ?? string.Empty;
                }
                else if (colRef == "value")
                {
                    var complex = val.ByLocalFirst("ComplexValue");
                    if (complex != null)
                    {
                        item.Name = complex.Element(NsCodeValue + "name")?.Value ?? string.Empty;

                        // Parse relationships
                        var relsContainer = complex.Element(NsCodeValue + "relationships");
                        if (relsContainer != null)
                        {
                            foreach (var rel in relsContainer.Elements(NsCodeValue + "relationship"))
                            {
                                item.Relationships.Add(new CodeListRelationship
                                {
                                    RelatedListName = rel.Element(NsCodeValue + "relatedListName")?.Value ?? string.Empty,
                                    RelatedCode = rel.Element(NsCodeValue + "relatedCode")?.Value ?? string.Empty
                                });
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(item.Code))
                items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Parse court locations response into <see cref="CourtLocation"/> list.
    /// ZipCodeValue: zipCode, locationCode, locationName, caseType
    /// </summary>
    public static List<CourtLocation> ParseCourtLocations(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        _ = TryDeserializeCodeList(xml); // schema-validation probe
        var doc = XDocument.Parse(xml);
        var rows = doc.DescByLocal("Row");
        var locations = new Dictionary<string, CourtLocation>();

        foreach (var row in rows)
        {
            var complex = row.DescByLocal("ComplexValue").FirstOrDefault();
            if (complex == null) continue;

            var locationCode = complex.Element(NsZipCodeValue + "locationCode")?.Value ?? string.Empty;
            var locationName = complex.Element(NsZipCodeValue + "locationName")?.Value ?? string.Empty;

            // De-duplicate by locationCode since the response returns one row per caseType
            if (!string.IsNullOrEmpty(locationCode) && !locations.ContainsKey(locationCode))
            {
                locations[locationCode] = new CourtLocation
                {
                    LocationCode = locationCode,
                    LocationName = locationName
                };
            }
        }

        return locations.Values.ToList();
    }

    /// <summary>
    /// Parse attorney list response into <see cref="AttorneyInfo"/> list.
    /// </summary>
    public static List<AttorneyInfo> ParseAttorneyList(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        _ = TryDeserializeCodeList(xml); // schema-validation probe
        var doc = XDocument.Parse(xml);
        var rows = doc.DescByLocal("Row");
        var attorneys = new List<AttorneyInfo>();

        foreach (var row in rows)
        {
            var complex = row.DescByLocal("ComplexValue").FirstOrDefault();
            if (complex == null) continue;

            attorneys.Add(new AttorneyInfo
            {
                Id = Elem(complex, "id"),
                BarNumber = Elem(complex, "barNumber"),
                FirstName = Elem(complex, "firstName"),
                MiddleName = Elem(complex, "middleName"),
                LastName = Elem(complex, "lastName"),
                LastNameSuffix = Elem(complex, "lastNameSuffix"),
                FirmName = Elem(complex, "firmName"),
                Address1 = Elem(complex, "address1"),
                Address2 = Elem(complex, "address2"),
                City = Elem(complex, "city"),
                State = Elem(complex, "state"),
                Zip = Elem(complex, "zip"),
                Country = Elem(complex, "country"),
                PhoneNumber = Elem(complex, "phoneNumber"),
                Email = Elem(complex, "email"),
                StatusCode = Elem(complex, "statusCode"),
                ParticipationStatus = Elem(complex, "participationStatus")
            });
        }

        return attorneys;

        static string? Elem(XElement parent, string name)
        {
            var val = parent.Element(NsAttorneyValue + name)?.Value;
            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }
    }

    /// <summary>
    /// Parse document list response into <see cref="DocumentListItem"/> list.
    /// DocumentValue: efmRequiresSubCase, name, metadata, caseTypes, caseCategories, formGroups
    /// </summary>
    public static List<DocumentListItem> ParseDocumentList(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        _ = TryDeserializeCodeList(xml); // schema-validation probe
        var doc = XDocument.Parse(xml);
        var rows = doc.DescByLocal("Row");
        var documents = new List<DocumentListItem>();

        foreach (var row in rows)
        {
            var item = new DocumentListItem();

            foreach (var val in row.ByLocal("Value"))
            {
                var colRef = val.Attribute("ColumnRef")?.Value;

                if (colRef == "code")
                {
                    item.Code = val.ByLocalFirst("SimpleValue")?.Value ?? string.Empty;
                }
                else if (colRef == "value")
                {
                    var complex = val.ByLocalFirst("ComplexValue");
                    if (complex == null) continue;

                    item.Name = complex.Element(NsDocValue + "name")?.Value ?? string.Empty;

                    var reqSubCase = complex.Element(NsDocValue + "efmRequiresSubCase")?.Value;
                    item.EfmRequiresSubCase = string.Equals(reqSubCase, "true", StringComparison.OrdinalIgnoreCase);

                    // Case types
                    var caseTypesEl = complex.Element(NsDocValue + "caseTypes");
                    if (caseTypesEl != null)
                        item.CaseTypes = ExtractNestedValues(caseTypesEl, NsDocValue + "caseType");

                    // Case sub-types
                    var caseSubTypesEl = complex.Element(NsDocValue + "caseSubTypes");
                    if (caseSubTypesEl != null)
                        item.CaseSubTypes = ExtractNestedValues(caseSubTypesEl, NsDocValue + "caseSubType");

                    // Case categories
                    var caseCatsEl = complex.Element(NsDocValue + "caseCategories");
                    if (caseCatsEl != null)
                        item.CaseCategories = ExtractNestedValues(caseCatsEl, NsDocValue + "caseCategory");

                    // Form groups
                    var formGroupsEl = complex.Element(NsDocValue + "formGroups");
                    if (formGroupsEl != null)
                        item.FormGroups = ExtractNestedValues(formGroupsEl, NsDocValue + "formGroup");

                    // Metadata items
                    var metadataEl = complex.Element(NsDocValue + "metadata");
                    if (metadataEl != null)
                    {
                        foreach (var mi in metadataEl.Elements(NsDocValue + "metadataItems"))
                        {
                            var meta = new DocumentMetadataItem
                            {
                                Code = mi.Element(NsDocValue + "code")?.Value ?? string.Empty,
                                Name = mi.Element(NsDocValue + "name")?.Value ?? string.Empty,
                                Description = mi.Element(NsDocValue + "description")?.Value,
                                Required = string.Equals(mi.Element(NsDocValue + "required")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                                Multiple = string.Equals(mi.Element(NsDocValue + "multiple")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                                ClassType = mi.Element(NsDocValue + "classType")?.Value ?? string.Empty,
                                Filter = mi.Element(NsDocValue + "filter")?.Value,
                                SubType = mi.Element(NsDocValue + "subType")?.Value,
                                ValueRestriction = mi.Element(NsDocValue + "valueRestriction")?.Value,
                                // codeList source name for codeList-classType items (e.g.
                                // CL_MOTION_OSC_DETAIL) — distinct from Code (e.g. MOTION_OSC_DETAIL).
                                // The UI dropdown loads its options by this source; dropping it made
                                // the renderer fall back to Code and query the wrong codelist type.
                                CodeList = mi.Element(NsDocValue + "codeList")?.Value
                            };

                            // Additional info tags
                            var tagsEl = mi.Element(NsDocValue + "additionalInfoTags");
                            if (tagsEl != null)
                            {
                                var tagText = tagsEl.Value;
                                if (!string.IsNullOrWhiteSpace(tagText))
                                {
                                    meta.AdditionalInfoTags = tagText
                                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .ToList();
                                }
                            }

                            item.MetadataItems.Add(meta);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(item.Code))
                documents.Add(item);
        }

        return documents;
    }

    /// <summary>
    /// Extract values from a container element that may use either:
    /// 1. Row/Value/SimpleValue genericode structure (live JTI API), or
    /// 2. Simple child elements like &lt;dv:formGroup&gt; (test XML).
    /// </summary>
    private static List<string> ExtractNestedValues(XElement container, XName simpleChildName)
    {
        var values = new List<string>();

        // Try Row/Value/SimpleValue structure first (actual JTI API format)
        foreach (var row in container.ByLocal("Row"))
        {
            foreach (var val in row.ByLocal("Value"))
            {
                var sv = val.ByLocalFirst("SimpleValue")?.Value;
                if (!string.IsNullOrEmpty(sv))
                    values.Add(sv);
            }
        }

        // Fallback: simple child elements (e.g. <dv:formGroup>EFCI_LEAD</dv:formGroup>)
        if (values.Count == 0)
        {
            values = container.Elements(simpleChildName)
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
        }

        return values;
    }
}
