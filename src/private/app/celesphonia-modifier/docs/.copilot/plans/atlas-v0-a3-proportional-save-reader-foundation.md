# Atlas V0 A3 Proportional Save Snapshot and Lossless Reader Foundation

**Lifecycle:** Conditional: proposed governing plan before verified shared `R3R1`; active normative
after verified shared `R3R1`

**Increment:** A3R1 - Proportional Save Snapshot and Lossless Reader Foundation

**Decision owner:** Project leader

**Base:** Exact verified shared `G15`
`4b6db87ae46c43b6f1cb6f1310b2303d7e756cb6`

**Purpose:** Add a proportional, read-only save snapshot boundary and a lossless RPG Maker MV reader
foundation without restoring the superseded A2 protected-operation protocol or authorizing real-data
use.

> **No authority by presence**
> This file changes no active authority until its exact persisted candidate is independently reviewed,
> committed, pushed, and activated by verified shared `R3R1`. Git gates establish provenance only.
> They are never runtime authorization, and no gate authorizes a real snapshot operation.

## 1. Context and threat model

A2R14, A2R15, and released `G15` establish a maintained definition-only intake and block the old
save-oriented A3. A3R1 is a later independent editor-safety workflow. It does not reactivate any
superseded A2R14 save protocol or private-run authority.

Celesphonia Modifier is trusted, private, single-user local software. The accepted environment trusts
the local user, local administrator, repository checkout, installed runtime, and application binaries
selected by that user.

In scope are credible local correctness and data-safety failures:

- accidental writes, renames, deletes, or metadata mutations against original saves;
- using the wrong explicitly configured save root;
- writable-path escape or save/output overlap;
- partial, interrupted, or corrupt copies;
- a selected source changing while it is copied or the selected top-level set changing during a run;
- included-name collisions, unsupported entries, or reparse-backed roots;
- malformed or truncated compressed save data; and
- resource exhaustion caused by decompression, JSON parsing, or JsonEx graph construction.

Out of scope are adversarial actions by the trusted machine owner or administrator:

- malicious replacement of requests, receipts, snapshots, binaries, source, Git history, or runtime;
- runtime Git, branch, worktree, source-hash, or binary self-attestation;
- authorization ceremonies or signed operation grants;
- exact serializer bytes for request and receipt JSON;
- SHA-256 graphs between control documents;
- r1/r2 documents, inventories, backup protocols, or persistent protocol state machines; and
- protection against an administrator redirecting paths after validation.

Controls and review findings must be proportional to the accepted local threat model. A mechanism is
required only when it prevents or detects a credible in-scope correctness, original-data,
containment, copy-fidelity, recovery, or resource-bounding failure.

## 2. Relationship to prior plans

Before verified shared `R3R1`:

- A2R14 and A2R15 remain the active Atlas intake boundary;
- the old A3 section in `atlas-v0-execution-plan.md` remains blocked; and
- `save-semantic-atlas-plan.md` remains historical supporting material with no A3 execution authority.

After verified shared `R3R1`, this plan:

- supersedes only the old A3 execution section in `atlas-v0-execution-plan.md`;
- imports compatible historical format facts and open research questions from
  `save-semantic-atlas-plan.md` as supporting context, not authority;
- preserves every released A1, A2, and A2R15 command and contract unchanged; and
- authorizes only the repository-safe C3R1 implementation and synthetic validation defined here.

A3R1 does not alter the released definition-only intake. Snapshotting and decoding are separate
boundaries: snapshot code may read only explicitly supplied live save files, while codec and reader
code may operate only on caller-supplied copied bytes or streams.

## 3. Outcome and claim

A successful A3R1 snapshot may claim only that:

1. the caller supplied an explicit absolute save root;
2. the operation selected only supported immediate-child save names;
3. every selected source was opened read-only and remained stable while its bytes were copied;
4. the selected top-level included-file set reconciled before and after copying;
5. every destination was contained beneath the derived run workspace and matched the bytes read from
   its source;
6. the receipt semantically described the complete copied file set; and
7. the incomplete directory was promoted only after the snapshot validated.

