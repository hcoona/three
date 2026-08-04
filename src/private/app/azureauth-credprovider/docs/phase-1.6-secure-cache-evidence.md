# Phase 1.6 Secure-Cache Behavior Evidence Gate

Status: **Superseded blocked historical decision**

Date: **2026-06-05**

The accepted `phase-1r-secure-cache-rescope.md` decision supersedes this
blocker's effect on MVP implementation. Product-owned persistent derived
credential caching is deferred and disabled by default. Current provider-cache
policy, including the bounded native Linux headless AzureAuth exception, is
documented by Phase 1R and the WP3/WP6 implementation records. The evidence
below remains as decision history and does not describe the current runtime
architecture.

Decision ID: **phase-1.6-secure-cache-evidence**

Gate name: **Phase 1.6 Secure-cache behavior gate**

Owner: **ID**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                                                                                                                                                                      |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Gate status                | Blocked for full target-platform secure-store validation. The local Linux environment allowed only non-mutating capability and failure-mode probes. Windows, macOS, and Linux write/read/delete secure-store behavior were not available as safe evidence in this local-run-first session.                                    |
| Decision                   | Lock the product policy, not the platform implementation: persistent credential cache must fail closed when an approved secure store is unavailable and must not silently fall back to plaintext storage. Do not lock default persistent cache enablement for any platform from this record alone.                            |
| Evidence scope             | Source inspection covers existing Microsoft credential-store patterns and known fallback risks. Local probes cover Ubuntu 24.04-era Linux host capability signals only, without writing secrets or fake credentials to the operator secure store.                                                                             |
| Implementation may proceed | No dependent Phase 2 or Phase 6 secure-cache contracts, tests, defaults, or implementation may proceed from this blocked record. While blocked, only the policy statement, evidence tracking, and non-persistent planning may be carried forward; no locked persistent-cache defaults or platform support claims are allowed. |
| Phase 1R routing           | Required unless the missing platform write/read/delete evidence closes before dependent secure-cache work starts. Phase 2+ secure-cache work must wait for Phase 1R acceptance of defer, remove, or resequence, or for the missing evidence to close.                                                                         |

## Decision Record Format Compliance

| Field                      | Value                                                                                                                                                       |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Decision ID                | `phase-1.6-secure-cache-evidence`                                                                                                                           |
| Gate name                  | Phase 1.6 Secure-cache behavior gate                                                                                                                        |
| Owner                      | ID                                                                                                                                                          |
| Date                       | 2026-06-05                                                                                                                                                  |
| Status                     | Blocked; local Linux no-write subset accepted                                                                                                               |
| Evidence links             | This document, source references below, and local probe transcript below.                                                                                   |
| Decision                   | No silent plaintext fallback is accepted as a hard policy. Persistent cache behavior is not locked for target platforms.                                    |
| Affected requirements      | `requirements.md`, `phase-0-decisions.md`, `phase-1.2-azureauth-suitability.md`, `high-level-design.md`, `mid-level-design.md`, and `project-breakdown.md`. |
| Follow-up actions          | Platform secure-store write/read/delete probes and Phase 1R or resequencing if those probes cannot run before any dependent Phase 2+ secure-cache work.     |
| Implementation may proceed | Policy statement, evidence tracking, and non-persistent planning only. Phase 2/6 secure-cache contracts, tests, implementations, and defaults are blocked.  |

## Product Policy Accepted by This Gate

The secure-cache policy is accepted because it is directly required by the
product requirements and design baseline:

1. Credential cache access is owned by the shared credential core.
2. Cache entries must be partitioned by ecosystem, resource identity, account,
   tenant, audience, and credential kind.
3. Secrets must not be logged or written to repository-local files by default.
4. A secure-store outage, missing platform capability, locked keychain, missing
   Secret Service session, unsupported DPAPI path, or cache verification failure
   is a hard `CacheUnavailable` failure for persistent writes.
5. The product must not silently write plaintext credential material as a
   fallback. Plaintext persistence may be added only as a separate explicit,
   user-visible, non-default mode after a later approved security decision.

This policy intentionally differs from some existing reference tools that permit
plaintext or unprotected-file fallback in specific modes. Those references are
useful evidence for failure modes, not accepted behavior for this product.

## Source References Inspected

### Repository Design Baseline

- `project-breakdown.md` lines 81-90 defines Phase 1.6 as mandatory before
  persistent cache behavior locks.
- `phase-0-decisions.md` lines 55-71 keeps Windows first-class and Linux/macOS
  as release validation targets while recognizing local-run-first evidence in
  the current workflow.
