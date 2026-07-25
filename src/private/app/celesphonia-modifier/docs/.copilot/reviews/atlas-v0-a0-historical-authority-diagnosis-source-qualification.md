# Atlas V0 A0 Historical Authority Diagnosis Source Qualification

**Lifecycle:** Proposed source-qualification evidence before verified shared `S0R5`

**Increment:** A0R5 - Historical Authority Diagnosis

**Outcome:** Exact source is qualified; one historical diagnosis remains blocked until this exact record
is independently reviewed, committed, pushed, and verified

**Final independent result:** `No findings`

**P0R5:** `d903cca066620b07f4ede0d0eda9804cce628ad1`

**R0R5:** `9f8abc31c336a7b782c1e2e523190b5d01117453`

**R0R5 tree:** `873ddecb1dd8adc94d6b50396c2c347c656a7db9`

**Governing plan:** `../plans/atlas-v0-a0-historical-authority-diagnosis.md`

**Plan-review record:** `atlas-v0-a0-historical-authority-diagnosis-plan-review.md`

**Next action:** `diagnose-once`

## 1. Exact derivation and protected workspace

Under exact clean shared `R0R5`, the fresh protected A0R5 workspace began with an empty `state`
directory and exactly the qualified A0R4 project and source bytes:

```text
initial renamed project
  ecfa6b2117fbbe0eda5d57f7968485eaef8f9a204a54950c7c43e59d6d120935
initial Program.cs
  4dfbb6a8813c3c24b11125a385a0bae3aaae164902962ba747c474a6850c5ea2
```

No A0R4 build output, source binding, locator, marker, receipt, or other runtime-artifact content was
copied or read. Implementation changed only the protected project and `Program.cs`; normal `bin` and
`obj` outputs were generated locally.

The final utility removes every A0R4 state filename and authority contract plus all runtime-locator,
current-tree metadata, save and definition enumeration, alias allocation, candidate construction,
codec replay, staging, and publication machinery. It retains only the CLI, fixed output, source
binding, Git, process capture, marker and receipt, strict historical parsing, manifest policy, and
synthetic-test machinery required by A0R5.

## 2. Exact qualified inputs

The final protected inputs are:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R5.csproj
  1ca7bef4b35025d2228f54d6521fe2d84466df27d2fcf1783545286154a91703
Program.cs
  9f8a812c131ee3c26a4cc6736571987687cbe698e10c2820ac4dac7f3b12becc
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
  9e67076bf21a004b8e05b6b4834c431dec2ed3ce0964094144775e14c32f18ef
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
  d30af90e604f2fc6807ba7b8092b37014060da2e8d4ed37fb4021dd317fa6410
source-bindings.json
  37dc2348c7983ebbc98120e9818a9b23c11c3256eb61750579ed6ec7f5b7f91a
```

The binding is 754 bytes of canonical single-line UTF-8 JSON without a BOM or trailing newline. It has
exact schema `atlas-a0r5-source-bindings/v1`, tool revision `atlas-a0r5/1`, exact `R0R5`, the four
reviewed relative names, and the four hashes above. It remains beside the project and outside `state`.

At qualification:

```text
state entries
  0
a0r5-historical-diagnostic-attempt.json
  absent
a0r5-historical-diagnostic-receipt.json
  absent
runtime locator
  not created
source-bindings.json
  present
tracked repository HEAD
  9f8abc31c336a7b782c1e2e523190b5d01117453
configured upstream
  9f8abc31c336a7b782c1e2e523190b5d01117453
