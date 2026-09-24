# Python Smoke Brief LLD

## Status and Ownership

This design realizes confirmed [`WD-PY-*`](./requirements.md#python-smoke-slice),
the [HLD](./high-level-design.md#python-smoke-extension) and its five MLDs.
The V3 maintainer authors and maintains this carrier; implementers and reviewers
use it to close the Python slice's concrete contracts without turning the MLDs
into command inventories. It specifies the implementation contract, not native registry support. The
[accepted Wave](../../../../../docs/delivery-wave.md#implement-the-disabled-python-v3-smoke)
authorizes disabled implementation and local validation; native operations,
configuration changes and publication remain separately gated.

The [source record](./research/python-smoke-evidence.md) owns service findings
and limits. The [migration policy](./migration-strategy.md#python-smoke-delivery)
owns staged delivery. No existing npm/NuGet permission is reused.

## Package and Build Contract

The package root is `src/public/lib/hcoona-release-smoke-python`, with
`src/hcoona_release_smoke_python/__init__.py` exporting `project_id()` and no
runtime dependencies. One Release Unit uses an Ubuntu/CPython 3.14 Build
Definition with wheel and sdist outputs. `Requires-Python` is `>=3.14`; the
universal `py3-none-any` wheel expresses absence of native ABI/platform payload,
not evidence of support on every Python implementation or operating system.

A project-local NBGV `version.json` owns the smoke lineage and public-main
projection. The Provider reads complete history/tags at the exact target,
selects `SemVer2`, calls the existing `nbgv-python` normalization and freezes
the canonical facts, field and exact PEP 440 value. Normalized parseable local
versions are valid for non-publishing CI/qualification. Release Live admission
requires an already normalized public version without local metadata and
rejects an ineligible projection without stripping or replacing it. The initial `0.1.0-beta.{height}` lineage must yield a supported prerelease projection on protected main; its concrete
version baseline is closed with the implementation descriptor, not derived
from a run number or package-index availability. Colliding coordinates fail.

Use the existing Hatch NBGV dynamic-version configuration in the source
manifest and a bounded staging transformation in the V3 Build Adapter:

1. Copy the admitted source closure into an isolated directory, without `.git`,
   workspace links or unrelated project configuration.
2. Replace the dynamic `version` entry with static `project.version` equal to
   the frozen projection. Remove only the NBGV version-source table and its
   build-system dependency; reject any other undeclared dynamic metadata or
   custom build hook. Keep `hatchling.build` and its declared build prerequisites.
3. Write canonical `_workflow_delivery_provenance.json` into the import package.
   Both wheel and sdist must contain it, and ordinary sdist rebuilding must
   preserve it in the installed package. The witness binds target, Release Unit,
   canonical/native version facts, Build Definition, catalog/control digests
   and purpose; no run, Attempt, channel or destination value is embedded.
4. Build both formats using UV with workspace sources disabled and the pinned
   Hatchling backend. Record exact resolved producer toolchain/build inputs.
   Release determinism excludes wall-clock/run/environment version data and
   binds archive ordering/timestamps through the Build Definition. Neither
   Build nor consumer invokes NBGV or the source dynamic-version hook.
5. Inspect the original wheel `METADATA`/`WHEEL`/`RECORD`, sdist `PKG-INFO` and
   staged `pyproject.toml` using native formats. Require matching name/version,
   no runtime dependencies, expected pure-Python payload, exact witness and the
   closed two-file output set. The sdist's static manifest and declared backend
   requirements must build outside Git with normal PEP 517 isolation.

The staging transformation is reviewed product-build behavior, not a general
manifest rewriting service. A missing or unsupported shape fails. `nbgv-python`
retains its existing public API and requirements. The producer lock/pinned
backend must bind its full resolved build closure; the sdist declares portable
build prerequisites, with the initial consumer's actual resolved versions
retained as evidence. The producer uses CPython 3.14.3, UV 0.10.9, Hatchling 1.32.0, NBGV
3.10.94, `nbgv-python` 2.1.0.dev1 and packaging 26.3. The hash-pinned
`eng/workflow-delivery/v3/python-build-constraints.txt` closes the Hatchling
backend dependency set against `uv.lock`.

## Qualification and Artifact Admission

One Build result contains two tagged Artifact References, `wheel` and `sdist`.
Each binds logical SHA-256, filename, target witness, producer and immutable
transport ID/digest. The pair's order is fixed and duplicate/missing variants
fail closed. Archive readers reject unsafe paths, duplicate members and
ambiguous metadata without extracting executable code in the decision zone.

Separate fresh credential-free environments outside the repository install the
exact wheel and rebuild/install the exact sdist. Disable package/workspace
substitution and caches; no `PYTHONPATH` to the checkout, installed smoke copy,
Git directory, .NET/NBGV command or ambient version variable can satisfy the
consumer. Build prerequisites use the explicit public PyPI index independently
of the smoke's destination. Use downloaded exact smoke files, never an
`--extra-index-url` fallback. Validate `importlib.metadata` version, the installed
witness and `project_id()`. The sdist consumer's wheel is retained only as
consumer evidence. CI and Release each bind their own Build/Evidence instances.

The later destination audit independently resolves each format from the chosen
registry, downloads it, verifies its digest/witness against the approved set,
and repeats these clean consumers. It must not install a local original and
call that destination consumption.

## Registry and Credential Profiles

| Binding              | TestPyPI Buddy                         | PyPI Official                      |
| -------------------- | -------------------------------------- | ---------------------------------- |
| Project              | `hcoona-release-smoke-python`          | `hcoona-release-smoke-python`      |
| Upload               | `https://test.pypi.org/legacy/`        | `https://upload.pypi.org/legacy/`  |
| Simple Index         | `https://test.pypi.org/simple/`        | `https://pypi.org/simple/`         |
| OIDC audience        | `testpypi`                             | `pypi`                             |
| Proposed Environment | `workflow-delivery-v3-python-testpypi` | `workflow-delivery-v3-python-pypi` |

These names are proposed configuration, not existing resources. Each protected
admission file under `.github/workflow-delivery/` is destination-specific,
initially blocked with `live_enabled: false`. The workflow is `.github/workflows/workflow-delivery-v3-python-smoke.yml`.
The protected files are
`.github/workflow-delivery/governance/hcoona-release-smoke-python-testpypi.json`
and `hcoona-release-smoke-python-pypi.json` in the same directory. Future OIDC
project registrations must bind the exact workflow filename and Environment. Do not register a wildcard,
use a pending publisher as evidence of admitted project ownership, or silently
change a registration to get a token.

Use the documented PyPI OIDC exchange and upload APIs with a pinned HTTP client
profile. It closes runtime/client/TLS versions, trusted HTTPS origins, timeouts,
finite read budgets, multipart metadata mapping and exactly one POST per
file. Upload uses the original qualified bytes; no rebuild, signing, metadata
rewrite, skip-existing option or automatic resend is allowed. No mutating
redirect fallback is admitted. Timeout after send is ambiguous. Even a returned
duplicate response remains failure. Retry transport only for immutable record
persistence, not token-accompanied upload. Token acquisition fails closed; no
credential appears in persistent logs or artifacts.

Read the supported JSON Simple Index and use `packaging` to identify all files
for the native-equivalent version. Retain sanitized response hashes and actual
file bytes. Index file URLs must satisfy the admitted HTTPS file-host policy;
no credentials accompany public downloads. Missing/inconsistent listing,
unknown filename/version, extra or yanked file, or failed byte/witness readback
blocks exactness. Whole-set absence is a creation candidate, not proof that
PyPI has never used the filenames. Deleted filename rejection remains failure.
The native profile must exercise the actual APIs, request mapping and runtime
rather than assuming an SDK/CLI name establishes one-shot behavior.

## Hosted Integration

`python -m three_workflow_delivery_v3.python_cli` exposes the bounded stages.
The workflow separates request, Provider, compiler, CI/Release Plan, Build,
Qualification, publication preparation, publisher and Finalizer jobs. Python CI
conservatively selects the entire slice at the trusted tested-merge SHA; it
makes no affected-path optimization claim. CI and Release retain separate
Plan/Snapshot, Evidence and Decision records. Only mechanical artifact and
consumer inspection are shared.

The exact-target Model schema is `workflow-delivery/v3/python-repository-model-snapshot`.
Python Governance uses `workflow-delivery/v3/python-governance-v1`; the other
Python records use their explicit `python-*` discriminators. The shared
Publication Result uses `variant: python-distribution-set`, and the shared
Finalizer returns the existing AttemptOutcome. Parsers reject foreign variants
and retain all current-run transitive references.

Each payload uses an immutable `archive: false` artifact. The persistence
composite downloads the returned artifact ID, requires digest validation and
binds readback bytes before exporting the next explicit DAG edge. No artifact
listing or name-based discovery reconstructs authority. Wheel and sdist remain
separate original transports. The publisher exports one scalar terminal
reference; later artifact presence cannot substitute for that output.

Protected configuration attestation covers the complete writer, Environment,
service registration and native evidence inventories. The job token cannot
read administrative Environment, invitation, team or variable APIs. Runtime
freshness therefore reads protected Governance plus accessible repository,
protected-branch and collaborator metadata; current-run approval, deployment
and the Environment sentinel establish the publisher boundary. Failed reads
block; no administrative-token fallback or empty-inventory substitution is
allowed. Changes outside this bounded runtime visibility still require the
owner to disable Governance and renew its configuration attestation.

The one-shot HTTPS transport uses CPython 3.14.3 with OpenSSL 3.5.5
(27 January 2026), verified TLS 1.2 or later and a 30-second socket timeout.
The exact version/profile check applies to the actual registry transport;
HTTP redirects, proxies and request retries are unavailable. The later native
acceptance must prove this actual profile separately at each destination.

## Approval and Terminal Contract

The credential-free preparation job persists one Snapshot and Approval Bundle
for the two-file set. The summary presents target/version, both original file
digests, destination/profile, two sequential uploads and possible partial effects.
The destination-bound Environment gates the trusted publisher. After native
current-run approval verification, that job emits the immutable Authorization
before requesting an OIDC assertion or short-lived registry token. The job
permission exists after Environment approval; reviewed control enforces this
later token-acquisition order, not a cryptographic artifact-bound permission.
It verifies all current bindings,
final Governance/configuration and fresh whole-set absence before the marker.

The action discriminator is `python-distribution-set`; its closed operations
are ordinal 0 (wheel) and 1 (sdist). One marker binds the Authorization and
actual profile proof. Persist and read-validate it before operation 0. Operation
1 is unreachable without definitive operation-0 success and exact readback.
After both succeed, exact whole-set readback is mandatory.

The existing Publication Result record gains a strict Python destination
variant with two operation entries and final whole-set readback. It refers to
the marker, which resolves both requested artifacts through Authorization and
Snapshot. Unknown/missing/extra operation entries fail admission; it cannot be
parsed as an npm or NuGet Result. Existing schema consumers must either
explicitly admit the Python variant or reject it before effects. No generic
list of independent actions or backward-compatible coercion is added.

| Terminal evidence                                                 | Set outcome                                                                          |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| Pre-marker rejection/cancellation                                 | No upload authorized or Result; current DAG explains failure.                        |
| Both uploads definitively succeed and final exact readback passes | `published`, known mutation.                                                         |
| Wheel fails definitively                                          | Failed; sdist is not attempted; mutation facts require supported evidence.           |
| Wheel succeeds; sdist fails or is not attempted                   | Failed with retained partial mutation; no compensation.                              |
| Any ambiguous upload or unsuccessful final readback               | Failed, retaining known and possible effects; never exact-satisfied in this Attempt. |
| Durable marker but no durable Result                              | Unknown and possibly mutated, irrespective of subsequent registry reads.             |
| No action and fresh complete exact proof                          | Exact-satisfied without Environment, token or publisher.                             |

Result/marker/null remains one scalar terminal reference. Rerun/other-Attempt
records, malformed references and missing publisher outputs fail admission;
Finalizer never searches artifacts or synthesizes missing records. After a
failure, a new separately permitted dispatch starts a new build and observation;
a partial set still blocks. Only separately requested recovery may address it.

## Evidence and Delivery Gates

Implementation validation covers these distinct claims before native admission:

- Scenario tests: complete two-file success; wheel failure prevents sdist;
  partial/extra/yanked/conflicting pre-state blocks; second-file failure and
  ambiguous response stop; zero-action exactness; fresh state drift; marker or
  Result persistence loss; cancellation; independent Buddy/Official lineage.
  Include successful CI qualification with a preserved valid local-version
  projection and rejection of that same projection at Release Live admission.
- Strict contract tests: target/version/source closure, two-artifact ordering,
  witness and native identity, authority/profile/audience/Environment mismatch,
  cross-run/purpose/schema rejection and one-shot mutation bounds. Mocked
  responses prove only application behavior.
- Real local integration: full-history NBGV projection, static Hatch metadata,
  original wheel/sdist contents, isolated clean consumers and reproducible
  bytes for the same target and frozen build inputs. Any reproducibility gap
  blocks this slice; sealed-artifact resume is outside scope.
- Separately authorized native acceptance at each destination/profile: fresh
  creation of both formats; same-byte and different-byte same-filename
  duplicate rejection; bounded competing creation with either winner allowed;
  preserved winner bytes; exact original readback and both clean consumers.
  Each concrete protocol must define fresh coordinates, before/after comparison,
  finite requests/concurrency, stop conditions and retained sanitized evidence.
  Disposable project OIDC configuration must be separately authorized; moving
  to the smoke project requires exact tuple/configuration binding and fresh
  admission, not blind reuse of a different project's ready record.
- Separately authorized TestPyPI publication, then PyPI publication: audit each
  current Attempt's target/version, original artifacts, Qualification, Snapshot,
  Approval/Authorization, terminal evidence/Outcome, destination downloads and
  consumers. Retain evidence outside the registry. Failed consumer audit blocks
  completion even when upload mutation succeeded.

No native protocol is executed from this LLD. It specifies the evidence needed;
the later bounded request supplies concrete coordinates/counts and permission.
Local passing tests cannot replace either destination's native or publication
proof. PyPI filename non-reuse and the accepted TestPyPI live-file dependency
are the `WD-PY-006` basis; no cross-file transaction or unlimited retention is
claimed. Review mutable source findings again when choosing the implementation
profile and at the implementation-Wave gate.