- `requirements.md` lines 24-31 assigns secure token cache coordination to the
  product boundary; lines 63-75 require cross-platform support, centralized token
  handling, redaction, cache partitioning, and no default repository-local
  credential writes.
- `high-level-design.md` lines 76-91 assigns secure credential cache access,
  cache partitioning, redaction, and policy enforcement to the shared core; lines
  216-231 define the cache-key model.
- `mid-level-design.md` lines 124-134 requires sensitive credential cache state
  to use platform-appropriate secure storage or an explicitly configured secure
  cache; lines 840-858 centralize token persistence and redaction; lines 887-900
  defines `CacheUnavailable` as fail-closed with no silent plaintext fallback;
  lines 902-919 require Windows, Linux, and macOS secure-storage coverage; lines
  961-976 list secure-cache availability as a required prototype gate.

### Reference Source Snapshot

Commands used to identify local reference snapshots:

```bash
git -C /workspace/public/git-credential-manager --no-pager rev-parse HEAD
git -C /workspace/public/git-credential-manager --no-pager describe --tags --always --dirty
git -C /workspace/public/artifacts-credprovider --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-credprovider --no-pager describe --tags --always --dirty
git -C /workspace/public/microsoft-authentication-cli --no-pager rev-parse HEAD
git -C /workspace/public/microsoft-authentication-cli --no-pager describe --tags --always --dirty
```

Results:

```text
git-credential-manager: 312354b884aca75efb078bedccf033df97fabb1f, v2.8.0-7-g312354b
artifacts-credprovider: 9c3840be1c97594708331b1797b0a2d9dce480b3, v2.0.1-9-g9c3840b
microsoft-authentication-cli: de20930c34b3b86c8a0ed7bbdeeca3f662dae918, 0.9.6-3-gde20930
status --short: no output for all three reference working trees
```

Auditable source-inspection commands used for the observations below:

```bash
awk 'NR >= 54 && NR <= 178 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/git-credential-manager/src/shared/Core/CredentialStore.cs
awk 'NR >= 180 && NR <= 255 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/git-credential-manager/src/shared/Core/CredentialStore.cs
awk 'NR >= 36 && NR <= 128 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/git-credential-manager/src/shared/Core/PlaintextCredentialStore.cs
awk 'NR >= 99 && NR <= 278 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/git-credential-manager/src/shared/Core/Interop/MacOS/MacOSKeychain.cs
awk 'NR >= 211 && NR <= 221 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/artifacts-credprovider/CredentialProvider.Microsoft/Util/SessionTokenCache.cs
awk 'NR >= 32 && NR <= 67 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/artifacts-credprovider/CredentialProvider.Microsoft/Util/EncryptedFileWithPermissions.cs
awk 'NR >= 72 && NR <= 155 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/microsoft-authentication-cli/src/MSALWrapper/PCACache.cs
awk 'NR >= 257 && NR <= 275 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/microsoft-authentication-cli/src/AzureAuth/Commands/Ado/CommandPat.cs
awk 'NR >= 31 && NR <= 58 { printf "%6d\t%s\n", NR, $0 }' \
  /workspace/public/microsoft-authentication-cli/src/AdoPat/PatCache.cs
```

Source observations:

- Git Credential Manager `CredentialStore` selects the backing store in
  `/workspace/public/git-credential-manager/src/shared/Core/CredentialStore.cs`
  lines 61-105, defaults to Windows Credential Manager on Windows, macOS
  Keychain on macOS, and no default store elsewhere in lines 126-135, and lists
  Linux Secret Service, GNU `pass`, Git in-memory cache, plaintext files, and
  disabled storage as explicit options in lines 138-178.
- Git Credential Manager validates Windows Credential Manager persistence before
  use in
  `/workspace/public/git-credential-manager/src/shared/Core/CredentialStore.cs`
  lines 180-199 and validates Linux Secret Service only when a graphical desktop
  session is present in lines 236-255.
- Git Credential Manager includes a plaintext credential-store implementation in
  `/workspace/public/git-credential-manager/src/shared/Core/PlaintextCredentialStore.cs`
  lines 36-128 and labels plaintext files as insecure in
  `/workspace/public/git-credential-manager/src/shared/Core/CredentialStore.cs`
  lines 173-174. This project must not adopt plaintext as an implicit fallback.
- Git Credential Manager macOS Keychain implementation uses Security Framework
  generic-password APIs for lookup, add/update, and delete in
  `/workspace/public/git-credential-manager/src/shared/Core/Interop/MacOS/MacOSKeychain.cs`
  lines 99-278.
