# Security Policy

This policy defines how to privately report security vulnerabilities in `Three`
(`hcoona/three`) and how supported deliverables are scoped across the monorepo.

## Reporting a Vulnerability

Report suspected security vulnerabilities privately through **GitHub Private
Vulnerability Reporting**:

- <https://github.com/hcoona/three/security/advisories/new> (or **Security →
  Report a vulnerability** on this repository)

Do **not** report undisclosed vulnerabilities through public GitHub Issues, pull
requests, or discussions.

This advisory form is the repository-wide private intake channel unless an
owning project authority explicitly retains an additional project-specific
contact route or upstream-vendor reporting requirement (for example,
[`azureauth-unofficial`](src/public/app/azureauth-unofficial/docs/project.md#security-reporting),
which retains private maintainer-email contact and separate Microsoft-upstream
reporting guidance).

To help reproduce and assess the issue quickly, include:

- the affected package, extension, or application path and version (or Git
  commit SHA);
- the trust boundary crossed and potential security impact;
- minimal reproduction steps or proof-of-concept input; and
- any relevant runtime, browser, operating-system, or configuration details.

## Supported Deliverables and Versions

This document does not maintain a duplicate list of packages or versions. Scope
routes to the accepted repository authorities:

- **Active public deliverables:** public libraries, browser extensions, and
  applications live under `src/public/` and route through the
  [documentation portal](docs/README.md) (`Libraries and Companion Interfaces`
  and `Applications`), with Official release-unit inventory governed by
  [Public Release Triage and Inventory Gate](docs/engineering/public-release-triage.md).
- **Supported release line:** where an active public deliverable declares
  external support in its owning project records, security fixes apply only to
  its current active release line on `main` (and ship as its next Official
  release when the unit is admitted to Official Workflow Delivery). Unreleased
  or explicitly unsupported public projects (such as
  [`azureauth-unofficial`](src/public/app/azureauth-unofficial/docs/project.md#support-and-release))
  carry no supported release commitment, and older major/minor lines or
  historical Git tags do not receive backported security releases.
- **Out of public support scope:** internal tooling under `src/private/`,
  samples under `src/sample/`, experiments under `src/lab/`, test fixtures under
  `tests/`, and retired projects listed in
  [docs/retired-projects.md](docs/retired-projects.md) are not externally
  supported deliverables.

## Handling and Coordinated Disclosure

Reports are evaluated on a best-effort maintainer basis against each project's
accepted support posture, threat model, trust boundaries, and privacy
commitments (see
[Shared Engineering Principles — Security](docs/engineering/engineering-principles.md#security)
and project records routed from [docs/README.md](docs/README.md); this
repository does not operate a paid bug-bounty program or fixed response-time
SLA).

When a report is confirmed in an active public deliverable, any accepted fix is
prepared on `main` (or in a temporary private advisory fork when coordinated
embargo is warranted), published through the repository's Official Workflow
Delivery channel for units admitted to Official release, and disclosed through
the GitHub Security Advisory (and unit release notes when an Official release is
cut) once the fix is available.
