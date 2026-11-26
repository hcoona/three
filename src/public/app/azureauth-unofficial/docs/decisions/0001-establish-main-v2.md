# 0001: Establish `main-v2` as an Orphan Line

## Context

AzureAuth v1 contains useful source and operational knowledge, but its existing file
layout, command surface, and Git history can make v1 compatibility appear to be an
implicit v2 requirement.

The v2 effort needs a clean initial state while retaining transparent upstream
provenance.

## Decision

Create `main-v2` as an orphan branch in the GitHub fork.

The branch starts with vision, evidence, governance, requirements, validation, and
decision records rather than copied production source. The repository retains its GitHub
fork relationship, and `UPSTREAM.md` records the audited baseline and source-import
policy.

## Consequences

- V2 has no inherited Git ancestry or automatic merge path from upstream.
- Upstream source reuse is explicit and reviewable.
- V1 compatibility is not presumed.
- Imported copyright and license notices remain mandatory.
- Security and mechanism fixes require deliberate evaluation rather than blanket merges.
