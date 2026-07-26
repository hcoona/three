# Atlas V0 A0 Approved-Byte Authority Correction Source Qualification

**Lifecycle:** Proposed source-qualification evidence before verified shared `S0R6`

**Increment:** A0R6 - Approved-Byte Authority Correction

**Outcome:** Exact source is qualified; one corrected historical diagnosis remains blocked until this
exact record is independently reviewed, committed, pushed, and verified

**Final independent result:** `No findings`

**P0R6:** `67fd65cc11b3c5b4dad0901ee38133a9bfa4d885`

**R0R6:** `5f1f44a248f011633ddf4c1a28501a3b52de7bc4`

**R0R6 tree:** `e03e4aa70cc9a09a8fcae8ed4a168aa123eb1af2`

**Governing plan:** `../plans/atlas-v0-a0-approved-byte-authority-correction.md`

**Plan-review record:** `atlas-v0-a0-approved-byte-authority-correction-plan-review.md`

**Next action:** `diagnose-once`

## 1. Exact derivation and protected workspace

Under exact clean shared `R0R6`, the fresh protected A0R6 workspace began with an empty `state`
directory and exactly the qualified A0R5 project and source bytes:

```text
initial renamed project
  1ca7bef4b35025d2228f54d6521fe2d84466df27d2fcf1783545286154a91703
initial Program.cs
  9f8a812c131ee3c26a4cc6736571987687cbe698e10c2820ac4dac7f3b12becc
```

No A0R5 build output, source binding, marker, receipt, or other runtime-artifact content was copied.
Implementation changed only the protected project and `Program.cs`; normal `bin` and `obj` outputs were
generated locally.

The final utility removes every A0R5 state filename and authority identity, the historical
`ManifestCanonical` boundary and result, separate manifest reads, and historical serializer replay. It
retains one released manifest load whose exact returned bytes supply the anchor digest and whose
validated document supplies the retained approval and semantic checks. It also retains only the CLI,
fixed output, source binding, Git, process capture, marker and receipt, strict historical parsing,
manifest policy, and synthetic-test machinery required by A0R6.

## 2. Exact qualified inputs

The final protected inputs are:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R6.csproj
  bad6e08fad36f39a172d09e474cc805e1d5927e301500c1a64d3d8a9ab74bd95
Program.cs
  68d8b171157b3fa2c7bfd74057a86de9d9b92ef3f582f5f460cb70684817b6d9
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
  2f5b89b602420ba58b983f1a42d71a96f021ea3a9fd365b2f0f5a006f640decf
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
  8eb761ed6234c58b4482cb6decb2ab16c6a00965f14368375f8c2ad322f0ff1c
source-bindings.json
  066ada86facc3db99322a996c680bd06996b7797d2b4e461efbc087ced8f1ef3
```

The binding is 754 bytes of canonical single-line UTF-8 JSON without a BOM or trailing newline. It has
exact schema `atlas-a0r6-source-bindings/v1`, tool revision `atlas-a0r6/1`, exact `R0R6`, the four
reviewed relative names, and the four hashes above. It remains beside the project and outside `state`.

At qualification:

```text
state entries
  0
a0r6-approved-byte-attempt.json
  absent
a0r6-approved-byte-receipt.json
  absent
runtime locator
  not created
source-bindings.json
  present
tracked repository HEAD
  5f1f44a248f011633ddf4c1a28501a3b52de7bc4
configured upstream
  5f1f44a248f011633ddf4c1a28501a3b52de7bc4
