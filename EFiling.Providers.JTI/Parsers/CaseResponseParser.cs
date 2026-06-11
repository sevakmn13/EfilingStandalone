using System.Xml.Linq;
using System.Xml.Serialization;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;
using CR = EFiling.WsdlGenerated.CourtRecord;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Parses GetCase, GetCaseList, and GetDocument SOAP responses from the CourtRecord endpoint.
///
/// Migration (Track B.5): The response bodies are schema-validated via the generated
/// <see cref="CR.CaseResponseMessageType"/> / <see cref="CR.CaseListResponseMessageType"/>.
/// Field extraction keeps using XDocument navigation because (a) the case payload is deeply
/// polymorphic across six concrete types (CivilCase, CivilCaseExt, AppellateCase, DomesticCase,
/// CriminalCase, Case) and (b) the existing flexible-lookup logic already handles per-court
/// schema differences that typed access would need bespoke per-variant traversals to match.
/// </summary>
public static class CaseResponseParser
{
    static readonly XNamespace NsSoapEnv = SoapEnvelopeBuilder.NsSoapEnv;

    // ─── Generated-type serializers (cached) ───────────────────────
    private const string CaseResponseNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseResponseMessage-4.0";
    private const string CaseListResponseNs = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListResponseMessage-4.0";

    private static readonly XmlSerializer CaseResponseSer = new(
        typeof(CR.CaseResponseMessageType),
        new XmlRootAttribute("CaseResponseMessage") { Namespace = CaseResponseNs });
    private static readonly XmlSerializer CaseListResponseSer = new(
        typeof(CR.CaseListResponseMessageType),
        new XmlRootAttribute("CaseListResponseMessage") { Namespace = CaseListResponseNs });

    // Track B.6: Use shared SoapBodyDeserializer.TryDeserializeBodyChild instead of a
    // local copy. Previously this file carried a ~17-line duplicate of the same walk-to-body
    // deserialization logic also present in FilingResponseParser.
    private static T? TryDeserializeBodyChild<T>(string xml, string localName, XmlSerializer ser) where T : class
        => SoapBodyDeserializer.TryDeserializeBodyChild<T>(xml, localName, ser);

