# PNPM Recorded Outputs

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