- Azure Artifacts Credential Provider `SessionTokenCache` calls
  `EncryptedFileWithPermissions.ReadFileBytes(..., readUnencrypted: true)` and
  `WriteFileBytes(..., writeUnencrypted: true)` in
  `/workspace/public/artifacts-credprovider/CredentialProvider.Microsoft/Util/SessionTokenCache.cs`
  lines 211-221. `EncryptedFileWithPermissions` falls back to raw file bytes on
  `NotSupportedException` when those flags are enabled in
  `/workspace/public/artifacts-credprovider/CredentialProvider.Microsoft/Util/EncryptedFileWithPermissions.cs`
  lines 32-67. This is negative evidence for this product no-plaintext-fallback
  policy.
- AzureAuth `PCACache` builds MSAL persistence with Linux keyring and macOS
  Keychain settings, verifies persistence, and then attempts plaintext fallback
  on headless Linux in
  `/workspace/public/microsoft-authentication-cli/src/MSALWrapper/PCACache.cs`
  lines 72-155. This is also negative evidence for this product policy.
- AzureAuth ADO PAT cache builds storage with macOS Keychain and Linux keyring
  settings in
  `/workspace/public/microsoft-authentication-cli/src/AzureAuth/Commands/Ado/CommandPat.cs`
  lines 257-275 and `PatCache` writes through the provided storage wrapper in
  `/workspace/public/microsoft-authentication-cli/src/AdoPat/PatCache.cs` lines
  31-58, but this record did not execute PAT cache writes.

## Platform Secure-Store Status

| Platform target | Evidence status                                                                           | Secure-store behavior conclusion                                                                                                                                                                    | Failure-mode conclusion                                                                                                                         | Gate effect                                                          |
| --------------- | ----------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| Windows         | Source inspection only. No Windows host was available.                                    | Windows Credential Manager and DPAPI are credible candidate backends from reference source, but this record does not prove product behavior.                                                        | Network or non-persistent Windows sessions, DPAPI failures, and Credential Manager persistence failures must become `CacheUnavailable`.         | Blocked for persistent default lock.                                 |
| macOS           | Source inspection only. No macOS host was available.                                      | macOS Keychain is a credible candidate backend from reference source, but this record does not prove product behavior.                                                                              | Locked keychain, denied keychain access, missing login keychain, or Security Framework errors must become `CacheUnavailable`.                   | Blocked for persistent default lock.                                 |
| Linux           | Local non-mutating probe plus source inspection. No safe write/read/delete probe was run. | Secret Service tooling is installed locally and a lookup for a synthetic missing item returned without stderr, but that is not proof of write/read/delete persistence. GNU `pass` is not installed. | Headless or locked Secret Service, missing DBus session, missing `pass`, missing GPG identity, or denied unlock must become `CacheUnavailable`. | Blocked for persistent default lock; local no-write subset accepted. |

## Local Linux No-Write Probe

The local host was Linux. The probe intentionally avoided writing even fake
credential material to the operator secure store.

Command:

```bash
set -u
printf 'os='; uname -a
printf 'python='; python3 --version 2>&1 || true
printf 'dotnet='; dotnet --version 2>&1 || true
printf 'secret-tool='; command -v secret-tool || true
printf 'gpg='; command -v gpg || true
printf 'pass='; command -v pass || true
printenv DISPLAY || true
printenv WAYLAND_DISPLAY || true
printenv DBUS_SESSION_BUS_ADDRESS || true
python3 - <<'PY'
import importlib.util
mods = ['keyring', 'secretstorage']
for name in mods:
    spec = importlib.util.find_spec(name)
    print(f'{name}_module={spec.origin if spec else "<missing>"}')
if importlib.util.find_spec('keyring'):
    import keyring
    kr = keyring.get_keyring()
    print(f'keyring_default={kr.__class__.__module__}.{kr.__class__.__name__}')
    print(f'keyring_priority={getattr(kr, "priority", "<missing>")}')
PY
```

Results:

```text
os=Linux TDC3072617042 6.17.0-1015-azure #15~24.04.1-Ubuntu SMP Wed May  6 22:37:49 UTC 2026 x86_64 x86_64 x86_64 GNU/Linux
python=Python 3.14.3
dotnet=10.0.300
secret-tool=/usr/bin/secret-tool
gpg=/usr/bin/gpg
pass=
DISPLAY=
WAYLAND_DISPLAY=
DBUS_SESSION_BUS_ADDRESS=unix:path=/tmp/dbus-9fTbINyfBS,guid=b616116b8e2238ee7682cdfa6a18f02d
keyring_module=<missing>
secretstorage_module=<missing>
```

