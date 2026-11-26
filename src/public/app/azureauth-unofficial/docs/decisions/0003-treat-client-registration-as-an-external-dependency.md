# 0003: Treat Client Registration as an External Dependency

## Context

OAuth native applications are public clients. Their client IDs are public identifiers,
not secrets. A client ID nevertheless refers to an application registration whose owner
controls account types, redirect and broker configuration, permissions, consent,
branding, and lifecycle.

The upstream Azure DevOps path uses a Microsoft-owned Visual Studio public-client
registration. Public Azure DevOps guidance also states that an ordinary Microsoft Entra
application cannot natively support Microsoft-account users for the Azure DevOps
resource. These facts do not prove the current behavior, intended reuse, or support status
of the Visual Studio registration for this fork.

## Decision

Treat client application identity as explicit configuration.

V2 may provide a named Azure DevOps compatibility profile using the existing
Microsoft-owned public-client ID, but:

- the fork does not claim ownership or support for that registration;
- the profile is separate from the authentication core;
- the client ID remains configurable;
- identity or capability changes fail visibly;
- user-facing material does not imply Microsoft sponsorship.

Default enablement of that profile remains excluded, and its general distribution
remains blocked until its account, resource, consent, audit, and branding behavior is
publicly reviewed and validated for the intended hosts.

One exception applies. On 2026-10-28 the repository owner decided, in the Delivery
Wave entry merged through
[PR #542](https://github.com/hcoona/microsoft-authentication-cli/pull/542), that
`azureauth-unofficial` prerelease archives may contain that profile as an explicit
opt-in file. The owner accepts that sign-in, consent, and audit records attribute such
use to the Microsoft-owned Visual Studio application, that Microsoft provides no
support or availability commitment, and that the remaining items of the
[externally owned Client Profile gate](../product/compatibility-and-migration.md#externally-owned-client-profile-gate)
stay open. The bundled file is selected only through an explicit `--profile` argument,
and the package README states these limits. The exception covers no default
selection, no other profile, and no supported release.

## Consequences

- Reusing the identifier is not treated as handling a secret.
- The compatibility profile has an external availability and governance dependency.
- An independently owned client registration may have different MSA and Azure DevOps
  capabilities.
- Cache namespace and migration must include client application identity.
- Opt-in behavior is explicit selection only. Distribution beyond the prerelease
  exception above remains an open decision.
