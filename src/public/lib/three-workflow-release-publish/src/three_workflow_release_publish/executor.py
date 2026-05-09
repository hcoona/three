"""Thin publish adapters for workflow-release publish requests."""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import tarfile
import zipfile
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from email.parser import Parser
from pathlib import Path
from urllib import parse
from urllib import request as urlrequest

from packaging.utils import canonicalize_name
from packaging.version import InvalidVersion, Version
from three_workflow_release_contracts import (
    ContractValidationError,
    validate_contract,
)

Json = dict[str, object]
type Runner = Callable[
    [Sequence[str], Path, Mapping[str, str] | None],
    subprocess.CompletedProcess[str],
]

_DOTNET_PACKAGE_KINDS = {"nuget", "snupkg"}
_NUGET_IDENTITY_NUMERIC_PARTS = 3
_NUGET_MAX_NUMERIC_PARTS = 4
_RUBYGEMS_TRUSTED_PUBLISHER_AUDIENCE = "rubygems.org"


class PublishExecutorError(ValueError):
    """Raised when a publish request cannot be fulfilled safely."""

    def __init__(
        self,
        message: str,
        *,
        code: str = "PUBLISH_FAILED",
        phase: str = "execution",
        details: Mapping[str, object] | None = None,
    ) -> None:
        """Create a publish executor error."""
        self.code = code
        self.phase = phase
        self.details = dict(details or {})
        super().__init__(message)


@dataclass(frozen=True, slots=True)
class _ArtifactInput:
    artifact_id: str
    path: Path
    upload_path: Path
    role: str
    kind_family: str
    concrete_kind: str


@dataclass(frozen=True, slots=True)
class _GithubAttestationContext:
    repo: str
    source_digest: str
    signer_workflow: str


def execute_publish(
    request: Mapping[str, object],
    repo_root: Path,
    *,
    runner: Runner | None = None,
    work_dir: Path | None = None,
    check_commit: bool = True,
) -> Json:
    """Execute one closed publish request and return a publish-result."""
    normalized_request = _validate_request(request)
    resolved_repo = repo_root.resolve()
    run = runner or _subprocess_runner
    if check_commit:
        _check_commit(normalized_request, resolved_repo, run)

    family = str(
        _mapping(normalized_request["target-instance-snapshot"])["family"]
    )
    publish_node = _mapping(normalized_request["publish-node"])
    if publish_node.get("publish-disposition") != "publish":
        msg = "publish executor only accepts publish-disposition publish"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")

    artifacts = _artifact_inputs(normalized_request, resolved_repo)
    _verify_artifact_files(normalized_request, artifacts)
    upload_artifacts = _stage_upload_filenames(
        normalized_request,
        artifacts,
        resolved_repo,
        work_dir,
    )
    _verify_identity(
        normalized_request, family, upload_artifacts, run, resolved_repo
    )
    evidence = _execute_family(
        normalized_request,
        family,
        upload_artifacts,
        resolved_repo,
        run,
    )
    result = _publish_result(normalized_request, evidence)
    validate_contract(result)
    return result


def _validate_request(request: Mapping[str, object]) -> Mapping[str, object]:
    """Validate and normalize a publish request object."""
    try:
        validate_contract(request)
    except ContractValidationError as exc:
        msg = "publish request violates the closed contract"
        raise PublishExecutorError(
            msg,
            code="PUBLISH_INVALID_INPUT",
            phase="validation",
            details={"validation-error": str(exc)},
        ) from exc
    _require_github_release_asset_attestations(request)
    return request


def _require_github_release_asset_attestations(
    request: Mapping[str, object],
) -> None:
    """Fail closed when GitHub Release attestations are absent."""
    snapshot = _mapping(request["target-instance-snapshot"])
    if snapshot.get("family") != "github-release":
        return
    if "github-release-asset-attestations" not in request:
        msg = (
            "GitHub Release publish request is missing attached "
            "asset attestations"
        )
        raise PublishExecutorError(
            msg,
            code="PUBLISH_INVALID_INPUT",
            phase="validation",
            details={"field": "github-release-asset-attestations"},
        )
    outputs = _mapping(request["github-release-asset-attestations"])
    node = _mapping(request["publish-node"])
    expected = set(_mapping(request["artifacts"]).keys())
    node_artifacts = node.get("artifact-ids")
    if isinstance(node_artifacts, Sequence) and not isinstance(
        node_artifacts, str
    ):
        expected = {str(artifact_id) for artifact_id in node_artifacts}
    if set(outputs) != expected:
        msg = "GitHub Release publish request has incomplete asset attestations"
        raise PublishExecutorError(
            msg,
            code="PUBLISH_INVALID_INPUT",
            phase="validation",
            details={
                "field": "github-release-asset-attestations",
                "expected-artifact-ids": sorted(expected),
                "actual-artifact-ids": sorted(outputs),
            },
        )


def _check_commit(
    request: Mapping[str, object],
    repo_root: Path,
    runner: Runner,
) -> None:
    """Fail unless the checkout is pinned to the publish commit."""
    result = _run_checked(
        [shutil.which("git") or "git", "rev-parse", "HEAD"],
        repo_root,
        runner,
        code="PUBLISH_CHECKOUT_FAILED",
        phase="materialization",
    )
    actual = result.stdout.strip()
    expected = str(request["commit-sha"])
    if actual != expected:
        msg = (
            f"checkout HEAD {actual!r} does not match publish "
            f"commit {expected!r}"
        )
        raise PublishExecutorError(
            msg,
            code="PUBLISH_CHECKOUT_FAILED",
            phase="materialization",
            details={"actual": actual, "expected": expected},
        )


