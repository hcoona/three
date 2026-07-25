# Atlas V0 A0 No-Candidate Stage Diagnosis Source Qualification

**Lifecycle:** Proposed source-qualification evidence before verified shared `S0R4`

**Increment:** A0R4 - No-Candidate Stage Diagnosis

**Outcome:** Exact source is qualified; locator materialization and one diagnostic remain blocked until
this exact record is independently reviewed, committed, pushed, and verified

**Final independent result:** `No findings`

**P0R4:** `24602b10d621ee6d0acd7658ba71d4fd2c2bed6d`

**R0R4:** `53c03b5de96c5208bc3d68cc3ff098ed50ce9ff4`

**R0R4 tree:** `537536cd5379468b77120a69c59eb5aa7e843746`

**Governing plan:** `../plans/atlas-v0-a0-no-candidate-stage-diagnosis.md`

**Plan-review record:** `atlas-v0-a0-no-candidate-stage-diagnosis-plan-review.md`

**Next action:** `diagnose-once`

## 1. Exact derivation and protected workspace

Under exact clean shared `R0R4`, the fresh protected A0R4 workspace began with an empty `state`
directory and exactly the qualified A0R3 project and source bytes:

```text
initial renamed project
  75b2e6ddbfdabdf8103bfc39c70eb4ff9b21f89d1b386f723d631d0ff67b764b
initial Program.cs
  9ac6f4292cd52376b25cb7d5330a31aa5428b51391c12e8cf14e12d7c400097d
```

No A0R3 build output, source binding, runtime locator, marker, candidate, staging path, or other runtime
state was copied or read. Implementation changed only the protected project and `Program.cs`; normal
`bin` and `obj` outputs were generated locally.

The final utility deletes the A0R3 census mode, marker names, candidate staging and final paths,
candidate publication, and candidate-success output. It retains candidate construction, serialization,
strict reload, and deterministic replay only in memory.

## 2. Exact qualified inputs

The final protected inputs are:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R4.csproj
  ecfa6b2117fbbe0eda5d57f7968485eaef8f9a204a54950c7c43e59d6d120935
Program.cs
  4dfbb6a8813c3c24b11125a385a0bae3aaae164902962ba747c474a6850c5ea2
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
  1842a50761de5bba41e012dc9b4edf13c3efe172d2a318b470d82ef3730ac1ad
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
  ce81b6516c800adddf781a58114e71ca28671dff51d0a5cac53d5f4b41fab053
source-bindings.json
  ea193e10d30aea35314df1989be4f98677b906e6e7863252b2a7c4b462829599
```

The binding is 754 bytes of canonical single-line UTF-8 JSON without a BOM or trailing newline. It has
exact schema `atlas-a0r4-source-bindings/v1`, tool revision `atlas-a0r4/1`, exact `R0R4`, the four
reviewed relative names, and the four hashes above. It remains beside the project and outside `state`.

At qualification:

```text
state entries
  0
root-locators.json
  absent
source-bindings.json
  present
tracked repository HEAD
  53c03b5de96c5208bc3d68cc3ff098ed50ce9ff4
configured upstream
  53c03b5de96c5208bc3d68cc3ff098ed50ce9ff4
