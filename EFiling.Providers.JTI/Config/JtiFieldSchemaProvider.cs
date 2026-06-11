using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EFiling.Providers.JTI.Config;

/// <summary>
/// Provides JTI classType / tag / case-category-policy schema definitions.
///
/// T-3a split the single hand-maintained <c>JtiFieldSchema.json</c>
/// into three authoritative slice files:
/// <list type="bullet">
///   <item><c>JtiClassTypeSchema.json</c>  — 19 classTypes with WSDL grounding, wire shape, evidence level, field lists.</item>
///   <item><c>JtiTagSchema.json</c>         — 9 additionalInfoTags with tagValueKind, paired-with rules, evidence level.</item>
///   <item><c>JtiCaseCategoryPolicy.json</c> — 8 CA case categories with per-category UX rules.</item>
/// </list>
///
/// The provider exposes:
/// <list type="bullet">
///   <item>Rich v2 slice accessors: <see cref="GetClassTypeSchema"/>, <see cref="GetTagSchema"/>, <see cref="GetCaseCategoryPolicy"/>.</item>
///   <item>Legacy back-compat accessors: <see cref="GetSchema"/>, <see cref="GetClassType"/>, <see cref="GetAdditionalInfoTag"/>
///         — these project v2 data onto the pre-T-3a <see cref="FieldSchema"/> shape so existing UI and controller
///         consumers continue to work unchanged.</item>
/// </list>
///
/// Open-string contract: unknown classType / tagType values return <c>null</c> from the typed lookups.
/// Fail-closed behavior is the caller's responsibility (builder / parser MUST enforce it per catalog §3.0 observation #3).
/// </summary>
public static class JtiFieldSchemaProvider
{
    private const string ClassTypeSchemaResource = "EFiling.Providers.JTI.Config.JtiClassTypeSchema.json";
    private const string TagSchemaResource = "EFiling.Providers.JTI.Config.JtiTagSchema.json";
    private const string CaseCategoryPolicyResource = "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";
    // Step #54 — KD-001 Option B closure: per-court CASE_CATEGORY → JCCC mapping.
    private const string CourtCategoryMappingsResource = "EFiling.Providers.JTI.Config.JtiCourtCategoryMappings.json";

    private static readonly Lazy<ClassTypeSchemaV2> _classTypeSchema =
        new(() => LoadSliceResource<ClassTypeSchemaV2>(ClassTypeSchemaResource));

    private static readonly Lazy<TagSchemaV2> _tagSchema =
        new(() => LoadSliceResource<TagSchemaV2>(TagSchemaResource));

    // Step #54: the mapping file MUST load before the policy schema's KnownCategoryCodes
    // projection runs (the projection reads court mappings to populate the legacy field).
    // Declared with a forward dependency on _courtCategoryMappings.
    private static readonly Lazy<CourtCategoryMappingSchemaV2> _courtCategoryMappings =
        new(() => LoadSliceResource<CourtCategoryMappingSchemaV2>(CourtCategoryMappingsResource));

    private static readonly Lazy<CaseCategoryPolicySchemaV2> _caseCategoryPolicy =
        new(() =>
        {
            var schema = LoadSliceResource<CaseCategoryPolicySchemaV2>(CaseCategoryPolicyResource);
            ProjectKnownCategoryCodesFromMappings(schema);
            return schema;
        });

    private static readonly Lazy<FieldSchema> _legacyProjection =
        new(BuildLegacyProjection);

    // ───────────────────────────────────────────────────────────────────
    // Legacy back-compat API (pre-T-3a callers continue to work unchanged)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the legacy merged field schema (classTypes + additionalInfoTags), projected from
    /// the v2 slice files. Preserves the pre-T-3a return shape for existing callers.
    /// </summary>
    public static FieldSchema GetSchema() => _legacyProjection.Value;

    /// <summary>
    /// Gets field definitions for a specific classType in the legacy shape.
    /// Returns <c>null</c> for unknown classTypes.
    /// </summary>
    public static ClassTypeDefinition? GetClassType(string classType)
    {
        if (string.IsNullOrEmpty(classType))
            return null;

        _legacyProjection.Value.ClassTypes.TryGetValue(classType.ToLowerInvariant(), out var definition);
        return definition;
    }

