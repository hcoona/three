# Workflow Release Low-Level Design Rebaseline Recommendation

## Recommendation

Use this page as a process record for major middle-layer release-topology
changes. When the middle layer changes the topology contract, do not restart the
workflow-release low-level design from scratch. Keep the requirements and
high-level architecture fixed, then run a focused low-level rebaseline and
readiness check against the new topology constraints.

The current design corpus has already incorporated topology-partitioned routing
for first-class OIDC topology and first-delivery live PyPI. This record captures
why that low-level rebaseline was necessary, how it was bounded, and how to
repeat or verify the same kind of pass after future middle-layer changes.

The expected outcome of such a pass is focused readiness evidence: identify any
places where the low-level design still carries superseded topology assumptions,
rewrite only those sections, and confirm that the implementation handoff is
coherent for the accepted publish topology.

## Why This Rebaseline Was Required

The middle-layer change is not cosmetic. It changed the release topology model
from a reusable-only publish-unit assumption to first-class OIDC publish topology:
entry-workflow-bound, caller-workflow-bound, reusable-workflow-bound, and
GitHub-token publish paths can have different workflow identities and different
permission placement.

That class of change is large enough to affect low-level workflow layout,
scheduling, permissions, request and receipt handoffs, trusted-publisher setup,
and readiness criteria. A low-level design that is otherwise detailed must be
checked for stale assumptions such as routing every live external publish through
one reusable workflow like `.github/workflows/release-publish-node.yml`.

Status: the current corpus has been updated for topology-partitioned publish
routing, including first-class OIDC topology and first-delivery live PyPI through
an entry-workflow-bound path. This page is not an assertion that the current
low-level design remains known-broken; it is the recommendation and verification
record for the rebaseline that addressed that risk.

## Assumptions the Rebaseline Must Check

- Live PyPI publication is no longer deferred beyond first delivery.
- External OIDC publishing is not one uniform reusable workflow behavior.
- PyPI is treated as entry-workflow-bound for current scope unless a successor
  design proves another supported topology.
- npmjs can remain caller-workflow-bound, with OIDC capability passed along the
  `workflow_call` path.
- RubyGems.org can use a reusable-workflow-bound topology where registry support
  is explicitly configured.
- GitHub Release and GitHub Packages remain GitHub-token publishing paths rather
  than external OIDC trusted-publisher paths.
- The registry-trusted workflow filename is a release contract, not an internal
  refactoring detail.

## Low-Level Sections to Review and Rebase

For a future rebaseline or verification pass, review the low-level design only
where comparable topology changes can invalidate implementation guidance:

- workflow file layout and stable workflow filename commitments;
- publish-node scheduling, especially entry-hosted publish jobs for
  entry-workflow-bound targets;
- GitHub Actions permissions, especially `id-token: write` placement;
- entry workflow and reusable workflow request handoffs;
- `publish-request.json`, `publish-result.json`, skip receipt, and immutable
  proof expectations for entry-hosted versus reusable publish nodes;
- registry adapter obligations for PyPI, npmjs, NuGet.org, RubyGems.org, GitHub
  Release, and GitHub Packages;
- external setup instructions for trusted-publisher configuration;
- dry-run and validation-build behavior where live-publish topology is observed
  but not executed;
- acceptance traceability for first-delivery live PyPI and mixed topology
  publish graphs.

Do not use such a pass to redesign descriptor schema, plan shape, architecture,
or requirements unless the review finds a contradiction that cannot be resolved
at the low-level layer.

## Proposed Waterfall Process

1. Keep requirements and high-level architecture fixed.
2. Treat the updated middle-layer topology model as the new baseline.
3. Run a focused low-level review against the changed topology constraints.
4. Mark each affected low-level section as still valid, needing targeted rewrite,
   or blocked by an upstream decision conflict.
5. Rewrite only targeted sections that retain superseded topology assumptions.
6. Run a readiness review that checks the low-level design can guide
   implementation of the accepted live-publish topology without hidden topology
   migration work.

For the current corpus, this process should be read as a verification checklist:
confirm that the low-level design now names stable trusted-publisher workflows,
schedules entry-workflow-bound PyPI publication in the entry workflow, preserves
caller-workflow-bound and reusable-workflow-bound OIDC paths where applicable,
and keeps GitHub-token publication distinct from external OIDC paths.

## Decision Boundary

The rebaseline pass may clarify or rewrite low-level implementation handoff text,
but it must not silently change scope. If the review discovers that a new
middle-layer topology requires overturning prior requirements, architecture,
descriptor, plan-shape, or workflow-boundary decisions, escalate to Human-in-Loop
review before changing those decisions.