Non-mutating Secret Service lookup probe:

```bash
set -u
status=0
secret-tool lookup service com.example.azureauth-credprovider.phase16 \
  account probe >/dev/null 2> .copilot-secret-tool-stderr || status=$?
printf 'secret_tool_lookup_status=%s\n' "$status"
printf 'secret_tool_lookup_stderr<<EOF\n'
cat .copilot-secret-tool-stderr
printf 'EOF\n'
rm -f .copilot-secret-tool-stderr
```

Results:

```text
secret_tool_lookup_status=1
secret_tool_lookup_stderr<<EOF
EOF
```

Interpretation:

- `secret-tool` and `gpg` are present, but `pass` is absent.
- No graphical display variables are set. A DBus session variable is present, but
  this alone does not prove an unlocked Secret Service collection.
- The Python `keyring` and `secretstorage` modules are not installed in the
  probed Python environment.
- The `secret-tool lookup` command produced no diagnostic error for a synthetic
  missing item, but exit status 1 is still insufficient to prove secure-store
  write/read/delete behavior.
- Because the probe did not write, it cannot close Linux persistent-cache
  behavior. It only supports doctor-style capability reporting and failure-mode
  planning.

## Failure Modes Required by the Decision

| Failure mode                                            | Required product behavior                                                                                                                              |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Secure store unavailable or unsupported                 | Return `CacheUnavailable`; do not create plaintext files.                                                                                              |
| Secure store locked, denied, or requires unavailable UI | Return `CacheUnavailable` or `InteractionBlocked` according to caller policy; do not prompt from protocol adapters unless the host protocol allows it. |
| Linux headless session without approved secure backend  | Fail closed for persistent writes. CI may use non-persistent credentials or configuration-manager-owned temporary files only when explicitly selected. |
| Cache verification fails after opening backend          | Treat as `CacheUnavailable`; do not downgrade to plaintext.                                                                                            |
| Cache entry corrupt or undecryptable                    | Delete only product-owned corrupt entries when safe and explicit; otherwise return a redacted hard failure.                                            |
| Concurrent cache lock timeout or cancellation           | Return a typed failure without partial writes or plaintext fallback.                                                                                   |
| User explicitly disables persistent cache               | Use non-persistent behavior and return no cached credential rather than writing plaintext.                                                             |
| Unsupported host or cache key mismatch                  | Return no-credential behavior; never reuse a less-specific cached token.                                                                               |

## Persistent Cache Lock Implications

Because this gate is blocked, it does not unlock dependent Phase 2 or Phase 6
secure-cache contracts, tests, implementations, or defaults. Those activities
must not proceed until either:

1. Missing Windows, macOS, and Linux write/read/delete evidence closes this gate;
   or
2. Phase 1R explicitly accepts defer, remove, or resequence for the dependent
   secure-cache work.

Allowed while blocked:

1. Preserve the policy statement that silent plaintext fallback is unacceptable.
2. Track evidence gaps, probe plans, and Phase 1R routing.
3. Plan non-persistent MVP behavior and documentation without creating locked
   secure-cache contracts or source changes.

The following work remains blocked:

1. Defining or implementing Phase 2/6 secure-cache contracts, typed
   `CacheUnavailable` APIs, fakes, or tests.
2. Selecting Windows Credential Manager versus DPAPI as the Windows default.
3. Selecting macOS Keychain item attributes, access groups, and unlock behavior.
4. Selecting Linux default behavior among Secret Service, GNU `pass`, disabled
   persistence, or explicit user-selected secure backend.
5. Enabling persistent credential writes by default on any platform.
6. Claiming release support for persistent cache behavior on Windows, macOS, or
   Linux.

## Phase 1.2 Interconnection Traceability

Phase 1.2 follow-up 4 required Phase 1.6 to decide final platform store behavior
and confirm no plaintext fallback for this product. This record only partially
satisfies that follow-up:

1. Satisfied: no silent plaintext fallback is accepted as product policy.
2. Open and blocked: final Windows, macOS, and Linux platform-store behavior,
   defaults, and support claims remain undecided until write/read/delete evidence
   closes this gate or Phase 1R accepts defer, remove, or resequence.

Owner and dependency impact:

