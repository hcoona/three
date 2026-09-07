"""Acceptance-only local administrator; never normal Live or approval.

The operator supplies prior exact disposable approval and existing classic gh
authentication with the documented package read/delete/restore permissions.
No credential, grant, package isolation or independent approval is created.
Publishing remains in the trusted Actions job with its actual GITHUB_TOKEN.
The eight dispatches are acceptance probes only; the separate single final
normal-Live dispatch is never part of this operator.

Every command is attempted once. Failures retain a private partial audit, not
a completed manifest, and never trigger restoration, retry or repair. Retain
this local audit beyond Actions' 45 days when needed. A completed digest is
only candidate Governance evidence after independent native operator audit;
synthetic execution cannot establish origin and nothing installs a generation.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import sys
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Literal
from urllib.parse import quote

from three_workflow_delivery_v3.acceptance.npm_capture import (
    GITHUB_API_VERSION,
    NpmStateCapture,
    OriginalDeletionContext,
    capture_npm_state,
)
from three_workflow_delivery_v3.acceptance.npm_evidence import (
    NpmProbeEvidence,
    read_npm_evidence,
)
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    _validate_npm_coordinates,
)
from three_workflow_delivery_v3.acceptance.npm_probe import NpmProbeRequest
from three_workflow_delivery_v3.acceptance.npm_suite import (
    NpmSuitePlan,
    NpmSuiteResult,
    run_npm_suite,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    DEFAULT_METADATA_LIMIT_BYTES,
    DEFAULT_TIMEOUT_SECONDS,
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
)
from three_workflow_delivery_v3.adapters.npm_runtime import write_private_file
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

    from three_workflow_delivery_v3.acceptance.native_npm import (
        PackageControl,
        VersionIdentity,
    )
    from three_workflow_delivery_v3.adapters.npm_process import NpmProcessRunner
    from three_workflow_delivery_v3.canonical import JsonValue

NPM_SUITE_VERSION = "workflow-delivery-v3/native-npm-suite/v1"
LOWER_LAYER_CONTRACT_REVISION = (
    "wdv3/github-packages-npm-documented-contract/v1"
)
# Suite-owned interpretation of the official contracts below, reviewed
# 2026-09-07. Not a GitHub-issued revision or an API restorability flag.
# The requested GitHub REST API version is recorded separately.
_CONTRACT_SOURCES = (
    "https://docs.github.com/en/rest/actions/workflows#create-a-workflow-dispatch-event",
    "https://docs.github.com/en/rest/packages/packages#delete-package-version-for-a-user",
    "https://docs.github.com/en/rest/packages/packages#restore-package-version-for-a-user",
    "https://cli.github.com/manual/gh_run_download",
)
_REPOSITORY = "hcoona/three"
_REPO_API = f"/repos/{_REPOSITORY}"
_WORKFLOW = "workflow-delivery-v3-native-npm-acceptance.yml"
_WORKFLOW_PATH = ".github/workflows/" + _WORKFLOW
_WORKFLOW_API = f"{_REPO_API}/actions/workflows/{_WORKFLOW}"
_WATCH_TIMEOUT = 20 * 60


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = "expected one JSON object"
        raise ValueError(message)  # noqa: TRY004
    return value


def _positive_id(value: JsonValue) -> int:
    if type(value) is not int or value <= 0:
        message = "expected positive exact ID"
        raise ValueError(message)
    return value


def _authorize(*, disposable: bool, delete_restore: bool) -> None:
    _require(
        disposable is True and delete_restore is True,
        "both explicit prior disposable and delete/restore approvals required",
    )
    _require(
        os.environ.get("GITHUB_ACTIONS", "").lower() != "true",
        "suite administration is operator-local only, never GitHub Actions",
    )


def _now() -> datetime:
    return datetime.now(UTC)


def _digest(path: Path) -> str:
    with path.open("rb") as stream:
        return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()


def _write(path: Path, document: dict[str, JsonValue]) -> None:
    write_private_file(path, canonicalize(document))


class _CaptureGhRunner:
    def __init__(
        self, operator: OperatorLocalNpmOperations, directory: Path
    ) -> None:
        self.operator = operator
        self.directory = directory
        self.count = 0

    def run(self, argv: tuple[str, ...], *, max_bytes: int) -> bytes:
        self.count += 1
        return self.operator.command(
            self.directory,
            f"read-{self.count}",
            argv,
            output_limit=max_bytes,
        )


class OperatorLocalNpmOperations:
    """Concrete fixed-suite IO, bound to one approved plan and protected SHA."""

    def __init__(  # noqa: PLR0913
        self,
        *,
        plan: NpmSuitePlan,
        expected_tooling_sha: str,
        repository_root: Path,
        audit_directory: Path,
        authorized_disposable: bool = False,
        authorized_delete_restore: bool = False,
        runner: NpmProcessRunner | None = None,
        clock: Callable[[], datetime] = _now,
    ) -> None:
        """Perform read-only preflight before allowing any suite operation."""
        _authorize(
            disposable=authorized_disposable,
            delete_restore=authorized_delete_restore,
        )
        _require(type(plan) is NpmSuitePlan, "operator requires one suite plan")
        _require(
            re.fullmatch(r"[0-9a-f]{40}", expected_tooling_sha) is not None,
            "tooling SHA must be a full lowercase commit SHA",
        )
        self.plan = plan
        self.tooling_sha = expected_tooling_sha
        self.root = repository_root.resolve(strict=True)
        self.audit = audit_directory.absolute()
        _require(
            self.audit == self.audit.resolve()
            and not self.audit.is_relative_to(self.root)
            and not self.root.is_relative_to(self.audit),
            "fresh private audit directory must be outside the checkout",
        )
        self.audit.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.runner = (
            runner if runner is not None else IsolatedNpmProcessRunner()
        )
        self.clock = clock
        self.environment = {
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
                "SSL_CERT_FILE",
                "SSL_CERT_DIR",
            }
        }
        self.environment["GH_PROMPT_DISABLED"] = "1"
        self._last_capture: NpmStateCapture | None = None
        self._original_control: PackageControl | None = None
        self._deletion: OriginalDeletionContext | None = None
        self._preflight()

    def command(  # noqa: PLR0913
        self,
        directory: Path,
        name: str,
        argv: tuple[str, ...],
        *,
        category: Literal["json", "empty", "text", "ignored"] = "json",
        timeout: float = DEFAULT_TIMEOUT_SECONDS,
        output_limit: int = DEFAULT_METADATA_LIMIT_BYTES,
    ) -> bytes:
        """Retain bounded API bytes and process facts; omit diagnostics."""
        words: list[JsonValue] = list(argv)
        _write(
            directory / f"{name}.command.json",
            {"argv": words, "expected_output": category},
        )
        outcome = self.runner.run(
            argv,
            cwd=self.root,
            environment=self.environment,
            timeout=timeout,
            output_limit=output_limit,
        )
        successful = outcome.classification == "definitive-success"
        secret_output = any(
            self.environment.get(key, "").encode() in outcome.output
            for key in ("GH_TOKEN", "GITHUB_TOKEN")
            if self.environment.get(key)
        )
        retained = (
            successful
            and category != "ignored"
            and not secret_output
            and (category != "empty" or outcome.output == b"")
        )
        _write(
            directory / f"{name}.process.json",
            {
                "classification": outcome.classification,
                "returncode": outcome.returncode,
                "truncated": outcome.truncated,
                "output_retained": retained,
            },
        )
        if retained:
            write_private_file(directory / f"{name}.raw", outcome.output)
        _require(
            successful
            and not secret_output
            and (not outcome.truncated or category == "ignored"),
            "operator command failed, timed out, or exceeded its output bound",
        )
        if category == "json":
            parse_json_strict(outcome.output)
        elif category == "empty":
            # gh exit success plus empty body, NOT an observed HTTP status.
            _require(outcome.output == b"", "unexpected mutation response body")
        return outcome.output

    def _api(
        self,
        directory: Path,
        name: str,
        route: str,
        *options: str,
        method: str = "GET",
        category: Literal["json", "empty"] = "json",
    ) -> bytes:
        return self.command(
            directory,
            name,
            (
                "gh",
                "api",
                "--hostname",
                "github.com",
                "--method",
                method,
                "-H",
                "Accept: application/vnd.github+json",
                "-H",
                f"X-GitHub-Api-Version: {GITHUB_API_VERSION}",
                route,
                *options,
            ),
            category=category,
        )

    def _main(self, directory: Path) -> None:
        raw = self._api(directory, "main", f"{_REPO_API}/git/ref/heads/main")
        current = _object(parse_json_strict(raw))
        _require(
            current.get("ref") == "refs/heads/main"
            and _object(current.get("object")).get("sha") == self.tooling_sha,
            "current protected main moved or does not match tooling SHA",
        )

    def _preflight(self) -> None:
        directory = self.audit / "preflight"
        directory.mkdir(mode=0o700)
        head = self.command(
            directory,
            "head",
            ("git", "rev-parse", "--verify", "HEAD"),
            category="text",
        )
        _require(
            head == (self.tooling_sha + "\n").encode(), "local HEAD mismatch"
        )
        clean = self.command(
            directory,
            "status",
            ("git", "status", "--porcelain=v1", "--untracked-files=all"),
            category="text",
        )
        _require(clean == b"", "local tracked/untracked worktree must be clean")
        for suffix, request in zip("awvd", self.plan.requests, strict=True):
            spec = request.fixture
            _require(
                spec.package.startswith("@hcoona/")
                and spec.version == f"0.0.0-native.{spec.generation}.{suffix}",
                "operator requires fixed generated USER-package versions",
            )
            _validate_npm_coordinates(spec, self.root)
        user = _object(parse_json_strict(self._api(directory, "user", "/user")))
        _require(
            type(user.get("id")) is int
            and user["id"] == 712433  # noqa: PLR2004
            and user.get("login") == "hcoona",
            "authenticated gh user must be hcoona/712433",
        )
        self._main(directory)
        raw = self._api(directory, "workflow", _WORKFLOW_API)
        workflow = _object(parse_json_strict(raw))
        _require(
            workflow.get("state") == "active"
            and workflow.get("path") == _WORKFLOW_PATH,
            "registered native workflow is inactive or has the wrong path",
        )
        _write(
            directory / "plan.json",
            {
                "tooling_sha": self.tooling_sha,
                "requests": [
                    request.to_document() for request in self.plan.requests
                ],
            },
        )

    def _step(self, label: str) -> Path:
        _require(
            re.fullmatch(r"[a-z][a-z0-9-]*", label) is not None,
            "operation label must be a simple fixed name",
        )
        directory = self.audit / label
        directory.mkdir(mode=0o700, exist_ok=False)
        return directory

    def capture(
        self,
        label: str,
        *,
        plan: NpmSuitePlan,
        original_deletion: OriginalDeletionContext | None = None,
    ) -> NpmStateCapture:
        """Read all four selectors using an ephemeral local gh read token."""
        _require(plan == self.plan, "capture plan mismatch")
        _require(
            original_deletion == self._deletion, "deletion context mismatch"
        )
        directory = self._step("capture-" + label)
        # Secret-only: no output file, diagnostic or command log.
        outcome = self.runner.run(
            ("gh", "auth", "token", "--hostname", "github.com"),
            cwd=self.root,
            environment=self.environment,
            timeout=DEFAULT_TIMEOUT_SECONDS,
            output_limit=4096,
        )
        _require(
            outcome.classification == "definitive-success"
            and not outcome.truncated,
            "local gh read credential unavailable",
        )
        token = outcome.output.decode("utf-8").strip()
        _require(
            bool(token) and not any(c.isspace() for c in token),
            "invalid local gh credential response",
        )
        captured = capture_npm_state(
            approved_disposable_package_preconditions=(
                self.plan.creation.disposable_package_preconditions
            ),
            scenarios=tuple(item.fixture for item in self.plan.requests),
            token=token,
            repository_root=self.root,
            audit_directory=directory / "state",
            gh_runner=_CaptureGhRunner(self, directory),
            original_deletion=original_deletion,
            clock=self.clock,
        )
        if self._original_control is None:
            self._original_control = captured.state.control
        self._last_capture = captured
        return captured

    def probe(self, label: str, request: NpmProbeRequest) -> NpmProbeEvidence:
        """Dispatch once, wait for that run, then read its unique artifact."""
        original = replace(
            request, fixture=replace(request.fixture, variant="original")
        )
        _require(
            original in self.plan.requests, "probe request outside bound plan"
        )
        directory = self._step("probe-" + label)
        _write(directory / "request.json", request.to_document())
        payload = directory / "dispatch-request.json"
        _write(
            payload,
            {
                "ref": "main",
                "return_run_details": True,
                "inputs": {
                    "request_json": canonicalize(
                        request.to_document()
                    ).decode(),
                    "authorized_disposable": True,
                    "expected_tooling_sha": self.tooling_sha,
                },
            },
        )
        self._main(directory)
        raw = self._api(
            directory,
            "dispatch",
            _WORKFLOW_API + "/dispatches",
            "--input",
            str(payload),
            method="POST",
        )
        dispatch = _object(parse_json_strict(raw))
        run_id = _positive_id(dispatch.get("workflow_run_id"))
        run_route = f"{_REPO_API}/actions/runs/{run_id}"
        _require(
            dispatch.get("run_url") == "https://api.github.com" + run_route
            and dispatch.get("html_url")
            == f"https://github.com/{_REPOSITORY}/actions/runs/{run_id}",
            "dispatch missing or ambiguous exact run URLs",
        )
        self.command(
            directory,
            "watch",
            (
                "gh",
                "run",
                "watch",
                str(run_id),
                "--repo",
                _REPOSITORY,
                "--interval",
                "15",
                "--compact",
            ),
            category="ignored",
            timeout=_WATCH_TIMEOUT,
        )
        raw_run = self._api(directory, "run", run_route)
        raw = self._api(
            directory,
            "artifacts",
            run_route + "/artifacts?per_page=100",
            "--paginate",
            "--slurp",
        )
        pages = parse_json_strict(raw)
        if not isinstance(pages, list) or not pages:
            message = "missing artifact pages"
            raise ValueError(message)
        name = f"wdv3-native-npm-probe-{run_id}"
        matches = []
        for page in pages:
            artifacts = _object(page).get("artifacts")
            if not isinstance(artifacts, list):
                message = "malformed artifact page"
                raise ValueError(message)  # noqa: TRY004
            matches.extend(
                _object(item)
                for item in artifacts
                if _object(item).get("name") == name
            )
        _require(
            len(matches) == 1 and matches[0].get("expired") is False,
            "expected exactly one nonexpired exact-run artifact",
        )
        artifact_id = _positive_id(matches[0].get("id"))
        raw_artifact = self._api(
            directory,
            "artifact",
            f"{_REPO_API}/actions/artifacts/{artifact_id}",
        )
        artifact = _object(parse_json_strict(raw_artifact))
        _require(
            all(
                artifact.get(key) == matches[0].get(key)
                for key in ("id", "name", "expired", "digest", "url")
            ),
            "exact artifact metadata disagrees with run membership",
        )
        bundle = directory / "bundle"
        bundle.mkdir(mode=0o700)
        self.command(
            directory,
            "download",
            (
                "gh",
                "run",
                "download",
                str(run_id),
                "--repo",
                _REPOSITORY,
                "--name",
                name,
                "--dir",
                str(bundle),
            ),
            category="ignored",
        )
        return read_npm_evidence(
            bundle,
            raw_run_metadata=raw_run,
            raw_artifact_metadata=raw_artifact,
            expected_run_id=run_id,
            expected_tooling_sha=self.tooling_sha,
            expected_request=request,
            repository_root=self.root,
        )

    def _version_route(self, context: OriginalDeletionContext) -> str:
        package = quote(
            self.plan.creation.fixture.package.removeprefix("@hcoona/"), safe=""
        )
        return (
            f"/users/hcoona/packages/npm/{package}/versions/"
            f"{context.original_version.version_id}"
        )

    def delete_exact(
        self,
        original_control: PackageControl,
        original: VersionIdentity,
    ) -> OriginalDeletionContext:
        """Persist the original context before one exact USER version delete."""
        captured = self._last_capture
        _require(
            captured is not None
            and original_control
            == captured.state.control
            == self._original_control
            and original_control.full_scoped_name
            == self.plan.creation.fixture.package
            and original in captured.active_inventory
            and original.name == self.plan.deleted_original.fixture.version,
            "delete must bind captured original control and planned D identity",
        )
        directory = self._step("delete-d")
        context = OriginalDeletionContext(
            original_control, original, self.clock()
        )
        _write(directory / "original-context.json", context.to_document())
        self._api(
            directory,
            "delete",
            self._version_route(context),
            method="DELETE",
            category="empty",
        )
        self._deletion = context
        return context

    def restore_exact(self, context: OriginalDeletionContext) -> None:
        """Restore only the original captured ID, never compensate a failure."""
        _require(
            self._deletion is not None and context == self._deletion,
            "restore requires this operator's original deletion context",
        )
        directory = self._step("restore-d")
        _write(directory / "original-context.json", context.to_document())
        self._api(
            directory,
            "restore",
            self._version_route(context) + "/restore",
            method="POST",
            category="empty",
        )

    def execute(self) -> tuple[Path, str]:
        """Write completion only after all gates and restored readback."""
        result = run_npm_suite(self.plan, self)
        document = self._manifest(result)
        pending = self.audit / ".suite-evidence.pending"
        _write(pending, document)
        digest = _digest(pending)
        completed = self.audit / "suite-evidence.json"
        pending.rename(completed)
        return completed, digest

    def _manifest(self, result: NpmSuiteResult) -> dict[str, JsonValue]:
        profile = github_packages_destination_operation_profile()
        sources: list[JsonValue] = list(_CONTRACT_SOURCES)
        files: list[JsonValue] = []
        for path in sorted(self.audit.rglob("*")):
            _require(
                not path.is_symlink(), "audit cannot contain symbolic links"
            )
            if path.is_file():
                files.append(
                    {
                        "filename": path.relative_to(self.audit).as_posix(),
                        "sha256": _digest(path),
                    }
                )
        return {
            "schema": "workflow-delivery-v3/native-npm-suite-evidence/v1",
            "scenario_verdict": "passed",
            "native_acceptance_suite_version": NPM_SUITE_VERSION,
            "destination_operation_profile_id": profile.profile_id,
            "destination_operation_profile_digest": profile.profile_digest,
            "github_api_version": GITHUB_API_VERSION,
            "lower_layer_contract_revision": LOWER_LAYER_CONTRACT_REVISION,
            "lower_layer_contract_sources": sources,
            "disposable_package_preconditions": (
                self.plan.creation.disposable_package_preconditions.to_document()
            ),
            "generation": self.plan.creation.fixture.generation,
            "tooling_sha": self.tooling_sha,
            "captured_at": result.captures[-1].captured_at.isoformat(),
            "original_restoration_verified": True,
            "original_deletion": result.original_deletion.to_document(),
            "probes": [
                {
                    "run_id": item.run_id,
                    "artifact_id": item.artifact_id,
                    "artifact_url": item.artifact_url,
                    "artifact_digest": item.artifact_digest,
                    "classification": item.process.classification,
                }
                for item in result.probes
            ],
            "files": files,
            "admission": (
                "candidate only; requires independent native operator audit"
            ),
        }


def main(argv: Sequence[str] | None = None) -> int:
    """Run the explicitly authorized local suite, with no inferred inputs."""
    parser = argparse.ArgumentParser(description=__doc__)
    suite = parser.add_subparsers(dest="command", required=True).add_parser(
        "suite",
        description=(
            "Requires prior approval of an exact preexisting, "
            "operator-controlled "
            "disposable container with no production dependency. Both flags "
            "assert those facts and delete/restore authorization; flags do not "
            "grant approval. Local classic gh authentication stays local."
        ),
    )
    for name in (
        "package",
        "generation",
        "tooling-sha",
        "creation-target",
        "race-target",
        "deleted-target",
    ):
        suite.add_argument("--" + name, required=True)
    for name in ("audit-directory", "repository-root"):
        suite.add_argument("--" + name, type=Path, required=True)
    for name in ("authorized-disposable", "authorized-delete-restore"):
        suite.add_argument(
            "--" + name,
            action="store_true",
            default=False,
            help="Acknowledge prior explicit approval; grants nothing.",
        )
    arguments = parser.parse_args(argv)
    _authorize(
        disposable=arguments.authorized_disposable,
        delete_restore=arguments.authorized_delete_restore,
    )
    preconditions = DisposablePackagePreconditions(
        arguments.package,
        preexisting_container=True,
        operator_controlled=True,
        production_dependency=False,
    )
    requests = tuple(
        NpmProbeRequest(
            NpmFixtureSpec(
                arguments.package,
                f"0.0.0-native.{arguments.generation}.{suffix}",
                target,
                arguments.generation,
            ),
            preconditions,
        )
        for suffix, target in zip(
            "awvd",
            (
                arguments.creation_target,
                arguments.race_target,
                arguments.race_target,
                arguments.deleted_target,
            ),
            strict=True,
        )
    )
    operator = OperatorLocalNpmOperations(
        plan=NpmSuitePlan(*requests),
        expected_tooling_sha=arguments.tooling_sha,
        repository_root=arguments.repository_root,
        audit_directory=arguments.audit_directory,
        authorized_disposable=arguments.authorized_disposable,
        authorized_delete_restore=arguments.authorized_delete_restore,
    )
    path, digest = operator.execute()
    sys.stdout.write(
        canonicalize({"path": str(path), "sha256": digest}).decode() + "\n"
    )
    return 0
