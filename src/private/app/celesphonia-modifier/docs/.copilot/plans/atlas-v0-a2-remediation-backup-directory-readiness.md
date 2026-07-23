# Atlas V0 A2 Remediation Backup Directory Readiness

**Lifecycle:** Active subordinate correction; planning-only before verified shared `R12C`

**Status:** Source correction blocked

**Increment:** A2R12C - Remediation Backup Directory Readiness

**Base:** `9de5a2f666c10446cbd5b7a8f256f4caf898fa87`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-intake-safety-plan.md`; and
- `atlas-v0-a2-baseline-manifest-row-remediation.md`.

**Planned review record:**
`../reviews/atlas-v0-a2-remediation-backup-directory-readiness-plan-review.md`

## 1. Safe conclusion

A repository-safe readiness check showed that the plan incorrectly required a not-yet-created
output directory to preexist. This reveals no qualification outcome, path, inventory value,
cardinality, eligibility result, hash, alias, or row content.

The refusal is a plan/code precondition defect. It does not authorize inferring eligibility or
changing private data.

## 2. Exact correction

Qualification remains read-only. For the exact derived `intake/inventory-backups` path it:

1. calls `AtlasDiscovery.ValidateCreateNewOutputDirectory` with the selected workspace root;
2. accepts either an absent leaf or an existing ordinary non-reparse directory;
3. treats the exact transient backup as absent when the directory is absent;
4. still requires the replacement staging leaf to be absent; and
5. performs no directory or file write.

Only approved remediation may create the directory. Before any operational file write it:

1. repeats `ValidateCreateNewOutputDirectory`;
2. calls `Directory.CreateDirectory` only for that exact validated path;
3. immediately validates the resulting directory through
   `AtlasDiscovery.ValidateExistingOrdinaryDirectory`, containment, and fixed-drive checks; and
4. only then validates and uses the exact transient-backup leaf.

An interruption after directory creation is restart-safe: the empty or helper-populated ordinary
directory is accepted by the same validation. Directory creation changes no inventory document and
creates no retained artifact. All original A2R12 backup, cleanup, publication, privacy, approval,
and result-neutral signal rules remain unchanged.

## 3. Gates and acceptance

Plan candidate `P12C` is the direct child of `R12` and may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-baseline-manifest-row-remediation.md
    atlas-v0-a2-intake-safety-plan.md
    atlas-v0-a2-remediation-backup-directory-readiness.md
```

Review record `R12C` is the direct child of final `P12C` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-remediation-backup-directory-readiness-plan-review.md
```

Only after verified shared `R12C` may the existing session source be corrected. Before another
private qualification:

1. synthetic tests prove absent, existing ordinary, file, and reparse directory states;
2. qualification proves no write when the directory is absent;
3. remediation tests prove approval-before-create and create-then-revalidate ordering, missing or
   invalid approval performs no directory write, and restart after creation;
4. all existing A2R10, A2R11, and A2R12 tests pass;
5. exact source builds warning-free and receives independent `No findings`; and
6. repository `HEAD`, upstream, and clean state equal `R12C`.

The correction grants no approval or remediation authority. That authority still depends on a
future eligible protected qualification and explicit project-leader approval under A2R12.