The claim is a trusted-local, per-file copy claim. It is not a simultaneous filesystem snapshot,
proof against a malicious owner, semantic validation of save values, permission to edit saves, or
authority to write original data.

The reader foundation may claim only compatibility with the reviewed public and synthetic vectors,
bounded lossless representation of accepted inputs, and faithful JsonEx graph construction. It makes
no game-semantic claim and creates no editable save model.

## 4. Product surface and strict contracts

A3R1 adds one CLI command:

```text
save-snapshot <request-path>
```

The command accepts one explicit JSON request and performs the complete snapshot operation in one
invocation. It does not discover a save root from the current directory, environment, registry,
Steam, game installation, definition intake, network, or ambient workspace state.

A3R1 adds exactly two snapshot contracts:

- `atlas-save-snapshot-request/v1`; and
- `atlas-save-snapshot-receipt/v1`.

Their repository schemas are:

```text
docs/.copilot/schemas/atlas-v0/atlas-save-snapshot-request.schema.json
docs/.copilot/schemas/atlas-v0/atlas-save-snapshot-receipt.schema.json
```

The request contains only:

- `schemaVersion`;
- `repositoryRoot`;
- `runId`; and
- `saveRoot`.

`repositoryRoot` and `saveRoot` are explicit absolute paths. `runId` is exactly 32 lowercase
hexadecimal characters. The request contains no authorization, Git, binary, application, build,
definition, discovery, or alternate-output fields.

The receipt contains only:

- `schemaVersion`;
- `runId`;
- `saveRoot`;
- `finalSnapshotRoot`; and
- nonempty deterministic `entries`.

Each entry contains only:

- `sourceFileName`;
- `destinationRelativePath`;
- nonnegative integer `length`; and
- lowercase 64-hex-character `sha256`.

`sourceFileName` records the selected source leaf spelling observed for that run.
`destinationRelativePath` uses the canonical lowercase supported name. Entries are ordered by the
fixed canonical sequence `global.rpgsave`, `config.rpgsave`, then `file1.rpgsave` through
`file20.rpgsave`, omitting absent files.

Both contracts use strict JSON with a maximum document depth of 8:

- every property is required unless the schema explicitly models an array element;
- unknown and duplicate properties are rejected;
- explicit JSON `null` is rejected for every property and array element;
- nesting depth is bounded; and
- numeric and string constraints are validated before use.

The request is limited to 64 KiB and the receipt to 256 KiB of UTF-8 JSON. These byte limits are
checked while reading and before deserialization. Parsing also limits each string to 32,768 UTF-16
code units, numeric tokens to 20 ASCII characters, total JSON tokens to 512, and receipt entries to
the fixed maximum of 22 supported files. The parser rejects a limit breach before retaining an
unbounded document, string, number, token sequence, or array.

Validation is semantic. Whitespace, indentation, and property serialization style are not identity
or recovery boundaries.

## 5. Save selection and path boundaries

The operation enumerates only the immediate children of `saveRoot`; it never recurses.

Supported names are matched case-insensitively:

```text
global.rpgsave
config.rpgsave
file1.rpgsave
...
file20.rpgsave
```

Any nonempty subset is valid, including sparse slot-only sets. A root with no supported ordinary file
is refused as a likely wrong configured root.

Child names that do not match the supported set are ignored before metadata or content access. This
includes `.bak` files, `steam_autocloud.vdf`, directories, definitions, executables, unexpected files,
and all other names. The operation does not recurse into or inspect attributes, lengths, timestamps,
or contents for ignored children.

For a supported name, the operation rejects:

- two or more entries that collide case-insensitively;
- a directory, reparse point, symbolic link, junction, device, or unsupported filesystem type;
- an entry whose normalized path escapes `saveRoot`; or
- a source that cannot be opened as an ordinary read-only file.

Destination spelling is always the canonical lowercase supported name, regardless of source casing.
No source leaf name contributes a directory segment.

All writable paths are derived exactly beneath:

```text
<repositoryRoot>/src/private/app/celesphonia-modifier/.private/
  atlas-save-snapshot/<runId>
```

The fixed layout is:

