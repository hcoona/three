# Python Native Acceptance Preparation

The V3 maintainer produces and maintains this operator interface. The owner and
independent native auditor use it to form the later bounded operational request.
The [protocol](python-native-acceptance.md) owns the schedule and evidence limits;
this record identifies tooling inputs and remaining resource decisions. Local
validation establishes tooling behavior only, not native admission or execution
authority. TestPyPI precedes a separately qualified PyPI request.

## Delivered Interfaces

The module `three_workflow_delivery_v3.acceptance.python_native` exposes:

- `build-fixtures`: credential-free exact-target Provider, Build, comparison
  construction and eight clean consumers; produces a raw fixture bundle and
  prints exact versions and digests for a later protected request.
- `prepare`: hosted preparation against the exact non-null protected request;
  binds the eight approved hashes, current run and protected tooling revision.
- `probe`: verifies immutable input, exact current-run owner approval and
  Environment proof before one assertion/exchange and the fixed native suite.
- `audit`: replays retained original service bytes and performs four fresh
  clean consumers. Its verdict is supplied-fact validation; independent native
  provenance and configuration review remains mandatory.
- `replay`: local supplied-fact audit from the exact prepared/probe files and
  a retained canonical lineage JSON (`prepared`, `probe`, `run-id`,
  `tooling-sha`); uses no hosted credential or destination request. Fresh
  sdist consumers still fetch public backend prerequisites.
- `archive` and `digest`: retain surviving evidence and expose exact logical
  bytes for immutable artifact transport. No command dispatches or polls a run.

The tooling implements the [bounded post-upload observer](../hcoona-release-smoke-python-lld.md#bounded-post-upload-observation)
for C1/C2/C7/C8 and audits every original pending response and its timing.
The changed operation profile requires a fresh native suite. Existing failed
bootstrap evidence cannot qualify it.

The manual-only hosted entry is
[Native Python acceptance workflow](../../../../../../.github/workflows/workflow-delivery-v3-native-python-acceptance.yml).
Only its Environment-gated probe job requests OIDC. Preparation and audit never
receive registry capability. The probe executes no target build or product code.
Both slots in
[native Python request slots](../../../../../../.github/workflow-delivery/native/python-requests.json)
are null. Both normal-Live Governance destinations remain disabled.

For local preparation from an initialized checkout, run this PowerShell command
with two reviewed protected-main ancestor commits and a new output filename:

```powershell
python -m three_workflow_delivery_v3.acceptance.python_native build-fixtures `
  --target-a FULL_A_SHA --target-b FULL_B_SHA --output python-native-fixtures.zip
```

`FULL_A_SHA` and `FULL_B_SHA` are explanatory placeholders, not admitted defaults.
Use the repository's pinned Python/UV/NBGV toolchain and locked V3 environment.
The command has no registry or OIDC operation. Clean sdist consumers may fetch
public build prerequisites through the existing adapter. The output path must
not exist. Never substitute these local supplied facts for native observations.

## Local Fixture Readiness

Credential-free validation on 2026-09-25 used these protected source ancestors:

| Slot | Exact target                               | Public NBGV version |
| ---- | ------------------------------------------ | ------------------- |
| A    | `1fad19a38727811d22901826454e79810dda377d` | `0.1.0b2`           |
| B    | `e3cb92d0daea4ff466fe155e2eba170ce2d0c7dc` | `0.1.0b3`           |

The native names are
`hcoona_release_smoke_python-0.1.0b2-py3-none-any.whl`,
`hcoona_release_smoke_python-0.1.0b2.tar.gz`,
`hcoona_release_smoke_python-0.1.0b3-py3-none-any.whl`, and
`hcoona_release_smoke_python-0.1.0b3.tar.gz`.
Each has an original and valid different-byte comparison candidate. The delivery
PR retains the exact eight hashes, producer/consumer evidence, test results and
reviewed tooling identity. These candidates are not reservations: actual
availability remains unknown until a separately authorized initial native read.
The later request must use freshly verified exact hashes from accepted tooling;
this prose is not a second fixture manifest.

## Concrete TestPyPI Request Template

The owner must close every row before a later protected slot change or native
execution. Unknown values remain absent; no executable placeholder request is
installed by preparation.

| Request field or resource | Exact value or required owner input                                                                                                                     |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Repository and operator   | `hcoona/three`, repository ID `1102295886`; `hcoona`, user ID `712433`                                                                                  |
| Native workflow           | `.github/workflows/workflow-delivery-v3-native-python-acceptance.yml`                                                                                   |
| Destination/project       | `testpypi`; `hcoona-release-smoke-python`; pinned `PythonRegistry("testpypi").profile_digest`                                                           |
| Owner authorization       | Reviewed HTTPS request URL and SHA-256; explicit smoke-only/no-production-use acceptance and permission to retain four files                            |
| Ownership                 | Actual owner account/project control, reviewed evidence URL and SHA-256; not yet inspected                                                              |
| Configuration             | Exact existing or separately provisioned trusted-publisher/Environment configuration, reviewed evidence URL and SHA-256; not yet inspected              |
| Environment               | `workflow-delivery-v3-python-testpypi`; actual numeric ID and sentinel; sole owner reviewer, no bypass, protected main, reviewed complete configuration |
| Publisher                 | Project-bound registration for the native workflow and exact Environment/repository; audience `testpypi`                                                |
| Sources                   | Two distinct protected-main ancestor commits and their unchanged distinct public smoke prereleases                                                      |
| Fixture hashes            | Eight exact original/comparison wheel/sdist logical SHA-256 values from accepted tooling                                                                |
| Generation                | Fresh 32-character lowercase hexadecimal identity, not previously dispatched                                                                            |
| Dispatch binding          | Exact accepted protected-main tooling SHA and canonical protected request SHA-256; attempt one only                                                     |
| Finite permission         | One explicit dispatch/approval, one OIDC assertion/exchange, ten uploads, 47 registry reads and at most eight GitHub proof reads                        |
| Failure disposition       | Partial success/lost evidence leaves the generation spent and possibly mutated; no retry, rerun, refill, replacement dispatch or cleanup authority      |
| Evidence custody          | Durable operator storage location and named independent auditor; copy complete artifact references and original bytes before 45-day Actions expiration  |

The strict request parser in `acceptance/python_native_contract.py` owns the
machine fields: `schema`, `generation`, `registry`, `project`, `profile-digest`,
`targets`, `fixture-digests`, `environment`, `authorization`, `configuration`
and `ownership`. A populated slot records a separately accepted request; it
cannot create consent. The operator records the actual dispatched run and retires
the consumed slot through protected delivery before considering another request.

Native registration is distinct from normal publication registration for
`workflow-delivery-v3-python-smoke.yml`. Native acceptance does not enable normal
Live. Configuration/native execution, destination-specific admission and each
normal publication retain their respective grants and current-Attempt Approval.
PyPI needs its own ownership/configuration, Environment
`workflow-delivery-v3-python-pypi`, audience `pypi`, fixtures, generation and
independent evidence after TestPyPI; nothing is promoted between destinations.
