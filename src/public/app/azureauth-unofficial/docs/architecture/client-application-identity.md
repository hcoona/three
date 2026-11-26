# Client Application Identity

This architecture view defines how v2 treats OAuth public-client application
registrations. It does not claim ownership of any Microsoft registration or freeze a
client-profile contract.

The [Windows Slice design](../designs/windows-ado-authentication.md#profile-contract-and-ownership)
and its linked schema now own the concrete Windows Profile representation and candidate
definition. This view continues to own registration semantics and tenant mapping;
candidate definition does not activate or distribute that Profile.

## Public Does Not Mean Ownerless

Native desktop applications are OAuth public clients. A distributed application cannot
keep a shared client secret confidential, and the client ID is therefore an identifier,
not a credential.

RFC 8252 nevertheless distinguishes public identification from proof of client identity
and explicitly discusses client impersonation:

- [Registration of Native App Clients](https://www.rfc-editor.org/rfc/rfc8252.html#section-8.4)
- [Client Authentication](https://www.rfc-editor.org/rfc/rfc8252.html#section-8.5)
- [Client Impersonation](https://www.rfc-editor.org/rfc/rfc8252.html#section-8.6)

In Microsoft Entra ID, a client ID identifies an application object whose owner controls
supported account types, redirect configuration, API permissions, branding, consent, and
lifecycle. See
[Apps and service principals in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity-platform/app-objects-and-service-principals).

Consequently:

- a client ID must never be treated as a secret;
- technical ability to send a client ID does not transfer ownership of its application
  registration;
- behavior enabled by a Microsoft-owned registration remains an external dependency;
- audit, consent, and policy systems can attribute activity to that application identity.

## Upstream Azure DevOps Compatibility Identity

At the audited upstream baseline, the Azure DevOps specialization uses:

- **Client ID:** `872cd9fa-d31f-45e0-9eab-6e460a02d1f1`
- **Azure DevOps scope:** `499b84ac-1321-427f-aa17-267ca6975798/.default`

The upstream source identifies the client ID as the Visual Studio 2019 and earlier public
client:

- [`src/AzureAuth/Ado/Constants.cs`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Ado/Constants.cs)

This fork does not own or control that registration. V2 may provide an explicitly named
compatibility profile that uses it. Product identity, support, strict-result, and fallback
behavior are governed by
[`V2-REQ-003`](../product/requirements/product-boundary.md#v2-req-003-unofficial-product-identity),
[`V2-REQ-022`](../product/requirements/strategy-interaction-and-host.md#v2-req-022-strict-result-identity),
[`V2-REQ-023`](../product/requirements/strategy-interaction-and-host.md#v2-req-023-classified-fallback),
and decision
[`0003`](../decisions/0003-treat-client-registration-as-an-external-dependency.md).

## Azure DevOps and Microsoft Accounts

The [2026-09-04 RECHECK-007 desk finding](../research/v1-public-contract-baseline.md#recheck-007-azure-devops-microsoft-account-behavior)
records Azure DevOps guidance and its limits. The upstream source proves that AzureAuth
uses a Microsoft-owned Visual Studio client; it does not prove current MSA behavior,
intended reuse, or support status for this fork. The subsequent
[bounded Windows observation](../research/experiments/windows-msal-account-metadata.md#observed-msa-token-git-discovery-and-silent-reuse)
demonstrates exact personal-account acquisition, authenticated Git discovery, and
fresh-process silent reuse for one existing configuration. This supplies concrete
mechanism evidence. The [external dependency boundary](#external-dependency-boundary)
below records the public reuse evidence and its support limits; the Profile-selection
gate remains open. The [primary journey](../product/user-stories.md) remains a first-release
blocker until its product validation obligations are met.

This makes client-application identity a functional input, not a replaceable cosmetic
constant.

## Architecture Model

1. The authentication core receives client-application configuration through the explicitly
   selected Client Profile, not inline caller configuration.
2. The core does not embed a client secret for a native public-client flow.
3. Pre-distributed and user-provided profiles have the same interpretation and validation
   and remain separate from the mechanism core. Selection follows
   [`V2-REQ-018`](../product/requirements/request-identity-and-authority.md#v2-req-018-explicit-client-profile-selection).
4. A Client Profile represents stable public-client application, one authority cloud,
   application tenant eligibility, ownership, and platform-integration configuration.
   Redirect, broker-registration, signing, and bundle-identity constraints are integration
   concerns, not a selected file schema. The Visual Studio compatibility candidate is
   Public Cloud only; its concrete candidate is defined by the Windows design and is not
   activated or supported until the external Profile gate passes. Decision 0003's
   prerelease exception lets prerelease archives bundle it as an explicit opt-in file.
5. Resource/scopes, strict email, and interaction permission belong to each request.
   Profiles do not own resource presets, scope catalogs, acquisition order, deadline
   defaults, or cache modes. Effective tenant resolution follows
   [`V2-REQ-019`](../product/requirements/request-identity-and-authority.md#v2-req-019-tenant-selection).

In the primary journey, the adapter supplies the Azure DevOps scope shown above as
consumer knowledge; it is not embedded as a profile resource default. The Windows design
selects an explicit caller-managed local Profile file and versioned strict reader, without
automatic discovery or an engine-owned configuration store.

Required configurability and visible ownership are defined by
[`V2-REQ-042`](../product/requirements/cache-security-and-operational-identity.md#v2-req-042-client-registration-as-configuration).
Strict request and fallback behavior are defined by `V2-REQ-022` and `V2-REQ-023`
above.

## Experiment Configuration and Tenant Policy

The successful [Windows probe](../research/experiments/windows-msal-account-metadata.md#gcm-informed-msa-acquisition-and-silent-reuse)
used an `organizations` authority with MSA passthrough enabled and, for silent reuse of
the selected MSA account, the public MSA transfer tenant. Those are experimental provider
settings. The probe changed authority and passthrough together; its result neither proves
that `common` is unsupported nor verifies a `common` configuration with passthrough.

Product normalization remains governed by `V2-REQ-019`: an eligible multitenant Profile
with no caller tenant resolves to `common`, and caller tenant selectors are `common` or
an exact tenant GUID. An exact selector constrains the token/resource tenant, not the
account's home tenant. `common` imposes no particular result tenant; it does not require
every provider operation to address a literal `/common` endpoint. The
[public source assessment](../research/experiments/windows-msal-account-metadata.md#client-profile-and-tenant-mapping-assessment)
shows that MSAL itself resolves tenantless authorities during acquisition.

Policy retains the normalized tenant constraint separately from the mechanism's provider
authority. This is an allocation within the existing policy and mechanism components,
not another public request field. The provider may resolve an unconstrained tenant only
within the selected Profile's eligible account types, cloud, and integration contract.
Changing the endpoint never relaxes strict email, client, scope, or result validation.

### Provider Mapping

The following rules define the high-level mapping if the corresponding Profile and
integration pass their existing acceptance gates. They do not enable a Profile, select a
supported platform, or freeze its configuration representation.

| Normalized tenant policy and eligible integration | Provider authority behavior | Required result behavior |
| --- | --- | --- |
| `common`, ordinary multitenant integration | Start with the cloud's `common` authority and use documented provider tenant resolution. Do not enable MSA passthrough for an arbitrary registration. | Retain the provider's actual tenant and authority; enforce all request and Profile constraints. |
| `common`, Public Cloud Visual Studio legacy passthrough compatibility integration | Use `organizations` with MSA passthrough for the initial authority. For silent acquisition of a uniquely resolved MSA home account, use the public MSA transfer tenant. For other accounts, use documented provider tenant resolution. | Preserve `common` as the caller's unconstrained tenant policy. The observed result tenant is authoritative; the routing tenant is not a fabricated result value. |
| Explicit compatible tenant GUID, including with the legacy integration | Use that exact resource tenant for silent and interactive acquisition. Do not replace it with the MSA home or transfer tenant. | Require the provider-reported token/resource tenant to equal the requested GUID. A mismatch is terminal validation failure. |
| Fixed single-tenant Profile | Use the Profile's fixed tenant and reject conflicting caller input before acquisition. | Require the fixed tenant; no MSA-based tenant substitution. |

The legacy row adapts the already observed Windows configuration and pinned GCM/MSAL
behavior. `organizations` together with the legacy passthrough capability admits the MSA
path; bare `organizations` is not a general substitute for `common`. The mapping is
specific to this externally owned Public Cloud compatibility candidate and is not inferred
from an email domain, requested resource, or arbitrary client ID. MSA home-account metadata
is used only after strict real-account resolution to choose the documented silent route;
it is not another caller selector or a replacement identity check.

The [GCM workaround](../research/experiments/windows-msal-account-metadata.md#client-profile-and-tenant-mapping-assessment)
does not guard against an explicit caller resource-tenant constraint. V2's exact-tenant
branch therefore takes precedence over that workaround. If the requested tenant cannot
satisfy the request, apply the existing failure classification without trying a different
tenant. Any permitted mechanism fallback keeps the same exact constraint.

Azure DevOps organization discovery and service-header interpretation remain downstream
consumer knowledge. The engine receives the resulting caller intent; it does not add
repository URLs, Git discovery, organization lookup, or a resource catalog to implement
this mapping. A `common` request accepts provider tenant resolution; a caller needing a
particular resource tenant must supply its exact GUID.

The architecture resolves how tenant intent and provider routing coexist. Real-host
evidence, from the [probe](../research/experiments/windows-msal-account-metadata.md) and the
[real-host results](../research/experiments/real-host-validation.md#results), covers
only the declared legacy configuration with `common`, not all rows above. The
[validation strategy](../validation/strategy.md#policy-tests) carries the remaining
mapping and exact-tenant scenarios before a support claim is accepted.

### External Dependency Boundary

Decision `0003` already permits an explicitly named compatibility Profile for the
Microsoft-owned registration. The [public evidence](../research/experiments/windows-msal-account-metadata.md#client-profile-and-tenant-mapping-assessment)
establishes the following boundary:

- Microsoft owns the Visual Studio registration. AzureAuth and GCM publicly use it for
  Azure DevOps authentication; their published source supplies a concrete reuse baseline.
- Microsoft documents MSA passthrough as a legacy first-party-application capability to
  avoid where possible. An independently registered application cannot be assumed to
  have it. Using the identifier does not make this fork a Microsoft first-party product.
- The reviewed sources provide no support or continued-availability guarantee for this
  fork. The candidate is an unofficial compatibility dependency; its documentation must
  identify Microsoft ownership and the lack of an upstream support commitment. No owner
  endorsement or blanket prohibition on third-party reuse is inferred from these sources.

This records the known reuse and the limits of support evidence without claiming
Microsoft approval of the fork. It does not waive the
[external-client-profile gate](../product/compatibility-and-migration.md#externally-owned-client-profile-gate),
whose conditions still need validation and acceptance before general distribution;
decision `0003` records the owner's prerelease exception. No Microsoft support
contract is asserted or required to treat an identifier as public configuration under
decision `0003`.

## Governing Evidence and Gates

Experiments, mutable Azure DevOps account guidance, compatibility activation, and
persisted operational identity are governed respectively by:

- [`experiment-safety.md`](../research/experiment-safety.md);
- [`RECHECK-007`](../research/rechecks.yaml);
- the external-client-profile gate in
  [`compatibility-and-migration.md`](../product/compatibility-and-migration.md);
- [`operational-identities.yaml`](../governance/operational-identities.yaml).
