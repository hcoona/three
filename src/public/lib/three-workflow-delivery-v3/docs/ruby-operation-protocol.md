# Ruby Configuration and Operation Protocol

## Scope and Delivery Boundary

This record implements the configuration and first-project admission portion of
[the Ruby LLD](./hcoona-release-smoke-ruby-lld.md#governance-and-first-project-protocol)
under [Issue #954](https://github.com/hcoona/three/issues/954) and the accepted
[Delivery Wave](../../../../../docs/delivery-wave.md). The V3 maintainer maintains
it for configuration-validator authors, the operator and independent
admission reviewers. Its distinct consumer is the concrete endpoint, phase and
request-binding contract; the LLD owns architecture and the Wave owns authority.

This protocol bounds configuration and the hosted/external operation callers.
The current source delivery contains [first-project validators](../src/three_workflow_delivery_v3/release/ruby_configuration.py)
and a separate [RubyGems project observer](../src/three_workflow_delivery_v3/adapters/ruby_project.py).
Their closed data contracts do not establish live configuration, source provenance,
independent approval or an executable caller. Fixed GitHub control/package
collectors, a supervised configuration transport and durable phase/send counters
are implemented. The hosted bootstrap/normal caller and its phase, job and maintained-action
budgets are implemented below. The external caller contract fixes actual-run,
artifact and approval budgets. Concrete callers still need their exact
independently reviewed source, launch and before/after plans.
The GitHub Packages first-project configuration and sole bootstrap slot are
enabled. The other nine slots remain null; both normal Governance sources remain
blocked. Fresh predispatch inspection and independent exact-request admission
remain required. No account, registry, OIDC or dispatch effect follows from
source or protocol delivery alone.

GitHub Packages completion precedes RubyGems.org configuration/execution. Exact
NBGV, reproducible original gems, current-run Qualification, Approval/Authorization
and independent terminal/consumer audits retain the LLD's gates. Bootstrap
establishes the first project; it is not normal completion.

## Fixed Configuration

| Binding                 | GitHub Packages                                 | RubyGems.org                                        |
| ----------------------- | ----------------------------------------------- | --------------------------------------------------- |
| Registry                | `https://rubygems.pkg.github.com/hcoona`        | `https://rubygems.org`                              |
| Repository / main       | `hcoona/three`, `refs/heads/main`               | Same                                                |
| Package                 | `hcoona-release-smoke-ruby`                     | Same                                                |
| Environment             | `workflow-delivery-v3-ruby-github-packages`     | `workflow-delivery-v3-ruby-rubygems`                |
| Bootstrap workflow      | `workflow-delivery-v3-ruby-bootstrap.yml`       | Same                                                |
| Normal workflow         | `workflow-delivery-v3-ruby-smoke.yml`           | Same                                                |
| Configuration principal | Existing admitted local GitHub owner credential | Owner's authenticated RubyGems website session      |
| Account                 | `hcoona`, GitHub ID `712433`                    | Confirmed account `Raffle2282`                      |
| Runtime publisher       | Repository job token                            | One short-lived gem/pending-publisher OIDC exchange |

Account control is already confirmed. Actual project ownership and service
registration still require observation. Do not collect credentials, repeat
unchanged account confirmation, install secrets or export the local owner
credential to Actions. Unsupported current credential capabilities stop the
selected plan; no scope upgrade or static-token fallback is included.

## Configuration Lifetime, Transport and Evidence

Use one independently reviewed canonical configuration plan per destination,
with exact source/caller/protocol hashes, a fresh identity and fixed durable
ledger location. Its absolute lifetime is at most seven days; each phase has a
30-minute outer deadline. Reserve phases and sends before transport, including
failed sends. Counters persist across crashes: no retry, reset, refill, transfer
or reconstruction after state loss. Missing, stale or unknown state stops.
Configuration counters are distinct from operation slot reservation.

Direct administrative REST uses verified TLS, `https://api.github.com`, JSON
media type and API version `2022-11-28`. Bound complete bodies to 2 MiB and
retained header envelopes to 64 KiB; permit no redirect, automatic retry or
conditional 304. Reject malformed, oversized, truncated or ambiguous responses.
The configuration transport supervises each send in a POSIX child with a
30-second **wall-clock** request deadline, further capped by phase/plan expiry,
and bounded child cleanup. Its fixed public RubyGems owners role uses
`https://rubygems.org` without Authorization. The separate RubyGems observer's
default transport has only a socket timeout; the completed operator must use
the supervised configuration response with that observer's classifier.
These limits do not bound browser assets, Actions internals or Git wire traffic.

Retain original HTTP body bytes outside the checkout with sanitized headers,
method/path, observed times, status, byte count, digest and durable phase/send
ordinal. Exclude authorization, cookies, assertions and tokens. Independently
inspect complete private owner inventories before publishing screened facts;
a public digest cannot substitute for that inspection. Do not put unrelated
package/account inventory into workflow inputs or public evidence.

The [durable ledger](../src/three_workflow_delivery_v3/release/ruby_configuration_ledger.py)
fixes the plan, phases and request roles and reserves each send before transport.
A root append-only journal reserves each phase before directory creation and
joins its immutable phase digest to the complete directory inventory. Missing
phase directories or journal entries, torn appends and inconsistent membership
stop further use. Interrupted, failed or altered evidence cannot resume a phase
or fund another.
The [transport](../src/three_workflow_delivery_v3/platform/ruby_configuration_http.py)
requires reserve/retain callbacks; the operator must retain original
responses before asking the ledger to complete a send, including responses that
arrive after phase expiry. A late response cannot make an expired phase succeed.
The [collector](../src/three_workflow_delivery_v3/platform/ruby_configuration_github.py)
validates complete control and owner-package inventories and preserves private
originals separately from screened facts. Local collector limits do not replace
persistent counters or independent inspection.

## Complete GitHub Control Inspection

One inspection unit has at most **32 direct reads**. Let
`R=/repos/hcoona/three` and `E=R/environments/<selected-environment>`.
Validated responses supply IDs; callers cannot supply arbitrary endpoints.

| Subject                       | GET endpoint                                           |  Maximum reads |
| ----------------------------- | ------------------------------------------------------ | -------------: |
| Principal                     | `/user`                                                |              1 |
| Repository                    | `R`                                                    |              1 |
| Main                          | `R/branches/main`                                      |              1 |
| Classic protection            | `R/branches/main/protection`                           |              1 |
| Effective main rules          | `R/rules/branches/main?per_page=100&page=n`            |              5 |
| Applicable rulesets           | `R/rulesets?includes_parents=true&per_page=100&page=n` |              5 |
| Relevant ruleset details      | `R/rulesets/{id}?includes_parents=true`                | 5 distinct IDs |
| Collaborators                 | `R/collaborators?affiliation=all&per_page=100&page=n`  |              5 |
| Environment                   | `E`                                                    |              1 |
| Secret metadata               | `E/secrets?per_page=100&page=n`                        |              2 |
| Variables                     | `E/variables?per_page=100&page=n`                      |              2 |
| Deployment branch policies    | `E/deployment-branch-policies?per_page=100&page=n`     |              2 |
| Custom deployment protections | `E/deployment_protection_rules`                        |              1 |

The last endpoint is unpaginated: require `total_count` to agree with the complete
array, without invented page parameters. For paginated endpoints require unique
identities, consistent totals where present, supported origin/path/query
continuity and proven terminal pagination. A full page at the cap without a
supported terminal indication is incomplete. More than five relevant rulesets,
unresolvable inherited details or uninterpretable conditions/bypass principals
stop. No generalized ruleset engine or silent increase is required.

Require actual principal and repository owner `hcoona` / `712433`, exact
repository identity, protected main and no foreign collaborator with
push/maintain/admin rights. Preserve full enforcing classic/ruleset requirements,
ref conditions and bypass facts for independent review. A classic-protection
404 alone cannot establish absence of protection or adequate access: ruleset-only
protection requires authenticated complete effective rules/details and independent
acceptance. No repository protection/access mutation is included.

Expected Environment state is one required user reviewer `712433`,
`prevent_self_review=false`, wait timer zero, no secrets, exactly one custom
branch policy for `main` and no tag policy, no custom app protection, and variable
`WDV3_APPROVAL_ENVIRONMENT_MARKER=<environment>/v1`. Unexpected relevant variables,
reviewers, policies or secrets block. Record the actual Boolean
`can_admins_bypass`; either value is accepted under the existing owner disposition.
Never invent a missing Boolean or send it as an undocumented write field. If REST
cannot establish it, permit one authenticated settings-page inspection per unit;
ambiguous state stops. This is a logical UI inspection, not a wire-request bound.
An absent optional wait-timer rule in a complete valid protection-rule array
means no configured delay; retain whether zero was explicit or absence-derived.
A present rule requires integer zero. An unavailable or malformed whole array
does not establish disabled protection.

## Bounded Configuration Writes

Independently inspect the initial state and exact before/after delta before
writing. Matching resources require no write; unknown or foreign settings stop.
Each destination has at most these three single-use mutation roles:

1. Create or update the understood selected Environment with `PUT E`, expected
   HTTP 200. The body contains only `wait_timer: 0`, `prevent_self_review: false`,
   `reviewers: [{"type":"User","id":712433}]` and
   `deployment_branch_policy: {"protected_branches":false,"custom_branch_policies":true}`.
2. Only if absent, create the main branch policy with
   `POST E/deployment-branch-policies`, body `{"name":"main","type":"branch"}`,
   expected HTTP 200 and exact created identity.
3. Only if absent, `POST E/variables`, body
   `{"name":"WDV3_APPROVAL_ENVIRONMENT_MARKER","value":"<environment>/v1"}`,
   expected HTTP 201 with a JSON empty object `{}`. Validate the JSON media
   type and schema; an empty HTTP body is not this response. A reviewed
   correction to an existing marker instead uses
   `PATCH E/variables/WDV3_APPROVAL_ENVIRONMENT_MARKER`, body
   `{"value":"<environment>/v1"}`, expected HTTP 204. Create and update share
   this one role.

### Exact M01 Read-Only Continuation

The original GitHub configuration campaign's marker-create request received
documented HTTP 201 and `{}`, but its caller incorrectly required an empty
body. The original failed receipt and incomplete phase remain unchanged;
the affirmative service response does not establish current marker value or
ready configuration. The corrected classifier accepts only a JSON empty
object for create 201 and retains no-body handling for update 204.

The protected [M01 acknowledgement](../src/three_workflow_delivery_v3/release/ruby_configuration_continuation.py)
addresses only the exact independently disposed plan, failed records and
five-phase original journal prefix pinned in its source. It is not a general
failed-phase recovery mechanism. The normal ledger entry still rejects every
incomplete prior phase. An explicit acknowledgement permits the original
unused GitHub read phases below, using the same ledger, phase reservations,
pre-send accounting and endpoint limits. All three writes remain spent.

An independently reviewed canonical
`workflow-delivery/v3/ruby-configuration-m01-continuation-v1` acknowledgement
must exist at the original ledger's sibling
`<ledger-name>.m01-continuation.json`. Its exact fields are `schema`,
`plan-digest`, `failed-phase-digest`, `failed-request-digest`,
`failed-response-digest`, `failed-receipt-digest`, `original-journal-digest`,
`source-commit`, `caller-digest`, `protocol-digest`, `reviewer`, `author`,
`reviewed-at`, `carrier` and `verdict`. The original evidence hashes are fixed
to this incident, while the three corrected source identities bind the clean
protected descendant checkout, corrected external caller and this protocol.
The original plan's source, caller, protocol, identity and expiry are never
rewritten. The corrected caller verifies original caller bytes and the
original protocol in Git before checking its corrected source/module lineage.

The accepted review follows the retained failure, precedes current time and
original plan expiry, and cites the governing Issue 954 comment. Author and
reviewer differ; independent provenance remains a procedural gate, not an
inference from supplied strings or hashes. The separate external M01 caller
has no initialization or mutation command. Preserve the original caller bytes
for source lineage; do not launch that old caller to bypass the failed phase.

The first new phase is `post-configuration-controls`, at most 32 reads. Its
existing variables inventory establishes marker readback as part of complete
control inspection; no preliminary diagnostic allowance is added. Independently
accept those actual originals before later activity. The remaining eligible
original read phases are `bootstrap-controls` (32), `bootstrap-inventory` (10),
`bootstrap-main` (1), `postbootstrap-controls` (32),
`postbootstrap-package` (1), and each original normal01 through normal04 slot's
`controls` (32), `package` (1) and `main` (1). Their existing purposes, separate
command admissions, protected-target freshness and independent gates remain.
These reads cannot activate an operation slot or authorize publication.

Each new phase records the same `continuation-digest` in its journal-bound
phase identity. The original failed files, original response, plan, journal
prefix and fixed acknowledgement bytes are rechecked before every active
send, receipt and finish. Every other prior phase must be fully complete;
another partial or failed phase stops continuation. Missing or changed
acknowledgement/evidence cannot be recreated. Completed or started phases
cannot be reopened, and ordinary GET-only phase validation forbids mutations.

Keep the original plan's absolute expiry and each read phase's at-most-30-minute
deadline. Do not relabel or complete marker-write, renew the plan, reset a
counter, create another ledger, transfer quota or repeat any write. A missing,
mismatched or ambiguous control stops without a corrective write allowance.
Protected correction delivery and independently reviewed concrete source,
acknowledgement and read-phase admission must precede actual continuation.

Persist intent, ordinal and exact method/path/body digest before sending.
Independently verify each definitive response before the next mutation; a failed
or uncertain result stops all later writes. Full postwrite inspection establishes
configuration. No rollback, deletion, automatic repair or replay is included.

There are zero branch-protection, repository-access, package visibility/access,
yank or deletion mutations in this plan. Package `github_repo` metadata names
`https://github.com/hcoona/three`; actual association and effective access must
still be inspected after creation. If insufficient, stop and prepare the smallest
separately reviewed correction under the existing Wave. Do not invent package
management endpoints or silently consume broader access authority.

## Whole-Project Absence and Native Read Accounting

### GitHub Packages

After confirming principal and existing supported `read:packages` capability,
read `GET /user/packages?package_type=rubygems&per_page=100&page=n` without a
visibility filter, at most ten pages. Validate complete owner namespace,
package type/name/ID, ownership context and terminal pagination. Any matching
project blocks bootstrap, including private, unlinked or zero-version objects.
Only a complete successful inventory without the target establishes current
absence. A package-specific 404, public-user inventory or repository-visible
native index cannot substitute.

Run this inventory once during initial configuration and once during bootstrap
predispatch inspection, each with its own ten-page allocation. Initial absence
is preparation; only fresh independently admitted predispatch evidence supports
the request. The owner credential stays local. The collector and reviewed caller must retain and join raw response, principal
and pagination provenance; screened-fact validators alone do not establish
inventory completeness.

### RubyGems.org

Send one credential-free GET to
`https://rubygems.org/api/v1/gems/hcoona-release-smoke-ruby/owners.json`, accepting
JSON, without conditional headers. A complete valid HTTP-200 JSON owner-object
array, including `[]`, means **present** and blocks bootstrap. Only HTTP 404
with media type `text/plain` and exact body `This rubygem could not be found.`
means absent. Unknown content/status, malformed data, redirects, timeout or
truncation mean unknown and stop. Apply the 2 MiB bound and final caller deadline.
Existence classification does not validate account ownership or registration.

This source contract uses direct project lookup; version inventory or gem-info
may instead report absence when no active versions exist. Neither destination's
method proves historical nonexistence, deleted history, atomic reservation or
future exclusivity. Unexpected deployed behavior stops and triggers source/profile
revalidation; pinned source alone is not a deployed-service observation.

The operation envelope retains its **32-send** partitioning: eligibility 3,
pre-marker 3, execute 20, zero-action 3 and remote-consumer 3. RubyGems owners
reads compose as follows:

| Phase                      | Owners reads | Other reads / allocation                                                                        |
| -------------------------- | -----------: | ----------------------------------------------------------------------------------------------- |
| Preliminary configuration  |            1 | Separate durable configuration allocation                                                       |
| Bootstrap eligibility      |            2 | Operator predispatch 1 + runtime 1; missing-coordinate inventory 1 completes the partition of 3 |
| Bootstrap pre-marker       |            1 | Missing-coordinate inventory 1; third send unused                                               |
| Postcreation configuration |            1 | Separate durable configuration allocation after independent terminal inspection                 |
| Normal eligibility         |            0 | Full existing three-send exact observer                                                         |
| Any final remote consumer  |            0 | Full existing three-send exact observer                                                         |

A successful exact observer requires version inventory, exact-version metadata
and original-gem download: preserve all three sends and their native equivalent
coordinate/platform, yanked, identity, SHA and original-byte checks. Do not infer
a two-send exact observer from the one-send missing-coordinate case. Share the
bootstrap eligibility counters across operator/runtime; no process-local reset.
The two configuration owners reads never extend or refill an operation partition.
Postcreation inspection cannot change a failed bootstrap's terminal result.

Normal admissions carry independently established ownership/registration facts
only while unchanged-resource conditions and expiry remain satisfied. No normal
owners GET is allocated. Change, contradiction, stale facts or inability to
establish unchanged conditions stops for a separately bounded reviewed inspection;
no hidden fourth observation send or free owner lookup is available.

## Configuration, Inspection and Review Chain

Use canonical JSON, exact closed schemas and SHA-256 identities. The executable
field/enum contracts are in
[`ruby_configuration.py`](../src/three_workflow_delivery_v3/release/ruby_configuration.py);
operation requests/admissions remain in
[`ruby_operation.py`](../src/three_workflow_delivery_v3/release/ruby_operation.py).
Unknown fields or mismatched fixed bindings fail closed.

`workflow-delivery/v3/ruby-bootstrap-configuration-v1` is the protected
first-project carrier. `blocked` must equal its complete disabled form;
`first-project-ready` binds the fixed bootstrap tuple, source-evidence revision,
operation profile, protocol, inspection/expiry, exact GitHub control facts and
native IDs, first-project registration intent and independent attestation digest.
Its lifetime is at most seven days and no later than the configuration plan.
GitHub registration intent is `associated-creation`; RubyGems intent is
`pending-publisher` for `Raffle2282`. Neither asserts existing ownership, name
reservation or normal ready Governance.

After protected target T and required checks exist, produce external
`workflow-delivery/v3/ruby-first-project-inspection-v1` bytes binding destination,
bootstrap slot/generation, T, configuration and control-baseline digests, actual
operator principal, absence kind/verdict, retained evidence-manifest digest and
observation/deadline fields. GitHub uses `github-owner-package-inventory` with
principal `hcoona` / `712433`; RubyGems uses `rubygems-project-owners` and the
unauthenticated public endpoint role. The verdict must be `absent`.

Complete controls and absence within the 30-minute inspection phase.
`observed-at` is completion of that entire inspection; `dispatch-by` is exactly
ten minutes later. Inspection expiry is no later than the earlier of two hours
after observation and configuration expiry. Seal the operation request only
then; its expiry must not exceed inspection expiry, even though the generic
request permits four hours. Do not renew observation timestamps or adopt old
absence evidence.

The independently authored canonical
`workflow-delivery/v3/ruby-operation-admission-review-v1` binds request,
configuration, first-project inspection, caller and protocol digests plus actual
reviewer/author, review time, verdict and governing carrier. Existing admission
hashes those exact review bytes and request; the durable reservation binds
admission. Inspection omits the request digest to avoid a hash cycle. The
configuration validator joins these bytes but does not establish reviewer
provenance: the reviewed sole-operator boundary and governing carrier must do so.
An arbitrary nonempty review or unequal author/reviewer strings is insufficient.

After independent admission, durably reserve the slot before its sole dispatch.
The sole-operator ledger journals each reservation, actual-run join and destination
completion before writing its separate evidence file. Every use verifies the
complete journal and exact file membership/digests. Missing or partial state
stops the campaign without reconstructing a reservation or reviving completion.
Immediately before dispatch, read main once and require main equals T, unchanged
reviewed caller/configuration/protocol and current inspection. Dispatch within
60 seconds of that read and before `dispatch-by`. Missing the window stops the
sealed request without renewal, replacement dispatch or reservation refund.

Runtime retains request, inspection, review, admission and reservation in
immutable current-run evidence, validates exact envelope/slot/generation and
actual run identity before registry access, and joins actual run and independent
admission before publication authority. Enforce inspection, configuration and
request expiry before Approval/Authorization and immediately before action.
GitHub's at-most-two-hour absence interval relies on the accepted sole operator
making no out-of-band package/access changes and the platform conflict boundary;
repository native indexes do not re-prove owner-wide absence. RubyGems adds the
fresh pre-marker owners check. A competing package or changed control fails the
request instead of refreshing it.

After dispatch retain pinned T, protected-main ancestry and relevant-path history
freshness, including touch/revert. Unrelated main advancement remains permitted.
Normal operations consume their separate ready Governance and new current-run
originals; they never consume the bootstrap ownership placeholder.

## RubyGems Account Actions and Postcreation Facts

After concrete protocol/configuration review, use the owner's authenticated
`Raffle2282` session at
`https://rubygems.org/profile/oidc/pending_trusted_publishers`. Create at most one
GitHub pending publisher with gem `hcoona-release-smoke-ruby`, repository owner
`hcoona`, repository `three`, workflow `workflow-delivery-v3-ruby-bootstrap.yml`
and Environment `workflow-delivery-v3-ruby-rubygems`. Leave Workflow Repository
Owner/Name unset: there is no cross-repository reusable workflow. Inspect/reuse
an exact existing entry; an incompatible entry or materially different form
stops. No manual gem upload or deletion/recreation is included.

After bootstrap, independently inspect actual owner `Raffle2282` and conversion
to an existing-gem bootstrap publisher. Then add at most one normal publisher
with the same repository/Environment and workflow
`workflow-delivery-v3-ruby-smoke.yml`, leaving cross-repository fields unset.
Retain the bootstrap registration; do not rewrite it into the normal tuple.
Unavailable owner interaction leaves dependent admission unavailable.

Each registration allows one page inspection before and one after submission;
postbootstrap pending-list/conversion allows two more: **six logical page
inspections and two submissions** maximum. Retain screened tuple/account facts,
never session material. Public owners evidence supplies actual ownership;
account views supply registration/conversion. Matching display text alone does
not replace available stable account identity.

For GitHub postcreation, use one
`GET /user/packages/rubygems/hcoona-release-smoke-ruby` under the admitted owner
credential. Require actual package ID/name/type, owner context, non-null exact
repository association, visibility and version count. Inspect package settings
at most twice to establish owner control and effective Actions access for
`hcoona/three`. Build metadata and association alone do not prove access.
Repeat one selected-package GET and at most two settings inspections before
each normal request. An unmet access condition stops pending a separately
reviewed scoped correction.

Before normal ready Governance and each normal request, perform the allocated
GitHub control inspection and compare relevant accepted settings, not volatile
timestamps or unrelated main SHAs. RubyGems uses its separately allocated
postcreation owners read and converted/normal publisher observations. Actual
unknown, absent or mismatching facts block readiness. A failed bootstrap remains
failed even when separate inspection establishes resource facts.

## Cumulative Configuration Allocation

Each destination has eight nontransferable, single-use control units: initial,
post-configuration, bootstrap predispatch, postbootstrap/normal readiness, and
normal01 through normal04 predispatch. Each permits 32 GitHub reads, yielding
256 per destination. Missing resources may terminate initial subreads early;
unused allowances do not transfer.

| Direct configuration/predispatch reads               | GitHub destination | RubyGems destination |
| ---------------------------------------------------- | -----------------: | -------------------: |
| Eight GitHub control units                           |                256 |                  256 |
| Two owner package inventories                        |                 20 |                    0 |
| Postbootstrap and four normal selected-package GETs  |                  5 |                    0 |
| Immediate main read for each of five operation slots |                  5 |                    5 |
| Preliminary and postcreation RubyGems owners GETs    |                  0 |                    2 |
| Original units subtotal                              |                286 |                  263 |
| One INIT-CA-01 supplemental inspection               |                 42 |                    0 |
| Total                                                |            **328** |              **263** |

Total is **591** direct reads: 589 GitHub plus 2 RubyGems configuration reads.
The additional 42 reads belong only to the once-only INIT-CA-01 unit below;
all original phase allocations remain unchanged.
The three bootstrap-operation owners reads are charged only to their existing
operation partitions. Across the campaign at most five RubyGems owners sends
are allocated: two configuration plus three bootstrap. Normal slots add none.
Possible writes total six GitHub mutation roles and two RubyGems submissions.
There is at most one Environment-bypass UI fallback per control unit (eight per
destination) and two selected-package settings inspections per GitHub
postbootstrap/normal checkpoint (ten total). These logical UI limits do not claim
HTTP asset counts. No browser write beyond the RubyGems submissions is included.

These are prospective maxima, not observed usage or a full campaign wire budget.
Any failure, exhausted cap or incomplete inventory stops its phase. Later
read-only diagnosis or scoped correction requires a concrete finite independent
protocol review under the existing Wave; it cannot refill these allocations.

## INIT-CA-01 Supplemental Predispatch Inspection

The independently disposed [INIT-CA-01 local launch failure][init-ca-stop]
occurred before external state, credential acquisition, slot reservation or
network activity. The configured CA path was an existing symbolic link rejected
by the external caller's regular-file reader. Its existing resolved file contains
the same admitted trust bytes. The failed invocation and its sealed request stay
stopped; no hosted Attempt or successful launch is inferred.

Under the existing scoped-correction route, admit at most **one additional
read-only inspection unit**, named `init-ca-01`, for this exact GitHub bootstrap
correction. It permits the existing 32-read complete control inspection followed
by the existing ten-page authenticated owner-package inventory. These are 42
explicit additional maximum GETs, not unused capacity, repetition or transfer
from an original phase. No supplemental main read, UI fallback, write,
credential upgrade, new account or registry probe is included. The cumulative
configuration/predispatch maximum becomes **591**: GitHub destination 328 and
RubyGems destination 263, comprising 589 GitHub-service reads and two RubyGems
configuration reads. The separate external operation's 328-send maximum is
unchanged and must not be combined with this coincident number.

Retain the original configuration plan, M01 acknowledgement, campaign,
37 completed GETs, three spent writes and all failure/original evidence. The
original completed `bootstrap-controls` and `bootstrap-inventory` units remain
spent, with their old target and observation. Their sealed inspection/request
must never be edited, rebound or renewed. The original unused `bootstrap-main`
read retains its immediate-predispatch role. Do not initialize another operation
campaign or add a bootstrap generation, reservation, dispatch or upload.

Before this unit can execute:

1. Protect-deliver the future budget binding to the existing resolved nonsymlink
   CA file, preserving its trust-content digest, caller, interpreter, access and
   effect limits. Use the exact bound file in process-local `SSL_CERT_FILE`;
   do not modify host trust or relax the caller's reader. Verify the caller's
   actual local file predicates, including regular-file, nonsymlink, link-count
   and size checks, in the preparatory launch inspection.
2. Select the new actual protected target/tree after its required checks and
   bounded native NBGV/reproducibility prerequisites pass. Retain the current
   bootstrap generation only while its campaign remains unreserved. Recompute
   protocol/configuration/envelope identities as needed, preserving historical
   configuration attestation, accepted facts and the original absolute expiry
   `2026-10-08T03:39:53.109710Z`.
3. Independently review the concrete temporary supplemental caller, exact source,
   isolated launch, canonical plan and evidence bindings. The plan fixes the
   incident, target/tree, caller/protocol, original configuration plan and M01
   acknowledgement, stopped request/inspection/review/admission and failure
   hashes, initialized campaign and header-only event history, complete original
   configuration history, accepted control baseline, principal/capability
   review, exact new state location, transport profile and 32/10 allocations.
   No arbitrary caller or directory supplied at execution can authorize itself.

The supplemental caller uses the existing supervised configuration transport and
control/absence classifiers, with identical endpoint, TLS, body, retention and
no-redirect/no-retry rules. It has one explicit initialization and one explicit
collection invocation; neither starts an operation caller or chains publication.
Bind a separate private single-use directory with an independently retained
sibling initialization anchor and append-before-file membership journal. Reserve
the whole unit before its first send and each category/ordinal before transport;
retain safe original responses and successful receipts before the next send.
Recheck the pinned old originals, failure and still-unreserved campaign before
initialization and every send. Missing, aliased, changed or partial state stops;
never recreate an anchor, replay a command or select another directory. Failure
spends this supplemental unit and permits no successor unit. The complete
inspection has a 30-minute outer deadline, capped by original configuration
expiry. It never renews the configuration lifetime.

Require actual protected main equal to the corrected target and independently
accepted unchanged relevant baseline/principal/scopes before owner inventory.
Complete successful inventory and original pagination must establish whole-project
absence. A changed control, matching package, unknown response or exhausted
allowance stops without correction effects. Keep raw private inventories private.
After independent review of complete originals, seal a new target-bound request
using the actual supplemental completion time. Preserve the existing ten-minute
dispatch window, two-hour inspection limit and configuration expiry. The old
request remains stopped; its observation is never substituted.

Independent exact-request admission precedes external initialization/preflight,
the existing campaign's sole bootstrap reservation, original M01 `bootstrap-main`
and one dispatch within 60 seconds. All hosted current-run Qualification,
Approval/Authorization, original-byte/terminal/clean-consumer audits,
postcreation facts and normal-completion gates remain unchanged. RubyGems stays
disabled until independently accepted normal GitHub completion. This one unit
does not create a general recovery or repeated-inspection mechanism.

[init-ca-stop]: https://github.com/hcoona/three/issues/954#issuecomment-5926665416

## Hosted Operation Caller

The separate bootstrap and normal workflows invoke
`three_workflow_delivery_v3.ruby_operation_cli`. The request input contains exactly
`request`, `review`, `admission`, `reservation` and `inspection`; normal inspection
is null. The native request job adds its actual run join and admission time.
The sole operator establishes independent reviewer provenance and separately
joins that same actual run to its durable reservation. A digest or different
reviewer string cannot establish independence.

Provider, Build and qualification precede registry access. Every cross-job edge
uses explicit current-run artifact IDs and fixed role/producer bindings. Each
raw upload is downloaded by its returned ID and checked before downstream use;
missing IDs or required files stop. Authorization and mutation-marker readbacks
precede credential acquisition. Publisher jobs are distinct for each destination:
GitHub has package write without OIDC write, and RubyGems has OIDC write without
package write. The parent writer owns both exchange and upload credentials.
Exact-satisfied normal publication skips both privileged jobs. Strict domain
replay determines the terminal record; a separate unprivileged job downloads
and compares the remote original and runs the clean consumer. Neither record
claims the independent audit's verdict.

Effect jobs explicitly initialize their shared local GitHub spending history.
Each immutable uploaded phase claim initializes its registry history once.
Separate durable membership binds actual run, attempt, job, path and every
ordinal digest. Readers never create missing histories. Missing whole or partial
history, aliases and interrupted writes stop before another send. CLI processes
in one job share the same direct-read history; registry readers cannot reuse an
ordinal. Fixed phase ownership and the run-attempt-one boundary prevent reuse
across jobs/reruns. These mechanisms do not authorize recovery.

The hosted HTTPS supervisor caps the complete request, including DNS, TLS,
headers, body and private pipe reception, at 30 seconds and the phase deadline.
Child and receiver cleanup each have a five-second cap. It performs raw HTTP in
a POSIX child while the parent retains writer credential identity. Redirects,
proxies and retries remain disabled. Registry partitions stay 3/3/20/3/3, with
the RubyGems bootstrap eligibility operator debit included; native OIDC adds at
most one request outside those registry partitions. Jobs are capped at 30 minutes,
outer steps at ten minutes and input checks at one minute. Each phase expires
within 30 minutes and the request's absolute expiry.

Only preparation, selected publisher, finalizer and final consumer jobs can
perform direct GitHub reads, sharing a cap of 128 per job across their CLI stages.
Preparation/consumer read repository, main and collaborator controls; publisher
also reads the exact run, approval history, exact target/Environment deployments
and statuses. Lists allow at most five pages of 100 within the same cap.
Finalizer uses direct controls only for normal zero-action freshness. Artifact
service operations use the pinned maintained actions, without a custom REST
artifact-discovery client.

The complete successful path bounds below count checkout invocations C, raw
uploads U, download invocations D and selected artifact IDs N summed over those
downloads. Every upload has a separate one-ID download/readback. Destination
publisher branches are mutually exclusive; failed paths terminate subsets.

| Path               |   C |   U |   D | Sum N | Incoming IDs by job             |
| ------------------ | --: | --: | --: | ----: | ------------------------------- |
| Bootstrap          |  10 |  23 |  32 |    96 | 0, 1, 2, 3, 4, 6, 7, 12, 18, 20 |
| Normal action      |  10 |  28 |  37 |   123 | 0, 2, 3, 5, 6, 8, 9, 15, 22, 25 |
| Normal zero-action |   9 |  22 |  30 |    87 | 0, 2, 3, 5, 6, 8, 9, 13, 19     |

For the pinned [checkout fetch implementation][ruby-checkout-source],
[raw upload implementation][ruby-upload-source] and
[download implementation][ruby-download-source], download logical RPCs are bounded by
D + 2 Sum N, with at most five attempts per RPC and five stream attempts per
selected ID. Upload uses 2U logical RPCs with at most five attempts each and
2U block/block-list operations with at most four attempts each, for stable
nonempty raw files at most 2 MiB. Upload concurrency is one and its no-progress
timeout is 60 seconds. Checkout permits at most 6C fetch process starts.
These operation classes are not a total wire-request or downloaded-byte bound.
Action service buffers, redirects and retry internals retain their pinned limits;
local payload validation is not a streaming network cap.

Freshness guards are separate from action checkout. Bootstrap has one preparation,
four publisher and one consumer guard, each with one isolated control Git read.
Normal action has the same guard counts, each with two isolated reads for the
operation envelope and Governance. Normal zero-action has one preparation,
one finalizer and one consumer guard, each with two reads. Every isolated read
uses one `ls-remote` and one full fetch plus bounded local validation; each Git
subprocess has a 120-second timeout. Full relevant history includes touch/revert;
unrelated main advancement is permitted after dispatch.

## External Operation Caller

The sole operator uses a separately retained, independently reviewed caller with
explicit `initialize`, `preflight`, `reserve`, `dispatch`, `discover`,
`prepare-review`, `approve`, `terminal` and `complete` commands. No command chains
mutation stages or supplies its own independent admission. The exact source,
interpreter, isolated launch, TLS trust, clean protected target/tree, protocol,
configuration plan, request, admission and finite budget must be bound before
execution. Imported control modules resolve under that exact target checkout.
Development overlays are not launch sources.

The request's `caller-digest` binds the external source and
`github-budget-digest` binds its complete canonical launch manifest. Schema
`workflow-delivery/v3/ruby-external-budget-v1` contains only `schema`,
`allocations`, `root`, `directory`, `operation-ledger`,
`configuration-plan-digest`, `interpreter-digest`, `independent-reviewer`,
`required-checks`, `launch`, `storage-origin-policy`, `poll-spacing-seconds`,
`stage-seconds`, `tls-cafile` and `tls-cafile-digest`. Admission fixes absolute
source/private-state paths, exact required check names and app IDs, and the
`[interpreter, "-I", caller]` launch. Existing verified system trust is selected
process-locally; no certificate bypass or host trust change is included.

Initialize private state once, retaining an independent sibling initialization
anchor and append-before-file membership journal. Verify exact immutable input,
stage and send membership before each command. A missing directory/member,
alias, torn append, incomplete stage or repeated command stops without
reconstruction, refill or resume. The campaign operation ledger reserves the
slot before the existing configuration caller's unique predispatch main read.
Dispatch reuses that completed evidence and receipt; it performs no extra main
read and must meet the existing 60-second and bootstrap dispatch-by limits.
The RubyGems caller requires the campaign's audited GitHub completion.

Preflight verifies the actual admitted owner and complete required target checks.
Dispatch sends once; a definitive HTTP 204 response does not itself identify a
run. Discovery requires exact workflow, event, target, main, actor and native
attempt-one identity with an unambiguous inventory. Download the explicit-ID
current-run operation original and join the complete request, admission,
inspection and reservation before recording the actual native run locally.

Preparation retains the qualified original and complete approval predecessors
for separate review. The reviewer examines actual originals, authority,
reservation, expiry and predecessor joins before authoring the approval carrier.
Approval rechecks the actual run and exact pending Environment and sends once;
only a definitive HTTP 200 deployment response matching target and Environment
is accepted. Terminal extraction retains actual run status and available exact
originals, including failed or incomplete evidence. Local normal completion
requires a separate accepted terminal audit and strict domain/original/consumer
replay; bootstrap never completes a destination. Native job success or the
caller's structural checks cannot supply an independent audit.

Approval and terminal review carriers use the respective schemas
`workflow-delivery/v3/ruby-external-approval-review-v1` and
`workflow-delivery/v3/ruby-external-terminal-review-v1`, with exactly `schema`,
`request-digest`, `run-id`, `target`, `manifest-digest`, `reviewer`, `author`,
`reviewed-at`, `carrier` and `verdict`. Verdict is `accepted`, the reviewer is
independently admitted and differs from the author, and the governing carrier is
an Issue #954 comment. Review follows actual manifest preparation and precedes
request expiry. The sole-operator procedure must establish genuine authorship;
unequal strings and supplied hashes do not do so.

### Direct External Calls

Let R be `/repos/hcoona/three`, W the bound workflow, T the exact target, I the
verified run and A an explicit current-run artifact ID. Caps are per request,
nontransferable and include failed sends.

| Category            | Method and endpoint                                                                                 |     Cap |
| ------------------- | --------------------------------------------------------------------------------------------------- | ------: |
| Principal           | GET `/user`                                                                                         |       1 |
| Required checks     | GET `R/commits/T/check-runs?filter=latest&per_page=100&page=n`                                      | 5 pages |
| Dispatch            | POST `R/actions/workflows/W/dispatches`                                                             |       1 |
| Run discovery       | GET `R/actions/workflows/W/runs?event=workflow_dispatch&branch=main&head_sha=T&per_page=100&page=1` |      10 |
| Run status          | GET `R/actions/runs/I`                                                                              |     152 |
| Pending Environment | GET `R/actions/runs/I/pending_deployments`                                                          |      32 |
| Artifact inventory  | GET `R/actions/runs/I/artifacts?per_page=100&page=1`                                                |      42 |
| Artifact metadata   | GET `R/actions/artifacts/A`                                                                         |      28 |
| Raw artifact ticket | GET `R/actions/artifacts/A/zip`                                                                     |      28 |
| Artifact body       | GET one supported signed storage URL without credentials                                            |      28 |
| Approval            | POST `R/actions/runs/I/pending_deployments`                                                         |       1 |

The ceiling is 300 GitHub API sends (298 reads and two effects) plus 28 storage
GETs, totaling 328 direct sends per request. Discovery permits ten observations,
preparation thirty and terminal 120, at least ten seconds apart. Each stage has
an original deadline of at most 1,800 seconds, capped by request expiry. Run lists require complete inventories of at most 100 and artifact lists at
most 28; unknown pagination stops. Unused category headroom cannot transfer. The 591 configuration
reads (including the one INIT-CA-01 unit), ten predispatch main reads within
that allocation, hosted action bounds,
per-job 128 REST reads and registry partitions remain separate and are not
counted again here.

Hosted raw uploads use `archive:false`: despite the REST route name `/zip`, the
caller accepts exact raw bytes, without archive inspection or transformation.
Accept direct HTTP 200 or one HTTP 302 to an HTTPS Azure Blob host matching
`[a-z0-9]+.blob.core.windows.net`, with no userinfo, nondefault port or fragment.
The storage request receives no owner Authorization; a second redirect or unknown
origin stops. Require immutable service SHA-256, size, current run/head/main,
role/name, unexpired metadata, unique ID and closed basename to match. Each
original is acquired once; no download-all exists. Changed service behavior
requires revalidation, not a broader redirect policy.

Each direct HTTPS send uses verified TLS and a complete 30-second supervisor,
including DNS, headers, body and private pipe reception, capped by the current
stage/request deadline. Child and receiver cleanup each have a five-second cap.
Bodies are limited to 2 MiB and retained headers to 64 KiB. No proxy, retry or TLS
downgrade is supported. Persist intent before send and retain original safe body
bytes, selected headers, status, times and digests privately. Signed storage
locations remain in memory; retain only their digests. Reflected credentials or
signed URLs stop before persistence. Private originals require screening before
public disclosure. Failures preserve spending and evidence and never authorize a
second effect.

## Remaining Execution Closure

Before configuration, independently review the concrete composition of the
collection/counter schemas, durable pre-send spending and bounded transport/caller,
actual supported principal/access, full raw evidence provenance and exact
before/after plan. Before any operation, independently admit the exact external
sole-operator source, launch and fixed budget manifest described above. Join its
actual-run reservation to the hosted evidence. Count the 591 configuration reads
(including the one INIT-CA-01 unit) and five predispatch main reads per
destination only once. Hosted cross-process
spending retains the 32-send registry envelope; operator/API/action classes remain
separate bounds, not a universal network counter.

Use maintained action bounds with finite step/job deadlines; transfer counts or
artifact size ceilings are not total wire-request/download-byte guarantees.
Complete protected implementation, independent source/test/record/evidence review,
exact request admission and existing Approval/Authorization before effects.
Stop each destination at first independently audited normal completion; failures
remain failed and operation ceilings never authorize retries.

## Source Contracts and Rechecks

The [Ruby evidence record](./research/ruby-smoke-evidence.md) retains the broader
platform basis and its limits. This protocol additionally uses:

- [GitHub REST OpenAPI](https://github.com/github/rest-api-description/blob/2b78fb0c53617f188e45979ecefd83b521c5428a/descriptions/api.github.com/api.github.com.json)
  for the exact control, Environment and authenticated-owner package routes.
- [RubyGems owners API guide](https://github.com/rubygems/guides/blob/299c071b2e7aa1612d94f4bb17018acc179fcd67/rubygems-org-api.md#owner-methods),
  [owners controller](https://github.com/rubygems/rubygems.org/blob/36e040d0c8a3282959bff078894a5d458f991472/app/controllers/api/v1/owners_controller.rb#L3),
  [direct project lookup](https://github.com/rubygems/rubygems.org/blob/36e040d0c8a3282959bff078894a5d458f991472/app/controllers/application_controller.rb#L125)
  and [not-found translation](https://github.com/rubygems/rubygems.org/blob/36e040d0c8a3282959bff078894a5d458f991472/config/locales/en.yml#L49).
- [Official Trusted Publishing guide](https://github.com/rubygems/guides/blob/299c071b2e7aa1612d94f4bb17018acc179fcd67/trusted-publishing.md)
  for pending new-gem registration, conversion and same-repository tuple fields.
- [GitHub private-resource 404 guidance](https://github.com/github/docs/blob/10844e10c034b5e5d9b62793c3af9feb4b91de07/content/rest/using-the-rest-api/troubleshooting-the-rest-api.md#404-not-found-for-an-existing-resource)
  and [package permissions](https://github.com/github/docs/blob/10844e10c034b5e5d9b62793c3af9feb4b91de07/content/packages/learn-github-packages/about-permissions-for-github-packages.md).

At concrete bootstrap admission, and on unexpected endpoint/UI behavior or a
material source/profile change, the V3 maintainer and independent admission
reviewer recheck the relied-on deployed contract against these source findings.
Unknown behavior stops admission pending reviewed reconciliation. Final concrete
operator review is the fallback review event; immutable pins do not by themselves
establish current live configuration or deployed implementation identity.

[ruby-checkout-source]: https://github.com/actions/checkout/blob/3d3c42e5aac5ba805825da76410c181273ba90b1/src/git-source-provider.ts#L180
[ruby-upload-source]: https://github.com/actions/upload-artifact/blob/043fb46d1a93c77aae656e7c1c64a875d1fc6a0a/dist/upload/index.js#L124229
[ruby-download-source]: https://github.com/actions/download-artifact/blob/3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c/dist/index.js#L126390
