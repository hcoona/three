# Request, Identity, and Authority Requirements

## V2-REQ-010: Versioned Command-Line Request

The first version must expose one native authentication request protocol through
command-line arguments. Every authentication request must explicitly declare its protocol
version. Unsupported major versions and invalid arguments must be rejected before
authentication begins.

Stdin-JSON, batch, and daemon request protocols are outside the first version.

## V2-REQ-011: Explicit Authentication Target

Each authentication request must explicitly provide scopes for exactly one target
resource. The selected Client Profile must identify the public client application and a
single trusted authority cloud. V2 must resolve the effective tenant under the applicable
tenant policy before acquisition. Resource and scope selection must remain request intent
rather than Client Profile configuration. The native interface must not accept a separate
resource input.

A Client Profile represents stable public-client application and cloud/platform
integration configuration, not a resource preset. V2 must not supply request scope
defaults or require a universal catalog of allowed resource scopes. The caller supplies
the scopes needed by its consumer.

## V2-REQ-011A: Trusted Authority

The selected Client Profile's authority must belong to an explicitly trusted Microsoft
Entra cloud and must pass MSAL authority validation. Arbitrary caller-supplied authority
hosts and disabling authority validation are prohibited unless a separate accepted
decision defines the trust model.

## V2-REQ-012: Required Email Account Constraint

Every authentication request must provide one account email. V2 must treat that value as
a strict, full-address, case-insensitive account constraint. It must not treat a different
alias, a domain or suffix match, an account type, or an ambient default account as
equivalent. A missing account email is an invalid request, and a result whose
provider-observed email is absent or does not match must not succeed.

Matching uses the whole email string. The first version must not expose an Account Kind
or stable provider account ID selector, infer a persistent first-account binding, or make
a separate Personal-versus-Work postcondition. Multiple matching accounts visible to the
provider are ambiguous; this contract does not promise detection of same-email accounts
that the provider does not enumerate.

## V2-REQ-017: Caller-Intent Precedence

Explicit caller inputs must override applicable defaults but must not override fixed
Client Profile or trust constraints. Conflicting inputs must fail before authentication
rather than being ignored or silently substituted. Environment variables and host
detection must not supply, replace, or widen authentication request intent. Host detection
may report capabilities or unavailability.

## V2-REQ-018: Explicit Client Profile Selection

Every authentication request must explicitly select one Client Profile. V2 must not infer
that selection from the account, scopes, ambient defaults, or the number of available
profiles. A missing or unresolved selection must fail before authentication.
Pre-distributed and user-provided Client Profiles must follow the same interpretation and
validation rules.

The first version must not accept inline full client configuration in place of an
explicit Client Profile selection. Profile representation and storage are not selected
by this requirement.

## V2-REQ-019: Tenant Selection

For a single-tenant Client Profile, V2 must use its fixed tenant. For an eligible
multitenant Client Profile, an omitted tenant selector must resolve to `common`. A caller
may instead require an exact tenant ID where compatible with the profile. An explicit
tenant ID must constrain the token/resource tenant, not the account's home tenant, and
must never fall back to `common`. Caller-supplied tenant selectors are limited to
`common` and tenant IDs in GUID form.

Eligibility includes MSA-capable profiles whose application policy permits `common`;
it is not a promise that any profile is available. `common` does not promise a particular
token tenant. V2 must not infer the tenant from an email domain or accept tenant domains,
`organizations`, `consumers`, or unknown aliases as selectors. A selector conflicting
with a fixed tenant or profile constraint is invalid.

## V2-REQ-027: Scope Satisfaction

For explicitly requested delegated permission scopes, a successful result must be backed
by provider-authoritative scope metadata covering every requested permission; additional
granted scopes are allowed. A resource's `/.default` scope must not be combined with
dynamic permission scopes. For `/.default`, V2 must preserve the requested resource and
rely on the provider's authoritative request/result association, without requiring the
literal `/.default` value in returned scope metadata. Access-token parsing must not be
used to establish these conditions.
