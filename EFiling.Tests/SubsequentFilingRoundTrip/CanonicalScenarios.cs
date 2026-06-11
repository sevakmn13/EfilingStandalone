namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Registry of the 48 canonical JTI baseline scenarios from the eFiling Baseline Courts sample set.
///
/// <para>
/// This is the source-of-truth fixture list for T-2 (round-trip harness) per the re-implementation
/// plan in <c>docs/EFILING_SUBSEQUENT_FILING_REIMPLEMENTATION_PLAN.md</c>. Every scenario ID here
/// maps 1:1 with a row in <c>docs/JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2 (T-1.C scenario catalog).
/// </para>
///
/// <para>
/// Scenario ID format: <c>[CAT]-[FT]-###</c> where CAT is case-category (CIV/FAM/PRO/MH),
/// FT is filing type (INI/SUB), and ### is zero-padded sequence. IDs are stable — renaming
/// or renumbering an ID breaks the test-fixture-to-catalog-row traceability.
/// </para>
///
/// <para>
/// Per-court variants (LASC, Alameda, Riverside) live under different subdirectories and are
/// catalogued separately in <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §6. They are not in this
/// registry because they mostly re-file the same semantic scenarios against a different
/// CaseType codeset. Adding them later is a question of expanding this list with new IDs
/// (e.g., <c>CIV-SUB-001-LASC</c>, <c>CIV-SUB-001-ALM</c>) pointing at their respective files.
/// </para>
/// </summary>
public static class CanonicalScenarios
{
    /// <summary>
    /// Relative path (from repo root) to the baseline sample directory.
    /// All <see cref="All"/> entries' <see cref="CanonicalScenario.RelativePath"/> is relative to this.
    /// </summary>
    public const string BaselineRoot =
        "docs/fileing files/ECF Operations/ReviewFiling/eFiling Baseline Courts";

