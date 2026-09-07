"""Read-only, operator-local native state capture; never an acceptance verdict.

The caller supplies prior disposable-package approval and a local read token.
gh uses its existing authentication independently; no token is renamed or
installed. Official gh pagination establishes REST completeness, not raw
version_count semantics. Registry inventory must agree with that complete read.
Reads are sequential, not an atomic destination snapshot.

Fresh private audit directories retain raw successful gh output and HTTP bodies.
Failures leave partial evidence, never a completed capture or automatic retry.
Deleted facts and the documented restoration inference stay acceptance-local.
"""

from __future__ import annotations

import hashlib
import os
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING, Protocol
from urllib.parse import quote, urlsplit, urlunsplit

from three_workflow_delivery_v3.acceptance.native_npm import (
    AcceptanceState,
    ObservedContent,
    PackageControl,
    RestorabilityEvidence,
    TombstoneState,
    VersionIdentity,
)
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    _validate_npm_coordinates,
    inspect_npm_fixture,
)
from three_workflow_delivery_v3.acceptance.npm_probe import NpmProbeRequest
from three_workflow_delivery_v3.adapters.github_packages import (
    DEFAULT_MAX_PAGES,
    DEFAULT_METADATA_LIMIT_BYTES,
    DEFAULT_TARBALL_LIMIT_BYTES,
    DEFAULT_TIMEOUT_SECONDS,
    GITHUB_PAGE_SIZE,
    HTTP_OK,
    MAX_REDIRECTS,
    GitHubPackagesHttpTransport,
    _allowed_url,
    _identity_encoding,
    _npm_transport_headers,
    _response_policy_ok,
    redirect_headers,
)
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.github_packages import (
        GitHubPackagesTransport,
    )
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.release.eligibility import (
        DisposablePackagePreconditions,
    )

GITHUB_API_VERSION = "2026-03-10"
_REGISTRY = "https://npm.pkg.github.com"


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = "capture requires a JSON object"
        raise TypeError(message)
    return value


def _array(value: JsonValue) -> list[JsonValue]:
    if not isinstance(value, list):
        message = "missing or malformed version inventory pages"
        raise TypeError(message)
    return value


def _string(value: JsonValue) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = "capture requires a nonempty exact string"
        raise ValueError(message)
    return value


def _integer(value: JsonValue) -> int:
    if type(value) is not int:
        message = "capture requires an exact integer ID"
        raise ValueError(message)
    return value


def _aware(value: datetime) -> None:
    _require(
        type(value) is datetime and value.utcoffset() is not None,
        "capture clock and original deletion bound must be timezone-aware",
    )


@dataclass(frozen=True)
class OriginalDeletionContext:
    """Original identity plus a bound recorded BEFORE authorized delete.

    Later discovery cannot refresh this anchor. This class authorizes nothing;
    the caller preserves it across deleted duplicate and restored captures.
    """

    original_control: PackageControl
    original_version: VersionIdentity
    deletion_lower_bound_at: datetime

    def __post_init__(self) -> None:
        """Reject untyped identities and an unaware original time."""
        _require(
            type(self.original_control) is PackageControl
            and type(self.original_version) is VersionIdentity,
            "original deletion requires typed original identities",
        )
        _aware(self.deletion_lower_bound_at)

    def to_document(self) -> dict[str, JsonValue]:
        """Retain original provenance outside the canonical semantic state."""
        return {
            "original_control": self.original_control.to_document(),
            "original_version": self.original_version.to_document(),
            "deletion_lower_bound_at": (
                self.deletion_lower_bound_at.isoformat()
            ),
        }


