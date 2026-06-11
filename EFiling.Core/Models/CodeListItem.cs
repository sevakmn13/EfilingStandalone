namespace EFiling.Core.Models;

/// <summary>
/// A single item from a court code list (CASE_TYPE, CASE_CATEGORY, PARTY_TYPE, etc.).
/// </summary>
public class CodeListItem
{
    /// <summary>The code value (e.g., "CU", "3201", "PLAIN").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name for the code.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Relationships to other code lists (e.g., CASE_CATEGORY → CASE_TYPE parent).</summary>
    public List<CodeListRelationship> Relationships { get; set; } = new();

    /// <summary>Additional attributes on the code (attributeName → attributeValue).</summary>
    public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>
/// A relationship from one code list item to another (e.g., a CASE_CATEGORY related to its CASE_TYPE).
/// </summary>
public class CodeListRelationship
{
    /// <summary>Name of the related list (e.g., "CASE_CATEGORY").</summary>
    public string RelatedListName { get; set; } = string.Empty;

    /// <summary>Code of the related list (e.g., "CASE_TYPE").</summary>
    public string RelatedListCode { get; set; } = string.Empty;

    /// <summary>The related code value.</summary>
    public string RelatedCode { get; set; } = string.Empty;
}
