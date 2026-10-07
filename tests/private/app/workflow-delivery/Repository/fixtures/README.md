# Recorded Native Outputs

## Python Metadata

`python-metadata.json` projects planning fields from the original basis metadata
command in [PR #1063's native evidence](https://github.com/hcoona/three/pull/1063#issuecomment-6045108404).
The archive SHA256 is
`f8952378e7b7341d20ae6f12ae8657f67150ac965a3ed4eadc85189d2b11ce25`;
the original is `generation1/commands/001-basis-metadata/stdout` in the retained
packet. UV is pinned to `0.12.23`, source
`46b84fd0bfec23b72f29e8e2185ba68a65052f48`.

Only typed endpoint path fields are rerooted to `/recorded/python-endpoint`.
All graph keys, referenced IDs, names, sources, groups, extras and marker strings
are retained. Artifact-only `wheels`, `sdist` and `latest_version` fields are
omitted; this fixture has no package-content consumer. The test reroots typed
path fields again for its host platform and asserts the original IDs unchanged.
This is controlled output replay, not a native run, caller qualification,
cross-revision impact observation or Windows evidence. The original campaign
remains closed.

`python-build-associations.json` projects `workspace_root`, `members` and
`build_requirements` from the same packet's
`generation1/candidate-observation/reader-evidence/result.json` native facts.
Its typed paths use the same rerooting. Tests associate those retained lowered
sources with metadata paths; the two originals were archive materializations of
the same reviewed tree. This does not prove execution, version satisfaction or
different-commit assembly.

`python-passive-contexts.json` retains the `projects` array of that original
candidate request, with the same typed path substitution. PyPA requirement
strings remain unchanged. The adapter uses these extractor answers to reject
omitted build contexts or requirement results, without interpreting the strings.

## PNPM

These test fixtures extract the wanted-lock listing, queried publish metadata and
queried closures from the retained PNPM `12.8.1` observations in
[PR #1009's evidence packet](https://github.com/hcoona/three/pull/1009#issuecomment-5973394222).
Only the absolute workspace prefix becomes `__WORKSPACE__`; the test substitutes
its platform's temporary root. The packet preserves original command outputs,
inputs, environment, source and executable identities. The
[research record](../../../../../../src/public/lib/three-workflow-delivery-v3/docs/research/pnpm-native-planning.md#directory-coordinate-adaptation-candidate)
owns their conclusions and limitations.

`PnpmRecordedOutputTests` reuses the full listing as membership input and extracts
controlled singleton full responses from it; the originals are global listings,
not observations of singleton queries. It returns
controlled null layout settings, empty metadata for unqueried projects and
singleton selections for unqueried closures. Recorded closures predate the
explicit legacy-filter override. These are adapter replay tests, not complete
native reader or installation qualification. They execute no PNPM operation.
