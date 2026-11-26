# Vision

This record defines the intended product boundary for the v2 effort. It is directional,
not a frozen command-line or library contract or a release commitment.

## Problem

Microsoft Authentication CLI v1 successfully wraps MSAL mechanisms, but its public model
is optimized for a human asking for a token rather than a machine requiring a
deterministic authentication outcome.

A machine caller cannot reliably express or verify all of the following:

- the exact account that must be used;
- a strict tenant or authority postcondition;
- the order of silent and interactive acquisition stages;
- a guarantee that no interactive surface will be created;
- one deadline covering locks, broker work, browser work, and fallback;
- a structured distinction between interaction required, cancellation, denial,
  unavailability, timeout, and internal failure.

The v2 effort exists to make those properties explicit.

## Vision

Provide a small, deterministic command-line authentication engine for delegated
Microsoft Entra public-client token acquisition.

The intended direction is an engine that lets machine callers state and verify identity,
authority, interaction, deadline, and structured outcomes, with deterministic acquisition
order and self-contained interaction, while reusing maintained MSAL and platform-broker
integrations.

## Behavioral Authority

This vision does not define required product behavior. The capability records under
[`requirements/`](requirements/) are authoritative for request, identity, interaction,
result, process, cache, security, operational-identity, build, and validation behavior.
[`user-stories.md`](user-stories.md) owns the concrete user context and motivation.
Architecture and validation records determine how accepted requirements are realized and
supported.

## Intended Uses

- Local developer tools that require delegated Microsoft Entra access tokens.
- Credential-provider processes that need deterministic silent versus interactive
  behavior.
- Diagnostics and explicit login workflows that must report the selected identity.
- Public-client applications that need a versioned command-line boundary around MSAL.

## Non-Goals

Product-boundary requirements [`V2-REQ-001`](requirements/product-boundary.md#v2-req-001-delegated-public-client-scope)
and [`V2-REQ-002`](requirements/product-boundary.md#v2-req-002-no-implicit-product-expansion)
define the normative scope and expansion rule. Architecture decisions
[`0003`](../decisions/0003-treat-client-registration-as-an-external-dependency.md)
and [`0004`](../decisions/0004-keep-the-authentication-engine-separate-from-consumers.md)
define the current client-registration and consumer boundaries.

## Release Direction

A future supported release must satisfy the accepted product requirements and the
evidence obligations in [`../validation/strategy.md`](../validation/strategy.md). This
vision does not define a separate release gate or compatibility commitment.
