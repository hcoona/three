# Hosted TestPyPI Partial-Publication Proof Protocol

## Authority, ownership and evidence level

The [Delivery Wave](../../../../../../docs/delivery-wave.md) grants the owner's
[approved bounded proof](https://github.com/hcoona/three/issues/843#issuecomment-5878115110).
This protocol owns its execution and audit boundaries. The V3 maintainer maintains
it; operators and independent reviewers consume it before effect admission and
at closeout. The [LLD](../hcoona-release-smoke-python-lld.md#temporary-hosted-recovery-proof)
owns the temporary control contract. PR #905 validates local real rebuilds and
controlled transport; hosted recovery is still unexecuted. The completed normal
publication of `0.1.0b26` is separate evidence.

Design acceptance does not skip tested implementation, protected delivery,
independent exact-source/operator admission or current-run Approval. Keep the
new five-scenario inventory separate from stopped campaigns. Routine
in-scope correction and review are delegated without repeated owner approval.

## Fixed subject and intended sequence

Use only TestPyPI project `hcoona-release-smoke-python`, repository
`hcoona/three`, accepted actor `hcoona`, protected `main`, workflow
`workflow-delivery-v3-python-smoke.yml`, and Environment
`workflow-delivery-v3-python-testpypi`. Carry forward the confirmed Sole Owner
and publisher registration. Preserve the workflow filename and registrations.

1. Deliver a narrowly scoped, default-off hosted interruption control in the
   existing workflow. Only a reviewed TestPyPI proof request may select it.
   Bind the selection to the actual current-run request and show it before
   Environment approval; reject it for PyPI, PR CI, unapproved subjects or an
   ineligible pre-state. It is not a generic production retry feature.
2. Pin an independently reviewed protected source/tooling SHA, actual public
   NBGV version, complete frozen inputs, exact filenames and successful
   exact-merge checks. Require a new absent coordinate. Both runs of the proof
   select the same SHA and use their own fresh builds and qualification.
   The request setting must not change distribution bytes or their witness.
3. In the first Attempt, approve the full pair with the interruption disclosed.
   Perform the real wheel upload and required exact TestPyPI readback. Only
   after that succeeds, stop before any sdist POST. Retain a truthful failed
   Result/Outcome, successful wheel evidence and `not-attempted` sdist.
   Independently verify the terminal run and wheel-only destination state.
   This is an intentionally interrupted client operation, not an observed
   TestPyPI outage or service rejection. Do not fabricate a native response.
4. Dispatch a different run at platform attempt 1 with interruption off. It
   independently rebuilds and qualifies both formats. Download and compare
   the existing wheel to its newly built original, including filename,
   bytes/digest, metadata and witness. Review its fresh Approval showing
   `wheel: already-present` and `sdist: upload`. Upload only the missing sdist.
5. Independently replay the current-run records and terminal scalar, require
   an authoritative `published` Outcome, and fetch the complete pair afresh
   from TestPyPI. Verify both against the recovery Attempt's originals and
   run separate clean wheel and sdist consumers. Preserve the first Attempt's
   failure unchanged. Stop at the first fully audited hosted recovery success.

No old-target override, source rewrite, reused artifact, inherited Approval or
GitHub rerun is introduced. If protected-main movement changes the admitted
target/witness, stop that scenario; do not retarget its partial version.
The proof establishes the wheel-present/sdist-missing hosted path. The inverse
subset remains covered by local integration tests, with no hosted claim.

## Finite campaign and autonomous continuation

Use one new persistent campaign inventory, disjoint from all stopped ledgers.
Permit at most five sequential scenario slots, each bound to one new version
and one fixed protected source. Each slot permits at most four fresh workflow
dispatches and at most four reached or uncertain file-upload POSTs. Thus the
whole campaign permits at most 20 dispatches and 20 file-upload POSTs over at
most five new versions. These are ceilings, not targets. A normal successful
proof consumes two dispatches and two file uploads on one version.

Each Attempt permits at most one Environment-approval POST, one OIDC assertion
acquisition/token exchange, and one upload invocation per missing file under
the existing per-file profile. Reserve dispatch and possible upload effects
before sending/approval; failed or uncertain sends consume their allowance.
No slot, deadline, ledger or effect allowance may be reset or replenished.
Only one operation/runner publication lifetime may be active at a time.

The operator has a cumulative four-hour lifetime from original reservation,
including construction, waiting and audit. No resume or tool correction resets
that deadline or the unchanged Governance expiry. Ordinary scoped evidence reads
use resource controls, not owner-replenished cumulative request allowances.
Retain every request and response/error as diagnostic evidence. Dispatch,
approval, credential acquisition/exchange and file POST reservations retain
their separate one-shot and cumulative limits.

Collect serially with 30-second socket timeouts, at most five pages of 100
entries per inventory and at most 32 required artifact IDs. Keep per-response
limits of 8 MiB for GitHub JSON, 16 MiB for an artifact and 32 MiB for a log,
and cumulative transfer guards of 512 MiB for artifacts and 128 MiB for logs.
Only artifact/log service transfers may follow admitted redirects; count each
hop and never forward administrative credentials. Control and registry reads
do not redirect. These local resource guards report incomplete evidence rather
than request an owner quota top-up or automatically start another publisher.

Run discovery/state polls start at least 30 seconds apart. Independent registry
polls back off through 30, 60, 120 and 300 seconds, then remain at least five
minutes apart. Honor any longer applicable service-directed Retry-After,
GitHub rate-reset or poll-interval wait. Persist next eligible time and error
state across invocations; a restart cannot shorten a wait. Do not start a wait
or request beyond the remaining original lifetime. Five consecutive transient
transport/service/rate-limit failures stop the read stage; a valid regressed
index is a pending observation, not a transport error. Permission failures,
invalid TLS, scope violations, malformed authoritative content and digest or
file-set conflicts stop immediately. A 403 is not automatically a rate limit.

Retry only idempotent evidence GETs for transient failures, respecting the
same identity, origin and resource limits. Retain failed/truncated responses
and validate a resumed transfer against the original artifact identity and
digest. Reuse complete immutable captures; download only evidence needed by
the current DAG or admitted native audit. A missing run listing never permits
a second dispatch. Read recovery never retries a POST, reacquires credentials,
or bypasses a cache. Ordinary locked dependency preparation, CI and local
clean consumers retain their existing scope.

The campaign ends at first independently audited success, owner cancellation,
exhaustion of these bounds, or the existing Governance expiry
`2026-10-05T02:27:04.836609Z`, whichever comes first. Governance must remain
fresh and unchanged at every admission; no renewal is included.

Every failure stops its Attempt's mutations. Further work within the envelope
requires independent evidence that its unique run is terminal, no publisher
can still send, the dispatch identity is resolved, and current state is known
from the allowed retained/native evidence. Missing logs or missing Result are
not proof of absence. No blind resend or same-Attempt retry is permitted.

After a diagnosed pre-upload failure, the agent may correct, test, review and
protected-deliver the cause and start a fresh eligible Attempt/slot. After a
partial or ambiguous upload, the old Outcome remains failed/unknown. A fresh
recovery Attempt may proceed only after independent audit resolves current
state to the exact expected subset, rechecks compatible frozen inputs and
remaining cumulative effects, and validates its own fresh Approval. Already
present files are never POSTed. A complete exact pair after an ambiguous
failure does not retroactively turn that failed run into the hosted recovery
proof; preserve it and select a fresh scenario within remaining bounds.

Unexpected/conflicting/extra/yanked files, unresolved dispatch or mutation,
changed ownership/configuration/profile/Governance, or unresolved scope/risk
findings stop dependent external effects. A diagnosed tooling correction and
new scenario inside the accepted envelope do not require another approval;
an expansion of the subject, effect bounds or trust assumptions does.

## Runtime and concrete caller admission

Use one scenario directory per ordinal and a new Attempt directory per
dispatch reservation. Persist original start/deadline, target/tree/version,
filenames, mode, unchanged Governance/profile digests, protocol and all caller
hashes, exact-merge CI evidence and independent admission before constructing
the operator. Construction failure spends its Attempt; missing/uncertain
construction evidence cannot be reconstructed as unused authority. An
exclusive campaign lock spans each complete operator invocation, and every
resumed invocation reloads counters from the original ledger.

The concrete caller dispatches the stable workflow on `main`, never an SHA
ref override, and checks current main immediately before dispatch and approval.
Identify exactly one new manual attempt-1 run with the fixed target, workflow,
actor and reserved time window. Bind selected proof mode to the recorded
dispatch and current-run Snapshot. Reserve each possibly reached file before
Environment approval; unknown effects remain spent. Complete verified immutable
artifacts are retained by ID and reused for offline replay. Only failed or
incomplete transfers may resume under the same identity and read-resource
controls; no name-based reconstruction of runtime authority is admitted.

Runtime registry calls retain the LLD's exact profile, HTTPS origins,
30-second socket timeout, 2 MiB index/8 MiB file/64 KiB upload-response limits,
single initial and pre-marker observations, and at most six post-upload index
reads in each 60-second admission window with ten-second pending spacing.
No runtime transport retries, cache bypass or token refresh is added. Only missing
files are sent, at most once per Attempt. Runtime protected-control reads stay
at five logical Governance reads (at most ten 120-second Git subprocesses),
five pages of 100 entries per GitHub inventory and 8 MiB per JSON body. The
existing complete mutation path has at most 2,537 GitHub JSON control GETs;
this is not a bound on Git smart-HTTP exchanges or ordinary pinned Actions
artifact/dependency traffic. CI and job timeouts retain their accepted scope.

An independent preapproval gate binds exact current-run artifacts, rebuilt
qualification, Snapshot/mode/dispositions, summary, Bundle and platform
deployment. A seed requires absence and stop mode; recovery requires the
verified wheel-only subset and normal mode. For the intended proof, compare
both fresh original archives with the seed archives as evidence, without
adopting their authority. Every gate binds actual reviewer identity and
retained evidence digests; an author-produced `passed` flag is insufficient.

After terminal collection, an independent diagnostic-read gate first resolves
the unique dispatch/run identity and proves no publisher can still send.
Within the original lifetime and read-resource controls, this gate may admit
that Attempt's independent index observations and required file downloads
after failed or unknown terminal evidence, including a marker-only or missing
Result. Retain raw sanitized responses and exact bytes. Missing logs/Result alone do not establish
identity, liveness or absence; unresolved dispatch/liveness blocks the read.
This diagnostic admission neither accepts the proof nor changes a failed or
unknown Outcome, and never releases spent uncertain effects. An independently
verified exact subset, compatible source/frozen inputs and remaining cumulative
budgets are still necessary before a new recovery Attempt.

The stronger proof-acceptance gates independently replay actual current-DAG
references, Authorization/marker/Result and original scalar terminal with
actual job/step conclusions. The seed proof requires failed partial publication,
wheel success/readback, no sdist invocation and the fresh exact wheel-only
destination audit. The recovery proof requires published, only the missing
sdist POST, the fresh complete pair audit and clean consumers on those downloaded
files. A diagnostic read after an ambiguous/failed upload cannot manufacture
these success facts. Diagnosis and proof share one persistent audit history and original deadline;
changing purpose does not discard responses, failures or resource usage.

A native index demonstrably regressed relative to retained successful runtime
readback leaves the independent audit pending. Preserve it and wait under the
read pacing policy; it does not establish current absence, undo the earlier
upload or authorize resending an existing file. Serial progression is only a
consistency check, never proof of contents. Accept only the expected target
inventory with exact freshly downloaded bytes. Unexpected/yanked/conflicting
files, differing bytes or a non-regressed incompatible state stop the audit.
A newer serial alone is insufficient. Deadline exhaustion leaves evidence
incomplete and dependent mutation stopped; it is not a quota-refill request.

### Read-only continuation across a protected operator correction

A tested, independently admitted protected operator correction may continue a
terminal Attempt's incomplete read-only audit within its original deadline.
Keep its original source/version, execution binding, caller snapshot, ledgers,
responses, failed Outcome and reservations immutable. Bind the correction's
exact protected source, caller/protocol hashes and original evidence in a
separate read-only continuation record and append-only audit history. Independent
admission must reestablish dispatch identity and publisher quiescence and
verify the original start/deadline and unchanged Governance/profile. The
continuation cannot dispatch, approve, acquire credentials, upload or change
source authority; historical artifact readers use their pinned contract.

The narrow continuation has only registry GET capability; it does not inherit
the normal operator's dispatch or approval methods. Its strict binding records
original scenario/Attempt/run/source/tree/version/mode, original binding and
ledger digests, original start/deadline, Governance/profile/frozen-input digests,
new protected caller source/tree and complete imported caller hashes, accepted
protocol digest, pinned historical reader checkout/source/tree, and inspected
original evidence hashes. Paths remain inside the admitted roots. Require a
clean exact reader checkout and verify the actually imported reader module;
new caller source and old publication subject are distinct identities.

Retain the binding, independent admission, accepted protocol, chronological
read ledger, sanitized response bodies/headers and final completion separately
under the original Attempt. The original failed read contributes to that
history and pacing; do not reset it as unused. Persist next eligible request
time, backoff position, consecutive-error count and completion/stop state under
the existing campaign lock. A request interrupted after reservation remains
recorded as uncertain; only a new idempotent read may follow. Completion needs
the exact expected wheel-only inventory and bytes, not merely a serial value.
No new GitHub or credential operation is needed for this terminal continuation.

Independent closure binds both the original and supplementary evidence. It
requires resolved current destination state; unknown-state abandonment is not
granted. A changed protected main stops same-version recovery. Only after
known-state closure may a new-version scenario use a newly admitted source and
its remaining original campaign slots. History validates each Attempt against
its own pinned protocol/callers, without rewriting prior admission or refilling
any effect allowance. Preserve the original protocol bytes when upgrading the
operator. A complete audit does not rewrite a failed publication Outcome or
manufacture a successful two-Attempt proof.

Native approval records retain response digests and structured platform facts,
not every original administrative API body. Later readback corroborates
identity but cannot reproduce a mutable original response digest. Keep this
existing provenance limit. A failed or incomplete terminal audit authorizes no
success claim; unresolved identity/effects blocks successor mutation. Offline
reprocessing never changes the original Outcome or replenishes requests.

## Delivery, evidence and exclusions

Before effects: owner acceptance recorded in Issue #843; independently
reviewed Wave merged; affected design/protocol accepted; interruption control,
strict consumers and operators tested, independently reviewed and protected
delivered; exact source, current admission and concrete operation bindings
independently checked. Current-run native Environment approval remains
mandatory; the agent may submit it only after its fresh evidence gate passes.
The injection control must preserve normal default behavior and truthful
terminal replay. The implementation gate must establish that no sdist request
is sent during the intended stop, and that recovery uses the ordinary path.

Keep raw sanitized TestPyPI responses and bytes, current-run artifact
identities/digests and DAG bindings, source/toolchain inputs, both independent
builds, approval/marker/Result/Outcome, interruption evidence and independent
consumer/audit results. Publish durable evidence and limited conclusions in
the existing Issue/PR carriers and project validation authority. Do not retain
tokens, authorization headers or signed download URLs in public evidence.

After successful audit, protected-deliver removal of the temporary proof
control and completed Wave entry, retain the tested recovery capability and
historical evidence, and update status routes. The V3 maintainer maintains the
protocol; operators and independent reviewers consume it at admission and
audit. A project validation record is justified for replayable hosted proof;
Issue #843 remains the proposal/progress carrier rather than a second authority.

Production PyPI stays blocked. No publisher registration, Environment/access,
credential, host trust or destination/profile change; no Governance renewal;
no duplicate-upload/race probe; no deletion, rollback, cleanup or changes to
earlier versions (including `0.1.0b6` and `0.1.0b26`). Preserve unrelated Waves.
No arbitrary old-version recovery or extra inverse-subset hosted experiment.