```

## 3. Validation evidence

The exact final source and bound binaries passed:

- repository-pinned `dotnet format --verify-no-changes`;
- two consecutive complete Release Rebuilds with warnings as errors and zero warnings or errors;
- byte equality of both qualified assemblies across the two rebuilds;
- the complete embedded synthetic suite;
- exact stdout bytes `test-passed\n`, exit code 0, and empty stderr;
- proof that protected `state` remained empty; and
- a final independent audit of the project, every `Program.cs` line, complete synthetic suite, both
  assemblies, exact binding, and the released manifest reader.

The final synthetic suite proves:

- exact two-mode CLI, literal fixed-output vocabulary, exit codes, newline bytes, and empty stderr;
- zero historical content, metadata, or enumeration access before the durable marker;
- complete, partial, and zero-byte marker consumption without retry-shaped output;
- absent marker after failed creation remains preflight-only;
- exact literal marker, receipt, binding, and source-authority canonical bytes;
- binding, Git, run-ID, current source, assembly, and loaded-runtime identity binding;
- no receipt-class output without one complete matching authoritative receipt;
- all nine literal typed boundaries, exact order, and first-refusal mappings without message parsing;
- unexpected post-marker failure requires a matching durable internal-refusal receipt;
- strict fixed historical paths and zero protected-workspace enumeration;
- exact four-field request behavior with every other request member inert;
- one released manifest load supplies both the strictly validated document and exact digest buffer;
- a non-normal-form but strict, digest-bound, approved, policy-valid manifest completes;
- digest mismatch, malformed strict JSON, invalid envelope, reason code, save policy, and definition
  policy each refuse in the correct group;
- no historical serializer replay, byte rewrite, normalized copy, second manifest read, or fallback;
- exact per-channel filesystem access allowlists and zero locator, current-tree, source-content,
  candidate, A2, A3, A0R4, or A0R5 runtime-state access;
- exact clean shared `S0R6`, direct-parent/path topology, committed record blob, and configured upstream;
- working-tree record substitution cannot influence committed authority;
- refusal before marker for missing, duplicate, malformed, stale, substituted, or mismatched authority;
- absent, behind, ahead, and unequal configured-upstream cases;
- text and binary process capture under both stdout-first and stderr-first pipe pressure; and
- drive-root and non-root containment preserve repository/workspace separation.

No historical private input, runtime locator, current-tree metadata, source content, A0R5 runtime state,
diagnostic marker, receipt, or result was accessed during implementation, validation, or review. No A2,
A3, candidate, production, or original-data operation occurred.

## 4. Independent source review

The reviewer was independent of implementation and used GPT-5.6 Sol. The review covered the complete
exact project and source rather than only the authority correction. Review input remained limited to
repository-safe authority, protected source, synthetic tests, generated binaries, the released Atlas
reader, and the final binding.

| Candidate                | Reviewer               | Result        |
| ------------------------ | ---------------------- | ------------- |
| Final exact bound source | `a0r6-source-reviewer` | `No findings` |

The reviewer independently reproduced the candidate identities and re-audited the one-read same-buffer
authority model, retained strict contract and policies, deletion completeness, boundary order and
mapping, fixed output, marker and receipt durability, Git gate, access allowlists, test independence,
privacy rule, and exclusions.

## 5. TP/FP adjudication

The independent review reported no findings. There are zero TP and zero FP dispositions.

## 6. Source authority

The exact source authority is:

<!-- prettier-ignore-start -->
<!-- atlas-a0r6-source-authority:start -->
{"schema":"atlas-a0r6-source-authority/v1","r0r6":"5f1f44a248f011633ddf4c1a28501a3b52de7bc4","sourceBindingsSha256":"066ada86facc3db99322a996c680bd06996b7797d2b4e461efbc087ced8f1ef3","projectSha256":"bad6e08fad36f39a172d09e474cc805e1d5927e301500c1a64d3d8a9ab74bd95","programSha256":"68d8b171157b3fa2c7bfd74057a86de9d9b92ef3f582f5f460cb70684817b6d9","utilityAssemblySha256":"2f5b89b602420ba58b983f1a42d71a96f021ea3a9fd365b2f0f5a006f640decf","atlasAssemblySha256":"8eb761ed6234c58b4482cb6decb2ab16c6a00965f14368375f8c2ad322f0ff1c"}
<!-- atlas-a0r6-source-authority:end -->
<!-- prettier-ignore-end -->

This block grants no authority while it remains uncommitted, unreviewed, or not exact shared `S0R6`.

## 7. S0R6 release gate

The staged record must receive independent `No findings` and be committed unchanged as `S0R6`, the
direct child of exact `R0R6`. `R0R6..S0R6` must add only this record path, the committed blob must equal
the reviewed staged blob, and `HEAD` plus configured upstream must equal `S0R6`.

Only then may the operator invoke the exact qualified utility once with a fresh run ID. No runtime
locator is created. The utility must publish and reload the durable marker before reading either fixed
historical input.

Any preflight refusal before a marker path may be corrected with a fresh run ID. Any complete, partial,
or zero-byte marker consumes A0R6 diagnostic authority. No branch authorizes retry, source or authority
correction, runtime-locator or current-tree access, candidate publication, A2, A3, or original-data
write.
