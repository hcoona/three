# Positive Fixture

## Proposed Change

A pull request moves a security architecture record into `src/public/lib/example/docs/security/`, updates its
only current links, updates the current record-family path, deletes the replaced path,
rebinds a standalone local migration skill in `docs/governance/record-families.yaml` to its
owning `.apm/skills/example` directory without changing its family responsibilities or
`apm.lock.yaml` source/deployment mappings, and passes all blocking hk checks and
explicit-base `records:check`. The concerns, producers, maintainers, consumers, and review
points are unchanged.

## Expected Review

```text
No material findings.
```
