namespace EFiling.Core.Models;

/// <summary>
/// Parsed case information returned by GetCase / GetCaseList.
/// </summary>
public class CaseInfo
{
    /// <summary>Court-assigned case tracking ID (internal CMS integer).</summary>
    public string? CaseTrackingId { get; set; }

    /// <summary>Public case docket number (e.g., "21STCV12345").</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Case title / caption.</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Case type code from court policy.</summary>
    public string? CaseTypeCode { get; set; }

    /// <summary>Case category code from court policy.</summary>
    public string? CaseCategoryCode { get; set; }

    /// <summary>Parties on the case.</summary>
    public List<CaseParty> Parties { get; set; } = new();

    /// <summary>Complaints / sub-cases within the case.</summary>
    public List<CaseComplaint> Complaints { get; set; } = new();

    /// <summary>
    /// Existing judgments on the case (projected from GetCase response
    /// <c>&lt;CaseCourtEvent xsi:type="CourtEventJudgmentType"&gt;</c> elements).
    /// Populated by <c>CaseResponseParser.ParseJudgments</c>; consumed by
    /// the Subsequent Filing UI's judgment classType picker (Step #48,
    /// 2026-05-21) so users can reference existing judgments via the
    /// <c>&lt;ns9:judgments&gt;&lt;ns10:judgmentId&gt;</c> wire shape on
    /// post-judgment documents like Writ of Return (LASC).
    ///
    /// <para>
    /// Empty list = case has no judgments OR the court hasn't returned any
    /// in this GetCase response. The UI shows a "No judgments found on
    /// this case" empty-state when the dispatcher renders the judgment
    /// metadata field with an empty source list.
    /// </para>
    /// </summary>
    public List<CaseJudgment> Judgments { get; set; } = new();

    /// <summary>Court location code.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Raw XML for debugging.</summary>
    public string? RawXml { get; set; }
}

/// <summary>
/// A party on a case.
/// </summary>
public class CaseParty
{
    /// <summary>Party's internal reference ID (e.g., "filedBy0").</summary>
    public string? ReferenceId { get; set; }

    /// <summary>Party's primary ID from the court system.</summary>
    public string? PrimaryId { get; set; }

    /// <summary>Party role code (e.g., "PLAIN", "DEF", "ATT").</summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary>True if the party is an organization, false if a person.</summary>
    public bool IsOrganization { get; set; }

    /// <summary>Person's first name (if person).</summary>
    public string? FirstName { get; set; }

    /// <summary>Person's middle name (if person).</summary>
    public string? MiddleName { get; set; }

    /// <summary>Person's last name (if person).</summary>
    public string? LastName { get; set; }

    /// <summary>Person's name suffix (if person).</summary>
    public string? NameSuffix { get; set; }

    /// <summary>Organization name (if organization).</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Attorney bar number (if role is ATT).</summary>
    public string? BarNumber { get; set; }
}

/// <summary>
/// A complaint / sub-case within a case (CivilCaseTypeExt.Complaint).
/// </summary>
public class CaseComplaint
{
    /// <summary>Complaint reference ID (e.g., "1", "2").</summary>
    public string? ComplaintId { get; set; }

    /// <summary>Case title / caption for this complaint.</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Case category code for this complaint.</summary>
    public string? CaseCategoryCode { get; set; }
}

/// <summary>
/// A judgment entry on a case, projected from a GetCase response
/// <c>&lt;CaseCourtEvent xsi:type="CourtEventJudgmentType"&gt;</c> element.
///
/// <para>
/// Wire-shape evidence: JTI "Subsequent Filing - Court Specific Concepts"
/// HTML doc lines 282-303 (LASC Post Judgment section) showing
/// <c>&lt;ns18:judgmentId&gt;</c>, <c>&lt;ns18:subCaseReferenceId&gt;</c>,
/// <c>&lt;ns18:judgmentTitle&gt;</c> children. Companion to the SF
/// submission existing-data wire shape
/// <c>&lt;ns9:judgments&gt;&lt;ns10:judgmentId&gt;{id}&lt;/ns10:judgmentId&gt;&lt;/ns9:judgments&gt;</c>
/// (catalog §3.19) which references the <c>JudgmentId</c> below.
/// </para>
///
/// <para>
/// JudgmentAward sub-structure (award party IDs + amounts) is intentionally
/// NOT projected onto this DTO because the SF judgment picker only needs
/// <c>(id, title)</c> for selection display. If future use cases need the
/// award detail, extend this POCO at that point.
/// </para>
/// </summary>
public class CaseJudgment
{
    /// <summary>
    /// Judgment ID from the court CMS (e.g., "2562247"). This is the
    /// value emitted as <c>&lt;ns10:judgmentId&gt;</c> when the user
    /// selects this judgment on a Writ of Return or similar
    /// post-judgment SF document.
    /// </summary>
    public string? JudgmentId { get; set; }

    /// <summary>
    /// Human-readable judgment description (e.g., "Judgment entered on
    /// 03/19/2020 for Plaintiff X against Defendant Y for $1,500.00").
    /// Used as the display text in the SF judgment picker dropdown.
    /// </summary>
    public string? JudgmentTitle { get; set; }

    /// <summary>
    /// Sub-case reference ID (e.g., "4045592") — typically the
    /// complaint ID the judgment relates to. Optional, included for
    /// future UX that wants to scope judgments to a specific complaint
    /// on multi-complaint cases.
    /// </summary>
    public string? SubCaseReferenceId { get; set; }
}