def _artifact_inputs(
    request: Mapping[str, object], repo_root: Path
) -> tuple[_ArtifactInput, ...]:
    """Return receipted artifact inputs from a publish request."""
    inputs: list[_ArtifactInput] = []
    artifacts = _mapping(request["artifacts"])
    for artifact_id, payload in artifacts.items():
        entry = _mapping(payload)
        artifact = _mapping(entry["artifact"])
        path = _safe_repo_path(repo_root, str(entry["input-path"]))
        inputs.append(
            _ArtifactInput(
                artifact_id=str(artifact_id),
                path=path,
                upload_path=path,
                role=str(artifact["role"]),
                kind_family=str(artifact["kind-family"]),
                concrete_kind=str(artifact["concrete-kind"]),
            )
        )
    return tuple(sorted(inputs, key=lambda item: item.artifact_id))


def _verify_artifact_files(
    request: Mapping[str, object], artifacts: Sequence[_ArtifactInput]
) -> None:
    """Verify materialized publish files match request receipts."""
    request_artifacts = _mapping(request["artifacts"])
    for artifact in artifacts:
        if not artifact.path.is_file():
            msg = f"artifact file is missing: {artifact.path}"
            raise PublishExecutorError(
                msg,
                code="PUBLISH_INVALID_INPUT",
                phase="validation",
                details={"artifact-id": artifact.artifact_id},
            )
        entry = _mapping(request_artifacts[artifact.artifact_id])
        data = artifact.path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest != entry.get("sha256") or len(data) != entry.get("byte-size"):
            msg = f"artifact receipt mismatch: {artifact.artifact_id}"
            raise PublishExecutorError(
                msg,
                code="PUBLISH_INVALID_INPUT",
                phase="validation",
                details={"artifact-id": artifact.artifact_id},
            )


def _stage_upload_filenames(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    work_dir: Path | None,
) -> tuple[_ArtifactInput, ...]:
    """Stage artifacts whose target-side filename is frozen in the plan."""
    node = _mapping(request["publish-node"])
    projection = _mapping(node["projection"])
    raw_names = projection.get("final-distribution-filenames-by-artifact-id")
    if raw_names is None:
        raw_names = projection.get("asset-names-by-artifact-id")
    if raw_names is None:
        return tuple(artifacts)
    names = _mapping(raw_names)
    base_dir = work_dir or repo_root / ".three-workflow-release-publish-work"
    stage_dir = base_dir / _safe_stage_name(
        str(request["plan-id"]), str(_publish_node_id(request))
    )
    if stage_dir.exists():
        _remove_tree(stage_dir)
    try:
        stage_dir.mkdir(parents=True)
    except OSError as exc:
        msg = f"publish staging directory could not be created: {exc}"
        raise PublishExecutorError(
            msg,
            code="PUBLISH_OUTPUT_INVALID",
            phase="materialization",
            details={"path": stage_dir.as_posix()},
        ) from exc
    staged: list[_ArtifactInput] = []
    used: set[str] = set()
    for artifact in artifacts:
        planned_name = str(names[artifact.artifact_id])
        if not _safe_filename(planned_name):
            msg = f"planned distribution filename is unsafe: {planned_name!r}"
            raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
        if planned_name in used:
            msg = f"duplicate planned distribution filename: {planned_name!r}"
            raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
        used.add(planned_name)
        upload_path = stage_dir / planned_name
        try:
            shutil.copy2(artifact.path, upload_path)
        except OSError as exc:
            msg = f"artifact could not be staged for upload: {exc}"
            raise PublishExecutorError(
                msg,
                code="PUBLISH_OUTPUT_INVALID",
                phase="materialization",
                details={"artifact-id": artifact.artifact_id},
            ) from exc
        staged.append(
            _ArtifactInput(
                artifact_id=artifact.artifact_id,
                path=artifact.path,
                upload_path=upload_path,
                role=artifact.role,
                kind_family=artifact.kind_family,
                concrete_kind=artifact.concrete_kind,
            )
        )
    return tuple(staged)


def _safe_stage_name(plan_id: str, publish_node_id: str) -> str:
    """Return a stable staging subdirectory name."""
    digest = hashlib.sha256(
        f"{plan_id}\n{publish_node_id}".encode()
    ).hexdigest()
    return digest[:24]


def _safe_filename(name: str) -> bool:
    """Return whether a planned upload filename is a single safe segment."""
    return name not in {"", ".", ".."} and "/" not in name and "\\" not in name


def _verify_identity(
    request: Mapping[str, object],
    family: str,
    artifacts: Sequence[_ArtifactInput],
    runner: Runner,
    cwd: Path,
) -> None:
    """Verify package metadata matches planner-frozen identity."""
    if family == "github-release":
        return
    identity = _mapping(
        _mapping(request["publish-node"])["resolved-publish-identity"]
    )
    expected_name = str(identity["package-name"])
    expected_version = str(identity["version"])
    for artifact in artifacts:
        name, version = _package_metadata(artifact, family, runner, cwd)
        if not _names_match(family, name, expected_name):
            msg = (
                f"{family} artifact {artifact.artifact_id!r} name {name!r} "
                f"does not match frozen name {expected_name!r}"
            )
            raise PublishExecutorError(msg, code="PUBLISH_IDENTITY_MISMATCH")
        if not _versions_match(family, version, expected_version, runner, cwd):
            msg = (
                f"{family} artifact {artifact.artifact_id!r} version "
                f"{version!r} does not match frozen version "
                f"{expected_version!r}"
            )
            raise PublishExecutorError(msg, code="PUBLISH_IDENTITY_MISMATCH")


def _package_metadata(
    artifact: _ArtifactInput,
    family: str,
    runner: Runner,
    cwd: Path,
) -> tuple[str, str]:
    """Read package name and version from the concrete upload file."""
    if family == "pypi":
        return _python_distribution_metadata(artifact.upload_path)
    if family == "npm":
        return _npm_tarball_metadata(artifact.upload_path)
    if family == "nuget":
        return _nuget_package_metadata(artifact.upload_path)
    if family == "rubygems":
        return _rubygem_metadata(artifact.upload_path, runner, cwd)
    msg = f"unsupported package registry family: {family!r}"
    raise PublishExecutorError(msg, code="PUBLISH_UNSUPPORTED_TARGET")


