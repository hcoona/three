---
name: record-system-review
description: Review governance, Delivery Wave, record-binding, root instruction, conformance-control, and authority/identifier contract changes for consistency with repository governance and record policies.
---

# Record-System Review

Review the proposed diff against target-branch
[`governance-system.md`](../../../docs/governance/governance-system.md) and
[`record-system.md`](../../../docs/governance/record-system.md). Do not invent
new policy from this Skill.

## Triggers and Preconditions

Invoke when a change edits `docs/governance/**`, `docs/delivery-wave.md`,
`schemas/governance/**`, `docs/engineering/engineering-principles.md`, root
`AGENTS.md` / `CONTRIBUTING.md` / `.github/pull_request_template.md`,
`record-families.yaml` or `apm.lock.yaml` provenance, `hk` conformance controls,
governance review Skills, authority/requirement-ID/immutability/license
contracts in any record, or claims the
[Record-System Gate](../../../docs/governance/record-system.md#record-system-gate).
Ordinary project record edits covered by wildcard bindings, `lychee`, and
`records:check` do not require this review unless they alter those contracts.

The reviewer must be independent of the change author and implementation agent.
Proposed governance amendments are judged against `main`'s currently accepted
authorities and cannot waive an accepted obligation during their own review.

## Checklist

1. **Authorization:** Verify the change either qualifies for the Wave-free
   ordinary PR channel (contract-preserving maintenance, internal refactoring/CI
   consolidation/dead-route retirement, owner-merged governance/doc cleanup, or
   routine public-product release transitions/owner-instructed dispatches) or
   stays within an accepted target-branch `docs/delivery-wave.md` entry
   ([work authorization](../../../docs/governance/record-system.md#work-authorization-and-concurrency)),
   with each `main` merge self-consistent.
2. **Thin Wave hygiene (when `docs/delivery-wave.md` changes):** Verify entries
   stay concise (~15–25 lines: carrier, scope/outcome, numeric external/paid
   ceilings when applicable, exclusions/closure), omit `Diagnosis` or
   implementation inventories, preserve active risk bounds, and retire grants
   whose work or carrier Issue is complete.
3. **Single authority & namespaces:** Verify each concern has one canonical
   authority, audience/portal files route without duplicating rules, wildcard or
   explicit bindings in `record-families.yaml` map to `repository` or real
   owning project/skill directories (never `tests/` or generated roots), and
   project records do not add workflow gates or quotas on harmless local
   iteration ([project namespaces](../../../docs/governance/record-system.md#project-namespaces)).
4. **Admission, identifiers & lifecycle:** Verify newly admitted records/controls
   have a real consumer and failure mode; established `REQ`/`FR`/`NFR`/`AC`
   identifiers, human-source inputs, provenance, and licenses are preserved or
   retired with valid `current authority` targets; and replaced files/links are
   cleaned up atomically.
5. **Controls & mechanical evidence:** Verify controls match governing policy
   without encoding subjective value/architecture/risk judgments into mechanical
   rules, and confirm `hk` (including `lychee` and `records:check`) passed on
   the reviewed snapshot.

Evaluate only the repository diff and relied-on evidence (on re-review, only the
remediation delta), not PR/Issue discussion formatting. Route empirical evidence
quality to `research-evidence-review` and value/scope/risk choices to the owner.

## Output and Triage

Output `No material findings.` when clean; otherwise report only high-confidence
material findings with `Rule`, `Severity` (`blocking` | `advisory`),
`Confidence` (`1`–`10`), `Location`, `Evidence`, and `Required action` (marking
`owner escalation` when an owner decision is required). Apply
[`GOV-011`](../../../docs/governance/governance-system.md#gov-011-separate-findings-from-disposition)
for delta verification and disputed/unresolved third-party triage.

Adapted from `hcoona/microsoft-authentication-cli` (`1a024985`); copyright and
MIT notice retained in `docs/governance/reference-license.txt`.
