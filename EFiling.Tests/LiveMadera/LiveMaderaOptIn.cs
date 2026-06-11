using Xunit;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Opt-in gate for tests that perform live network operations against Madera
/// staging (<see cref="JtiEFilingProvider"/>-backed Submit, GetFilingStatus, etc.).
///
/// <para>
/// <b>Why this exists.</b> Live Madera tests create real filings in Madera's
/// staging database (or pull real case data from it) on every invocation. The
/// class-level <c>[Trait("Category", "LiveMadera")]</c> marker was categorization
/// only — running <c>dotnet test</c> without a filter still executed the tests,
/// which meant every default regression produced 20+ new <c>26MA000042xx</c>
/// acceptances. This gate converts the category into a real opt-in toggle so
/// default runs are fast + side-effect-free.
/// </para>
///
/// <para>
/// <b>How to opt in.</b> Set the environment variable <c>EFILING_LIVE_MADERA</c>
/// to <c>1</c> (or <c>true</c>, case-insensitive) before running the tests:
/// <code>
/// # PowerShell
/// $env:EFILING_LIVE_MADERA = "1"
/// dotnet test --filter "FullyQualifiedName~TierB_LiveMaderaSubmit"
///
/// # bash
/// EFILING_LIVE_MADERA=1 dotnet test --filter "FullyQualifiedName~TierB_LiveMaderaSubmit"
/// </code>
/// </para>
///
/// <para>
/// <b>Skip behaviour.</b> When the env var is unset or set to anything other than
/// the enable values, test discovery still finds the tests but they report as
/// skipped with a reason that tells the operator how to enable them. This keeps
/// the test list visible in IDE runners while preventing accidental live hits.
/// </para>
///
/// <para>
/// <b>Static cache.</b> The env var is read once per process (static readonly
/// field), so toggling it mid-run has no effect — start a fresh <c>dotnet test</c>
/// process after changing the variable.
/// </para>
/// </summary>
internal static class LiveMaderaOptIn
{
    /// <summary>Name of the environment variable that enables live Madera tests.</summary>
    public const string EnvVarName = "EFILING_LIVE_MADERA";

    /// <summary>
    /// Enable values (case-insensitive). Any other value — including unset —
    /// counts as disabled. Deliberately narrow to avoid ambiguous inputs like
    /// <c>"yes"</c> or <c>"on"</c>; we want operators to type one of these two.
    /// </summary>
    private static readonly string[] EnableValues = ["1", "true"];

    private static readonly bool _isEnabled = ComputeIsEnabled();

    /// <summary>
    /// Whether live Madera tests should execute in this process. Computed once
    /// at first access from <see cref="EnvVarName"/>.
    /// </summary>
    public static bool IsEnabled => _isEnabled;

    /// <summary>
    /// Skip reason shown when a gated test is filtered out. Mentions the env var
    /// name and valid enable values so the operator can recover without hunting
    /// for docs.
    /// </summary>
    public const string SkipReason =
        "Live Madera tests disabled by default. Set env var EFILING_LIVE_MADERA=1 " +
        "(or =true) and re-run to opt in. See " +
        "src/EFiling/EFiling.Tests/LiveMadera/LiveMaderaOptIn.cs for details.";

    private static bool ComputeIsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var enabler in EnableValues)
        {
            if (string.Equals(value.Trim(), enabler, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Drop-in replacement for <see cref="FactAttribute"/> on tests that perform
/// live Madera operations. When the opt-in env var is unset the test is marked
/// as skipped (via the base attribute's <see cref="FactAttribute.Skip"/>
/// property) with a reason that tells the operator how to enable it.
/// </summary>
public sealed class LiveMaderaFactAttribute : FactAttribute
{
    public LiveMaderaFactAttribute()
    {
        if (!LiveMaderaOptIn.IsEnabled)
            Skip = LiveMaderaOptIn.SkipReason;
    }
}

/// <summary>
/// Theory variant of <see cref="LiveMaderaFactAttribute"/>. Use in place of
/// <see cref="TheoryAttribute"/> on parameterized live-Madera tests.
/// </summary>
public sealed class LiveMaderaTheoryAttribute : TheoryAttribute
{
    public LiveMaderaTheoryAttribute()
    {
        if (!LiveMaderaOptIn.IsEnabled)
            Skip = LiveMaderaOptIn.SkipReason;
    }
}
