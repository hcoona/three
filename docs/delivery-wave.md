# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Design the Ruby V3 smoke and its two-destination journey

- **Issue:** [#954](https://github.com/hcoona/three/issues/954).
- **Accepted inputs:** the owner's selection of a Ruby smoke library, reusing
  one if present or otherwise creating a minimal one in a later implementation;
  GitHub Packages first when it supports Ruby gems, then RubyGems.org; NBGV
  integration and reproducible builds as prerequisites; the completed Python
  two-destination journey and NuGet reproducibility work; and current V3 and
  repository authorities.
- **Advancement and outcome:** complete public-source research, requirements,
  applicable HLD/MLD extensions and a concise Ruby LLD covering the smoke
  product, exact-target version integration, original-package reproducibility,
  destination-specific delivery/recovery and clean-consumer acceptance. Reconcile
  migration and handoff records, independently review the complete design and
  evidence, and stop before implementation. Record unresolved feasibility or
  owner decisions without claiming runtime support.
- **Effects and delivery:** permit public-source research, read-only repository
  and GitHub inspection, local documentation validation and tool preparation,
  independent delegated review, ordinary Issue/PR/check/comment operations,
  protected delivery and ordinary CI. Apply record-system, research-evidence
  and V3 domain review, including open-code-review-delegate before each merge.
- **Exclusions:** no product/runtime implementation or smoke-package creation;
  local or hosted behavioral experiments; package registration, registry probes,
  native acceptance, release dispatch, credentials/access/publisher changes,
  OIDC or publication. Completed npm, NuGet and Python operations remain closed.
  Implementation and external operations require later owner authorization,
  accepted Wave scope and their applicable domain gates.
