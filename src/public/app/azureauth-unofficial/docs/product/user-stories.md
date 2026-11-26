# Product User Stories

This record owns user context and motivation for requirements work. It does not define
normative behavior, realization choices, validation procedures, or current support.
Those concerns belong to [requirements](requirements/),
[architecture](../architecture/overview.md), [validation](../validation/strategy.md), and
the [support and release policy](../governance/project.md#support-and-release).

## Primary Journey: Personal Azure DevOps Git Access

> As a developer working with a personal Azure DevOps Git repository while my Windows WAM
> default account is a corporate work account, I want the Git credential adapter to
> request an Azure DevOps access token from the AzureAuth Unofficial V2 authentication
> engine for the personal Microsoft account email selected for that repository, so that
> Git operations do not silently authenticate as the corporate account and can reuse valid
> authentication state without prompting when it is available.

The developer has selected a personal account for this repository, not whichever account
happens to be the operating-system default. Repeated Git operations make wrong-account
success and unnecessary prompts particularly disruptive.

The external adapter identifies Azure DevOps, selects a Client Profile, and supplies the
account email and Azure DevOps scope as consumer knowledge. Repository bindings, remote
parsing, and Git credential protocol remain outside the authentication
engine under [decision 0004](../decisions/0004-keep-the-authentication-engine-separate-from-consumers.md).
The engine's PAT prohibition is governed by
[V2-REQ-005](requirements/product-boundary.md#v2-req-005-no-personal-access-tokens).

This is the primary first-release blocking journey; "first release" means the first
supported, non-prerelease release, so unsupported `0.1.0-alpha` prereleases are exempt.
Its acceptance evidence belongs to
the [primary-journey validation gate](../validation/strategy.md#primary-journey-gate).
The WAM context explains the wrong-default risk; it does not select WAM as the required
implementation or establish that any Client Profile or platform supports this journey.

### Account-Context Variants

The same requested-account goal applies across company-account and non-company-account
computer contexts, each with a requested company or personal account. These four
combinations are scenario variants, not four independent stories or additional
first-release commitments. The computer's default account, existing target-account
sign-in, and device management, join, or compliance are separate context; account labels
do not establish device state.

The concrete work-account variant is cloning a company Azure DevOps Git repository from
WSL using the company account selected for that repository. The same developer can use a
personal account for a different repository. Each request must preserve that repository's
selected identity even when another account is available in the Windows session. The
caller-to-engine deployment is defined by the
[WSL architecture](../architecture/overview.md#deployment-wsl-caller-and-windows-cli).

## Reuse Existing Operating-System Sign-In on First Use

> As a developer already signed in to my selected account through the operating system,
> I want the authentication engine to reuse that sign-in when compatible state is
> available on its first use, so that adopting the engine does not require an unnecessary
> separate sign-in.

This goal does not depend on an earlier engine invocation. OS sign-in alone does not
establish safely identifiable target-account state, resource authorization, or a suitable
Client Profile. The goal does not bypass interactive service policy or permit use of an
opaque OS default. Provider-account visibility and actual reuse remain evidence-gated.

## Reuse Authentication Across Package Ecosystems and Repositories

> As a developer using Azure Artifacts from multiple package ecosystems and repositories,
> I want compatible requests to reuse available valid authentication results for my
> selected account, so that moving between those tools and repositories does not cause
> redundant sign-in for the same authorized access.

The engine's contribution is safe authentication-state and access-token reuse. Package
configuration, ecosystem-specific credential translation, and derived-credential
lifecycle stay with downstream adapters. The goal does not require identical token bytes,
sharing across incompatible authorization or security contexts, or a single prompt
across concurrent processes.

## Background Requests Without Unexpected UI

> As a developer whose tools request authentication in the background, I want those
> requests to run without unexpected authentication or state-unlock UI, so that background
> work does not interrupt me or wait for a prompt I cannot attend.

A caller can prohibit interaction even when sign-in or unlocking state would be needed.
This goal is control over interaction, not guaranteed background success.

## Explicitly Request Selected-Account Interaction

> As a developer who explicitly wants an interactive sign-in path for a selected account,
> I want to request account selection even when reusable credentials are available,
> so that my request reaches the provider interactive path without an engine silent attempt.

This is deliberate caller intent rather than a side effect of switching repositories or
tools. It does not mean clearing shared authentication state, signing out other
applications, revoking prior tokens, or selecting a specific credential or MFA method.
Provider policy controls the authentication experience and normal protected-state updates.
It may reuse an existing session within the interactive call. This intent does not promise
visible UI or renewed credentials.

## Direct Protected-Service Access

> As a developer calling a protected development or test service, I want to obtain an
> access token for the intended resource using my authorized user identity, so that I
> can make permitted calls from local tools or scripts without implementing a separate
> sign-in flow in each caller.

User identity means delegated end-user identity, not a general promise of personal
Microsoft-account support. The caller supplies its explicit authentication intent;
service calls and use of the returned access token remain caller responsibilities.

## Authentication Integrated Into a Remote-Tool Workflow

> As a user of a tool client, I want its authentication integration to acquire and reuse
> credentials for the intended remote service under my selected user identity, so that I
> can use authorized remote capabilities without manually obtaining or copying access
> tokens.

MCP is an illustrative consumer protocol, not a protocol handled by the engine or a
selected proxy, interception, or injection mechanism. The integration calls the engine
for delegated authentication; connections and service-specific credential application
remain downstream. This goal makes no claim about a particular implementation.

## Upgrade the Engine Without Changing a Compatible Adapter

> As an adapter maintainer, I want users to upgrade the authentication engine without
> changing my adapter while its protocol major remains supported, so that authentication
> improvements do not force unrelated integration changes.

Protocol majors are distinct from the V2 product-generation name. This goal is not
indefinite support for old protocols or compatibility with official AzureAuth.

## Requirements and Validation Routes

These routes identify the behavioral and evidence authorities; they are not a second
set of acceptance criteria. Additional stories do not select profiles, mechanisms,
platforms, or new first-release scenario commitments. The primary journey remains the
first-release blocker.

| User goal | Capability requirements | Validation route |
| --- | --- | --- |
| Requested rather than default account | [Request and identity](requirements/request-identity-and-authority.md) (`V2-REQ-011`, `012`, `018`, `019`, `027`); [acquisition and validation](requirements/strategy-interaction-and-host.md) (`013`, `020`, `022`) | [Primary journey gate](../validation/strategy.md#primary-journey-gate) and [account-state matrix](../validation/strategy.md#account-state-matrix) |
| First-use OS sign-in reuse | [Reusable state](requirements/cache-security-and-operational-identity.md) (`V2-REQ-040`, `041`); [strict silent acquisition](requirements/strategy-interaction-and-host.md) (`013`, `020`, `022`) | [Account-state matrix](../validation/strategy.md#account-state-matrix) |
| Cross-ecosystem and cross-repository reuse | [Reusable state](requirements/cache-security-and-operational-identity.md) (`V2-REQ-040`, `041`); [engine boundary](requirements/product-boundary.md) (`002`) | [Cross-consumer reuse scenarios](../validation/strategy.md#cross-consumer-reuse-scenarios) |
| Background requests without UI | [Interaction and deadline](requirements/strategy-interaction-and-host.md) (`V2-REQ-014`, `015`, `021`, `025`); [typed outcomes](requirements/result-and-process-protocol.md) (`030`, `032`) | [Interaction matrix](../validation/strategy.md#interaction-matrix) |
| Explicit selected-account interaction | [Interaction intent and order](requirements/strategy-interaction-and-host.md) (`V2-REQ-013`, `014`, `016`, `020`, `022`, `023`); [state boundaries](requirements/cache-security-and-operational-identity.md) (`041`) | [Interaction matrix](../validation/strategy.md#interaction-matrix) and [Windows Slice design acceptance](../validation/strategy.md#windows-slice-design-acceptance) |
| Direct protected-service access | [Delegated public-client scope](requirements/product-boundary.md) (`V2-REQ-001`); [explicit target](requirements/request-identity-and-authority.md) (`010`, `011`, `012`, `018`, `019`, `027`); [validated opaque result](requirements/result-and-process-protocol.md) (`030`–`036`) | [Generic caller scenarios](../validation/strategy.md#generic-caller-scenarios) |
| Integrated remote-tool authentication | [Downstream boundary](requirements/product-boundary.md) (`V2-REQ-002`, `004`); [explicit request](requirements/request-identity-and-authority.md); [process result](requirements/result-and-process-protocol.md); [reuse](requirements/cache-security-and-operational-identity.md) (`041`) | [Generic caller scenarios](../validation/strategy.md#generic-caller-scenarios) and [cross-consumer reuse scenarios](../validation/strategy.md#cross-consumer-reuse-scenarios) |
| Engine updates with an unchanged adapter | [Supported protocol compatibility](requirements/result-and-process-protocol.md#v2-req-037-supported-protocol-compatibility) (`V2-REQ-037`) | [Contract tests](../validation/strategy.md#contract-tests) |