```

## 3. Validation evidence

The exact final source and bound binaries passed:

- repository-pinned `dotnet format --verify-no-changes`;
- two consecutive complete Release Rebuilds with warnings as errors and zero warnings or errors;
- byte equality of both qualified assemblies across the two rebuilds;
- the complete embedded synthetic suite;
- exact stdout `test-passed`, exit code 0, and empty stderr; and
- a final independent audit of the project, every `Program.cs` line, complete synthetic suite, both
  assemblies, and exact binding.

The final synthetic suite proves:

- exact two-mode CLI, fixed output lines, exit codes, and empty stderr;
- no historical, locator, current-tree, or source-content read before a durable marker;
- complete, partial, and zero-byte marker consumption without retry-shaped output;
- absent marker after publication failure remains preflight-only;
- strict matching marker and receipt schemas plus authoritative receipt reload;
- no receipt-class output without a matching complete receipt;
- all two outer gates and five typed pipeline boundaries map to fixed classes without message parsing;
- unexpected post-marker failure is authoritative only after a durable internal-refusal receipt;
- current candidate construction, strict reload, and replay remain memory-only;
- exact anchor, manifest, locator, metadata, stability, and alias behavior;
- exact `S0R4` parent/path/upstream gate and binary-safe committed-record loading;
- working-tree source-record substitution cannot influence authority;
- current source, assembly, and loaded-runtime identity match the exact binding;
- concurrent process capture drains large stderr and stdout without pipe deadlock;
- invalid locator paths remain runtime-locator refusals without current-tree access;
- drive-root containment cannot bypass repository/workspace separation; and
- no historical runtime-state, candidate publication, A2, A3, or original-data operation occurs.

No runtime locator, historical private input, current corpus metadata, source content, consumed A0R3
state, or diagnostic result was accessed during implementation, validation, or review. The diagnostic
marker and receipt do not exist.

## 4. Independent review iterations

Every reviewer was independent of implementation and used GPT-5.6 Sol. Each review covered the complete
current project and source rather than only the most recent correction. Review input remained limited
to repository-safe authority, protected source, synthetic tests, generated binaries, and the final
binding.

| Candidate                | Reviewer                      | Result        |
| ------------------------ | ----------------------------- | ------------- |
| Initial implementation   | `a0r4-source-reviewer`        | 3 TP          |
| First corrected source   | `a0r4-source-rereviewer`      | 1 TP          |
| Second corrected source  | `a0r4-source-final-reviewer`  | 1 TP          |
| Third corrected source   | `a0r4-source-fourth-reviewer` | 2 TP          |
| Final exact bound source | `a0r4-bound-source-reviewer`  | `No findings` |

The final reviewer verified the exact binding against actual bytes and re-audited every source and test
line, prior correction, authority boundary, output, marker, receipt, Git gate, metadata pipeline, and
exclusion.

## 5. TP/FP adjudication

All seven findings were adjudicated TP. No finding was FP.

| #   | Finding                                                                | Correction                                                                                                 |
| --- | ---------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| 1   | A receipt-class token could escape without an authoritative receipt.   | Make pre-marker and post-marker fallback stage-aware; outer fallback emits only non-authoritative refusal. |
| 2   | A marker-open failure with no marker was misreported as consumed.      | Inspect marker existence after failure; absent is preflight, while any final path consumes.                |
| 3   | Source authority was read from mutable working-tree bytes.             | Load the record binary-safely from the verified `S0R4` Git blob.                                           |
| 4   | Sequential redirected-stream draining could deadlock Git preflight.    | Drain stdout and stderr concurrently and test a bounded large-stderr child process.                        |
| 5   | An invalid locator path could be misclassified as internal failure.    | Normalize expected path-validation exceptions and prove runtime-locator refusal with no tree read.         |
| 6   | Drive-root containment duplicated a separator and missed child paths.  | Reuse an existing trailing separator and test root, sibling, false-prefix, and preflight cases.            |
| 7   | Pre-binding review could not qualify the required exact source inputs. | Freeze the canonical binding, then repeat the complete independent audit over all bound inputs.            |

None changes corpus policy, privacy, output vocabulary, candidate behavior, or the
`trusted-local-filesystem/v1` profile.

## 6. Source authority

The exact source authority is:

<!-- prettier-ignore-start -->
<!-- atlas-a0r4-source-authority:start -->
{"schema":"atlas-a0r4-source-authority/v1","r0r4":"53c03b5de96c5208bc3d68cc3ff098ed50ce9ff4","sourceBindingsSha256":"ea193e10d30aea35314df1989be4f98677b906e6e7863252b2a7c4b462829599","projectSha256":"ecfa6b2117fbbe0eda5d57f7968485eaef8f9a204a54950c7c43e59d6d120935","programSha256":"4dfbb6a8813c3c24b11125a385a0bae3aaae164902962ba747c474a6850c5ea2","utilityAssemblySha256":"1842a50761de5bba41e012dc9b4edf13c3efe172d2a318b470d82ef3730ac1ad","atlasAssemblySha256":"ce81b6516c800adddf781a58114e71ca28671dff51d0a5cac53d5f4b41fab053"}
<!-- atlas-a0r4-source-authority:end -->
<!-- prettier-ignore-end -->

This block grants no authority while it remains uncommitted, unreviewed, or not exact shared `S0R4`.

## 7. S0R4 release gate

The staged record must receive independent `No findings` and be committed unchanged as `S0R4`, the
direct child of exact `R0R4`. `R0R4..S0R4` must add only this record path, the committed blob must equal
the reviewed staged blob, and `HEAD` plus configured upstream must equal `S0R4`.

Only then may the operator materialize the fixed protected `root-locators.json` and invoke the exact
qualified utility once with a fresh run ID. The utility must publish the durable marker before reading
that locator, historical authority, or current-tree metadata.

Any preflight refusal before marker bytes may be corrected with a fresh run ID. Any complete, partial,
or zero-byte marker consumes A0R4 diagnostic authority. No branch authorizes retry, candidate
publication, source correction, A2, A3, or original-data write.
