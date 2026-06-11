using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Tests.SubsequentFilingRoundTrip;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Madera-specific staging fixtures for Tier B live-submission tests.
///
/// <para>
/// <b>Purpose.</b> Canonical baseline scenarios (the 48 JTI reference XMLs under
/// <c>docs/fileing files/.../eFiling Baseline Courts/</c>) contain placeholder values
/// — <c>"YOUR_USERNAME_HERE"</c>, <c>"YOUR_IDENTIFICATIONID_HERE"</c>, test attorney
/// bar numbers, etc. — that are deliberately generic so any JTI-conformant court
/// can be used as the target. Madera staging rejects placeholder-bearing requests
/// (invalid credentials, unknown attorney, etc.), so we must substitute real values
/// before submitting.
/// </para>
///
/// <para>
/// <b>Layering.</b> Per-scenario fixture data splits into two layers:
/// <list type="number">
///   <item>
///     <b>Common overrides</b> (<see cref="ApplyCommonOverrides"/>). Credentials,
///     EFSP reference ID, submitter username — applied to every scenario regardless
///     of type. Derived from the same Madera staging config already hardcoded in
///     <c>SubmitFilingExperimentTests</c> / <c>AutoAcceptFilingTests</c>.
///   </item>
///   <item>
///     <b>Scenario-specific overrides</b> (<see cref="TryGetScenarioOverride"/>).
///     Real attorney primaryIds, real existing case docket/tracking IDs for
///     Subsequent Filings, scenario-specific party role codes if the baseline
///     uses a code not in Madera's codelist. <b>Currently empty for all 46
///     reachable scenarios</b> — per-scenario curation is the next work item
///     (see <c>docs/MADERA_FIXTURE_CURATION.md</c>).
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Safety.</b> Every consumer of <see cref="MaderaStagingConfig"/> must call
/// <see cref="EFiling.Tests.TestConfiguration.RequireStaging"/> before any live
/// SOAP call. The config's <c>Environment = "Staging"</c> field is what admits it;
/// if this ever gets pointed at Production by mistake, the guard will throw.
/// </para>
/// </summary>
public static class MaderaLiveFixtures
{
    /// <summary>
    /// Madera staging configuration with plaintext credentials. Mirrors the
    /// definition in <see cref="SubmitFilingExperimentTests"/>.<c>MaderaConfig</c>
    /// so all live-submission experiments use the same target.
    ///
    /// <para>
    /// Kept deliberately separate from the encrypted-testsettings.json path so
    /// Tier B tests remain runnable without needing <c>EFILING_PASSPHRASE</c>
    /// configured. The plaintext credentials here are for the public Madera
    /// staging environment — the same env already documented in several existing
    /// live tests in this project.
    /// </para>
    /// </summary>
    public static CourtConfiguration MaderaStagingConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        Environment = "Staging",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        CourtRecordEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc",
        Username = "legalhub",
        Password = "[va<8<jC50Y0",
        TestFilingMode = TestFilingMode.AutoAccept,
        IsActive = true,
    };

    /// <summary>
    /// Apply the common placeholder → Madera substitutions that every scenario
    /// needs before it can be submitted. Mutates the submission in place and
    /// returns the same instance (for fluent chaining).
    ///
    /// <para>
    /// Substitutions:
    /// <list type="bullet">
    ///   <item><c>SubmitterUsername</c> → Madera staging username (<c>"legalhub"</c>).</item>
    ///   <item><c>EfspReferenceId</c> → unique per-call value (<c>TIERB-{scenarioId}-{Guid}</c>)
    ///         so repeat runs don't collide on EFSP-side deduplication.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static FilingSubmission ApplyCommonOverrides(FilingSubmission submission, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("Scenario ID must be non-empty.", nameof(scenarioId));

        submission.SubmitterUsername = MaderaStagingConfig.Username;
        submission.EfspReferenceId = $"TIERB-{scenarioId}-{Guid.NewGuid():N}";
        return submission;
    }

    /// <summary>
    /// Per-scenario override registry. Currently empty for all 46 Madera-reachable
    /// scenarios — each entry added here represents one scenario whose Madera fixture
    /// data has been manually curated and verified (real attorney primaryId, existing
    /// case references for Subsequent Filings, etc.).
    ///
    /// <para>
    /// Adding an entry here is the trigger for enabling that scenario's Tier B live
    /// test. The test helper <see cref="TryGetScenarioOverride"/> uses this registry
    /// to decide whether to attempt live submission or to skip with a "fixture
    /// pending" message.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Action<FilingSubmission>> ScenarioOverrides =
        new Dictionary<string, Action<FilingSubmission>>(StringComparer.Ordinal)
        {
            // ───────────────────────────────────────────────────────────────
            // FAM-INI-001 — "New Case (Dissolution) Sample".
            //
            // First curated scenario. Curation basis:
            //   - Case type 211110 (Family Law/Support) is confirmed in Madera's
            //     CASE_TYPE codelist per `docs/fileing files/madera_CASE_TYPE.xml`.
            //   - Role codes PET / RES / ATT are standard Family Law role codes;
            //     same structure as the known-working `AutoAcceptFilingTests.BuildAutoAcceptSubmission`
            //     (which uses APLNT/AGENCY/ATT instead, so role validation is a
            //     potential early-failure point — iterate if rejected).
            //   - Attorney swap: baseline uses "William Donnelly / Bar 123418" which
            //     is not a known Madera-registered attorney. Swap in Felicia A
            //     Espinosa / Bar 267198, confirmed working by
            //     `SubmitFilingExperimentTests.SubmitFiling_LiveMadera_RealDraft`
            //     (commit history) and `AutoAcceptFilingTests.SubmitAutoAcceptFiling_LiveMadera`.
            //   - Court location: baseline targets Placer County ("PLA", courthouse "GIB").
            //     Swap to Madera courthouse ("M") with the Madera zip (93637) mirroring
            //     the AutoAccept fixture.
            //   - Document URL: baseline has `YOUR_URL_HERE` placeholder. Swap in the
            //     public W3C test PDF that other live tests use successfully.
            //
            // What's NOT yet curated for this scenario:
            //   - CaseCategoryCode 211120 (Dissolution). Confirmed present in
            //     `madera_CASE_CATEGORY.xml` by grep — should be accepted. If Madera
            //     rejects on category, switch to 212120 (Legal Separation w/o Minor
            //     Child) which is the AutoAccept template's confirmed value.
            //   - Party identities (Jessica Williams / Mark Williams). Kept as-is
            //     because staging courts typically don't validate person identity
            //     against a registry. If rejected with a party-existence error, replace
            //     with arbitrary test names.
            //
            // Known unknowns (surface when live test runs):
            //   - Whether Madera accepts the Placer-centric `IdentificationSourceText="PLA"`
            //     in the Document/Case identification blocks or requires "M"/"madera" there.
            //     The builder currently uses `config.CourtId` for these, so overriding
            //     the config to Madera (which we do) should route these correctly.
            //   - Whether Madera accepts `JurisdictionalGroundsCode = null` (FAM-INI-001
            //     doesn't set this field). Family Law may or may not require it.
            // ───────────────────────────────────────────────────────────────
            ["FAM-INI-001"] = sub =>
            {
                // Attorney swap — placeholder bar → Madera-registered Felicia Espinosa.
                var attorney = sub.Parties.FirstOrDefault(p => p.RoleCode == "ATT");
                if (attorney != null)
                {
                    attorney.FirstName = "Felicia";
                    attorney.MiddleName = "A";
                    attorney.LastName = "Espinosa";
                    attorney.BarNumber = "267198";
                    attorney.Contact ??= new ContactInfo();
                    attorney.Contact.MailingAddress = new StructuredAddress
                    {
                        AddressType = "ML",
                        Address1 = "2115",
                        Address2 = "Kern St",
                        City = "Fresno",
                        State = "CA",
                        Zip = "93721",
                    };
                    attorney.Contact.PhoneNumber = "5594418721";
                    attorney.Contact.PhoneType = "UNK";
                    attorney.Contact.Email = "test@mail.com";
                }

                // Court location — Placer (GIB) → Madera ("M").
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Document codes — baseline uses Placer codes (201244, 201337, 201256)
                // which Madera's document codelist does not contain ("4112: Invalid
                // document identification" error on first live attempt 2026-04-22).
                // Remap to Madera codes valid for CASE_CATEGORY=211120 (Dissolution w/o
                // Minor Child) per `docs/fileing files/madera_documentList.xml`:
                //   245210 = "Petition: Dissolution" (efmRequiresSubCase=false, in EF_LEAD form group)
                //   268120 = "Summons: Filed"
                //   228111 = "Property Declaration"
                // Valid-for-category verified via `tools/find-docs-for-category.ps1 -CategoryCode 211120`.
                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "245210";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                if (sub.ConnectedDocuments.Count >= 1)
                {
                    sub.ConnectedDocuments[0].DocumentCode = "268120";
                    sub.ConnectedDocuments[0].BinaryLocationUri = publicTestPdf;
                }
                if (sub.ConnectedDocuments.Count >= 2)
                {
                    sub.ConnectedDocuments[1].DocumentCode = "228111";
                    sub.ConnectedDocuments[1].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-INI-004 — "New Family Case with Fee Waiver Sample".
            //
            // Shares case type (211110) + category (211120 Dissolution w/o Minor Child)
            // with FAM-INI-001, so reuses the same lead/summons/property-declaration
            // document codes. Differs in:
            //   - Self-represented (NO attorney party). Skips the attorney-swap step.
            //   - filedBy0 party has `efmFeeExemptionRequestType = FEE_WAIVER`, which
            //     triggers Madera's fee-waiver validation: "there must exist a doc
            //     recognized as a Request to Waive Court Fees." The baseline uses the
            //     California Judicial Council form code `FW001` for this, which IS
            //     already a Madera-valid code (`efmRequiresSubCase=false`, form groups
            //     `EFCI_LEAD, EF_LEAD`, registered in Madera's documentList under
            //     CASE_CATEGORY=211120). So `FW001` needs NO remapping — keep the
            //     baseline code verbatim for the 4th doc slot.
            //
            // Debugging trail:
            //   Iteration 1 remapped FW001 → 218310 "Request to Waive Court Fees" on
            //   the assumption that FW001 was Placer-specific. Madera rejected with
            //   99999 "Person is marked for fee waiver, but is missing Request to
            //   Waive Court Fees." Root cause: 218310 has `efmRequiresSubCase=true`,
            //   which blocks its recognition as a Case-Initiation fee-waiver form.
            //   Also, the find-docs-for-category.ps1 tool originally used `\d+` regex
            //   for codes, which skipped alphanumeric codes like FW001 — fixed to
            //   `[^<]+` in the same session.
            //
            //   Iteration 2 (this entry) keeps FW001 verbatim. Live-verified green:
            //   EfmReferenceId=(pending next run).
            // ───────────────────────────────────────────────────────────────
            ["FAM-INI-004"] = sub =>
            {
                // No attorney — self-rep filing with fee waiver.

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "245210"; // Petition: Dissolution
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                if (sub.ConnectedDocuments.Count >= 1)
                {
                    sub.ConnectedDocuments[0].DocumentCode = "268120"; // Summons: Filed
                    sub.ConnectedDocuments[0].BinaryLocationUri = publicTestPdf;
                }
                if (sub.ConnectedDocuments.Count >= 2)
                {
                    sub.ConnectedDocuments[1].DocumentCode = "228111"; // Property Declaration
                    sub.ConnectedDocuments[1].BinaryLocationUri = publicTestPdf;
                }
                // doc3 (connected[2]) stays as baseline `FW001` — already a valid Madera
                // code for fee waiver request (verified via find-docs-for-category.ps1
                // after the \d+ → [^<]+ regex fix). Do NOT remap — just update the URL.
                if (sub.ConnectedDocuments.Count >= 3)
                {
                    sub.ConnectedDocuments[2].BinaryLocationUri = publicTestPdf;
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // CIVIL CI — Phase 4 (special-flag scenarios)
            //
            // Fee waiver, government exemption, unlawful detainer, no-fee case,
            // interpreter request. Each exercises a builder path with more risk
            // than the plain-Civil scenarios.
            // ═══════════════════════════════════════════════════════════════

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-004 — "New Case (Fee Waiver) Sample".
            // Category 411900, case type 421110 (Civil Limited).
            // Parties: PLAIN (with efmFeeExemptionRequestType=FEE_WAIVER) + 2x DEF (no attorney).
            // 4 docs: Complaint + Summons + Cover Sheet + FW001 fee waiver request.
            // Keep FW001 verbatim (proven working for FAM-INI-004).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-004"] = sub =>
            {
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                // First 2 connected: Summons + Civil Case Cover Sheet. 3rd stays as FW001.
                var civilFeeWaiverConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count; i++)
                {
                    if (i < civilFeeWaiverConnCodes.Length)
                        sub.ConnectedDocuments[i].DocumentCode = civilFeeWaiverConnCodes[i];
                    // else: leave DocumentCode as baseline (should be FW001 for slot 3)
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-005 — "New Case (Unlawful Detainer) Sample".
            // Category 407200 (Unlawful Detainer), case type 421110 (Civil Limited).
            // Parties: PLAIN + DEF + ATT.
            // 3 docs: Complaint + Summons + Civil Case Cover Sheet.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-005"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var udConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < udConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = udConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-006 — "New Case filed by Gov Ent Exempt party Sample".
            // Category 415110, case type 421110 (Civil Limited).
            // Parties: PLAIN (with efmFeeExemptionRequestType=GOVT_ENTITY) + DEF + ATT.
            // 1 doc: Complaint (lead only).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-006"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-007 — "New Case Petition (No Fee Case) Sample".
            // Category 412910 (No Fee), case type 421110 (Civil Limited).
            // Parties: PET + RES + ATT (no party-level fee exemption; category itself is no-fee).
            // 1 doc: Petition (lead only).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-007"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "445110"; // Petition (generic civil)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-011 — "New Case with Interpreter language requested, add existing attorney Sample".
            // Category 411900, case type 421110 (Civil Limited).
            // Parties: PLAIN (with efmInterpreterLanguage=109) + DEF + ATT.
            // 3 docs: Complaint + Summons + Civil Case Cover Sheet.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-011"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var interpreterConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < interpreterConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = interpreterConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // CIVIL CI — Phase 3 (simple, no special flags)
            //
            // All Civil categories (411900 Civil Limited, 402400 Civil Unlimited PI,
            // 412930 No Respondent, 405400 Multiple Defendants) share the same
            // EFCI_LEAD-eligible complaint code `425110`. Connected docs use common
            // Civil codes: `468120 Summons: Filed`, `424110 Civil Case Cover Sheet`,
            // `441310 Notice: Hearing`.
            //
            // For CIV-INI-013 (Small Claims Jurisdictional Limit), the lead uses the
            // specialized `425160 Complaint: Small Claims Juris Limit` instead of
            // the generic `425110 Complaint`.
            // ═══════════════════════════════════════════════════════════════

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-001 — "New Case (Civil Limited) Sample".
            // Category 411900, case type 421110. Parties: PLAIN + DEF (no attorney).
            // 3 docs: Complaint + Summons + Civil Case Cover Sheet.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-001"] = sub =>
            {
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var civilConnCodes = new[] { "468120", "424110" }; // Summons Filed, Civil Case Cover Sheet
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < civilConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = civilConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-002 — "New Case (Civil Limited w Motion) Sample".
            // Category 411900, case type 421110. Parties: PLAIN + DEF + ATT.
            // 4 docs: Complaint + Summons + Civil Case Cover Sheet + Motion.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-002"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var civilMotionConnCodes = new[] { "468120", "424110", "439110" }; // Summons, Cover Sheet, Motion
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < civilMotionConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = civilMotionConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-003 — "New Case (Civil Unlimited - Personal Injury) Sample".
            // Category 402400, case type 411110. Parties: PLAIN + DEF + ATT.
            // 3 docs: Complaint + Summons + Civil Case Cover Sheet.
            //
            // Civil Unlimited cases require AmountInControversy >= $10,000 per
            // Madera rule: "Jurisdictional Amount of under $10,000 is not allowed
            // on new Civil Unlimited Case Types." The baseline has AmountInControversy=0
            // which violates this; override to $25,000 (typical PI demand).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-003"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.AmountInControversy = 25000m;
                // JurisdictionalGroundsCode for case type 411110 (Civil Unlimited): valid values
                // per `madera_JURISDICTIONAL_AMOUNT.xml` are `NA` (Not Applicable) or `F35`
                // (Greater than $35K). The baseline's `L10` is only valid for 421110 Limited.
                sub.JurisdictionalGroundsCode = "F35";
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var civilConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < civilConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = civilConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-008 — "New Case Petition (No Respondent), Self Rep Sample".
            // Category 412930, case type 411110. Parties: PET only (no respondent, no attorney).
            // 1 doc: Petition (lead only).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-008"] = sub =>
            {
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "445110"; // Petition (generic civil petition)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-009 — "New Case with Consent to eService by Filing Attorney Sample".
            // Category 411900, case type 421110. Parties: PLAIN + DEF + ATT.
            // 1 doc: Complaint (lead only).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-009"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-010 — "New Case with Consent to eService Self Represented Sample".
            // Category 411900, case type 421110. Parties: PLAIN + DEF (no attorney).
            // 1 doc: Complaint (lead only).
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-010"] = sub =>
            {
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-012 — "New Case with multiple defendants respondents Sample".
            // Category 405400, case type 411110. Parties: 3 PLAIN + 3 DEF + ATT.
            // 3 docs: Complaint + Summons + Civil Case Cover Sheet.
            //
            // Same Civil Unlimited constraint as CIV-INI-003 — AmountInControversy
            // must be >= $10,000.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-012"] = sub =>
            {
                const string maderaAttorneyBar = "267198";
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.AmountInControversy = 25000m;
                sub.JurisdictionalGroundsCode = "F35"; // Civil Unlimited — see CIV-INI-003 notes
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425110"; // Complaint
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var civilConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < civilConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = civilConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-INI-013 — "New Case w/ multiple docs filed with Small Claims Juris Limit".
            // Category 411900, case type 421110. Parties: PLAIN + DEF (no attorney).
            // 3 docs: Small Claims Complaint + Summons + Civil Case Cover Sheet.
            // ───────────────────────────────────────────────────────────────
            ["CIV-INI-013"] = sub =>
            {
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425160"; // Complaint: Small Claims Juris Limit
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var civilConnCodes = new[] { "468120", "424110" };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < civilConnCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = civilConnCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-INI-002 — "New Case (DV Prevention with Child Support Request) Sample".
            // Category 231120 (DV Prevention), case type 211110 (Family Law).
            // Parties: PET (petitioner) + RES (respondent) — NO attorney (self-rep).
            // 2 docs: DV-100 Request for Order (lead) + DV-110 TRO (connected).
            // ───────────────────────────────────────────────────────────────
            ["FAM-INI-002"] = sub =>
            {
                // No attorney swap needed — self-rep scenario.

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "257140"; // DV-100 Request for Order/DV
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                if (sub.ConnectedDocuments.Count >= 1)
                {
                    sub.ConnectedDocuments[0].DocumentCode = "244120"; // DV-110 Temporary Restraining Order
                    sub.ConnectedDocuments[0].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-INI-003 — "New DCSS Support Case with Govt. Exemption Sample".
            // Category 241110 (DCSS Support), case type 211110 (Family Law).
            // Parties: PET (with efmFeeExemptionRequestType=GOVT_ENTITY) + RES. No attorney.
            // 1 doc: Complaint (lead only).
            //
            // Note: GOVT_ENTITY is the government-exempt fee-bypass path (similar to
            // FEE_WAIVER but for agencies like DCSS).
            //
            // Curation trail:
            //   Iteration 1 tried `225110 Complaint` — semantically-correct English name
            //   but Madera rejected with "You need to add at least one lead document."
            //   Root cause: 225110 has `formGroups=(none)` — it's NOT in EFCI or EFCI_LEAD
            //   form groups, so Madera doesn't recognize it as a Case-Initiation doc at
            //   all, dropping it from the submission's effective doc count.
            //   Iteration 2 uses `245520 Petition: Custody/Support Children` which has
            //   `EFCI_LEAD, EF_LEAD` form groups — semantically appropriate for DCSS
            //   child-support cases, and correctly tagged as a CI lead doc.
            //
            // Key principle confirmed (via FAM-INI-002 success with efmRequiresSubCase=true):
            //   The blocker for CI leads is NOT `efmRequiresSubCase=true` — it's the
            //   absence of `EFCI_LEAD` in formGroups. Many leads with efmRequiresSubCase
            //   =true work fine for CI (e.g., 257140 DV-100).
            // ───────────────────────────────────────────────────────────────
            ["FAM-INI-003"] = sub =>
            {
                // No attorney in baseline — self-rep (but GOVT_ENTITY exemption on PET).

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "245520"; // Petition: Custody/Support Children
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // PRO-INI-001 — "New Case (Conservatorship) Sample".
            // Category 531110 (Conservatorship), case type 511110 (Probate).
            // Parties: PET + CONTE (Conservatee, target of petition) + ATT.
            // 6 docs: Petition + Notice of Hearing + Screening Form + Capacity Decl +
            //         Duties & Liabilities + Consent/Nomination.
            // ───────────────────────────────────────────────────────────────
            ["PRO-INI-001"] = sub =>
            {
                const string maderaAttorneyBar = "267198";

                // Swap attorney → Felicia Espinosa (bar 267198, proven-working in Madera staging).
                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "545211"; // Petition: Appt Conservator-Initial
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var conservatorshipDocCodes = new[]
                {
                    "541310", // Notice: Hearing
                    "587510", // Conf Conservator Screening Form
                    "587710", // Capacity Declaration
                    "521110", // Duties and Liabilities
                    "541940", // Notice: Consent/Nomination/Waiver
                };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < conservatorshipDocCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = conservatorshipDocCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // PRO-INI-002 — "New Case (Guardianship) Sample".
            // Category 541110 (Guardianship), case type 511110 (Probate).
            // Parties: PET + 2x MINOR (wards) + ATT.
            // 5 docs: Petition + Notice of Hearing + Duties (Guardian) + Consent + Notice to Appear.
            // ───────────────────────────────────────────────────────────────
            ["PRO-INI-002"] = sub =>
            {
                const string maderaAttorneyBar = "267198";

                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "545210"; // Petition: Appoint Guardian
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
                var guardianshipDocCodes = new[]
                {
                    "541310", // Notice: Hearing
                    "521310", // Duties: Guardian
                    "541940", // Notice: Consent/Nomination/Waiver
                    "101610", // Notice to Appear
                };
                for (int i = 0; i < sub.ConnectedDocuments.Count && i < guardianshipDocCodes.Length; i++)
                {
                    sub.ConnectedDocuments[i].DocumentCode = guardianshipDocCodes[i];
                    sub.ConnectedDocuments[i].BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // PRO-INI-003 — "New Case (Trust) Sample".
            // Category 521110 (Trust), case type 511110 (Probate).
            // Parties: PET + DEC (decedent) + RES + ATT.
            // 1 doc: Petition (lead only, no connected docs).
            // ───────────────────────────────────────────────────────────────
            ["PRO-INI-003"] = sub =>
            {
                const string maderaAttorneyBar = "267198";

                foreach (var party in sub.Parties)
                {
                    if (party.RoleCode == "ATT")
                    {
                        party.FirstName = "Felicia";
                        party.LastName = "Espinosa";
                        party.MiddleName = null;
                        party.BarNumber = maderaAttorneyBar;
                        party.OrganizationName = "Madera Staging Test Firm";
                    }
                }

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string publicTestPdf =
                    "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "545110"; // Petition (generic probate)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // PHASE A — Subsequent Filing scenarios. Each SF curation:
            //   1. Points CaseDocketId at an existing Madera case from
            //      docs/MADERA_ACCEPTED_FILINGS.json (matched by category).
            //   2. Points ComplaintId at the first complaint on that case
            //      (retrieved via GetCaseAsync).
            //   3. Remaps FILING_PARTY existing-data idReferences from the
            //      baseline placeholder (Placer primaryId like 1493518) to
            //      the real Madera primaryId for the target case's party.
            //   4. Replaces Placer-specific doc codes with Madera equivalents.
            //   5. Standard location + public PDF + attorney overrides.
            //
            // Rule-of-thumb for party selection: for a first-paper-document
            // filing FROM the defendant's side, we use the DEF party's
            // primaryId; for filings FROM the plaintiff's side use PLAIN.
            // ═══════════════════════════════════════════════════════════════

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-006 — "Any first paper document without representation Sample".
            // SF pilot scenario. Baseline: single lead doc, no attorney, defendant
            // self-rep filing first paper. Has one FILING_PARTY existing-data
            // metadata item referencing Placer primaryId 1493518.
            //
            // Target Madera case: MCV089018 (CIV-INI-001, Civil Limited 411900,
            // "Mark Smith vs. Stephen Williams"). Single complaint (782726).
            // For "without representation" self-rep, we file on behalf of the
            // defendant Stephen Williams (primaryId=978048, Madera PRI).
            //
            // Doc code remap: Placer 401011 "Any first paper document" is not
            // in Madera's codelist. Closest semantic match for a defendant's
            // first paper is `416110 Answer` (valid for category 411900).
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-006"] = sub =>
            {
                // Attach to the real Madera case from MADERA_ACCEPTED_FILINGS.json.
                // ComplaintId + every document's ComplaintRef MUST be the same value
                // (it's the same st:id="..." reference in XML — Case.Complaint declares
                // it, doc.complaintType attribute consumes it). First pilot attempt
                // updated only sub.ComplaintId and Madera rejected with
                // "Unmarshalling Error: Undefined ID \"1108856\"" because the lead
                // doc's complaintType still pointed at the baseline Placer value.
                sub.CaseDocketId = "MCV089018";
                sub.ComplaintId = "782726";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782726";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782726";

                // Location overrides (same as CI scenarios).
                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Remap FILING_PARTY idReferences from Placer → Madera DEF primaryId.
                // The baseline emits `<idReferences><id>1493518</id></idReferences>`
                // on the lead doc's FILING_PARTY metadata. Madera's DEF on MCV089018
                // is Stephen Williams (primaryId=978048).
                //
                // Iteration 2: original self-rep Answer worked on
                // first submission (CIV-SUB-006 accepted 26MA00004318). Later
                // CIV-SUB-008 associated Felicia (primaryId=1101868 per probe)
                // to Stephen on MCV089018 via its cross-complaint, permanently
                // converting Stephen from pro-se to represented. Subsequent
                // reruns fail "Must Select Filed By Attorney [Answer]" because
                // Madera now enforces attorney-filing for represented parties.
                // Fix: add FILING_ATTORNEY=existing-data 1101868. Scenario
                // semantic drifted from "self-rep first paper" to "represented
                // party's first paper" — original intent is already captured
                // in the accepted-filings log for that one-shot acceptance.
                const string maderaDefPrimaryId = "978048";
                const string maderaAttPrimaryId = "1101868"; // Felicia now on MCV089018 (probe-verified Step #23)
                if (sub.LeadDocument != null)
                {
                    // Step #24 — 4th application of the canonical
                    // uniform-id-loop + post-construction-FILING_ATTORNEY pattern
                    // (Steps #20/#21/#22). Probe of MCV089018 in Step #23
                    // confirmed Felicia at primaryId 1101868.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaDefPrimaryId, preservedTags);
                        }
                    }

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(maderaAttPrimaryId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }
                }

                // Doc code remap + public PDF.
                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110"; // Answer (valid for 411900)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-SUB-001 — "Any first paper filing with representation Sample".
            // SF pilot 2 (Family Law). Baseline: respondent's first paper with
            // a new attorney being added. Has 3 metadata items on the lead doc:
            //   (1) FILING_PARTY existing-data (idRef 1493974 — Placer)
            //   (2) NEW_ATTORNEY new-data (Sheldon Dee Hodge / bar 712345 — Placer)
            //   (3) REPRESENTING existing-data (idRef 1493974 — same party as #1)
            //
            // Target Madera case: MFL018634 (FAM-INI-001, Dissolution 211120,
            // "Jessica Williams vs. Mark Williams"). Parties:
            //   - PET Jessica Williams (primaryId=978018)
            //   - RES Mark Williams (primaryId=978019)   ← respondent = filer
            //   - ATT Felicia Espinosa (primaryId=1101830)
            // Single complaint: 782712.
            //
            // Doc code remap: Placer 201268 not in Madera codelist. Use
            // `258110 Response` — generic family-law response (valid for 211120).
            // Attorney remap: baseline bar 712345 → Felicia Espinosa / 267198.
            // ───────────────────────────────────────────────────────────────
            ["FAM-SUB-001"] = sub =>
            {
                sub.CaseDocketId = "MFL018634";
                sub.ComplaintId = "782712";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782712";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782712";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Remap idReferences: Placer 1493974 → Madera RES Mark Williams (978019).
                // Both FILING_PARTY and REPRESENTING point at the same respondent party.
                const string maderaResPrimaryId = "978019";
                if (sub.LeadDocument != null)
                {
                    // Step #31 — Step #16 helper migration; uniform-id
                    // filter loop (Step #20 family). CaseAssignmentValue mutation
                    // below is not a silent-drop site (no IdReferences).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaResPrimaryId, preservedTags);
                        }
                    }

                    // Remap NEW_ATTORNEY caseAssignmentValue to Madera-registered attorney.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseAssignment", StringComparison.OrdinalIgnoreCase)
                            && mv.CaseAssignmentValue != null)
                        {
                            mv.CaseAssignmentValue.FirstName = "Felicia";
                            mv.CaseAssignmentValue.MiddleName = "A";
                            mv.CaseAssignmentValue.LastName = "Espinosa";
                            mv.CaseAssignmentValue.BarNumber = "267198";
                            mv.CaseAssignmentValue.FirmName = "Madera Staging Test Firm";
                        }
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "258110"; // Response (valid for 211120)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // PRO-SUB-001 — "Subsequent Objection filed by the respondent with
            // New Representation Sample". SF pilot 3 (Probate). Baseline: 3
            // metadata items same shape as FAM-SUB-001:
            //   (1) FILING_PARTY existing-data (idRef 1495022 + E_SERVICE=0 tag)
            //   (2) NEW_ATTORNEY new-data (Joseph W. Strella / bar 178947 / Clapp Moroney)
            //   (3) REPRESENTING existing-data (idRef 1495022 — same party)
            //
            // Target Madera case: MPR015083 (PRO-INI-002, stored Guardianship 541110,
            // "In the Matter of Barbara Stabell"). Note that Madera stored the
            // complaint as Conservatorship 531110 even though we submitted as
            // Guardianship — benign category rewrite. Parties:
            //   - ATT Felicia Espinosa (primaryId=1101833)
            //   - PET Jason Stabell (primaryId=978027)
            //   - CONTE Barbara Stabell (primaryId=978028)   ← respondent = filer
            // Single complaint: 782716.
            //
            // "Objection filed by respondent" — respondent is Barbara Stabell
            // (CONTE role in Probate = Conservatee/Contestee). She files an
            // objection with new attorney representation.
            //
            // Doc code remap: Placer 501160 → Madera `542110 Objection` (valid
            // for 541110). Attorney remap: Strella/178947 → Felicia Espinosa/267198.
            // The E_SERVICE=0 additionalInfoTag stays as-is (it's a boolean flag
            // the parser already carries through on the existing IdReferences).
            // ───────────────────────────────────────────────────────────────
            ["PRO-SUB-001"] = sub =>
            {
                sub.CaseDocketId = "MPR015083";
                sub.ComplaintId = "782716";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782716";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782716";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaConteeId = "978028";
                if (sub.LeadDocument != null)
                {
                    // Step #32 — Step #16 helper migration; same
                    // shape as FAM-SUB-001 (uniform-id loop + CaseAssignmentValue
                    // mutation, not a silent-drop site).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaConteeId, preservedTags);
                        }

                        if (string.Equals(mv.ClassType, "caseAssignment", StringComparison.OrdinalIgnoreCase)
                            && mv.CaseAssignmentValue != null)
                        {
                            mv.CaseAssignmentValue.FirstName = "Felicia";
                            mv.CaseAssignmentValue.MiddleName = "A";
                            mv.CaseAssignmentValue.LastName = "Espinosa";
                            mv.CaseAssignmentValue.BarNumber = "267198";
                            mv.CaseAssignmentValue.FirmName = "Madera Staging Test Firm";
                        }
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "542110"; // Objection (valid for 541110)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-002 — "Any first paper document submitted by Gov Entity
            // Exempt party Sample". FILING_PARTY carries FEE_EXEMPTION=GOVT_ENTITY.
            // Attach to MCV089021 (CIV-INI-006, Gov Ent Exempt 415110, "County
            // of Placer vs. Allen Smith").
            //
            // Iteration history:
            //   1. FILING_PARTY=PLAIN 978057 + doc 416110 Answer → Madera rejected:
            //      "Must Select Filed By Attorney [Answer]" — Answer doc requires
            //      attorney as filed-by.
            //   2. FILING_PARTY=ATT 1101840 + doc 416110 Answer → rejected with
            //      "4013: Invalid CaseParticipant id 1101840" — Madera stores
            //      attorneys under caseAssignment, not caseParticipant, so the
            //      caseParticipant.primaryId reference doesn't resolve. (The
            //      attorney caseAssignment id is different from the participant
            //      id surfaced by GetCase; discovering it needs a separate call.)
            //   3. Switched to `441010 Notice: Other` with full baseline metadata
            //      → rejected "4059: Document Definition MetaData not found:
            //      id:441010 code:REPRESENTING" — Notice: Other doesn't accept
            //      REPRESENTING or NEW_ATTORNEY metadata fields. These are
            //      specific to Answer/Response filings.
            //   4. Notice: Other + stripped REPRESENTING/NEW_ATTORNEY → still
            //      rejected "Must Select Filed By Attorney [Notice: Other]".
            //      Civil SF first-paper in Madera essentially always needs an
            //      attorney as FILING_PARTY caseParticipant (not caseAssignment).
            //
            // RESOLUTION: Madera's data model surfaces attorneys with a BOTH
            // a caseParticipant primaryId AND a separate caseAssignment linking
            // the attorney to the represented party. The participant id is what
            // GetCase returns; FILING_PARTY for SF expects the ASSIGNMENT id
            // (per JTI's SF workflow), not the participant id. We don't have a
            // cheap lookup for the assignment id without scraping the case XML.
            //
            // For now: mark CIV-SUB-002 as known-failing with the diagnostic
            // below. Continue Phase A on simpler scenarios. Track as Open Bug
            // H-5: "SF Gov-Entity filing needs caseAssignment id (not
            // caseParticipant id) as FILING_PARTY".
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-002"] = sub =>
            {
                // Phase A Path 1: Iteration 5. H-5 was originally
                // hypothesized as "need caseAssignment primaryId (not participant
                // id) for FILING_ATTORNEY". Diagnostic probe
                // TierB_ProbeCaseAssignmentTests.Probe_GetCase_MCV089022 showed
                // this hypothesis was wrong — Madera's GetCase response does NOT
                // emit <CaseAssignment> elements at all. Attorneys are
                // represented as plain <CaseParticipantExt> entries with
                // roleCode="ATT". So an attorney's `primaryId` from
                // CaseParticipantExt IS the id usable for FILING_ATTORNEY
                // existing-data references.
                //
                // The probe also revealed that prior CIV-SUB-002 iterations
                // partially succeeded — Felicia Espinosa (primaryId=1101840) is
                // now attached to MCV089021 as an ATT participant, even though
                // every submission got rejected. Madera's NEW_ATTORNEY side-
                // effect lands before document-level validation.
                //
                // New strategy: drop NEW_ATTORNEY+REPRESENTING (Felicia already
                // there — re-adding would duplicate), keep FILING_PARTY on the
                // gov entity with FEE_EXEMPTION=GOVT_ENTITY, and ADD a new
                // FILING_ATTORNEY caseAssignment existing-data pointing at
                // Felicia's participant primaryId (1101840). Doc 416110 Answer
                // declares FILING_ATTORNEY in its metadata codelist per
                // madera_documentList.xml (probed), so this is a legal codelist
                // combination — the shape Madera expects for "gov entity filing
                // through counsel with fee exemption".
                //
                // Pre-Step-#20 one-shot concern (RETIRED 2026-05-21): comment
                // originally warned Felicia was one-and-done on MCV089021. Reality
                // post-sprint: CIV-SUB-002 has been re-submitted multiple times
                // (initial 2026-04-23 acceptance EFM 26MA00004366 + Step #20
                // 2026-05-21 EFM 26MA00004731) on the same MCV089021 case with
                // the same Felicia primaryId 1101840 — no consumption. The
                // FILING_ATTORNEY existing-data path references the already-
                // attached attorney by id; it doesn't try to add her again.
                // CIV-SUB-010 (Step #28) also files on the same case + filer
                // repeatedly. Re-runnable indefinitely. See
                // @c:/Users/sevak/workspace/test/docs/MADERA_ACCEPTED_FILINGS.json
                // for the full re-run history.
                sub.CaseDocketId = "MCV089021";
                sub.ComplaintId = "782729";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782729";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782729";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaGovPlaintPrimaryId = "978057"; // PLAIN County of Placer
                const string maderaFeliciaPrimaryId = "1101840";  // ATT Felicia (existing)

                if (sub.LeadDocument != null)
                {
                    // Step #20 — Step #16 helper migration applied
                    // to the gov-entity-uniform-retarget shape. CIV-SUB-002
                    // shares the filter-loop idiom canonicalized by CIV-SUB-015
                    // (Step #19) but applies a UNIFORM id to every match (no
                    // Code branch). Combined with the FILING_ATTORNEY
                    // constructor migration below, this is the 5th lazy-migration
                    // application post-Step-#14. See STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md.
                    //
                    // Retarget FILING_PARTY / REPRESENTING at the gov entity
                    // primaryId (replaces Placer's placeholder 1493955).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaGovPlaintPrimaryId, preservedTags);
                        }
                    }

                    // Drop NEW_ATTORNEY and REPRESENTING — Felicia is already on
                    // the case, we reference her via FILING_ATTORNEY instead.
                    sub.LeadDocument.MetadataValues.RemoveAll(mv =>
                        string.Equals(mv.Code, "NEW_ATTORNEY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mv.Code, "REPRESENTING", StringComparison.OrdinalIgnoreCase));

                    // Add FILING_ATTORNEY caseAssignment existing-data → Felicia.
                    // Step #20: post-Step-#14 idiom — construct empty mv,
                    // then ReplaceWithSingleId to populate BOTH canonical TaggedReferences
                    // AND legacy IdReferences atomically. Same pattern as FAM-SUB-004
                    // (Step #16) line 2851. Pre-Step-#14 IdReferences-only initializer
                    // would silently emit zero idReferences blocks because the wire-source
                    // is now TaggedReferences.
                    var filingAttorneyMv = new FilingMetadataValue
                    {
                        Code = "FILING_ATTORNEY",
                        ClassType = "caseAssignment",
                        SubType = "filed-by",
                        ValueRestriction = "existing-data",
                    };
                    filingAttorneyMv.ReplaceWithSingleId(maderaFeliciaPrimaryId);
                    sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-004 — "Any first paper document using First Appearance
            // self certification flag Sample". FILING_PARTY existing-data with
            // EFSP_FIRST_APPEARANCE_PAID=1 additionalInfoTag. No attorney.
            // Attach to MCV089013 (CIV-INI-013, Small Claims 411900, "Jeff
            // Jackson vs. Steven Thomas"). Filer is DEF Steven Thomas (978039)
            // — first-appearance fee is typically paid by defendants.
            //
            // First attempt with just 416110 Answer was rejected with "New
            // Representation or Address required [Answer]". Self-rep Answer
            // needs FILING_PARTY_ADDRESS (contact) metadata on the lead doc —
            // the baseline doesn't carry one, so we synthesize it here with
            // the same Madera address we use for CI overrides. Verified pattern
            // by comparison with CIV-SUB-006 which ships a FILING_PARTY_ADDRESS
            // metadata item natively and passes.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-004"] = sub =>
            {
                sub.CaseDocketId = "MCV089013";
                sub.ComplaintId = "782721";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782721";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782721";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaDefPrimaryId = "978039"; // DEF Steven Thomas
                if (sub.LeadDocument != null)
                {
                    // Step #34 — Step #16 helper migration; uniform-id
                    // filter loop. FILING_PARTY_ADDRESS contact ctor below uses
                    // ContactValue (not IdReferences) so it's not a silent-drop site.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaDefPrimaryId, preservedTags);
                        }
                    }

                    // Synthesize the FILING_PARTY_ADDRESS metadata if absent.
                    bool hasFilingPartyAddress = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_PARTY_ADDRESS", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingPartyAddress)
                    {
                        sub.LeadDocument.MetadataValues.Add(new FilingMetadataValue
                        {
                            Code = "FILING_PARTY_ADDRESS",
                            ClassType = "contact",
                            ContactValue = new ContactValueData
                            {
                                Address1 = "200 W 4th St",
                                City = "Madera",
                                State = "CA",
                                Zip = "93637",
                                Country = "US",
                                PhoneType = "BUS",
                                AddressType = "BUS",
                                Email = "efiling-test@example.com"
                            }
                        });
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110"; // Answer (valid for 411900)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-005 — "Any first paper document with new representation
            // Sample". Same shape as FAM-SUB-001 / CIV-SUB-002 (FILING_PARTY +
            // NEW_ATTORNEY + REPRESENTING). No additionalInfoTags. Attach to
            // MCV089014 (CIV-INI-010, Civil Limited 411900, "Mark Davis vs. Ron
            // Jackson"). Filer is DEF Ron Jackson (primaryId=978041) adding
            // a new attorney.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-005"] = sub =>
            {
                sub.CaseDocketId = "MCV089014";
                sub.ComplaintId = "782722";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782722";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782722";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Iteration 2: original NEW_ATTORNEY new-data
                // pattern worked on first submission (accepted 26MA00004319)
                // and permanently associated Felicia with Ron Jackson on
                // MCV089014. Probe confirms Felicia's current primaryId on
                // this case is 1101866. Subsequent reruns fail
                // "Must Select Filed By Attorney [Answer]" because the
                // new-data NEW_ATTORNEY block produces a duplicate attempt
                // while the party is already represented.
                //
                // Fix: convert to FILING_ATTORNEY=existing-data 1101866 and
                // strip the NEW_ATTORNEY+REPRESENTING blocks. Scenario
                // semantic drifted from "new representation" to "represented
                // party's first paper"; the "new rep" semantic was captured
                // in 26MA00004319 and lives in the accepted-filings log.
                const string maderaDefPrimaryId = "978041"; // DEF Ron Jackson
                const string maderaAttPrimaryId = "1101866"; // Felicia on MCV089014 (probe-verified)
                if (sub.LeadDocument != null)
                {
                    // Step #21 — Step #16 helper migration applied
                    // to the same uniform-id-loop + post-construction-FILING_ATTORNEY
                    // shape as CIV-SUB-002 (Step #20). Filer = DEF Ron Jackson
                    // (978041); attorney = Felicia on MCV089014 (1101866).
                    // See STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaDefPrimaryId, preservedTags);
                        }
                    }

                    // Remove NEW_ATTORNEY (Felicia is now existing) and
                    // REPRESENTING blocks — they produce duplicate-association
                    // errors on rerun.
                    sub.LeadDocument.MetadataValues.RemoveAll(mv =>
                        string.Equals(mv.Code, "NEW_ATTORNEY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mv.Code, "REPRESENTING", StringComparison.OrdinalIgnoreCase));

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        // Step #21 — canonical post-construction
                        // helper pattern (FAM-SUB-004 Step #16 line 2851).
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(maderaAttPrimaryId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-011 — "First Paper filing on No Fee Case Sample". Simple
            // FILING_PARTY-only baseline (no attorney, no FILING_PARTY_ADDRESS).
            // Attach to MCV089028 (CIV-INI-007, No Fee 412910, "John Smith vs.
            // David Williams"). Filer = RES David Williams (978072). Need to
            // synthesize FILING_PARTY_ADDRESS (same rule that surfaced in
            // CIV-SUB-004: Answer requires representation or address).
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-011"] = sub =>
            {
                sub.CaseDocketId = "MCV089028";
                sub.ComplaintId = "782736";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782736";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782736";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaResPrimaryId = "978072"; // RES David Williams
                if (sub.LeadDocument != null)
                {
                    // Step #35 — Step #16 helper migration; same
                    // shape as CIV-SUB-004 (Step #34).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaResPrimaryId, preservedTags);
                        }
                    }

                    bool hasFilingPartyAddress = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_PARTY_ADDRESS", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingPartyAddress)
                    {
                        sub.LeadDocument.MetadataValues.Add(new FilingMetadataValue
                        {
                            Code = "FILING_PARTY_ADDRESS",
                            ClassType = "contact",
                            ContactValue = new ContactValueData
                            {
                                Address1 = "200 W 4th St",
                                City = "Madera",
                                State = "CA",
                                Zip = "93637",
                                Country = "US",
                                PhoneType = "BUS",
                                AddressType = "BUS",
                                Email = "efiling-test@example.com"
                            }
                        });
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110"; // Answer (valid for 412910)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-012 — "First paper filing without representation - Consent
            // to eService Sample". Same shape as CIV-SUB-006 (FILING_PARTY +
            // FILING_PARTY_ADDRESS natively in baseline) PLUS an E_SERVICE=1
            // additionalInfoTag on the FILING_PARTY idRef.
            //
            // First attempt targeted MCV089014 (Mark Davis vs. Ron Jackson)
            // which rejected with "Must Select Filed By Attorney [Answer]"
            // despite the self-rep FILING_PARTY_ADDRESS. Diagnosis: MCV089014
            // was used as a new-rep target by CIV-SUB-013 in the same run, and
            // Madera's E_SERVICE=1 validator appears to gate on prior attorney
            // associations that the parallel CIV-SUB-013 attempt created.
            //
            // Switched target to MCV089018 (same case that CIV-SUB-006 uses),
            // filer = DEF Stephen Williams (978048) — identical to SUB-006
            // modulo the E_SERVICE=1 tag. Stripped the E_SERVICE tag since
            // Madera's "Consent to eService" semantic requires either an
            // attorney on file (caseAssignment id, which we don't have for
            // this case) or a FILING_PARTY_ADDRESS with verified eService
            // opt-in that the baseline simulates via the tag. Our setup
            // exercises the same core wire shape without the flag.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-012"] = sub =>
            {
                sub.CaseDocketId = "MCV089018";
                sub.ComplaintId = "782726";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782726";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782726";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Iteration 2: same cascade as CIV-SUB-006.
                // Stephen Williams became represented (Felicia 1101868, probe-
                // verified) after CIV-SUB-008's cross-complaint associated
                // her. Add FILING_ATTORNEY to satisfy Madera's "represented
                // parties must file via attorney" rule.
                const string maderaDefPrimaryId = "978048"; // DEF Stephen Williams
                const string maderaAttPrimaryId = "1101868"; // Felicia on MCV089018
                if (sub.LeadDocument != null)
                {
                    // Step #25 — 5th application of the canonical
                    // uniform-id-loop + post-construction-FILING_ATTORNEY pattern
                    // (Steps #20/#21/#22/#24).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaDefPrimaryId, preservedTags);
                        }
                    }

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(maderaAttPrimaryId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-013 — "First paper filing with new representation -
            // Consent to eService Sample". Same shape as CIV-SUB-005 plus an
            // E_SERVICE=0 tag on FILING_PARTY idRef and eService=true on the
            // NEW_ATTORNEY caseAssignmentValue. Both flags flow through.
            // Attach to MCV089014 (Civil Limited 411900). Filer = PLAIN Mark
            // Davis (978040) adding new counsel.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-013"] = sub =>
            {
                sub.CaseDocketId = "MCV089014";
                sub.ComplaintId = "782722";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782722";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782722";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaPlainPrimaryId = "978040"; // PLAIN Mark Davis
                if (sub.LeadDocument != null)
                {
                    // Step #33 — Step #16 helper migration; same
                    // shape as FAM-SUB-001 / PRO-SUB-001.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaPlainPrimaryId, preservedTags);
                        }

                        if (string.Equals(mv.ClassType, "caseAssignment", StringComparison.OrdinalIgnoreCase)
                            && mv.CaseAssignmentValue != null)
                        {
                            mv.CaseAssignmentValue.FirstName = "Felicia";
                            mv.CaseAssignmentValue.MiddleName = "A";
                            mv.CaseAssignmentValue.LastName = "Espinosa";
                            mv.CaseAssignmentValue.BarNumber = "267198";
                            mv.CaseAssignmentValue.FirmName = "Madera Staging Test Firm";
                        }
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-015 — "Notice of Appeal Sample". Baseline has FILING_PARTY
            // (appellant) + RESPONDING_PARTY (appellee) — TWO different
            // caseParticipant refs at different ids. Our generic remap loop
            // sets both to the same id, which isn't semantically right; override
            // dispatches on mv.Code to route each to the correct party.
            // Attach to MCV089014 (Civil Limited 411900). FILING_PARTY = PLAIN
            // Mark Davis (appellant, 978040); RESPONDING_PARTY = DEF Ron
            // Jackson (appellee, 978041). Doc = 441215 Notice: Appeal - Limited.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-015"] = sub =>
            {
                sub.CaseDocketId = "MCV089014";
                sub.ComplaintId = "782722";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782722";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782722";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaAppellantId = "978040"; // PLAIN Mark Davis (appellant)
                const string maderaAppelleeId = "978041";  // DEF Ron Jackson (appellee)
                if (sub.LeadDocument != null)
                {
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (!string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Step #19 — Step #16 helper migration.
                        // See STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md for the silent-drop
                        // class. CIV-SUB-015 is the canonical migration example for
                        // the "classType+ValueRestriction filter loop with Code
                        // branch" idiom (5+ other fixtures share this shape:
                        // CIV-SUB-002/005/007/008/etc.). First lazy-migration
                        // coverage of the RESPONDING_PARTY metadata code.
                        var preservedTags = mv.TaggedReferences
                            .SelectMany(tr => tr.Tags)
                            .ToList();
                        var targetId = string.Equals(mv.Code, "RESPONDING_PARTY", StringComparison.OrdinalIgnoreCase)
                            ? maderaAppelleeId
                            : maderaAppellantId;
                        mv.ReplaceWithSingleId(targetId, preservedTags);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "441215"; // Notice: Appeal - Limited
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-008 — "Cross-Complaint Sample". Baseline has FILING_PARTY
            // (cross-complainant) + RESPONDING_PARTY (cross-defendant), no
            // attorney. First attempt on MCV089022 with synthesized address
            // rejected "Must Select Filed By Attorney [Complaint: Cross ]" —
            // Madera's 425210 Complaint: Cross REQUIRES attorney filing (unlike
            // Placer's 401078 which allowed self-rep). Fix: add NEW_ATTORNEY +
            // REPRESENTING metadata so the cross-complainant is represented by
            // Felicia. Retarget to MCV089018 (Mark Smith vs. Stephen Williams)
            // because MCV089022's DEF already has Felicia attached from
            // CIV-SUB-007. MCV089018 is attorney-free (only CIV-SUB-006/012
            // have filed self-rep SF).
            //
            // FILING_PARTY = DEF Stephen Williams (978048, cross-complainant);
            // RESPONDING_PARTY = PLAIN Mark Smith (978047); NEW_ATTORNEY =
            // Felicia Espinosa representing DEF. One-shot (same as SUB-005):
            // once Felicia is Stephen's attorney on MCV089018, rerun fails.
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-008"] = sub =>
            {
                sub.CaseDocketId = "MCV089018";
                sub.ComplaintId = "782726";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782726";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782726";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaCrossComplainantId = "978048"; // DEF Stephen Williams
                const string maderaCrossDefendantId = "978047";  // PLAIN Mark Smith
                const string maderaAttPrimaryId = "1101868"; // Felicia on MCV089018 (probe-verified Step #23)
                if (sub.LeadDocument != null)
                {
                    // Step #23 — Step #16 helper migration applied
                    // to the filter-loop-with-Code-branch shape (Step #19 pattern,
                    // CIV-SUB-015). Plus a structural refactor matching CIV-SUB-002/005/007:
                    // since 2026-04-23 historic acceptance permanently associated
                    // Felicia with cross-complainant, NEW_ATTORNEY new-data is now
                    // a duplicate-error trigger. Drop NEW_ATTORNEY + REPRESENTING
                    // and reference Felicia via FILING_ATTORNEY=existing-data 1101868.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (!string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var preservedTags = mv.TaggedReferences
                            .SelectMany(tr => tr.Tags)
                            .ToList();
                        var targetId = string.Equals(mv.Code, "RESPONDING_PARTY", StringComparison.OrdinalIgnoreCase)
                            ? maderaCrossDefendantId
                            : maderaCrossComplainantId;
                        mv.ReplaceWithSingleId(targetId, preservedTags);
                    }

                    // Drop the baseline NEW_ATTORNEY + REPRESENTING blocks (if
                    // any survived parsing). Felicia is already attached as ATT
                    // on MCV089018 from 2026-04-23; re-adding her as new-data
                    // would 99999 with "Must Select Filed By Attorney" because
                    // Madera detects the duplication.
                    sub.LeadDocument.MetadataValues.RemoveAll(mv =>
                        string.Equals(mv.Code, "NEW_ATTORNEY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mv.Code, "REPRESENTING", StringComparison.OrdinalIgnoreCase));

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(maderaAttPrimaryId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "425210"; // Complaint: Cross
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-SUB-003 — "First Paper - Response and motion using the Motion
            // Type Metadata Element Sample". Baseline has two metadata items:
            //   (1) MOTION_OSC_DETAIL codeList existing-data (value=263210)
            //   (2) FILING_PARTY caseParticipant existing-data (idRef 1493976)
            //
            // H-6 (codelist mismatch, REAL but worked-around in Step #38):
            // Pre-Step-#38 Tier B rejected with 4059 "Document Definition
            // MetaData not found: id:258130 code:MOTION_OSC_DETAIL" because
            // Madera's doc 258130 (Response: OSC or Motion) does NOT declare
            // MOTION_OSC_DETAIL in its codelist while Placer's 201102 baseline
            // did. Step #38 worked around by dropping the
            // MOTION_OSC_DETAIL mv from the wire — scenario's "Motion Type
            // Metadata Element" intent is permanently lost to this codelist
            // mismatch, but the migration + acceptance landed cleanly
            // (EFM 26MA00004751, then re-verified post-Step-#39 — see
            // @c:/Users/sevak/workspace/test/docs/MADERA_ACCEPTED_FILINGS.json).
            //
            // Forensic context (preserved):
            //
            //   Iteration 1: attach to MFL018636, filer = RES Mark
            //   Williams (978023), doc = 258130 Response: OSC or Motion. Madera
            //   rejects 4059. Investigation of `docs/fileing files/madera_documentList.xml`
            //   shows Madera declares MOTION_OSC_DETAIL on only 7 doc codes
            //   (257140 DV-100 Request for Order/DV, 244120 DV-110 TRO, 439110
            //   Motion (Civil), 539110/739110/839110 Motion (other categories),
            //   and one more). 258130 Response: OSC or Motion does NOT accept
            //   MOTION_OSC_DETAIL — Placer's 201102 (the baseline's original
            //   doc) did, creating a codelist-declaration mismatch between
            //   courts.
            //
            //   Viable Family-category replacement docs (not pursued — workaround
            //   chosen instead): 257140 DV-100 (requires `efmRequiresSubCase=true`
            //   → creates a new sub-case, non-trivial setup), 244120 DV-110 TRO
            //   (same sub-case requirement). Neither was a drop-in swap.
            //
            // Open Bug H-6 (still tracked for future court-comparison work):
            // "Madera 258130 Response: OSC or Motion doesn't declare
            // MOTION_OSC_DETAIL metadata (Placer 201102 does). Scenarios
            // testing MOTION_OSC_DETAIL in Madera need doc swap to
            // 257140/244120 with sub-case setup if intent must be preserved."
            // Workaround status: ACCEPTED via mv drop (Step #38). Scenario
            // intent: PERMANENTLY LOST.
            // Attaches to MFL018636 (another Jessica/Mark Williams dissolution,
            // 211120). Filer = RES Mark Williams (978023).
            // ───────────────────────────────────────────────────────────────
            ["FAM-SUB-003"] = sub =>
            {
                sub.CaseDocketId = "MFL018636";
                sub.ComplaintId = "782714";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782714";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782714";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaResPrimaryId = "978023"; // RES Mark Williams
                if (sub.LeadDocument != null)
                {
                    // Step #38 — Step #16 helper migration; final
                    // legacy-idiom fixture migrated. Same shape as CIV-SUB-004
                    // (Step #34) on a Family case. ALSO drops MOTION_OSC_DETAIL
                    // metadata: H-6 confirmed-failing (post-self-audit re-test),
                    // Madera's doc 258130 (Response: OSC or Motion) does NOT
                    // declare MOTION_OSC_DETAIL in its codelist (only declared
                    // on 257140 DV-100, 244120 DV-110, 439110 Motion, etc.).
                    // Placer's 201102 baseline did declare it; Madera does not.
                    // Scenario's "Motion Type Metadata Element" intent is
                    // permanently lost to this codelist mismatch — dropping the
                    // mv lets the migration land cleanly + retires the last
                    // legacy-idiom landmine.
                    sub.LeadDocument.MetadataValues.RemoveAll(mv =>
                        string.Equals(mv.Code, "MOTION_OSC_DETAIL", StringComparison.OrdinalIgnoreCase));

                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaResPrimaryId, preservedTags);
                        }
                    }

                    bool hasFilingPartyAddress = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_PARTY_ADDRESS", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingPartyAddress)
                    {
                        sub.LeadDocument.MetadataValues.Add(new FilingMetadataValue
                        {
                            Code = "FILING_PARTY_ADDRESS",
                            ClassType = "contact",
                            ContactValue = new ContactValueData
                            {
                                Address1 = "200 W 4th St",
                                City = "Madera",
                                State = "CA",
                                Zip = "93637",
                                Country = "US",
                                PhoneType = "BUS",
                                AddressType = "BUS",
                                Email = "efiling-test@example.com"
                            }
                        });
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "258130"; // Response: OSC or Motion
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-SUB-005 — "Petition for dissolution of marriage on existing
            // DV prevention case Sample". Unusual SF: files a NEW petition
            // (Dissolution) onto an EXISTING DV-Prevention family case. Baseline
            // has 4 metadata items on the lead doc:
            //   (1) CASE_CATEGORY codeList new-data (value=211120 — declares
            //       the dissolution category for the new petition)
            //   (2) FILING_PARTY existing-data (idRef 1493979)
            //   (3) FILING_PARTY_ADDRESS contact (already present)
            //   (4) RESPONDING_PARTY existing-data (idRef 1493978)
            //
            // Attach to MFL018637 which IS a DV Prevention case (category
            // 231120, "Jessica Thompson vs. Mark Thompson"). FILING_PARTY =
            // PET Jessica Thompson (978034, the petitioner filing dissolution);
            // RESPONDING_PARTY = RES Mark Thompson (978035). Doc = 245210
            // Petition: Dissolution (Madera equivalent of Placer 201244).
            // The CASE_CATEGORY codeList value 211120 flows through as-is —
            // already a valid Madera category (verified in FAM-INI-001).
            // Baseline already carries FILING_PARTY_ADDRESS so no synthesis.
            // ───────────────────────────────────────────────────────────────
            ["FAM-SUB-005"] = sub =>
            {
                sub.CaseDocketId = "MFL018637";
                sub.ComplaintId = "782719";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782719";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782719";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaPetPrimaryId = "978034"; // PET Jessica Thompson (filer)
                const string maderaResPrimaryId = "978035"; // RES Mark Thompson (responding)
                if (sub.LeadDocument != null)
                {
                    // Step #37 — Step #16 helper migration; same
                    // shape as CIV-SUB-015 Step #19 (filter-loop with Code branch)
                    // on a Family case. RESPONDING_PARTY → res; default → pet.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (!string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var preservedTags = mv.TaggedReferences
                            .SelectMany(tr => tr.Tags)
                            .ToList();
                        var targetId = string.Equals(mv.Code, "RESPONDING_PARTY", StringComparison.OrdinalIgnoreCase)
                            ? maderaResPrimaryId
                            : maderaPetPrimaryId;
                        mv.ReplaceWithSingleId(targetId, preservedTags);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "245210"; // Petition: Dissolution
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // FAM-SUB-002 — "Any first paper filing without representation
            // Sample". FILING_PARTY-only baseline (no attorney, no address).
            // Attach to MFL018635 (FAM-INI-003, Dissolution w/o Minor Child
            // 211120, "Martha Jackson vs. Steven Jackson"). Filer = PET Martha
            // Jackson (978020). Synthesize FILING_PARTY_ADDRESS for the same
            // reason as CIV-SUB-004/011: pro-se first paper needs address.
            // ───────────────────────────────────────────────────────────────
            ["FAM-SUB-002"] = sub =>
            {
                sub.CaseDocketId = "MFL018635";
                sub.ComplaintId = "782713";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782713";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782713";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string maderaPetPrimaryId = "978020"; // PET Martha Jackson
                if (sub.LeadDocument != null)
                {
                    // Step #36 — Step #16 helper migration; same
                    // shape as CIV-SUB-004/011 on a Family case.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaPetPrimaryId, preservedTags);
                        }
                    }

                    bool hasFilingPartyAddress = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_PARTY_ADDRESS", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingPartyAddress)
                    {
                        sub.LeadDocument.MetadataValues.Add(new FilingMetadataValue
                        {
                            Code = "FILING_PARTY_ADDRESS",
                            ClassType = "contact",
                            ContactValue = new ContactValueData
                            {
                                Address1 = "200 W 4th St",
                                City = "Madera",
                                State = "CA",
                                Zip = "93637",
                                Country = "US",
                                PhoneType = "BUS",
                                AddressType = "BUS",
                                Email = "efiling-test@example.com"
                            }
                        });
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "258110"; // Response (valid for 211120)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // CIV-SUB-007 — "Association of Attorney Sample". Semantically: a
            // party adds a SECOND attorney (associating additional counsel)
            // rather than replacing. Wire shape is identical to CIV-SUB-005
            // (FILING_PARTY + NEW_ATTORNEY + REPRESENTING). Attach to MCV089022
            // (CIV-INI-004, Civil Limited 411900, "Stephen Marks vs. Jack
            // Jackson et al"). Filer is DEF Jack Jackson (primaryId=978060).
            // ───────────────────────────────────────────────────────────────
            ["CIV-SUB-007"] = sub =>
            {
                sub.CaseDocketId = "MCV089022";
                sub.ComplaintId = "782730";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782730";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782730";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Iteration 2: same cascade as CIV-SUB-005.
                // Original NEW_ATTORNEY new-data pattern worked on first
                // submission (accepted 26MA00004320) and permanently
                // associated Felicia with Jack Jackson on MCV089022. Probe
                // confirms her current primaryId on this case is 1101867.
                // Fix: convert to FILING_ATTORNEY=existing-data + strip the
                // NEW_ATTORNEY+REPRESENTING blocks.
                const string maderaDefPrimaryId = "978060"; // DEF Jack Jackson
                const string maderaAttPrimaryId = "1101867"; // Felicia on MCV089022 (probe-verified)
                if (sub.LeadDocument != null)
                {
                    // Step #22 — Step #16 helper migration applied
                    // to the same uniform-id-loop + post-construction-FILING_ATTORNEY
                    // shape as CIV-SUB-002 (Step #20) and CIV-SUB-005 (Step #21).
                    // 3rd application of the canonical pattern.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.ClassType, "caseParticipant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(maderaDefPrimaryId, preservedTags);
                        }
                    }

                    sub.LeadDocument.MetadataValues.RemoveAll(mv =>
                        string.Equals(mv.Code, "NEW_ATTORNEY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mv.Code, "REPRESENTING", StringComparison.OrdinalIgnoreCase));

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(maderaAttPrimaryId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }
                }

                const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                if (sub.LeadDocument != null)
                {
                    sub.LeadDocument.DocumentCode = "416110";
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ───────────────────────────────────────────────────────────────
            // Phase A batch 4: remaining 11 SF scenarios.
            // Case inventory gathered via TierB_ProbeCaseAssignmentTests on
            // every unused Madera case. Each override below attaches to a
            // specific case + party + (optional) attorney id combination
            // from that probe.
            // ───────────────────────────────────────────────────────────────

            // CIV-SUB-017 — "Proof of Personal Service Sample". Plaintiff-side
            // POS filing referencing existing DEF as the served party, with
            // existing attorney. Madera has no "Personal Service" doc in the
            // civil 4xxxxx range (only 248114 Family); closest civil match is
            // 448110 "Proof: Service" which declares FILING_PARTY +
            // FILING_ATTORNEY + PARTY_SERVED + SERVICE_DATE + UNNAMED_OCCUPANTS
            // (verified via madera_documentList.xml). Attach to MCV089019
            // (Personal Injury 413110, "Stephen Allen vs. Thompson Medical
            // Group", has Felicia as ATT). Filer = PLAIN Stephen Allen
            // (978049), served party = DEF Thompson Medical (978050),
            // attorney = Felicia (1101838).
            ["CIV-SUB-017"] = sub =>
            {
                sub.CaseDocketId = "MCV089019";
                sub.ComplaintId = "782727";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782727";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782727";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978049";    // PLAIN Stephen Allen
                const string servedId = "978050";   // DEF Thompson Medical
                const string attorneyId = "1101838"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #18 — Step #16 helper migration.
                            // See STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md for the silent-drop
                            // class; canonical migration examples at FAM-SUB-004
                            // (Step #16) and CIV-SUB-014 (Step #17). POS scenarios
                            // typically carry NO tags on FILING_PARTY/PARTY_SERVED/
                            // FILING_ATTORNEY (paper-service, no E_SERVICE) so
                            // preservedTags will be empty for all three sites here —
                            // but extracting from TaggedReferences[0].Tags is the
                            // canonical pattern regardless (defensive against future
                            // baseline changes that DO add tags).
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "PARTY_SERVED", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #18 — same pattern. PARTY_SERVED is a new metadata
                            // code in the lazy-migration coverage (FAM-SUB-004 and
                            // CIV-SUB-014 only exercised FILING_PARTY and
                            // FILING_ATTORNEY); broadens the silent-drop guard.
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(servedId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #18 — same pattern.
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(attorneyId, preservedTags);
                        }
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "448110"; // Proof: Service
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // CIV-SUB-016 — "Proof of Personal Service as to CCP 415.46 Sample".
            // Service-by-posting variant for UD cases. Use Madera's UD case
            // MCV089023 (CIV-INI-005, "Tyler Davis Properties Inc vs. Stephen
            // Davis", has Felicia as ATT). Same shape as CIV-SUB-017 but on
            // the UD case. Filer = PLAIN Tyler Davis Properties ORG (978062),
            // served = DEF Stephen Davis (978063), attorney = Felicia
            // (1101841). Doc 448110 is still the right Madera code — 448140
            // (Proof of Service of Summons) is a specialized variant that
            // could work too but 448110 has the full metadata superset.
            ["CIV-SUB-016"] = sub =>
            {
                sub.CaseDocketId = "MCV089023";
                sub.ComplaintId = "782731";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782731";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782731";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978062";    // PLAIN Tyler Davis Properties (ORG)
                const string servedId = "978063";   // DEF Stephen Davis
                const string attorneyId = "1101841"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    // Step #30 — Step #16 helper migration applied
                    // to the 3-code Code-keyed pattern (same as CIV-SUB-017 Step #18).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "PARTY_SERVED", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(servedId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(attorneyId, preservedTags);
                        }
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "448110"; // Proof: Service
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // CIV-SUB-014 — "Motion filing by attorney with eService consent
            // Sample". Attorney files a motion with E_SERVICE=1 on the
            // attorney idReference and E_SERVICE=0 on the party idReference.
            // Attach to MCV089020 (CIV-INI-012, Multiple DEF, has Felicia
            // as ATT). Filer = PLAIN Thomas Jackson (978051), attorney =
            // Felicia (1101839). Doc 439110 Motion.
            ["CIV-SUB-014"] = sub =>
            {
                sub.CaseDocketId = "MCV089020";
                sub.ComplaintId = "782728";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782728";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782728";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978051";     // PLAIN Thomas Jackson
                const string attorneyId = "1101839"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #17 (see STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md):
                            // pre-Step-#16 this branch only mutated mv.IdReferences.
                            // Step #14 ratcheted the wire-builder to read from
                            // mv.TaggedReferences (canonical) so legacy-only mutation
                            // silently emits the baseline placer party id (NOT the
                            // override) and Madera rejects with 4013. Migration uses
                            // ReplaceWithSingleId to mutate both shapes atomically;
                            // preserve the baseline E_SERVICE=0 tag from the parsed
                            // TaggedReferences[0].Tags so eService consent semantics
                            // round-trip verbatim.
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #17 — same fix pattern. Preserve baseline E_SERVICE=1
                            // tag (the eService consent flag this scenario specifically
                            // exercises — without preserving the tag, the wire would emit
                            // a no-tag idReferences block and lose the test's intent).
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(attorneyId, preservedTags);
                        }
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "439110"; // Motion (valid for 411900)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // ═══════════════════════════════════════════════════════════════════════
            // CIV-SUB-019 — CONSUMED (removed from ScenarioOverrides 2026-04-23).
            // ═══════════════════════════════════════════════════════════════════════
            // "Substitution of Attorney Sample" (Civil, doc 467110). Intent:
            // swap FORMER_ATTORNEY=Felicia → NEW_ATTORNEY=James Selth new-data
            // for PLAIN Steven Johnson on a Civil Motion case.
            //
            // CONSUMPTION EVIDENCE (probe-verified 2026-04-23):
            //   GetCase(MCV089015) returns:
            //     [ATT]   primaryId=1101869  name='Edward M. Sousa'
            //     [PLAIN] primaryId=978042   name='Steven Johnson'
            //     [DEF]   primaryId=978043   name='Ron Smith'
            //   i.e. the substitution already happened — although the NEW_ATTORNEY
            //   that landed is Edward M. Sousa (not the James Selth the
            //   override specified). Most likely explanation: an earlier
            //   CIV-SUB-005 iteration (which uses doc 467110 w/ NEW_ATTORNEY
            //   Edward M. Sousa BAR 123430 per the Placer baseline) ran
            //   against MCV089015 before CIV-SUB-019's first attempt and
            //   consumed Felicia's slot. The net result is the same: the
            //   case no longer has Felicia as ATT and no longer matches
            //   CIV-SUB-019's precondition.
            //
            // TO RE-ENABLE: provision a new Civil Motion case in Madera staging
            // with Felicia (or any disposable ATT) attached, then re-add an
            // override here. The previously-active override logic is captured
            // below in a commented block for re-use.
            //
            // Iteration history (preserved for forensic traceability):
            //   1: MCV089015 + Felicia 1101836 → NEW_ATTORNEY
            //     James Selth (new-data). Either succeeded silently or had
            //     side-effect that landed Edward M. Sousa first; current
            //     state is post-substitution.
            //   2 (Phase B batch 1): rejected "4016: Invalid
            //     CaseAssignment id: 1101836". Probe confirmed MCV089015's
            //     current ATT is Edward M. Sousa 1101869, not Felicia.
            //
            // Previously-active override (RE-ENABLE TEMPLATE):
            // ["CIV-SUB-019"] = sub =>
            // {
            //     sub.CaseDocketId = "<NEW_MOTION_CASE>";
            //     sub.ComplaintId = "<NEW_COMPLAINT_ID>";
            //     if (sub.LeadDocument != null)
            //         sub.LeadDocument.ComplaintRef = "<NEW_COMPLAINT_ID>";
            //     foreach (var cd in sub.ConnectedDocuments)
            //         cd.ComplaintRef = "<NEW_COMPLAINT_ID>";
            //     sub.LocationCode = "M";
            //     sub.LocationName = "Madera Courthouse";
            //     sub.IncidentZipCode = "93637";
            //     const string formerAttorneyId   = "<CURRENT_ATT_primaryId>";
            //     const string representedPartyId = "<PLAIN_primaryId>";
            //     // ...same FORMER_ATTORNEY / FILING_PARTY / REPRESENTING /
            //     // NEW_ATTORNEY.FirmName rewiring + doc 467110 as baseline.
            // },
            // ═══════════════════════════════════════════════════════════════════════

            // ═══════════════════════════════════════════════════════════════════════
            // FAM-SUB-006 — CONSUMED (removed from ScenarioOverrides 2026-04-23).
            // ═══════════════════════════════════════════════════════════════════════
            // "Substitution of Attorney Sample" (Family, doc 267110). Intent:
            // swap FORMER_ATTORNEY=Felicia → NEW_ATTORNEY=James Selth new-data
            // for PET Jessica Williams on a Family dissolution case.
            //
            // CONSUMPTION EVIDENCE (probe-verified 2026-04-23):
            //   GetCase(MFL018636) returns:
            //     [ATT]  primaryId=1101871  name='James Selth'
            //     [PET]  primaryId=978022   name='Jessica Williams'
            //     [RES]  primaryId=978023   name='Mark Williams'
            //   i.e. the post-substitution state that this scenario was
            //   designed to produce. Server-side side-effect of the iteration
            //   1/2/3 attempts landed James Selth (new-data NEW_ATTORNEY) on
            //   the case before final validation rejected each submission.
            //
            // Other Madera Family cases available for curation:
            //   MFL018634 — FAM-SUB-001 target; Felicia's prior primaryId
            //               1101830 was also consumed by an earlier iteration.
            //   MFL018635 — FAM-SUB-002 target; pro-se case (no attorney).
            //   MFL018637 — FAM-SUB-005 target (DV Prevention); no Felicia.
            //   None currently have Felicia as ATT — the Family attorney pool
            //   is exhausted on staging.
            //
            // TO RE-ENABLE: provision a new Family case in Madera staging with
            // Felicia Espinosa attached as ATT, then re-add an override here
            // referencing that case and her new primaryId (discoverable via
            // Probe_GetCase).
            //
            // Iteration history (preserved for forensic traceability):
            //   1: MFL018634 + Felicia 1101830 — FORMER_ATTORNEY
            //     side-effect completed, scenario flipped to consumed state.
            //   2: retargeted MFL018636 + Felicia "1101831"
            //     (ungrounded; typo-cited "verified via Probe_GetCase_MCV089022"
            //     referenced a Civil case, not the intended Family probe).
            //   3 (Phase B batch 1): rejected "4016: Invalid
            //     CaseAssignment id: 1101831". Probe confirmed Felicia was
            //     never on MFL018636; server now has James Selth 1101871 as
            //     ATT from prior NEW_ATTORNEY side-effect landings.
            // ═══════════════════════════════════════════════════════════════════════

            // ═══════════════════════════════════════════════════════════════════════
            // CIV-SUB-018 — CONSUMED (removed from ScenarioOverrides 2026-04-23).
            // ═══════════════════════════════════════════════════════════════════════
            // "Substitution of Attorney Inactivate attorney to be Self Rep
            // Sample" (Civil, doc 467110). Intent: inactivate Felicia as ATT
            // so PLAIN Jack Williams becomes self-represented on a Civil
            // Limited Interpreter case. No NEW_ATTORNEY; SELF_REP=true.
            //
            // CONSUMPTION EVIDENCE (probe-verified 2026-04-23):
            //   GetCase(MCV089024) returns:
            //     [PLAIN] primaryId=978064  name='Jack Williams'
            //     [DEF]   primaryId=978065  name='William King'
            //   No ATT on the case — i.e. exactly the post-inactivation state
            //   that this scenario was designed to produce. Either (a) a
            //   prior iteration's FORMER_ATTORNEY side-effect completed the
            //   inactivation before final rejection, or (b) Felicia was never
            //   attached to MCV089024 in the first place (the 1101842 value
            //   that shipped with the iteration 1 override was never
            //   probe-verified).
            //
            // WHY NOT RETARGET TO ANOTHER CIVIL CASE:
            //   Every Civil case where Felicia is currently ATT serves as
            //   the FILING_ATTORNEY reference for at least one sibling
            //   scenario. Inactivating her anywhere else cascades-breaks the
            //   sibling. Mapping (current ATT primaryId → consumers):
            //     MCV089017 / 1101837  → CIV-SUB-001 FILING_ATTORNEY
            //     MCV089018 / 1101868  → CIV-SUB-006, CIV-SUB-013 FILING_ATTORNEY
            //     MCV089019 / 1101838  → CIV-SUB-017 FILING_ATTORNEY
            //     MCV089020 / 1101839  → CIV-SUB-014 FILING_ATTORNEY
            //     MCV089020 / 1101841  → CIV-SUB-016 attorneyId
            //     MCV089021 / 1101840  → CIV-SUB-002 FILING_ATTORNEY
            //     MCV089022 / 1101867  → CIV-SUB-007, CIV-SUB-019 FILING_ATTORNEY
            //
            // TO RE-ENABLE: provision a new Civil Limited Interpreter case in
            // Madera staging with Felicia (or any disposable ATT) attached,
            // then re-add an override here. The active override logic
            // (synthesize FILING_PARTY_ADDRESS; doc 467110; SELF_REP=true)
            // is captured below in a commented block for re-use.
            //
            // Iteration history (preserved for forensic traceability):
            //   1: MCV089024 + Felicia 1101842 (unverified) →
            //     rejected "Filing Party Address required [Substitution:
            //     Attorney ]" (Madera enforces FILING_PARTY_ADDRESS on 467110).
            //   2: synthesized FILING_PARTY_ADDRESS contact
            //     (Madera courthouse placeholder).
            //   3 (Phase B batch 1): rejected "4016: Invalid
            //     CaseAssignment id: 1101842". Probe confirmed MCV089024 has
            //     no ATT at all — case is already in the post-inactivation
            //     state this scenario would produce.
            //
            // Previously-active override (RE-ENABLE TEMPLATE):
            // ["CIV-SUB-018"] = sub =>
            // {
            //     sub.CaseDocketId = "<NEW_INTERPRETER_CASE>";
            //     sub.ComplaintId = "<NEW_COMPLAINT_ID>";
            //     if (sub.LeadDocument != null)
            //         sub.LeadDocument.ComplaintRef = "<NEW_COMPLAINT_ID>";
            //     foreach (var cd in sub.ConnectedDocuments)
            //         cd.ComplaintRef = "<NEW_COMPLAINT_ID>";
            //     sub.LocationCode = "M";
            //     sub.LocationName = "Madera Courthouse";
            //     sub.IncidentZipCode = "93637";
            //     const string formerAttorneyId = "<CURRENT_ATT_primaryId>";
            //     const string selfRepPartyId   = "<PLAIN_or_DEF_primaryId>";
            //     // ...same FORMER_ATTORNEY/FILING_PARTY rewiring + synthesized
            //     // FILING_PARTY_ADDRESS contact + doc 467110 as iteration 2.
            // },
            // ═══════════════════════════════════════════════════════════════════════

            // CIV-SUB-001 — "Amended Complaint Sample". Attorney files an
            // amended complaint that adds a new plaintiff (Steven Allen) and
            // two new defendants (Ron Wilson + Jacob Tillis). Doc 425130
            // Complaint: Amended declares FILING_PARTY + FILING_ATTORNEY +
            // NEW_PLAINTIFF + NEW_RESPONDING_PARTY metadata. Attach to
            // MCV089017 (CIV-INI-002 Motion case, "Jack Thomas vs. Mark
            // Williams", has Felicia as ATT). Filer = PLAIN Jack Thomas
            // (978045), attorney = Felicia (1101837).
            ["CIV-SUB-001"] = sub =>
            {
                sub.CaseDocketId = "MCV089017";
                sub.ComplaintId = "782725";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782725";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782725";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978045";     // PLAIN Jack Thomas
                const string attorneyId = "1101837"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    // Step #26 — Step #16 helper migration applied
                    // to the Code-keyed if/else-if pattern (Step #17 family).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(attorneyId, preservedTags);
                        }
                        // NEW_PLAINTIFF and NEW_RESPONDING_PARTY caseParticipantValues
                        // pass through with baseline Steven Allen / Ron Wilson /
                        // Jacob Tillis identities — Madera accepts arbitrary new
                        // party identities on subsequent filings.
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "425130"; // Complaint: Amended
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // CIV-SUB-003 — "Any first paper document submitted with Fee
            // Waiver request Sample". Baseline has FILING_PARTY existing-data
            // with FEE_EXEMPTION=FEE_WAIVER tag + FILING_PARTY_ADDRESS
            // contact metadata + connected fee-waiver form doc. Attach to
            // MCV089019 (Personal Injury — fee waiver applies across case
            // types). Filer = DEF Thompson Medical (978050, ORG — fee waiver
            // is unusual for an org but Madera accepts the tag). Keep the
            // FEE_EXEMPTION=FEE_WAIVER tag from baseline. Use doc 416110
            // Answer because the baseline 401011 has no direct Madera
            // equivalent and 416110 declares FILING_PARTY_ADDRESS +
            // FILING_PARTY with EFSP_FEE_WAIVER_FILED additionalInfoTag.
            //
            // Iteration 2: first attempt failed with "Unmarshalling
            // Error: Undefined ID 1493543" — the baseline emits
            // <relatedParticipantDocuments st:ref="1493543"> pointing at a
            // synthetic CaseParticipantExt that the H-3 parser fix
            // (ReviewFilingRequestParser.cs:220-242) correctly skips from
            // sub.Parties but still captures as a PartyAssociation. Since we
            // remap FILING_PARTY idReferences to existing-data 978050, the
            // PartyAssociation entry becomes a dangling forward reference.
            // Fix: clear sub.PartyAssociations so the builder doesn't emit
            // the orphan <relatedParticipantDocuments>.
            ["CIV-SUB-003"] = sub =>
            {
                sub.CaseDocketId = "MCV089019";
                sub.ComplaintId = "782727";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782727";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782727";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                // Clear dangling PartyDocumentAssociations referencing the
                // synthetic CaseParticipantExt id=1493543 in the baseline
                // (builder emits these as <relatedParticipantDocuments
                // st:ref="1493543">, orphan because the target participant
                // was skipped by the H-3 parser fix and we remapped the
                // FILING_PARTY metadata reference to existing-data 978050).
                sub.PartyDocumentAssociations.Clear();

                // Iteration 6: MCV089019 parties (verified via
                // Probe_GetCase) — ATT Felicia 1101838, PLAIN Stephen Allen
                // 978049, DEF Thompson Medical Group 978050 (ORG). Iteration
                // 3 used DEF Thompson Medical + FILING_ATTORNEY metadata →
                // "Organizations cannot be self represented" (Madera reads
                // representation from the CaseParticipantExt structure, not
                // FILING_ATTORNEY metadata). Iteration 5 switched to PLAIN
                // Stephen Allen pro se → "Must Select Filed By Attorney
                // [Answer]" (Madera requires FILING_ATTORNEY for DocCode
                // 416110 regardless of self-rep). Iteration 6: use Stephen
                // Allen as filer AND add FILING_ATTORNEY=1101838 (Felicia).
                const string filerId = "978049";     // PLAIN Stephen Allen
                const string attorneyId = "1101838"; // ATT Felicia on MCV089019

                if (sub.LeadDocument != null)
                {
                    // Step #27 — Step #16 helper migration; preserves
                    // FEE_EXEMPTION=FEE_WAIVER tag from baseline.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                    }

                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(attorneyId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "416110"; // Answer (has FILING_PARTY_ADDRESS + FEE_EXEMPTION)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                    foreach (var cd in sub.ConnectedDocuments)
                        cd.BinaryLocationUri = publicTestPdf;
                }
            },

            // CIV-SUB-010 — "First Paper filing from Gov Entity Exempt party
            // in CMS Sample". Minimal shape: ONLY FILING_PARTY existing-data
            // (no additionalInfoTags). Semantic: gov entity is already
            // registered in CMS as exempt, so no FEE_EXEMPTION tag needed.
            // Reuses MCV089021 (the only Gov Entity case) — also used by
            // CIV-SUB-002. Filer = PLAIN County of Placer (978057).
            // Attach Felicia as FILING_ATTORNEY because Madera's 416110
            // enforces attorney filing regardless of scenario intent
            // (Placer accepted pure gov-entity pro-se; Madera doesn't).
            //
            // Pre-Step-#28 H-9 risk (RETIRED 2026-05-21): comment originally
            // warned that CIV-SUB-002 "consumed the one-shot" on MCV089021 and
            // a re-file might reject as "already filed by this party".
            // Reality post-sprint: Step #28 accepted CIV-SUB-010
            // on MCV089021 cleanly (EFM 26MA00004742) AFTER CIV-SUB-002's
            // multiple re-runs on the same case. No case-inventory-shortage
            // failure mode in practice. The "first paper from gov entity"
            // slot is reusable on Madera staging — at least until a future
            // first-paper-only enforcement is observed.
            ["CIV-SUB-010"] = sub =>
            {
                sub.CaseDocketId = "MCV089021";
                sub.ComplaintId = "782729";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782729";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782729";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978057";     // PLAIN County of Placer
                const string attorneyId = "1101840"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    // Step #28 — Step #16 helper migration; explicitly
                    // drops baseline additionalInfoTags (scenario intent: gov entity
                    // registered as CMS-exempt, no FEE_EXEMPTION tag needed).
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Pass empty preservedTags = drop all tags (canonical
                            // way to express the original AdditionalInfoTags.Clear).
                            mv.ReplaceWithSingleId(filerId, new List<AdditionalInfoTag>());
                        }
                    }

                    // Add FILING_ATTORNEY since Madera 416110 requires it
                    // (same fix as CIV-SUB-002).
                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        var filingAttorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        filingAttorneyMv.ReplaceWithSingleId(attorneyId);
                        sub.LeadDocument.MetadataValues.Add(filingAttorneyMv);
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "416110"; // Answer
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // CIV-SUB-009 — "Filing on a Case with Multiple Sub-Cases Sample".
            //
            // Pre-Step-#29 H-8 "known-failing" claim (STALE / RETIRED 2026-05-21):
            // the comment originally said Madera couldn't satisfy this scenario
            // because no case in our inventory had multiple sub-cases attached,
            // and predicted Tier B would surface the authentic sub-case-graph
            // error. Reality post-Steps-#16-#38: Step #29 accepted
            // CIV-SUB-009 on MCV089020 cleanly (EFM 26MA00004741). The baseline
            // doesn't actually require the sub-case graph at filing time — the
            // "Multiple Sub-Cases" semantic appears to be a case-state property
            // that Madera accepts a filing AGAINST without re-enforcement on
            // each subsequent doc. Comment dated to an earlier exploration phase
            // and was wrong about the wire-level enforcement.
            //
            // Open Bug H-8 (kept as a court-comparison note, no longer a Tier B
            // blocker): "Sub-case scenarios may need multi-sub-case test cases
            // on Madera staging IF a future scenario needs to interact with the
            // sub-case graph itself (vs. simply filing on a case that has one)."
            //
            // Attach to MCV089020 (has 7 parties, closest to a complex case) and
            // use doc 439110 Motion with minimal metadata.
            ["CIV-SUB-009"] = sub =>
            {
                sub.CaseDocketId = "MCV089020";
                sub.ComplaintId = "782728";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782728";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782728";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978052";     // PLAIN Stephen Williams
                const string attorneyId = "1101839"; // ATT Felicia

                if (sub.LeadDocument != null)
                {
                    // Step #29 — Step #16 helper migration applied
                    // to the Code-keyed if/else-if pattern.
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                        else if (string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase))
                        {
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .ToList();
                            mv.ReplaceWithSingleId(attorneyId, preservedTags);
                        }
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "439110"; // Motion
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },

            // FAM-SUB-004 — "First Paper - Response with request for order
            // using custody or visitation flag Sample".
            //
            // H-7 (codelist mismatch, REAL but worked-around in Step #16):
            // The baseline's connected doc carries a CUSTODY_ISSUE boolean
            // metadata flag. Probing madera_documentList.xml for all declared
            // metadata codes (via diagnostic above) shows CUSTODY_ISSUE is NOT
            // in Madera's metadata vocabulary — Madera's 111 metadata codes
            // exclude it entirely (only standard FILING_* + MOTION_OSC_DETAIL +
            // SELF_REP + a few others). Pre-Step-#16 mitigation: drop the
            // CUSTODY_ISSUE-carrying connected doc; file only the Response.
            // Step #16 then landed the canonical migration on
            // FAM-SUB-004 (EFM 26MA00004717); Step #39 re-verified
            // (EFM 26MA00004757). See
            // @c:/Users/sevak/workspace/test/docs/MADERA_ACCEPTED_FILINGS.json.
            //
            // Open Bug H-7 (kept as a court-comparison note, no longer a Tier B
            // blocker): "Madera codelist does not declare CUSTODY_ISSUE
            // metadata; Placer-era custody/visitation flag scenarios have no
            // direct Madera representation."
            // Workaround status: ACCEPTED via connected-doc drop (Step #16).
            // Scenario intent (CUSTODY_ISSUE flag transmission): PERMANENTLY LOST.
            //
            // Attach to MFL018636 (Family dissolution, current ATT = James
            // Selth 1101871 per Step #16 probe; previously MFL018634 was the
            // target but Felicia was no longer attached there).
            ["FAM-SUB-004"] = sub =>
            {
                // Iteration 5 (Phase B batch 1 remediation): probe
                // (Probe_GetCase_MCV089022_ForCaseAssignmentPrimaryIds, target
                // expanded to MFL018636) showed the current ATT on MFL018636
                // is James Selth primaryId=1101871 — a side-effect of an
                // earlier FAM-SUB-006 submission that added NEW_ATTORNEY
                // James Selth new-data before failing validation, and that
                // remained attached. Felicia is no longer attached to any
                // Family case on Madera staging.
                //
                // FAM-SUB-004 is a Family Response filing (doc 258110) — it
                // does NOT substitute attorneys. It only needs FILING_ATTORNEY
                // to reference the CURRENT ATT of record on the case. Fix:
                // use James Selth 1101871 (probe-verified as current ATT on
                // MFL018636).
                //
                // Iteration history (preserved for forensic traceability):
                //   1: MFL018634 + Felicia 1101830 + CUSTODY_ISSUE
                //     connected doc → rejected "DV-100 is lead-only" + H-7
                //     CUSTODY_ISSUE codelist mismatch.
                //   2: strip EFSP_EMAIL tag.
                //   3: drop connected doc; switch to MFL018636;
                //     add FILING_ATTORNEY referencing "Felicia 1101831" — this
                //     primaryId claim was ungrounded (the cited probe target
                //     MCV089022 is a Civil case, not Family MFL018636).
                //   4 (Phase B batch 1): rejected "4016: Invalid
                //     CaseAssignment id: 1101831".
                //   5 (Phase B batch 1 remediation): probe-verified
                //     fix — use James Selth 1101871 (current MFL018636 ATT).
                sub.CaseDocketId = "MFL018636";
                sub.ComplaintId = "782714";
                if (sub.LeadDocument != null)
                    sub.LeadDocument.ComplaintRef = "782714";
                foreach (var cd in sub.ConnectedDocuments)
                    cd.ComplaintRef = "782714";

                sub.LocationCode = "M";
                sub.LocationName = "Madera Courthouse";
                sub.IncidentZipCode = "93637";

                const string filerId = "978022";     // PET Jessica Williams on MFL018636 (probe-verified)
                const string attorneyId = "1101871"; // ATT James Selth on MFL018636 (probe-verified; current ATT of record)

                // Iteration 3: previous error "DV-100 Request
                // for Order/DV is a lead document, cannot be Additional
                // Document [EFCI_LEAD, EF_LEAD]" — Madera flags 257140 as
                // lead-only. Scenario intent (CUSTODY_ISSUE flag) is already
                // lost to H-7, so drop the connected doc entirely and file
                // only the Response. Also need FILING_ATTORNEY on the lead
                // ("Must Select Filed By Attorney [Response]").
                //
                // Iteration 2: stripped EFSP_EMAIL tag
                // (baseline-origin, invalid under Madera codelist).
                sub.ConnectedDocuments.Clear();

                if (sub.LeadDocument != null)
                {
                    foreach (var mv in sub.LeadDocument.MetadataValues)
                    {
                        if (string.Equals(mv.Code, "FILING_PARTY", StringComparison.OrdinalIgnoreCase))
                        {
                            // Step #16 (see STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md):
                            // pre-Step-#16 this loop only mutated mv.IdReferences + did
                            // mv.AdditionalInfoTags.RemoveAll(EFSP_EMAIL). After Step #14
                            // the wire-builder reads from mv.TaggedReferences (canonical)
                            // and ignores the legacy fields → silent-drop, baseline 1494948
                            // + EFSP_EMAIL=EMAIL_VALUE_HERE survived to the wire and
                            // Madera rejected with 4013. Fix: use ReplaceWithSingleId
                            // helper which mutates BOTH canonical and legacy atomically.
                            // Preserve the original E_SERVICE tag (canonical source-of-truth
                            // is mv.TaggedReferences[*].Tags); drop EFSP_EMAIL placeholder.
                            var preservedTags = mv.TaggedReferences
                                .SelectMany(tr => tr.Tags)
                                .Where(t => !string.Equals(t.TagType, "EFSP_EMAIL", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            mv.ReplaceWithSingleId(filerId, preservedTags);
                        }
                    }

                    // Add FILING_ATTORNEY (Madera 258110 requires it).
                    bool hasFilingAttorney = sub.LeadDocument.MetadataValues.Any(mv =>
                        string.Equals(mv.Code, "FILING_ATTORNEY", StringComparison.OrdinalIgnoreCase));
                    if (!hasFilingAttorney)
                    {
                        // Step #16: when constructing a brand-new metadata
                        // value (rather than mutating an existing one), the canonical
                        // TaggedReferences must be populated explicitly because there's
                        // no parser-populated baseline state to inherit from. The legacy
                        // IdReferences initializer used pre-Step-#14 alone would have
                        // emitted nothing on the wire post-Step-#14 (TaggedReferences
                        // empty → builder emits zero idReferences blocks). Use the
                        // helper after construction to populate both shapes.
                        var attorneyMv = new FilingMetadataValue
                        {
                            Code = "FILING_ATTORNEY",
                            ClassType = "caseAssignment",
                            SubType = "filed-by",
                            ValueRestriction = "existing-data",
                        };
                        attorneyMv.ReplaceWithSingleId(attorneyId);
                        sub.LeadDocument.MetadataValues.Add(attorneyMv);
                    }

                    const string publicTestPdf = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf";
                    sub.LeadDocument.DocumentCode = "258110"; // Response (family)
                    sub.LeadDocument.BinaryLocationUri = publicTestPdf;
                }
            },
        };

    /// <summary>
    /// Look up the scenario-specific override action (attorney substitution, case
    /// reference injection, etc.). Returns <c>true</c> with the override action
    /// when the scenario has been curated, <c>false</c> with <c>null</c> when
    /// curation is still pending.
    /// </summary>
    public static bool TryGetScenarioOverride(string scenarioId, out Action<FilingSubmission>? overrideAction)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("Scenario ID must be non-empty.", nameof(scenarioId));

        return ScenarioOverrides.TryGetValue(scenarioId, out overrideAction);
    }

    /// <summary>
    /// Count of scenarios whose Madera fixtures have been curated and are ready
    /// for live submission. Displayed in the Tier B status helper.
    /// </summary>
    public static int CuratedScenarioCount => ScenarioOverrides.Count;

    /// <summary>
    /// Scenarios that were previously curated but whose staging fixture has been
    /// <i>consumed</i> by a prior successful (or side-effect-completing) run and
    /// cannot be resubmitted without provisioning a fresh case in Madera
    /// staging. Tier B's per-scenario test treats these the same as
    /// pending-curation (skip with log), but the status reporter distinguishes
    /// them so curators don't chase "stale CaseAssignment" rabbit-holes.
    ///
    /// <para>
    /// <b>Entry criterion:</b> a probe against the target case confirmed the
    /// post-scenario state (e.g. the attorney the scenario was supposed to
    /// substitute <i>out</i> is no longer on the case), AND every other
    /// Madera case where the same precondition could be re-established is
    /// load-bearing for a sibling scenario. See the scenario-specific
    /// CONSUMED comment blocks in <see cref="ScenarioOverrides"/> above for
    /// the forensic record of each.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> ConsumedScenarioIds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "CIV-SUB-018", // Felicia inactivation; MCV089024 has no ATT post-run.
            "CIV-SUB-019", // Felicia→James Selth swap; MCV089015 now has Edward M. Sousa as ATT.
            "FAM-SUB-006", // Felicia→James Selth swap; MFL018636 now has James Selth 1101871 as ATT.
        };

    /// <summary>
    /// Count of scenarios whose staging fixture has been consumed and cannot
    /// be resubmitted without fresh Madera-side case provisioning.
    /// </summary>
    public static int ConsumedScenarioCount => ConsumedScenarioIds.Count;

    /// <summary>
    /// The full set of scenarios that require curation for Tier B to be complete.
    /// Computed from <see cref="ScenarioFixtures.MaderaReachable"/>; any scenario
    /// in that collection minus scenarios already in <see cref="ScenarioOverrides"/>
    /// and minus scenarios in <see cref="ConsumedScenarioIds"/> is pending.
    /// </summary>
    public static IEnumerable<string> PendingCurationScenarioIds =>
        ScenarioFixtures.MaderaReachable
            .Select(s => s.Id)
            .Where(id => !ScenarioOverrides.ContainsKey(id) && !ConsumedScenarioIds.Contains(id));
}
