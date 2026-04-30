# three-workflow-release-publish

Publish executors for the Three workflow release design. The package consumes one
closed `publish-request.json`, verifies receipt digests and package identity
against the planner-frozen publish node, performs exactly the selected live
publish action, and emits a closed `publish-result.json`. It does not plan
releases, read descriptors or target catalogs, create tags, or decide skip and
replay policy.

For GitHub Release targets, the publish job must run `actions/attest@v4` for
each planned release asset and pass the action outputs in the closed
`publish-request.json`. Before any GitHub Release mutation or asset upload, the
executor verifies every asset with `gh attestation verify` using the supplied
bundle path, planner-frozen signer workflow, and source digest. A positive
`publish-result.json` is emitted only after this preflight succeeds.