```text
run workspace
  <repositoryRoot>/src/private/app/celesphonia-modifier/.private/
    atlas-save-snapshot/<runId>
incomplete snapshot root
  <runWorkspace>/save-snapshot.incomplete
final snapshot root
  <runWorkspace>/save-snapshot
receipt
  <candidateSnapshotRoot>/save-snapshot-receipt.json
```

The request cannot supply alternate writable paths. Path comparison uses platform path APIs and
case-insensitive comparison on Windows. Repository/output layout validation occurs before any mutation and rejects:

- a `repositoryRoot` that does not identify the expected checkout layout;
- save/output equality or either root containing the other;
- path escape from the fixed private snapshot parent;
- a reparse-backed repository root;
- any existing component from the repository root through the fixed
  application/private/snapshot parent, run workspace, or present incomplete/final root that is a
  reparse; and
- an existing output leaf of an unsupported type.

These targeted checks do not attest unrelated ancestors to the drive root. An output root is
operation-owned only when it is the exact expected non-reparse directory under the validated run
workspace. Missing fixed output directories may be created only beneath the validated repository
layout, one segment at a time, with containment and reparse status checked after creation. No Git
state is consulted.

Live-source preflight is required only when a new copy will start or restart. It then requires
`saveRoot` to be an existing absolute non-reparse directory and applies the overlap and supported-entry
rules above. Valid final or valid incomplete recovery does not probe, enumerate, or require the live
save root.

Original save files are opened with read access only and sharing that permits other readers but
denies concurrent write, rename, and delete access while that one source handle is open. A conflicting
existing writer causes safe refusal rather than a mixed-generation copy. This short per-file sharing
constraint is released when the copy finishes; it is not a persistent lock or authorization
mechanism. The operation performs no write, rename, delete, replacement, attribute change, timestamp
change, or other metadata mutation against `saveRoot` or any original entry.

## 6. Snapshot copy and reconciliation

The pre-copy selection is an in-memory value, not a persisted protocol document. For each supported
ordinary file it records the observed source leaf, canonical destination, length, and last-write
value needed for stability and reconciliation.

For each selected file, in deterministic canonical order, the operation:

1. opens the source read-only and keeps that handle open through the copy;
2. captures held-source length and last-write metadata;
3. creates a new canonical destination directly beneath the incomplete snapshot root;
4. streams source bytes to the destination while computing SHA-256;
5. flushes and closes the destination;
6. reopens the destination and independently verifies its length and SHA-256;
7. requires the verified destination to match the bytes and length read from the held source; and
8. requires held-source length and last-write metadata to remain stable before closing the source.

Destinations use create-new behavior. No selected source is decoded during snapshotting.

After all destinations validate, the operation repeats immediate-child name selection. It again
ignores unsupported names before metadata access, validates supported entry types and collisions, and
requires the post-copy supported set, source spellings, lengths, and last-write values to equal the
pre-copy selection. A mismatch fails the operation before receipt completion or promotion.

The operation then writes the receipt inside the incomplete root, validates the complete candidate
snapshot, and atomically promotes the incomplete directory to the final directory by an ordinary
same-parent directory rename. No original path participates in promotion.

## 7. Receipt validation and recovery

A candidate receipt is valid only when:

- its schema and strict JSON rules pass;
- its `runId` and `saveRoot` semantically equal the current request;
- `finalSnapshotRoot` equals the path derived from the current request and fixed layout;
- entries follow the deterministic canonical ordering and mapping;
- each recorded source name is a supported leaf and maps case-insensitively to its canonical
  destination;
- no source or destination appears more than once;
- the receipt records every file selected for that snapshot exactly once;
- every listed destination is an ordinary non-reparse file directly beneath the candidate root;
- no extra file or directory exists, except the one receipt file itself; and
- every destination length and SHA-256 matches the receipt.

For a previously finalized snapshot, the receipt-recorded selected set is the immutable copy plan.
Validation does not require live source bytes to remain unchanged after finalization and does not
reopen live originals when a valid final snapshot already exists.

Recovery is directory-based:

