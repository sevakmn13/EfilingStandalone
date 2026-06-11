using System;
using System.Collections.Generic;
using System.Linq;
using EFiling.Core.Models;
using EFiling.Nop.Models;

namespace EFiling.Nop.Mapping;

/// <summary>
/// Maps a UI-layer <see cref="MetadataEntryDto"/> (deserialized from <c>MetadataJson</c>) to
/// one-or-more provider-layer <see cref="FilingMetadataValue"/> elements, centralizing the
/// classType dispatch that was previously duplicated in two controller paths:
/// <list type="bullet">
///   <item><description><see cref="Controllers.CourtFilingController.BuildSubmissionFromCreateModel"/>
///     (full submission builder — the former source-of-truth).</description></item>
///   <item><description><c>EFilingMvcController.BuildQuickSubmission</c> (fee-calc preview —
///     historically a lightweight subset that silently dropped <c>contact</c>, new-data parties,
///     new-data attorneys, <c>ValueRestriction</c> propagation, and <c>AdditionalInfoTags</c>).</description></item>
/// </list>
///
/// <para><b>T-5 (plan §T-5, 2026-04-23):</b> consolidating to one mapper eliminates the drift
/// surface and back-fills Site 2 with the audit C-1 / C-2 / C-3 / D-2 fixes that had previously
/// only landed in Site 1.</para>
///
/// <para><b>Semantic contract:</b> one <see cref="MetadataEntryDto"/> maps to ZERO, ONE, or
/// MULTIPLE <see cref="FilingMetadataValue"/> elements. Returns zero when the entry is malformed
/// (missing <c>Code</c>, null/empty <c>Values</c>, empty simple-value payload, etc.) — the
/// builder's schema-aware default arm catches anything that slips through upstream filtering.
/// Returns multiple when a single <c>caseParticipant</c> or <c>caseAssignment</c> entry carries
/// both existing-data references AND new-data entries; each branch emits its own
/// <see cref="FilingMetadataValue"/> so wire order and wrapper-element dispatch stays correct.</para>
///
/// <para><b>Unknown classType policy:</b> silently yields nothing here (preserves historical
/// controller behavior). Anything that reaches the builder with an unknown classType hits the
/// schema-aware default arm at <c>ReviewFilingXmlBuilder.BuildMetadataItem</c> and throws
/// fail-closed per catalog §3.0 observation #3.</para>
/// </summary>
internal static class MetadataValueMapper
{
    /// <summary>
    /// Maps a single <see cref="MetadataEntryDto"/> to zero or more provider-layer
    /// <see cref="FilingMetadataValue"/> elements. See class-level remarks for the semantic
    /// contract.
    /// </summary>
    public static IEnumerable<FilingMetadataValue> FromDto(MetadataEntryDto meta)
    {
        if (meta is null) yield break;
        if (string.IsNullOrEmpty(meta.Code) || meta.Values == null || meta.Values.Count == 0)
            yield break;

        var classType = (meta.ClassType ?? string.Empty).ToLowerInvariant();

        switch (classType)
        {
            case "caseparticipant":
                foreach (var mv in MapCaseParticipant(meta)) yield return mv;
                yield break;

            case "caseassignment":
            case "attorney":
                // Controller-layer scope: `attorney` classType new-data is historically routed
                // through the caseAssignment wire shape per §3.3 open question #1. The builder
                // (residual c fix, 2026-04-23 evening) has a separate `attorney` arm for any
                // upstream that emits ClassType="attorney" on a FilingMetadataValue directly,
                // but the UI-layer DTO path normalizes attorney → caseAssignment here.
                foreach (var mv in MapCaseAssignment(meta)) yield return mv;
                yield break;

            case "codelist":
                {
                    var val = meta.Values.FirstOrDefault()?.Value?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        yield return new FilingMetadataValue
                        {
                            Code = meta.Code!,
                            ClassType = "codeList",
                            ValueRestriction = meta.ValueRestriction, // Audit D-2
                            CodeValue = val,
                        };
                    yield break;
                }

            case "currency":
                {
                    var val = meta.Values.FirstOrDefault()?.Value?.ToString();
                    if (decimal.TryParse(val, out var amount))
                        yield return new FilingMetadataValue
                        {
                            Code = meta.Code!,
                            ClassType = "currency",
                            ValueRestriction = meta.ValueRestriction, // Audit D-2
                            CurrencyValue = amount,
                        };
                    yield break;
                }

            case "date":
                {
                    var val = meta.Values.FirstOrDefault()?.Value?.ToString();
                    if (DateTime.TryParse(val, out var date))
                        yield return new FilingMetadataValue
                        {
                            Code = meta.Code!,
                            ClassType = "date",
                            ValueRestriction = meta.ValueRestriction, // Audit D-2
                            DateValue = date,
                        };
                    yield break;
                }

            case "boolean":
                {
                    // Boolean is special: absence of a value still emits false (to match the
                    // historical Site 1 behavior — the DTO pattern carries "unchecked" as a
                    // present value, not an absent one, for boolean switches).
                    var val = meta.Values.FirstOrDefault()?.Value;
                    var boolVal = val is bool b ? b : (val?.ToString()?.ToLower() == "true");
                    yield return new FilingMetadataValue
                    {
                        Code = meta.Code!,
                        ClassType = "boolean",
                        ValueRestriction = meta.ValueRestriction, // Audit D-2
                        BooleanValue = boolVal,
                    };
                    yield break;
                }

            case "text":
            case "string":
                {
                    var val = meta.Values.FirstOrDefault()?.Value?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        yield return new FilingMetadataValue
                        {
                            Code = meta.Code!,
                            ClassType = "text",
                            ValueRestriction = meta.ValueRestriction, // Audit D-2
                            TextValue = val,
                        };
                    yield break;
                }

            case "judgment":
                {
                    // Step #15 audit (Path C — see docs/STEP15_JUDGMENT_AUDIT.md §3 + §9):
                    // pre-fix this branch was missing entirely → the default arm silently
                    // yielded nothing, dropping any judgment DTO from the UI before it
                    // reached the builder. The builder's separate stub-throw never fired
                    // for SF flows because the mapper killed the value first.
                    //
                    // Existing-data: read each MetadataValueDto.Id (LASC vendor sample shows
                    // 1 judgmentId per metadata item, but WSDL allows N — emit all populated
                    // ids into JudgmentIds list).
                    //
                    // New-data: not implemented — schema marks newData.awaitingEvidence=true.
                    // Mapper passes the value through with empty JudgmentIds; builder throws
                    // NotImplementedException downstream with a clear message.
                    var restriction = (meta.ValueRestriction ?? "existing-data").ToLowerInvariant();
                    var judgmentIds = new List<string>();
                    if (restriction == "existing-data")
                    {
                        foreach (var v in meta.Values)
                        {
                            if (v == null) continue;
                            // SF UI's existing-data DTO populates Id; CI / future loaders may
                            // populate Value (single-value pattern from simple-value collect).
                            // Accept either; prefer Id when populated.
                            var idVal = !string.IsNullOrEmpty(v.Id)
                                ? v.Id
                                : v.Value?.ToString();
                            if (!string.IsNullOrEmpty(idVal))
                                judgmentIds.Add(idVal);
                        }
                        if (judgmentIds.Count == 0)
                            yield break; // no usable id → drop the metadata item
                    }
                    yield return new FilingMetadataValue
                    {
                        Code = meta.Code!,
                        ClassType = "judgment",
                        ValueRestriction = meta.ValueRestriction, // pass through verbatim
                        JudgmentIds = judgmentIds,
                    };
                    yield break;
                }

            case "contact":
                {
                    // Audit C-2 Bug A fix (catalog §3.4) — prior to this branch, any
                    // ClassType="contact" MetadataEntryDto (e.g., CIV-SUB-003 FILING_PARTY_ADDRESS)
                    // was silently dropped before reaching the builder. The HasAnyContactField
                    // gate keeps us from emitting an empty <contactValue/> wire element.
                    //
                    // DTO is schema-aligned (TelephoneNumber/TelephoneType match schema field names).
                    // The wire shape ContactValueData uses JTI's XML naming convention (PhoneNumber/
                    // PhoneType match XML <phoneNumber>/<phoneType>) — hence the rename here at the
                    // mapping boundary. This is the only place the schema-name ↔ wire-name mapping happens.
                    var contactDto = meta.Values.FirstOrDefault();
                    if (contactDto != null && HasAnyContactField(contactDto))
                        yield return new FilingMetadataValue
                        {
                            Code = meta.Code!,
                            ClassType = "contact",
                            ContactValue = new ContactValueData
                            {
                                Address1 = contactDto.Address1,
                                Address2 = contactDto.Address2,
                                City = contactDto.City,
                                State = contactDto.State,
                                Zip = contactDto.Zip,
                                Country = contactDto.Country,
                                AddressType = contactDto.AddressType,
                                PhoneNumber = contactDto.TelephoneNumber,  // schema 'telephoneNumber' → wire 'phoneNumber'
                                PhoneType = contactDto.TelephoneType,       // schema 'telephoneType' → wire 'phoneType'
                                Email = contactDto.Email,
                            },
                        };
                    yield break;
                }

            default:
                // Unknown classType → silent drop at the controller layer. The builder's
                // schema-aware default arm (`ReviewFilingXmlBuilder.BuildMetadataItem` default
                // case, added 2026-04-23 evening) handles fail-closed downstream if anything
                // slips through via another upstream path.
                yield break;
        }
    }