    /// <summary>
    /// All 48 canonical baseline scenarios. Order matches <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.
    /// </summary>
    public static readonly IReadOnlyList<CanonicalScenario> All = new List<CanonicalScenario>
    {
        // ─── Civil & Small Claims — Case Initiation (13) ────────────────────
        new("CIV-INI-001", CaseCategory.Civil,        FilingType.Initiation, "New Case (Civil Limited) Sample",
            @"Civil & Small Claims\New Case (Civil Limited) Sample.xml"),
        new("CIV-INI-002", CaseCategory.Civil,        FilingType.Initiation, "New Case (Civil Limited w Motion) Sample",
            @"Civil & Small Claims\New Case (Civil Limited w Motion) Sample.xml"),
        new("CIV-INI-003", CaseCategory.Civil,        FilingType.Initiation, "New Case (Civil Unlimited - Personal Injury) Sample",
            @"Civil & Small Claims\New Case (Civil Unlimited - Personal Injury) Sample.xml"),
        new("CIV-INI-004", CaseCategory.Civil,        FilingType.Initiation, "New Case (Fee Waiver) Sample",
            @"Civil & Small Claims\New Case (Fee Waiver) Sample.xml"),
        new("CIV-INI-005", CaseCategory.Civil,        FilingType.Initiation, "New Case (Unlawful Detainer) Sample",
            @"Civil & Small Claims\New Case (Unlawful Detainer) Sample.xml"),
        new("CIV-INI-006", CaseCategory.Civil,        FilingType.Initiation, "New Case filed by Gov Ent Exempt party Sample",
            @"Civil & Small Claims\New Case filed by Gov Ent Exempt party Sample.xml"),
        new("CIV-INI-007", CaseCategory.Civil,        FilingType.Initiation, "New Case Petition (No Fee Case) Sample",
            @"Civil & Small Claims\New Case Petition (No Fee Case) Sample.xml"),
        new("CIV-INI-008", CaseCategory.Civil,        FilingType.Initiation, "New Case Petition (No Respondent), Self Rep Sample",
            @"Civil & Small Claims\New Case Petition (No Respondent), Self Rep Sample.xml"),
        new("CIV-INI-009", CaseCategory.Civil,        FilingType.Initiation, "New Case with Consent to eService by Filing Attorney Sample",
            @"Civil & Small Claims\New Case with Consent to eService by Filing Attorney Sample.xml"),
        new("CIV-INI-010", CaseCategory.Civil,        FilingType.Initiation, "New Case with Consent to eService Self Represented Sample",
            @"Civil & Small Claims\New Case with Consent to eService Self Represented Sample.xml"),
        new("CIV-INI-011", CaseCategory.Civil,        FilingType.Initiation, "New Case with Interpreter language requested, add existing attorney Sample",
            @"Civil & Small Claims\New Case with Interpreter language requested, add existing attorney Sample.xml"),
        new("CIV-INI-012", CaseCategory.Civil,        FilingType.Initiation, "New Case with multiple defendants respondents Sample",
            @"Civil & Small Claims\New Case with multiple defendants respondents Sample.xml"),
        new("CIV-INI-013", CaseCategory.Civil,        FilingType.Initiation, "New Case with multiple documents in the same transaction filed with Small Claims Jurisdictional Limit Sample",
            @"Civil & Small Claims\New Case with multiple documents in the same transaction filed with Small Claims Jurisdictional Limit Sample.xml"),

        // ─── Civil & Small Claims — Subsequent Filing (19) ──────────────────
        new("CIV-SUB-001", CaseCategory.Civil,        FilingType.Subsequent, "Amended Complaint Sample",
            @"Civil & Small Claims\Subsequent Filing\Amended Complaint Sample.xml"),
        new("CIV-SUB-002", CaseCategory.Civil,        FilingType.Subsequent, "Any first paper document submitted by Gov Entity Exempt party Sample",
            @"Civil & Small Claims\Subsequent Filing\Any first paper document submitted by Gov Entity Exempt party Sample.xml"),
        new("CIV-SUB-003", CaseCategory.Civil,        FilingType.Subsequent, "Any first paper document submitted with Fee Waiver request Sample",
            @"Civil & Small Claims\Subsequent Filing\Any first paper document submitted with Fee Waiver request Sample.xml"),
        new("CIV-SUB-004", CaseCategory.Civil,        FilingType.Subsequent, "Any first paper document using First Appearance self certification flag Sample",
            @"Civil & Small Claims\Subsequent Filing\Any first paper document using First Appearance self certification flag Sample.xml"),
        new("CIV-SUB-005", CaseCategory.Civil,        FilingType.Subsequent, "Any first paper document with new representation Sample",
            @"Civil & Small Claims\Subsequent Filing\Any first paper document with new representation Sample.xml"),
        new("CIV-SUB-006", CaseCategory.Civil,        FilingType.Subsequent, "Any first paper document without representation Sample",
            @"Civil & Small Claims\Subsequent Filing\Any first paper document without representation Sample.xml"),
        new("CIV-SUB-007", CaseCategory.Civil,        FilingType.Subsequent, "Association of Attorney Sample",
            @"Civil & Small Claims\Subsequent Filing\Association of Attorney Sample.xml"),
        new("CIV-SUB-008", CaseCategory.Civil,        FilingType.Subsequent, "Cross-Complaint Sample",
            @"Civil & Small Claims\Subsequent Filing\Cross-Complaint Sample.xml"),
        new("CIV-SUB-009", CaseCategory.Civil,        FilingType.Subsequent, "Filing on a Case with Multiple Sub-Cases Sample",
            @"Civil & Small Claims\Subsequent Filing\Filing on a Case with Multiple Sub-Cases Sample.xml"),
        new("CIV-SUB-010", CaseCategory.Civil,        FilingType.Subsequent, "First Paper filing from Gov Entity Exempt party in CMS Sample",
            @"Civil & Small Claims\Subsequent Filing\First Paper filing from Gov Entity Exempt party in CMS Sample.xml"),
        new("CIV-SUB-011", CaseCategory.Civil,        FilingType.Subsequent, "First Paper filing on No Fee Case Sample",
            @"Civil & Small Claims\Subsequent Filing\First Paper filing on No Fee Case Sample.xml"),
        new("CIV-SUB-012", CaseCategory.Civil,        FilingType.Subsequent, "First paper filing without representation - Consent to eService Sample",
            @"Civil & Small Claims\Subsequent Filing\First paper filing without representation - Consent to eService Sample.xml"),
        new("CIV-SUB-013", CaseCategory.Civil,        FilingType.Subsequent, "First paper filng with new representation - Consent to eService Sample",
            @"Civil & Small Claims\Subsequent Filing\First paper filng with new representation - Consent to eService Sample.xml"),
        new("CIV-SUB-014", CaseCategory.Civil,        FilingType.Subsequent, "Motion filing by attorney with eService consent Sample",
            @"Civil & Small Claims\Subsequent Filing\Motion filing by attorney with eService consent Sample.xml"),
        new("CIV-SUB-015", CaseCategory.Civil,        FilingType.Subsequent, "Notice of Appeal Sample",
            @"Civil & Small Claims\Subsequent Filing\Notice of Appeal Sample.xml"),
        new("CIV-SUB-016", CaseCategory.Civil,        FilingType.Subsequent, "Proof of Personal Service as to CCP 415.46 Sample",
            @"Civil & Small Claims\Subsequent Filing\Proof of Personal Service as to CCP 415.46 Sample.xml"),
        new("CIV-SUB-017", CaseCategory.Civil,        FilingType.Subsequent, "Proof of Personal Service Sample",
            @"Civil & Small Claims\Subsequent Filing\Proof of Personal Service Sample.xml"),
        new("CIV-SUB-018", CaseCategory.Civil,        FilingType.Subsequent, "Substitution of Attorney Inactivate attorney to be Self Rep Sample",
            @"Civil & Small Claims\Subsequent Filing\Substitution of Attorney Inactivate attorney to be Self Rep Sample.xml"),
        new("CIV-SUB-019", CaseCategory.Civil,        FilingType.Subsequent, "Substitution of Attorney Sample",
            @"Civil & Small Claims\Subsequent Filing\Substitution of Attorney Sample.xml"),

        // ─── Family Law — Case Initiation (4) ───────────────────────────────
        new("FAM-INI-001", CaseCategory.FamilyLaw,    FilingType.Initiation, "New Case (Dissolution) Sample",
            @"Family Law\New Case (Dissolution) Sample.xml"),
        new("FAM-INI-002", CaseCategory.FamilyLaw,    FilingType.Initiation, "New Case (DV Prevention with Child Support Request) Sample",
            @"Family Law\New Case (DV Prevention with Child Support Request) Sample.xml"),
        new("FAM-INI-003", CaseCategory.FamilyLaw,    FilingType.Initiation, "New DCSS Support Case with Govt. Exemption Sample",
            @"Family Law\New DCSS Support Case with Govt. Exemption Sample.xml"),
        new("FAM-INI-004", CaseCategory.FamilyLaw,    FilingType.Initiation, "New Family Case with Fee Waiver Sample",
            @"Family Law\New Family Case with Fee Waiver Sample.xml"),

        // ─── Family Law — Subsequent Filing (6) ─────────────────────────────
        new("FAM-SUB-001", CaseCategory.FamilyLaw,    FilingType.Subsequent, "Any first paper filing with representation Sample",
            @"Family Law\Subsequent Filing\Any first paper filing with representation Sample.xml"),
        new("FAM-SUB-002", CaseCategory.FamilyLaw,    FilingType.Subsequent, "Any first paper filing without representation Sample",
            @"Family Law\Subsequent Filing\Any first paper filing without representation Sample.xml"),
        new("FAM-SUB-003", CaseCategory.FamilyLaw,    FilingType.Subsequent, "First Paper - Response and motion using the Motion Type Metadata Element Sample",
            @"Family Law\Subsequent Filing\First Paper - Response and motion using the Motion Type Metadata Element Sample.xml"),
        new("FAM-SUB-004", CaseCategory.FamilyLaw,    FilingType.Subsequent, "First Paper - Response with request for order using custody or visitation flag Sample",
            @"Family Law\Subsequent Filing\First Paper - Response with request for order using custody or visitation flag Sample.xml"),
        new("FAM-SUB-005", CaseCategory.FamilyLaw,    FilingType.Subsequent, "Petition for dissolution of marriage on existing DV prevention case Sample",
            @"Family Law\Subsequent Filing\Petition for dissolution of marriage on existing DV prevention case Sample.xml"),
        new("FAM-SUB-006", CaseCategory.FamilyLaw,    FilingType.Subsequent, "Substitution of Attorney Sample",
            @"Family Law\Subsequent Filing\Substitution of Attorney Sample.xml"),

        // ─── Probate — Case Initiation (3) ──────────────────────────────────
        new("PRO-INI-001", CaseCategory.Probate,      FilingType.Initiation, "New Case (Conservatorship) Sample",
            @"Probate\Case Initiation\New Case (Conservatorship) Sample.xml"),
        new("PRO-INI-002", CaseCategory.Probate,      FilingType.Initiation, "New Case (Guardianship) Sample",
            @"Probate\Case Initiation\New Case (Guardianship) Sample.xml"),
        new("PRO-INI-003", CaseCategory.Probate,      FilingType.Initiation, "New Case (Trust) Sample",
            @"Probate\Case Initiation\New Case (Trust) Sample.xml"),

        // ─── Probate — Subsequent Filing (1) ────────────────────────────────
        new("PRO-SUB-001", CaseCategory.Probate,      FilingType.Subsequent, "Subsequent Objection filed by the respondent with New Representation Sample",
            @"Probate\Subsequent Filing\Subsequent Objection filed by the respondent with New Representation Sample.xml"),

        // ─── Mental Health — Case Initiation (2) ────────────────────────────
        new("MH-INI-001",  CaseCategory.MentalHealth, FilingType.Initiation, "New W&I 8103 Weapons Case Sample",
            @"Mental Health\New W&I 8103 Weapons Case Sample.xml"),
        new("MH-INI-002",  CaseCategory.MentalHealth, FilingType.Initiation, "New W&I LPS Conservatorship Case Sample",
            @"Mental Health\New W&I LPS Conservatorship Case Sample.xml"),
    }.AsReadOnly();