- **Final absent, incomplete absent:** start a new snapshot.
- **Valid final present:** return success without changing it.
- **Valid incomplete present, final absent:** promote it to final and return success.
- **Invalid cleanable incomplete present, final absent:** delete only its allowlisted ordinary files
  and exact root, then restart.
- **Final and incomplete both present:** refuse without modifying either.
- **Invalid final present:** refuse without modifying it.

A run parses and bounds the request, validates the repository/output layout, and evaluates final and
incomplete recovery before live-source preflight. Valid final validation uses only the canonical
request value, derived layout, receipt, and copied files. Valid incomplete validation and promotion
use the same evidence. Neither path requires `saveRoot` to still exist or retain its prior contents.
If an invalid cleanable incomplete root is eligible for restart, the operation completes current
live-source preflight before deleting it, then deletes only that exact root and starts the new copy.

A reparse or unsupported entry at an output-root path is not treated as owned and is refused
unchanged. An incomplete root is cleanable only when every child is an ordinary non-reparse file
whose leaf is one canonical supported save name or `save-snapshot-receipt.json`. Any extra file,
subdirectory, reparse, or unsupported entry causes refusal unchanged. Cleanup deletes the allowlisted
ordinary files individually and then removes the now-empty exact incomplete root nonrecursively; it
never performs recursive deletion. Recovery has no inventory replacement, receipt staging protocol,
r1/r2 state, backup engine, source-recapture prohibition marker, or persistent state machine.

Codec or reader failure after a snapshot is verified never invalidates, alters, renames, or deletes
that snapshot.

## 8. Lossless codec and reader foundation

C3R1 implements an independently written RPG Maker MV-compatible LZ-String Base64 codec. The
historical JavaScript helper remains non-executable historical reference. C3R1 neither authorizes nor
requires running it. Compatibility evidence uses reviewed public vectors, repository-safe synthetic
vectors, and independently derived format facts.

The accepted compressed-input grammar is the canonical `compressToBase64` form:

- bytes are nonempty ASCII with no BOM, whitespace, line breaks, or surrounding text;
- payload characters are only `A-Z`, `a-z`, `0-9`, `+`, and `/`;
- zero through three `=` characters may appear only as one trailing padding run;
- total character count is a multiple of four;
- padding count is exactly `(4 - (payloadLength % 4)) % 4`;
- the decompressor must reach the LZ-String end code without truncated code width or dictionary
  reference; and
- re-encoding the decompressed UTF-16 text must reproduce the exact accepted ASCII input.

Characters outside the alphabet, embedded padding, excess or missing padding, trailing characters,
whitespace, a missing end code, truncated codes, and noncanonical alternate encodings are classified
failures. The decoded text remains ordinary UTF-16 code units, including unpaired surrogates; later
JSON parsing decides whether the resulting text is valid JSON.

Exact encoded bytes are required only for vectors where the codec compatibility contract specifies
that exact output. Structural JSON equivalence is not substituted for a codec byte-compatibility
claim, and exact request or receipt JSON bytes are never required.

The reader retains:

- the exact original compressed bytes supplied by the caller;
- an ordered, source-preserving JSON representation;
- every object member occurrence in source order, including unknown members;
- scalar token spelling and lexemes rather than only normalized runtime values;
- sufficient token/span information to retain JsonEx marker spelling and wrapper structure; and
- the distinction between source JSON representation and resolved graph representation.

The JsonEx graph reader retains:

- `@c` identity definitions;
- `@` opaque class markers without runtime type activation;
- `@a` array wrappers;
- `@r` references;
- identity definitions versus reference occurrences;
- shared references; and
- cycles.

Unknown class names remain opaque data. The reader does not instantiate arbitrary classes or discard
unknown wrapper members.

Exact property names `@`, `@c`, `@a`, and `@r` are reserved in the JsonEx graph profile. Keys such as
`@x` or `@@` remain ordinary members. JsonEx marker grammar is:

- an identity or reference value is a canonical nonnegative decimal integer token
  (`0` or `[1-9][0-9]*`) within signed 32-bit range; fractions, exponents, signs, negative zero, strings,
  and other JSON types are invalid;
