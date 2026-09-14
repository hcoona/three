# Workflow Delivery v3 NuGet Consumer Restore Host

This private companion implements the native restore stage of Workflow Delivery
v3's [destination consumer](../../../public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-github-packages-lld.md#evidence-admission-and-completion).
The [project handoff](../../../public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
owns scope and the separate implementation, native-operation and publication
gates. This executable does not authorize an operation.

The sole entry is `restore <request-path>`. It reads a bounded JSON request and
the caller-supplied `WDV3_NUGET_CONSUMER_READ_TOKEN` environment input. It never
acquires a credential, publishes, evaluates MSBuild, builds a project or invokes
product code. The caller must independently admit the original package, witness,
service-index bytes, exact prebuilt host/runtime provenance and concrete read
allowance before execution.

The host validates the SDK-generated graph, exact version, source mapping and
fresh caches. NuGet's native restore engine reads directly from the selected
GitHub Packages HTTP source through a GET-only handler with cumulative request,
response-body-byte and time limits. Redirects, ambient proxies, cookies and
default authentication are disabled. These limits describe application reads,
not all TLS or network traffic. Original safe response bytes and reservations
are retained; reflected credentials stop the operation before persistence or
handoff to NuGet. Success requires exact installed archive/witness bytes and
assets bound to that package. Failure retains safe partial evidence and cannot
resume in the same directory.

The Python [consumer component](../../../public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/acceptance/nuget_consumer.py)
owns fresh project creation and POSIX process supervision, then checks the native
result before credential-free build and marker invocation. Windows operators
need configured WSL for that coordinator; local controlled tests do not establish
the separate Windows publication profile, real feed access or native acceptance.
Scoped CPM leaves the original fixture/reader helper's build inputs unchanged.
