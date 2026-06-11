using System.Collections.Generic;
using EFiling.Nop.Mapping;
using Newtonsoft.Json.Linq;

namespace EFiling.Tests;

/// <summary>
/// Step #13.1 regression tests for <see cref="SchemaSliceJsonSerializer"/>.
///
/// <para>
/// Background: the previous serializer used
/// <c>CamelCasePropertyNamesContractResolver</c> which sets
/// <c>ProcessDictionaryKeys = true</c>. Newtonsoft's acronym-aware camelCase
/// converter then mangled tag-code dictionary keys like <c>FEE_EXEMPTION</c>
/// into <c>feE_EXEMPTION</c> — silently corrupting the wire because every
/// JS / server consumer that did equality checks against the canonical codes
/// would no longer match. The bug surfaced when the user verified the
/// pre-serialize payload in the browser console (Step #13 verification).
/// </para>
/// </summary>
public class SchemaSliceJsonSerializerTests
{
    // ───────────────────────────────────────────────────────────────────
    // Bug pin: all-caps dictionary keys must round-trip verbatim.
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("FEE_EXEMPTION")]
    [InlineData("EFSP_FIRST_APPEARANCE_PAID")]
    [InlineData("E_SERVICE")]
    [InlineData("EFSP_EMAIL")]
    [InlineData("SELF_REPRESENTED")]
    [InlineData("SEALED")]
    [InlineData("ERRONEOUSLY_SUED_TRUENAME")]
    [InlineData("EFSP_GOVERNMENT_EXEMPT")]
    [InlineData("EFSP_FEE_WAIVER_FILED")]
    public void Serialize_DictionaryKey_IsPreservedVerbatim_NotCamelCased(string tagCode)
    {
        // Reproduces the wire shape of the /api/efiling/field-schema?slice=tags
        // response. Pre-fix the key would emerge as e.g. "feE_EXEMPTION".
        var payload = new
        {
            success = true,
            slice = "tags",
            data = new
            {
                tags = new Dictionary<string, object>
                {
                    [tagCode] = new { tagValueKind = "digit-boolean" }
                }
            }
        };

        var json = SchemaSliceJsonSerializer.Serialize(payload);
        var parsed = JObject.Parse(json);

        var tagsObj = (JObject?)parsed["data"]?["tags"];
        Assert.NotNull(tagsObj);
        Assert.True(tagsObj!.ContainsKey(tagCode),
            $"Tag code dictionary key '{tagCode}' must round-trip verbatim. " +
            $"Got keys: [{string.Join(", ", tagsObj.Properties().Select(p => p.Name))}].");
    }

    // ───────────────────────────────────────────────────────────────────
    // Pin the partner behavior: POCO properties still get camelCased.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_PocoProperty_IsCamelCased()
    {
        // The point of the slice serializer is camelCase for properties.
        // Pinning this so a future "just disable everything" refactor doesn't
        // silently flip POCOs back to PascalCase.
        var payload = new SamplePoco { FirstName = "Sevak", LastName = "M" };

        var json = SchemaSliceJsonSerializer.Serialize(payload);
        var parsed = JObject.Parse(json);

        Assert.Equal("Sevak", parsed["firstName"]?.Value<string>());
        Assert.Equal("M", parsed["lastName"]?.Value<string>());
        Assert.Null(parsed["FirstName"]); // PascalCase must NOT be emitted
    }

    // ───────────────────────────────────────────────────────────────────
    // Pin the partner behavior: mixed-case keys also preserved (not just
    // all-caps). Confirms ProcessDictionaryKeys=false applies universally.
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("FilingParty")]            // PascalCase
    [InlineData("caseParticipant")]        // camelCase
    [InlineData("411900")]                 // numeric code
    [InlineData("MCV089014")]              // mixed alphanumeric
    public void Serialize_NonAllCapsDictionaryKey_IsPreservedVerbatim(string key)
    {
        var payload = new
        {
            data = new Dictionary<string, string>
            {
                [key] = "value"
            }
        };

        var json = SchemaSliceJsonSerializer.Serialize(payload);
        var parsed = JObject.Parse(json);

        var dataObj = (JObject?)parsed["data"];
        Assert.NotNull(dataObj);
        Assert.True(dataObj!.ContainsKey(key),
            $"Dictionary key '{key}' must round-trip verbatim. " +
            $"Got: [{string.Join(", ", dataObj.Properties().Select(p => p.Name))}].");
    }

    private class SamplePoco
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