    /// <summary>
    /// caseParticipant branch: emits 0-or-1 existing-data entry (id references + tags) and
    /// 0-or-N new-data entries (one per new party). Mirrors Site 1 semantics at
    /// <c>CourtFilingController.BuildSubmissionFromCreateModel</c> (lines 835-916 pre-T-5).
    /// </summary>
    private static IEnumerable<FilingMetadataValue> MapCaseParticipant(MetadataEntryDto meta)
    {
        var subType = !string.IsNullOrEmpty(meta.SubType) ? meta.SubType : "filed-by";

        // Existing-data path — audit C-3a fix: emit AdditionalInfoTags from meta.Values[*].Tags.
        // Step #14 (silent-drop #10): write canonical TaggedReferences with per-id tag fidelity.
        // Legacy IdReferences + flat AdditionalInfoTags still populated for back-compat readers
        // (test asserts, log formatters); builder prefers TaggedReferences when non-empty.
        var existingValues = meta.Values!.Where(v => !string.IsNullOrEmpty(v.Id) && !v.IsNew).ToList();
        if (existingValues.Count > 0)
        {
            var taggedRefs = existingValues
                .Select(v => new TaggedReference
                {
                    Id = v.Id!,
                    Tags = BuildAdditionalInfoTags(new[] { v }),
                })
                .ToList();

            yield return new FilingMetadataValue
            {
                Code = meta.Code!,
                ClassType = "caseParticipant",
                SubType = subType,
                ValueRestriction = "existing-data",
                TaggedReferences = taggedRefs,
                // Legacy parallel-list projection — same content the pre-Step-#14 mapper produced.
                IdReferences = taggedRefs.Select(r => r.Id).ToList(),
                AdditionalInfoTags = taggedRefs.SelectMany(r => r.Tags).ToList(),
            };
        }

        // New-data path — one FilingMetadataValue per new party. Self-represented parties carry
        // an inline ContactInfo; non-self-represented parties associate to a LeadAttorneyId
        // via IdReferences (kept on the same FilingMetadataValue per wire convention).
        var newParties = meta.Values!.Where(v => v.IsNew).ToList();
        foreach (var newParty in newParties)
        {
            var partyMeta = new FilingMetadataValue
            {
                Code = meta.Code!,
                ClassType = "caseParticipant",
                SubType = subType,
                ValueRestriction = "new-data",
                NewPartyValue = new FilingParty
                {
                    RoleCode = newParty.PartyType ?? string.Empty,
                    IsOrganization = newParty.IsOrganization,
                    FirstName = newParty.FirstName,
                    MiddleName = newParty.MiddleName,
                    LastName = newParty.LastName,
                    NameSuffix = newParty.Suffix,
                    OrganizationName = newParty.OrganizationName,
                    InterpreterLanguage = newParty.InterpreterLanguage,
                    FeeExemptionRequestType = newParty.FeeExemptionType,
                    // Site 1 quirk preserved: self-rep → Contact always materialized (even if
                    // all contact fields are empty). Non-self-rep → Contact null (the lead
                    // attorney acts as the contact). If an empty-contact element ever trips
                    // wire validation this gate can tighten to HasAnyContactField(newParty).
                    Contact = newParty.SelfRepresented
                        ? new ContactInfo
                        {
                            MailingAddress = new StructuredAddress
                            {
                                Address1 = newParty.Address1,
                                Address2 = newParty.Address2,
                                City = newParty.City,
                                State = newParty.State,
                                Zip = newParty.Zip,
                                Country = newParty.Country,        // bonus silent-drop fix 2026-05-17 (audit finding)
                                AddressType = newParty.AddressType, // bonus silent-drop fix 2026-05-17 (audit finding)
                            },
                            PhoneNumber = newParty.TelephoneNumber,
                            PhoneType = newParty.TelephoneType,    // bonus silent-drop fix 2026-05-17 (audit finding)
                            Email = newParty.Email,
                        }
                        : null,
                },
            };

            if (!newParty.SelfRepresented && !string.IsNullOrEmpty(newParty.LeadAttorneyId))
                partyMeta.IdReferences.Add(newParty.LeadAttorneyId);

            // AKA / DBA alternate names — bonus silent-drop fix 2026-05-17 (audit finding).
            // Pre-fix the SF flow silently dropped Akas at deserialization (DTO didn't declare
            // the field) while the CC initial-filing flow (CourtFilingController.BuildSubmission
            // FromCreateModel ~line 499) has supported it since the AKA card UI landed.
            if (newParty.Akas != null && partyMeta.NewPartyValue != null)
            {
                foreach (var aka in newParty.Akas)
                {
                    if (string.IsNullOrEmpty(aka.Type)) continue;
                    partyMeta.NewPartyValue.AlternateNames.Add(new AlternateName
                    {
                        Type = aka.Type,
                        FirstName = aka.IsOrganization ? null : aka.FirstName,
                        MiddleName = aka.IsOrganization ? null : aka.MiddleName,
                        LastName = aka.IsOrganization ? null : aka.LastName,
                        NameSuffix = aka.IsOrganization ? null : aka.Suffix, // 2026-05-17: pre-fix silently dropped
                        OrganizationName = aka.IsOrganization ? aka.OrganizationName : null
                    });
                }
            }

            // Audit C-3c fix: per-tag-type dispatch via DetermineTagValue, skipping malformed
            // tags (e.g. FEE_EXEMPTION with no FeeExemptionType set) per §2.6.2 fail-closed.
            partyMeta.AdditionalInfoTags = BuildAdditionalInfoTags(new[] { newParty });

            yield return partyMeta;
        }
    }