- a reference wrapper is an object with exactly one `@r` member and no other member;
- an array wrapper is an object with exactly one `@c` identity member and one `@a` member whose value
  is a JSON array, with no other member;
- an identity-bearing object has exactly one `@c`, may have exactly one `@` string class marker, may
  have any ordered ordinary members, and has neither `@a` nor `@r`;
- a class marker is a nonempty string and is invalid without `@c`; unknown class strings remain
  opaque metadata;
- a plain object or array has none of the four reserved marker names and remains an untracked ordinary
  container; and
- duplicate reserved markers, invalid marker types, marker coexistence outside these shapes, and
  marker-shaped objects with extra members are rejected.

Ordinary member occurrences, including duplicate ordinary names, remain ordered source/graph edges
rather than being collapsed into a dictionary. Graph construction first registers every `@c`
identity, rejecting duplicates, then resolves `@r` wrappers. References may point backward or forward
within the same document; an unresolved identity is dangling and invalid. An array wrapper registers
its materialized array before resolving its elements, so self-cycles, shared targets, and longer
cycles remain representable.

Token census and graph census are independent in-memory types:

- the token census counts JSON containers, member occurrences, array elements, scalar tokens, and
  each JsonEx marker occurrence before graph resolution; and
- the graph census counts materialized graph nodes, identity definitions, reference edges, shared
  targets, and cycles after resolution.

Reconciliation checks relate these types without making one an implementation alias of the other.
Neither census is a new persisted JSON contract in A3R1.

The default per-file bounded profile is:

| Limit                        | Default                 |
| ---------------------------- | ----------------------- |
| Encoded input                | 8 MiB                   |
| Decompressed text            | 32 Mi UTF-16 code units |
| JSON depth                   | 256                     |
| JSON tokens                  | 2,000,000               |
| One scalar token             | 8 Mi UTF-16 code units  |
| Materialized graph nodes     | 1,000,000               |
| JsonEx identity definitions  | 250,000                 |
| JsonEx reference occurrences | 500,000                 |

Counters are checked before disproportionate allocation or recursion. Limits are injectable so tests
can use lower values without changing production defaults. Cancellation is observed during input
read, decompression, tokenization, representation construction, graph resolution, census, and any
codec output operation.

Failures are classified at least as:

- malformed or truncated LZ-String Base64;
- invalid Base64 alphabet or padding behavior for the compatibility profile;
- decompressed-size limit;
- malformed JSON;
- JSON depth, token-count, or scalar-size limit;
- duplicate JsonEx identity;
- dangling JsonEx reference;
- invalid marker type;
- invalid array or reference wrapper;
- graph-node, identity-count, or reference-count limit;
- unsupported internal representation state; and
- cancellation.

A semantic no-op returns the exact original compressed bytes retained from the caller. It does not
decompress and re-encode merely to produce equivalent content.

The codec and reader accept only caller-supplied copied bytes or streams. They do not locate files,
open live originals, write decoded JSON, scan game semantics, create editable projections, write
saves, or expose scalar values in logs or diagnostics.

## 9. CLI and diagnostic behavior

`save-snapshot` uses ordinary process semantics:

- exit `0` after a valid final snapshot and receipt exist;
- use the existing invalid-argument result for malformed invocation;
- return cancellation when cancellation is requested; and
- return a nonzero result for validation, containment, I/O, copy, or recovery refusal.

Help lists the new command without changing existing command syntax. Diagnostics may identify a
fixed phase, supported leaf name, request field, or classified failure. They must not emit decoded
JSON, graph content, save scalar values, private paths, private hashes, receipt contents, or original
save bytes.

The reader foundation is a library boundary in C3R1. A3R1 adds no decode, census, scan, or edit CLI
command.

## 10. Scope and privacy exclusions

A3R1 excludes:

- any real-data run during planning, implementation, review, or release;
- ambient save discovery or a snapshot without an explicit absolute `saveRoot`;
- definition access, game-tree access, executable access, or ignored private content;
- A4 semantic scanning;
- A5 full or private corpus work;
- A6 source correlation;
- editable save models or projections;
- encoder-driven save write-back;
- original-data writes;
- backup, transaction, rollback, or restore engines;
- WinUI, databases, networking, telemetry, installers, packaging, or distribution; and
- a real snapshot operation as part of C3R1 or G3R1.

