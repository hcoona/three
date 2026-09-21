# Workflow Delivery v3

This project owns Three's Workflow Delivery v3 control primitives and domain
records. Start with the [document set](docs/README.md) and
[agent handoff](docs/agent-handoff.md) for the authority order, current evidence,
and operating limits.

Workflow v3 is repository-specific tooling, with no PyPI distribution or
third-party Python API support commitment. Its Python modules and same-revision
callers may evolve together. Compatibility obligations follow the CLI/workflow,
serialized and domain contracts defined in the project records.

New sessions start at the [handoff entry](docs/agent-handoff.md#starting-a-new-session).
The [NuGet delivery retrospective](docs/research/nuget-delivery-retrospective.md)
explains reusable integration lessons and the next-task guide.

The old `governance run-fixed-acceptance-probe` command and fixed-coordinate
Python producer APIs are retired. `governance admit-acceptance-evidence` and
reviewer inspection still read historical evidence; the original bytes and
provenance remain retained. Old producer replay requires its exact historical
Git revision and separate operation authorization. Current native acceptance
and Release continue to use their existing commands and contracts.

The [npm smoke package](../hcoona-release-smoke-npm/README.md) owns its small
package purpose. This project owns the
[first-slice proving protocol](docs/hcoona-release-smoke-npm-lld.md).
The [NuGet authority helper](../../../private/app/workflow-delivery-v3-nuget-authority/README.md)
is a static-reference component. The
[NuGet second-slice handoff](docs/nuget-smoke-research-handoff.md) routes the
confirmed requirements, protected design and implementation. The
[selected NuGet smoke library](../hcoona-release-smoke-github-packages/README.md)
owns its marker-product purpose; the
[native .NET helper](../../../private/app/workflow-delivery-v3-dotnet-provider/README.md)
is a v3 Provider and package-inspection component. The handoff retains the
distinct native and publication admission gates. Explicit NuGet Release
records and qualification bind admitted native inputs and the original package
to separate content and consumer Evidence. Destination integration
adds native eligibility, active observation, original-archive publication,
and shared Approval/terminal closure. The native control CLI imports Provider
facts, compiles the Model, and binds Eligibility and the current Attempt.
Native Build/Qualification commands connect matched planning, frozen Build,
original-package upload binding, separate quality Evidence, and finalization.
Native observation and approval commands connect supported active readback,
zero-or-one action materialization, and current-Attempt Approval/Authorization.
Native publication commands connect fresh exact-state proof, build-free
preparation, one-shot execution, and current-DAG finalization. The separate
[NuGet manual caller](../../../../.github/workflows/workflow-delivery-v3-nuget-buddy-smoke.yml)
and [reusable Attempt](../../../../.github/workflows/workflow-delivery-v3-nuget-live-attempt.yml)
connect these commands on Windows. The [audited native generation and Governance evidence](docs/research/nuget-smoke-evidence.md#2026-09-15-query--native-acceptance-and-governance-activation)
are bound by the exact production admission and ready Governance with Live enabled.
Windows normal-Live publication and fresh exact-version destination consumption
are [independently verified](docs/validation/nuget-normal-live-evidence.md).
The [native fixture and operator contract](docs/hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
defines acceptance-tooling preparation and its evidence boundary.

The offline [NuGet fixture component](src/three_workflow_delivery_v3/acceptance/nuget_fixture.py)
prepares original A/B archives from one frozen compilation, inspects their
native identity and payload relationship, and requires A's clean local
consumer. It retains inputs, original bytes and diagnostics. Its local SDK
checks do not establish Windows or destination evidence. The separate
[preparation entry](../../../../.github/workflows/workflow-delivery-v3-nuget-fixtures.yml)
connects protected request, trusted helper and native setup transport to the
offline pair on Windows. Its acceptance-only command module is
`three_workflow_delivery_v3.acceptance.nuget_preparation`; `request`, `setup`
and `prepare` retain exact current inputs and completed or partial evidence.
A bounded Windows preparation run has passed independent artifact-body and
lineage audit, including original A/B packages and A's clean local consumer.
The [preparation evidence](https://github.com/hcoona/three/issues/676) retains
the exact request, source/run identities, artifact digests and audit limits.
The [NuGet capture component](src/three_workflow_delivery_v3/acceptance/nuget_capture.py)
connects the existing active-state reader to a closed request, finite read/page/body
allowances and retained response evidence. It records complete sequential state
or partial failure evidence; neither result is a native acceptance verdict.
Retained headers are an explicit non-secret projection. Known request
credentials in evidence cause failure before that evidence is written; safe
response bodies retain their original bytes.
Its completion deadline rejects late results but does not terminate a blocked
process. The caller must admit exact tooling and the prebuilt helper, supervise
the process and enforce the complete generation budget. The local reader runtime
record does not establish the Windows publication profile.

The [local NuGet read operator](src/three_workflow_delivery_v3/acceptance/nuget_operator.py)
adds POSIX process supervision and a fixed, nonresumable generation budget.
Windows operators need a configured WSL environment for this local entry;
Windows Build and publication retain their separate platform requirements.
`python -m three_workflow_delivery_v3.acceptance.nuget_operator --help` describes
the single-capture preflight entry. Its canonical `NuGetReadRequest` binds the
exact clean reader checkout and local Python/TLS identity, the original complete
helper artifact and producer/run, independently admitted audit bytes, unchanged
helper source inputs, and the actual local .NET host and runtime information.
These comparisons preserve admitted provenance; supplied hashes or an audit file
do not independently establish producer authenticity, input completeness or
local helper compatibility. Close those facts in the reviewed native request.

The operator retains bounded helper command/output records, excludes the read
credential from helper environments, and terminates the capture process group
and its descendants at the deadline. Process reaping has a separate five-second
bound. Cumulative allowances reserve each declared capture once; helper output
allowances include one overflow-detection byte per call. A failed capture spends
the collector lifetime, retaining safe partial evidence without a retry. The
library supports all six ordered capture positions; the CLI runs only the
separate preflight and rejects a present scenario coordinate. It supplies no
publication authority. A complete preflight capture is not
the required access audit, native acceptance, Windows profile or publication
authority. The accepted generation's complete evidence is linked above;
future operations still require their own concrete requests.

The [one-probe component](src/three_workflow_delivery_v3/acceptance/nuget_probe.py)
and separate [Windows acceptance entry](../../../../.github/workflows/workflow-delivery-v3-native-nuget-acceptance.yml)
consume an independently admitted original fixture pair and complete helper.
`python -m three_workflow_delivery_v3.acceptance.nuget_probe --help` exposes
prospective-spec binding, unprivileged inspection and one publication stage.
The canonical spec uses `workflow-delivery/v3/nuget-probe-spec` and the request
fields defined by `NuGetProbeRequest`, omitting the not-yet-created run ID.
The entry binds the actual current run, preserves original producer identities,
checks actual source bytes and the complete Windows operation profile, and
selects original A or equivalent B without rebuilding either package. Its
process-local Git configuration preserves LF checkout bytes; it changes no
machine Git settings. The unprivileged stage retains helper command diagnostics.

The publisher job holds package-write authority, including its pinned checkout
and setup actions. The invocation step receives the token explicitly through
its environment. The publisher consumes the current-run immutable prepared
archive, evaluates no target or helper, and persists an exclusive, flushed
invocation marker before one HTTP call. Safe response bytes, selected headers and partial failures are
retained for 45 days. An expected HTTP status completes only that observation;
it establishes neither destination readback nor native acceptance. Lost output
or an ambiguous response leaves the request spent and cannot justify a rerun.
The spec's audit/preflight/capture hashes identify separately admitted evidence;
they do not verify its provenance or supply authorization. Close the concrete
native request and actual access/profile prerequisites before dispatch.
The admitted generation and ready Governance do not authorize another probe.

The [profile observer](src/three_workflow_delivery_v3/acceptance/nuget_profile.py)
and separate [Windows entry](../../../../.github/workflows/workflow-delivery-v3-nuget-profile.yml)
measure the existing publication profile without a package credential, helper
or SDK invocation. `python -m three_workflow_delivery_v3.acceptance.nuget_profile --help`
describes its canonical input and retained output. Its small inline request
binds protected tooling and the exact resource projection from a complete,
independently audited preflight. Resource hashes alone do not establish that
audit or authorize dispatch. The observer shares discovery's URL validation,
including the exact owner-root publish address, and retains the complete
actual Windows profile for independent review. It neither admits a native
generation nor enables Live; source availability grants no observation run.

The [destination consumer component](src/three_workflow_delivery_v3/acceptance/nuget_consumer.py)
creates a fresh, fixed consumer and SDK graph, then calls the prebuilt
[native restore host](../../../private/app/workflow-delivery-v3-nuget-consumer/README.md).
NuGet restores the exact version directly from the selected HTTP feed through
bounded native transport. Only that restore child receives the supplied read
credential. Its v2 request binds `nuget-package-location-v1`: the selected
package's 301/302 may supply one validated storage GET, with no forwarded
credentials and no automatic redirects. Both sends share the original bounds;
redirect/error bodies are counted and omitted. The coordinator checks the v2
HTTP transcript, terminal package response, actual installed archive, witness
and assets before credential-free `--no-restore` build and marker invocation. It
supervises five bounded commands in one POSIX process group and retains safe
partial failure without retry. It is a library component, without a new CLI.
Callers must independently admit original inputs, complete prebuilt tooling,
current read authority and the finite native generation; supplied hashes do not
establish provenance. Controlled local tests establish native SDK and component
behavior, not actual destination consumption, Windows profile or acceptance.

The [fixed-suite operator](src/three_workflow_delivery_v3/acceptance/nuget_suite_operator.py)
connects those components in the accepted order: create A, verify the creation
delta, consume A, reject identical A, then reject equivalent different-byte B.
It retains all six captures and compares official native coordinates and GitHub
object identities, including unchanged neighboring versions. Original probe
uploads bind the exact current run, prepared input, invocation marker, response
and result. A duplicate rejection preserves its conservative mutation flag;
it is not a successful publication.

`python -m three_workflow_delivery_v3.acceptance.nuget_suite_operator --help`
describes the operator-local entry. Its canonical `NuGetSuiteRequest` binds the
static plan, separate preflight request, finite GitHub limits and the digest of
the independently reviewed admission carrier. That carrier must close the
concrete execution request, current protection/access facts, original fixture
and helper provenance, current Windows profile, and prebuilt consumer runtime.
Neither a request file nor its audit hash supplies missing authorization.
The entry reads only the selected existing token environment variable. It
does not look up credentials, build a runtime or modify access.

Dispatch requests return the exact run ID. The collector rechecks current main,
workflow and actor, polls only that attempt-one run, and requires a complete
single artifact page. Direct HTTPS calls preserve original operation and artifact
response bodies. The authenticated actor response retains only a typed, explicitly
derived `id`/`login` projection; bodies carrying `Location` are omitted. Both
exceptions retain original byte counts and hashes. Dispatch permits no redirects
or retries. Each original artifact download may
follow one redirect to the exact independently admitted storage origin without
forwarding the GitHub credential. The temporary signed URL is not retained.
Request and response-body budgets include redirects and overflow detection;
each call has a supervised deadline. Native captures and the consumer retain
their separate bounds inside the fixed generation deadline.

The private audit retains original uploads, prior evidence, safe partial
responses and the actual consumer tree. Any failure spends the lifetime and
stops subsequent mutation. An observation timeout does not establish that a
queued remote run cannot still publish. A completed suite observation is only
candidate sequential evidence: independent native audit, admission of the
accepted NuGet integration contract, protected activation and a separate
normal-Live publication remain required. Adapter changes invalidate the prior
source identity, so the current Windows profile must be admitted before native
execution. NuGet admission binds the exact six-field native generation,
including its integration-contract revision and evidence digest. Its production
registry contains the independently audited generation linked above; no
separate atomic-assurance registry is used.

`repository provide-dotnet --help` describes the unprivileged native Provider
entry. `release nuget --help` exposes the separate control commands for Intent,
Model, Eligibility, Attempt admission, Build/Qualification, approval, publication,
and finalization.
Model compilation consumes immutable
uploaded Provider bytes without reevaluating the target. Live control reads
exclude operator administration endpoints; disabled Governance performs no
native profile collection. Attempt admission requires an explicit freshness or
authorization-replay phase. Command availability grants no native dispatch or
real publication. Ready Governance remains subject to current-run authority,
freshness and Approval requirements.

The qualification sequence is `plan-qualification`, `run-build`,
`form-artifact`, `artifact-contents` and `restore-build-invoke`, then
`finalize-qualification`. Planning imports matched native facts without target
reevaluation. Build retains the original `.nupkg`; upload binding keeps its
content identity distinct from the transport digest. The two quality commands
produce separate Evidence for that artifact. Missing Evidence remains
`incomplete`, while malformed or mismatched transport is rejected. Use each
command's `--help` for its required current-run inputs. These local command
contracts do not establish Actions execution or native destination behavior.

After Qualification, `observe-github-packages` admits current native authority
before supported reads. `materialize-publication` forms zero or one action;
only an action produces the native reviewer summary. Upload it as
`reviewer-summary.md` with the Publication Snapshot before
`form-approval-bundle`. `form-publication-authorization` replays the exact
publication basis and Bundle references, then requires fresh matching
Governance and the existing Approval boundary sentinel. A zero-action Snapshot
cannot acquire Approval. All native commands reject GitHub reruns.

`prove-exact-satisfied` repeats fresh Governance, package-control, and actual-byte
checks for a zero-action Snapshot with a skipped publisher. For an approved
action, `prepare-publication` retains the original archive in an owned runtime
directory and emits the mutation marker. Admit its immutable uploaded reference
with the shared `release admit-publication-terminal` before
`release nuget execute-publication`. Execution consumes that marker once;
conflict or response loss remains failed even after exact diagnostic readback.
The shared `release resolve-publication-terminal` resolves marker/Result transport.
`release nuget finalize-live` admits the three native Qualification Evidence
records and current terminal chain without destination reads. Missing terminal
evidence cannot establish success. Use each command's `--help` for its complete
current-run transport inputs.

The native workflows bind the actual NuGet caller path and current immutable
artifacts. An unprivileged job builds the trusted same-revision helper and
transports its complete runtime to readers and the publisher. Target evaluation,
Build, and both quality checks have no publication permission; the publisher
uses the prebuilt helper. Blocked Eligibility retains its Decision and fails
before the Attempt. Zero action uses fresh proof; one action requires the existing
Approval Environment, current Authorization, and re-admission of the uploaded
marker before invocation. Failed Result and Outcome payloads remain available
for immutable upload. Local glue tests establish no Actions or destination
behavior, and workflow availability supplies no missing native admission.

The documentation records accepted domain constraints and evidence. It grants no
runtime work and does not renew either spent npm dispatch authorization.
