# Workflow Delivery v3 AI Agent Handoff

## Authority and Purpose

Read this page before acting on Workflow Delivery v3.
The [document-set entry](./README.md) states the authority boundary and source
provenance for these records.

This is an operating handoff, not a second specification. The
[requirements](./requirements.md) and replacement
[HLD](./high-level-design.md) govern new contraction design. The
[middle-level design](./middle-level-design.md) defines component, selection,
execution, transfer and outcome contracts. The HLD owns terminology and
implementation order. The [implementation plan](./migration-strategy.md) identifies
concrete callers, integration contracts and retirement dependencies.
Root CI owns the adopted Node fixture, selected retained Node matrix and
[native .NET CI group](./migration-strategy.md#native-net-ci-group). Its existing
Windows scope job consumes both .NET endpoint graphs, native event comparison and
control-test resources. The existing .NET job receives the original selected plan
and complete candidate control distribution, executes selected native and adopted
NuGet checks, and collects every required result without replanning. Ordinary
Windows Debug qualification is recorded in
[PR #1054](https://github.com/hcoona/three/pull/1054). The [native Python planning-fact contract](./middle-level-design.md#native-python-planning-facts)
defines the implemented Python extraction boundary. Passive extraction, the pinned Rust
supplement, ordered configuration preflight, metadata adaptation and operation
dependency projection have controlled unit boundaries. The private app's
[bounded native pytest configuration component](../../../../private/app/workflow-delivery/README.md#native-pytest-configuration-component)
also has controlled tests; the separate planning-input observation below qualifies
its exact native query and target/input composition. The [native Python caller](../../../../private/app/workflow-delivery/README.md#native-python-ci-caller)
composes root CI, explicit local full and smoke PR retirement;
[PR #1114](https://github.com/hcoona/three/pull/1114) carries complete ordinary
caller qualification; the
[native Python CI group contract](./middle-level-design.md#native-python-ci-group)
defines its planning, preparation, execution and transfer boundary. The
[paired Python producer-to-owner component](../../../../private/app/workflow-delivery/README.md#paired-python-producer-to-owner-component)
has controlled supplied-fact coverage. The
[controlled paired Python fact assembly](../../../../private/app/workflow-delivery/README.md#controlled-paired-python-fact-assembly)
binds supplied native targets and actual checks. The
[controlled native Python graph collector](../../../../private/app/workflow-delivery/README.md#controlled-native-python-graph-collection)
composes metadata, passive inputs and ordered native dependency answers with
caller operations bound to the same metadata. The separately accepted
[current-tool observation](./research/python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies exact complete ordinary/no-dev graph composition on its pinned Linux
subjects; its campaign and cleanup are complete, and remaining captures stopped. The [controlled Python repository input associations](../../../../private/app/workflow-delivery/README.md#controlled-python-repository-input-associations)
bind supplied actual check owners to repository engineering inputs. The separate
[planning-input observation](./research/python-planning-enrichment-qualification.md#completed-planning-input-observation)
qualifies native pytest, committed NBGV/quality inputs and producer input mapping
on the exact retained Linux subjects; complete caller qualification has its separate
ordinary PR CI gate. The
[controlled Python package component](../../../../private/app/workflow-delivery/README.md#python-package-execution-component)
implements finite adopted preset expansion, preparation and execution. The
[completed corrected Linux package observation](./research/python-native-package-qualification.md#completed-corrected-linux-package-observation)
independently qualifies its exact four required checks and original wheel/Git-free
sdist consumers, with the audit's separately classified filename-discovery method
exception retained. That package campaign is stopped: one lifetime spent and both
unused package slots expired. The
[finite Python CI execution component](../../../../private/app/workflow-delivery/README.md#finite-python-ci-execution-component)
has controlled Git/command-transfer coverage for the original pytest/package plan.
The [native Python CI caller](../../../../private/app/workflow-delivery/README.md#native-python-ci-caller)
implements root CI and committed local full execution; the smoke PR entry is
retired. Actual root pytest and the complete Linux caller have ordinary hosted
qualification in [PR #1114](https://github.com/hcoona/three/pull/1114). Release remains separate.
The [prepared endpoint collector](../../../../private/app/workflow-delivery/README.md#prepared-python-endpoint-collection)
composes existing readers and paired selection with controlled coverage. Its
prepared group CLI binds exact scope/endpoints and transfers candidate-native
execution context. Its native query-tool preparation component has controlled
command coverage. The concrete caller receives complete native sources/control,
materializes exact committed endpoints and executes the original mixed plan.
[PR #1114](https://github.com/hcoona/three/pull/1114) supplied ordinary hosted
qualification of actual Linux preparation and the complete root/local/smoke
replacement together. Later caller changes retain their applicable qualification
gate. The
[primitive Linux observation](./research/python-native-planning.md#primitive-native-observation)
qualifies the pinned ordered helper composition; its campaign is stopped and
both unused diagnostic reserves expired. The complete caller mapping is
implemented; [PR #1114](https://github.com/hcoona/three/pull/1114) carries ordinary hosted qualification. The separate enrichment campaign
and cleanup are complete; both unused diagnostics stopped. Other ecosystem execution cutover and
Release remain pending. The
[private application's entry](../../../../private/app/workflow-delivery/README.md)
describes its implemented CI selection/result interface, GitHub event comparison,
native Node facts, control-test resource interface and check execution. The
[bounded native observation](./research/node-ci-native-execution.md) qualifies
the adopted fixture composition/effects on Linux. Its finite allowance is stopped
and the unused reserve expired. Root CI's adopted Node group and selected retained
Node matrix have ordinary hosted transfer/execution qualification in
[PR #1042](https://github.com/hcoona/three/pull/1042). The exclusive npm CI shadow
and bootstrap projection are retired. Python caller wiring is implemented with its
ordinary hosted qualification carried by [PR #1114](https://github.com/hcoona/three/pull/1114); other quality presets, Ruby
execution cutover and Release remain pending. The
[requirements transition](./requirements.md#requirements-and-implementation-transition)
defines the retained implementation/evidence scope of the old
[glossary](./architecture-glossary.md),
[pinned former MLDs](./README.md#normative-hierarchy),
[migration policy](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md), and applicable slice LLD
([npm](./hcoona-release-smoke-npm-lld.md) or
[NuGet](./hcoona-release-smoke-github-packages-lld.md), or
[Python](./hcoona-release-smoke-python-lld.md), or
[Ruby](./hcoona-release-smoke-ruby-lld.md)). They do not yet demonstrate
conformity to the contraction requirements. The operating details below retain
the existing implementation and historical campaign limits; they do not
reinstate superseded requirements for replacement design. The owner accepted
[Windows feasibility as an assumption](./research/contraction-feasibility.md#decision-and-evidence-scope)
and skipped a separate experiment; do not restore it as a pending gate.

v3 is the only normative line. Use v1 or v2 only when a v3 document explicitly
requests mechanism extraction and revalidation. Git and delivery PRs carry
chronology. Retained decision evidence lives in
[this project's research and validation records](./README.md#historical-source-rule).
Repository work authorization and generic contribution procedure route to the
[Delivery Wave](../../../../../docs/delivery-wave.md),
[record policy](../../../../../docs/governance/record-system.md), and
[contribution guide](../../../../../CONTRIBUTING.md). The domain gates below
remain additional prerequisites; repository governance does not grant native
operations, publication, or another npm proving run.

## Contraction Entry

The [Wave](../../../../../docs/delivery-wave.md) authorizes the full contraction
tracked by [#984](https://github.com/hcoona/three/issues/984) and its coordinated
work under [#983](https://github.com/hcoona/three/issues/983). Start with the
requirements, replacement HLD, middle-level design and implementation plan, then close affected integration details
before implementation, validation, caller cutover and retirement. Preserve permanent requirement IDs
and the existing consumers of historical evidence. The Wave's effects boundary
does not reopen any completed ecosystem campaign or authorize a new publication.

The [native Ruby planning facts](./middle-level-design.md#native-ruby-planning-facts)
and [CI group contract](./middle-level-design.md#native-ruby-ci-group) define the
next extraction boundary. The [integration order](./migration-strategy.md#native-ruby-ci-integration)
keeps source gem, bundle and installed dependency identities separate and
preserves whole RSpec and adopted gem consumers. Native API composition and actual
caller qualification remain pending; these contracts introduce no Ruby
implementation, publication or campaign continuation.

## Ruby Delivery Entry

The [Ruby normal Live evidence](./validation/ruby-normal-live-evidence.md)
records independently accepted GitHub Packages and RubyGems.org publication,
exact original-byte/witness readback, clean remote consumers and durable
destination stops. Both destination campaigns are complete and stopped. All eleven
protected envelope slots are null; campaign-specific external operation and
configuration callers are retired from invocation use. Stable workflows, publisher bindings,
validators and evidence readers remain for their existing consumers.
The evidence record preserves failed bootstraps, cancelled GitHub normal01,
failed RubyGems normal01 and normal02's successful publication with failed
workflow closure. Completion refunds no slot and authorizes no further
dispatch, Approval, publication or configuration. Historical Governance
and its original expiry remain unchanged.

[Issue #954](https://github.com/hcoona/three/issues/954) retains the completed
work carrier. The [Ruby LLD](./hcoona-release-smoke-ruby-lld.md) and
[operation protocol](./ruby-operation-protocol.md) retain contracts and
historical replay boundaries. Preserve the existing successor ledger,
original evidence, owner-reported account facts and both publisher
associations. Do not repeat account setup, native qualification or any
spent operation. Further work needs its own accepted authority.

## Starting a New Session

The Python two-destination publication and clean-consumer objective is
[independently complete](./validation/python-normal-live-evidence.md): TestPyPI
`0.1.0b26` and production PyPI `0.1.0b53`. Production joins actual uploads in
a failed Attempt with a fresh successful `exact-satisfied` Attempt and a new
native wheel/sdist consumer audit. Both completion campaigns are stopped;
operation-only callers are retired and all prior failures remain failed.
No further operation or Ruby work follows automatically from this completion.

Production PyPI has reviewed ready v2 Governance for
`hcoona-release-smoke-python`, based on the owner's confirmed Sole Owner
`Sherry7290`, exact associated bootstrap and normal publishers, and independently
reviewed GitHub configuration. The [independent admission](https://github.com/hcoona/three/issues/843#issuecomment-5904112737)
and [original inventory](https://github.com/hcoona/three/issues/843#issuecomment-5904112525)
retain the exact report bound by the protected Governance digest. Its
`source-evidence-revision` pins the accepted historical source basis, not the
current main tip. The protected source pins the accepted
`WD-PY-006` basis and retains actual administrator bypass `true` under
`WD-PY-007`. Its expiry remains the original configuration deadline,
`2026-10-06T18:39:51.026223Z`; admission
does not renew the configuration lifetime or authorize publication.

Carry account control, project ownership and both publisher tuples forward
unless changed or contradicted. Preserve repository `hcoona/three`, normal
workflow `workflow-delivery-v3-python-smoke.yml` and Environment
`workflow-delivery-v3-python-pypi`. Do not request unchanged-field confirmation,
repeat registration or ask the owner to disable administrator bypass. The
separate `hcoona-release-smoke` project and existing TestPyPI resources remain
outside this production slice.

The first-project generation is spent. Its
[actual run](https://github.com/hcoona/three/actions/runs/36640775966) failed after
wheel upload returned HTTP 200 and all six bounded P2 index reads returned 404.
The [independent terminal audit](https://github.com/hcoona/three/issues/843#issuecomment-5900585460)
and [public original packet](https://github.com/hcoona/three/issues/843#issuecomment-5900584636)
retain that failure; no sdist upload or final consumer audit ran. Later
owner-confirmed resource facts closed only the
[existing-project prerequisite](https://github.com/hcoona/three/issues/843#issuecomment-5902863345).
Both bootstrap slots are null. No rerun, refill, automatic partial completion,
replacement bootstrap version or deletion is authorized.

The [production audit](./validation/python-normal-live-evidence.md#production-pypi-completion)
retains the two distinct normal Attempts, original source/run identities and
public replay packets. The uploading Attempt remains failed; its successor
freshly rebuilt and qualified both exact files, used no upload authority and
passed its own terminal and native consumer audits. Do not reopen the stopped
campaign, perform more registry reads or reuse its artifacts as new authority.
Future operations need their own applicable [Wave](../../../../../docs/delivery-wave.md)
and domain gates. Administrator bypass does not substitute for current-run
Approval/Authorization when publication requires action.

The [hosted partial-publication recovery](./validation/python-hosted-recovery.md)
is independently complete for TestPyPI `0.1.0b39`: a fresh Attempt rebuilt both
archives byte-identically, retained the existing wheel and uploaded only the
missing sdist. Both clean consumers passed; the seed Outcome remains failed.
The campaign is stopped and its temporary interruption control and operators
are retired. The [ordinary recovery contract](./hcoona-release-smoke-python-lld.md#approval-and-terminal-contract)
retains both exact single-file subsets; the inverse subset has local evidence
only. Preserve the stable workflow/publisher/Environment and historical evidence.
This recovery completion grants no further operation and is distinct from the
completed production audit above.

The npm and NuGet GitHub Packages smoke objectives are complete. Use the
[NuGet completion evidence](./validation/nuget-normal-live-evidence.md) and
the current checkpoints below; earlier delivery history does not create a
new task or revive spent operations.

The [retired fixed-coordinate producer boundary](../README.md) preserves
historical admission and reviewer inspection. Use the current native commands
for newly authorized acceptance work; the removed producer APIs are not
compatibility aliases for them.

For the owner's next Workflow Delivery v3 task:

1. Inspect the actual checkout and read the accepted repository instructions,
   policies and Delivery Wave. Follow the [required reading order](#required-reading-order)
   for the affected concern.
2. Read the [NuGet delivery retrospective and next-Agent guide](./research/nuget-delivery-retrospective.md#next-agent-guide)
   to reuse the completed integration and avoid its diagnosed failure modes.
3. Identify the selected next project, user-visible result and remaining
   requirements from the owner's instruction and current work carrier.
   [Issue #843](https://github.com/hcoona/three/issues/843) tracks Python:
   the completed TestPyPI and production PyPI journey. Ruby requires a separate
   proposal and selected Wave; completion does not authorize starting it.
   Start with the confirmed [`WD-PY-*`](./requirements.md#python-smoke-slice),
   HLD/five MLDs and [Python LLD](./hcoona-release-smoke-python-lld.md), with
   the [source evidence](./research/python-smoke-evidence.md) for claim limits.
   The revised design adopts the owner's
   [platform-reliance decision](https://github.com/hcoona/three/issues/843#issuecomment-5860732497):
   no duplicate-upload or competing-creation probes and no native-generation
   prerequisite. HTTP-200 identical replay may succeed only with required exact
   file/set readback; a rejected or ambiguous upload remains failed.
   The [LLD migration](./hcoona-release-smoke-python-lld.md#python-governance-v2-migration)
   specifies v2 admission without a fake passing native record. Runtime and both
   protected Governance files now use strict v2. TestPyPI has reviewed ready
   admission, and its normal publication and fresh clean wheel/sdist consumers
   are [independently complete](./validation/python-normal-live-evidence.md)
   for `0.1.0b26`; production PyPI publication and separate clean consumers are
   complete for `0.1.0b53`. The duplicate/race producer
   and hosted workflow are removed. Both completion campaigns stopped at their first
   audited success; no further slot, dispatch, Approval, OIDC, upload or registry
   audit is authorized. Do not reopen their operators or reuse their artifacts as a
   new Attempt. Future operations need their own applicable Wave and domain gates.
   Preserve the earlier source-bound
   [failed normal Attempt](https://github.com/hcoona/three/actions/runs/36383200978)
   and [audit](https://github.com/hcoona/three/issues/843#issuecomment-5864320684),
   and the separate [TLS-stopped operation](https://github.com/hcoona/three/issues/843#issuecomment-5862680211).
   Process-local selection of the existing system CA store enabled the later
   operator connection; do not disable TLS validation or change host trust.
   The [migration order](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#python-smoke-delivery) owns
   these stages; design acceptance grants no configuration, OIDC or dispatch.
   Carry forward Backspace7980's confirmed Sole Owner status and all three configured
   TestPyPI publisher tuples (bootstrap, retired native and normal smoke workflows, repository
   `hcoona/three`, Environment `workflow-delivery-v3-python-testpypi`) unless
   changed or contradicted. Do not routinely ask the owner to recheck them.
   The normal entry is `workflow-delivery-v3-python-smoke.yml`; preserve this
   stable entry and existing registrations. Its [owner confirmation](https://github.com/hcoona/three/issues/843#issuecomment-5862180876)
   joins the independently reviewed [GitHub configuration evidence](https://github.com/hcoona/three/issues/843#issuecomment-5862183420).
   The protected TestPyPI Governance file binds the exact attestation digest,
   accepted source-evidence revision and finite inspection/expiry interval.
   Ready admission does not grant dispatch or establish publication completion.
   The [retired native interface](./validation/python-native-readiness.md)
   preserves old evidence readers with both native request slots null.
   The [failed TestPyPI audit](https://github.com/hcoona/three/issues/843#issuecomment-5859869282)
   remains failed: `0.1.0b2` pair verified through C2, identical wheel HTTP 200,
   stop before C3; `0.1.0b3` was not uploaded. The partial bootstrap `0.1.0b6`
   also remains failed and untouched. Missing captures and consumers cannot be
   inferred from source code or the owner's reliance decision. Neither failure
   grants retry, refill, deletion or another generation. Any future normal publication
   needs its own complete pair, Approval, exact readback and clean consumers.

Recovery uses repository records and linked GitHub carriers. A prior
conversation, local operator directory or old Agent handle is not required.
Keep next-task progress in its Issue or PR rather than appending another
session history here.

## Current Checkpoint

- The replacement baseline, bounded static-reference foundation, and
  record-model contraction are merged.
- The disabled Governance/authorization and exact-satisfied unit was merged
  through PR #651 as
  `db5d9f053baf2c16cd32a1e9e9aae38ffb8c2b74`. Its reviewed tree, protected
  merged tree, post-merge Continuous Integration, and CodeQL were verified.
- The Publication/Finalizer requirements, HLD, glossary, MLD, migration, and
  first-slice LLD contraction was delivered through PR #652. Its reviewed and
  protected trees are identical, and required PR and post-merge checks passed.
  Git and PR #652 record the exact delivery chronology.
- The dependency-ordered Publication/Finalizer implementation was delivered
  through PR #653 as `1c0effa8471c58e517cf1c307af65f4ae7b19acb`.
  Component and whole-group gates, independent OCR rereview, final
  contraction, exact reviewed/merged-tree comparison, and post-merge
  Continuous Integration and CodeQL are complete.
- The replacement runtime implements strict Governance v2, active-only
  Observation, fresh
  exact-satisfied proof, profile-bound one-shot publication, immutable
  marker/Result terminal transport, and the tagged current-DAG Outcome.
  Receipt, ActionResult, and the superseded marker and proof formats have no
  runtime aliases.
- Retained-ref compatibility and semantic no-reference proof are complete.
  The exact legacy Environment was removed after the protected delivery
  gates; all other Environment configurations were unchanged.
- The native tooling and exact storage-origin correction were
  protected-delivered through PRs #655 and #656. The subsequent native suite
  rejected its v1 reservation contract: publishing the deleted D version again with
  identical bytes succeeded, creating a new version object while the original
  remained deleted.
- The active-lifecycle design was protected-delivered through PR #658:
  active versions remain non-overwritable, but administrator deletion ends
  that lifetime and retained deleted records do not reserve coordinates.
  The active-only v2 tooling was protected-delivered through PR #659.
- One explicitly authorized fresh v2 generation completed all five native
  probes and six active captures and passed independent audit. PR #660
  protected-delivered its exact admitted contract and fresh `ready` Governance
  with `live_enabled: true`. Reviewed/merged trees, post-merge CI and CodeQL,
  and authenticated platform readback were verified.
- PR #661 protected-delivered the supported package-level npm metadata reader.
  Its reviewed/merged tree, post-merge CI, CodeQL, and fresh platform readback
  passed. The publication profile and admitted native generation are unchanged.
- PR #662 protected-delivered the transport-to-logical-basename correction.
  Its exact reviewed/merged tree, post-merge CI and CodeQL, and fresh platform
  and neutral NBGV preflight passed.
- The separately authorized second run, `34332094944`, rebuilt and qualified
  its own artifact, received Approval after its new Snapshot and archive were
  reviewed, and emitted an authoritative `published` Outcome with
  `possibly-mutated: false`. Its Publication Result records a successful
  invocation and exact destination bytes/witness. All 23 current-run artifact
  bodies are retained. Independent lineage and fresh destination-byte audit
  passed: native version `1227103825` is byte-identical to the approved archive,
  and only the intended version and target tag were added. The auditable
  normal-Live proving objective is complete.
- Both authorized dispatches are spent. No third dispatch, GitHub rerun,
  prior-run artifact or Approval adoption, automatic retry, additional native
  probe, administrative operation, or grant change is authorized.
- Preserve the first run, `34320726590`, as independently audited
  `failed-before-publication`, `possibly-mutated: false`, with no invocation.
  Both incomplete v1 generations and separately unresolved original-D recovery
  remain unchanged. Exact evidence is preserved in the
  [retained source extracts](./validation/native-and-normal-live-evidence.md)
  with their pinned source provenance.

## NuGet Second-Slice Implementation

The NuGet research delivery is complete. The user has selected
`Hcoona.ReleaseSmoke.GithubPackages` and confirmed its operator-controlled,
smoke-only use without production dependencies or existing compatibility
obligations. Scope, trust, and acceptance are now confirmed as `WD-NUGET-*`.
The HLD, five MLD extensions, and brief NuGet LLD define the design. The
[NuGet handoff](./nuget-smoke-research-handoff.md) routes its admission gates.
Design delivery is complete through PR #669. The user subsequently delegated
end-to-end implementation and proving of this C# smoke project without
intermediate confirmation. Continue within the confirmed scope; close each
concrete native request and its bounded budget before execution. This grant
does not waive native guarantees, protected delivery, current-run Approval,
independent audit, or ambiguity stop conditions, and does not reopen npm.

The selected C# smoke project's verified go-live is complete. The
[NuGet normal-Live evidence](./validation/nuget-normal-live-evidence.md) binds
its own Windows publication, current-run authority, fresh destination bytes and
clean consumer. Its consumed operation allowances grant no further execution.

The accepted [Delivery Wave](../../../../../docs/delivery-wave.md)
supplies repository work authorization. [Issue #676](https://github.com/hcoona/three/issues/676)
coordinates the concrete NuGet advancement and retains delivery evidence;
neither this handoff nor the Issue enlarges the Wave or domain effect bounds.

The native Provider and frozen Build/Quality foundation, independent
authoring/compiler contracts, one-shot HTTP adapter, and blocked NuGet
Governance are protected-delivered through PR #678. PR #679 protected-delivered
explicit NuGet Release records, planning from matched admitted Model/Provider
inputs, and original-archive content/consumer Qualification.

Disabled destination integration adds a separate native Eligibility Decision,
no-tag active observation and action records, and original `.nupkg` publication.
It reuses Publication Snapshot, current-Attempt Approval/Authorization, fresh
exact-satisfied proof, marker/Result transport, and the shared Outcome Finalizer.
Imported HTTP profiles retain their original runtime/source/TLS identity;
publication compares the actual profile before its one-shot invocation.
Initial protected-main control evidence and later protected-path freshness
remain distinct. The native Eligibility Decision carries no npm static-reference
placeholder. Modeled service/admission scenarios establish local contracts only.
PR #680 completed protected destination-runtime delivery.

The native control entry adds `repository provide-dotnet` and the separate
`release nuget` commands for Intent normalization, immutable Provider import,
Model compilation, Eligibility, and current-Attempt binding/admission. Control
consumers do not reevaluate the target. Minimal Live platform collection reuses
actual protected-main/run/merged-tree checks and retains raw review carriers;
the comprehensive operator collector remains separate. Disabled Governance
performs no native profile collection. Attempt admission selects current
freshness or authorization replay explicitly. Inspect current Git/PR state
before relying on remote availability; PR #684 completed protected
control-entry delivery.

The native Build/Qualification entry adds `plan-qualification`, `run-build`,
`form-artifact`, `artifact-contents`, `restore-build-invoke`, and
`finalize-qualification` under `release nuget`. It imports matched native facts,
freezes the Snapshot and witness, retains the original `.nupkg` through upload
binding, and produces independent contents/consumer Evidence for the shared
Finalizer. Mechanical metadata travels separately from the archive; its
representation does not add native authority. Missing Evidence remains
incomplete, malformed partial transport is rejected, and GitHub reruns are
rejected before effects. PR #685 completed protected Build/Qualification entry
delivery. Local command tests do not prove Windows Actions execution or
destination behavior.

The native observation and approval entry adds `observe-github-packages`,
`materialize-publication`, `form-approval-bundle`, and
`form-publication-authorization` under `release nuget`. It admits native current
authority before supported reads, retains blocking observations, and materializes
zero or one action. Only an action produces `reviewer-summary.md` and may form
an Approval Bundle. Authorization replays the exact publication basis and
Bundle closure before requiring fresh matching Governance and the existing
Approval sentinel. All entries reject reruns. PR #687 completed protected
observation/approval entry delivery.

The native publication entry adds `prove-exact-satisfied`, `prepare-publication`,
`execute-publication`, and `finalize-live` under `release nuget`. Fresh zero-action
proof requires a skipped publisher and current Governance, package-control, and
actual bytes. Preparation preserves the original archive without target
execution. Execution requires complete current Approval/Authorization and an
admitted durable marker, discovers the supported service resources, and consumes
the marker once. A conflict or lost response remains failed despite exact
diagnostic readback. The native Finalizer admits all three Qualification Evidence
records and the current terminal chain; it performs no destination reads.
Existing shared `release admit-publication-terminal` and
`release resolve-publication-terminal` handle native marker/Result transport.
PR #688 completed protected publication-entry delivery. Local scenarios use
modeled destination and native-helper
seams and establish no Windows Actions or real publication evidence.

The separate `workflow-delivery-v3-nuget-buddy-smoke.yml` caller and
`workflow-delivery-v3-nuget-live-attempt.yml` reusable workflow connect the
native commands on Windows. Their Intent binds the native caller path. An
unprivileged helper producer uploads its full same-revision runtime; readers
and the publisher consume that immutable archive without building it. The
Attempt retains native Build/content/consumer Evidence, current-run Approval,
uploaded-marker admission, zero-action proof, and failed terminal payloads.
The workflow pair is present on protected `main`.
Local shell/transport contracts establish no actual native Windows workflow execution.
The [NuGet fixture and operator contract](./hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
governs acceptance tooling. The independently audited native generation and
current Governance collection now supply the exact admission and ready
Governance described below. Separate [actual normal-Live evidence](./validation/nuget-normal-live-evidence.md)
establishes Windows publication and fresh destination consumption.
The offline `acceptance.nuget_fixture` component prepares two original pack
outputs from one frozen compilation. It requires explicit local dependency
archives, a new dependency cache, official identity/content inspection and A's
clean local consumer before returning a complete pair. Only fixture preparation
disables remote vulnerability auditing; ordinary Release keeps its configuration.
Available diagnostics and already retained outputs survive failure without a
success manifest. Local SDK scenarios establish this mechanism,
not actual Windows preparation, destination behavior or service-owned atomic
guarantees. The separate `workflow-delivery-v3-nuget-fixtures.yml` entry connects
the protected current request, immutable helper/native setup inputs and offline
pair transport on Windows. It rejects reruns and closes native Provider/Model
admission under `destination-acceptance`. Setup acquires original dependencies
through native locked restore before the offline pair boundary. Failed preparation
retains diagnostics without completed output. Inspect Git and protected delivery
before relying on remote availability. A bounded Windows preparation run has
passed independent artifact-body and lineage audit: the original A/B packages
have native-equivalent identities and different bytes, and A's clean local
consumer passed. The [preparation evidence](https://github.com/hcoona/three/issues/676)
retains the exact request, protected source/run identities, artifact digests
and audit limits. Its single dispatch allowance is spent; the evidence grants
no rerun or replacement. The [NuGet capture component](../README.md) retains
bounded complete observations or partial evidence through the existing reader.
The local read operator adds a separate executable preflight, exact source and
immutable-helper comparisons, POSIX process supervision and fixed cumulative
capture allowances. It preserves the helper's original producer/run lineage;
independent helper admission and actual local runtime compatibility remain
required. A failed collector cannot resume or replenish its budget. Its local
runtime is not the Windows publication profile. Close the exact reviewed native
request and required access facts before collection; the entry's presence does
not supply them. The separate one-probe Windows entry binds a prospective spec
to its actual protected current run, inspects the original immutable pair with
the admitted prebuilt helper in an unprivileged job, and transports the selected
unchanged package to the publisher. The publisher checks the current profile,
persists its invocation marker and calls the one-shot adapter once without
target or helper execution. Retained HTTP/partial-failure evidence is not a
native verdict; the spec's evidence hashes do not authenticate their sources
or authorize dispatch. Local tests establish source and shell contracts only.
The [destination consumer component](../README.md) adds a fresh SDK graph,
bounded native NuGet restore, exact installed-byte checks, and credential-free
build/marker invocation under POSIX process supervision. Its prebuilt restore
host is separate from the original fixture/reader helper and receives the read
credential only for restore. Callers still own original/tooling provenance,
current read authority and a concrete finite generation. Controlled local
evidence is not actual destination consumption or Windows acceptance.
The [fixed-suite operator](../README.md) connects the three probes, all six
captures and creation's clean consumer. It requires current protected tooling,
exact dispatch run details, bounded original-byte HTTP collection and unchanged
state through both duplicates. Failed positions cannot resume or trigger a
replacement dispatch. Original uploads, prior admission evidence, partial
responses and consumer files remain available to the independent auditor.
Controlled integration tests establish application behavior only. The accepted
Native R4 audit below and separate [normal-Live audit](./validation/nuget-normal-live-evidence.md)
establish their respective actual collection and publication outcomes. Adapter
changes require admission of the current Windows profile. The
[credential-free profile observer](../README.md) and dedicated Windows entry
collect that existing profile from separately audited complete preflight
resources, without helper, SDK or package access. Shared discovery validation
accepts the advertised exact owner-root publish address while retaining origin,
owner, query and traversal checks. The observer requires its own bounded
reviewed dispatch; neither a partial preflight nor a self-consistent digest
establishes resource provenance or permission to run it.
Close the concrete native request and actual access/profile prerequisites
before dispatch; source availability supplies no missing gate.
The [audited first NuGet create-response finding](./research/nuget-smoke-evidence.md#2026-09-14-query--correct-the-nuget-push-response-contract)
identified HTTP 200 outside the original 201-only profile. The corrected closed
profile selects 200/201/202 consistently across adapter, probe, original-evidence
reader and suite, while preserving the raw status, exact readback and duplicate
409 failures. Original run `34907316467` and its suite remain failed and spent;
they are not native qualification for the revised profile. A new independently
audited Windows profile and a separately reviewed native generation remain
required. The original artifact collector's later 302 failure is distinct;
its exact predicate and storage-hop initiation are not established by this
status correction. Earlier storage-origin observations do not guarantee the
origin of future artifacts.
The independently disposed
[Actions artifact-origin and package-download findings](./research/nuget-smoke-evidence.md#2026-09-15-query--bounded-response-selected-downloads)
use explicit successor contracts. Actions requests bind
`github-api-location-v1` instead of a preobserved storage host. Package reads
bind `nuget-package-location-v1` and the versioned capture transcript; only the
selected package-content GET may follow its original 301/302 once. Both paths
validate the current response-selected HTTPS target, send no credentials to
storage, retain no signed capabilities in evidence, charge every
hop and preserve exact successful bytes. Metadata GET and publication PUT
redirects remain forbidden. Those original failed lifetimes stay spent. The
subsequent original-coordinate read independently established A's exact bytes
and witness; it did not backfill the original generation's missing captures.
The [successor R3 audit](https://github.com/hcoona/three/issues/676#issuecomment-5675393479)
also established its fresh create/after-create bytes, then stopped when the
native consumer rejected selected package 302. Three GETs and 2,197 body bytes
were below its 12-GET/16-MiB allowance; this was not proved budget exhaustion.
The [consumer correction](./research/nuget-smoke-evidence.md#2026-09-15-query--native-consumer-package-redirect)
extends the existing package-only policy to the native restore host and
versioned consumer evidence. Its local controlled tests do not qualify a
replacement native generation. R3 remains failed/spent; duplicate probes,
complete consumer evidence and native admission were not obtained. Protected
source delivery, separately admitted rebuilt consumer provenance, independent
review and a renewed exact request precede further operations.
The [complete Native R4 and Governance audits](./research/nuget-smoke-evidence.md#2026-09-15-query--native-acceptance-and-governance-activation)
qualify the exact six-field NuGet generation and ready Governance with Live enabled.
The create, both duplicate rejections, all six captures and clean destination
consumer passed; original failed generations and their spent allowances remain
unchanged. The completed [normal-Live publication](./validation/nuget-normal-live-evidence.md)
retains its own qualification, Snapshot, Approval, Authorization and independently
audited terminal, destination and clean-consumer evidence. `WD-NUGET-006` relies explicitly
on GitHub version uniqueness and duplicate non-replacement, with no chosen
concurrency winner, global read-consistency claim, or serialized-only scope.
The independent native suite still supplies bounded observations, not proof
of arbitrary concurrent behavior. The control uses the exact six-field native
generation, including the integration-contract revision and evidence digest,
without a separate atomic-assurance registry. Its production set binds the
accepted generation. Future publication still requires fresh current-run
authority/profile checks and its own concrete authorization. No support inquiry
is a prerequisite.

## Git Inspection and Implementation Scope

Do not trust a recorded branch name, SHA, dirty-state claim, or PR status.
Inspect current branch, `HEAD`, local and remote `main`, merge base, status,
untracked paths, the complete diff, and the implementation PR state before
acting. Skip delivery steps already completed; a recorded checkpoint does not
prove that a branch is unpushed, a PR is unopened, or a change is unmerged.

For the completed npm objective, the implementation surface was the release record and transport
model, strict Governance eligibility, GitHub Packages Adapter, Live
materialization/finalization and CLI wiring, the normal-Live workflows,
protected Governance v2 document, affected scenario/contract tests,
and synchronized current-state documentation. Treat that as a bounded purpose,
not an exhaustive path allowlist; inspect every changed path and reject
unrelated scope.

That inventory is not authorization to reopen npm implementation. NuGet
implementation follows the protected design and the subsequent end-to-end
delegation described above. Changes to shared code preserve npm contracts;
NuGet receives its own Provider facts, policy, Governance, and native evidence.

When these paths are under review, preserve unrelated worktree changes and do
not reset or overwrite them.

## External State

### Approval Environment

`workflow-delivery-v3-buddy-approval` is the retained authority-bearing Environment:

- ID `20895030723`; reviewer rule `64124473`; sole reviewer `hcoona` / `712433`;
- `prevent_self_review: false`; zero wait; no secrets; no branch/tag restriction; `can_admins_bypass: false`;
- sentinel `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`; and
- recorded deployments `6344271579` and `6346279889` from the two real runs.
  This is an observed checkpoint, not a zero-deployment Governance requirement.

### Removed Legacy Environment

`workflow-delivery-v3-buddy-github-packages`, ID `20895037877`, was deleted
after exact retained-ref and no-authority-reference proof. Its pre-deletion
deployment inventory was empty. Complete Environment readback confirmed only
that resource was removed and the retained Approval Environment was unchanged.
Do not recreate it or repeat the deletion.

### Packages and Live State

- The credential principal is repository `hcoona/three`.
- Known reach includes production package `hexo-renderer-asciidoc` and disposable smoke packages.
- This accepted repository-principal blast radius is not package isolation and is not an exhaustive grant inventory.
- Package access remains unchanged. Protected Governance is ready with
  `live_enabled: true`; the corrected publisher has emitted a real `published`
  Outcome for `1.0.0-beta.253.g6325e93`.
- Authorized native probes created disposable scenario versions and tags.
  Original-D recovery remains unresolved; the active replacement is retained.
  The first normal-Live run failed before publication; the second published
  after its own Approval. No explicit restoration occurred.

### Native Acceptance Evidence

The profile requires the LLD section 18.6 suite: exact creation, both active
duplicates, and the bounded distinct-version tag race. The implemented v2
tooling uses five probes, three versions, two scenario tags, and six complete
active captures, with no deleted state, deletion, or restoration. The retired
v1 suite and legacy fixed-coordinate/retry-5 helpers are not alternatives.

The fresh v2 suite completed and passed independent native audit. Its exact
generation, run identities, byte and state digests, and provenance limits are
recorded in the [activation evidence](./validation/native-and-normal-live-evidence.md#2026-09-09-query--prepare-normal-live-activation-from-audited-native-evidence).
The admitted identity in Governance binds that canonical evidence, not the
failed v1 interpretation.

Current acceptance-only components live in
`three_workflow_delivery_v3.acceptance`. The distinct
`workflow-delivery-v3-native-npm-acceptance.yml` entry invokes one probe with
the actual Actions-issued repository `GITHUB_TOKEN`. It accepts only an
explicitly confirmed manual request on protected `main` by the accepted actor
at attempt one. Its job compares the GitHub-resolved SHA with the requested
tooling SHA before any steps, rejecting intervening `main` movement.
It does not revive any retired Buddy acceptance identity or
enter the normal-Live workflow graph. Inspect Git and protected delivery
before treating the entry as available remotely.

The probe retains immutable request, actual fixture, matched profile, process
facts, and platform context. Those facts are not a native acceptance verdict:
the operator must collect complete active destination state and apply every
scenario gate in sequence. Missing evidence, ambiguity, or an
unexpected delta stops mutation; no probe or workflow failure permits a blind
retry. The collector retains actual complete inventories, scenario bytes, and
raw native responses; the audit reader binds downloaded probe evidence to its
exact run, request, and tooling revision. The fixed-suite local operator
connects those components without a retry or recovery protocol. The
[LLD tooling boundary](./hcoona-release-smoke-npm-lld.md#1861-native-suite-tooling-boundary)
separates these responsibilities.

The operator confirmed the pre-existing public
`@hcoona/hcoona-release-smoke-npm-dual` container, ID `12047077`, associated
with `hcoona/three`, is operator-controlled and has no production dependency.
The separately approved five-probe v2 execution is complete. No further native
generation is authorized. The lifecycle revision and passing suite authorize
no additional administrative operation; unrelated versions, tags, and access
changes remain outside their scope.

The first generation stopped on the now-corrected storage-origin policy.
Its created version remains untouched. The second v1 generation reached the
deleted/restorable scenario and stopped when the identical duplicate publish
succeeded instead of failing. Read-only inventory distinguishes the deleted
original from the new active object; this was not restoration.

This remains a valid counterexample to the superseded reservation requirement,
not another reader defect or a passing revised generation. The different-byte
deleted probe and explicit restoration were not attempted. Do not delete the
replacement, retry restoration, or install either incomplete generation.
Recovery still needs a separate explicit decision. The approved correction
changes the lifecycle requirement rather than adding runtime history or
administrative compensation. See the [native rejection record](./validation/native-and-normal-live-evidence.md#2026-09-08-query--reject-the-native-deleted-version-primitive)
for exact run and object identities.

The standard-publish probes must use the real Actions-issued repository token;
do not relabel a local PAT as `GITHUB_TOKEN`, add secrets or access grants
implicitly, or send administrative credentials into normal runtime. No
acceptance generation may be installed before a real passing suite.

### Local Native Operator

The following runbook is for a future separately authorized v2 generation,
not a request to repeat the completed acceptance. Its exact tooling revision
must be protected-delivered and its bounded five-probe execution confirmed.
Retired v1 tooling is not an alternative path.

Use a clean POSIX checkout of the exact protected tooling revision, with the
repository's locked pnpm dependencies and mise-selected Python uv environment
prepared with `uv sync --frozen --package three-workflow-delivery-v3`.
Windows operators need a configured POSIX environment such as WSL. Existing
classic gh authentication must support package reads, dispatch, and
`gh run watch`; the revised suite needs no delete/restore capability.
No command installs credentials or expands grants.
If the isolated process cannot access the desktop keyring, provide the same
credential through the supported process-local `GH_TOKEN` environment
variable. Never print it, persist it in audit files, or pass it to Actions.

The example below is **not executable authorization**. Replace every
placeholder only after confirming the exact pre-existing, operator-controlled
disposable package has no production dependency and obtaining bounded
five-probe approval. The flag acknowledges prior approval; it does not
grant it. Use a fresh lowercase hexadecimal generation and two distinct
target SHAs whose scenario tags are absent in that package.

```bash
uv run --no-sync --package three-workflow-delivery-v3 \
  python -m three_workflow_delivery_v3.acceptance suite \
  --package '@hcoona/<approved-disposable-name>' \
  --generation '<fresh-generation>' \
  --tooling-sha '<verified-protected-main-sha>' \
  --creation-target '<creation-target-sha>' \
  --race-target '<race-target-sha>' \
  --repository-root '<absolute-clean-checkout>' \
  --audit-directory '<new-absolute-directory-outside-checkout>' \
  --authorized-disposable
```

Use `suite --help` for the input contract. Configure any machine-specific
trusted CA location in the operator environment; never disable TLS
verification or add application certificate fallbacks.

The fixed sequence dispatches five acceptance probes: create A, identical
active A duplicate, differing active A duplicate, create W, and candidate V.
It performs no administrative mutation or deleted-state read. These are not
normal-Live dispatches.
Any failure stops mutation and preserves partial audit data. Inspect exact
recorded runs and destination state read-only before deciding an explicitly
authorized recovery; do not rerun the command against the same audit, dispatch
again blindly, or use the suite as an administrative recovery command.

Successful completion writes `suite-evidence.json` and prints its path and
digest. Its `scenario_verdict: "passed"` records completed supplied-fact gates;
it is not proof of native provenance or installed admission. Independently
audit the actual run/artifact references, raw observations, empty deltas, and
creation/tag-race results before preparing the Activation PR. Retain the local evidence
beyond the Actions artifact lifetime when necessary. Never put detailed
tombstone facts or administrative credentials into Governance.

## Accepted Risk and Threat Model

- `hcoona` is the sole accepted repository writer, reviewer, and publisher TCB member.
- A selected same-repository ref may supply branch-controlled workflow, Planner, Finalizer, Providers, Adapters,
  compiler, clients, catalogs, capability declarations, and publisher code.
- After Approval, the selected-revision publisher may use short-lived repository `GITHUB_TOKEN` with effective
  `packages: write`, no PAT, and no `id-token: write`.
- The publisher must not run target-defined product or build code.
- Protected Governance, Approval, exact bindings, permissions, static-reference checks, immutable artifacts, and
  concurrency protect against outsiders, mistakes, and accidental operators.
- They do not constrain a malicious accepted writer to the smoke package or intended workflow.
- Every package granting Actions access to `hcoona/three` is in effective reach. Coordinate and action checks govern
  intended behavior and reconciliation only.
- A clean static-reference result proves only absence of prohibited direct references in its bounded supported catalog.
- Self-approval is not independent security review. Official npmjs authority remains separate.
- Any writer, reviewer, role, team, or relevant access change requires `live_enabled: false` and a new Governance
  decision.
- Flag-off blocks fresh admission and a publisher before its final check; it is not rollback or instantaneous
  revocation after that check.

Never claim package isolation, reviewer independence, exhaustive grant discovery, universal consumer proof, or
instantaneous revocation.

Normal Live requires the exact admitted native contract, fresh ready
Governance, and protected Activation delivery. The completed native evidence
does not waive post-merge readback or prove a normal-Live outcome. The design deliberately
treats a post-Observation tag race as bounded routing damage: supported
consumers resolve exact `name@version`, and exact version bytes, digests, and
witness remain authoritative.

Active-version non-overwrite is not lifetime-global coordinate immutability.
Administrator deletion may permit a new object and different content at the
same coordinate; caches may retain an earlier lifetime's content. This risk
is accepted only within the smoke-only, sole-writer TCB boundary. Normal
publication neither performs nor compensates for administrative operations.

## Explicit Authorization Boundary

The end-to-end authorization covered:
design delivery, disabled implementation and protected merge, bounded native
acceptance, obsolete-Environment cleanup after proof, ready Governance v2
activation, and auditable normal-Live proving. The failed first run consumed
the initial dispatch authorization. The separately authorized second new run
has also completed. Both dispatch authorizations are spent; this checkpoint
grants no standing permission for another run.

The authorization is contract-bounded. Do not change package or repository
access, touch unrelated packages or tags, use package-admin authority in
runtime, weaken review or readback gates, perform a GitHub rerun, reuse the
first run's artifacts, Snapshot, or Approval, or issue a third proving dispatch.
The separately approved native v2 generation is complete; another generation
requires new authorization.
Revised native acceptance performs no deletion or
restoration. Recovery of the original disposable D and its replacement remains
a separate explicit operator decision, not implied permission from the
lifecycle correction or a passing revised suite. Any ambiguous external response stops
mutation and triggers read-only investigation rather than retry.

## Required Reading Order

After Git inspection, read this handoff, the [project entry](./README.md),
[requirements](./requirements.md), replacement [HLD](./high-level-design.md) and
[middle-level design](./middle-level-design.md), then the
[implementation plan](./migration-strategy.md). Use the MLD's
[transfer contracts](./middle-level-design.md#data-crossing-boundaries) and the HLD's
[implementation order](./high-level-design.md#implementation-and-retirement-order)
for new work. Load the relevant existing glossary, pinned former MLD/migration section, LLD
and source only to understand an actual implementation or evidence consumer.
The [document portal](./README.md#normative-hierarchy) routes those baseline
records. Do not preload all historical designs or treat npm contracts as a
specification for another ecosystem.

Do not infer policy from stale runtime behavior or archived designs.

## Next Executable Workflow

The implementation, activation, real publication, and independent proving audit
are complete. No further runtime or proving action is needed for this objective.

Perform the required Git inspection before using this documentary checkpoint.
Preserve both actual runs and the retained native evidence; do not repeat
provisioning, acceptance, either dispatch, or either Approval. Original-D
recovery and any future mutation require separate explicit authorization.
This completed task authorizes no third proving dispatch.

The NuGet slice's [verified normal-Live objective](./validation/nuget-normal-live-evidence.md)
is also complete under its separate end-to-end delegation. Preserve its accepted
Native R4, ready Governance, actual publication and joined postpublication audit.
Do not repeat completed implementation, native acceptance, dispatch, Approval,
publication or postpublication verification. All consumed and failed lifetimes
remain spent. Future operations require their own concrete bounded requests,
fresh current-run authority/profile checks and satisfied technical gates.

## Validation and Review Protocol

- Validate the complete affected implementation and design set, not only
  README, handoff, and evidence records.
- Check links, headings, requirement references, terminology, precedence, authority ordering, and failure behavior.
- Run repository-existing Prettier and Markdownlint, applicable documentation/HK gates, hooks when commits are
  prepared, and `git diff --check`.
- Search contextually for superseded target claims: Capability Environment/Profile authority, `approval-finalizer`,
  capability groups/bundles, history-derived authority, approval on zero actions, normal-Live run-attempt record
  fields, Preparation PR or `main` freeze, activation tag, rerun recovery, universal consumer proof, and package token
  isolation.
- Legacy terms are allowed only to inventory an existing resource or describe a removed mechanism for safe migration.
- Review the complete zero-action and one-action paths, cancellation, missing Result, fresh observation, and ambiguous
  activation response.
- An unresolved contradiction blocks commits; do not hide it or choose policy from implementation.
- Review follows green local validation. Each finding is atomic, independently adjudicated, fixed if true, and returned
  to the original reviewer until zero findings.
- Use the `open-code-review-delegate` skill for multi-agent review, including
  the complete Git path manifest when OCR filtering omits documentation.
- Insert contraction after every two review iterations, after all changes and
  reviews, and before creating a PR.
- Do not claim validation, review, delivery, or merge before persistent evidence exists.

For later runtime work, complete affected tests, root HK, and hooks before multi-review. Documentation work uses the
applicable documentation and repository gates but keeps the same validate-before-review order.

## Architecture, Design, Testing, and Documentation Discipline

### Architecture and Design

- Confirm requirements, affected architecture, concrete boundary contracts and
  validation basis before implementation and caller cutover. A fixed number of
  MLDs or matching CI/Release documents is not required.
- Keep design contract-bounded; do not silently infer policy or expand channels, destinations, credentials, services,
  authority, abstractions, or external resources.
- CI and Release retain distinct responsibilities in one internal application;
  share concrete native mechanisms without symmetric domain frameworks.
- Rely on documented lower-layer guarantees. If one is absent, block the capability rather than simulate a weaker one.
  The NuGet-specific dependency and evidence basis follows `WD-NUGET-006`;
  do not reintroduce its superseded service-statement prerequisite. Python
  separately follows `WD-PY-006` for its per-file platform dependency and actual
  publication evidence basis, without prerequisite duplicate/race probes; it assumes no atomic two-file release or unlimited registry retention.
- Add an abstraction only when concrete scenarios prove independent identity, behavior, lifecycle, or policy.
- Do not freeze non-authoritative topology, shell choreography, parser branches, or inventory counts as architecture.

### Testing and Review

- Use scenario-first tests for business behavior.
- Use focused unit tests for impact/outcome logic and contract tests for actual
  transfers, qualification and effect boundaries. Do not preserve tests whose
  only purpose is enforcing retired record chains or duplicating native approval.
- Use real integration tests at GitHub, Git, npm, NBGV, HK, or destination contract boundaries.
- Avoid brittle tests for ordinary implementation detail.
- Run complete affected tests, HK, and hooks before independent multi-review; adjudicate findings atomically and return
  fixes to original reviewers.

### Documentation and Git

- Follow the [repository contribution guide](../../../../../CONTRIBUTING.md)
  for generic documentation and Git procedure.
- Keep requirement IDs stable, links valid, and current-state prose concise.
- Do not preserve obsolete retry ledgers, PR narratives, test-count histories, artifact tables, or superseded
  mechanisms in current-state pages.
- Use Git and delivery work carriers for chronology. Preserve verbatim source
  evidence and provenance in the project research and validation records.
- Make commits dependency-ordered and human-reviewable.
- Update the handoff, v3 README, and affected concern records when phase or
  slice status changes; keep run chronology in the delivery work carrier.
- Keep claims truthful, relevant, clear, and no more detailed than necessary.