def _python_distribution_metadata(path: Path) -> tuple[str, str]:
    """Read Python wheel or sdist core metadata."""
    if path.suffix == ".whl":
        try:
            with zipfile.ZipFile(path) as archive:
                names = [
                    name
                    for name in archive.namelist()
                    if name.endswith(".dist-info/METADATA")
                ]
                if len(names) != 1:
                    msg = "wheel must contain exactly one METADATA file"
                    raise PublishExecutorError(msg)
                metadata = archive.read(names[0]).decode("utf-8")
        except (OSError, zipfile.BadZipFile, UnicodeDecodeError) as exc:
            msg = "wheel metadata could not be inspected"
            raise PublishExecutorError(msg) from exc
        return _core_metadata_name_version(metadata, "wheel")
    try:
        with tarfile.open(path, "r:gz") as archive:
            members = [
                member
                for member in archive.getmembers()
                if member.isfile() and member.name.endswith("/PKG-INFO")
            ]
            if len(members) != 1:
                msg = "sdist must contain exactly one PKG-INFO file"
                raise PublishExecutorError(msg)
            file_obj = archive.extractfile(members[0])
            if file_obj is None:
                msg = "sdist PKG-INFO could not be read"
                raise PublishExecutorError(msg)
            metadata = file_obj.read().decode("utf-8")
    except (OSError, tarfile.TarError, UnicodeDecodeError) as exc:
        msg = "sdist metadata could not be inspected"
        raise PublishExecutorError(msg) from exc
    return _core_metadata_name_version(metadata, "sdist")


def _core_metadata_name_version(metadata: str, label: str) -> tuple[str, str]:
    """Parse name and version from Python core metadata."""
    parsed = Parser().parsestr(metadata)
    name = parsed.get("Name")
    version = parsed.get("Version")
    if not name or not version:
        msg = f"{label} metadata is missing Name or Version"
        raise PublishExecutorError(msg)
    return name, version


def _npm_tarball_metadata(path: Path) -> tuple[str, str]:
    """Read npm package metadata from a packed tarball."""
    try:
        with tarfile.open(path, "r:gz") as archive:
            package_member = archive.extractfile("package/package.json")
            if package_member is None:
                msg = "npm tarball is missing package/package.json"
                raise PublishExecutorError(msg)
            package_json = json.loads(package_member.read().decode("utf-8"))
    except (
        OSError,
        tarfile.TarError,
        UnicodeDecodeError,
        json.JSONDecodeError,
    ) as exc:
        msg = "npm tarball metadata could not be inspected"
        raise PublishExecutorError(msg) from exc
    if not isinstance(package_json, Mapping):
        msg = "npm package.json must be an object"
        raise PublishExecutorError(msg)
    name = package_json.get("name")
    version = package_json.get("version")
    if not isinstance(name, str) or not isinstance(version, str):
        msg = "npm package.json is missing name or version"
        raise PublishExecutorError(msg)
    return name, version


def _nuget_package_metadata(path: Path) -> tuple[str, str]:
    """Read NuGet package id and version from embedded nuspec metadata."""
    try:
        with zipfile.ZipFile(path) as archive:
            names = [
                name for name in archive.namelist() if name.endswith(".nuspec")
            ]
            if len(names) != 1:
                msg = "NuGet package must contain exactly one .nuspec"
                raise PublishExecutorError(msg)
            nuspec = archive.read(names[0]).decode("utf-8")
    except (OSError, zipfile.BadZipFile, UnicodeDecodeError) as exc:
        msg = "NuGet package metadata could not be inspected"
        raise PublishExecutorError(msg) from exc
    package_id = _xml_text(nuspec, "id")
    version = _xml_text(nuspec, "version")
    if package_id is None or version is None:
        msg = "NuGet metadata is missing id or version"
        raise PublishExecutorError(msg)
    return package_id, version


def _xml_text(document: str, local_name: str) -> str | None:
    """Extract simple XML element text by local name."""
    pattern = (
        rf"<(?:[A-Za-z_][\w.-]*:)?{re.escape(local_name)}>"
        r"([^<]+)"
        rf"</(?:[A-Za-z_][\w.-]*:)?{re.escape(local_name)}>"
    )
    match = re.search(pattern, document)
    return match.group(1).strip() if match is not None else None


def _rubygem_metadata(path: Path, runner: Runner, cwd: Path) -> tuple[str, str]:
    """Read RubyGems specification name and version from a built gem."""
    gem = shutil.which("gem") or "gem"
    name_result = _run_checked(
        [gem, "specification", path.as_posix(), "name", "--yaml"], cwd, runner
    )
    version_result = _run_checked(
        [gem, "specification", path.as_posix(), "version", "--yaml"],
        cwd,
        runner,
    )
    name = _parse_rubygems_scalar(name_result.stdout, "name")
    version = _parse_rubygems_scalar(version_result.stdout, "version")
    if name is None or version is None:
        msg = "RubyGem metadata is missing name or version"
        raise PublishExecutorError(msg)
    return name, version


def _parse_rubygems_scalar(stdout: str, key: str) -> str | None:
    """Parse RubyGems YAML-ish scalar output."""
    for line in stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith(f"{key}:"):
            return stripped.removeprefix(f"{key}:").strip().strip("'\"")
        if stripped.startswith("---"):
            scalar = stripped.removeprefix("---").strip()
            if scalar and not scalar.startswith("!"):
                return scalar.strip("'\"")
    return None


def _names_match(family: str, observed: str, frozen: str) -> bool:
    """Compare package names by registry identity rules."""
    if family == "pypi":
        return canonicalize_name(observed) == canonicalize_name(frozen)
    if family == "nuget":
        return observed.casefold() == frozen.casefold()
    return observed == frozen


