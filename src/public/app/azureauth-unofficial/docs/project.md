# AzureAuth Unofficial V2 Project Identity and Posture

## Public Project Identity

**AzureAuth Unofficial V2** is the canonical public display name for this project.
`Unofficial` is part of the name and must be retained when an audience-facing entry
point names the project.

The project originated on the `main-v2` line of `hcoona/microsoft-authentication-cli`
and now resides in `hcoona/three` under `src/public/app/azureauth-unofficial/`.

The public display name does not select a future distributed product or any operational
identity. Those selections remain governed by
[`operational-identities.yaml`](operational-identities.yaml).

## Unofficial Status

**AzureAuth Unofficial V2** is an unofficial clean-room implementation. It is not an
official Microsoft Authentication CLI release and has no upstream support, service-level,
release, or security-response commitment.

The project is licensed under `LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`. Neither
this license nor the upstream MIT License establishes trademark rights,
application-registration ownership, service entitlement, or Microsoft sponsorship.

## Naming and Branding

The public display name does not authorize `AzureAuth`, `azureauth`, or a Microsoft
publisher namespace as an operational identifier. A distributed v2 product must use the
independent operational identities that
[`V2-REQ-044`](product/requirements/cache-security-and-operational-identity.md#v2-req-044-independent-distribution-identity)
requires.

Until those identities are selected:

- do not publish a binary as an official AzureAuth release;
- do not reuse upstream logos, signing identities, release badges, telemetry identities,
  or update channels;
- retain a prominent unofficial notice in public user-facing material.

## Support and Release

Historical unsupported `0.1.0-alpha` prereleases of the Windows `azureauth-unofficial`
package through `0.1.0-alpha.37` were built by the
[former standalone release workflow](https://github.com/hcoona/microsoft-authentication-cli/blob/f509276cad32c49b5764af906e8374f3f9b182ae/.github/workflows/release.yml)
and published on `hcoona/microsoft-authentication-cli` after a release review; future
monorepo releases require explicit Workflow Delivery v3 onboarding. They carry no
support, production-readiness or compatibility claim, and the repository provides no
supported v2 release or service-level objective.

A future support claim must identify the exact release, platform, account state, client
application, mechanism, dependency set, and validation evidence to which it applies.

Current compatibility and migration commitments are defined only in
[`compatibility-and-migration.md`](product/compatibility-and-migration.md).

## Public Record Boundary

Committed rationale must use public source, public standards and documentation, public
Issues or pull requests, or reproducible sanitized experiments.

Do not commit credentials, tokens, authorization codes, private identities, private
account or tenant information, internal company information, private conversations,
unpublished downstream observations, or raw broker diagnostics. If private information
motivates work, restate the public technical question and collect publishable evidence
before using it as repository rationale.

## Governance Authority

Repository governance, work authorization, and review gates follow
[`governance-system.md`](../../../../../docs/governance/governance-system.md) and
[`record-system.md`](../../../../../docs/governance/record-system.md).

## Security Reporting

**AzureAuth Unofficial V2** has no production-support or response-time commitment.

Do not report a suspected vulnerability that contains sensitive details through a public
issue. Contact the repository maintainer privately at the author email recorded in the
root [`pyproject.toml`](../../../../../pyproject.toml).

If the issue also affects the Microsoft-owned upstream project, follow the upstream
security policy and report it to Microsoft through the appropriate private channel.
Reporting it here does not report it to Microsoft.

Public issues may be used for non-sensitive hardening proposals, threat-model questions,
and already-disclosed dependency advisories. Never include real tokens, authorization
codes, cache files, account identifiers, tenant-specific policy output, or private broker
diagnostics in a report or test case.
