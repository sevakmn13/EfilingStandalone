using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EFiling.Nop.Mapping;

/// <summary>
/// Single source of truth for the JSON serializer used by the T-4 schema slice
/// endpoints (<c>/CourtFiling/api/efiling/field-schema?slice=…</c>). Configured to:
/// <list type="bullet">
/// <item><description>CamelCase POCO property names (so JS consumers can use the
/// camelCase access pattern that matches the JSON source files).</description></item>
/// <item><description><b>Preserve dictionary keys verbatim</b> — tag codes and
/// other map keys are wire identifiers (NIEM-aligned) that must round-trip
/// without transformation.</description></item>
/// </list>
///
/// <para>
/// <b>Step #13.1 root-cause fix:</b> the previous resolver,
/// <c>CamelCasePropertyNamesContractResolver</c>, sets
/// <c>ProcessDictionaryKeys = true</c> by default. Newtonsoft's acronym-aware
/// camelCase converter then mangled all-caps keys like <c>FEE_EXEMPTION</c> into
/// <c>feE_EXEMPTION</c> (lowercasing all leading uppercase letters until the one
/// immediately before a separator). This silently corrupted tag codes on the
/// wire and caused every JS / server consumer that did equality checks against
/// the canonical codes to fail. The regression went undetected because the
/// downstream <c>MetadataValueMapper.DetermineTagValue</c> fall-back logic
/// emits "1" for unknown tagTypes, so the wire kept producing <i>something</i>
/// rather than throwing.
/// </para>
/// </summary>
public static class SchemaSliceJsonSerializer
{
    /// <summary>
    /// Serializes <paramref name="value"/> with the slice-endpoint conventions
    /// (camelCase properties, verbatim dictionary keys).
    /// </summary>
    public static string Serialize(object value)
    {
        return JsonConvert.SerializeObject(value, Settings);
    }

    /// <summary>
    /// Cached <see cref="JsonSerializerSettings"/>. Exposed for callers that
    /// need to plug the settings into a custom serialization pipeline (e.g.,
    /// streaming writer). The naming strategy is immutable per Newtonsoft
    /// conventions, so reusing the settings across calls is thread-safe.
    /// </summary>
    public static readonly JsonSerializerSettings Settings = BuildSettings();

    private static JsonSerializerSettings BuildSettings()
    {
        return new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    // Tag codes (e.g. FEE_EXEMPTION) are wire identifiers, NOT
                    // properties — must NOT be mangled. See class doc-comment
                    // for the bug history.
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = true
                }
            }
        };
    }
}
