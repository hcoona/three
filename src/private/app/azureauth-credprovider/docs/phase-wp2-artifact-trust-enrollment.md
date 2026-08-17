# WP2 — AzureAuth Provider and Binding Persistence

AzureAuth integration supports the Microsoft AzureAuth `0.9.5` release from
source commit `21258ff3a2cbb01d6891243114a55abe9ae3587e`.

## Provider configuration

The persisted provider configuration contains only:

- schema version;
- provider selection;
- AzureAuth version `0.9.5` when AzureAuth is selected.

Executable paths, hashes, signatures, certificate text, publishers, provenance,
ACL observations, and artifact identities are not persisted. The supported
installation path is derived from the selected version and the current Windows
user's `LocalApplicationData`.

## Binding

The binding contains:

- provider selection;
- optional account;
- required tenant for a bound provider;
- UTC recording timestamp.

An absent binding record is the unbound state.
Unbinding deletes the binding record with the same cooperative revision check
used for writes.

Account and tenant values are trimmed. Comparisons used for request hints are
case-insensitive. The account is a best-effort preference; it is not proof that
AzureAuth selected that account. Bindings are not coupled to executable hashes,
deployment keys, or one installed file instance.

No binding or provider record stores tokens, passwords, or other credentials.

## Identity configuration workflow

The product owns one identity configuration workflow:

```text
azureauth-credprovider identity configure --tenant <id> [--account <name>]
azureauth-credprovider identity reconfigure --tenant <id> [--account <name>]
azureauth-credprovider identity unconfigure
```

The CLI does not expose a generic provider extension point. It records the only
implemented identity path, AzureAuth `0.9.5`, together with the required tenant
and optional account preference.

`configure` creates missing state and is idempotent for identical state. It
requires `reconfigure` rather than silently replacing different or malformed
state. `reconfigure` creates, replaces, or repairs both records.
`unconfigure` idempotently deletes the binding first and then the provider
record. A concurrent update remains an explicit retryable conflict.

Configuration does not launch AzureAuth, acquire a token, or verify that the
account exists. It records operator intent needed because Git, NuGet, and Python
credential requests do not carry the Microsoft Entra tenant required by
AzureAuth.

## Persistence

Provider and binding records are bounded plain UTF-8 JSON beneath the normal
product configuration root:

- `$XDG_CONFIG_HOME/azureauth-credprovider`, or
- `$HOME/.config/azureauth-credprovider`, or
- Windows `LocalApplicationData`.

Writes use a same-process mutex, an ordinary cross-process file lock, a
same-directory temporary file, and atomic move/replace. Product-created
directories and files use owner-only Unix modes. Content hashes serve as
cooperative revisions so concurrent CLI updates can report conflicts.

The provider and binding are separate records, not a cross-file transaction.
If a concurrent command interrupts a multi-record change, the resulting partial
state fails closed and can be repaired by rerunning `identity reconfigure` or
`identity unconfigure`.

The store does not implement custom filesystem attestation, ancestor ownership
proofs, link policing, Linux `statx`, directory `fsync`, Base64 envelopes, or
ABA-safe generation tokens. Those mechanisms do not address the supported
threat model. Missing, present, malformed, success, and conflict are the normal
persistence outcomes; ordinary I/O failures remain explicit errors.

## Threat model

The product assumes a supported Windows, WSL2, or native Linux host, an
uncompromised OS and user account, and cooperative same-user commands. It
handles accidental missing or wrong versions, malformed configuration,
concurrent updates, secret redaction, and actionable operational errors. It
does not attempt to defend against root/Administrator, hostile same-user binary
replacement, malicious kernels/filesystems, or adversarial TOCTOU.