    /// <summary>
    /// Gets an additional-info-tag definition in the legacy shape.
    /// Returns <c>null</c> for unknown tagTypes.
    /// </summary>
    public static AdditionalInfoTagDefinition? GetAdditionalInfoTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        _legacyProjection.Value.AdditionalInfoTags.TryGetValue(tag.ToUpperInvariant(), out var definition);
        return definition;
    }

    // ───────────────────────────────────────────────────────────────────
    // v2 slice accessors (T-3a)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the full v2 classType schema (all 19 classTypes, WSDL-grounded, evidence-tagged).
    /// </summary>
    public static ClassTypeSchemaV2 GetClassTypeSchema() => _classTypeSchema.Value;

    /// <summary>
    /// Gets the v2 tag schema (9 tags: 4 evidence-backed + 5 unverified-hypothesis).
    /// </summary>
    public static TagSchemaV2 GetTagSchema() => _tagSchema.Value;

    /// <summary>
    /// Gets the v2 case-category policy schema (8 CA case categories).
    /// </summary>
    public static CaseCategoryPolicySchemaV2 GetCaseCategoryPolicy() => _caseCategoryPolicy.Value;

    /// <summary>
    /// Step #54 — KD-001 Option B closure. Gets the per-court CASE_CATEGORY
    /// codelist projections onto JCCC 3-letter policy entries. Each court's
    /// <c>CategoryCodeToJccc</c> dictionary maps that court's per-court codelist
    /// values (e.g., Madera numeric "407200", LASC alpha "UD") to canonical JCCC
    /// policies in <see cref="GetCaseCategoryPolicy()"/>.
    /// </summary>
    public static CourtCategoryMappingSchemaV2 GetCourtCategoryMappings() => _courtCategoryMappings.Value;

    /// <summary>
    /// Gets the rich v2 definition for a specific classType. Returns <c>null</c> for unknowns.
    /// Prefer this over <see cref="GetClassType"/> for new callers that need WSDL / wire / evidence data.
    /// </summary>
    public static ClassTypeDefinitionV2? GetClassTypeV2(string classType)
    {
        if (string.IsNullOrEmpty(classType))
            return null;

        // The JSON keys are lowerCamelCase. Do case-insensitive lookup.
        foreach (var (key, value) in _classTypeSchema.Value.ClassTypes)
        {
            if (string.Equals(key, classType, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Gets the rich v2 definition for a specific tagType. Returns <c>null</c> for unknowns.
    /// </summary>
    public static TagDefinitionV2? GetTagV2(string tagType)
    {
        if (string.IsNullOrEmpty(tagType))
            return null;

        foreach (var (key, value) in _tagSchema.Value.Tags)
        {
            if (string.Equals(key, tagType, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Gets the policy for a specific case category code (e.g., "CIV", "FAM"). Returns <c>null</c> for unknowns.
    /// </summary>
    public static CaseCategoryPolicy? GetCaseCategoryPolicy(string categoryCode)
    {
        if (string.IsNullOrEmpty(categoryCode))
            return null;

        foreach (var (key, value) in _caseCategoryPolicy.Value.Policies)
        {
            if (string.Equals(key, categoryCode, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Resolves a per-court CASE_CATEGORY codelist code to the matching JCCC
    /// 3-letter policy entry. Returns the policy whose <c>CategoryCode</c> is
    /// referenced by the (<paramref name="courtId"/>, <paramref name="courtCategoryCode"/>)
    /// mapping in <see cref="GetCourtCategoryMappings"/>, or <c>null</c> if no
    /// court mapping or policy declares the code.
    ///
    /// <para>
    /// Step #42 — original courtId-less implementation added for
    /// T-7 SF.cshtml UX-hook wiring (UD CCP §1161.2 disclaimer). Step #54
    /// — KD-001 Option B closure: signature gained the
    /// <paramref name="courtId"/> parameter, and the flat namespace lookup was
    /// replaced by a court-scoped projection through <see cref="_courtCategoryMappings"/>.
    /// Pre-Step-#54 callers must thread <c>courtId</c> through their call chain;
    /// see the Step #54 migration notes in <c>JtiCaseCategoryPolicy.json</c>.
    /// </para>
    /// </summary>
    /// <param name="courtId">Lowercase court identifier (e.g., <c>"madera"</c>, <c>"lasc"</c>). Must match a key in <see cref="CourtCategoryMappingSchemaV2.Courts"/>.</param>
    /// <param name="courtCategoryCode">Per-court CASE_CATEGORY codelist value (e.g., Madera <c>"407200"</c>, LASC <c>"UD"</c>).</param>
    public static CaseCategoryPolicy? FindPolicyByCourtCategoryCode(string courtId, string courtCategoryCode)
    {
        if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(courtCategoryCode))
            return null;

        // Court ID: case-insensitive (courts use lowercase by convention but tolerate
        // mixed-case input from view-layer ViewBag / model binding).
        var court = _courtCategoryMappings.Value.Courts
            .FirstOrDefault(c => string.Equals(c.Key, courtId, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (court == null)
            return null;

        // Code: case-insensitive lookup (covers both numeric Madera/Placer codes
        // and any future alpha LASC codes).
        var jcccKey = court.CategoryCodeToJccc
            .FirstOrDefault(kv => string.Equals(kv.Key, courtCategoryCode, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (string.IsNullOrEmpty(jcccKey))
            return null;

        // JCCC key → policy: case-insensitive (3-letter codes are uppercase by convention).
        return _caseCategoryPolicy.Value.Policies
            .FirstOrDefault(p => string.Equals(p.Key, jcccKey, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    /// <summary>
    /// Step #54 helper. Returns the list of per-court CASE_CATEGORY
    /// codelist values for the given <paramref name="courtId"/> that map to the
    /// given <paramref name="jcccCategory"/> JCCC 3-letter policy. Returns an
    /// empty list if the court has no mapping or no codes map to that category.
    ///
    /// <para>
    /// Used by Step #49/#52/#53 drift-guard tests in place of the pre-Step-#54
    /// <c>policy.KnownCategoryCodes</c> field (which is now a projection from
    /// the mapping file, see <see cref="ProjectKnownCategoryCodesFromMappings"/>).
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> GetKnownCategoryCodesForCourt(string courtId, string jcccCategory)
    {
        if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(jcccCategory))
            return Array.Empty<string>();

        var court = _courtCategoryMappings.Value.Courts
            .FirstOrDefault(c => string.Equals(c.Key, courtId, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (court == null)
            return Array.Empty<string>();

        return court.CategoryCodeToJccc
            .Where(kv => string.Equals(kv.Value, jcccCategory, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Step #58 — resolve a per-court CASE_CATEGORY codelist code to its
    /// human-readable label (e.g., Madera <c>"411100"</c> → <c>"Civil Comp: RICO (27)"</c>).
    /// Returns <c>null</c> when the court or code is unknown so callers can choose
    /// their display fallback (typically: render the bare code).
    ///
    /// <para>
    /// Source: each court's <c>CourtCategoryMapping.CodelistLabels</c> in
    /// <c>JtiCourtCategoryMappings.json</c>. The same code MAY render different
    /// labels per court (because each court's codelist convention is independent —
    /// see KD-001 closure rationale).
    /// </para>
    ///
    /// <para>
    /// Used by <c>SearchCase.cshtml</c> (category dropdown + results table) and by
    /// the <c>EFilingMvcController.BuildCategoryOptionsByCourt</c> helper that
    /// surfaces dropdown options.
    /// </para>
    /// </summary>
    /// <param name="courtId">Lowercase court identifier (e.g., <c>"madera"</c>). Case-insensitive.</param>
    /// <param name="courtCategoryCode">Per-court CASE_CATEGORY codelist value (e.g., <c>"411100"</c>).</param>
    /// <returns>The human-readable label, or <c>null</c> when no label is registered.</returns>
    public static string? GetCategoryLabel(string courtId, string courtCategoryCode)
    {
        if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(courtCategoryCode))
            return null;

        var court = _courtCategoryMappings.Value.Courts
            .FirstOrDefault(c => string.Equals(c.Key, courtId, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (court == null)
            return null;

        return court.CodelistLabels.TryGetValue(courtCategoryCode, out var label) ? label : null;
    }

    // ───────────────────────────────────────────────────────────────────
    // Loading + legacy projection
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Step #54 — populate each <see cref="CaseCategoryPolicy.KnownCategoryCodes"/>
    /// by projecting the per-court mappings in <see cref="JtiCourtCategoryMappings.json"/>
    /// onto the JCCC policy keys.
    ///
    /// <para>
    /// Pre-Step-#54 the JSON declared <c>knownCategoryCodes</c> arrays directly on
    /// each policy, which silently assumed a flat per-court CASE_CATEGORY namespace
    /// (KD-001). Step #54 moved the codes to <c>JtiCourtCategoryMappings.json</c>
    /// with explicit court scoping; the legacy <c>KnownCategoryCodes</c> field
    /// remains on the model as the UNION of all courts' codes that map to that
    /// policy. Existing pre-Step-#54 callers (drift-guard tests, <c>policy.KnownCategoryCodes</c>
    /// reads) continue to work; new callers should prefer the explicit
    /// court-scoped <see cref="GetKnownCategoryCodesForCourt"/> helper.
    /// </para>
    /// </summary>
    private static void ProjectKnownCategoryCodesFromMappings(CaseCategoryPolicySchemaV2 schema)
    {
        var mappings = _courtCategoryMappings.Value;
        foreach (var (jcccKey, policy) in schema.Policies)
        {
            var codes = new List<string>();
            foreach (var (_, courtMap) in mappings.Courts)
            {
                foreach (var (code, mappedJccc) in courtMap.CategoryCodeToJccc)
                {
                    if (string.Equals(mappedJccc, jcccKey, StringComparison.OrdinalIgnoreCase))
                        codes.Add(code);
                }
            }
            // Preserve declaration order: keep null when no codes (matches pre-Step-#54
            // behaviour for policies without knownCategoryCodes — JUV, CRI, APP).
            policy.KnownCategoryCodes = codes.Count > 0 ? codes : null;
        }
    }

    private static T LoadSliceResource<T>(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} not found.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new InvalidOperationException($"Failed to deserialize {resourceName}.");
    }

    /// <summary>
    /// Projects the v2 slice data onto the legacy <see cref="FieldSchema"/> shape used by
    /// pre-T-3a callers (controller API + UI form rendering). Preserves wire compatibility.
    /// </summary>
    private static FieldSchema BuildLegacyProjection()
    {
        var v2Classes = _classTypeSchema.Value;
        var v2Tags = _tagSchema.Value;

        var legacy = new FieldSchema
        {
            Version = v2Classes.Version,
            Provider = v2Classes.Provider,
            Description = "Legacy merged view projected from T-3a v2 slices (JtiClassTypeSchema.json + JtiTagSchema.json). For new code, prefer the slice-specific accessors.",
            ClassTypes = new Dictionary<string, ClassTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            AdditionalInfoTags = new Dictionary<string, AdditionalInfoTagDefinition>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var (ctName, v2Def) in v2Classes.ClassTypes)
        {
            var legacyDef = new ClassTypeDefinition
            {
                Description = v2Def.Description,
                Variants = new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var (variantName, v2Variant) in v2Def.Variants)
            {
                legacyDef.Variants[variantName] = new VariantDefinition
                {
                    Fields = v2Variant.Fields.Select(f => new FieldDefinition
                    {
                        Name = f.Name,
                        Label = f.Label,
                        Type = f.Type,
                        Required = f.Required,
                        Source = f.Source,
                        Default = f.Default,
                        MaxLength = f.MaxLength,
                        Min = f.Min,
                        Max = f.Max
                    }).ToList()
                };
            }

            // Index by lower-invariant to match legacy GetClassType casing.
            legacy.ClassTypes[ctName.ToLowerInvariant()] = legacyDef;
        }

        foreach (var (tagName, v2Tag) in v2Tags.Tags)
        {
            legacy.AdditionalInfoTags[tagName.ToUpperInvariant()] = new AdditionalInfoTagDefinition
            {
                Label = v2Tag.Label,
                Description = v2Tag.Description,
                Type = v2Tag.UiType
            };
        }

        return legacy;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Legacy schema models (pre-T-3a shape; retained for back-compat with
// existing controller + UI consumers). New code should prefer the v2 models
// in JtiSchemaModelsV2.cs.
// ═══════════════════════════════════════════════════════════════════════════

#region Legacy Schema Models

public class FieldSchema
{
    public string Version { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, ClassTypeDefinition> ClassTypes { get; set; } = new();
    public Dictionary<string, AdditionalInfoTagDefinition> AdditionalInfoTags { get; set; } = new();
}

public class ClassTypeDefinition
{
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, VariantDefinition> Variants { get; set; } = new();
}

public class VariantDefinition
{
    public List<FieldDefinition> Fields { get; set; } = new();
}

public class FieldDefinition
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
}

public class AdditionalInfoTagDefinition
{
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

#endregion
