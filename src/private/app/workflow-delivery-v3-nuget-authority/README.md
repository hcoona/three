# Workflow Delivery v3 NuGet Authority Helper

This helper supplies `nuget-lock` and `nuget-packages-config` static-reference
facts for [Workflow Delivery v3](../../../public/lib/three-workflow-delivery-v3/README.md).
The v3 project owns its [static-reference contract](../../../public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-npm-lld.md).

It is not a .NET Release Provider or NuGet publisher. The
[NuGet second-slice handoff](../../../public/lib/three-workflow-delivery-v3/docs/nuget-smoke-research-handoff.md)
routes the confirmed requirements, accepted design, and remaining admission
gates. Disabled implementation, native probes, and publication require
separate authorization.
