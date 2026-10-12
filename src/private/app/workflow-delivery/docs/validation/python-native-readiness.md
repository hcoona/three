# Retired Python Native Tooling Boundary

The V3 maintainer maintains this narrow interface for auditors of retained
failed evidence after retirement of the native producer. The
[current requirements](../requirements.md#python-smoke-slice) trust platform
behavior; this is no longer an operator readiness checklist. The
[retired protocol](python-native-acceptance.md) identifies the failed run and
links its exact historical contract. It supplies no new execution permission.

The current tree contains no `three_workflow_delivery_v3.acceptance.python_native`
module, native request-slot file, acceptance workflow or bootstrap caller.
The [historical-reader map](historical-readers.md#producer-qualified-readers)
routes the actual producer's code, catalog, request and setup source separately
from the later interpretation protocol and last-source navigation. The
[current handoff](../agent-handoff.md) routes ordinary Release work independently
of this historical interface; it requires no new duplicate/race generation.

Historical `replay`, `archive` and `digest` contracts, including the static catalog,
are recoverable at their exact source revisions with the original evidence. Use
the map's producer-qualified closure when interpreting historical archives;
the current application supplies no replay or compatibility service.
Historical replay validates supplied bytes and lineage;
it cannot recreate missing captures, prove provenance by itself or upgrade the
failed verdict. Existing replay may run local clean consumers with locked public
build prerequisites, so it is not a zero-effect operation; use it only within
separately applicable work authorization. Do not re-upload or re-observe the
registry to complete historical evidence.

The [historical v2 migration](../hcoona-release-smoke-python-lld.md#python-governance-v2-migration)
explains the former admission change without fabricating suite or generation
fields. Its former readers and bootstrap helpers remain recoverable in pinned
Git source, not as current runtime or resource-establishment callers.
The configured native publisher remains an external resource until a separately
authorized configuration change removes it; producer retirement does not
claim its deletion. Current actual publication and clean-consumer evidence
requirements are in the [requirements](../requirements.md#python-smoke-slice)
and [concrete Python Release integration](../migration-strategy.md#concrete-python-release-integration).
