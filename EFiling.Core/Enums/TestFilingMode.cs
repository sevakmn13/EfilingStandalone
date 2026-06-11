namespace EFiling.Core.Enums;

/// <summary>
/// Controls whether filings are submitted with a JTI test header
/// that causes immediate acceptance or rejection (staging/UAT only).
/// </summary>
public enum TestFilingMode
{
    /// <summary>Normal filing — no test header injected.</summary>
    None = 0,

    /// <summary>Inject auto-accept header — filing is accepted immediately without clerk review.</summary>
    AutoAccept = 1,

    /// <summary>Inject auto-reject header — filing is rejected immediately.</summary>
    AutoReject = 2,
}
