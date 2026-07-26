# Atlas V0 A0 Approved-Byte Authority Correction Plan Review

**Lifecycle:** Proposed plan-review evidence before verified shared `R0R6`

**Increment:** A0R6 - Approved-Byte Authority Correction

**Outcome:** Protected source preparation permitted only after release; historical inputs and diagnosis
remain blocked

**Final independent result:** `No findings`

**Base G0R5:** `cd6ee62e8fe0b744bd8111959e21842e2de39a45`

**Final P0R6:** `67fd65cc11b3c5b4dad0901ee38133a9bfa4d885`

**Final P0R6 tree:** `b233e0fc67b63fa3099ee9cdc8081dce13fa9313`

**Governing plan:** `../plans/atlas-v0-a0-approved-byte-authority-correction.md`

**Planned staged-record reviewer:** `a0r6-plan-record-reviewer`

## 1. Exact plan candidate

`P0R6` is the direct child of exact `G0R5`. Its exact no-renames path set is:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-approved-byte-authority-correction.md
    atlas-v0-a0-approved-manifest-corpus-refresh.md
    atlas-v0-a0-historical-authority-diagnosis.md
    atlas-v0-a2-intake-safety-plan.md
```

Its exact blobs are:

```text
README.md
  f399b3c055fc355a9d5e4e12cb8c8252fd44e6e2
atlas-v0-a0-approved-byte-authority-correction.md
  bbfc0e355ab14f29fabe3ab0405305b8abf2c0ad
atlas-v0-a0-approved-manifest-corpus-refresh.md
  c01fb099257337a97f14ba09150475ad0287024a
atlas-v0-a0-historical-authority-diagnosis.md
  bc22ecbeaf135613e6fc068ff931896870aa40ea
atlas-v0-a2-intake-safety-plan.md
  130258bd31036b445ad33dc3b9b3bd3432de7ef5
