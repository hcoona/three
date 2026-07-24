# Atlas V0 A0 Current Corpus Recovery Plan Review

**Lifecycle:** Proposed plan-review evidence before verified shared `R0R2`

**Increment:** A0R2 - Diagnostic-Gated Census Recovery

**Outcome:** Utility preparation remains blocked; private diagnosis and census remain unauthorized

**Final independent result:** `No findings`

**Base G0R1:** `94d632ca59e44e9312e4691928091195e23a0d4c`

**Final P0R2:** `c82f1c767fab496dd2b025fa1ab25f5d6583cd46`

**Final P0R2 tree:** `156bd367f3d6e4d4b53d01de8a1fd5b58595ba7c`

**Governing plan:** `../plans/atlas-v0-a0-current-corpus-recovery.md`

**Planned staged-record reviewer:** `a0r2-plan-record-reviewer`

## 1. Exact plan candidate

`P0R2` is the direct child of `G0R1`. Its exact no-renames path set is:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a0-current-corpus-refresh-governance-remediation.md
    atlas-v0-a0-current-corpus-recovery.md
    atlas-v0-a2-intake-safety-plan.md
```

Its exact blobs are:

```text
README.md
  ccad179be57ef3f948e60645044732c91a20d06c
atlas-v0-a0-current-corpus-refresh.md
  5bd89d6d4ab4f89859a3bfaa2639fb38d1f8fa77
atlas-v0-a0-current-corpus-refresh-governance-remediation.md
  a03c388abbf5470b8884fbf5a460a31a6b3c4715
atlas-v0-a0-current-corpus-recovery.md
  6d4c6b5365bf05f2e7674ab1f6e86ab56bda790a
atlas-v0-a2-intake-safety-plan.md
  4058a5ddbb1d8e1de3adb99118a363ee2c50f9a4
