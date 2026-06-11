namespace EFiling.Providers.JTI.Config;

// ════════════════════════════════════════════════════════════════════════════
// T-3a v2 schema models — rich slice-scoped types.
//
// These types mirror the three T-3a JSON files:
//   - JtiClassTypeSchema.json   → ClassTypeSchemaV2
//   - JtiTagSchema.json         → TagSchemaV2
//   - JtiCaseCategoryPolicy.json → CaseCategoryPolicySchemaV2
//
// Consumed by JtiFieldSchemaProvider. The provider exposes v2 types directly
// via new slice-accessor methods (GetClassTypeSchema / GetTagSchema /
// GetCaseCategoryPolicy) and ALSO projects a legacy FieldSchema shape from
// these v2 types for back-compat with the existing UI + controller.
//
// All properties on these models are optional/nullable to tolerate evidence-
// pending entries (V2/V3 classTypes; V3-hypothesis tags; pre-T-1.D categories).
// ════════════════════════════════════════════════════════════════════════════

#region ClassType Schema (slice 1)

public class ClassTypeSchemaV2
{
    public string Version { get; set; } = string.Empty;
    public string SchemaSlice { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CatalogRef { get; set; }
    public string? PlanRef { get; set; }
    public string? WsdlRef { get; set; }
    public Dictionary<string, string> EvidenceLevels { get; set; } = new();
    public Dictionary<string, ClassTypeDefinitionV2> ClassTypes { get; set; } = new();
}

public class ClassTypeDefinitionV2
{
    public string Description { get; set; } = string.Empty;
    public string? CatalogSection { get; set; }
    // T-4-A pre-implementation additions. The renderer dispatches on
    // `RenderHint` per T-4-A.1 rule 7; `VariantSelector` drives the filer's
    // variant choice per rule 3. Both are JSON-additive and ignored by the
    // legacy v1 projection.
    public string? RenderHint { get; set; }
    public VariantSelectorInfo? VariantSelector { get; set; }
    public bool AwaitingEvidence { get; set; }
    public EvidenceInfo? Evidence { get; set; }

    /// <summary>
    /// Whether this classType appears in the JTI canonical "Class Types" enumeration
    /// (the Document Metadata HTML doc's <c>&lt;h3&gt;Class Types&lt;/h3&gt;</c> section
    /// at <c>docs/fileing files/Document Metadata/Document Metadata _ EFM Documentation.html</c>).
    /// <para>
    /// Per the Step #46 audit, JTI documents exactly 10 canonical classTypes:
    /// <c>attorney</c>, <c>boolean</c>, <c>caseParticipant</c>, <c>contact</c>,
    /// <c>crsReceiptNumber</c>, <c>currency</c>, <c>date</c>, <c>judgment</c>, <c>text</c>,
    /// <c>codelist</c>. The other 9 schema entries (<c>caseassignment</c>, plus the 8
    /// Tier-3 stubs: <c>number</c>, <c>email</c>, <c>action</c>, <c>address</c>,
    /// <c>document</c>, <c>scheduledEvent</c>, <c>caseSpecialStatus</c>, <c>relatedCase</c>)
    /// are NOT in the JTI canonical list — they exist as WSDL wrapper fields but are
    /// not exposed as filer-controllable classTypes.
    /// </para>
    /// <para>
    /// <b>null</b> = unset (default, treated as canonical-listed for back-compat with the
    /// 10 implemented arms which don't carry the flag).<br/>
    /// <b>false</b> = explicitly marked Tier-3 (8 stubs, plus <c>caseassignment</c> if
    /// ever re-classified).<br/>
    /// <b>true</b> = reserved for future explicit marking of canonical entries.
    /// </para>
    /// </summary>
    public bool? JtiCanonicalListed { get; set; }