    /// <summary>
    /// caseAssignment branch: emits 0-or-1 existing-data entry (attorney id references + tags)
    /// and 0-or-N new-data entries (one per new attorney). Mirrors Site 1 semantics at
    /// <c>CourtFilingController.BuildSubmissionFromCreateModel</c> (lines 917-994 pre-T-5).
    /// Audit C-1 fix (catalog §3.3) is rooted HERE — before its landing, any IsNew=true
    /// attorney DTO was silently dropped by Site 2 (the fee-calc preview), under-reporting
    /// the fee-bearing attorney count.
    /// </summary>
    private static IEnumerable<FilingMetadataValue> MapCaseAssignment(MetadataEntryDto meta)
    {
        // Existing-data path — audit C-3b consistency fix: emit AdditionalInfoTags even though
        // baseline has no evidence of tags on caseAssignment references. Forward-compatible
        // per §2.6.2 — don't silently drop what the UI sends. Step #14 (silent-drop #10):
        // write canonical TaggedReferences with per-id tag fidelity. Legacy parallel-list
        // fields kept populated as a back-compat projection.
        var existingValues = meta.Values!.Where(v => !string.IsNullOrEmpty(v.Id) && !v.IsNew).ToList();
        if (existingValues.Count > 0)
        {
            var taggedRefs = existingValues
                .Select(v => new TaggedReference
                {
                    Id = v.Id!,
                    Tags = BuildAdditionalInfoTags(new[] { v }),
                })
                .ToList();

            yield return new FilingMetadataValue
            {
                Code = meta.Code!,
                ClassType = "caseAssignment",
                SubType = "attorney",
                ValueRestriction = "existing-data",
                TaggedReferences = taggedRefs,
                IdReferences = taggedRefs.Select(r => r.Id).ToList(),
                AdditionalInfoTags = taggedRefs.SelectMany(r => r.Tags).ToList(),
            };
        }

        // New-data path — audit C-1 fix (catalog §3.3). Wire evidence: CIV-SUB-005/007/013/019,
        // FAM-SUB-001/006, PRO-SUB-001.
        var newAttorneys = meta.Values!.Where(v => v.IsNew).ToList();
        foreach (var a in newAttorneys)
        {
            ContactInfo? attorneyContact = null;
            if (HasAnyContactField(a))
            {
                var hasAddress = !string.IsNullOrEmpty(a.Address1);
                attorneyContact = new ContactInfo
                {
                    MailingAddress = hasAddress
                        ? new StructuredAddress
                        {
                            Address1 = a.Address1,
                            Address2 = a.Address2,
                            City = a.City,
                            State = a.State,
                            Zip = a.Zip,
                            Country = a.Country,         // bonus silent-drop fix 2026-05-17 (audit finding)
                            AddressType = a.AddressType, // bonus silent-drop fix 2026-05-17 (audit finding)
                        }
                        : null,
                    PhoneNumber = a.TelephoneNumber,
                    PhoneType = a.TelephoneType,         // bonus silent-drop fix 2026-05-17 (audit finding)
                    Email = a.Email,
                };
            }

            yield return new FilingMetadataValue
            {
                Code = meta.Code!,
                ClassType = "caseAssignment",
                SubType = "attorney",
                ValueRestriction = "new-data",
                CaseAssignmentValue = new CaseAssignmentData
                {
                    FirstName = a.FirstName,
                    MiddleName = a.MiddleName,
                    LastName = a.LastName,
                    NameSuffix = a.Suffix,                 // 2026-05-17 silent-drop fix (parallel to AKA Suffix)
                    BarNumber = a.BarNumber,
                    FirmName = a.FirmName,
                    // §3.3 evidence: "ATT" is the only AssignmentRole observed in baseline.
                    // CaseAssignmentData's default is already "ATT"; explicit here for clarity.
                    AssignmentRole = "ATT",
                    Contact = attorneyContact,
                },
            };
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // Helpers — moved from CourtFilingController in T-5. Tests that used
    // the internal static helpers on the controller should import them from here.
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True if the metadata-value DTO carries at least one populated contact field. Used by the
    /// subsequent-filing classType=contact switch branch (audit C-2 Bug A fix, catalog §3.4) and
    /// by the new-attorney contact gate (audit C-1, catalog §3.3) to avoid emitting empty wire
    /// elements when the user supplied no contact data.
    /// </summary>
    public static bool HasAnyContactField(MetadataValueDto dto) =>
        !string.IsNullOrEmpty(dto.Address1)
        || !string.IsNullOrEmpty(dto.Address2)
        || !string.IsNullOrEmpty(dto.City)
        || !string.IsNullOrEmpty(dto.State)
        || !string.IsNullOrEmpty(dto.Zip)
        || !string.IsNullOrEmpty(dto.Country)
        || !string.IsNullOrEmpty(dto.AddressType)
        || !string.IsNullOrEmpty(dto.TelephoneNumber)
        || !string.IsNullOrEmpty(dto.TelephoneType)
        || !string.IsNullOrEmpty(dto.Email);

    /// <summary>
    /// Computes the wire-correct <c>tagValue</c> for a given tagType, drawing from the
    /// populated fields of <paramref name="dto"/>. Returns <c>null</c> when the tag should be
    /// SKIPPED (e.g., FEE_EXEMPTION without FeeExemptionType — cannot be emitted as a
    /// well-formed tag per catalog §4.2; fail-closed per §2.6.2).
    ///
    /// Catalog §4.0 value-semantics taxonomy:
    /// <list type="bullet">
    /// <item><description><b>digit-boolean</b> — <c>E_SERVICE</c>, <c>EFSP_FIRST_APPEARANCE_PAID</c>:
    /// emit <c>"1"</c> (presence = opt-in). NOT <c>"true"</c> — the pre-fix hardcoded literal.</description></item>
    /// <item><description><b>string-enum</b> — <c>FEE_EXEMPTION</c>: emit
    /// <c>dto.FeeExemptionType</c> (e.g., <c>"GOVT_ENTITY"</c> or <c>"FEE_WAIVER"</c>).</description></item>
    /// <item><description><b>free-text</b> — <c>EFSP_EMAIL</c>: emit <c>dto.Email</c>.</description></item>
    /// <item><description>Unknown — default <c>"1"</c> with TODO to tighten once
    /// §4.5 unverified-hypothesis tags resolve with live evidence.</description></item>
    /// </list>
    /// Audit C-3 fix (catalog §4.0 scope expansion).
    /// </summary>
    public static string? DetermineTagValue(string tagType, MetadataValueDto dto)
    {
        return tagType?.ToUpperInvariant() switch
        {
            // digit-boolean per catalog §4.1 / §4.3 — presence implies opt-in (emit "1"). The
            // "0" (opt-out) case is not representable in the current DTO shape (Tags is a
            // presence-only List<string>); if opt-out tagging is needed, expand the DTO to
            // carry per-tag values explicitly. Deferred — no baseline UI evidence either way.
            "E_SERVICE" => "1",
            "EFSP_FIRST_APPEARANCE_PAID" => "1",
            // string-enum per §4.2. Skip emission if FeeExemptionType unset — the tag is
            // meaningless without a chosen exemption class; emitting a malformed tag would
            // trip JTI's validator. The skipping path is regression-gated by the
            // AuditC3c "SkipsEmission" test.
            "FEE_EXEMPTION" => string.IsNullOrEmpty(dto.FeeExemptionType) ? null : dto.FeeExemptionType,
            // free-text per §4.4. Skip emission if Email unset — see fail-closed note above.
            "EFSP_EMAIL" => string.IsNullOrEmpty(dto.Email) ? null : dto.Email,
            // Unknown tagType — preserve the historical pre-fix behavior (emit "1") to avoid
            // regressing any per-court variant tags that rely on presence-only semantics. This
            // is intentionally loose; tighten to null (fail-closed) once §4.5 hypotheses
            // resolve with live evidence per §2.6.2 discipline.
            _ => "1",
        };
    }

    /// <summary>
    /// Collects wire-correct <see cref="AdditionalInfoTag"/> entries from the
    /// <c>Tags</c> list across all passed-in DTO values, using <see cref="DetermineTagValue"/>
    /// for per-tag-type value dispatch. Skips tags whose value cannot be determined (returns
    /// null from <see cref="DetermineTagValue"/>). Audit C-3 fix (catalog §4.0 / §4.1-§4.4).
    ///
    /// Scope note: the returned list is a flat aggregate across all DTO values. If multiple
    /// existing-data values carry different tag sets, the caller receives a combined list —
    /// per-id tag differentiation would require a domain-model change (see §4.0 "CRITICAL
    /// semantic principle"). No baseline evidence of per-id tag differentiation within a
    /// single metadata item, so the simplification is safe for observed patterns.
    /// </summary>
    public static List<AdditionalInfoTag> BuildAdditionalInfoTags(IEnumerable<MetadataValueDto> values)
    {
        var result = new List<AdditionalInfoTag>();
        foreach (var v in values)
        {
            if (v.Tags == null) continue;
            foreach (var tagType in v.Tags)
            {
                if (string.IsNullOrWhiteSpace(tagType)) continue;
                var tagValue = DetermineTagValue(tagType, v);
                if (tagValue is null) continue; // fail-closed skip
                result.Add(new AdditionalInfoTag { TagType = tagType, TagValue = tagValue });
            }
        }
        return result;
    }
}
