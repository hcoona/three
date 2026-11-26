# Experiment Safety Protocol

## Scope

This policy applies before running an upstream or v2 authentication binary, restore,
build, cache, installer, or migration experiment. [Development
validation](#development-validation) is not an experiment.

Experiments may use repository-owner-designated existing machines and authorized account
state. They must keep effects within the accepted experiment boundary, protect unrelated
state, and publish only sanitized evidence. Disposable accounts, operating-system users,
VMs, and a fresh-state environment are not general prerequisites.

## Development Validation

Development validation checks repository source without observing real platform,
account, or service behavior. It is not an experiment and needs no protocol, but the
work it validates still needs Delivery Wave authorization. It is limited to:

- ordinary credential-free restore, build, tool evaluation, and synthetic scenario runs,
  using the pinned toolchain, the public package sources named by the repository
  configuration, and the existing local package cache, on a contributor host, a
  repository-owner-designated host, or a GitHub-hosted runner;
- deterministic operating-system checks in those scenarios that read the current
  process or thread state; that temporarily change the state of the test process, its
  own threads or its child processes and revert or end that change before the test
  exits; or that create, under a fresh temporary root for the run, temporary files,
  junctions, child processes, console process groups, and hidden windows owned by the
  test process. Child processes, console process groups and windows are destroyed
  before the test process exits. Files and junctions may remain inside that root, and
  whoever runs the scenarios disposes of the root;
- a provider-library call whose HTTP transport is a synthetic handler that returns
  canned responses and fails the test on any unexpected request, that uses no account
  or credential, and that opens no browser, operator surface, or window not owned by
  the test process;
- opening and analyzing a repository record in its native desktop tool when its record
  family requires that validation, under the conditions the authorizing Wave entry
  states.

A run that uses an account or credential, starts real interactive or broker
authentication, contacts an identity or authority-discovery endpoint, uses a
credential-bearing or non-public dependency source, or changes state outside its
checkout, build output, the local package cache, its temporary root, and any
tool-owned state the Wave entry names is not development validation. An existing local
package cache is not a non-public source by itself: restore runs in locked mode against
the committed lock files and configures no credential-bearing or non-public source.
Development
validation supports claims only about the tested source. It supports no public-restore,
real-platform, or support claim.

## Experiment Authorization

Before any experiment covered by this policy runs:

- the target branch's accepted Delivery Wave entry must authorize the
  decision-relevant question, environment, maximum external effects, and bounded
  outcome;
- a Git-tracked protocol defining the exact subject, source and dependency versions,
  environment and account-state boundaries, expected observations and interaction,
  evidence limits, finite attempt/time/cumulative-effect bounds, stop conditions, and
  cleanup or intentional retention must be independently reviewed and accepted on
  `main-v2`;
- execution and preflight must bind the exact accepted protocol revision; and
- every applicable contextual review and mechanical precondition must be satisfied.

The Delivery Wave entry must include an explicit repository-owner risk decision before
first crossing or expanding a boundary involving non-disposable identities or tenants,
credential-bearing state, persistent host, account, cache, or installation state,
remote mutation, or another material external effect. No separate owner approval is
required for each execution that remains within the accepted entry and protocol.

An accepted protocol may permit repeated executions only within its finite bounds. Count
manual and automated attempts, including failed starts and interrupted runs. Before each
attempt, recover prior consumption from retained sanitized execution evidence and record
that the attempt has started before invoking the subject. A protocol involving persistent
state or cumulative external effects must define measurable units and prohibit concurrent
execution unless it defines safe capacity reservation. A simple sequential attempt record
is sufficient; a general runner or reservation service is not required.

Protocol revisions and machine switches do not reset consumed capacity. Record prior
attempts and remaining limits, and fail closed when remaining capacity cannot be
established. Deleting or narrowing the Delivery Wave entry ends or narrows authority for
subsequent execution.

An Issue, Milestone, branch, pull request, comment, protocol, or unmerged Delivery Wave
change cannot grant experiment authority. Non-executing planning and review must not
invoke the binary or tool under study, resolve dependencies, access a credential or
account store, or create another planned side effect.

## Environment and Effects

### Source and Build

- Pin source-built subjects to a recorded commit in a detached checkout; record the
  dependency versions and any probe changes. For a released binary, record its public
  provenance, version, and artifact identity.
- A claim that public restore works requires an empty package cache and no inherited
  package-source credentials. Do not use a cached private package to support that claim.
- For other experiment preparation, declare public dependency sources, existing cache
  use, toolchain and artifact locations, and their evidence limits in the protocol.
  Do not silently use ambient credentials or infer a clean restore from a warm cache.
- A credential-bearing or non-public dependency source, such as a private feed, a
  credential provider, or an experiment cache seeded from either, requires an explicit
  repository-owner risk decision in the Delivery Wave entry and protocol coverage. An
  Issue, pull-request comment, or other disposition outside the merged Wave cannot grant
  it.
- Keep build output and experiment package caches outside production install paths.
- Record publicly reviewable nonpublic-dependency blockers without accessing private
  feeds, packages, service connections, or signing identities.

### User and Credential State

- The protocol identifies the owner-designated machine, host/session, selected account
  roles, relevant existing state, and permitted observations and updates. Public records
  use sanitized roles and state descriptions, not account, tenant, or machine identifiers.
  The operator supplies actual account selectors locally without putting them in logs or
  committed records.
- Existing OS accounts, broker sessions, and secure caches may be used when the accepted
  Wave and protocol cover that state and its ordinary authentication updates. Describe
  relevant initial state and what is unknown; do not assume an existing environment is
  clean or clear unrelated state to simulate first use.
- Use documented account and authentication APIs. A protocol may permit in-memory
  account enumeration needed for selection, while prohibiting acquisition as an
  unrelated account. Do not dump broker contents or copy refresh-token caches into the
  repository, agent traces, or session artifacts.
- Application-owned experiment files should have dedicated locations. That separation
  does not isolate an OS broker or change its account scope. Declare any intentional
  access to an existing application cache, keychain, keyring, registry value, or
  installation, including whether access is read-only or allows normal authentication
  updates. Never modify an unrelated store or installation.
- Native and cross-host execution must identify both the initiating environment and the
  host that owns the account state and interaction. WSL invocation of a Windows helper
  requires explicit protocol coverage of the Windows-side broker, storage, transport,
  UI, and termination boundaries and the applicable rechecks.
- The owner may operate steps or switch among protocol-covered machines. A machine,
  account-state context, or effects boundary outside that coverage requires protocol
  amendment and, if it expands the Wave's effects, a new accepted owner decision.

### Interaction and Telemetry

- Before running an upstream AzureAuth v1
  ([AzureAD/microsoft-authentication-cli](https://github.com/AzureAD/microsoft-authentication-cli))
  binary, disable its telemetry through its documented controls. This policy does not
  govern telemetry from the toolchain, experiment tools, or host-wide programs.
- When the behavior of a telemetry switch is under test, isolate network access and
  record only sanitized endpoint and field observations.
- Record the expected interactive surface before execution. Keep sign-in, account
  choice, user consent, and any state-unlock interaction under operator control.
- Ensure the operator can identify and close prompts created by the test. Authentication
  secrets and device codes may appear only on the intended operator surface, not in
  captured output, agent traces, screenshots, or committed evidence.
- Require operator attendance when an accepted action needs human input, a choice,
  or state unlock. Finish preparation and verify the actual
  waiting action before requesting a fresh readiness response. Do not run those
  actions in CI or unattended sessions. Manual assistance must use the accepted
  procedure and counts toward the same attempt limits.
- Fully automated, credential-free synthetic window tests do not require operator
  attendance solely because a window is visible. Their accepted protocol and exact
  admission must cover the automation, owned surfaces, required observations, and
  bounded termination on the designated host/session. Keep sign-in, account choice,
  consent, and state unlock under operator control. Unexpected human interaction
  follows the protocol's stop procedure; it is not permission to automate it.
- Do not request desktop attendance solely to watch automated work. Use admitted
  automation for observations; if it cannot establish a required observation,
  leave that claim unvalidated until suitable evidence is admitted.

### Network and Resource Effects

- Declare identity endpoints, client registration, authority class, scopes, and any
  read-only resource probe. Bound required public dependency downloads separately.
- Prefer token acquisition and read-only resource probes. Ordinary authentication may
  update the selected account's sessions and secure reusable state when authorized.
- Do not create, delete, push, publish, revoke, or mutate remote resources unless that
  side effect is the explicit subject and lies within the accepted Wave and protocol.
- Do not create PATs as an incidental fallback or automate administrator consent.
- Bound every operation with a documented timeout and termination procedure.

### Termination and Retention

The protocol identifies experiment-controlled processes, listeners, prompts, and files,
and how to stop or retain them. Rely on documented process, broker, and storage contracts
within the workstation threat model. Do not require proof of all internal OS or provider
activity, terminate a shared broker, or repair unrelated account state.

On timeout or cancellation, end the attempt and terminate experiment-controlled work
using the declared bounded procedure. Further attempts require that the protocol's stop
conditions permit continuation and its remaining capacity is known. An unexpected effect
stops further attempts. Record any uncertainty; if safe termination or file ownership
cannot be established, preserve the affected state and do not perform speculative cleanup
or continue the experiment.

Delete only identified experiment-owned artifacts when cleanup is safe. Retain normal
selected-account session or secure-cache updates when the protocol declares that outcome.
Deleting local files does not reverse provider-side authentication, consent, or session
changes. Do not sign out, revoke consent, erase caches, or promise automatic rollback of
existing account state as incidental cleanup.

## Authentication Experiment Records

Every committed authentication-experiment result must state the applicable fields below
and explicitly mark nonapplicable context when omission could change interpretation:

- accepted protocol revision and actual subject/source or artifact identity;
- v2 commit, if applicable;
- MSAL and native-broker versions;
- operating system, architecture, WSL version, and initiating/interaction host types;
- sanitized account-state shape and relevant prior-use or unknown-state limitations;
- client profile, authority class, scopes, and requested policy;
- application-file separation and declared existing account/cache/configuration access;
- telemetry, network, and sensitive-output controls;
- expected UI and typed result;
- observed UI, result, and state effects, including operator-assisted observations;
- manual steps, failed starts, interruption, and termination outcomes;
- cleanup performed or state intentionally retained, without claiming remote rollback;
- attempt history, prior consumption, remaining capacity, and known variability;
- whether the record is a source finding, runtime observation, inference, or hypothesis.

Record decision-relevant sanitized outcomes such as account-match status and metadata
availability rather than identities, token contents, or raw broker diagnostics. Manual
observations have the same protocol, provenance, and review requirements as automated
ones. Existing-state success cannot establish fresh-state behavior, another host's
behavior, or broader platform/Profile support.

## Stop Conditions and Deviations

Stop the experiment if:

- a real token, code, credential, or private account detail would enter captured or
  retained evidence;
- a prompt appears outside the declared interaction policy or in an unexpected session,
  or cannot be identified by the operator;
- account acquisition, cache/store access, host execution, or network effects exceed
  the accepted protocol;
- a public-restore claim would depend on inherited credentials or cached private
  packages;
- native or cross-host execution accesses an environment not covered by the protocol;
- remaining authorized attempts or cumulative capacity cannot be established;
- experiment-controlled work cannot be stopped within the declared bounds, or safe
  ownership cannot be established for cleanup;
- the subject's source or artifact identity no longer matches the accepted protocol; or
- continuing would mutate an unrelated account, installation, or remote resource.

A deviation is execution outside the accepted Wave entry or protocol, including
continuing after a stop condition fires. After a deviation, stop. Make no further attempt
until a merged Wave or protocol change covers it. Evidence from the deviating attempt or
produced after it is admitted only by repository-owner disposition with rationale in the
experiment's governing carrier; that disposition cannot authorize further execution. The
experiment record states each deviation, its disposition, and the cleanup performed or
state intentionally retained because of it.
