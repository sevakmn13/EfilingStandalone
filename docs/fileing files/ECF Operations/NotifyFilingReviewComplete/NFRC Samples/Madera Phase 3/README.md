# Madera Phase 3 — NFRC fixture set

Captured during the NFRC audit's Phase 3 (live submissions to Madera staging,
2026-04-26 / 2026-04-27). Provenance for each file is documented below.

## What's in this folder

| File | Source | EFM ref | Submission shape | NFRC stage | Madera filing-status |
|---|---|---|---|---|---|
| `CC-Accept-NFRC1-ClerkAction.xml` | `EFilingNfrcLog.Id=18` | `26MA00003990` | CC, normal (no auto-* header) | NFRC #1 (clerk action) | `ACCEPTED` |
| `CC-Accept-NFRC2-Financials.xml`  | `EFilingNfrcLog.Id=19` | `26MA00003990` | CC, normal (no auto-* header) | NFRC #2 (financials) | `ACCEPTED` |
| `CC-Reject-NFRC1-ClerkAction.xml` | `EFilingNfrcLog.Id=24` | `26MA00004477` | CC, JTI auto-reject test header | NFRC #1 (clerk action) | `REJECTED` |
| `SF-Reject-NFRC1-ClerkAction.xml` | `EFilingNfrcLog.Id=23` | `26MA00004476` | SF, JTI auto-reject test header | NFRC #1 (clerk action) | `REJECTED` |

The `CC-Accept-NFRC1` and `CC-Accept-NFRC2` files are the **same filing** at two stages:
NFRC #1 fires when the clerk's accept action is recorded; NFRC #2 fires after Madera's
fees-calculation pipeline runs (~4 seconds later in this sample). Use the pair when
exercising 2-stage acceptance flows.

## Why these specific rows

Three of the four are fresh from the Phase 3 controlled submissions. The CC-Accept pair
is **historical** (2026-04-11) rather than fresh because the Phase 3 CC accept submission
used the JTI test `<status>This is a test filing</status>` SOAP header, which Madera
acknowledges synchronously (`GetFilingStatus` reports `ACCEPTED` and assigns a docket)
**but the filing is not picked up by the NFRC dispatch pipeline.** Madera's `GetNFRC`
SOAP returns the semantic message _"Transaction not fully processed by the court system"_
on the auto-accept-header filings, while reject submissions return _"Your request has
been queued"_ and the corresponding NFRC actually arrives a few seconds later.

The historical 2026-04-11 CC accept (order 24, EFM `26MA00003990`) was a **normal** clerk-
driven submission that exercised the full NFRC pipeline, so it is the most accurate
"real-world CC accept NFRC" we have.

## What's missing

There is **no SF accept fixture**. The Phase 3 SF accept submission (`26MA00004475`,
`SFAUTOTEST-…`) reached Madera, was assigned to docket `MFL018634`, and `GetFilingStatus`
reports `ACCEPTED`, but no NFRC ever arrived — same dispatch-pipeline bypass as the CC
accept above. There are also no historical SF NFRCs in `EFilingNfrcLog` because (until
Phase 0 instrumentation landed in the same session) unmatched SF callbacks only hit
application logs and were lost.

The SF accept fixture is therefore deferred until Phase 6 of the audit, by which time
P1 will have landed `EFilingOrderRecord` creation for SF and a normal SF submit (without
the auto-accept header) will produce a normal NFRC that matches and persists.

## How to use these fixtures

Mirror `NfrcResponseParser_LascFixtureTests.cs`. Path constant:

```csharp
private const string MaderaPhase3Relative =
    "ECF Operations/NotifyFilingReviewComplete/NFRC Samples/Madera Phase 3";
```

Combine with `SampleLoader.RepoRoot` and `"docs/fileing files"` like the LASC tests do.

## Cross-references

- Audit plan: `docs/EFILING_NFRC_AUDIT_PLAN.md` § 15.4 (Phase 3 findings).
- Phase 3 submission tests: `src/EFiling/EFiling.Tests/AutoAcceptFilingTests.cs:439-672`.
- Phase 0 instrumentation that captured the unmatched rows: `src/EFiling/EFiling.Nop/Services/NfrcCallbackTriage.cs` + the `AddNfrcLogUnmatchedColumns` migration.
