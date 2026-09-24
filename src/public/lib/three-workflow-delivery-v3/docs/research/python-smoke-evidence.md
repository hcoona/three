# Python Smoke Destination and Build Evidence

This public-source research supports the Python requirements confirmation in
[Issue #843](https://github.com/hcoona/three/issues/843), under the accepted
[Python design Wave](../../../../../../docs/delivery-wave.md#define-the-python-v3-smoke-scope-and-destination).
The owner selected TestPyPI followed by production PyPI. This record does not
confirm requirements, select an implementation, admit a destination, or grant
publication. npm and NuGet remain complete under their existing authorities.

Workflow-delivery maintainers produce and maintain this evidence. Requirements,
design and independent evidence reviewers consume it to avoid treating Python
as a copy of either single-archive GitHub Packages slice. The project research
carrier preserves recoverable sources and their limits; the Issue owns proposed
scope, decisions and delivery progress.

## Source Basis

Official public pages below were retrieved on 2026-09-24. Statements in this
section are documentation findings, not observations of this repository's
accounts, packages, OIDC configuration or publishing behavior. The code finding
uses the repository baseline `12d45fdd3c8cae6b69df0e6cff520e5c7e478505`.

### Separate Destinations and Retention

[PyPA Using TestPyPI](https://packaging.python.org/en/latest/guides/using-testpypi/),
introduction and Registering your account, identifies a separate index and
database. Its explicit limitation is: "The database for TestPyPI may be
periodically pruned, so it is not unusual for user accounts to be deleted."
Successful TestPyPI installation therefore cannot promise lasting availability
or establish production PyPI configuration.

[PyPA .pypirc, Common configurations](https://packaging.python.org/en/latest/specifications/pypirc/#common-configurations)
lists the upload repositories `https://test.pypi.org/legacy/` and
`https://upload.pypi.org/legacy/`. The TestPyPI guide's installation section
identifies `https://test.pypi.org/simple/` and `https://pypi.org/simple/`.
These endpoints do not establish package-name availability, ownership or grants.

[PyPI Help, Why am I getting a filename has already been used error?](https://pypi.org/help/#file-name-reuse)
states: "PyPI does not allow for a filename to be reused, even once a project
has been deleted and recreated." It explains that a distribution will resolve
to the same file and "it can only be removed". Its project-deletion guidance
also states that deleted files cannot be re-uploaded and that administrators
cannot restore deleted projects, releases or files. This is a documented PyPI
file immutability contract, not indefinite availability or a promise about
TestPyPI after database pruning.

### Publication Granularity and Observation

[PyPI Upload API, Upload a file](https://docs.pypi.org/api/upload/#upload-a-file)
states: "Releases on PyPI are created by uploading one file at a time. The
first file uploaded of a new version creates a release for that version, and
populates its metadata." The request is a multipart POST with one content file
and its filename. A wheel and sdist therefore require two uploads; a release
may already exist after just one succeeds. The documentation does not promise
an atomic two-file transaction, rollback or a single snapshot across reads.

[PyPI Index API, Project detail](https://docs.pypi.org/api/index-api/)
documents project file listings with filename, URL, hashes, Python constraint
and yanked status. Such metadata can locate and describe distribution files;
it does not itself prove equality with the qualified bytes. V3's consumer and
publication checks still need fresh actual file bytes and the agreed witness.

[UV Building and publishing](https://docs.astral.sh/uv/guides/package/),
publishing section, describes checking existing files and handling raced
parallel uploads. This convenience behavior must not be assumed to implement
V3's current rule that a conflicting or ambiguous mutation response remains
failed in that Attempt. A concrete publisher profile needs a review of actual
retry, skip, reconciliation and attestation behavior before selection.

### Version and Build Inputs

[Python version specifiers, Local version identifiers](https://packaging.python.org/en/latest/specifications/version-specifiers/#local-version-identifiers)
states that PyPI "MUST NOT allow the use of local version identifiers".
A PEP 440 parseable version is therefore not automatically a publishable PyPI
version. NBGV-derived local components must not be silently stripped, and a
workflow-run number must not become a replacement version authority.

At the pinned repository baseline,
[`NbgvVersionSource.get_version_data()`](https://github.com/hcoona/three/blob/12d45fdd3c8cae6b69df0e6cff520e5c7e478505/src/public/lib/nbgv-python/src/nbgv_python/hatch_plugin.py)
constructs `NbgvRunner`, queries the configured working directory, selects the
configured field and normalizes it. It has no direct frozen-version input in
that method. The reusable
[`normalize_version_field()`](https://github.com/hcoona/three/blob/12d45fdd3c8cae6b69df0e6cff520e5c7e478505/src/public/lib/nbgv-python/src/nbgv_python/versioning.py)
preserves local metadata and may create it for unrecognized prerelease labels.
The existing [adapter authority](../../../nbgv-python/docs/architecture/hatch-integration.md)
records those limits. These are code-inspection findings; they do not prove
that every Hatch sdist build invokes the plugin or that source installation
necessarily fails.

[Hatch project metadata, Version](https://hatch.pypa.io/latest/config/metadata/#version)
documents static and dynamic version configuration. Its
[environment version source](https://hatch.pypa.io/latest/plugins/version-source/env/)
reads a named environment variable. Those are available backend mechanisms,
not an accepted V3 choice or evidence that a consumer without that environment
can rebuild a distribution. The Python design must choose a frozen-input path
that satisfies both planned build and ordinary sdist consumption without
re-resolving NBGV, an ambient override, or an undeclared Git requirement.

The UV build guide says `uv build` respects `tool.uv.sources` for build
dependencies by default and recommends `--no-sources` when checking publication
compatibility with other build tools. A workspace build alone therefore does
not establish that declared sdist build requirements suffice outside the repo.
The dependency-free runtime package still has separately declared build inputs.

### Trusted Publishing Boundary

[PyPI Adding a trusted publisher, GitHub Actions](https://docs.pypi.org/trusted-publishers/adding-a-publisher/)
requires repository owner, repository name and workflow filename; an Environment
is optional in the service but strongly recommended. The
[using-a-publisher manual section](https://docs.pypi.org/trusted-publishers/using-a-publisher/)
distinguishes OIDC audience `pypi` from `testpypi` and requires the appropriate
workflow permission to obtain a GitHub token.

[Trusted publishing security model](https://docs.pypi.org/trusted-publishers/security-model/)
states that minted API tokens expire no more than 15 minutes after the OIDC
flow, that both token forms are sensitive, and that trusted publishing does not
establish safe code, trustworthy authors or unchanged build contents. Publishers
are registered to projects. Repository writers who can change publishing
workflows remain part of the trust decision; short token lifetime does not
remove that authority.

The sources support proposing separate destination-bound publishers and
protected Environments, credential-free build/quality, and a build-free publisher.
They establish no existing registration or permission in this repository. No
account, Environment, trusted-publisher registration or token was inspected or
changed by this research.

## Decision Impact

The following are recommendations for owner confirmation, not accepted Python
requirements or relaxations of the current V3 baseline:

1. Keep the selected end-to-end scope: one pure Python package, one wheel and
   one sdist, first TestPyPI and then an independent Official PyPI Attempt.
   Require exact-target NBGV facts, frozen Python version, content and clean
   consumer qualification for both formats. Require each registry's own fresh
   byte readback and destination consumption; no promotion of Buddy evidence.
2. Treat the two immutable files as a finite publication set at one destination.
   A later upload can fail after the first is visible. Retain that partial
   effect as failure; stop mutation after failure or ambiguity, without rollback,
   deletion, automatic retry or adoption of another Attempt's result. A partial
   pre-existing release should block normal Live; exceptional recovery remains
   a separate request. Design must reconcile this explicit two-action scenario
   with `WD-OPS-006` and affected terminal/Approval contracts before implementation.
3. Rely on the documented PyPI filename non-reuse boundary and qualify each
   selected destination/profile through independently audited bounded native
   evidence. Do not import the NuGet exception or claim that the read sources
   prove concurrent implementation behavior. Whether the existing non-NuGet
   dependency/evidence basis is sufficient must be resolved explicitly in the
   Python requirements review; missing required evidence keeps Live blocked.
4. Limit the smoke's availability claim to the fresh verification event. Accept
   TestPyPI's possible pruning and PyPI's administrative deletion as external
   lifecycle limits only if the owner agrees; retain audit evidence separately.
   Missing or changed remote state must never yield fresh success or authorize
   restoration. This is a proposed evidence/retention boundary, not a service
   guarantee or permission to weaken accepted requirements silently.
5. Propose `hcoona` as the sole writer/operator and explicit Approval reviewer
   for the dedicated smoke, protected-main targets, and separate project-bound
   OIDC publisher configurations for TestPyPI and PyPI. Self-approval is operator
   confirmation, not independent review. No static-token fallback, third-party
   consumer compatibility promise or malicious-writer isolation is proposed.
   Registry ownership and all setup effects remain unverified and separately
   gated. The exact publisher/tool profile belongs in the later design.

## Required Follow-Through and Limits

The [requirements](../requirements.md) and [handoff](../agent-handoff.md) remain
the accepted domain authorities. The owner's destination selection and Wave
acceptance have not confirmed the above new per-file, retention or trust
decisions. Record that disposition in #843 before reconciling requirements and
the HLD, MLDs and brief LLD in order. No runtime experiment, package build,
native probe, workflow dispatch, registry mutation or account inspection was
performed for this record. Design readiness and end-to-end smoke completion
remain distinct.

The design author rechecks the mutable service upload, filename, retention,
index and OIDC sources when confirming the destination or selecting its
publisher profile, and whenever contrary behavior is reported. The independent
evidence reviewer evaluates the outcome. The Wave review selecting
implementation is the fallback event. Material changes update this record and
dependent decisions before execution; uncertainty blocks only the affected
capability. Recheck backend/build documentation when selecting or changing its
version. The pinned repository-code finding instead requires reevaluation when
the selected adapter source changes.