    /// <summary>
    /// Parse a GetCase (CaseResponseMessage) response from raw SOAP XML.
    /// Returns null if the response contains an error or no case data.
    /// </summary>
    public static CaseInfo? ParseCaseResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);

        // SOAP fault short-circuit
        if (doc.Descendants(NsSoapEnv + "Fault").Any())
            return null;

        // Schema-validate body via generated types. If successful, check for typed errors
        // first (Error is inherited from QueryResponseMessageType).
        var caseResponse = TryDeserializeBodyChild<CR.CaseResponseMessageType>(xml, "CaseResponseMessage", CaseResponseSer);
        if (caseResponse?.Error is { Length: > 0 })
        {
            var code = caseResponse.Error[0]?.ErrorCode?.Value;
            if (!string.IsNullOrEmpty(code) && code != "0")
                return null;
        }

        // Fallback: check via XDocument in case the typed deserializer didn't populate Error
        // (e.g., legacy/variant response shapes).
        var errors = doc.DescByLocal("Error").ToList();
        if (errors.Count > 0)
        {
            var errorCode = errors[0].ByLocalFirst("ErrorCode")?.Value;
            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0")
                return null;
        }

        // Find the concrete case element — polymorphic across CivilCase / CivilCaseExt /
        // AppellateCase / DomesticCase / CriminalCase / base Case.
        var caseEl = doc.DescByLocal("CivilCaseExt").FirstOrDefault()
                  ?? doc.DescByLocal("CivilCase").FirstOrDefault()
                  ?? doc.DescByLocal("AppellateCase").FirstOrDefault()
                  ?? doc.DescByLocal("DomesticCase").FirstOrDefault()
                  ?? doc.DescByLocal("CriminalCase").FirstOrDefault()
                  ?? doc.DescByLocal("Case").FirstOrDefault();

        if (caseEl == null)
            return null;

        return ParseCaseElement(caseEl, xml);
    }

    /// <summary>
    /// Parse a GetCaseList (CaseListResponseMessage) response from raw SOAP XML.
    /// Returns a list of CaseInfo objects.
    /// </summary>
    public static List<CaseInfo> ParseCaseListResponse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = XDocument.Parse(xml);
        var cases = new List<CaseInfo>();

        // SOAP fault → empty list
        if (doc.Descendants(NsSoapEnv + "Fault").Any())
            return cases;

        // Schema-validate body via generated types (result discarded — field extraction uses
        // XDocument navigation below). If schema fails entirely we still try extraction, since
        // older responses sometimes vary in envelope element name.
        _ = TryDeserializeBodyChild<CR.CaseListResponseMessageType>(xml, "CaseListResponseMessage", CaseListResponseSer);

        // Find all case elements in the response
        var caseElements = doc.DescByLocal("CivilCaseExt")
            .Concat(doc.DescByLocal("CivilCase"))
            .Concat(doc.DescByLocal("AppellateCase"))
            .Concat(doc.DescByLocal("DomesticCase"))
            .Concat(doc.DescByLocal("CriminalCase"));

        // Deduplicate by checking parent — CivilCaseExt can contain nested CivilCase
        var processed = new HashSet<XElement>();

        foreach (var el in caseElements)
        {
            // Skip if this element is a descendant of an already-processed element
            if (processed.Any(p => el.AncestorsAndSelf().Contains(p)))
                continue;

            processed.Add(el);
            var caseInfo = ParseCaseElement(el, null);
            if (caseInfo != null)
                cases.Add(caseInfo);
        }

        // If no typed cases found, try generic Case elements
        if (cases.Count == 0)
        {
            foreach (var el in doc.DescByLocal("Case"))
            {
                var caseInfo = ParseCaseElement(el, null);
                if (caseInfo != null)
                    cases.Add(caseInfo);
            }
        }

        return cases;
    }

    // ─── Internal Parsing ────────────────────────────────────────

    private static CaseInfo? ParseCaseElement(XElement caseEl, string? rawXml)
    {
        var info = new CaseInfo { RawXml = rawXml };

        // CaseTrackingID
        info.CaseTrackingId = caseEl.DescByLocal("CaseTrackingID").FirstOrDefault()?.Value;

        // CaseDocketID
        info.CaseDocketId = caseEl.DescByLocal("CaseDocketID").FirstOrDefault()?.Value;

        // CaseTitleText or CaseGeneralCategoryText
        info.CaseTitle = caseEl.DescByLocal("CaseTitleText").FirstOrDefault()?.Value
                      ?? caseEl.DescByLocal("CaseTitle").FirstOrDefault()?.Value;

        // CaseTypeText
        info.CaseTypeCode = caseEl.DescByLocal("CaseTypeText").FirstOrDefault()?.Value
                         ?? caseEl.ByLocalFirst("CaseTypeText")?.Value;

        // CaseCategoryText
        info.CaseCategoryCode = caseEl.DescByLocal("CaseCategoryText").FirstOrDefault()?.Value
                             ?? caseEl.ByLocalFirst("CaseCategoryText")?.Value;

        // Location
        info.LocationCode = caseEl.DescByLocal("LocationName").FirstOrDefault()?.Value
                         ?? caseEl.DescByLocal("ParentLocationCode").FirstOrDefault()?.Value;

        // Parse parties (CaseParticipant / CaseParticipantExt elements)
        ParseParties(caseEl, info);

        // Parse complaints
        ParseComplaints(caseEl, info);

        // Step #48 — parse existing judgments from CaseCourtEvent
        // elements with xsi:type="CourtEventJudgmentType". Powers the SF
        // judgment classType picker (catalog §3.19 promotion criteria item 1).
        ParseJudgments(caseEl, info);

        return info;
    }

    private static void ParseParties(XElement caseEl, CaseInfo info)
    {
        // Look for CaseParticipantExt (JTI extension) or CaseParticipant
        var participants = caseEl.DescByLocal("CaseParticipantExt")
            .Concat(caseEl.DescByLocal("CaseParticipant"));

        var seenIds = new HashSet<string>();

        foreach (var p in participants)
        {
            var party = new CaseParty();

            // JTI extension fields
            party.PrimaryId = p.ByLocalFirst("primaryId")?.Value;
            party.ReferenceId = p.ByLocalFirst("referenceId")?.Value;

            // Deduplicate by primaryId
            var dedupeKey = party.PrimaryId ?? party.ReferenceId ?? p.ToString().GetHashCode().ToString();
            if (!seenIds.Add(dedupeKey))
                continue;

            // Role from CaseParticipantRoleCode
            party.RoleCode = p.DescByLocal("CaseParticipantRoleCode").FirstOrDefault()?.Value ?? string.Empty;

            // Entity — person or organization
            var entityPerson = p.DescByLocal("EntityPerson").FirstOrDefault();
            var entityOrg = p.DescByLocal("EntityOrganization").FirstOrDefault();

            if (entityPerson != null)
            {
                party.IsOrganization = false;
                var personName = entityPerson.DescByLocal("PersonName").FirstOrDefault();
                if (personName != null)
                {
                    party.FirstName = personName.ByLocalFirst("PersonGivenName")?.Value;
                    party.MiddleName = personName.ByLocalFirst("PersonMiddleName")?.Value;
                    party.LastName = personName.ByLocalFirst("PersonSurName")?.Value;
                    party.NameSuffix = personName.ByLocalFirst("PersonNameSuffixText")?.Value;
                }

                // Bar number (for attorneys)
                party.BarNumber = entityPerson.DescByLocal("BarNumber")?.FirstOrDefault()?.Value
                               ?? entityPerson.DescByLocal("JuristBarMembershipID")?.FirstOrDefault()?.Value;
                if (party.BarNumber == null)
                {
                    // Look in PersonOtherIdentification for BarNumber category
                    var barId = entityPerson.DescByLocal("PersonOtherIdentification")
                        .FirstOrDefault(e => e.ByLocalFirst("IdentificationCategoryText")?.Value == "BarNumber");
                    party.BarNumber = barId?.ByLocalFirst("IdentificationID")?.Value;
                }
            }
            else if (entityOrg != null)
            {
                party.IsOrganization = true;
                party.OrganizationName = entityOrg.DescByLocal("OrganizationName").FirstOrDefault()?.Value;
            }

            info.Parties.Add(party);
        }
    }

    private static void ParseComplaints(XElement caseEl, CaseInfo info)
    {
        // JTI CivilCaseTypeExt has Complaint elements
        var complaints = caseEl.ByLocal("Complaint");

        foreach (var c in complaints)
        {
            var complaint = new CaseComplaint();

            // st:id attribute
            var stId = c.Attribute("id")?.Value
                    ?? c.Attribute(XNamespace.Get(SoapEnvelopeBuilder.NsStructures) + "id")?.Value;
            complaint.ComplaintId = stId;

            // CaseTitleText inside the complaint
            complaint.CaseTitle = c.DescByLocal("CaseTitleText").FirstOrDefault()?.Value;

            // CaseCategoryText inside the complaint
            complaint.CaseCategoryCode = c.DescByLocal("CaseCategoryText").FirstOrDefault()?.Value;

            info.Complaints.Add(complaint);
        }
    }

    /// <summary>
    /// Step #48 — extract judgments from a GetCase response.
    ///
    /// <para>
    /// <b>Wire shape</b> (per JTI HTML "Subsequent Filing - Court Specific
    /// Concepts" §LASC Post Judgment, lines 282-303):
    /// </para>
    /// <code>
    /// &lt;ns5:CaseCourtEvent ns1:id="Judgment" xsi:type="ns18:CourtEventJudgmentType"&gt;
    ///   &lt;ns18:judgmentId&gt;2562247&lt;/ns18:judgmentId&gt;
    ///   &lt;ns18:subCaseReferenceId&gt;4045592&lt;/ns18:subCaseReferenceId&gt;
    ///   &lt;ns18:judgmentTitle&gt;Judgment entered on 03/19/2020 for ...&lt;/ns18:judgmentTitle&gt;
    ///   &lt;ns18:JudgmentAward&gt;...&lt;/ns18:JudgmentAward&gt;
    /// &lt;/ns5:CaseCourtEvent&gt;
    /// </code>
    ///
    /// <para>
    /// <b>Detection strategy:</b> The discriminator could be the
    /// <c>xsi:type="ns18:CourtEventJudgmentType"</c> attribute, but that
    /// requires namespace-qualified attribute access and assumes the
    /// court's namespace prefix. Instead we use the structural
    /// discriminator <c>presence of a child &lt;judgmentId&gt; element</c>
    /// — only <see cref="EFiling.WsdlGenerated.CourtRecord.CourtEventJudgmentType"/>
    /// has this child per the WSDL (FilingReview/Reference.cs:22747 +
    /// CourtRecord/Reference.cs:22842). This matches the established
    /// local-name-based pattern used by <see cref="ParseParties"/> and
    /// <see cref="ParseComplaints"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Award sub-structure (intentionally ignored):</b> The
    /// <c>&lt;JudgmentAward&gt;</c> children carry party + amount detail
    /// not needed by the SF judgment picker — projecting only
    /// <c>(judgmentId, judgmentTitle, subCaseReferenceId)</c> keeps the
    /// CaseInfo DTO lean. Extend if future UX needs award detail.
    /// </para>
    /// </summary>
    private static void ParseJudgments(XElement caseEl, CaseInfo info)
    {
        var seenIds = new HashSet<string>();

        foreach (var ce in caseEl.DescByLocal("CaseCourtEvent"))
        {
            // Structural discriminator: only CourtEventJudgmentType has a
            // <judgmentId> direct child. Generic CourtEventType / CourtEventTypeExt
            // / BookingType / CourtOrderType / etc. do NOT.
            var judgmentIdEl = ce.ByLocalFirst("judgmentId");
            if (judgmentIdEl == null) continue;

            var judgmentId = judgmentIdEl.Value?.Trim();
            if (string.IsNullOrEmpty(judgmentId)) continue;

            // Deduplicate by judgmentId. The same judgment shouldn't appear
            // twice in a well-formed response, but a defensive guard here
            // prevents downstream issues (e.g., the UI rendering duplicate
            // <option> entries with the same value).
            if (!seenIds.Add(judgmentId)) continue;

            info.Judgments.Add(new CaseJudgment
            {
                JudgmentId = judgmentId,
                JudgmentTitle = ce.ByLocalFirst("judgmentTitle")?.Value?.Trim(),
                SubCaseReferenceId = ce.ByLocalFirst("subCaseReferenceId")?.Value?.Trim()
            });
        }
    }
}
