# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Correct Node Provider and Repository Model Test Responsibilities

- **Work carrier:** [Issue #767](https://github.com/hcoona/three/issues/767).
- **Authorized outcome:** Correct the test abstraction and ownership between
  Node Provider facts, Fact Bundle admission and Repository Model compilation.
  Test malformed input at the boundary that rejects it; compiler scenarios must
  reach the compiler. Replace heuristic discovery of a fixed same-revision
  API with direct typed fixtures. Reduction is a possible result, not a quota.
- **Accepted basis:** Reconcile current source with the independently reviewed
  [responsibility assessment](https://github.com/hcoona/three/pull/766#issuecomment-5745485171)
  and Issue proposal. Follow the current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [repository model design](../src/public/lib/three-workflow-delivery-v3/docs/repository-model-release-unit-mld.md),
  [Shared Foundation design](../src/public/lib/three-workflow-delivery-v3/docs/shared-foundation-mld.md),
  [npm design](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-npm-lld.md)
  and [shared testing principles](engineering/engineering-principles.md#testing).
  Refresh analysis if a relied-on source, contract or owner changes.
- **Bounded implementation:** Modify only these paths under
  `src/public/lib/three-workflow-delivery-v3/tests/`:
    - `contracts/test_commit3_contract_boundaries.py`: replace the Fact Bundle
      API-discovery, argument-synthesis and result-extraction helpers and their
      callers with explicit current Node APIs. Assert independent literal
      fields, values and canonical identities; retire instance identity only
      when no current contract requires it.
    - `repository/test_compiler.py`: trace and relocate the malformed-NBGV and
      checkout-primitive families to their actual producer/admission owner.
      Retain compiler-specific invalid-wrapper, wrong-context, complete and
      incomplete model, and exact-target scenarios with inputs that reach the
      compiler. Make real Git setup explicit for scenarios that need it.
    - `repository/test_node_provider.py`: adjust related direct Provider
      contracts only as needed to establish the owning tests. Preserve existing
      real Git/NBGV acquisition tests and their assumptions.
    - `contracts/test_node_provider_admission.py`: optionally add a focused
      in-memory owner for the relocated producer/admission contracts.
      Permit necessary local test fixtures, meaningful owning cases and removal
      of demonstrably unused local setup/imports within those files. Do not move
      rows into hidden replay loops, create a generic test framework, share mutable
      Git state or broaden the change to other test families.
- **Preserved obligations:** Retain exact schema, canonical bytes/digests,
  current context, purpose, target, manifest and transport binding; supported
  model closure/readiness and native projections; frozen/slotted records;
  no Provider re-execution; and real Git/NBGV integration assumptions. Keep
  production validation and invalid-wrapper rejection unchanged. Fact Bundle
  wrappers are publicly constructible; their names are not proof of admission.
  Classify assertions by required behavior and credible failure, not by their
  uniqueness or directory. Preserve required assertions atomically when moving
  ownership; justify any retirement against current contracts and sufficient
  surviving evidence. An unproven ownership transfer retains the original test
  until corrected analysis and validation resolve it.
- **Validation and review:** Record each affected family's rule, actual owner,
  input boundary and asserted failure. Separate additions, moves, renames and
  removals against the accepted baseline. Run complete affected modules and
  any discovered fixture consumers, then the normal full v3/repository gates
  and hooks. Preserve exact candidate, patches, results and resource evidence.
  Formal independent domain and evidence reviews follow green validation;
  independently adjudicate material findings and refresh affected validation
  and reviews after correction. Follow the handoff's contraction protocol and
  [agent-execution guidance](engineering/agent-execution.md). Static setup
  changes and case counts establish no measured runtime or peak-inode saving.
- **Scope and effects:** Permit ordinary validation/dependency operations,
  task-owned evidence and normal Git/Issue/PR delivery. Preserve #721 resource
  ownership, bounded child lifetimes and failed/interrupted evidence; stop on
  exhaustion or unreaped task sessions and resolve ownership before continuing.
  Production, workflows/HK, dependencies, resource limits, schemas, authorities
  and historical/native evidence remain unchanged. Production extraction,
  admitted-Snapshot API changes, CI derivation consolidation, other test
  cleanup, legacy acceptance retirement and other v3 development are excluded.
  No native probe, dispatch/rerun, external publication, administrative or
  authentication/access change, host-wide cleanup or unrelated process
  termination is authorized. Existing npm/NuGet gates and spent-operation
  boundaries remain in force.
- **Completion:** Retain the accepted responsibility changes, evidence limits,
  validation, reviews and dispositions in the Issue and delivery carriers.
  End this finite grant through reviewed Wave deletion or replacement after
  acceptance. Further abstraction work requires its own accepted scope.
