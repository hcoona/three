# Retired Python Native Tooling Boundary

The V3 maintainer maintains this narrow interface for auditors of retained
failed evidence after retirement of the native producer. The
[current requirements](../requirements.md#python-smoke-slice) trust platform
behavior; this is no longer an operator readiness checklist. The
[retired protocol](python-native-acceptance.md) identifies the failed run and
links its exact historical contract. It supplies no new execution permission.

`three_workflow_delivery_v3.acceptance.python_native` retains only local
`replay`, `archive` and `digest` entry points. The hosted workflow
`workflow-delivery-v3-native-python-acceptance.yml` and its prepare/probe/audit
commands, fixture producers and executable duplicate/race suite are removed.
Both slots in `.github/workflow-delivery/native/python-requests.json` remain null.
The [handoff](../agent-handoff.md#starting-a-new-session) routes current normal
admission independently of this retired interface. No fresh fixtures,
generation or duplicate/race run is needed for admission.

Historical `replay`, `archive` and `digest` consumers retain their exact-revision
contracts, including the static catalog, and original evidence. Use the exact
producer revision when replaying historical archives; the current runtime does
not coerce an earlier catalog into current authority. Replay validates supplied bytes and lineage;
it cannot recreate missing captures, prove provenance by itself or upgrade the
failed verdict. Existing replay may run local clean consumers with locked public
build prerequisites, so it is not a zero-effect operation; use it only within
separately applicable work authorization. Do not re-upload or re-observe the
registry to complete historical evidence.

The [v2 migration](../hcoona-release-smoke-python-lld.md#python-governance-v2-migration)
moves normal admission off `native-acceptance` without fabricating suite or
generation fields. Retained code validates the original request, fixture,
capture and audit schemas for historical readers. Shared bootstrap helpers keep
their separate resource-establishment consumers.
The configured native publisher remains an external resource until a separately
authorized configuration change removes it; producer retirement does not
claim its deletion. Current actual publication and clean-consumer evidence
requirements are in the [LLD](../hcoona-release-smoke-python-lld.md#evidence-and-delivery-gates).
