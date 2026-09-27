# Retired Python Native Tooling Boundary

The V3 maintainer maintains this narrow interface for auditors of retained
failed evidence and implementers retiring the disabled native producer. The
[current requirements](../requirements.md#python-smoke-slice) trust platform
behavior; this is no longer an operator readiness checklist. The
[retired protocol](python-native-acceptance.md) identifies the failed run and
links its exact historical contract. It supplies no new execution permission.

`three_workflow_delivery_v3.acceptance.python_native` and
`.github/workflows/workflow-delivery-v3-native-python-acceptance.yml` remain in
the current implementation pending the separately authorized retirement.
Both slots in `.github/workflow-delivery/native/python-requests.json` are null;
both normal destinations remain blocked. Availability of prepare/probe/audit
or fixture commands is not authority to use them. No fresh fixtures, generation
or duplicate/race run is needed for the new admission.

Historical `replay`, `archive` and `digest` consumers retain their exact-revision
contracts and original evidence. Replay validates supplied bytes and lineage;
it cannot recreate missing captures, prove provenance by itself or upgrade the
failed verdict. Existing replay may run local clean consumers with locked public
build prerequisites, so it is not a zero-effect operation; use it only within
separately applicable work authorization. Do not re-upload or re-observe the
registry to complete historical evidence.

The [v2 migration](../hcoona-release-smoke-python-lld.md#python-governance-v2-migration)
moves normal admission off `native-acceptance` without fabricating suite or
generation fields. Its later disabled implementation must retire the Python
native producer route and keep only justified historical reader consumers.
The configured native publisher remains an external resource until a separately
authorized configuration change removes it; documentary retirement does not
claim its deletion. Current actual publication and clean-consumer evidence
requirements are in the [LLD](../hcoona-release-smoke-python-lld.md#evidence-and-delivery-gates).