    /// <summary>
    /// xUnit <c>MemberData</c> source yielding <c>(scenarioId)</c> pairs for theory tests.
    /// Usage:
    /// <code>
    /// [Theory]
    /// [MemberData(nameof(CanonicalScenarios.AllScenarioIds), MemberType = typeof(CanonicalScenarios))]
    /// public void Test(string scenarioId) { ... }
    /// </code>
    /// </summary>
    public static IEnumerable<object[]> AllScenarioIds =>
        All.Select(s => new object[] { s.Id });

    /// <summary>Look up a scenario by its stable ID.</summary>
    public static CanonicalScenario GetById(string id)
    {
        var match = All.FirstOrDefault(s => s.Id == id);
        if (match is null)
            throw new ArgumentException(
                $"No canonical scenario found with ID '{id}'. Known IDs: {string.Join(", ", All.Select(s => s.Id))}",
                nameof(id));
        return match;
    }

    /// <summary>Scenarios filtered by case category.</summary>
    public static IEnumerable<CanonicalScenario> ByCategory(CaseCategory category) =>
        All.Where(s => s.Category == category);

    /// <summary>Scenarios filtered by filing type (Initiation / Subsequent).</summary>
    public static IEnumerable<CanonicalScenario> ByFilingType(FilingType filingType) =>
        All.Where(s => s.FilingType == filingType);
}

