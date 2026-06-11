namespace EFiling.Core.Enums;

/// <summary>
/// Document-level status codes from NFRC / GetFilingStatus.
/// </summary>
public enum DocumentStatus
{
    /// <summary>R — Received by court.</summary>
    Received,

    /// <summary>F — Filed / docketed.</summary>
    Filed,

    /// <summary>I — Issued by court.</summary>
    Issued,

    /// <summary>RJ — Rejected by clerk.</summary>
    Rejected,

    /// <summary>RP — Proposed-Received.</summary>
    ProposedReceived,

    /// <summary>FG — Filed and Granted.</summary>
    FiledAndGranted,

    Unknown
}
