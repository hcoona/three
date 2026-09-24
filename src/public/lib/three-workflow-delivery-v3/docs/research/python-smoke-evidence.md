# Python Smoke Destination and Build Evidence

This public-source research supports the Python requirements confirmation in
[Issue #843](https://github.com/hcoona/three/issues/843), under the accepted
[Python design Wave](https://github.com/hcoona/three/blob/53b0aaa75dad1aac238f59127a4c1fd63a6642cc/docs/delivery-wave.md#define-the-python-v3-smoke-scope-and-destination).
The owner selected TestPyPI followed by production PyPI. This record does not
confirm requirements, select an implementation, admit a destination, or grant
publication. npm and NuGet remain complete under their existing authorities.

Workflow-delivery maintainers produce and maintain this evidence. Requirements,
design and independent evidence reviewers consume it to avoid treating Python
as a copy of either single-archive GitHub Packages slice. The project research
carrier preserves recoverable sources and their limits; the Issue owns owner disposition of
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
reads a named environment variable. Those are available backend mechanisms, not empirical evidence of V3
consumer behavior. The [Python Model](../repository-model-release-unit-mld.md#python-smoke-model)
selects isolated static metadata materialization; the later build and clean
sdist consumer must validate that choice without re-resolving NBGV, ambient
overrides or an undeclared Git requirement.

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

### Hosted Runtime Permission Boundary

The implementation source recheck on 2026-09-24 includes GitHub's
[App permission matrix](https://docs.github.com/en/rest/authentication/permissions-required-for-github-apps)
and [workflow token permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions).
Repository invitations/teams require Administration read, Environment
variables/secrets require Environments read, and repository Actions variables
require Variables read. Those categories are not available workflow
`GITHUB_TOKEN` permission keys. Collaborator inventory is Metadata read;
workflow review history and Environment identity use Actions read. These are
source findings, not observed successful or denied calls in this slice.

The implementation therefore preserves the accepted protected configuration
attestation/runtime freshness split. A normal job does not enumerate the
administrative configuration or substitute an empty inventory on failure.
The [Python LLD](../hcoona-release-smoke-python-lld.md#hosted-integration)
states the remaining runtime and attestation responsibilities. The later
separately authorized platform validation must prove the actual endpoints and
native approval/deployment binding with the normal job token.

GitHub's [hosted-runner communication requirements](https://docs.github.com/en/actions/reference/runners/github-hosted-runners#communication-requirements-for-github-hosted-runners)
list `*.actions.githubusercontent.com` for retrieving OIDC tokens. The
[OIDC environment-variable contract](https://docs.github.com/en/actions/reference/security/oidc#requesting-the-jwt-using-environment-variables)
provides `ACTIONS_ID_TOKEN_REQUEST_URL` and its request token. These sources
support using that supplied HTTPS URL with the documented hosted-runner host
family and selected audience. The JWT issuer is a separate concept. No OIDC
request was made to establish this source finding.

## Decision Impact and Limits

The owner [confirmed the requirements packet](https://github.com/hcoona/three/issues/843#issuecomment-5822043601)
on 2026-09-24. [`WD-PY-*`](../requirements.md#python-smoke-slice) owns the
resulting product/trust/evidence decisions; the HLD and MLDs own their
realization. The material choices are partial two-file failure, an explicit
Python per-file dependency/evidence basis and bounded availability, plus the
sole-writer/operator Approval boundary. Acceptance is an owner decision, not a
new service finding. It does not turn the TestPyPI dependency into a documented
guarantee or promote finite future probes into universal proof.

The design and implementation source rechecks re-read the cited service/build
sources on 2026-09-24 when selecting the design and concrete profile. They still support one-file upload, PyPI filename
non-reuse, TestPyPI pruning, distinct OIDC audiences and static Hatch metadata;
no source establishes cross-file atomicity or implemented V3 compatibility.
The [Python LLD](../hcoona-release-smoke-python-lld.md) selects a bounded
one-set/two-operation action and static staged metadata. Those are design
choices whose implementation and native gates remain unpassed.

No runtime experiment, package build, native probe, workflow dispatch,
registry mutation or account inspection was performed for this record.
Design readiness and end-to-end smoke completion remain distinct.

The design author rechecks the mutable service upload, filename, retention,
index and OIDC sources when confirming the destination or selecting its
publisher profile, and whenever contrary behavior is reported. The independent
evidence reviewer evaluates the outcome. The Wave review selecting
implementation is the fallback event. Material changes update this record and
dependent decisions before execution; uncertainty blocks only the affected
capability. Recheck backend/build documentation when selecting or changing its
version. The pinned repository-code finding instead requires reevaluation when
the selected adapter source changes.
