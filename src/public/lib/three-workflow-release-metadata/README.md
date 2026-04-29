# three-workflow-release-metadata

Metadata helpers for the Three workflow release design. The current helper
consumes the closed `.NET` metadata input handoff and emits the Windows-authored
`dotnet-planner-metadata.json` observation used by the planner. It does not
rediscover descriptors, plan releases, build artifacts, or publish anything.
