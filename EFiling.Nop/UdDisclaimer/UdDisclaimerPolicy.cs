using EFiling.Providers.JTI.Config;

namespace EFiling.Nop.UdDisclaimer;

/// <summary>
/// Source-grounded constants + helpers for the JTI §1161.2 Unlawful Detainer
/// public-disclaimer mandate.
///
/// <para>
/// Step #43 — implements UD-1 ("Public Disclaimer") and UD-2
/// ("Access Tracking and Data Capture") from JTI EFM vendor doc
/// node/436#UnlawfulDetainer
/// (<c>docs/fileing files/Subsequent Filing/General Concepts/Subsequent Filing - General Concepts _ EFM Documentation.html:230-263</c>).
/// </para>
///
/// <para>
/// The <see cref="DisclaimerVerbatim"/> string MUST be presented to users
/// without modification — it is the JTI-mandated text per the vendor doc,
/// not an assistant paraphrase. Step #42-R reverted a prior
/// implementation that used a paraphrase; this constant is the corrective
/// source-fidelity baseline.
/// </para>
/// </summary>
public static class UdDisclaimerPolicy
{
    /// <summary>
    /// VERBATIM §1161.2 disclaimer text mandated by JTI EFM doc
    /// node/436#UnlawfulDetainer. The view must display this string
    /// without modification.
    /// </summary>
    /// <remarks>
    /// Source: <c>docs/fileing files/Subsequent Filing/General Concepts/Subsequent Filing - General Concepts _ EFM Documentation.html:241-243</c>
    /// (block-quoted in the doc as the EFSP-mandated alert text).
    /// </remarks>
    public const string DisclaimerVerbatim =
        "Code of Civil Procedure §1161.2 (a) limits access to unlawful detainer cases. " +
        "Accessing an unlawful detainer case through this system could provide confidential " +
        "information regarding the case. By accessing this case, you agree not to disclose, " +
        "copy, publish, sell, or otherwise use confidential case information you access for " +
        "any other purpose. Doing so may expose you to legal liability or result in criminal " +
        "consequences.";

    /// <summary>
    /// VERBATIM lead-in sentence from JTI EFM doc that introduces the
    /// disclaimer. Shown above the block-quoted disclaimer text.
    /// </summary>
    /// <remarks>
    /// Source: <c>docs/fileing files/Subsequent Filing/General Concepts/Subsequent Filing - General Concepts _ EFM Documentation.html:230-237</c>.
    /// </remarks>
    public const string LeadInVerbatim =
        "Per Rules of Court, Unlawful Detainer cases are deemed confidential and locked " +
        "from public view at case initiation.";

    /// <summary>
    /// Party-attestation question wording. The JTI doc does not mandate
    /// specific wording for the question itself — only that the user's
    /// answer must be captured and that non-parties must be blocked.
    /// </summary>
    public const string AttestationQuestion =
        "Are you a party to this Unlawful Detainer case, or an attorney representing a party?";

    /// <summary>
    /// Determines whether the given (<paramref name="courtId"/>, <paramref name="caseCategoryCode"/>)
    /// pair maps to an Unlawful Detainer case category that requires the
    /// §1161.2 disclaimer gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backed by <see cref="JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode(string, string)"/>
    /// which resolves per-court CASE_CATEGORY codelist values (Madera numeric "407200",
    /// LASC alpha "UD", etc.) through <c>JtiCourtCategoryMappings.json</c> onto the
    /// canonical JCCC policy (UD) and reads the <c>requiresUdDisclaimer</c> flag.
    /// </para>
    /// <para>
    /// Step #43 — original courtId-less signature. Step #54
    /// — KD-001 Option B closure: added the <paramref name="courtId"/> parameter so
    /// the same numeric code can mean different things across courts without silent
    /// misclassification (e.g., LASC might use "UD" while Placer uses "421110").
    /// </para>
    /// </remarks>
    public static bool RequiresDisclaimer(string? courtId, string? caseCategoryCode)
    {
        if (string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(caseCategoryCode))
            return false;
        var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode(courtId, caseCategoryCode);
        return policy?.RequiresUdDisclaimer == true;
    }
}