```

The governing plan's SHA-256 is:

```text
c8ab9180dcef4a6a86cc0a8ef43c323f98bb1eb5b66703dc54cb1bf8babe6436
```

`P0R6` was committed unchanged from the final reviewed candidate, pushed, and verified as the clean
shared branch tip before this record was authored.

## 2. Authority adjudication

Before plan authorship, independent ideal-first and adversarial repository-safe adjudications separated
five concerns:

| Concern                    | Retained authority                                                       |
| -------------------------- | ------------------------------------------------------------------------ |
| Exact identity/integrity   | SHA-256 of the original approved raw bytes                               |
| Authorization/provenance   | Exact approval envelope and original A0 decision reference               |
| Parseability and schema    | One strict released versioned `atlas-intake/v2` reader                   |
| Corpus and semantic policy | Complete manifest consistency, reason, save, and definition validations  |
| Producer normal form       | Canonical serialization for newly generated artifacts, not historical ID |

Both adjudicators concluded that current-serializer reserialization equality adds no independent
identity, approval, schema, or semantic-policy property once the exact original bytes are digest-bound,
strictly parsed, and approved. Preserving it would let producer-serializer evolution revoke immutable
approval; rewriting the manifest would change its digest and invalidate its anchor and approval.

The project leader approved removal of only that historical-consumer predicate while retaining every
other authority layer and prohibiting rewrite, historical serializer reconstruction, known-digest
exceptions, alternate readers, and compatibility fallback.

The only substantive technical concern was a potential read-to-read substitution gap. The strongest
retained guardrail closes it: one released manifest read returns both the exact bytes used for the
digest and the parsed document used for all envelope and semantic-policy checks.

No adjudicator or reviewer accessed or inferred the private byte difference, its cause, an old
serializer, or any later policy result.

## 3. Review iterations

Every reviewer was independent of plan authorship and used GPT-5.6 Sol. Review input was limited to
repository-safe plans, records, released source and API behavior, Git facts, the adjudication, and the
complete candidate. No reviewer received a historical request or manifest, protected runtime state,
runtime locator, game tree, private path, private value, count, hash, difference, candidate, or corpus
content.

| Candidate                     | Reviewer               | Result        |
| ----------------------------- | ---------------------- | ------------- |
| Initial complete plan         | `a0r6-plan-reviewer`   | 1 TP          |
| Final complete plan candidate | `a0r6-plan-rereviewer` | `No findings` |

The final reviewer re-examined the complete five-file candidate, authority adjudication, prior finding
and disposition, lifecycle edits, single-read pipeline, fixed results, source gate, privacy, and
continuation rather than only the correction.

## 4. Planning-drift gate

Before correction and final release, the candidate was checked against the ideal minimum:

- A0R5 remains closed and consumed; A0R6 is a new increment and never reads A0R5 runtime state;
- exact original approved bytes remain immutable identity through their existing digest and approval;
- one strict released read supplies both the exact digest buffer and the parsed policy object;
- only the historical current-serializer equality predicate is deleted;
- every strict contract, approval-envelope, reason, save, and definition policy remains fail-closed;
- canonical serialization remains mandatory for newly generated control artifacts and future outputs;
- no manifest rewrite, old serializer, exception, fallback, production change, locator, current-tree
  access, candidate, A2, or A3 work occurs;
- one new marker permits at most one corrected no-locator replay after exact source qualification;
- ready means only corrected historical-gate completion and grants no census or candidate authority;
- no receipt-class token exists without a matching complete receipt; and
- every branch closes result-safely and returns to separately authorized planning.

The plan adds no new corpus policy, private format, approval process, runtime locator, threat model, or
hostile-local defense. The new `ManifestDocument` group is only the minimum one-read replacement for
A0R5's separate contract and byte-access groups.

## 5. TP/FP adjudication

The one finding was adjudicated TP. No finding was FP.

| #   | Finding                                                                 | Correction                                                                                |
| --- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| 1   | A0R3 section 2.2 still called the anchored input exact canonical bytes. | Call them exact original approved bytes so no unsupported canonicality remains operative. |

Final review verified the correction in the complete candidate. It does not expand supersession beyond
the historical-consumer canonical-equality requirement and changes no semantic policy.

## 6. Validation evidence

Repository hooks validated exact `P0R6` Markdown with EditorConfig, typos, markdownlint-cli2, and
Prettier. `git diff --check` passed. Git verification proved:

- exact `G0R5` is `P0R6`'s direct parent;
- `G0R5..P0R6` changes exactly the five paths and blobs in section 1;
- committed `P0R6` tree equals the final reviewed candidate;
- `P0R6` equals the remote development-branch tip; and
- the worktree was clean before record authorship.

No historical input, protected runtime state, runtime locator, current-tree metadata, source content,
or candidate was read during planning and review. No A2 or A3 operation occurred.

## 7. Accepted boundary

After verified shared `R0R6`, work may:

1. create a new protected A0R6 workspace containing only the exact qualified final A0R5 project and
   source bytes plus an empty state directory;
2. modify only copied project and source to implement the reviewed A0R6 plan;
3. delete canonical historical acceptance, double-read, A0R5 identity, and obsolete fixture machinery;
4. retain one strict released load returning both exact original bytes and its parsed manifest object;
5. format, rebuild twice, run the complete synthetic suite, and prove qualified output byte stability;
6. create the exact immutable source-binding document;
7. independently review the complete source, project, binaries, tests, and binding until
   `No findings`; and
8. author and independently review the result-safe `S0R6` source-qualification record.

Neither `P0R6` nor `R0R6` authorizes reading either historical input, publishing a marker or receipt,
diagnosing, reading A0R5 state, creating a locator, accessing a current tree, publishing a candidate,
correcting source from private evidence, or performing A2, A3, production, or original-data work.

Only verified shared `S0R6` may authorize one consuming corrected historical replay. A pure preflight
refusal before any marker path may be corrected and reinvoked with a fresh run ID. Any complete,
partial, or zero-byte marker consumes the attempt.

## 8. R0R6 release gate

This proposed record grants protected source-preparation authority only. Work may continue only after:

1. this exact staged record receives independent `No findings`;
2. it is committed unchanged as `R0R6`, the direct child of exact `P0R6`;
3. `P0R6..R0R6` adds only this record path;
4. the committed record blob equals the reviewed staged blob;
5. `R0R6` is pushed and verified as the clean shared branch tip; and
6. no historical input read, marker, receipt, or diagnosis occurs before exact reviewed, committed,
   pushed, and verified `S0R6`.
