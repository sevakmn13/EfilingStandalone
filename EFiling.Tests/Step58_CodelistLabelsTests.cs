using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EFiling.Nop.Controllers;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for Step #58 — per-code human-readable labels
/// for the SearchCase UI.
///
/// <para><b>What changed at Step #58:</b></para>
/// <list type="bullet">
///   <item><b>JSON:</b> Added <c>codelistLabels</c> dict (180 entries from
///     <c>scripts/madera_case_category.txt</c>) under
///     <c>JtiCourtCategoryMappings.json</c>.<c>courts.madera</c>.</item>
///   <item><b>Schema model:</b> <c>CourtCategoryMapping.CodelistLabels</c>
///     property added.</item>
///   <item><b>Provider:</b> <c>JtiFieldSchemaProvider.GetCategoryLabel(
///     courtId, code)</c> helper added.</item>
///   <item><b>Controller:</b> <c>CategoryOption</c> record gained a <c>Label</c>
///     field; <c>BuildCategoryOptionsByCourt</c> populates it.</item>
///   <item><b>View:</b> dropdown options render <c>${label} (${code})</c>;
///     results table renders Type/Category labels with a small code subtitle;
///     "Parties" column replaced by "Court" column; "View" button removed;
///     "File" button renamed to "File into case".</item>
/// </list>
///
/// <para>
/// These tests pin the structural shape so a future schema-shape change
/// or accidental codelist drift surfaces immediately.
/// </para>
/// </summary>
public sealed class Step58_CodelistLabelsTests
{
    private const string MappingsResourceName =
        "EFiling.Providers.JTI.Config.JtiCourtCategoryMappings.json";

    // ─── JSON shape ────────────────────────────────────────────────────────

    [Fact]
    public void Step58_MaderaCodelistLabels_HasExpectedEntryCount()
    {
        // 181 entries = 180 from scripts/madera_case_category.txt (PowerShell
        // ingest at Step #58, sorted by code) + 1 manual entry for 603110
        // ("LPS Conservatorship (CMS-stored)") which is referenced in
        // categoryCodeToJccc (PRB) per Step #52 evidence but is NOT in the
        // submission codelist file because Madera CMS stores it under a
        // different value (532110 submission → 603110 CMS storage). Drift
        // would indicate either an accidental edit or a real codelist
        // refresh — both should force a deliberate review.
        using var doc = LoadMappingsJson();
        var labels = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("codelistLabels");

        var count = 0;
        foreach (var _ in labels.EnumerateObject()) count++;
        Assert.Equal(181, count);
    }

    [Theory]
    // 4 codes promoted at Step #57 — each MUST have a Step #58 label.
    [InlineData("411100", "Civil Comp: RICO (27)")]
    [InlineData("213110", "Nullity w/Minor Child")]
    [InlineData("606110", "PC1368-Competency")]
    [InlineData("612110", "In Re Hop-Developmentally Disabled")]
    // Pre-Step-#57 evidence-backed codes — sanity spot-check.
    [InlineData("407200", "Unlawful Detainer: Residential (32)")]
    [InlineData("531110", "Conservatorship")]
    [InlineData("613110", "Mental Health-Other")]
    public void Step58_MaderaCodelistLabels_ContainsExpectedLabel(string code, string expectedLabel)
    {
        using var doc = LoadMappingsJson();
        var labels = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("codelistLabels");

        Assert.True(labels.TryGetProperty(code, out var labelEl),
            $"codelistLabels missing required code '{code}'.");
        Assert.Equal(expectedLabel, labelEl.GetString());
    }

    [Fact]
    public void Step58_MaderaCodelistLabels_AllCategoryCodeToJcccEntriesHaveLabels()
    {
        // Forcing function: every code mapped to a JCCC SHOULD have a
        // human-readable label so the dropdown never falls back to the bare
        // code. This catches the case where someone adds a new
        // categoryCodeToJccc entry but forgets the matching label.
        using var doc = LoadMappingsJson();
        var madera = doc.RootElement.GetProperty("courts").GetProperty("madera");
        var jccc = madera.GetProperty("categoryCodeToJccc");
        var labels = madera.GetProperty("codelistLabels");

        var missing = new List<string>();
        foreach (var entry in jccc.EnumerateObject())
        {
            if (!labels.TryGetProperty(entry.Name, out _))
                missing.Add(entry.Name);
        }
        Assert.True(missing.Count == 0,
            $"categoryCodeToJccc entries missing codelistLabels: [{string.Join(", ", missing)}]");
    }

    // ─── Schema-model surface ──────────────────────────────────────────────

    [Fact]
    public void Step58_CourtCategoryMapping_HasCodelistLabelsProperty()
    {
        var prop = typeof(CourtCategoryMapping).GetProperty(
            "CodelistLabels",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(Dictionary<string, string>), prop!.PropertyType);
    }

    [Fact]
    public void Step58_GetCategoryLabel_ResolvesKnownMaderaCode()
    {
        var label = JtiFieldSchemaProvider.GetCategoryLabel("madera", "411100");
        Assert.Equal("Civil Comp: RICO (27)", label);
    }

    [Fact]
    public void Step58_GetCategoryLabel_IsCaseInsensitiveOnCourtId()
    {
        var label = JtiFieldSchemaProvider.GetCategoryLabel("MADERA", "606110");
        Assert.Equal("PC1368-Competency", label);
    }

    [Fact]
    public void Step58_GetCategoryLabel_ReturnsNull_ForUnknownCourt()
    {
        Assert.Null(JtiFieldSchemaProvider.GetCategoryLabel("nonexistent-court", "411100"));
    }

    [Fact]
    public void Step58_GetCategoryLabel_ReturnsNull_ForUnknownCode()
    {
        Assert.Null(JtiFieldSchemaProvider.GetCategoryLabel("madera", "999999"));
    }

    [Fact]
    public void Step58_GetCategoryLabel_ReturnsNull_ForEmptyInputs()
    {
        Assert.Null(JtiFieldSchemaProvider.GetCategoryLabel("", "411100"));
        Assert.Null(JtiFieldSchemaProvider.GetCategoryLabel("madera", ""));
        Assert.Null(JtiFieldSchemaProvider.GetCategoryLabel(null!, null!));
    }

    // ─── Controller-side dropdown contract ─────────────────────────────────

    [Fact]
    public void Step58_CategoryOption_RecordHasLabelField()
    {
        // The view embeds the JSON-serialized list of CategoryOption records
        // and JS reads `o.Label` from the embedded data. Ensuring Label exists
        // on the record protects the JS contract.
        var labelProp = typeof(EFilingMvcController.CategoryOption)
            .GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(labelProp);
        Assert.Equal(typeof(string), labelProp!.PropertyType);
    }

    [Fact]
    public void Step58_CategoryOption_ConstructorHasFourPositionalArgs()
    {
        // record CategoryOption(Jccc, JcccLabel, Code, Label)
        var ctor = typeof(EFilingMvcController.CategoryOption)
            .GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 4);
        Assert.NotNull(ctor);
        var paramNames = ctor!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Contains("Jccc", paramNames);
        Assert.Contains("JcccLabel", paramNames);
        Assert.Contains("Code", paramNames);
        Assert.Contains("Label", paramNames);
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static JsonDocument LoadMappingsJson()
    {
        var asm = typeof(JtiFieldSchemaProvider).Assembly;
        using var stream = asm.GetManifestResourceStream(MappingsResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();
        return JsonDocument.Parse(json);
    }
}
