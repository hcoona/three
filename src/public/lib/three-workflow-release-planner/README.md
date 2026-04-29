# three-workflow-release-planner

Planner core for the Three workflow release design. It consumes the validated
authoring model and planner request contract, resolves planner-owned identities,
queries planner-owned version authorities, computes final PyPI filenames through
the checked-in build backend, and emits `release-plan.json`,
`execution-sets.json`, or fail-closed planner diagnostics. It does not execute
publish side effects.