1. ID remains accountable for carrying the open Phase 1.2 secure-cache follow-up
   into Phase 1R or closure evidence.
2. ARCH and ID must not lock Phase 6 shared-core/direct-MSAL secure-cache
   contracts, APIs, fakes, tests, or defaults from this record alone.
3. Phase 6 direct-MSAL work may plan only non-persistent behavior and
   fake-provider seams until platform-store behavior is accepted.

## Affected Requirements and Designs

| Source                               | Effect                                                                                                                                                       |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `requirements.md`                    | Secure token cache coordination and cache partitioning remain required. No default repository-local credential writes and secret redaction remain mandatory. |
| `phase-0-decisions.md`               | The Windows-first and Linux/macOS release-target baseline remains unchanged. This record does not supply unavailable remote platform evidence.               |
| `phase-1.2-azureauth-suitability.md` | Follow-up 4 is only partially satisfied: no-plaintext fallback policy is accepted, but final platform-store behavior and defaults remain blocked/open.       |
| `high-level-design.md`               | Shared core remains the owner of secure credential cache access and cache partitioning.                                                                      |
| `mid-level-design.md`                | `CacheUnavailable` fail-closed behavior is accepted as policy. Secure-cache availability remains an open prototype risk for persistent defaults.             |
| `project-breakdown.md`               | Phase 1.6 records a blocked decision before persistent cache behavior is locked. Mandatory failure must enter Phase 1R unless evidence closes first.         |

## Follow-Ups

| Owner           | Follow-up                                                                                                                                                                                                                                       | Dependency effect                                                                 |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| ID and PLATFORM | Run Windows write/read/delete/remove probes with fake credential values against Windows Credential Manager and, if considered, DPAPI-protected files. Include unavailable-store and denied-persistence cases.                                   | Required before Windows persistent cache default is selected or claimed.          |
| ID and PLATFORM | Run macOS Keychain write/read/delete/remove probes with fake credential values. Include locked keychain, denied access, and non-interactive host-tool contexts.                                                                                 | Required before macOS persistent cache default is selected or claimed.            |
| ID and PLATFORM | Run Linux Secret Service and GNU `pass` probes with fake credential values in desktop, headless, missing-backend, locked-backend, and CI-like environments.                                                                                     | Required before Linux persistent cache default is selected or claimed.            |
| ID and ARCH     | Hold secure-cache contracts and tests until missing evidence closes or Phase 1R accepts defer, remove, or resequence; then ensure plaintext fallback is impossible unless a future explicit security decision enables a named non-default mode. | Blocks Phase 2 and Phase 6 secure-cache work.                                     |
| ID and ARCH     | Keep Phase 1.2 follow-up 4 open as partially satisfied until platform-store behavior and defaults are accepted; reflect the blocked state in shared-core and direct-MSAL Phase 6 planning.                                                      | Blocks Phase 6 direct-MSAL/shared-core persistent-cache defaults and contracts.   |
| CONFIG          | Ensure any credential-bearing configuration-file writes remain explicit policy decisions and do not masquerade as secure-cache fallback.                                                                                                        | Required before npm, Yarn, NuGet, or CI temporary credential writes.              |
| QA              | Add redaction and filesystem scans that fail if persistent credential-cache tests create plaintext token files by default.                                                                                                                      | Required before hardening and release acceptance.                                 |
| PL              | Enter Phase 1R if the product needs to proceed without full target-platform secure-store evidence.                                                                                                                                              | Required to narrow MVP to non-persistent cache or resequence platform validation. |

## Validation

Documentation-only change. Validation commands run after editing:

```bash
pnpm exec -- prettier --check -- src/private/app/azureauth-credprovider/docs/phase-1.6-secure-cache-evidence.md
pnpm exec -- markdownlint-cli2 -- src/private/app/azureauth-credprovider/docs/phase-1.6-secure-cache-evidence.md
```

Results:

```text
prettier --check: passed
markdownlint-cli2: 0 errors
```

## Residual Risks

- Linux local evidence is intentionally non-mutating and does not prove secure
  persistence.
- Windows and macOS target evidence is absent in this local-run-first workflow.
- Reference tools contain plaintext or unprotected-file fallback paths; product
  implementation must actively prevent copying those behaviors by accident.
- CI and headless Linux users may expect persistent caching; MVP documentation and
  doctor output must state when persistence is unavailable and why the product
  fails closed.
- A future explicit plaintext mode, if ever approved, would need separate threat
  modeling, user-visible warnings, opt-in UX, cleanup behavior, and tests proving
  it is never selected silently.