Only synthetic repository-safe save-shaped data may be used. Do not persist decoded JSON, graph
content, save scalar values, real save names beyond the fixed supported leaves, private paths, private
hashes, private receipts, or private snapshot contents in Git.

A later real snapshot requires an explicit user request that supplies the absolute save root. No
plan, review, release record, Git state, or application runtime state can infer or substitute for
that request.

## 11. Acceptance evidence

Focused repository-safe synthetic tests must cover:

### Snapshot selection and original-data safety

- immediate-child-only selection for every supported name and sparse slot sets;
- canonical lowercase destination mapping from mixed-case supported source names;
- exclusion of `.bak`, `steam_autocloud.vdf`, directories, definitions, executables, and unexpected
  names before metadata or content access;
- included-name case collisions;
- included directories, reparses, and unsupported entry types;
- save/output overlap, containment escape, wrong repository layout, and reparse-backed roots;
- source handles opened without write or delete access;
- no source write, rename, delete, attribute, timestamp, or other metadata mutation;
- held-source length or last-write change during copy;
- pre/post supported-file addition, removal, spelling, length, or last-write change;
- streaming digest, destination reopen, length, and hash fidelity;
- refusal when no supported ordinary file exists and success for sparse supported-file selections;
  and
- cancellation and I/O failure without promotion.

### Recovery and contracts

- neither-root start;
- valid-final idempotent success without live-source reopen;
- valid-incomplete promotion;
- invalid exact owned incomplete cleanup and restart;
- refusal without cleanup for an incomplete root containing any unexpected file, directory, reparse,
  or unsupported entry;
- refusal with both roots;
- refusal with invalid final;
- refusal rather than deletion for reparse or unsupported output roots;
- strict request and receipt required, unknown, duplicate, depth, type, range, and explicit-null
  validation;
- every request/layout/receipt binding mismatch;
- deterministic entry ordering and source-to-destination mapping;
- duplicate, missing, or extra receipt entries;
- missing, extra, nested, reparse, directory, corrupt, wrong-length, or wrong-hash snapshot entries;
- semantic receipt acceptance across insignificant serializer formatting differences; and
- decode or reader failure leaving a verified snapshot unchanged.

### Codec and lossless reader

- reviewed public and synthetic LZ-String Base64 vectors;
- exact encoded compatibility bytes where the codec contract requires them;
- empty, ASCII, Unicode, surrogate-pair, and large-but-in-limit payloads;
- malformed alphabet, malformed padding profile, malformed and truncated streams;
- canonical alphabet/padding grammar, missing end code, trailing data, and decode/re-encode
  canonicality;
- encoded-input and decompressed-size boundaries;
- cancellation during read, decompression, parse, graph resolution, census, and encode;
- JSON depth, token, scalar, graph-node, identity, and reference limits with injected lower test
  values;
- property order, repeated member occurrence, unknown member, and scalar lexeme preservation;
- integer, decimal, exponent, negative zero, Boolean, null, escaped-string, and Unicode token
  spellings;
- `@`, `@c`, `@a`, and `@r` markers;
- shared references, cycles, identity/reference distinction, and unknown opaque classes;
- duplicate identity, dangling reference, invalid marker, and invalid wrapper failures;
- duplicate reserved markers, marker coexistence, marker value types, reference-wrapper extra
  members, forward references, and marker-like ordinary names;
- independent token and graph census reconciliation; and
- semantic no-op returning the exact original compressed bytes.

### Integration and regression

- `save-snapshot` help, success, invalid-argument, cancellation, refusal, and failure exits;
- no scalar values or private paths/hashes in diagnostics;
- exactly the two A3R1 snapshot schemas;
- preservation of all existing A1, A2, and A2R15 commands and contracts; and
- preservation of all existing tests.

Validation runs the smallest relevant build, test, format, schema, CLI, and repository checks already
present. It never accesses a real save, definition, game installation, or ignored private workspace.

## 12. Review policy

Independent review remains required by `project-operating-model.md`, but findings are adjudicated
against the accepted proportional threat model.