/// <summary>
/// A canonical JTI sample scenario catalogued in <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.
/// </summary>
/// <param name="Id">Stable ID in <c>[CAT]-[FT]-###</c> format (e.g., <c>CIV-SUB-007</c>).</param>
/// <param name="Category">Case category (Civil / FamilyLaw / Probate / MentalHealth).</param>
/// <param name="FilingType">Initiation or Subsequent filing.</param>
/// <param name="Description">Human-readable scenario description, matches catalog row.</param>
/// <param name="RelativePath">Path relative to <see cref="CanonicalScenarios.BaselineRoot"/>.</param>
public sealed record CanonicalScenario(
    string Id,
    CaseCategory Category,
    FilingType FilingType,
    string Description,
    string RelativePath);

/// <summary>CA court case categories used in canonical baseline samples.</summary>
public enum CaseCategory
{
    /// <summary>Civil (Limited, Unlimited, UD) and Small Claims — combined per baseline sample layout.</summary>
    Civil,
    FamilyLaw,
    Probate,
    MentalHealth,
    // Juvenile, Criminal, Appellate intentionally omitted — no baseline samples exist.
    // Tracked as open questions in JTI_SUBSEQUENT_FILING_CATALOG.md §7.2.4, §7.2.5, §7.2.
}

/// <summary>Filing type axis — does the filing create a new case or file into an existing case?</summary>
public enum FilingType
{
    /// <summary>Case Initiation — creates a new case docket in the court CMS.</summary>
    Initiation,
    /// <summary>Subsequent Filing — files documents into an existing case (by CaseTrackingID or CaseDocketID).</summary>
    Subsequent,
}
