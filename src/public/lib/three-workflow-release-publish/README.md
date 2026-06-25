# three-workflow-release-publish

Publish executors for the Three workflow release design. For package-registry
targets, the package consumes one closed `publish-request.json`, verifies receipt
digests and package identity against the planner-frozen publish node, performs
exactly the selected live publish action, and emits a closed
`publish-result.json`. It does not plan releases, read descriptors or target
catalogs, create tags, or decide skip and replay policy.

GitHub Release publication is handled by the active
`release-create-github-release.yml` path, not by this package-registry executor.
Attestation gates run separately in `release-orchestrate.yml`; this executor does
not verify GitHub Release attestations before mutation. The GitHub Release path
emits the result file as `github-release-result.json`; the uploaded artifact is
scoped as
`release-github-release-result-v1-<run_id>-<run_attempt>-<binding_digest>`.