def _versions_match(
    family: str,
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare package versions by registry identity rules."""
    if family == "pypi":
        try:
            return Version(observed) == Version(frozen)
        except InvalidVersion as exc:
            msg = "Python package version is not PEP 440 compatible"
            raise PublishExecutorError(msg) from exc
    if family == "nuget":
        return _nuget_normalized_version(observed) == _nuget_normalized_version(
            frozen
        )
    if family == "npm":
        return _npm_versions_match(observed, frozen, runner, cwd)
    if family == "rubygems":
        return _rubygem_versions_match(observed, frozen, runner, cwd)
    return observed == frozen


def _nuget_normalized_version(version: str) -> str:
    """Normalize a NuGet package version for identity comparison."""
    public_version = version.strip().split("+", 1)[0]
    release, separator, prerelease = public_version.partition("-")
    numeric_parts = release.split(".")
    if not 1 <= len(numeric_parts) <= _NUGET_MAX_NUMERIC_PARTS:
        return public_version
    try:
        normalized_numbers = [str(int(part)) for part in numeric_parts]
    except ValueError:
        return public_version
    while len(normalized_numbers) < _NUGET_IDENTITY_NUMERIC_PARTS:
        normalized_numbers.append("0")
    if (
        len(normalized_numbers) == _NUGET_MAX_NUMERIC_PARTS
        and normalized_numbers[3] == "0"
    ):
        normalized_numbers.pop()
    normalized = ".".join(normalized_numbers)
    if not separator:
        return normalized
    prerelease_parts = [
        str(int(part)) if part.isdecimal() else part.lower()
        for part in prerelease.split(".")
    ]
    return f"{normalized}-{'.'.join(prerelease_parts)}"


def _npm_versions_match(
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare npm package versions with node-semver semantics."""
    if observed == frozen:
        return True
    npm_root = _run_checked(
        [shutil.which("npm") or "npm", "root", "-g"], cwd, runner
    )
    script = """
const path = require("node:path");
const payload = JSON.parse(process.argv[1]);
const candidates = [
  "semver",
  path.join(payload.npmGlobalRoot, "npm", "node_modules", "semver"),
];
let semver;
for (const candidate of candidates) {
  try {
    semver = require(candidate);
    break;
  } catch {}
}
if (!semver) process.exit(2);
const observed = semver.clean(payload.observed);
const frozen = semver.clean(payload.frozen);
process.stdout.write(
  observed !== null && frozen !== null && semver.eq(observed, frozen)
    ? "true"
    : "false"
);
"""
    payload = json.dumps(
        {
            "observed": observed,
            "frozen": frozen,
            "npmGlobalRoot": npm_root.stdout.strip(),
        },
        separators=(",", ":"),
    )
    result = _run_checked(
        [shutil.which("node") or "node", "-e", script, payload], cwd, runner
    )
    return result.stdout.strip() == "true"


def _rubygem_versions_match(
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare RubyGem versions with Gem::Version equality."""
    script = (
        "observed = Gem::Version.new(ARGV.fetch(0)); "
        "frozen = Gem::Version.new(ARGV.fetch(1)); "
        "puts(observed == frozen ? 'true' : 'false')"
    )
    result = _run_checked(
        [
            shutil.which("ruby") or "ruby",
            "-rrubygems",
            "-e",
            script,
            observed,
            frozen,
        ],
        cwd,
        runner,
    )
    return result.stdout.strip() == "true"


def _execute_family(
    request: Mapping[str, object],
    family: str,
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Dispatch to a target-family publish adapter."""
    if family == "github-release":
        return _publish_github_release(request, artifacts, repo_root, runner)
    if family == "pypi":
        return _publish_pypi(request, artifacts, repo_root, runner)
    if family == "npm":
        return _publish_npm(request, artifacts, repo_root, runner)
    if family == "nuget":
        return _publish_nuget(request, artifacts, repo_root, runner)
    if family == "rubygems":
        return _publish_rubygems(request, artifacts, repo_root, runner)
    msg = f"unsupported publish target family: {family!r}"
    raise PublishExecutorError(msg, code="PUBLISH_UNSUPPORTED_TARGET")


def _publish_github_release(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Create or converge a GitHub Release according to frozen mode."""
    node = _mapping(request["publish-node"])
    identity = _mapping(node["resolved-publish-identity"])
    destination = _mapping(
        _mapping(request["target-instance-snapshot"])["destination"]
    )
    projection = _mapping(node["projection"])
    tag = str(identity["release-tag"])
    repo = f"{destination['owner']}/{destination['repo']}"
    desired_state = _mapping(node["desired-publish-state"])
    prerelease = desired_state.get("release-state") == "prerelease"
    assets = _github_asset_args(projection, artifacts)
    mode = str(node["publish-mode"])
    if mode not in {
        "create-only",
        "overwrite-mutable",
        "replace-authoritative",
    }:
        msg = f"unsupported GitHub Release publish mode: {mode!r}"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    attestation_evidence = _verify_github_release_asset_attestations(
        request,
        artifacts,
        repo,
        repo_root,
        runner,
    )
    if mode == "create-only":
        command = [
            shutil.which("gh") or "gh",
            "release",
            "create",
            tag,
            *assets,
            "--repo",
            repo,
            "--verify-tag",
            "--title",
            tag,
            "--notes",
            "",
        ]
        if prerelease:
            command.append("--prerelease")
        _run_checked(command, repo_root, runner)
    elif mode in {"overwrite-mutable", "replace-authoritative"}:
        _delete_extra_github_assets(tag, repo, projection, repo_root, runner)
        edit = [
            shutil.which("gh") or "gh",
            "release",
            "edit",
            tag,
            "--repo",
            repo,
            "--verify-tag",
        ]
        edit.append(f"--prerelease={str(prerelease).lower()}")
        _run_checked(
            [
                shutil.which("gh") or "gh",
                "release",
                "upload",
                tag,
                *assets,
                "--repo",
                repo,
                "--clobber",
            ],
            repo_root,
            runner,
        )
        _run_checked(edit, repo_root, runner)
    evidence = _github_release_evidence(tag, repo, repo_root, runner)
    evidence["asset-attestations"] = attestation_evidence
    return evidence


def _github_asset_args(
    projection: Mapping[str, object], artifacts: Sequence[_ArtifactInput]
) -> list[str]:
    """Create gh asset arguments with planner-frozen names and labels."""
    names = _mapping(projection["asset-names-by-artifact-id"])
    labels = _mapping(projection["asset-labels-by-artifact-id"])
    result: list[str] = []
    for artifact in artifacts:
        planned_name = str(names[artifact.artifact_id])
        if artifact.upload_path.name != planned_name:
            msg = "GitHub Release upload asset name does not match plan"
            raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
        label = labels.get(artifact.artifact_id)
        suffix = f"#{label}" if isinstance(label, str) and label != "" else ""
        result.append(f"{artifact.upload_path.as_posix()}{suffix}")
    return result


def _delete_extra_github_assets(
    tag: str,
    repo: str,
    projection: Mapping[str, object],
    repo_root: Path,
    runner: Runner,
) -> None:
    """Delete unplanned assets before authoritative replacement."""
    result = _run_checked(
        [
            shutil.which("gh") or "gh",
            "release",
            "view",
            tag,
            "--repo",
            repo,
            "--json",
            "assets",
        ],
        repo_root,
        runner,
    )
    try:
        payload = json.loads(result.stdout or "{}")
    except json.JSONDecodeError as exc:
        msg = "gh release view emitted invalid JSON"
        raise PublishExecutorError(msg) from exc
    planned = set(_mapping(projection["asset-names-by-artifact-id"]).values())
    for asset in payload.get("assets", []):
        if isinstance(asset, Mapping) and asset.get("name") not in planned:
            _run_checked(
                [
                    shutil.which("gh") or "gh",
                    "release",
                    "delete-asset",
                    tag,
                    str(asset["name"]),
                    "--repo",
                    repo,
                    "--yes",
                ],
                repo_root,
                runner,
            )


def _github_release_evidence(
    tag: str, repo: str, repo_root: Path, runner: Runner
) -> Json:
    """Return small evidence from GitHub Release after mutation."""
    result = _run_checked(
        [
            shutil.which("gh") or "gh",
            "release",
            "view",
            tag,
            "--repo",
            repo,
            "--json",
            "url,assets,isPrerelease",
        ],
        repo_root,
        runner,
    )
    try:
        payload = json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        return {"release-tag": tag, "repository": repo}
    evidence: Json = {
        "release-tag": tag,
        "repository": repo,
    }
    if isinstance(payload, Mapping):
        if isinstance(payload.get("url"), str):
            evidence["url"] = payload["url"]
        if isinstance(payload.get("isPrerelease"), bool):
            evidence["is-prerelease"] = payload["isPrerelease"]
        assets = payload.get("assets")
        if isinstance(assets, list):
            evidence["asset-names"] = sorted(
                asset["name"]
                for asset in assets
                if isinstance(asset, Mapping)
                and isinstance(asset.get("name"), str)
            )
    return evidence


def _verify_github_release_asset_attestations(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo: str,
    repo_root: Path,
    runner: Runner,
) -> dict[str, Json]:
    """Fail closed unless every GitHub Release asset has verified provenance."""
    node = _mapping(request["publish-node"])
    attestation = _mapping(node["attestation"])
    signer_workflow = str(attestation["signer-workflow"])
    context = _GithubAttestationContext(
        repo=repo,
        source_digest=str(request["commit-sha"]),
        signer_workflow=signer_workflow,
    )
    action_outputs = _mapping(request["github-release-asset-attestations"])
    evidence: dict[str, Json] = {}
    for artifact in artifacts:
        output = _mapping(action_outputs[artifact.artifact_id])
        bundle_path = _attestation_bundle_path(
            repo_root, str(output["bundle-path"])
        )
        if not bundle_path.is_file():
            msg = (
                "GitHub Release asset attestation bundle is missing: "
                f"{output['bundle-path']}"
            )
            raise PublishExecutorError(
                msg,
                code="PUBLISH_ATTESTATION_FAILED",
                phase="verification",
                details={"artifact-id": artifact.artifact_id},
            )
        verified = _verify_github_asset_attestation(
            artifact,
            bundle_path,
            context,
            repo_root,
            runner,
        )
        evidence[artifact.artifact_id] = {
            "asset-name": artifact.upload_path.name,
            "sha256": verified["sha256"],
            "predicate-type": "https://slsa.dev/provenance/v1",
            "signer-workflow": signer_workflow,
            "source-repository": repo,
            "source-digest": str(request["commit-sha"]),
            "attestation-id": str(output["attestation-id"]),
            "attestation-url": str(output["attestation-url"]),
            "bundle-path": str(output["bundle-path"]),
        }
        if "storage-record-ids" in output:
            evidence[artifact.artifact_id]["storage-record-ids"] = str(
                output["storage-record-ids"]
            )
    return evidence


def _verify_github_asset_attestation(
    artifact: _ArtifactInput,
    bundle_path: Path,
    context: _GithubAttestationContext,
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Verify an uploaded GitHub Release asset's SLSA provenance attestation."""
    result = _run_checked(
        [
            shutil.which("gh") or "gh",
            "attestation",
            "verify",
            artifact.upload_path.as_posix(),
            "--bundle",
            bundle_path.as_posix(),
            "--repo",
            context.repo,
            "--predicate-type",
            "https://slsa.dev/provenance/v1",
            "--signer-workflow",
            context.signer_workflow,
            "--source-digest",
            context.source_digest,
            "--format",
            "json",
        ],
        repo_root,
        runner,
        code="PUBLISH_ATTESTATION_FAILED",
        phase="verification",
    )
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        msg = "gh attestation verify emitted invalid JSON"
        raise PublishExecutorError(
            msg, code="PUBLISH_ATTESTATION_FAILED", phase="verification"
        ) from exc
    digest = _sha256_file(artifact.upload_path)
    if not _attestation_subject_verified(
        payload, artifact.upload_path.name, digest
    ):
        msg = (
            "GitHub Release asset attestation does not bind the planned "
            f"asset name and digest: {artifact.upload_path.name}"
        )
        raise PublishExecutorError(
            msg,
            code="PUBLISH_ATTESTATION_FAILED",
            phase="verification",
            details={"artifact-id": artifact.artifact_id},
        )
    return {"sha256": digest}


def _attestation_subject_verified(
    payload: object, subject_name: str, sha256: str
) -> bool:
    """Return whether gh verification JSON contains the expected subject."""
    entries = payload if isinstance(payload, list) else [payload]
    for entry in entries:
        if not isinstance(entry, Mapping):
            continue
        verification = entry.get("verificationResult")
        if not isinstance(verification, Mapping):
            continue
        statement = verification.get("statement")
        if not isinstance(statement, Mapping):
            continue
        subjects = statement.get("subject")
        if not isinstance(subjects, list):
            continue
        for subject in subjects:
            if not isinstance(subject, Mapping):
                continue
            digest = subject.get("digest")
            if (
                subject.get("name") == subject_name
                and isinstance(digest, Mapping)
                and digest.get("sha256") == sha256
            ):
                return True
    return False


def _sha256_file(path: Path) -> str:
    """Return the SHA-256 digest of a file."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _publish_pypi(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish Python distributions using uv trusted publishing."""
    _require_pypi_artifact_set(artifacts)
    files = [artifact.upload_path.as_posix() for artifact in artifacts]
    _run_checked(
        [
            shutil.which("uv") or "uv",
            "publish",
            "--trusted-publishing",
            "always",
            *files,
        ],
        repo_root,
        runner,
    )
    identity = _mapping(
        _mapping(request["publish-node"])["resolved-publish-identity"]
    )
    package_name = str(identity["package-name"])
    version = str(identity["version"])
    normalized = canonicalize_name(package_name)
    return {
        "distribution-filenames": [Path(path).name for path in files],
        "project-url": f"https://pypi.org/project/{normalized}/",
        "release-url": f"https://pypi.org/project/{normalized}/{version}/",
    }


def _publish_npm(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish one npm tarball to npmjs or GitHub Packages."""
    _require_single_artifact(artifacts, "npm-package")
    host = str(
        _mapping(_mapping(request["target-instance-snapshot"])["destination"])[
            "host"
        ]
    )
    package_path = artifacts[0].upload_path
    command = [shutil.which("npm") or "npm", "publish", package_path.as_posix()]
    env = None
    if host == "registry.npmjs.org":
        command.append("--provenance")
        identity = _mapping(
            _mapping(request["publish-node"])["resolved-publish-identity"]
        )
        package_name = str(identity["package-name"])
        if package_name.startswith("@"):
            command.extend(["--access", "public"])
    elif host == "npm.pkg.github.com":
        token = os.environ.get("GITHUB_TOKEN")
        if not token:
            msg = "GITHUB_TOKEN is required for GitHub Packages npm publish"
            raise PublishExecutorError(
                msg, code="PUBLISH_AUTH_FAILED", phase="validation"
            )
        command.extend(["--registry", "https://npm.pkg.github.com"])
        env = {
            "NODE_AUTH_TOKEN": token,
            "npm_config_//npm.pkg.github.com/:_authToken": token,
            "npm_config_always_auth": "true",
        }
    else:
        msg = f"unsupported npm registry host: {host!r}"
        raise PublishExecutorError(msg, code="PUBLISH_UNSUPPORTED_TARGET")
    _run_checked(command, repo_root, runner, env=env)
    return {"package-filename": package_path.name, "registry": host}


def _publish_nuget(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish supported NuGet package members."""
    destination = _mapping(
        _mapping(request["target-instance-snapshot"])["destination"]
    )
    host = str(destination["host"])
    package = _nuget_primary_artifact(artifacts)
    if host == "nuget.org":
        return _publish_nuget_org(package, repo_root, runner)
    if host == "nuget.pkg.github.com":
        return _publish_github_packages_nuget(
            package,
            artifacts,
            destination,
            repo_root,
            runner,
        )
    msg = f"unsupported NuGet registry host: {host!r}"
    raise PublishExecutorError(msg, code="PUBLISH_UNSUPPORTED_TARGET")


def _publish_nuget_org(
    package: _ArtifactInput,
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish a NuGet package to NuGet.org using a trusted-publishing token."""
    source = "https://api.nuget.org/v3/index.json"
    token = os.environ.get("NUGET_API_KEY")
    if not token:
        msg = "NUGET_API_KEY from NuGet/login is required for NuGet.org publish"
        raise PublishExecutorError(
            msg, code="PUBLISH_AUTH_FAILED", phase="validation"
        )
    command = [
        shutil.which("dotnet") or "dotnet",
        "nuget",
        "push",
        package.upload_path.as_posix(),
        "--source",
        source,
        "--api-key",
        token,
        "--skip-duplicate",
    ]
    _run_checked(command, repo_root, runner)
    return {"package-filename": package.upload_path.name, "source": source}


def _publish_github_packages_nuget(
    package: _ArtifactInput,
    artifacts: Sequence[_ArtifactInput],
    destination: Mapping[str, object],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish a NuGet package to GitHub Packages."""
    if any(artifact.concrete_kind == "snupkg" for artifact in artifacts):
        msg = (
            "GitHub Packages NuGet symbols (.snupkg) publication is not "
            "supported by the current publish adapter"
        )
        raise PublishExecutorError(
            msg, code="PUBLISH_UNSUPPORTED_TARGET", phase="validation"
        )
    source = f"https://nuget.pkg.github.com/{destination['owner']}/index.json"
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        msg = "GITHUB_TOKEN is required for GitHub Packages NuGet publish"
        raise PublishExecutorError(
            msg, code="PUBLISH_AUTH_FAILED", phase="validation"
        )
    command = [
        shutil.which("dotnet") or "dotnet",
        "nuget",
        "push",
        package.upload_path.as_posix(),
        "--source",
        source,
        "--api-key",
        token,
    ]
    _run_checked(command, repo_root, runner)
    return {"package-filename": package.upload_path.name, "source": source}


def _publish_rubygems(
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactInput],
    repo_root: Path,
    runner: Runner,
) -> Json:
    """Publish one RubyGem to RubyGems.org or GitHub Packages."""
    _require_single_artifact(artifacts, "rubygem")
    destination = _mapping(
        _mapping(request["target-instance-snapshot"])["destination"]
    )
    host = str(destination["host"])
    package = artifacts[0].upload_path
    command = [shutil.which("gem") or "gem", "push"]
    if host == "rubygems.org":
        token = _exchange_rubygems_trusted_publisher_token()
        command.extend(["--host", "https://rubygems.org"])
        env = {"GEM_HOST_API_KEY": token}
    elif host == "rubygems.pkg.github.com":
        token = os.environ.get("GITHUB_TOKEN")
        if not token:
            msg = (
                "GITHUB_TOKEN is required for GitHub Packages RubyGems publish"
            )
            raise PublishExecutorError(
                msg, code="PUBLISH_AUTH_FAILED", phase="validation"
            )
        command.extend(
            [
                "--host",
                f"https://rubygems.pkg.github.com/{destination['owner']}",
            ]
        )
        env = {"GEM_HOST_API_KEY": f"Bearer {token}"}
    else:
        msg = f"unsupported RubyGems registry host: {host!r}"
        raise PublishExecutorError(msg, code="PUBLISH_UNSUPPORTED_TARGET")
    command.append(package.as_posix())
    _run_checked(command, repo_root, runner, env=env)
    identity = _mapping(
        _mapping(request["publish-node"])["resolved-publish-identity"]
    )
    gem_name = str(identity["package-name"])
    version = str(identity["version"])
    evidence: Json = {
        "gem-filename": package.name,
        "gem-name": gem_name,
        "version": version,
        "registry": host,
    }
    if host == "rubygems.org":
        evidence.update(
            {
                "gem-url": f"https://rubygems.org/gems/{gem_name}",
                "version-url": (
                    f"https://rubygems.org/gems/{gem_name}/versions/{version}"
                ),
            }
        )
    return evidence


def _require_pypi_artifact_set(artifacts: Sequence[_ArtifactInput]) -> None:
    """Require the PyPI one-wheel plus optional-sdist artifact shape."""
    wheel_count = sum(
        1 for artifact in artifacts if artifact.concrete_kind == "wheel"
    )
    sdist_count = sum(
        1 for artifact in artifacts if artifact.concrete_kind == "sdist"
    )
    if (
        wheel_count != 1
        or sdist_count > 1
        or len(artifacts) != wheel_count + sdist_count
    ):
        msg = (
            "PyPI publication requires exactly one wheel and zero or one sdist"
        )
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")


def _exchange_rubygems_trusted_publisher_token() -> str:
    """Exchange a GitHub Actions OIDC token for a RubyGems API token."""
    oidc_token = _github_actions_oidc_token(
        _RUBYGEMS_TRUSTED_PUBLISHER_AUDIENCE
    )
    endpoint = (
        "https://rubygems.org/api/v1/oidc/trusted_publisher/exchange_token"
    )
    payload = json.dumps({"jwt": oidc_token}).encode()
    req = urlrequest.Request(  # noqa: S310
        endpoint,
        data=payload,
        headers={
            "content-type": "application/json",
            "accept": "application/json",
        },
        method="POST",
    )
    try:
        with urlrequest.urlopen(req, timeout=30) as response:  # noqa: S310
            raw = response.read().decode("utf-8")
    except OSError as exc:
        msg = "RubyGems.org trusted publisher token exchange failed"
        raise PublishExecutorError(
            msg,
            code="PUBLISH_AUTH_FAILED",
            phase="execution",
        ) from exc
    try:
        result = json.loads(raw)
    except json.JSONDecodeError as exc:
        msg = "RubyGems.org token exchange emitted invalid JSON"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED") from exc
    token = (
        result.get("rubygems_api_key") if isinstance(result, Mapping) else None
    )
    if not isinstance(token, str) or token == "":
        msg = "RubyGems.org token exchange did not return rubygems_api_key"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED")
    return token


def _github_actions_oidc_token(audience: str) -> str:
    """Request a GitHub Actions OIDC identity token for an audience."""
    token_url = os.environ.get("ACTIONS_ID_TOKEN_REQUEST_URL")
    request_token = os.environ.get("ACTIONS_ID_TOKEN_REQUEST_TOKEN")
    if not token_url or not request_token:
        msg = "GitHub Actions OIDC token request environment is unavailable"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED")
    if parse.urlparse(token_url).scheme != "https":
        msg = "GitHub Actions OIDC token request URL must use HTTPS"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED")
    separator = "&" if "?" in token_url else "?"
    url = f"{token_url}{separator}audience={parse.quote(audience, safe='')}"
    req = urlrequest.Request(  # noqa: S310
        url,
        headers={
            "Authorization": f"bearer {request_token}",
            "Accept": "application/json",
        },
    )
    try:
        with urlrequest.urlopen(req, timeout=30) as response:  # noqa: S310
            raw = response.read().decode("utf-8")
    except OSError as exc:
        msg = "GitHub Actions OIDC token request failed"
        raise PublishExecutorError(
            msg,
            code="PUBLISH_AUTH_FAILED",
            phase="execution",
        ) from exc
    try:
        result = json.loads(raw)
    except json.JSONDecodeError as exc:
        msg = "GitHub Actions OIDC token response was invalid JSON"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED") from exc
    token = result.get("value") if isinstance(result, Mapping) else None
    if not isinstance(token, str) or token == "":
        msg = "GitHub Actions OIDC token response did not include value"
        raise PublishExecutorError(msg, code="PUBLISH_AUTH_FAILED")
    return token


def _require_single_artifact(
    artifacts: Sequence[_ArtifactInput], concrete_kind: str
) -> None:
    """Require exactly one artifact with the requested concrete kind."""
    if len(artifacts) != 1 or artifacts[0].concrete_kind != concrete_kind:
        msg = f"expected exactly one {concrete_kind} artifact"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")


def _single_kind(
    artifacts: Sequence[_ArtifactInput], concrete_kind: str
) -> _ArtifactInput:
    """Return the single artifact for a concrete kind."""
    matches = [
        item for item in artifacts if item.concrete_kind == concrete_kind
    ]
    if len(matches) != 1:
        msg = f"expected exactly one {concrete_kind} artifact"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    if any(
        item.concrete_kind not in _DOTNET_PACKAGE_KINDS for item in artifacts
    ):
        msg = "NuGet publication accepts only NuGet package artifacts"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    return matches[0]


def _nuget_primary_artifact(
    artifacts: Sequence[_ArtifactInput],
) -> _ArtifactInput:
    """Return the primary NuGet package after checking planned members."""
    matches = [item for item in artifacts if item.concrete_kind == "nuget"]
    if len(matches) != 1:
        msg = "expected exactly one nuget artifact"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    if any(
        item.concrete_kind not in _DOTNET_PACKAGE_KINDS for item in artifacts
    ):
        msg = "NuGet publication accepts only NuGet package artifacts"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    if sum(1 for item in artifacts if item.concrete_kind == "snupkg") > 1:
        msg = "NuGet publication accepts at most one snupkg artifact"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    return matches[0]


def _publish_result(
    request: Mapping[str, object],
    evidence: Mapping[str, object],
) -> Json:
    """Create a closed publish-result receipt."""
    node = _mapping(request["publish-node"])
    return {
        "api-version": "three.release.publish-result/v1alpha1",
        "kind": "publish-result",
        "plan-id": request["plan-id"],
        "project-id": node["project-id"],
        "publish-node-id": _publish_node_id(request),
        "target-instance-snapshot-id": node["target-instance-snapshot-id"],
        "resolved-publish-identity": dict(
            _mapping(node["resolved-publish-identity"])
        ),
        "outcome": "published",
        "evidence": dict(evidence),
    }


def _publish_node_id(request: Mapping[str, object]) -> str:
    """Return the closed publish request's explicit publish node id."""
    project = _mapping(request["project"])
    node_ids = project.get("publish-node-ids")
    if not isinstance(node_ids, list):
        msg = "project publish-node-ids must be an array"
        raise PublishExecutorError(msg)
    publish_node_id = str(request["publish-node-id"])
    if publish_node_id in node_ids:
        return publish_node_id
    msg = "publish-node-id is not listed by the project snapshot"
    raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")


def _run_checked(  # noqa: PLR0913
    args: Sequence[str],
    cwd: Path,
    runner: Runner,
    *,
    code: str = "PUBLISH_FAILED",
    phase: str = "execution",
    env: Mapping[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    """Run a subprocess through the injectable runner and require success."""
    try:
        result = runner(args, cwd, env)
    except OSError as exc:
        command = " ".join(args[:3])
        msg = f"{command} could not start: {exc}"
        raise PublishExecutorError(
            msg,
            code=code,
            phase=phase,
            details={"command": tuple(args[:3]), "cwd": cwd.as_posix()},
        ) from exc
    if result.returncode != 0:
        stderr = result.stderr.strip()
        stdout = result.stdout.strip()
        detail = stderr or stdout or f"exit code {result.returncode}"
        command = " ".join(args[:3])
        msg = f"{command} failed: {detail[:2000]}"
        raise PublishExecutorError(
            msg,
            code=code,
            phase=phase,
            details={"command": tuple(args[:3]), "cwd": cwd.as_posix()},
        )
    return result


def _subprocess_runner(
    args: Sequence[str], cwd: Path, env: Mapping[str, str] | None
) -> subprocess.CompletedProcess[str]:
    """Run a text subprocess without invoking a shell."""
    merged_env = os.environ.copy()
    if env is not None:
        merged_env.update(env)
    return subprocess.run(  # noqa: S603
        list(args),
        cwd=cwd,
        env=merged_env,
        check=False,
        capture_output=True,
        text=True,
    )


def _safe_repo_path(repo_root: Path, value: str) -> Path:
    """Resolve a normalized repository-relative path."""
    candidate = Path(value)
    if (
        candidate.is_absolute()
        or "\\" in value
        or "." in candidate.parts
        or ".." in candidate.parts
    ):
        msg = f"path is not normalized repo-relative: {value!r}"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    resolved = (repo_root / candidate).resolve()
    try:
        resolved.relative_to(repo_root)
    except ValueError as exc:
        msg = f"path escapes repository root: {value!r}"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT") from exc
    return resolved


def _attestation_bundle_path(repo_root: Path, value: str) -> Path:
    """Resolve an actions/attest bundle path for offline verification."""
    if not value:
        msg = "GitHub Release asset attestation bundle path is empty"
        raise PublishExecutorError(
            msg, code="PUBLISH_ATTESTATION_FAILED", phase="verification"
        )
    candidate = Path(value)
    if candidate.is_absolute():
        return candidate.resolve()
    return _safe_repo_path(repo_root, value)


def _mapping(value: object) -> Mapping[str, object]:
    """Require a mapping after contract validation."""
    if not isinstance(value, Mapping):
        msg = "expected a JSON object"
        raise PublishExecutorError(msg, code="PUBLISH_INVALID_INPUT")
    return value


def _remove_tree(path: Path) -> None:
    """Remove a directory tree without following symlinked directories."""
    if path.is_symlink() or not path.is_dir():
        path.unlink()
        return
    for child in path.iterdir():
        _remove_tree(child)
    path.rmdir()
