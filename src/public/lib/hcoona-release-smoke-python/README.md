# Python Release Smoke

This dependency-free package is the Python proving payload for
[Workflow Delivery v3](../three-workflow-delivery-v3/README.md).
Its installed API is `hcoona_release_smoke_python.project_id()`, which returns
`hcoona-release-smoke-python`.

The [Python slice design](../three-workflow-delivery-v3/docs/hcoona-release-smoke-python-lld.md)
owns versioning, wheel/sdist qualification and publication behavior. The
[V3 handoff](../three-workflow-delivery-v3/docs/agent-handoff.md#starting-a-new-session)
routes destination admission and separately authorized publication gates.
This package has no production consumers or
compatibility promise.
