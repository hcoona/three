# Workflow Delivery Common Integration Tables

These tables own only the Official channel scope and the CLI/boundary payload
table shared by every ecosystem; the MLD owns the other shared inventories and
contracts. The
[MLD](../middle-level-design.md#integration-mapping) states the shared scope and
authority of the integration contracts.

## Official Channel Scope

WD-CHN-002 requires an eligible reviewed official ref. The implemented Workflow
Delivery callers support a narrower set, which this table owns. The retired v1
Official stack ([retired records](../validation/historical-readers.md#retired-records))
is not an Official route.

| Ecosystem | Official destination | Implemented Official scope                       |
| --------- | -------------------- | ------------------------------------------------ |
| Python    | PyPI                 | Live publication only from `refs/heads/main`     |
| Ruby      | RubyGems.org         | Live publication only from `refs/heads/main`     |
| npm       | npmjs                | Dry-run simulation only; no live Official caller |
| NuGet     | None                 | No Official channel; GitHub Packages Buddy only  |

`ReleasePolicy.RequireEligible` is the one application check. Python and Ruby
apply it during preparation and again in the privileged publisher; live
Buddy there requires a branch ref under `refs/heads/`. The npm and NuGet Buddy
requests carry the dispatched ref in their subject without a ref-class check. Dry run needs no live
ref eligibility. Native Environment branch policies may constrain a channel
further; they do not replace this check. Adding another Official ref, a live
npm Official caller or a NuGet Official destination is a caller change that
updates this table and its tests together.

## Locked Tool Selection

Root `mise.toml` and `mise.lock` are the only tool and runtime version authority
for Release preparation. Python, npm and Ruby preparation query
`mise ls --json --current --locked <tool>` in the checkout and accept its single
answer only when the version is an exact release, the tool is active in the
prepared tool data and the source is the checkout's root `mise.toml`. The
actual tool must then report that version. The application keeps no version
literal, major line or floor; there is no documented native adapter limit, so a
routine committed update needs no code change. Each failure names the tool and
the failed condition ([`LockedToolSelection`](../../Release/LockedToolSelection.cs)).

The ordinary Release and CI workflows follow the same authority. Each reads
the versions it installs once from the checked-out root `mise.lock` (the .NET
SDK from `global.json`) through
[`locked_tool_versions.py`](../../../../../../eng/scripts/locked_tool_versions.py);
Release jobs without a checkout receive them as the preparing job's outputs and
still install outside the checkout. Root `mise.toml` selects Node exactly, like
UV, PNPM and Ruby, so the Renovate mise manager proposes each patch with its
lock update. The npm Buddy publisher uses the npm CLI bundled with the locked Node; no record documents a publisher npm limit.
[`test_python_runtime.py`](../../../../../../tests/eng/test_python_runtime.py)
rejects literal tool versions in workflows other than the retired NuGet Live
attempt and the separately owned `release-*.yml`/`official.yml` stack.

## CLI and Boundary Payloads

The application is `WorkflowDelivery.dll`, run with `dotnet`; these are internal
command families with JSON files only at actual process/job boundaries. The
[application interface](../../README.md#commands-and-transfers) and its CLI sources
own exact signatures. Changes to field spelling update producers and consumers
together.
Do not build an envelope registry or independent schema version service.

| Command and caller                                                                                   | Required inputs                                                                                         | Output consumed at the next real boundary                                                                                                                            |
| ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ci comparison` in root CI planning                                                                  | Native event name/payload and platform-tested candidate                                                 | Comparison basis, candidate and explicit full flag for native checkout/plan preparation                                                                              |
| `ci caller-control prepare`, `ci caller-<ecosystem> materialize/prepare` on a required native runner | Exact comparison, candidate control and fresh owned directories                                         | Materialized endpoints and requests; native facts are read in process, with explicit coverage/errors                                                                 |
| `ci caller-dotnet select`, `ci caller-<ecosystem> plan`, `ci plan*`                                  | Candidate/comparison identities, changed paths and necessary native facts, supported group              | Selected project/target/check/dimensions, reasons, required/advisory flags, runner, prerequisites and unresolved scope                                               |
| `ci caller-<ecosystem> execute`, `ci run-*` in a selected executor                                   | Selected work, candidate and required native inputs                                                     | Check/target/dimensions, actual status, native command result and output identities where consumed                                                                   |
| `ci result`, `ci caller-node join` in collection                                                     | Expected work and available candidate-bound results/native job conclusions                              | Required-check result and missing/failed/unknown reasons; no success from absent work                                                                                |
| `release job-inputs <ecosystem> <operation>` in each Release job                                     | Native run context, request subject, transferred preparation and phase                                  | Request, originals and phase directories; `receive <phase>` binds the received preparation's subject to the request                                                  |
| `release prepare-<ecosystem>` before write capability                                                | Source/control revision, unit, channel, destination, live/dry-run mode                                  | Complete qualified original files with native coordinates/version, qualification results, applicable completion checks and maximum intended writes                   |
| `release observe-*`, `describe-*` without product execution                                          | Prepared request/files and scoped read access where needed                                              | Per-file absent/exact/conflict/unknown, native metadata, remaining required checks and proposed missing writes                                                       |
| `release publish-*` in the gated publisher                                                           | Prepared selected-control artifact, qualified input identities, maximum writes and native authorization | Fresh observation, one native invocation per needed operation, raw command outcomes and bounded readback; stop application writes after invocation failure/ambiguity |
| Final `release observe-*` in the unprivileged verification job                                       | Prepared request/files and destination read access where required                                       | Required remote coordinate observations and downloaded original-byte comparisons for the complete intended set                                                       |
| `release complete-*` in collection                                                                   | Preparation, initial and final observations and publication result (`-` only when omitted)              | Satisfied/unsatisfied/unknown with command abnormality and unresolved effects kept distinct                                                                          |

In-process callers use the same typed operations without serializing each row.
The `ci caller-<ecosystem> execute` commands keep stdout as the unchanged
`CiOutcome` JSON. They also write each non-passed check's key, status and
recorded failure text to stderr, the step log. A check without failure text
reports its last unsuccessful native command. Native failure text names the
stage, termination and exit code, followed by the last 4,096 characters of each
of the native error and stderr, trimmed and marked when truncated. Because
MSBuild reports errors on stdout, the .NET graph query also includes a stdout
tail with the same bound. The .NET graph query has no retained artifact, so it
keeps only these bounded stdout and stderr tails; other complete command results
stay in the retained result artifacts. Every message line written to stderr,
including application failure messages, starts with an indented `|` marker,
and check header lines are neutralized the same way.
Each `##[` becomes `#\#[`, so native text cannot start an Actions workflow
command ([`RunnerLog`](../../Platform/RunnerLog.cs)).
The finite [GitHub comparison
mapping](../middle-level-design.md#github-event-comparison) reads native payload
fields directly rather than adding another event declaration. Its implementation
alone does not switch root CI or establish group coverage. Artifact references
crossing jobs carry the same-run immutable artifact ID, and the native download
checks its digest (`digest-mismatch: error`). Qualified file entries carry
relative path, size and digest for package comparison. Each later Release job
binds the received preparation's subject to its request with `release job-inputs
<ecosystem> receive <phase>`. These checks bind actual consumers, not approvals
or a chain of internal facts. A receiving consumer validates the actual
download.

Dry run executes qualification and supported reads, reports hypothetical writes
and missing read authorization, and never enters a write gate.