```

`P0R2` was pushed as the clean shared tip before this record was authored.

## 2. Review iterations

Every reviewer was independent of plan authorship and used GPT-5.6 Sol. Review input was limited to
the exact repository-safe candidate, tracked governance sources, and released source. No reviewer
received the protected utility, execution state or result, manifest, inventory, private path,
filename, count, corpus hash, difference, or installed-file content.

| Candidate                      | Tree                                       | Reviewer                         | Result        |
| ------------------------------ | ------------------------------------------ | -------------------------------- | ------------- |
| Initial staged candidate       | `47c96c7fdb9b26e48d2bea92f267f64265938932` | `a0r2-plan-reviewer`             | 6 TP          |
| First corrected candidate      | `3495e521cd6dc60d9ebe65ee4695fbbfd94c1b16` | `a0r2-final-plan-reviewer`       | 1 TP          |
| Source-gated candidate         | `1f049fad1c783394da2eec42b6c589bc76cb82d5` | `a0r2-release-plan-reviewer`     | 1 TP          |
| Preflight-corrected candidate  | `e76abbdf471d4d170fced51858640eb68660f080` | `a0r2-plan-no-findings-reviewer` | 1 TP          |
| A2-handoff-corrected candidate | `e6deb75b75d9ef7bc5090842217b75f5bfa085ed` | `a0r2-ultimate-plan-reviewer`    | 1 TP          |
| Decision-closure candidate     | `eb79beb9a4a199eb9ae1ed55d0ca5ead273b1272` | `a0r2-final-authority-reviewer`  | 4 TP          |
| Strict-authority candidate     | `481020a82c6ccadd92cab7eaa0d93cc7b3871cd5` | `a0r2-no-findings-gate-reviewer` | 5 TP          |
| Historical-authority candidate | `fe5af933f563f82e37ce035d3ff08f177702e85a` | `a0r2-complete-plan-reviewer`    | 1 TP          |
| Final staged candidate         | `156bd367f3d6e4d4b53d01de8a1fd5b58595ba7c` | `a0r2-plan-clearance-reviewer`   | `No findings` |

The final candidate was committed unchanged as `P0R2`. Its committed tree and blobs equal the final
reviewed staged candidate.

## 3. Planning-drift gate

Each round re-derived the ideal minimal shape before correction:

- A0R1 remains closed and its unauthorized later result remains unusable;
- diagnosis must precede any census rather than repeat the failed path blindly;
- runtime state begins empty, while exact reviewed source identity comes from Git;
- only a marker begins a consuming private attempt;
- every consumed attempt has a result-safe terminal closure;
- one exact protected project-leader decision and reviewed Git gate precede census;
- no pending candidate is approved or finalized in A0R2; and
- A2 remains blocked pending separate refreshed-manifest approval and A2R14.

Reviewers found no planning-drift or overcomplexity finding. Each added marker or Git gate directly
closes an observed authority, interruption, or machine-verification gap.

## 4. TP adjudication

All 20 findings were atomic, in scope, and adjudicated TP. No finding was FP.

| #   | Finding                                                                        | Correction                                                                                       |
| --- | ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| 1   | Census refusal and interruption lacked distinguishable durable evidence.       | Use one result-neutral `census no candidate` terminal branch after a marker without a candidate. |
| 2   | A2 retained live A0R1 progression and output authority.                        | Close A0R1 throughout A2 and require later refreshed approval plus A2R14.                        |
| 3   | CLI arguments, path derivation, identifiers, and resume order were incomplete. | Define exact modes, roots, fresh IDs, outputs, exits, and ordered resume.                        |
| 4   | The copied utility source was not bound to G0R1.                               | Require an exact two-file allowlist, empty state, and published G0R1 source hashes.              |
| 5   | Eight diagnostic boundaries mapped to seven result classes.                    | Merge codec serialization, reload, and replay into one candidate-replay boundary.                |
| 6   | README omitted the A0R1, A0R1C1, and G0R1 gate evidence.                       | Add all three records to current gate evidence.                                                  |
| 7   | Empty runtime state could not supply externally reviewed source identity.      | Add machine-verifiable `S0R2` and workspace-root `source-bindings.json`.                         |
| 8   | Census preflight refusal consumed no marker but appeared non-retryable.        | Permit fresh-ID preflight reinvocation; forbid only a second marked private attempt.             |
| 9   | A2 approval could resume through a stale post-A2R11 route.                     | Require A0R2, separate manifest approval, G/G10/G11/G12/G13, and reviewed A2R14.                 |
| 10  | Interrupted protected-decision publication lacked closure.                     | Add a one-shot decision marker and `decision-incomplete` terminal branch.                        |
| 11  | A malformed diagnostic receipt lacked terminal handling.                       | Define validity strictly and treat missing or malformed output as diagnostic-incomplete.         |
| 12  | `D0R2` lacked machine-verifiable persisted authority.                          | Add a unique strict canonical decision-authority block parsed before census.                     |
| 13  | Census invocation and consuming-attempt terminology conflicted.                | Reserve consuming private attempt for post-marker execution.                                     |
| 14  | Historical A0R1C1 evidence was listed as current governance.                   | Separate normative sources from historical provenance and evidence.                              |
| 15  | The A0R1 banner left approval and finalization rules operative.                | Revoke every A0R1 execution route and name only the policy sections imported by A0R2.            |
| 16  | A2 summaries omitted exact `S0R2` and `D0R2` prerequisites.                    | Name source qualification for diagnosis and authorize-census decision authority for census.      |
| 17  | Acceptance unconditionally required a receipt on diagnostic-incomplete.        | Make strict receipt evidence conditional on controlled completion.                               |
| 18  | Global no-retry wording contradicted preflight reinvocation.                   | Prohibit retry only after diagnostic, decision, or census markers exist.                         |
| 19  | README listed historical A0R1C1 under normative baseline.                      | Move A0R1C1 to historical and supporting artifacts.                                              |
| 20  | A0R1 was historical while A0R2 still imported its detailed policy.             | Keep A0R1 partially superseded with only the exactly imported sections operative.                |

Final review verified every correction in the complete candidate. None changes the approved corpus
policy, threat model, or privacy boundary.

## 5. Validation evidence

Repository hooks validated exact `P0R2` Markdown with EditorConfig, typos, markdownlint-cli2, and
Prettier. `git diff --check` passed. Git verification proved:

- `P0R2` is the direct child of exact `G0R1`;
- it changes exactly the five paths in section 1;
- its blobs and tree equal section 1;
- `P0R2` equals the remote development-branch tip; and
- the worktree was clean before record authorship.

No private read, diagnostic, census, protected decision, candidate publication, source-content read,
or A2 operation occurred during A0R2 planning and review.

## 6. Accepted boundary

After verified shared `R0R2`, work may:

1. create a new protected A0R2 workspace from the exact two-file G0R1 technical allowlist;
2. modify only the copied project and source;
3. format, build, and run synthetic tests;
4. independently review the complete exact source until `No findings`; and
5. author and independently review the result-safe `S0R2` source-qualification record.

Neither `P0R2` nor `R0R2` authorizes a private diagnostic, current-tree metadata read, census,
protected project-leader decision, candidate publication, or A2 operation.

Only verified shared `S0R2` may authorize one consuming private diagnostic attempt. Even then,
diagnostic success grants no census. Only a complete valid `authorize-census` protected decision
represented by verified shared `D0R2` may authorize one consuming private census attempt.

## 7. R0R2 release gate

This proposed record grants utility-preparation authority only. Work may continue only after:

1. this exact staged record receives independent `No findings`;
2. it is committed unchanged as `R0R2`, the direct child of `P0R2`;
3. `P0R2..R0R2` adds only this record path;
4. the committed record blob equals the reviewed staged blob;
5. `R0R2` is pushed and verified as the clean shared branch tip; and
6. no private operation occurs before exact reviewed and verified `S0R2`.
