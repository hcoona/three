# NuGet Delivery Retrospective

The first .NET smoke took substantial time because it was also the first complete
NuGet integration of Workflow Delivery v3. The small library was the payload;
Provider evaluation, frozen builds, Qualification, publication, native acceptance,
Windows execution, Governance, Approval, destination-byte verification and clean
consumption all had to work together.

There were also avoidable detours. The work temporarily depended on a stronger
service guarantee than the owner needed. Several implementation and private-tool
assumptions were discovered only when the real workflow reached them. Recovery,
evidence handling and coordination then enlarged the work. The coordinating Agent
owns those execution choices; the costs should not be attributed entirely to
GitHub, .NET or repository governance.

This is a synthesis for the next v3 implementer and reviewer, maintained by the
project maintainers when later reuse changes a lesson. It explains the concrete
failure modes behind the recommendations in the [next-Agent guide](#next-agent-guide).
The [main handoff](../agent-handoff.md#starting-a-new-session) routes new sessions
here. Existing [engineering guidance][engineering] already owns the general
principles; this record introduces no new skill, release gate or product requirement.

## Completed Outcome and Evidence Scope

The selected `Hcoona.ReleaseSmoke.GithubPackages` package, version
`1.0.0-beta.253.g6920074`, completed verified go-live on September 15, 2026.
The [canonical normal-Live evidence][live-evidence] binds Windows publication
[34995015270, attempt 1][normal-run], source
`6920074c47b6f6ff805013f7f31dbb0090870938`, the original qualified package,
current-run Approval, fresh destination bytes and a clean consumer.
[PR #709][docs-pr] delivered the completion documentation; its
[final delivery audit][delivery-audit] retains the accepted protected delivery.

The accepted [NuGet requirement][nuget-requirements] relies on active-version
uniqueness and duplicate non-replacement. It requires no request ordering,
selected winner, linearizability, serialized-only publishing restriction or
separate GitHub Support statement. This dependency boundary is not a proof of
universal concurrent-service behavior. Duplicate observations and byte checks
do not establish behavior under every concurrent write or service failure.

The npm and NuGet smoke objectives are complete. Their operational allowances
remain spent. A different project or destination needs its own applicable scope
and remaining gates; this retrospective does not authorize those operations.

This repository synthesis uses the retained repository evidence and published
review carriers cited below. Source findings and previously audited runtime
observations remain evidence at their original scope. Lessons are retrospective
inferences; proposed improvements are recommendations, not measured outcomes.
Private session logs, local file paths and prior Agent handles are not recovery
prerequisites. Original runtime observations and exhaustive inventories were
not replayed to prepare this document.

## What Took Time

| Stage                       | Retained evidence                                                                                                                                                                                                                                                                    | Interpretation                                                                                                       |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------- |
| Initial integration         | The September 9 [research history][research] found npm-bound Provider/publication support, deliberately removed historical smoke projects, NBGV frozen-version replay constraints, SDK NuGet-client identity differences and ordinary push retry behavior.                           | Compiling a small library could not complete the release journey. This was real first-time integration work.         |
| Service dependency decision | The September 14 [uniqueness reconciliation][uniqueness] removed the separate service-owned atomic statement prerequisite after the owner rejected both linear consistency and a serialized-only alternative.                                                                        | A requirement detour enlarged the task beyond the needed dependency boundary.                                        |
| Native protocol integration | [Create-response evidence][push-response] showed complete HTTP 200 where the initial contract expected 201. [Artifact/package downloads][downloads] used redirects; the distinct [native restore host][consumer-redirect] still rejected a package 302 after other paths were fixed. | Some actual platform behavior needed bounded observation. Fragmented handling amplified the correction scope.        |
| Normal R1/R2/R3             | Independent triage identified a [timestamp mismatch][clock], [lost blocked-result evidence][shell], and then an [LF/CRLF identity mismatch][newline].                                                                                                                                | Three concrete integration failures, not an unexplained NuGet outage. The original R2 blocked cause remains unknown. |
| Publication and closure     | [Private metadata predicates][metadata], a [helper basename][helper], an [omitted migration-status consumer][migration] and [overwritten frozen draft][draft] required correction.                                                                                                   | Avoidable private-tool and evidence-management work continued around the delivery.                                   |

The necessary technical work remains valuable: frozen-version projection, actual
SDK/client identity, one-shot push behavior within accepted operation bounds,
safe response-selected downloads, strict admission, and destination/consumer
verification. Future work should reuse these capabilities and check their
integration boundaries earlier.

### Observable Cost and Limits

The [final documentation delivery audit][delivery-audit] records 5,927 tests in
501.23 seconds for its two-file correction, seven applicable hook steps and
commitlint. Its complete delivery inventory contains 514 files and 2,521,571
bytes. These are inherited measurements of that bounded delivery, not the entire
NuGet effort and not a claim that every check was necessary or dispensable.

The original [postmerge CI run][postmerge-ci] started at 19:42:28 UTC and reached
its terminal update at 20:02:35 UTC: 20 minutes 7 seconds, with all 11 jobs
successful. This interval includes parallel jobs and cannot be added as though
each job consumed separate developer time.

One [helper-input closure][helper] checked 1,549 lineage hashes and 10,784
preservation entries. That demonstrates the size of its evidence workload; it
does not establish that those integrity checks should have been omitted.
The documented corrections and their review/retention work support an inference
of substantial recovery overhead. They do not establish a waste percentage,
monetary cost, or reliable counterfactual delivery duration.

## Failure Modes and Corrections

| Failure mode                                            | Evidence and consequence                                                                                                                                                                                                            | Apply on the next relevant task                                                                                                                                                                                                                                                                  |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Service responsibility exceeded the required dependency | The owner rejected a linear-consistency prerequisite and the serialized-only alternative; accepted reconciliation removed the separate service statement gate. [Uniqueness decision][uniqueness]                                    | Start from the accepted user-visible property. Distinguish GitHub's responsibility from application request/result checks. Reopen the service question only when a new requirement depends on it.                                                                                                |
| Protocol assumptions differed across entry points       | Complete create HTTP 200 invalidated a 201-only assumption in the profile and four consumers. Redirect handling still missed a distinct native restore host. [Push response][push-response]; [consumer redirect][consumer-redirect] | Map the actual producers and consumers before changing a contract. Apply the accepted status set and bounded redirect policy consistently. Share code or a contract where concrete consumers justify it.                                                                                         |
| Clock fixtures masked a real boundary                   | The real clock produced microseconds, while a strict consumer required whole seconds. The passing fixture supplied a whole-second clock and mocked relevant readers. [Normal R1 triage][clock]                                      | Exercise the producer-to-validator boundary with a representative fractional instant. Preserve the strict contract and project current time at its producer.                                                                                                                                     |
| Handled failure lost its evidence                       | R2 accepted `blocked/1` but left `$LASTEXITCODE=1`; Actions skipped seal/upload and lost the decision. [Normal R2 triage][shell]                                                                                                    | Exercise pass and valid blocked results through actual shell semantics. Retain the validated decision successfully, then propagate blocked status. Do not make blocked mean pass or invent its missing historical reason.                                                                        |
| Windows checkout changed admitted source bytes          | R3's 31-field profile differed only in adapter SHA. Exactly 1,085 LF-to-CRLF conversions reproduced the failed hash; the observer and Normal workflow had different conversion settings. [Normal R3 triage][newline]                | Preserve the required raw bytes with the narrow accepted LF policy. Check source/Provider checkout behavior under Windows conversion settings before depending on that identity. Do not admit transformed bytes merely to bypass rejection.                                                      |
| Test environment and local storage assumptions leaked   | A fixture inherited `GITHUB_ACTIONS=true`, causing nine CI failures. A separate local incident directly identified inode exhaustion despite free byte capacity. [CI triage][ci-fixture]; [local incident][local-storage]            | Isolate fixture environment variables. Inspect inode and byte capacity on setup failure; use an owned process-local recovery path. Two opaque subprocess failures remained unexplained, so do not attribute every historical failure to inode exhaustion.                                        |
| Private tooling added an undeclared prerequisite        | A collector demanded nonempty waiting `environment_url`; the actual field was empty while the required immutable Summary was independently bound. [Metadata triage][metadata]                                                       | Trace private predicates to requirements or necessary joins. Check optional metadata shapes. Use justified offline derivation when preserved evidence already answers the question.                                                                                                              |
| Hash equality was mistaken for consumer readiness       | The helper archive had correct bytes at the wrong basename; `read_uploaded` rejected it. A byte-identical copy at the required name resolved the input defect. [Helper closure][helper]                                             | Check real consumer predicates, including basename, path/type and content, before consuming an operational lifetime. A matching hash alone is insufficient.                                                                                                                                      |
| Closure omitted an authoritative consumer               | Five completion documents omitted the migration policy, leaving contradictory current status. Copilot found it after earlier independent reviews and a handoff probe; scope became six paths. [Migration triage][migration]         | Resolve current-status consumers from the authority portal and search the affected concern for remaining pending claims. Review counts do not prove exhaustive coverage.                                                                                                                         |
| The coordinating Agent reused a frozen path             | The reviewed PR-body draft was overwritten with the final body. Recovery restored the draft and preserved the final separately; the exact overwrite time remains unknown. [Retention closure][draft]                                | Use distinct draft/final paths and exclusive creation after freeze. Bind the body before consumption. Never rewrite historical commands to pretend they used a later recovery path.                                                                                                              |
| Recovery evidence and coordination became costly        | Large retained inventories and multiple correction/closure rounds are visible in the [helper closure][helper] and [delivery audit][delivery-audit].                                                                                 | Give each reviewer one bounded subject, evidence set and completion condition. Communicate changed facts at useful boundaries. Reuse applicable accepted evidence and keep one current checkpoint with pointers. These are cost-control recommendations, not permission to omit required checks. |

## What Review Established

Open Code Review delegation was used in the [retained research/source reviews][research].
OCR supplied deterministic selection and rule resolution; independent Agents
performed substantive review. Markdown exclusions or an empty preview did not
shrink the explicit complete-path scope. Source review, actual-operation audit,
finding triage and handoff probing were distinct activities.

This does not mean one OCR run reviewed every action in the whole trajectory.
The later clock, shell, newline, metadata, basename and migration-status findings
show the limits of the earlier fixtures and scopes. Review found real defects
and preserved accountable corrections; it did not make integration failures
impossible.

Useful bounded reuse already happened. For the final two-file correction, the
other four documents retained their earlier review. Unchanged handoff entry
points did not trigger another five-task takeover probe [delivery audit][delivery-audit].
Preserve that pattern under the applicable policy. Changing an accepted gate
requires its own normally reviewed change.

## Next-Agent Guide

Start with the [new-session entry](../agent-handoff.md#starting-a-new-session).
The following recommendations apply inside the next task's accepted authorities;
they do not add release gates.

1. **Recover completed state once.** Identify the owner's next project and
   user-visible result, the accepted source baseline and applicable work carrier.
   Keep the finished smoke operations closed. If the new project/result has not
   been selected, establish that scope before project-specific implementation.
2. **Separate reusable capability from project-specific gaps.** Assess descriptor,
   version policy, declared inputs, target environment, package permissions and
   real consumer behavior. Reuse unchanged Provider/publisher implementation.
   Determine separately whether prior native/profile/Governance evidence applies;
   smoke-specific evidence and permissions do not automatically admit another
   project. A different registry is a different integration boundary.
3. **Resolve credential sources without exposing values.** For authorized local
   operations, `gh`'s configured credential source can be a candidate; CLI login
   alone does not prove package-read permission or a suitable token type. CI uses
   its configured injected source with the required job/package permissions.
   Follow the [NuGet authentication boundaries][nuget-handoff] and ask only for
   information or authorization still missing from the actual task.
4. **Check affected integration boundaries early.** Where relevant, reuse checks
   for actual timestamps, Actions PowerShell exit semantics, Windows byte
   conversion, HTTP status/redirect handling and real helper-input predicates.
   Complete mandatory hooks/CI at their required boundary. Avoid duplicate
   equivalent commands when the accepted validation scope already covers them.
5. **Review concrete changes and their consumers.** Supply the complete manifest,
   applicable rules, successful relevant validation and changed assumptions.
   Preserve independent review and material-finding triage. Inherit unchanged
   evidence only where its applicability and policy permit.
6. **Bound coordination and recovery.** Keep ownership, source, last accepted
   result, exact next action and evidence pointers in the existing work carrier.
   Batch independent reads and communicate changes while reviewers work.
   Keep frozen evidence separate from mutable drafts.
7. **Complete only the new journey's remaining gates.** Authorized runtime work
   still needs its concrete bounded request, current authority/profile checks and
   native/publication prerequisites. Preserve terminal failure evidence; do not
   replay spent operations or reconstruct the finished smoke as a new task.

### Evaluate Reuse on the Next Authorized Project

Use the existing work carrier to record:

- which Provider/adapter/publisher components were reused and why changes were needed;
- integration failures and where they were caught, including recurrence of known defects;
- first-pass stage outcomes and elapsed time, distinguishing waiting where evidence permits;
- inherited versus new reviews/checks, with reruns tied to changed inputs, failures or accepted triggers; and
- the project-specific user result and required evidence.

Compare like-for-like scope and avoid invented target durations. Possible
follow-on code improvements include shared protocol fixtures, private-input
validation and frozen-file creation. They are proposals, not changes implemented
by this retrospective. Add a reusable skill only if another task demonstrates
a stable procedure missing from existing guidance.

## Sources

The repository records retain source identity and the cited GitHub carriers
retain the original reports and, where supplied, hash-bound companions.
Their original evidence limits survive this synthesis. Readers can recover the
lessons from these published records without accessing a prior session or local
operator directory.

[engineering]: ../../../../../../docs/engineering/engineering-principles.md
[live-evidence]: ../validation/nuget-normal-live-evidence.md
[nuget-requirements]: ../requirements.md#nuget-second-slice
[nuget-handoff]: ../nuget-smoke-research-handoff.md
[research]: nuget-smoke-evidence.md
[uniqueness]: nuget-smoke-evidence.md#2026-09-14-query--adopt-the-nuget-uniqueness-dependency
[push-response]: nuget-smoke-evidence.md#2026-09-14-query--correct-the-nuget-push-response-contract
[downloads]: nuget-smoke-evidence.md#2026-09-15-query--bounded-response-selected-downloads
[consumer-redirect]: nuget-smoke-evidence.md#2026-09-15-query--native-consumer-package-redirect
[normal-run]: https://github.com/hcoona/three/actions/runs/34995015270
[docs-pr]: https://github.com/hcoona/three/pull/709
[postmerge-ci]: https://github.com/hcoona/three/actions/runs/35015221998
[clock]: https://github.com/hcoona/three/issues/676#issuecomment-5679663777
[shell]: https://github.com/hcoona/three/issues/676#issuecomment-5680539843
[newline]: https://github.com/hcoona/three/issues/676#issuecomment-5682286336
[ci-fixture]: https://github.com/hcoona/three/pull/700#issuecomment-5667936646
[local-storage]: https://github.com/hcoona/three/issues/676#issuecomment-5645328358
[metadata]: https://github.com/hcoona/three/issues/676#issuecomment-5684408691
[helper]: https://github.com/hcoona/three/issues/676#issuecomment-5685724253
[migration]: https://github.com/hcoona/three/issues/676#issuecomment-5686417763
[draft]: https://github.com/hcoona/three/issues/676#issuecomment-5686435863
[delivery-audit]: https://github.com/hcoona/three/issues/676#issuecomment-5687395910