A finding is a true positive only when it demonstrates a credible in-scope defect involving:

- original-save integrity;
- explicit-root selection or input/output containment;
- supported-file selection before metadata access;
- copy fidelity or source-change detection;
- interrupted-operation recovery;
- strict contract correctness;
- malformed-input containment or practical resource bounds;
- lossless representation or JsonEx graph correctness;
- cancellation;
- privacy-safe diagnostics; or
- maintainable local correctness and preservation of released behavior.

A finding based only on malicious owner/administrator substitution, runtime Git or binary
attestation, exact request/receipt JSON bytes, document SHA graphs, authorization ceremony, or more
persistent protocol state is out of scope unless it also demonstrates a credible accidental-failure
path. Prefer simplification or a narrower claim over additional protocols.

After two consecutive review rounds with structural findings, reset to the ideal minimal design
rather than continuing to harden the current shape. Once the complete candidate and dispositions
receive `No findings`, do not escalate review merely to seek additional ceremony.

## 13. Git gates

The A3R1 gates are release provenance only. The application never inspects these commits, records,
Git state, source hashes, or binary hashes at runtime.

### P3R1 - plan candidate

`P3R1` is the direct child of exact `G15`
`4b6db87ae46c43b6f1cb6f1310b2303d7e756cb6` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a3-proportional-save-reader-foundation.md
  atlas-v0-execution-plan.md
  save-semantic-atlas-plan.md
```

The exact four-document candidate receives independent holistic plan review until `No findings`, then
is committed and pushed unchanged.

### R3R1 - plan-review activation record

`R3R1` is the direct child of exact `P3R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a3-proportional-save-reader-foundation-plan-review.md
```

Verified shared `R3R1` activates this plan and authorizes only C3R1.

The record minimally binds:

- exact `G15`, `P3R1`, and `P3R1` tree identifiers;
- the exact reviewed four-path set;
- the governing plan Git blob and SHA-256;
- reviewer identifier and independence statement;
- every review iteration and atomic TP/FP disposition;
- repository-safe validation results; and
- final `No findings`.

### C3R1 - implementation candidate

`C3R1` is the direct child of exact `R3R1`. It contains only the minimum library, CLI, two snapshot
schemas, and automated-test changes required by this plan. It uses synthetic repository-safe data
only, preserves released commands/contracts, and contains no parallel authorization or state-machine
protocol.

The implementation candidate is committed and pushed before final independent review. Review covers
the complete exact candidate, acceptance evidence, safety/privacy boundaries, regression risk, and
handoff.

### G3R1 - release record

`G3R1` is the direct child of exact reviewed `C3R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a3-proportional-save-reader-foundation-release-gate.md
```

The record binds the exact candidate commit/tree, governing plan and R3R1 record, reviewed paths,
validation commands and outcomes, every finding and disposition, reviewer independence, and final
`No findings`. The staged record itself receives independent `No findings`, is committed unchanged,
and is pushed and verified as the shared tip.

`G3R1` records release provenance only. It grants no runtime authorization and no permission for a
real snapshot, private corpus access, decoded-data persistence, or original-save write.

## 14. Stop conditions and handoff

Stop and return to planning if implementation requires:

- live-save discovery without an explicit user-supplied absolute root;
- writes or metadata mutations against original saves;
- recursion below the save root;
- writes outside the exact derived snapshot workspace;
- a third A3R1 snapshot contract;
- persisted decoded data, graph content, census, or private receipt;
- an editable model, semantic scanner, writer, backup, transaction, or restore engine;
- running the historical JavaScript helper;
- runtime Git/binary attestation, authorization ceremony, or a persistent protocol state machine;
- real save, definition, game, or ignored private content; or
- a malicious-owner threat assumption.

Resume implementation only from verified shared `R3R1`. Implement the snapshot boundary first, then
the caller-supplied-byte codec and reader foundation, run synthetic validation, and obtain independent
review under section 12.

After verified shared `G3R1`, synthetic A4 planning may begin. A5/private-corpus work and every real
snapshot remain blocked until a later explicit user-authorized operation supplies the save root.
