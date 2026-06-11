namespace EFiling.Core.Enums;

/// <summary>
/// Filing-level status codes returned by the court/EFM.
/// </summary>
public enum FilingStatus
{
    /// <summary>Filing submitted, awaiting clerk review.</summary>
    ReceivedUnderReview,

    /// <summary>Filing accepted by clerk.</summary>
    Accepted,

    /// <summary>Some documents accepted, some rejected.</summary>
    PartiallyAccepted,

    /// <summary>Filing rejected by clerk.</summary>
    Rejected,

    /// <summary>Filing has been docketed/recorded by the court.</summary>
    Filed,

    /// <summary>Status is unknown or not yet retrieved.</summary>
    Unknown
}
