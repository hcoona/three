# AzureAuth Unofficial V2 Upstream Provenance

## Source

- **Repository:** <https://github.com/AzureAD/microsoft-authentication-cli>
- **Upstream branch:** `main`
- **Audited baseline:** `de20930c34b3b86c8a0ed7bbdeeca3f662dae918`
- **Tag `0.9.5`:** `21258ff3a2cbb01d6891243114a55abe9ae3587e`
- **Tag `0.9.6`:** `8ef1b8b00782bf20a51de078289819a79c3cba70`
- **Baseline revalidated:** 2026-08-28
- **Upstream license:** MIT
- **Monorepo consolidation snapshot:** [`hcoona/microsoft-authentication-cli` `main-v2` at `f509276cad32c49b5764af906e8374f3f9b182ae`](https://github.com/hcoona/microsoft-authentication-cli/tree/f509276cad32c49b5764af906e8374f3f9b182ae)

The project originated on an orphan `main-v2` branch in
`hcoona/microsoft-authentication-cli` with independent Git history and was consolidated
into `hcoona/three` under `src/public/app/azureauth-unofficial/` and
`tests/public/app/azureauth-unofficial/`. The clean-room reset prevents v1 compatibility
and file layout from becoming implicit v2 requirements without hiding upstream
provenance.

## Import Policy

Do not merge `AzureAD/microsoft-authentication-cli` `main` directly into this project.

## Imported Source Inventory

No production source file has been imported from upstream. Because the clean-room
`main-v2` codebase and records were authored solely by the repository owner, the project
itself is licensed under `hcoona/three`'s default
`LGPL-3.0-or-later WITH LGPL-3.0-linking-exception` rather than retaining the former
root MIT `LICENSE.txt` copied from `de20930c34b3b86c8a0ed7bbdeeca3f662dae918`.

An upstream change may be imported only when:

1. it provides a mechanism, security fix, test, or operational behavior needed by an
   accepted v2 requirement;
2. the exact upstream commit and imported files are recorded in the change;
3. copyright and license notices are preserved;
4. the code is adapted to v2 contracts rather than reintroducing v1 orchestration;
5. platform, security, and behavioral validation accompanies the import.

Every copied or substantially derived file from upstream must retain the notices
required by the MIT License. Imported code remains subject to independent review;
upstream origin is not evidence that it satisfies v2 invariants.

### Security Modeling Template

[`docs/security/authentication-engine.tm7`](docs/security/authentication-engine.tm7)
embeds the public `default.tb7` template from
[`microsoft/threat-modeling-templates` at `0ece9c71b6f3710b10d497bd1ef63e57805e7c3e`](https://github.com/microsoft/threat-modeling-templates/blob/0ece9c71b6f3710b10d497bd1ef63e57805e7c3e/default.tb7).
Its Microsoft copyright and MIT permission notice are retained in the model's XML
comment. The project-specific diagram and dispositions are architecture records, not
imported production implementation. Bundled binary runtime and MSAL third-party MIT
notices for packaged Windows archives remain in
[`packaging/windows/THIRD-PARTY-NOTICES.header.txt`](packaging/windows/THIRD-PARTY-NOTICES.header.txt).

## Update Policy

This project does not promise continuous synchronization with upstream. Security and
mechanism-level fixes should be evaluated promptly, but every import must remain
intentional and reviewable. Repository developer tooling and dependency locks are owned
by the monorepo [workspace setup](../../../../docs/engineering/workspaces.md).

Mutable upstream facts are rechecked only through the typed triggers and outcomes in
[`docs/research/rechecks.yaml`](docs/research/rechecks.yaml). Record the resulting
conclusion or disposition in the affected current record and its pull request. This file
remains the authority for upstream provenance, import policy, and imported-source
inventory; it is not a parallel recheck ledger.

Do not publish internal or nonpublic evidence when explaining why an upstream change is
or is not imported.
