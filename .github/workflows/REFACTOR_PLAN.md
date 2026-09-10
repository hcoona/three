# Legacy Release Orchestrator Compatibility Notes

> **Archived and superseded:** This pre-v3 design is retained only for
> historical context. The legacy `buddy.yml` and `release-buddy.yml` routes are
> retired. Do not use this document to recreate either route; current Buddy
> delivery is owned by Workflow Delivery v3.

This filename remains a reader interface embedded in the retained validator
and workflow diagnostics. It preserves channel-migration guidance and the
context of their Step 2 through Step 6 comments. It is not an executable
refactoring plan. Current delivery work starts with the
[v3 handoff](../../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
and its [legacy boundary evidence](../../src/public/lib/three-workflow-delivery-v3/docs/research/legacy-workflow-boundary.md).
Generic work procedure belongs to [CONTRIBUTING.md](../../CONTRIBUTING.md).

The [original plan](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/.github/workflows/REFACTOR_PLAN.md)
records the complete proposal. Read-only reconciliation at that revision
found the additive `resolve-hub-context` job still present in
[release-orchestrate.yml](release-orchestrate.yml), with no
`release-orchestrate-<lang>.yml` spokes. The old future-tense steps are not
claims of implemented behavior or permission to create Environments.

## 2. Target Architecture (Hub-and-Spoke)

The retained proposal separated policy/context resolution from hypothetical
language-specific spokes. The existing hub context does not by itself prove
those spokes were delivered. Direct callers of a reusable workflow would not
inherit guards from a different caller's DAG.

### 2.2 The Spokes (`release-orchestrate-<lang>.yml`)

The historical forwarding contract named `package_dir`, `target_environment`,
`channel_profile` (`official`, `buddy`, `custom`), and `publish_mode`
(`publish`, `build-only`), plus language-specific inputs. `project_variant`
was hub-internal; `project_kind` came from `needs.resolve.outputs`.
Node/npm registry selection was a separate pair of booleans,
`publish_node_gpr` and `publish_node_npmjs`, rather than extra publish modes.

`publish_mode` and `channel_profile` describe routing intent, not approval or
attestation authority. The existing workflow comments require a separate
approval gate and a publish job with the registry's fixed OIDC Environment;
putting a dynamic routing Environment on an OIDC job changes the token claim.
This is retained legacy design context, not the v3 Environment model.

## 3. Implementation Steps

These headings preserve the names cited by the retained code. They describe
compatibility constraints and unimplemented proposal context, not a backlog
or standing work grant.

### Step 2: Extract central policy jobs

The validator's changed input contract remains implemented in
[release_orchestrate_policy_validate_inputs.sh](../../eng/scripts/release_orchestrate_policy_validate_inputs.sh).
Its self-test diagnostics also cite this section.

#### Breaking changes in Step 2

The channel-name pattern is `^[a-z0-9]([a-z0-9]|[_-][a-z0-9])*$`, replacing
`^[a-z0-9_-]+$`. Consecutive or mixed separators and leading/trailing
separators are rejected in allowlist entries and custom channel inputs.
Existing migration examples are:

- `my--channel` → `my-channel`;
- `my__channel` → `my_channel` or `my-channel`;
- `-beta` → `beta`;
- `alpha-` or `alpha_` → `alpha`;
- `a_-b` → `a-b`.

These are manual rename examples, not automatic transformations. The hub's
defensive sanitization collapses consecutive dashes but not underscores;
the validator rejects those invalid production inputs before routing. This
avoids allowlist names collapsing onto the same Environment name.

`x-official` and `x-buddy` are reserved internal remapping slugs, rejected
both as direct channel inputs and as allowlist entries. Use another valid
slug, such as `ext-off` or `ext-official`. Empty input, whitespace, and
uppercase have separate early rejection diagnostics. The guards improve the
error and exit point without making a formerly invalid input valid.

### Step 3: Create Python spoke workflow

The original proposal required pre-existing reviewer-protected approval
Environments before deployment: GitHub can auto-create an absent Environment
without the intended reviewers. No migration note proves those resources
exist or authorizes their creation. The legacy Buddy route remains retired.
The `project_kind` mapping and publish-mode derivation would also need to
agree before any separately authorized spoke implementation.

### Step 4: Create Node/WXT spoke workflow

The selected historical option passed `publish_node_gpr` and
`publish_node_npmjs` separately because a binary `publish_mode` cannot
distinguish GPR-only, npmjs-only, and both. Encoding the registry set as new
publish modes was rejected because all forwarding consumers would change.
No Node/WXT spoke is claimed delivered by this decision.

### Step 5: Create Ruby spoke workflow

The old plan proposed moving gem build and registry operations into a spoke.
It did not establish a new destination contract or authorize publication.
The retained implementation and v3 domain boundary must be consulted before
any mechanism extraction.

### Step 6: Wire hub static routing

The retained proposal sourced `project_kind` from `needs.resolve.outputs`,
kept `project_variant` internal, and split Node/npm from WXT using canonical
`is_wxt` values `true` or `false`. A routing job consuming release notes
would need `prepare-release-notes` directly in its own `needs`; that job is
not part of the hub-context dependency chain. The comment proposing script
extraction describes unimplemented work, not a requirement to execute it.
