# Workflow Delivery v3

This project owns Three's Workflow Delivery v3 control primitives and domain
records. Start with the [document set](docs/README.md) and
[agent handoff](docs/agent-handoff.md) for the authority order, current evidence,
and operating limits.

The [contraction requirements](docs/requirements.md) and replacement
[HLD](docs/high-level-design.md) govern new design. The
[middle-level design](docs/middle-level-design.md) defines component, selection,
execution, transfer and outcome contracts for the internal C# application under
`src/private/app/workflow-delivery/`. The [implementation plan](docs/migration-strategy.md) maps actual projects,
CLI/workflow callers, integration contracts and retirement order;
the runtime described below has not yet been replaced.
The [private application](../../../private/app/workflow-delivery/README.md)
now provides CI selection/results, GitHub event comparison, native Node fact assembly,
control-test resource inputs and the adopted check executor. Its
[native Ruby primitives](../../../private/app/workflow-delivery/README.md#native-ruby-primitives)
provide RubyGems/Bundler fact serialization and public RSpec JSON result handling;
native Ruby planning, execution and original-result collection now run in root CI. Root CI consumes the
native comparison and resource response before selecting its existing .NET test
owner. The [bounded Linux observation](docs/research/node-ci-native-execution.md)
qualifies that fixture's native composition and effects. The adopted Node group
and selected retained Node matrix now execute under root CI;
[PR #1042](https://github.com/hcoona/three/pull/1042) retains their ordinary hosted
transfer and execution qualification. The exclusive npm CI shadow is retired.
Python passive extraction, the pinned native supplement, metadata adaptation and
dependency projection have controlled component boundaries. The private app also
provides a [bounded native pytest configuration component](../../../private/app/workflow-delivery/README.md#native-pytest-configuration-component)
with controlled tests; the separate planning-input observation below qualifies
its exact native query and target/input composition. Complete caller wiring is implemented;
[PR #1114](https://github.com/hcoona/three/pull/1114) carries ordinary hosted
qualification. The
[paired Python producer-to-owner component](../../../private/app/workflow-delivery/README.md#paired-python-producer-to-owner-component)
has controlled supplied-fact coverage. The
[controlled paired Python fact assembly](../../../private/app/workflow-delivery/README.md#controlled-paired-python-fact-assembly)
binds supplied native targets and actual checks. The
[controlled native Python graph collector](../../../private/app/workflow-delivery/README.md#controlled-native-python-graph-collection)
composes metadata, passive inputs and ordered native dependency answers with
caller operations bound to the same metadata. The separately accepted
[current-tool observation](docs/research/python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies complete ordinary/no-dev native graph composition on its exact Linux
subjects. Its campaign and cleanup are complete, and remaining captures stopped.
The [controlled Python repository input associations](../../../private/app/workflow-delivery/README.md#controlled-python-repository-input-associations)
bind supplied actual check owners to repository engineering inputs. The separate
[planning-input observation](docs/research/python-planning-enrichment-qualification.md#completed-planning-input-observation)
qualifies native pytest, committed NBGV/quality inputs and producer input mapping
on the exact Linux subjects; complete caller qualification has its separate
ordinary PR CI gate. The
[controlled Python package component](../../../private/app/workflow-delivery/README.md#python-package-execution-component)
implements finite adopted preset expansion, preparation and execution. The
[completed corrected Linux package observation](docs/research/python-native-package-qualification.md#completed-corrected-linux-package-observation)
independently qualifies its exact four required checks and original wheel/Git-free
sdist consumers, with the audit's separately classified filename-discovery method
exception retained. That package campaign is stopped: one lifetime spent and both
unused package slots expired. The
[finite Python CI execution component](../../../private/app/workflow-delivery/README.md#finite-python-ci-execution-component)
has controlled Git/command-transfer coverage for the original pytest/package plan.
The [native Python CI caller](../../../private/app/workflow-delivery/README.md#native-python-ci-caller)
implements root CI and committed local full execution; the smoke PR entry is
retired. Actual root pytest and the complete Linux caller have ordinary hosted
qualification in [PR #1114](https://github.com/hcoona/three/pull/1114). Release remains separate.
The [prepared endpoint collector](../../../private/app/workflow-delivery/README.md#prepared-python-endpoint-collection)
composes existing native readers and paired selection with controlled coverage.
The implemented caller mapping, actual Linux query-tool preparation and complete
root/local/smoke replacement qualified together in that PR. Later caller changes
still require their applicable complete qualification. The
[primitive Linux observation](docs/research/python-native-planning.md#primitive-native-observation)
qualifies the pinned helper composition. Complete hosted caller qualification
is carried by [PR #1114](https://github.com/hcoona/three/pull/1114); completed enrichment and graph campaigns
supply no further execution allowance.
The private application's [NuGet Release preparation component](../../../private/app/workflow-delivery/README.md#nuget-release-preparation-component)
implements complete native preparation for Buddy and development dry runs
independently of CI. The [NuGet Buddy interface](../../../private/app/workflow-delivery/README.md#nuget-buddy-release)
composes the ordinary caller; component qualification does not establish hosted
Release/feed behavior or new live acceptance. Other quality presets and remaining
Release integrations are pending.
The private application's [Python preparation component](../../../private/app/workflow-delivery/README.md#python-release-preparation-component)
qualifies the complete original wheel/sdist pair independently of CI, with
controlled and local native Linux coverage. Destination observation, publication,
remote consumption and ordinary Release caller cutover remain pending; this
component does not establish hosted Release or live qualification.
The [transition boundary](docs/requirements.md#requirements-and-implementation-transition)
scopes the implementation descriptions and evidence below; they are not a claim
that the existing runtime already satisfies the contraction requirements.

Workflow v3 is repository-specific tooling, with no PyPI distribution or
third-party Python API support commitment. Its Python modules and same-revision
callers may evolve together. Compatibility obligations follow the CLI/workflow,
serialized and domain contracts for the existing implementation. The replacement
may break these internal formats/APIs while updating or retiring active callers;
it has no mandatory legacy compatibility or replay product.

New sessions start at the [handoff entry](docs/agent-handoff.md#starting-a-new-session).
The [NuGet delivery retrospective](docs/research/nuget-delivery-retrospective.md)
explains reusable integration lessons and the next-task guide.

The Python slice is the separate `hcoona-release-smoke-python` package. Its
[Python design entry](docs/README.md#python-smoke-design) routes the confirmed
requirements, HLD/MLDs, brief LLD and source evidence for TestPyPI Buddy followed
by PyPI Official. Publication and fresh clean wheel/sdist consumption
are [independently complete](docs/validation/python-normal-live-evidence.md)
for TestPyPI `0.1.0b26` and production PyPI `0.1.0b53`. The production
result joins the failed uploading Attempt with a fresh successful
`exact-satisfied` Attempt and its separate native consumer audit. Both
completion campaigns are stopped. The production
bootstrap failed at wheel readback; its request is retired and its partial/unknown
effects remain failed. The
[handoff](docs/agent-handoff.md#starting-a-new-session) routes current operation
limits, and the [delivery gates](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#python-smoke-delivery)
keep implementation, configuration/admission and publication separately authorized.
The npm and NuGet smoke objectives remain complete.
The [partial-publication recovery design](docs/hcoona-release-smoke-python-lld.md#approval-and-terminal-contract)
is implemented: fresh reproducible builds and qualification feed exact-subset
completion, missing-file uploads and strict terminal audit. Real-build and
controlled-transport recovery tests preserve prior failures. The
[hosted recovery evidence](docs/validation/python-hosted-recovery.md) establishes
wheel-present/sdist-missing completion for `0.1.0b39`: two independent builds
produced identical archives, only the missing sdist was uploaded, and fresh
native readback and separate clean consumers passed. The campaign stopped at
its first independently audited success. Its temporary interruption control
and operators are retired; pinned historical readers preserve evidence replay.
The inverse subset remains locally tested, without a hosted claim.

The old `governance run-fixed-acceptance-probe` command and fixed-coordinate
Python producer APIs are retired. `governance admit-acceptance-evidence` and
reviewer inspection still read historical evidence; the original bytes and
provenance remain retained. Old producer replay requires its exact historical
Git revision and separate operation authorization. Retained npm/NuGet native
acceptance commands serve their historical consumers; ordinary Release callers
use the private application's interfaces.

The [Python smoke package](../hcoona-release-smoke-python/README.md) provides the
wheel/sdist proving payload. The [Python LLD](docs/hcoona-release-smoke-python-lld.md)
owns its concrete build, qualification and publication contracts.
Root native Python CI checks the tested PR merge target. The Python smoke
workflow provides manual Release only; its TestPyPI/PyPI entry first requires the
destination's protected Governance.
The revised
[platform-reliance design](docs/hcoona-release-smoke-python-lld.md#python-governance-v2-migration)
is implemented as strict v2. Normal runtime rejects v1 and hybrid admission.
The [handoff](docs/agent-handoff.md#starting-a-new-session) routes current
admission evidence, both completed destination audits and spent operation limits. The [retired native tooling boundary](docs/validation/python-native-readiness.md)
retains failed-evidence consumers with both request slots null. No duplicate/race
suite is a prerequisite. Configuration/admission and actual publication retain
separate grants and each destination still needs exact readback and clean consumers.

The [npm smoke package](../../../../tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-npm/README.md) owns its small
package purpose. This project owns the
[first-slice proving protocol](docs/hcoona-release-smoke-npm-lld.md).
The [NuGet authority helper](../../../private/app/workflow-delivery-v3-nuget-authority/README.md)
is a static-reference component. The
[NuGet second-slice handoff](docs/nuget-smoke-research-handoff.md) routes the
confirmed requirements, protected design and implementation. The
[selected NuGet smoke library](../hcoona-release-smoke-github-packages/README.md)
owns its marker-product purpose; the
[native .NET helper](../../../private/app/workflow-delivery-v3-dotnet-provider/README.md)
is retained for historical Provider and package-inspection consumers. The handoff
retains the distinct native-operation and publication authorization gates. The
following record-chain summary describes retained historical source and evidence,
not the replacement ordinary caller. Explicit NuGet Release
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
and [reusable Release](../../../../.github/workflows/workflow-delivery-v3-nuget-live-attempt.yml)
now compose the private application's [NuGet Buddy interface](../../../private/app/workflow-delivery/README.md#nuget-buddy-release)
on Windows. Their current preparation, native operations and result boundary are
described there. The [audited native generation and Governance evidence](docs/research/nuget-smoke-evidence.md#2026-09-15-query--native-acceptance-and-governance-activation)
are bound by the exact production admission and ready Governance with Live enabled.
Windows normal-Live publication and fresh exact-version destination consumption
are [independently verified](docs/validation/nuget-normal-live-evidence.md).
The [native fixture and operator contract](docs/hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
defines acceptance-tooling preparation and its evidence boundary.

The offline [NuGet fixture component](src/three_workflow_delivery_v3/acceptance/nuget_fixture.py)
prepares original A/B archives from one frozen compilation, inspects their
native identity and payload relationship, and requires A's clean local
consumer. It retains inputs, original bytes and diagnostics. Its local SDK
checks do not establish Windows or destination evidence. The retired
[preparation entry](https://github.com/hcoona/three/blob/b361a52908d44bbb5c2c961aee0dbdacb6cfa72e/.github/workflows/workflow-delivery-v3-nuget-fixtures.yml)
connected protected request, trusted helper and native setup transport to the
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

The retained [one-probe component](src/three_workflow_delivery_v3/acceptance/nuget_probe.py)
and retired [Windows acceptance entry](https://github.com/hcoona/three/blob/b361a52908d44bbb5c2c961aee0dbdacb6cfa72e/.github/workflows/workflow-delivery-v3-native-nuget-acceptance.yml)
consumed an independently admitted original fixture pair and complete helper.
The following entry and publisher behavior describes that historical source.
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

The retained [profile observer](src/three_workflow_delivery_v3/acceptance/nuget_profile.py)
and retired [Windows entry](https://github.com/hcoona/three/blob/b361a52908d44bbb5c2c961aee0dbdacb6cfa72e/.github/workflows/workflow-delivery-v3-nuget-profile.yml)
measured the publication profile without a package credential, helper
or SDK invocation. The following description serves its historical consumers.
`python -m three_workflow_delivery_v3.acceptance.nuget_profile --help`
describes its canonical input and retained output. Its small inline request
binds protected tooling and the exact resource projection from a complete,
independently audited preflight. Resource hashes alone do not establish that
audit or authorize dispatch. The observer shares discovery's URL validation,
including the exact owner-root publish address, and retains the complete
actual Windows profile for independent review. It neither admits a native
generation nor enables Live; source availability grants no observation run.

The private application's [NuGet remote consumer component](../../../private/app/workflow-delivery/README.md#nuget-remote-consumer-component)
implements official native restore, original-byte comparison, build and marker
invocation with a separate no-write caller boundary. Its controlled validation
does not establish ordinary CLI/workflow cutover or new live-feed acceptance.
The legacy coordinator and restore host below retain their existing consumers
until the corresponding cutover and retirement.

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

The retained [fixed-suite operator](src/three_workflow_delivery_v3/acceptance/nuget_suite_operator.py)
connected those components in the accepted order: create A, verify the creation
delta, consume A, reject identical A, then reject equivalent different-byte B.
It retains all six captures and compares official native coordinates and GitHub
object identities, including unchanged neighboring versions. Original probe
uploads bind the exact current run, prepared input, invocation marker, response
and result. A duplicate rejection preserves its conservative mutation flag;
it is not a successful publication.

The three campaign workflow definitions are retired from the current tree.
The suite's remote dispatch route is unavailable because its collector requires
the retired acceptance workflow. Retained components and tests serve original
source/evidence readers; they are not an alternative executable campaign.
Git preserves the definitions linked above. The
[accepted native evidence](docs/research/nuget-smoke-evidence.md#2026-09-15-query--native-acceptance-and-governance-activation)
retains exact Native R4 source, profile producer, original artifacts, failures
and spent lifetimes. Removal grants no replacement dispatch or publication.
Ordinary requests use the private [NuGet Buddy interface](../../../private/app/workflow-delivery/README.md#nuget-buddy-release).

The remaining NuGet command descriptions serve retained historical source and
evidence readers, including the retired fixture, profile and acceptance sources
above.
They do not describe the replacement ordinary caller or reopen any stopped
campaign. The [handoff](docs/agent-handoff.md) retains operation limits.

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

The ordinary NuGet entry and reusable Release route to the private interface
above. Its source implementation and controlled tests establish no new hosted
Release/feed qualification. The retained old command/marker descriptions preserve
their historical consumers and terminal outcomes; they do not supply ordinary
caller admission or new operation authorization.

The documentation records accepted domain constraints and evidence. It grants no
runtime work and does not renew either spent npm dispatch authorization.

## Ruby Build and Qualification

The [Ruby requirements](./docs/requirements.md#ruby-smoke-slice) and
[brief LLD](./docs/hcoona-release-smoke-ruby-lld.md) define the selected
GitHub Packages then RubyGems.org journey. [Migration gates](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#ruby-smoke-delivery)
separate implementation and actual acceptance. The [Ruby smoke](../../../../tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-ruby/README.md)
has exact-target native Provider facts, a closed Model, frozen original-gem Build,
immutable Artifact records and separate CI and Release qualification. Both
qualification paths require original-package inspection and an isolated native
installed consumer. Release rejects CI evidence and binds its own current Attempt.
The native registry readers verify the remote original gem. The implemented
[download correction](docs/ruby-operation-protocol.md#github-gem-download-continuation)
permits one explicit credential-free GitHub storage continuation, retaining
original verified gem bytes and screened redirect evidence. Strict offline
replay verifies its safe route and response joins without retaining signed URLs. The singular publication path binds immutable approval,
rechecks missing state before its marker, uploads once and admits visibility
reads only after HTTP 200. Strict replay joins this evidence to the shared
scalar Attempt Outcome; zero-action completion requires a fresh exact proof.
Root [CI](../../../../.github/workflows/ci.yml) owns native Ruby planning,
complete selected RSpec/gem checks and original result collection. The former
Ruby shadow PR workflow is retired. Node planning receives both original Ruby
fact endpoints from the same run, and selected Hexo checks prepare their native
locked bundle. Manual Release retains its existing record actions and contracts
until its own caller transition.
The GitHub control adapter checks protected Governance, native approval and the
fixed RubyGems OIDC audience. Separate manual
[bootstrap](../../../../.github/workflows/workflow-delivery-v3-ruby-bootstrap.yml)
and [normal publication](../../../../.github/workflows/workflow-delivery-v3-ruby-smoke.yml)
workflows connect fresh Provider/Build/qualification, exact immutable artifact
readbacks, destination-specific publisher permissions and strict terminal replay.
Their shared operation CLI keeps first-project authority separate from normal
Governance and skips the privileged job for exact-satisfied publication.
An unprivileged final job verifies the remote original and a clean consumer.
The [Ruby normal Live evidence](docs/validation/ruby-normal-live-evidence.md)
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