@dataclass(frozen=True)
class CaptureFile:
    """One retained file, named relative to the caller's audit directory."""

    filename: str
    sha256: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return an evidence-manifest entry."""
        return {"filename": self.filename, "sha256": self.sha256}


@dataclass(frozen=True)
class NpmStateCapture:
    """Observed state and provenance; active IDs stay outside semantic state."""

    state: AcceptanceState
    captured_at: datetime
    original_deletion: OriginalDeletionContext | None
    files: tuple[CaptureFile, ...]
    active_inventory: tuple[VersionIdentity, ...]


class GhCommandRunner(Protocol):
    """Return complete successful gh JSON bytes or raise, without retrying."""

    def run(self, argv: tuple[str, ...], *, max_bytes: int) -> bytes:
        """Use a bounded process; timeout, truncation and nonzero exit fail."""
        ...


class GhCliCommandRunner:
    """Reuse bounded local process mechanics with existing gh authentication.

    No REST client, Link parser, shell, installation or retry engine is added.
    The shared process runner combines stdout/stderr: diagnostics make JSON
    invalid rather than being discarded to upgrade a partial read.
    """

    def __init__(self, repository_root: Path) -> None:
        """Bind only the local process working directory."""
        self._root = repository_root

    def run(self, argv: tuple[str, ...], *, max_bytes: int) -> bytes:
        """Do not print or retain authentication, environment or diagnostics."""
        environment = {
            key: value
            for key, value in os.environ.items()
            if key
            in {
                "PATH",
                "HOME",
                "SystemRoot",
                "WINDIR",
                "USERPROFILE",
                "XDG_CONFIG_HOME",
                "GH_CONFIG_DIR",
                "GH_TOKEN",
                "GITHUB_TOKEN",
            }
        }
        environment["GH_PROMPT_DISABLED"] = "1"
        outcome = IsolatedNpmProcessRunner().run(
            argv,
            cwd=self._root,
            environment=environment,
            timeout=DEFAULT_TIMEOUT_SECONDS,
            output_limit=max_bytes,
        )
        _require(
            outcome.classification == "definitive-success"
            and not outcome.truncated,
            "gh capture failed, timed out, or exceeded its output bound",
        )
        parse_json_strict(outcome.output)
        return outcome.output


class _Audit:
    def __init__(self, directory: Path) -> None:
        directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.directory = directory
        self.files: list[CaptureFile] = []
        self.sources: list[JsonValue] = []

    def write(self, filename: str, body: bytes) -> CaptureFile:
        descriptor = os.open(
            self.directory / filename,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL,
            0o600,
        )
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(body)
        item = CaptureFile(
            filename, "sha256:" + hashlib.sha256(body).hexdigest()
        )
        self.files.append(item)
        return item

    def raw(self, filename: str, body: bytes, source: str) -> None:
        item = self.write(filename, body)
        # Preserve ordinary requested URLs, never signed redirect URLs.
        parsed = urlsplit(source)
        safe_source = (
            source
            if source.startswith("/")
            else urlunsplit(parsed._replace(query="", fragment=""))
        )
        self.sources.append({**item.to_document(), "source": safe_source})


def _gh_read(
    runner: GhCommandRunner, audit: _Audit, route: str, *, inventory: str | None
) -> JsonValue:
    argv = (
        "gh",
        "api",
        "--hostname",
        "github.com",
        "--method",
        "GET",
        "-H",
        "Accept: application/vnd.github+json",
        "-H",
        f"X-GitHub-Api-Version: {GITHUB_API_VERSION}",
        *(("--paginate", "--slurp") if inventory is not None else ()),
        route,
    )
    limit = DEFAULT_METADATA_LIMIT_BYTES * (
        DEFAULT_MAX_PAGES if inventory is not None else 1
    )
    body = runner.run(argv, max_bytes=limit)
    _require(
        type(body) is bytes and len(body) <= limit, "unbounded gh response"
    )
    audit.raw(
        f"github-{inventory}-pages.json"
        if inventory is not None
        else "github-package.json",
        body,
        route,
    )
    return parse_json_strict(body)


def _inventory(value: JsonValue) -> tuple[VersionIdentity, ...]:
    pages = _array(value)
    _require(
        0 < len(pages) <= DEFAULT_MAX_PAGES,
        "missing or unbounded gh slurped pages",
    )
    identities: list[VersionIdentity] = []
    for page_value in pages:
        page = _array(page_value)
        _require(
            len(page) <= GITHUB_PAGE_SIZE,
            "missing or malformed version inventory page",
        )
        for item in page:
            entry = _object(item)
            if "metadata" in entry:
                _require(
                    _object(entry["metadata"]).get("package_type") == "npm",
                    "version metadata package type mismatch",
                )
            identities.append(
                VersionIdentity(_integer(entry["id"]), _string(entry["name"]))
            )
    _require(
        len({item.name for item in identities}) == len(identities)
        and len({item.version_id for item in identities}) == len(identities),
        "conflicting version inventory names or IDs",
    )
    return tuple(sorted(identities, key=lambda item: item.name))


def _control(value: JsonValue, package: str) -> PackageControl:
    document = _object(value)
    owner = document.get("owner")
    _require(
        owner is None or _object(owner).get("login") == "hcoona",
        "explicit package owner mismatch",
    )
    _require(
        document.get("package_type") == "npm"
        and document.get("name") == package.removeprefix("@hcoona/")
        and document.get("visibility") == "public"
        and _object(document["repository"]).get("full_name") == "hcoona/three",
        "package control does not match the approved public coordinate",
    )
    return PackageControl(
        container_id=_integer(document["id"]),
        full_scoped_name=package,
        owner="hcoona",
        visibility="public",
        repository_full_name="hcoona/three",
        exposed_access=(),
    )


def _registry_read(  # noqa: PLR0913
    transport: GitHubPackagesTransport,
    audit: _Audit,
    token: str,
    url: str,
    *,
    filename: str,
    stage: str,
) -> bytes:
    _require(_allowed_url(url, stage=stage), "off-policy requested URL")
    limit = (
        DEFAULT_TARBALL_LIMIT_BYTES
        if stage == "tarball"
        else DEFAULT_METADATA_LIMIT_BYTES
    )
    headers = _npm_transport_headers(token, tarball=stage == "tarball")
    if stage != "tarball":
        headers = tuple(
            (name, "application/json" if name == "Accept" else value)
            for name, value in headers
        )
    response = transport.get(
        url,
        headers=redirect_headers(
            source_url=_REGISTRY,
            target_url=url,
            headers=headers,
        ),
        timeout=DEFAULT_TIMEOUT_SECONDS,
        max_bytes=limit,
    )
    _require(len(response.body) <= limit, "unbounded registry response")
    audit.raw(filename, response.body, url)
    lengths = [
        value
        for name, value in response.headers
        if name.lower() == "content-length"
    ]
    _require(
        response.status == HTTP_OK
        and response.complete
        and not response.truncated
        and _identity_encoding(response)
        and sum(
            name.lower() == "content-encoding" for name, _ in response.headers
        )
        <= 1
        and (
            not lengths
            or (
                len(lengths) == 1
                and lengths[0].isascii()
                and lengths[0].isdecimal()
                and int(lengths[0]) == len(response.body)
            )
        )
        and len(response.redirects) <= MAX_REDIRECTS
        and _response_policy_ok(response, requested_url=url, stage=stage)
        and (
            stage == "tarball"
            or all(hop == url for hop in (*response.redirects, response.url))
        ),
        "incomplete or off-policy registry response",
    )
    return response.body


def _tombstone(
    context: OriginalDeletionContext,
    control: PackageControl,
    active: tuple[VersionIdentity, ...],
    deleted: tuple[VersionIdentity, ...],
    captured_at: datetime,
) -> TombstoneState:
    _require(
        control == context.original_control,
        "original package control or namespace changed",
    )
    _require(
        not {item.name for item in active} & {item.name for item in deleted}
        and not {item.version_id for item in active}
        & {item.version_id for item in deleted},
        "active and deleted identities overlap",
    )
    original = context.original_version
    matches = tuple(
        item
        for item in (*active, *deleted)
        if item.name == original.name or item.version_id == original.version_id
    )
    _require(matches == (original,), "original target identity is unprovable")
    _require(
        captured_at >= context.deletion_lower_bound_at,
        "inspection precedes the original deletion bound",
    )
    return TombstoneState(
        deleted_versions=deleted,
        target=matches[0],
        restorability=(
            RestorabilityEvidence(
                context.original_control,
                context.original_version,
                context.deletion_lower_bound_at,
                captured_at,
            )
            if original in deleted
            else None
        ),
    )


def _now() -> datetime:
    return datetime.now(UTC)


def capture_npm_state(  # noqa: PLR0913
    *,
    approved_disposable_package_preconditions: DisposablePackagePreconditions,
    scenarios: tuple[NpmFixtureSpec, ...],
    token: str,
    repository_root: Path,
    audit_directory: Path,
    gh_runner: GhCommandRunner | None = None,
    transport: GitHubPackagesTransport | None = None,
    original_deletion: OriginalDeletionContext | None = None,
    clock: Callable[[], datetime] = _now,
) -> NpmStateCapture:
    """Capture complete inventories and actual active scenario fixture bytes.

    Scenario specs select versions, not expected presence, target or content.
    Each selected active version is independently inspected. Inactive selectors
    have no content operand. The caller applies comparison/presence gates.
    Only explicit original deletion context enables deleted-state reads.
    """
    _require(type(token) is str and bool(token), "local read token is required")
    _require(
        type(scenarios) is tuple
        and bool(scenarios)
        and all(type(spec) is NpmFixtureSpec for spec in scenarios)
        and len({spec.version for spec in scenarios}) == len(scenarios),
        "capture requires unique typed scenario version selectors",
    )
    for spec in scenarios:
        NpmProbeRequest(spec, approved_disposable_package_preconditions)
        _validate_npm_coordinates(spec, repository_root)
    package = approved_disposable_package_preconditions.package
    _require(package.startswith("@hcoona/"), "capture requires the USER owner")
    if original_deletion is not None:
        _require(
            type(original_deletion) is OriginalDeletionContext
            and original_deletion.original_control.full_scoped_name == package
            and original_deletion.original_version.name
            in {spec.version for spec in scenarios},
            "original deletion must bind this package and a selected version",
        )
    audit = _Audit(audit_directory)
    runner = (
        gh_runner
        if gh_runner is not None
        else GhCliCommandRunner(repository_root)
    )
    http = transport if transport is not None else GitHubPackagesHttpTransport()
    route = "/users/hcoona/packages/npm/" + quote(
        package.removeprefix("@hcoona/"), safe=""
    )
    control = _control(_gh_read(runner, audit, route, inventory=None), package)
    active = _inventory(
        _gh_read(
            runner,
            audit,
            route + "/versions?state=active&per_page=100",
            inventory="active",
        )
    )
    deleted = (
        _inventory(
            _gh_read(
                runner,
                audit,
                route + "/versions?state=deleted&per_page=100",
                inventory="deleted",
            )
        )
        if original_deletion is not None
        else ()
    )
    packument = _object(
        parse_json_strict(
            _registry_read(
                http,
                audit,
                token,
                f"{_REGISTRY}/{quote(package, safe='')}",
                filename="npm-packument.json",
                stage="npm-metadata",
            )
        )
    )
    versions = _object(packument["versions"])
    _require(
        packument.get("name") == package
        and set(versions) == {item.name for item in active},
        "packument and complete active inventory disagree",
    )
    for version, value in versions.items():
        manifest = _object(value)
        _require(
            manifest.get("name") == package
            and manifest.get("version") == version,
            "packument manifest identity mismatch",
        )
    tags = tuple(
        sorted(
            (_string(name), _string(value))
            for name, value in _object(packument["dist-tags"]).items()
        )
    )
    contents: list[ObservedContent] = []
    for index, spec in enumerate(
        sorted(scenarios, key=lambda item: item.version)
    ):
        if spec.version not in versions:
            continue
        dist = _object(_object(versions[spec.version])["dist"])
        body = _registry_read(
            http,
            audit,
            token,
            _string(dist["tarball"]),
            filename=f"scenario-{index}.tgz",
            stage="tarball",
        )
        observed = inspect_npm_fixture(body, repository_root=repository_root)
        _require(
            observed.version == spec.version
            and parse_canonical_json(observed.witness)["package"] == package,
            "actual fixture identity does not match the selected version",
        )
        contents.append(observed)
    captured_at = clock()
    _aware(captured_at)
    state = AcceptanceState(
        control,
        tuple(item.name for item in active),
        tags,
        tuple(contents),
        _tombstone(original_deletion, control, active, deleted, captured_at)
        if original_deletion is not None
        else None,
    )
    audit.write("state.json", canonicalize(state.to_document()))
    scenario_versions: list[JsonValue] = [
        spec.version
        for spec in sorted(scenarios, key=lambda item: item.version)
    ]
    audit.write(
        "capture.json",
        canonicalize(
            {
                "schema": "workflow-delivery-v3/native-npm-capture/v1",
                "captured_at": captured_at.isoformat(),
                "github_api_version": GITHUB_API_VERSION,
                "approved_disposable_package_preconditions": (
                    approved_disposable_package_preconditions.to_document()
                ),
                "scenario_versions": scenario_versions,
                "original_deletion": (
                    original_deletion.to_document()
                    if original_deletion is not None
                    else None
                ),
                "state_digest": state.digest(),
                "raw_responses": audit.sources,
            }
        ),
    )
    return NpmStateCapture(
        state, captured_at, original_deletion, tuple(audit.files), active
    )
