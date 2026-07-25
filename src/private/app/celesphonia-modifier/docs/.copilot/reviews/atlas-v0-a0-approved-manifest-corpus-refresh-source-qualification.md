# Atlas V0 A0 Approved-Manifest Corpus Refresh Source Qualification

**Lifecycle:** Proposed source-qualification evidence before verified shared `S0R3`

**Increment:** A0R3 - Approved-Manifest Corpus Refresh

**Outcome:** Exact source qualified; one private census remains blocked

**Final independent source result:** `No findings`

**P0R3:** `1c6a568aa4595784f0da6f06ed8b61a390c6a9dc`

**R0R3:** `2bfa608e119da76784f68b2879a1040d6e82f851`

**Governing plan:** `../plans/atlas-v0-a0-approved-manifest-corpus-refresh.md`

**Plan-review gate:** `atlas-v0-a0-approved-manifest-corpus-refresh-plan-review.md`

**Planned staged-record reviewer:** `a0r3-source-qualification-record-reviewer`

## 1. Initial source derivation

Under exact clean shared `R0R3`, the new protected A0R3 workspace began with exactly:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R3.csproj
Program.cs
state/
```

`state` was empty. No A0R2 binding, build output, runtime state, attempt, receipt, decision, candidate,
or A0R1/A0R2 execution artifact was copied.

Before modification, copied bytes matched the exact qualified A0R2 technical inputs:

```text
project
  b333fcdd9c72b0c0b31ab02f3c0b0444cb82b6635f0f3222ba526f327dad2548
Program.cs
  4da1ff87fd26a8437aaea691d4f96ba4f25db48fa13fe1ccd2fbfc9b3f1a24dc
```

## 2. Exact qualified source and assemblies

The final protected files are:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R3.csproj
  75b2e6ddbfdabdf8103bfc39c70eb4ff9b21f89d1b386f723d631d0ff67b764b
Program.cs
  9ac6f4292cd52376b25cb7d5330a31aa5428b51391c12e8cf14e12d7c400097d
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.Tests.dll
  1ec58fdd337ac15f48eacc7804b0ec880aef923a00fe7ec9983b41674d4065eb
bin/Release/net10.0/Hcoona.CelesphoniaModifier.Atlas.dll
  94da516dce6c066fb7e85b6e36522cb0f07b93d207f60565e39c014603903bc9
source-bindings.json
  3895866bfd1d1b498e350dcd136889cb1411a3d870885ce88027b3b7456682d9
```

`Hcoona.CelesphoniaModifier.Atlas.Tests.dll` remains the utility's one real friend-assembly identity.
The canonical `atlas-a0r3-source-bindings/v1` file names exactly those four qualified inputs, binds
exact `R0R3`, and matches every listed SHA-256.

## 3. Validation

All .NET commands used `mise exec -- dotnet`.

- format verification completed with no change;
- two consecutive complete warning-as-error Release Rebuilds completed with zero warnings and errors;
- both Rebuilds produced byte-identical utility and linked Atlas hashes listed in section 2;
- the complete synthetic suite returned exactly `test-passed`, exit `0`, and empty stderr;
- synthetic execution left protected `state` empty;
- the tracked repository remained clean and shared at exact `R0R3`; and
- no live census, candidate publication, source-content read, or other private-data operation occurred.

The final exact source and binaries were not rebuilt or changed after the final independent source
review. The canonical source-binding file was created afterward from those frozen hashes and contains
no runtime locator or private corpus value.

## 4. Source review and TP/FP adjudication

Every reviewer used GPT-5.6 Sol, reviewed the complete exact candidate from a fresh independent
context, and received no game data, private corpus, historical request or manifest instance, runtime
locator, or unauthorized execution result.

| Candidate                    | Reviewer                 | Result        |
| ---------------------------- | ------------------------ | ------------- |
| Initial implemented source   | `a0r3-source-reviewer`   | 3 TP          |
| First corrected source       | `a0r3-source-reviewer-2` | 2 TP          |
| Final exact qualified source | `a0r3-source-reviewer-3` | `No findings` |

All five findings were in scope and adjudicated TP. No finding was FP.

| Finding                                                                    | Correction                                                                                                           |
| -------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Approved save entries were not checked against filename classification.    | Validate every one-segment save locator through released classification and require exact role, slot, and decision.  |
| Approved definition entries were not checked against ordered rules.        | Apply the manifest's ordered first-match rules and require exact group and decision for every approved entry.        |
| Pre-marker tests omitted private metadata and enumeration access.          | Reject content, metadata, or enumeration access to every private input and current runtime tree before the marker.   |
| Marker reinvocation and zero-byte publication had retry-shaped outcomes.   | Treat any final marker, including zero-byte or partial, as consumed before source gates and never delete it.         |
| Schema-invalid optional reason codes passed and could reach the candidate. | Enforce the schema domain on all root, entry, and group reason codes in historical and bounded candidate validation. |

The final review verified every correction in the complete source, project, exact binary identity,
and synthetic suite.

## 5. Source authority

The following is the unique machine-readable authority block required by the reviewed utility:

<!-- prettier-ignore-start -->
<!-- atlas-a0r3-source-authority:start -->
{"schema":"atlas-a0r3-source-authority/v1","r0r3":"2bfa608e119da76784f68b2879a1040d6e82f851","sourceBindingsSha256":"3895866bfd1d1b498e350dcd136889cb1411a3d870885ce88027b3b7456682d9","projectSha256":"75b2e6ddbfdabdf8103bfc39c70eb4ff9b21f89d1b386f723d631d0ff67b764b","programSha256":"9ac6f4292cd52376b25cb7d5330a31aa5428b51391c12e8cf14e12d7c400097d","utilityAssemblySha256":"1ec58fdd337ac15f48eacc7804b0ec880aef923a00fe7ec9983b41674d4065eb","atlasAssemblySha256":"94da516dce6c066fb7e85b6e36522cb0f07b93d207f60565e39c014603903bc9"}
<!-- atlas-a0r3-source-authority:end -->
<!-- prettier-ignore-end -->

The block grants no authority by file presence. The utility must verify exact committed `S0R3`,
direct parent `R0R3`, the one-path Git change, clean shared state, canonical authority and binding
documents, frozen source and assembly hashes, and empty protected state before marker publication.

## 6. S0R3 release gate

This proposed record grants no private operation until release. One A0R3 census attempt may occur only
after:

1. this exact staged record and the canonical protected source binding receive independent
   `No findings`;
2. the staged record is committed unchanged as `S0R3`, the direct child of exact `R0R3`;
3. `R0R3..S0R3` adds only this source-qualification path;
4. the committed blob equals the reviewed staged blob;
5. `S0R3` is pushed and verified as the clean shared branch tip;
6. every protected source, assembly, and binding hash still equals sections 2 and 5; and
7. protected runtime `state` remains empty.

Even verified `S0R3` authorizes only one consuming metadata-only census attempt. It grants no census
retry, candidate approval, decline, finalization, A2 operation, production change, source-content
read, or original-data write.
