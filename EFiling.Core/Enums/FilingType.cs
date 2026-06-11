namespace EFiling.Core.Enums;

/// <summary>
/// Indicates whether a filing is initiating a new case or adding to an existing one.
/// </summary>
public enum FilingType
{
    /// <summary>Case initiation — creates a new case.</summary>
    Initial,

    /// <summary>Subsequent filing — adds documents to an existing case.</summary>
    Subsequent
}
