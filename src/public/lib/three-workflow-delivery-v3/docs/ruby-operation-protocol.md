# Ruby Configuration and Operation Protocol

## Scope and Delivery Boundary

This record implements the configuration and first-project admission portion of
[the Ruby LLD](./hcoona-release-smoke-ruby-lld.md#governance-and-first-project-protocol)
under [Issue #954](https://github.com/hcoona/three/issues/954) and the accepted
[Delivery Wave](../../../../../docs/delivery-wave.md). The V3 maintainer maintains
it for configuration-validator authors, the forthcoming operator and independent
admission reviewers. Its distinct consumer is the concrete endpoint, phase and
request-binding contract; the LLD owns architecture and the Wave owns authority.

This is a **partial operation protocol**. The current source delivery contains
candidate [first-project validators](../src/three_workflow_delivery_v3/release/ruby_configuration.py)
and a separate [RubyGems project observer](../src/three_workflow_delivery_v3/adapters/ruby_project.py).
Their closed data contracts do not establish live configuration, source provenance,
independent approval or an executable caller. Fixed GitHub control/package
collectors, a supervised configuration transport and durable phase/send counters
are implemented. The reviewed configuration operator, bootstrap/normal runtime
assembly and final coupled budgets remain pending. All ten operation slots remain null; both normal Governance
sources remain blocked. No configuration, account, registry, OIDC or dispatch
effect follows from this partial delivery alone.

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
Interrupted, failed or altered evidence cannot resume a phase or fund another.
The [transport](../src/three_workflow_delivery_v3/platform/ruby_configuration_http.py)
requires reserve/retain callbacks; the forthcoming operator must retain original
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
   expected HTTP 201. A reviewed correction to an existing marker instead uses
   `PATCH E/variables/WDV3_APPROVAL_ENVIRONMENT_MARKER`, body
   `{"value":"<environment>/v1"}`, expected HTTP 204. Create and update share
   this one role.

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
the request. The owner credential stays local. The future collector must prove
raw response/principal/pagination provenance: the current screened-fact validators
do not implement this observer or establish inventory completeness.

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
| Total                                                |            **286** |              **263** |

Total is **549** direct reads: 547 GitHub plus 2 RubyGems configuration reads.
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

## Remaining Execution Closure

Before configuration, complete and independently review the concrete composition
of collection/counter schemas, durable pre-send spending and bounded transport/caller,
actual supported principal/access, full raw evidence provenance and concrete
before/after plan. Before any operation, close the final runtime DAG and trusted
caller: per-job controls, actual-run/check/artifact discovery, checkout count C,
raw uploads U, downloads D and each selected-artifact count N, including upload
readback and inspection/review edges. Count the 549 reads once and the five
predispatch main reads per destination only once. Mechanically join cross-process
bootstrap spending without resetting the 32-send envelope.

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
