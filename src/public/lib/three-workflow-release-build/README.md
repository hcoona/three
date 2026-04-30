# three-workflow-release-build

Build executors for the Three workflow release design. The package consumes one
closed `build-request.json`, realizes exactly the requested variant artifacts
inside a caller-provided bundle directory, and emits a closed
`build-result.json`. It does not plan releases, read descriptors or target
catalogs, upload artifacts, or publish packages.