```

## 3. Validation evidence

The exact final source and bound binaries passed:

- repository-pinned `dotnet format --verify-no-changes`;
- two consecutive complete Release Rebuilds with warnings as errors and zero warnings or errors;
- byte equality of both qualified assemblies across the two rebuilds;
- the complete embedded synthetic suite;
- exact stdout `test-passed`, exit code 0, and empty stderr;
- proof that protected `state` remained empty; and
- a final independent audit of the project, every `Program.cs` line, complete synthetic suite, both
  assemblies, exact binding, and all prior dispositions.

The final synthetic suite proves:

- exact two-mode CLI, literal fixed-output vocabulary, exit codes, newline bytes, and empty stderr;
- zero historical content, metadata, or enumeration access before the durable marker;
- complete, partial, and zero-byte marker consumption without retry-shaped output;
- absent marker after failed creation remains preflight-only;
- exact literal marker, receipt, binding, and source-authority canonical bytes;
- binding, Git, run-ID, current source, assembly, and loaded-runtime identity binding;
- no receipt-class output without one complete matching authoritative receipt;
- all eleven literal typed boundaries, exact order, and first-refusal mappings without message parsing;
- unexpected post-marker failure requires a matching durable internal-refusal receipt;
- strict fixed historical paths and zero protected-workspace enumeration;
- exact four-field anchor behavior with all other request members inert;
- strict released-manifest contract, digest binding, approval envelope, canonical bytes, reason codes,
  save classification, and definition first-match policy;
- exact per-channel filesystem access allowlists and zero locator, current-tree, source-content,
  candidate, A2, A3, or A0R4-state access;
- exact clean shared `S0R5`, direct-parent/path topology, committed record blob, and configured upstream;
- working-tree record substitution cannot influence committed authority;
- refusal before marker for missing, duplicate, malformed, stale, substituted, or mismatched authority;
- absent, behind, ahead, and unequal configured-upstream cases;
- text and binary process capture under both stdout-first and stderr-first pipe pressure; and
- drive-root and non-root containment preserve repository/workspace separation.

No historical private input, runtime locator, current-tree metadata, source content, A0R4 runtime state,
diagnostic marker, receipt, or result was accessed during implementation, validation, or review. No
A2, A3, candidate, production, or original-data operation occurred.

## 4. Independent review iterations

Every reviewer was independent of implementation and used GPT-5.6 Sol. Each review covered the complete
current project and source rather than only the most recent correction. Review input remained limited
to repository-safe authority, protected source, synthetic tests, generated binaries, and the final
binding.

| Candidate                | Reviewer                 | Result        |
| ------------------------ | ------------------------ | ------------- |
| Initial bound source     | `a0r5-source-reviewer`   | 4 TP          |
| Final exact bound source | `a0r5-source-rereviewer` | `No findings` |

The final reviewer verified the exact binding against actual bytes and re-audited every source and test
line, all four corrections and dispositions, authority boundary, fixed output, marker, receipt, Git
gate, historical pipeline, privacy rule, and exclusion.

## 5. TP/FP adjudication

All four findings were adjudicated TP. No finding was FP.

| #   | Finding                                                                 | Correction                                                                                            |
| --- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| 1   | Protocol tests derived outputs and mappings from production objects.    | Add literal output/exit tables and exact ordered boundary/result pairs independent of production.     |
| 2   | Canonical tests generated positive bytes with the serializer tested.    | Add hard-coded binding, authority, marker, and receipt UTF-8 vectors with exact byte comparisons.     |
| 3   | Access tests proved required reads but did not reject every extra read. | Enforce exact per-channel and aggregate access allowlists plus zero historical preflight access.      |
| 4   | Process-capture tests saturated only stderr.                            | Exercise text and binary capture under both stdout-first and stderr-first pipe pressure with timeout. |

None changes historical semantics, corpus policy, privacy, output vocabulary, authority, or the
`trusted-local-filesystem/v1` profile.

## 6. Source authority

The exact source authority is:

<!-- prettier-ignore-start -->
<!-- atlas-a0r5-source-authority:start -->
{"schema":"atlas-a0r5-source-authority/v1","r0r5":"9f8abc31c336a7b782c1e2e523190b5d01117453","sourceBindingsSha256":"37dc2348c7983ebbc98120e9818a9b23c11c3256eb61750579ed6ec7f5b7f91a","projectSha256":"1ca7bef4b35025d2228f54d6521fe2d84466df27d2fcf1783545286154a91703","programSha256":"9f8a812c131ee3c26a4cc6736571987687cbe698e10c2820ac4dac7f3b12becc","utilityAssemblySha256":"9e67076bf21a004b8e05b6b4834c431dec2ed3ce0964094144775e14c32f18ef","atlasAssemblySha256":"d30af90e604f2fc6807ba7b8092b37014060da2e8d4ed37fb4021dd317fa6410"}
<!-- atlas-a0r5-source-authority:end -->
<!-- prettier-ignore-end -->

This block grants no authority while it remains uncommitted, unreviewed, or not exact shared `S0R5`.

## 7. S0R5 release gate

The staged record must receive independent `No findings` and be committed unchanged as `S0R5`, the
direct child of exact `R0R5`. `R0R5..S0R5` must add only this record path, the committed blob must equal
the reviewed staged blob, and `HEAD` plus configured upstream must equal `S0R5`.

Only then may the operator invoke the exact qualified utility once with a fresh run ID. No runtime
locator is created. The utility must publish and reload the durable marker before reading either fixed
historical input.

Any preflight refusal before a marker path may be corrected with a fresh run ID. Any complete, partial,
or zero-byte marker consumes A0R5 diagnostic authority. No branch authorizes retry, source or authority
correction, runtime-locator or current-tree access, candidate publication, A2, A3, or original-data
write.
