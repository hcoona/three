# three-workflow-release-proof

Proof wrapper and replay classification helpers for the Three workflow-release
system. The package consumes closed build/publish/plan handoffs plus
control-plane run provenance, emits validated `immutable-proof` and
`github-release-asset-proof` receipts, and derives fail-closed planner remote
observation maps from normalized remote state and admissible proof wrappers.

Immutable replay classification requires closed build-result receipt inputs in
addition to immutable proof wrappers. Each receipt names the uploaded build-result
artifact id/name and contains the validated `build-result`; a wrapper is
admissible only when that referenced receipt matches the current plan, project,
variant, artifact digest, and byte size.
