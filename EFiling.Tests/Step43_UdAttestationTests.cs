using EFiling.Nop.Domain;
using EFiling.Nop.Models;
using EFiling.Nop.UdDisclaimer;

namespace EFiling.Tests;

/// <summary>
/// Source-fidelity tests for the §1161.2 Unlawful Detainer disclaimer +
/// party-attestation flow (Step #43, 2026-05-21).
///
/// <para>
/// These tests pin the JTI EFM vendor-doc requirements end-to-end:
/// </para>
/// <list type="bullet">
///   <item>UD detection: <see cref="UdDisclaimerPolicy.RequiresDisclaimer"/>
///     resolves Madera category code 407200 to TRUE; CIV (411900) and FAM
///     (211110) codes resolve to FALSE.</item>
///   <item>Verbatim text fidelity: the constants <see cref="UdDisclaimerPolicy.DisclaimerVerbatim"/>
///     and <see cref="UdDisclaimerPolicy.LeadInVerbatim"/> exactly match the
///     JTI doc's block-quoted text. If these constants ever drift, the
///     tests fail.</item>
///   <item>Domain audit-row shape: <see cref="UdAccessAttestation"/> exposes
///     all fields required by the JTI UD-2 mandate (customer, court,
///     case#, answer, timestamp).</item>
/// </list>
///
/// <para>
/// JTI source documents:
/// </para>
/// <list type="bullet">
///   <item><c>docs/fileing files/Subsequent Filing/General Concepts/Subsequent Filing - General Concepts _ EFM Documentation.html:230-263</c>
///     (node/436#UnlawfulDetainer — disclaimer text + audit-capture mandate)</item>
///   <item><c>docs/JTI_SUBSEQUENT_FILING_CATALOG.md §5.6.1 – §5.6.2</c>
///     (catalog with verbatim quotes + URLs)</item>
/// </list>
/// </summary>
public class Step43_UdAttestationTests
{
    // ─── UdDisclaimerPolicy.RequiresDisclaimer ──────────────────────────

    [Fact]
    public void Step43_RequiresDisclaimer_MaderaUdCode407200_ReturnsTrue()
    {
        // Madera category code 407200 maps to JCCC "UD" per
        // JtiCourtCategoryMappings.json (Step #54 closure of KD-001).
        // Step #42-R retained requiresUdDisclaimer=true on UD; UD-1 in the JTI
        // doc mandates display.
        Assert.True(UdDisclaimerPolicy.RequiresDisclaimer("madera", "407200"));
    }

    [Fact]
    public void Step43_RequiresDisclaimer_MaderaCivilCode411900_ReturnsFalse()
    {
        // Civil 411900 maps to JCCC "CIV". UD is a SUB-category of Civil per
        // the catalog §5.6 hierarchy. The disclaimer attaches to the UD
        // sub-category only, NOT the parent CIV category. This test pins
        // that distinction (a UD case will have category 407200, not 411900).
        // Step #54: courtId-aware resolver.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "411900"));
    }

    [Theory]
    [InlineData("211110")]
    [InlineData("211120")]
    [InlineData("212110")]
    [InlineData("212120")]
    public void Step43_RequiresDisclaimer_MaderaFamilyCodes_ReturnFalse(string famCode)
    {
        // Family Law codes — Step #42-R removed `requiresMinorChildRedaction`
        // and the family card from SF.cshtml (no JTI source). The §1161.2
        // disclaimer is UD-specific, never Family. This test guards against
        // future drift that would accidentally enable the UD disclaimer
        // gate for FAM cases. Step #49.X corrected '212220' → '212120'
        // (Legal Separation w/o Minor Child) — the original was a transposition
        // typo from Step #42 caught by the KD-001 drift-guard.
        // Step #54: courtId-aware resolver.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", famCode));
    }

    [Fact]
    public void Step43_RequiresDisclaimer_EmptyCode_ReturnsFalse()
    {
        // Step #54: courtId-aware resolver returns false on
        // empty/null code regardless of courtId.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", string.Empty));
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", null));
    }

    [Fact]
    public void Step43_RequiresDisclaimer_UnknownCode_ReturnsFalse()
    {
        // A code that doesn't map to any JCCC policy must NOT fire the
        // disclaimer gate (fail-closed default).
        // Step #54: courtId-aware resolver.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "999999"));
    }

    // ─── UdDisclaimerPolicy verbatim text fidelity ──────────────────────

