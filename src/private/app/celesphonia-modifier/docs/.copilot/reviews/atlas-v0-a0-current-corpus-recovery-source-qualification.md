# Atlas V0 A0 Current Corpus Recovery Source Qualification

**Lifecycle:** Proposed source-qualification evidence before verified shared `S0R2`

**Increment:** A0R2 - Diagnostic-Gated Census Recovery

**Outcome:** Exact source qualified; private diagnosis remains blocked

**Final independent source result:** `No findings`

**P0R2:** `c82f1c767fab496dd2b025fa1ab25f5d6583cd46`

**R0R2:** `789c12b83dfa0ba4ede8f7efdf2cfb64d386167f`

**Governing plan:** `../plans/atlas-v0-a0-current-corpus-recovery.md`

**Plan-review gate:** `atlas-v0-a0-current-corpus-recovery-plan-review.md`

**Planned staged-record reviewer:** `a0r2-source-qualification-record-reviewer`

## 1. Initial source derivation

Under exact clean shared `R0R2`, the new protected A0R2 workspace began with exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R2.csproj
Program.cs
state/
```

`state` was empty. No A0R1 runtime state, attempt, receipt, candidate, decision, binding file, build
output, or other artifact was copied.

Before modification, copied bytes matched G0R1:

```text
project
  d3d92482d279f4c7afbdd8b0fbbcfbf2e04251feb1bffe1b19195ab79b3f43a8
Program.cs
  0eeefcf9d0c9d68dd1e58ac2271ac286f5d8527e149d0a50f1ea93ac7c5b37f9
```

## 2. Exact qualified source and assemblies

The final protected files are:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R2.csproj
  b333fcdd9c72b0c0b31ab02f3c0b0444cb82b6635f0f3222ba526f327dad2548
Program.cs
  4da1ff87fd26a8437aaea691d4f96ba4f25db48fa13fe1ccd2fbfc9b3f1a24dc
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
  3596c44a59cad7e268d7d835004af46d71beb1b2e8f1c8b199fbb4edcd22cf63
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
  4e7fdcfbd6ed3497f10739bb96328509edf4b8e8990c01e66bfaa8deaca79049
source-bindings.json
  b2cbb9b99d4b92127d74c3bc28ce7d54ba2950f4b1c0ede58b24bcf9e9e67aad
```

`Hcoona.CelesphoniaModifier.Atlas.Tests.dll` is the utility's single real friend-assembly identity.
No copied `A0R2.dll` alias remains.

The binding file is canonical `atlas-a0r2-source-bindings/v1`, names exactly those four qualified
inputs, binds exact `R0R2`, and matches every listed SHA-256.

## 3. Validation

All .NET commands used `mise exec -- dotnet`.

- format verification completed with no change;
- two consecutive complete warning-as-error Release Rebuilds completed with zero warnings and errors;
- both Rebuilds produced byte-identical utility and linked Atlas hashes listed in section 2;
- the complete synthetic suite returned exactly `test-passed`, exit `0`, and empty stderr;
- synthetic execution left protected `state` empty;
- the tracked repository remained clean and shared at exact `R0R2`; and
- no live diagnostic, decision, census, candidate publication, or private-data operation occurred.

The exact source and final binaries were not rebuilt or changed after final independent source review.

## 4. Source review and TP adjudication

Every reviewer used GPT-5.6 Sol, reviewed the complete exact candidate from a fresh context, and
received no game data, private corpus, A0R1 runtime state, or unauthorized execution result.

| Candidate                  | Reviewer                     | Result        |
| -------------------------- | ---------------------------- | ------------- |
| Initial implemented source | `a0r2-source-reviewer`       | 6 TP          |
| Corrected source           | `a0r2-source-rereviewer`     | 1 TP          |
| Final exact source         | `a0r2-final-source-reviewer` | `No findings` |

All seven findings were in scope and adjudicated TP. No finding was FP.

| Finding                                                               | Correction                                                                                                |
| --------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Release output identities were stale and ambiguous.                   | Use one real friend assembly and bind the final byte-stable qualification Rebuild outputs.                |
| Staged attempt-marker publication had an unclosable interruption gap. | Direct-create and flush final marker paths; partial final markers consume their attempts.                 |
| Device-backed historical ancestor components were accepted.           | Reject `Device` and `ReparsePoint` on every component before content reads.                               |
| `--test` accepted relative and non-ordinary roots.                    | Validate canonical ordinary DOS drive-rooted syntax before dispatch for every mode.                       |
| Marker-order tests did not prove zero pre-marker private access.      | Assert zero historical reads and current-tree enumeration at every after-marker hook.                     |
| `D0R2` tests constructed authority from the artifacts under test.     | Independently construct authority and mutate every receipt, decision, source, commit, and action binding. |
| Superscript `COM` and `LPT` device names passed root parsing.         | Reject Windows reserved ASCII and superscript device-name forms with cross-mode coverage.                 |

Final review verified all corrections in the complete source, project, binary identity, and synthetic
suite.

## 5. Source authority

The following is the unique machine-readable authority block required by the reviewed utility:

<!-- prettier-ignore-start -->
<!-- atlas-a0r2-source-authority:start -->
{"schema":"atlas-a0r2-source-authority/v1","r0r2":"789c12b83dfa0ba4ede8f7efdf2cfb64d386167f","sourceBindingsSha256":"b2cbb9b99d4b92127d74c3bc28ce7d54ba2950f4b1c0ede58b24bcf9e9e67aad","projectSha256":"b333fcdd9c72b0c0b31ab02f3c0b0444cb82b6635f0f3222ba526f327dad2548","programSha256":"4da1ff87fd26a8437aaea691d4f96ba4f25db48fa13fe1ccd2fbfc9b3f1a24dc","utilityAssemblySha256":"3596c44a59cad7e268d7d835004af46d71beb1b2e8f1c8b199fbb4edcd22cf63","atlasAssemblySha256":"4e7fdcfbd6ed3497f10739bb96328509edf4b8e8990c01e66bfaa8deaca79049"}
<!-- atlas-a0r2-source-authority:end -->
<!-- prettier-ignore-end -->

The block grants no authority by file presence. The utility must verify exact committed `S0R2`,
direct parent `R0R2`, the one-path Git change, clean shared state, canonical block, binding file, and
fresh file hashes before any private access.

## 6. S0R2 release gate

This proposed record grants no private operation until release. One diagnostic attempt may occur only
after:

1. this exact staged record receives independent `No findings`;
2. it is committed unchanged as `S0R2`, the direct child of exact `R0R2`;
3. `R0R2..S0R2` adds only this source-qualification path;
4. the committed blob equals the reviewed staged blob;
5. `S0R2` is pushed and verified as the clean shared branch tip;
6. every protected source, assembly, and binding hash still equals sections 2 and 5; and
7. protected runtime `state` remains empty.

Even verified `S0R2` authorizes only one consuming private diagnostic attempt. It grants no protected
decision, census, candidate approval, finalization, A2 operation, production change, or original-data
write.