    public WsdlInfo? Wsdl { get; set; }
    public WireInfo? Wire { get; set; }
    public ValueRestrictionsInfo? ValueRestrictions { get; set; }
    public Dictionary<string, VariantDefinitionV2> Variants { get; set; } = new();
    public string? AdditionalInfoTagsNote { get; set; }
    public List<string>? KnownBugs { get; set; }
    public List<string>? OpenQuestions { get; set; }
    public string? PromotionCriteria { get; set; }
}

public class VariantSelectorInfo
{
    /// <summary>UX picker type — "radio" or "dropdown".</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Ordered list of variant keys (must align with sibling Variants dict keys).</summary>
    public List<string> Values { get; set; } = new();
    /// <summary>Default variant key — must appear in Values.</summary>
    public string Default { get; set; } = string.Empty;
    /// <summary>Filer-facing label shown above the picker.</summary>
    public string UxLabel { get; set; } = string.Empty;
}

public class VariantDefinitionV2
{
    public string? Description { get; set; }
    public string? AliasOf { get; set; }
    public string? FieldsNote { get; set; }
    // T-4-A pre-implementation additions. Documents WSDL fields that
    // the UI deliberately drops (UX-narrowing audit trail). Stub form is
    // `NotSurfacedWsdlFields = []` + `AuditNote` until first-evidence pass.
    public List<NotSurfacedWsdlField>? NotSurfacedWsdlFields { get; set; }
    public string? NotSurfacedWsdlFieldsSource { get; set; }
    public string? AuditNote { get; set; }
    public List<FieldDefinitionV2> Fields { get; set; } = new();
}

public class NotSurfacedWsdlField
{
    /// <summary>XPath-like reference to the WSDL/NIEM element (e.g. "nc:EntityPerson/nc:PersonName/nc:PersonNamePrefixText").</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Why the UI deliberately does not surface this field.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class FieldDefinitionV2
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Source { get; set; }
    public string? Default { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string? XmlPath { get; set; }
}

public class WsdlInfo
{
    public string Field { get; set; } = string.Empty;
    public string ClrType { get; set; } = string.Empty;
    public int? XsdOrder { get; set; }
    public int? ReferenceLine { get; set; }
    public string? ClrTypeNote { get; set; }
    public string? WireElementNote { get; set; }
}

public class WireInfo
{
    public string WrapperElement { get; set; } = string.Empty;
    public string? WrapperNamespacePrefix { get; set; }
    public string? WrapperNamespaceUri { get; set; }
    public string? ChildrenNamespacePrefix { get; set; }
    public string? ChildrenNamespaceUri { get; set; }
    public string? InnerChild { get; set; }
    public string? InnerChildNamespacePrefix { get; set; }
    public string? InnerChildNamespaceUri { get; set; }
    public string? NiemGrounding { get; set; }
}

public class ValueRestrictionsInfo
{
    public ValueRestrictionBranch? ExistingData { get; set; }
    public ValueRestrictionBranch? NewData { get; set; }
}

public class ValueRestrictionBranch
{
    public bool Supported { get; set; }
    public string? Wire { get; set; }
    public string? Notes { get; set; }
}

#endregion

#region Tag Schema (slice 2)

public class TagSchemaV2
{
    public string Version { get; set; } = string.Empty;
    public string SchemaSlice { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CatalogRef { get; set; }
    public string? PlanRef { get; set; }
    public string? WsdlRef { get; set; }
    public TagWireStructure? WireStructure { get; set; }
    public Dictionary<string, string> ValueSemanticKinds { get; set; } = new();
    public Dictionary<string, TagDefinitionV2> Tags { get; set; } = new();
    public TagCompletenessSummary? CompletenessSummary { get; set; }
}

public class TagWireStructure
{
    public string WrapperElement { get; set; } = string.Empty;
    public string? WrapperNamespacePrefix { get; set; }
    public string? WrapperNamespaceUri { get; set; }
    public Dictionary<string, string> Children { get; set; } = new();
    public string? Repeatability { get; set; }
    public List<string>? ScopeLocations { get; set; }
}

public class TagDefinitionV2
{
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CatalogSection { get; set; }
    public string TagValueKind { get; set; } = string.Empty;
    public string UiType { get; set; } = string.Empty;
    public bool AwaitingEvidence { get; set; }
    public EvidenceInfo? Evidence { get; set; }
    public string? Scope { get; set; }
    public List<string>? EnumValues { get; set; }
    public Dictionary<string, string>? EnumValueLabels { get; set; }
    public TagPairing? PairedWith { get; set; }
    public List<string>? SampleUsage { get; set; }
    public List<string>? AuditFindings { get; set; }
    public List<string>? OpenQuestions { get; set; }
    public List<string>? EvidencePaths { get; set; }
    public List<string>? RelatedClassTypes { get; set; }
    public string? FailClosedPolicy { get; set; }
}

public class TagPairing
{
    public string TagType { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
}

public class TagCompletenessSummary
{
    public int TotalTags { get; set; }
    public int EvidenceBacked { get; set; }
    public int UnverifiedHypothesis { get; set; }
    public List<string>? EvidenceBackedTags { get; set; }
    public List<string>? UnverifiedHypothesisTags { get; set; }
}

#endregion

#region Court Category Mappings (slice 4 — Step #54, 2026-05-28)

// JtiCourtCategoryMappings.json → CourtCategoryMappingSchemaV2
//
// Step #54 — closes KD-001 (Flat CASE_CATEGORY codelist namespace).
// Per-court mappings from court-specific CASE_CATEGORY codes (Madera numeric
// "407200", LASC alpha "UD", Placer numeric "421110", etc.) onto the canonical
// JCCC 3-letter policy entries in JtiCaseCategoryPolicy.json.
//
// Consumed by JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode(courtId, code).
// The pre-Step-#54 flat resolver `FindPolicyByCourtCategoryCode(code)` is removed
// in this step — see Step54_KD001ClosureTests for the closure drift-guard.

public class CourtCategoryMappingSchemaV2
{
    public string Version { get; set; } = string.Empty;
    public string SchemaSlice { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? LoggedStep { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? PolicyFileRef { get; set; }
    public string? DesignRationale { get; set; }
    public List<string>? AddingNewCourtChecklist { get; set; }
    public Dictionary<string, CourtCategoryMapping> Courts { get; set; } = new();
}

public class CourtCategoryMapping
{
    public string? Label { get; set; }
    public string? EcourtHost { get; set; }
    public string? CodelistSource { get; set; }
    public Dictionary<string, string> CategoryCodeToJccc { get; set; } = new();

    // Step #58 — per-court CASE_CATEGORY codelist code → human-readable
    // label projection. Used by `SearchCase.cshtml` (category dropdown + results table)
    // to render labels instead of bare numeric codes. Source: each court's published
    // codelist file (Madera: scripts/madera_case_category.txt). Distinct from
    // `CategoryCodeToJccc` because labels cover the FULL per-court codelist (180+
    // entries for Madera) whereas JCCC mappings cover only evidence-backed entries
    // (42 for Madera post-Step-#57). When a code is in CategoryCodeToJccc but missing
    // from CodelistLabels (or vice-versa), the resolver falls back gracefully (shows
    // raw code or no JCCC). Multi-court ready: each court declares its own label
    // dict per its codelist convention (numeric vs alpha).
    public Dictionary<string, string> CodelistLabels { get; set; } = new();
}

#endregion

#region Case Category Policy (slice 3)

public class CaseCategoryPolicySchemaV2
{
    public string Version { get; set; } = string.Empty;
    public string SchemaSlice { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CatalogRef { get; set; }
    public string? PlanRef { get; set; }
    public string? EcfGrounding { get; set; }
    public bool AwaitingEvidenceOverall { get; set; }
    public string? CompletenessNote { get; set; }
    public Dictionary<string, CaseCategoryPolicy> Policies { get; set; } = new();
    public PolicyCompletenessSummary? CompletenessSummary { get; set; }
}

public class CaseCategoryPolicy
{
    public string Label { get; set; } = string.Empty;
    public string? CatalogSection { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string? EcfElement { get; set; }
    public string? ParentCategory { get; set; }
    public bool AwaitingEvidence { get; set; }
    public EvidenceInfo? Evidence { get; set; }
    public List<string>? DefaultPartyRoles { get; set; }
    public List<CategoryRule>? Rules { get; set; }
    public List<string>? SubCategories { get; set; }
    /// <summary>
    /// Numeric per-court CASE_CATEGORY codelist codes that map to this JCCC
    /// 3-letter category. Bridges the Madera (and future JTI court) numeric
    /// codelist to the canonical JCCC structure used in this policy file.
    /// E.g., UD entry has KnownCategoryCodes = ["407200"] (Madera UD code).
    /// Step #42 — added to wire T-7 UX hooks (UD CCP §1161.2
    /// disclaimer, Family minor-child redaction warning) without per-court
    /// JS lookup ceremony. Court-agnostic flat list assumes JTI courts share
    /// the same numeric codelist for the same JCCC category, which is true
    /// for Madera+Placer per current evidence; if a future court diverges
    /// this can be promoted to a court-keyed dictionary.
    /// </summary>
    public List<string>? KnownCategoryCodes { get; set; }

    // ───────────────────────────────────────────────────────────────────
    // Source-grounded UX hook flags. Each must trace to a JTI EFM vendor
    // doc or WSDL element. Step #42-R removed the
    // unsourced `RequiresMinorChildRedaction` — see policy JSON
    // step42rRevisionNote for the audit trail.
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// EFSP must display the §1161.2 disclaimer when SEARCHING for UD cases.
    /// Source: JTI EFM vendor doc node/436#UnlawfulDetainer (verbatim text
    /// in policy JSON UD-1 sourceQuote). Wire footprint: NONE (EFSP-side
    /// UX obligation only).
    /// </summary>
    public bool? RequiresUdDisclaimer { get; set; }

    /// <summary>
    /// Juvenile case data is REDACTED SERVER-SIDE by JTI EFM (3-code
    /// CaseTypeText). EFSPs do NOT perform Juvenile redaction; they
    /// consume already-redacted data. This flag's semantic is "the EFSP UI
    /// should treat 'Confidential'/'REDACTED' field values as intentional".
    /// Source: JTI EFM vendor doc node/436#CriminalJuvenileCaseTypes
    /// (verbatim text in policy JSON JUV-1 sourceQuote).
    /// </summary>
    public bool? RequiresJuvenileRedaction { get; set; }

    /// <summary>
    /// DOB required for AS TO party on Mental Health case initiation.
    /// Source: JTI sample "Date of Birth Field Required for AS TO Party
    /// on Mental Health Case Initiation on ALL Case Categories Sample
    /// XML.xml" (policy JSON MEN-1).
    /// </summary>
    public bool? RequiresDobForAsToParty { get; set; }
    public bool? ExternalFilingBlocked { get; set; }
    public string? ExternalFilingBlockedReason { get; set; }
    public bool? BlockedFromTierB { get; set; }
    public string? BlockedReason { get; set; }
    public List<string>? OpenQuestions { get; set; }
}

public class CategoryRule
{
    public string Id { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? EvidenceLevel { get; set; }
    public string? UxHook { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Court-scope marker (lowercase court IDs). When present, this rule applies
    /// ONLY to the listed courts. When null/empty, the rule applies to all courts
    /// supporting the parent case category.
    ///
    /// <para>
    /// Step #47 — property added to deserialize the
    /// <c>appliesToCourts</c> JSON field which had been silently unbound. Used
    /// by per-court catalog §6 cross-reference tests. Examples in
    /// <c>JtiCaseCategoryPolicy.json</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item>UD-3: <c>["lasc"]</c> — COVID-19 question (UDCOV19 status code) on
    ///     new UD case initiation, LASC only.</item>
    ///   <item>UD-4: <c>["ventura"]</c> — Civil-Limited-only UD disclaimer.</item>
    /// </list>
    /// </summary>
    public List<string>? AppliesToCourts { get; set; }
}

public class PolicyCompletenessSummary
{
    public int TotalCategories { get; set; }
    public int EvidenceBacked { get; set; }
    public List<string>? EvidenceBackedCategories { get; set; }
    public int AwaitingEvidence { get; set; }
    public List<string>? AwaitingEvidenceCategories { get; set; }
    public ExternalFilingStatus? ExternalFilingStatus { get; set; }
}

public class ExternalFilingStatus
{
    public List<string>? Known { get; set; }
    public List<string>? Unknown { get; set; }
}

#endregion

#region Shared

public class EvidenceInfo
{
    public string Level { get; set; } = string.Empty;
    public int BaselineSampleCount { get; set; }
    public bool TierBLiveVerified { get; set; }
    public string? LayerAEvidence { get; set; }
}

#endregion