    [Fact]
    public void Step43_DisclaimerVerbatim_ExactlyMatchesJtiDocText()
    {
        // JTI vendor doc node/436#UnlawfulDetainer lines 241-243.
        // If JTI ever publishes an updated disclaimer text and we update
        // this constant to match, this test ensures the diff is intentional
        // (not accidental drift from an assistant paraphrase, as happened
        // in Step #42 — reverted in Step #42-R).
        const string expected =
            "Code of Civil Procedure §1161.2 (a) limits access to unlawful detainer cases. " +
            "Accessing an unlawful detainer case through this system could provide confidential " +
            "information regarding the case. By accessing this case, you agree not to disclose, " +
            "copy, publish, sell, or otherwise use confidential case information you access for " +
            "any other purpose. Doing so may expose you to legal liability or result in criminal " +
            "consequences.";

        Assert.Equal(expected, UdDisclaimerPolicy.DisclaimerVerbatim);
    }

    [Fact]
    public void Step43_LeadInVerbatim_ExactlyMatchesJtiDocText()
    {
        // JTI vendor doc node/436#UnlawfulDetainer lines 230-237.
        const string expected =
            "Per Rules of Court, Unlawful Detainer cases are deemed confidential and locked " +
            "from public view at case initiation.";

        Assert.Equal(expected, UdDisclaimerPolicy.LeadInVerbatim);
    }

    [Fact]
    public void Step43_DisclaimerVerbatim_ContainsTheRequiredCitation()
    {
        // Sanity guard — the disclaimer text MUST cite §1161.2(a) verbatim
        // (the JTI doc's exact wording, not "Section 1161.2" or similar).
        Assert.Contains("§1161.2 (a)", UdDisclaimerPolicy.DisclaimerVerbatim);
    }

    [Fact]
    public void Step43_AttestationQuestion_IncludesAttorneyClause()
    {
        // The JTI doc does not mandate exact wording for the Y/N question,
        // but the UD-2 spec says non-parties are blocked. An "attorney
        // representing a party" is functionally a party per case-access
        // rules — so the question wording must include the attorney clause
        // to avoid blocking legitimate attorney filers.
        Assert.Contains("attorney", UdDisclaimerPolicy.AttestationQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("party", UdDisclaimerPolicy.AttestationQuestion, StringComparison.OrdinalIgnoreCase);
    }

    // ─── UdAccessAttestation domain audit-row shape ────────────────────

    [Fact]
    public void Step43_UdAccessAttestation_ExposesAllJtiUd2MandatedFields()
    {
        // JTI UD-2 mandate (verbatim from doc):
        // "the EFSP is required to capture the answer to the question, the
        //  user name and Case Number involved in the search"
        // Required audit fields per spec:
        //  - "the answer to the question" → AttestedAsParty (bool)
        //  - "the user name"              → CustomerId (int FK to Customer)
        //  - "Case Number involved"        → CaseDocketId (string)
        //  - (implied for any audit row) → AttestedUtc timestamp
        var attestation = new UdAccessAttestation
        {
            CustomerId = 42,
            CourtId = "madera",
            CaseDocketId = "MCV089023",
            CaseCategoryCode = "407200",
            AttestedAsParty = true,
            AttestedUtc = new DateTime(2026, 5, 21, 19, 0, 0, DateTimeKind.Utc),
            DisclaimerTextShown = "verbatim text snapshot",
            IpAddress = "127.0.0.1"
        };

        Assert.Equal(42, attestation.CustomerId);
        Assert.Equal("madera", attestation.CourtId);
        Assert.Equal("MCV089023", attestation.CaseDocketId);
        Assert.Equal("407200", attestation.CaseCategoryCode);
        Assert.True(attestation.AttestedAsParty);
        Assert.NotEqual(default, attestation.AttestedUtc);
    }

    [Fact]
    public void Step43_UdAccessAttestation_NegativeAttestationAlsoSupported()
    {
        // UD-2 requires capturing the answer EVEN WHEN IT IS NO:
        // "If the user states they are not a party to the case, they should
        //  not able to proceed further" — but the answer is still captured.
        var attestation = new UdAccessAttestation
        {
            CustomerId = 42,
            CourtId = "madera",
            CaseDocketId = "MCV089023",
            AttestedAsParty = false, // N response
            AttestedUtc = DateTime.UtcNow
        };

        Assert.False(attestation.AttestedAsParty);
    }

    // ─── UdAttestationModel shape ──────────────────────────────────────

    [Fact]
    public void Step43_UdAttestationModel_DefaultsAreSafe()
    {
        var model = new UdAttestationModel();
        Assert.Null(model.CourtId);
        Assert.Null(model.CaseDocketId);
        Assert.Null(model.CaseCategoryCode);
        Assert.Null(model.ReturnUrl);
        Assert.False(model.AttestedAsParty); // N is the safe default
    }
}
