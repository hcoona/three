"""Local POSIX NuGet reads with a fixed, nonresumable capture budget.

Requests are reviewed inputs, never execution grants. Original helper provenance
requires independent admission before use; local hashes cannot prove a producer.
The operator builds nothing, changes no credentials, and never dispatches or
publishes. Its local runtime is distinct from the Windows publication profile.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import multiprocessing
import os
import re
import selectors
import shutil
import signal
import subprocess
import time
from contextlib import suppress
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING, cast
from urllib.parse import unquote

from three_workflow_delivery_v3.acceptance import nuget_capture
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    materialize_helper,
)
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    neutral_dotnet_environment,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence
    from multiprocessing.connection import Connection

_SCHEMA = "workflow-delivery/v3/nuget-read-operator-request"
_SOURCE = "src/public/lib/three-workflow-delivery-v3/src"
_POSITIONS = (
    "before-create",
    "after-create",
    "before-identical",
    "after-identical",
    "before-equivalent",
    "after-equivalent",
)
_HELPER_ROOT = "src/private/app/workflow-delivery-v3-dotnet-provider/"
_CONTAINER_ID = 12024661


def _require(value: bool, message: str) -> None:  # noqa: FBT001
    if not value:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    _require(isinstance(value, dict), "expected an operator input object")
    assert isinstance(value, dict)  # noqa: S101
    return value


def _sha(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _text(value: JsonValue) -> str:
    _require(type(value) is str, "operator input requires text")
    return cast("str", value)


def _integer(value: JsonValue) -> int:
    _require(type(value) is int, "operator input requires an exact integer")
    return cast("int", value)


def _number(value: JsonValue) -> float:
    _require(type(value) in (int, float), "operator input requires a number")
    return cast("float", value)


def _digest(value: object, length: int = 64) -> None:
    _require(
        type(value) is str
        and re.fullmatch(rf"[0-9a-f]{{{length}}}", value) is not None,
        "invalid operator identity digest",
    )


@dataclass(frozen=True)
class NuGetReadRequest:
    """A separately reviewed preflight or all six capture positions."""

    captures: tuple[nuget_capture.NuGetCaptureRequest, ...]
    generation_timeout_seconds: float
    helper_reference: ArtifactReference
    helper_tooling_sha: str
    helper_run_id: int
    helper_audit_sha256: str
    helper_source_inputs: tuple[tuple[str, str], ...]
    helper_calls_per_capture: int
    helper_output_bytes_per_call: int
    helper_timeout_seconds: float
    dotnet_executable_sha256: str
    dotnet_info_sha256: str
    reader_runtime: dict[str, JsonValue]

    def __post_init__(self) -> None:
        """Close fixed scope and finite bounds before starting a process."""
        _require(
            type(self.captures) is tuple
            and bool(self.captures)
            and all(
                type(item) is nuget_capture.NuGetCaptureRequest
                for item in self.captures
            ),
            "missing operator capture requests",
        )
        labels = tuple(item.label for item in self.captures)
        _require(
            labels in (("preflight",), _POSITIONS),
            "operator requires preflight or the ordered six capture positions",
        )
        first = self.captures[0]
        for item in self.captures:
            _require(
                (
                    item.generation,
                    item.tooling_sha,
                    item.helper_runtime_sha256,
                    item.container_id,
                    item.version,
                )
                == (
                    first.generation,
                    first.tooling_sha,
                    first.helper_runtime_sha256,
                    first.container_id,
                    first.version,
                ),
                "capture positions have inconsistent subjects",
            )
        _require(
            first.container_id == _CONTAINER_ID, "unselected NuGet container"
        )
        _require(
            self.helper_reference.payload_digest
            == "sha256:" + first.helper_runtime_sha256,
            "capture and helper artifact disagree",
        )
        _digest(self.helper_tooling_sha, 40)
        for value in (
            self.helper_audit_sha256,
            self.dotnet_executable_sha256,
            self.dotnet_info_sha256,
        ):
            _digest(value)
        for value in (
            self.helper_run_id,
            self.helper_calls_per_capture,
            self.helper_output_bytes_per_call,
        ):
            _require(type(value) is int and value > 0, "invalid helper bound")
        # Reuse the capture contract's finite-positive timeout validation.
        nuget_capture.NuGetCaptureLimits(
            1,
            1,
            1,
            self.helper_timeout_seconds,
            self.generation_timeout_seconds,
        )
        paths = set()
        for name, digest in self.helper_source_inputs:
            relative = PurePosixPath(name)
            _require(
                bool(name)
                and not relative.is_absolute()
                and relative.as_posix() == name
                and ".." not in relative.parts
                and "\\" not in name
                and name not in paths,
                "unsafe or repeated helper source input",
            )
            _digest(digest)
            paths.add(name)
        _require(
            {
                _HELPER_ROOT + "Program.cs",
                _HELPER_ROOT + "WorkflowDeliveryV3DotnetProvider.csproj",
                _HELPER_ROOT + "packages.lock.json",
            }.issubset(paths),
            "missing helper source inputs; independent audit required",
        )
        _require(type(self.reader_runtime) is dict, "missing reader runtime")

    def to_document(self) -> dict[str, JsonValue]:
        """Expose maximum cumulative allowances, without refundable attempts."""
        return {
            "schema": _SCHEMA,
            "captures": [item.to_document() for item in self.captures],
            "generationTimeoutSeconds": self.generation_timeout_seconds,
            "totalAllowances": {
                "requests": sum(item.limits.requests for item in self.captures),
                "versionPages": sum(
                    item.limits.version_pages for item in self.captures
                ),
                "responseBytes": sum(
                    item.limits.response_bytes for item in self.captures
                ),
                "helperCalls": len(self.captures)
                * self.helper_calls_per_capture,
                "helperOutputBytes": len(self.captures)
                * self.helper_calls_per_capture
                * (self.helper_output_bytes_per_call + 1),
            },
            "helper": {
                "reference": self.helper_reference.to_document(),
                "producerToolingSha": self.helper_tooling_sha,
                "producerRunId": self.helper_run_id,
                "independentAuditSha256": self.helper_audit_sha256,
                "sourceInputs": dict(self.helper_source_inputs),
                "callsPerCapture": self.helper_calls_per_capture,
                "outputBytesPerCall": self.helper_output_bytes_per_call,
                "timeoutSeconds": self.helper_timeout_seconds,
                "dotnetExecutableSha256": self.dotnet_executable_sha256,
                "dotnetInfoSha256": self.dotnet_info_sha256,
            },
            "readerRuntime": self.reader_runtime,
        }


def read_request(content: bytes) -> NuGetReadRequest:
    """Parse exact canonical request bytes, rejecting unknown fields."""
    document = _object(parse_canonical_json(content))
    helper = _object(document["helper"])
    raw_captures = document["captures"]
    _require(isinstance(raw_captures, list), "missing capture list")
    assert isinstance(raw_captures, list)  # noqa: S101
    captures = []
    for value in raw_captures:
        item = _object(value)
        bounds = _object(item["limits"])
        captures.append(
            nuget_capture.NuGetCaptureRequest(
                _text(item["generation"]),
                _text(item["label"]),
                _text(item["callerToolingSha"]),
                _text(item["callerHelperRuntimeSha256"]),
                _integer(item["containerId"]),
                _text(item["version"]),
                nuget_capture.NuGetCaptureLimits(
                    _integer(bounds["requests"]),
                    _integer(bounds["versionPages"]),
                    _integer(bounds["responseBytes"]),
                    _number(bounds["socketTimeoutSeconds"]),
                    _number(bounds["completionTimeoutSeconds"]),
                ),
            )
        )
    request = NuGetReadRequest(
        tuple(captures),
        _number(document["generationTimeoutSeconds"]),
        artifact_reference_from_document(helper["reference"]),
        _text(helper["producerToolingSha"]),
        _integer(helper["producerRunId"]),
        _text(helper["independentAuditSha256"]),
        tuple(
            (name, _text(value))
            for name, value in sorted(_object(helper["sourceInputs"]).items())
        ),
        _integer(helper["callsPerCapture"]),
        _integer(helper["outputBytesPerCall"]),
        _number(helper["timeoutSeconds"]),
        _text(helper["dotnetExecutableSha256"]),
        _text(helper["dotnetInfoSha256"]),
        _object(document["readerRuntime"]),
    )
    _require(
        canonicalize(request.to_document()) == content,
        "operator request closure mismatch",
    )
    return request


class _Evidence:
    def __init__(self, directory: Path, token: str) -> None:
        self.directory = directory
        self.forbidden = (
            token.encode(),
            base64.b64encode(f"hcoona:{token}".encode()),
        )

    def check(self, content: bytes) -> None:
        _require(
            all(secret and secret not in content for secret in self.forbidden),
            "read credential reflected in operator evidence",
        )
        decoded = unquote(content.decode("utf-8", errors="replace")).encode()
        _require(
            all(secret not in decoded for secret in self.forbidden),
            "encoded read credential reflected in operator evidence",
        )

    def write(self, name: str, content: bytes) -> None:
        self.check(content)
        with (self.directory / name).open("xb") as stream:
            stream.write(content)


def _command(  # noqa: PLR0913
    argv: tuple[str, ...],
    *,
    cwd: Path,
    environment: Mapping[str, str],
    timeout: float,
    output_limit: int,
    evidence: _Evidence,
) -> bytes:
    """Retain bounded, separate helper streams inside the supervised group."""
    evidence.write("command.json", canonicalize({"argv": list(argv)}))
    output = {"stdout": bytearray(), "stderr": bytearray()}
    total = 0
    output_complete = False
    deadline = time.monotonic() + timeout
    with subprocess.Popen(  # noqa: S603
        argv,
        cwd=cwd,
        env=dict(environment),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    ) as process:
        try:
            assert process.stdout is not None  # noqa: S101
            assert process.stderr is not None  # noqa: S101
            with selectors.DefaultSelector() as selector:
                selector.register(
                    process.stdout, selectors.EVENT_READ, "stdout"
                )
                selector.register(
                    process.stderr, selectors.EVENT_READ, "stderr"
                )
                while selector.get_map() or process.poll() is None:
                    remaining = deadline - time.monotonic()
                    _require(remaining > 0, "helper process deadline expired")
                    for key, _event in selector.select(min(remaining, 0.1)):
                        chunk = os.read(
                            key.fd, min(65536, output_limit - total + 1)
                        )
                        if not chunk:
                            selector.unregister(key.fileobj)
                            continue
                        _require(
                            total + len(chunk) <= output_limit,
                            "helper output bound exceeded",
                        )
                        output[key.data].extend(chunk)
                        total += len(chunk)
            code = process.wait(timeout=max(0.001, deadline - time.monotonic()))
            output_complete = True
        except BaseException:
            process.kill()
            process.wait(timeout=5)
            raise
        finally:
            # Validate both streams before retaining either one.
            for content in output.values():
                evidence.check(bytes(content))
            for name, content in output.items():
                evidence.write(name + ".bin", bytes(content))
            evidence.write(
                "process.json",
                canonicalize(
                    {
                        "returnCode": process.poll(),
                        "outputComplete": output_complete,
                    }
                ),
            )
    _require(code == 0, "helper command failed")
    return bytes(output["stdout"])


class _Helper:
    def __init__(
        self,
        request: NuGetReadRequest,
        dll: Path,
        dotnet: Path,
        evidence: _Evidence,
    ) -> None:
        self.request = request
        self.dll = dll
        self.dotnet = dotnet
        self.evidence = evidence
        self.calls = 0

    def command(self, arguments: tuple[str, ...]) -> bytes:
        _require(
            self.calls < self.request.helper_calls_per_capture,
            "helper call allowance exhausted",
        )
        self.calls += 1
        directory = self.evidence.directory / f"helper-{self.calls:04d}"
        directory.mkdir(mode=0o700)
        return _command(
            (str(self.dotnet), *arguments),
            cwd=self.dll.parent,
            environment=neutral_dotnet_environment(),
            timeout=self.request.helper_timeout_seconds,
            output_limit=self.request.helper_output_bytes_per_call,
            evidence=_Evidence(directory, self.evidence.forbidden[0].decode()),
        )

    def normalize_identity(
        self, package_id: str, version: str
    ) -> dict[str, JsonValue]:
        return _object(
            parse_json_strict(
                self.command(
                    (str(self.dll), "normalize-identity", package_id, version)
                )
            )
        )

    def _file(self, operation: str, content: bytes) -> dict[str, JsonValue]:
        name = f"helper-input-{self.calls + 1:04d}.bin"
        self.evidence.write(name, content)
        return _object(
            parse_json_strict(
                self.command(
                    (
                        str(self.dll),
                        operation,
                        str(self.evidence.directory / name),
                    )
                )
            )
        )

    def service_resources(self, index: bytes) -> dict[str, JsonValue]:
        return self._file("service-resources", index)

    def inspect_package(self, content: bytes) -> dict[str, JsonValue]:
        return self._file("inspect-package", content)


def _worker(  # noqa: PLR0913, PLR0917
    connection: Connection,
    request: NuGetReadRequest,
    index: int,
    directory: Path,
    dll: Path,
    dotnet: Path,
    token: str,
) -> None:
    # The handshake precedes all helper/network work. The parent owns this
    # session, including helper descendants; helpers create no separate group.
    os.setsid()
    connection.send("ready")
    if connection.recv() != "start":
        return
    connection.close()
    evidence = _Evidence(directory, token)
    try:
        _require(
            nuget_capture._reader_runtime() == request.reader_runtime,  # noqa: SLF001
            "local reader runtime changed",
        )
        _require(
            _sha(dotnet.read_bytes()) == request.dotnet_executable_sha256,
            "dotnet executable changed",
        )
        helper = _Helper(request, dll, dotnet, evidence)
        info = helper.command(("--info",))
        _require(
            _sha(info) == request.dotnet_info_sha256,
            "local dotnet runtime information mismatch",
        )
        nuget_capture.capture_nuget_state(
            request.captures[index],
            authority=helper,
            transport=native.NuGetHttpTransport(),
            token=token,
            audit_directory=directory / "capture",
        )
    except BaseException as error:  # noqa: BLE001
        evidence.write(
            "worker-failure.json",
            canonicalize({"errorType": type(error).__name__}),
        )
        raise SystemExit(1) from None


def _supervise(target: Callable, arguments: tuple, timeout: float) -> int:
    """Start a private process session and terminate all owned descendants."""
    _require(
        os.name == "posix",
        "NuGet operator requires POSIX; use configured WSL on Windows",
    )
    context = multiprocessing.get_context("spawn")
    parent, child = context.Pipe()
    process = context.Process(target=target, args=(child, *arguments))
    deadline = time.monotonic() + timeout
    ready = False
    process.start()
    child.close()
    try:
        _require(
            parent.poll(max(0, deadline - time.monotonic())),
            "capture process did not become ready",
        )
        _require(parent.recv() == "ready", "invalid capture process handshake")
        ready = True
        parent.send("start")
        process.join(max(0, deadline - time.monotonic()))
        _require(not process.is_alive(), "capture process deadline expired")
        return process.exitcode if process.exitcode is not None else 1
    finally:
        parent.close()
        if ready:
            assert process.pid is not None  # noqa: S101
            with suppress(ProcessLookupError):
                os.killpg(process.pid, signal.SIGKILL)
        if process.is_alive():
            process.kill()
        process.join(5)
        _require(not process.is_alive(), "capture process termination failed")
        process.close()


def _git(root: Path, *arguments: str) -> bytes:
    return subprocess.check_output(  # noqa: S603
        ("git", "--no-pager", "-C", str(root), *arguments),  # noqa: S607
        stderr=subprocess.DEVNULL,
        env=neutral_dotnet_environment(),
        timeout=30,
    )


def _admit_source(root: Path, request: NuGetReadRequest) -> None:
    tooling = request.captures[0].tooling_sha
    _require(
        _git(root, "rev-parse", "HEAD").decode().strip() == tooling,
        "operator checkout revision mismatch",
    )
    _require(
        not _git(root, "status", "--porcelain", "--untracked-files=all"),
        "operator requires a clean checkout",
    )
    package_root = root / _SOURCE / "three_workflow_delivery_v3"
    _require(
        Path(__file__).resolve().is_relative_to(package_root.resolve()),
        "operator imported outside the pinned checkout",
    )
    # Verify actual imported-source files, rather than trusting a recorded SHA.
    for path in package_root.rglob("*.py"):
        _require(not path.is_symlink(), "symlinked operator source")
        relative = path.relative_to(root).as_posix()
        _require(
            path.read_bytes() == _git(root, "show", f"{tooling}:{relative}"),
            "operator source bytes mismatch",
        )
    for name, digest in request.helper_source_inputs:
        original = _git(root, "show", f"{request.helper_tooling_sha}:{name}")
        current = _git(root, "show", f"{tooling}:{name}")
        _require(
            _sha(original) == digest and current == original,
            "helper dependency source compatibility mismatch",
        )


def _runtime_files(directory: Path) -> dict[str, JsonValue]:
    files: dict[str, JsonValue] = {}
    for path in sorted(directory.rglob("*")):
        _require(not path.is_symlink(), "symlinked helper runtime")
        if path.is_file():
            files[path.relative_to(directory).as_posix()] = _sha(
                path.read_bytes()
            )
    return files


class NuGetReadOperator:
    """One live collector lifetime; a failure spends the generation."""

    def __init__(  # noqa: PLR0913
        self,
        request: NuGetReadRequest,
        *,
        checkout: Path,
        helper_archive: Path,
        helper_audit: Path,
        audit_directory: Path,
        token: str,
    ) -> None:
        """Admit immutable inputs before any helper or destination execution."""
        _require(
            os.name == "posix",
            "NuGet operator requires POSIX; use configured WSL on Windows",
        )
        _require(
            bool(token) and "\n" not in token and "\r" not in token,
            "invalid read credential",
        )
        self.request = read_request(canonicalize(request.to_document()))
        self.token = token
        self.next_index = 0
        self.failed = False
        self.directory = audit_directory.absolute()
        root = checkout.resolve(strict=True)
        self.root = root
        _require(
            self.directory == self.directory.resolve()
            and not self.directory.is_relative_to(root)
            and not root.is_relative_to(self.directory),
            "operator evidence must be outside the checkout",
        )
        _admit_source(root, request)
        audit = helper_audit.read_bytes()
        _require(
            _sha(audit) == request.helper_audit_sha256,
            "helper independent audit bytes mismatch",
        )
        dotnet = shutil.which("dotnet")
        _require(dotnet is not None, "missing local dotnet host")
        assert dotnet is not None  # noqa: S101
        self.dotnet = Path(dotnet).resolve(strict=True)
        _require(
            _sha(self.dotnet.read_bytes()) == request.dotnet_executable_sha256,
            "local dotnet executable mismatch",
        )
        self.directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.evidence = _Evidence(self.directory, token)
        self.evidence.write("request.json", canonicalize(request.to_document()))
        self.evidence.write("helper-independent-audit.md", audit)
        self.dll = materialize_helper(
            helper_archive, request.helper_reference, self.directory / "helper"
        ).helper_dll
        self.helper_files = _runtime_files(self.dll.parent)
        self.evidence.write(
            "helper-files.json", canonicalize(self.helper_files)
        )
        self.started = time.monotonic()
        self.deadline = self.started + request.generation_timeout_seconds
        self.evidence.write(
            "generation-started.json",
            canonicalize(
                {
                    "startedAt": datetime.now(UTC).isoformat(),
                    "requestDigest": canonical_sha256(
                        self.request.to_document()
                    ),
                    "terminationGraceSeconds": 5,
                }
            ),
        )

    def capture(self, label: str) -> Path:
        """Consume the next position once, preserving safe partial evidence."""
        _require(not self.failed, "operator generation already failed")
        _require(
            self.next_index < len(self.request.captures),
            "operator capture allowance exhausted",
        )
        item = self.request.captures[self.next_index]
        _require(label == item.label, "unexpected capture position")
        index = self.next_index
        self.next_index += 1
        directory = self.directory / label
        started = time.monotonic()
        try:
            directory.mkdir(mode=0o700)
            self.evidence.write(
                label + "-reserved.json",
                canonicalize(
                    {
                        "index": index,
                        "captureRequestDigest": canonical_sha256(
                            item.to_document()
                        ),
                        "maximumAllowances": item.limits.to_document(),
                        "startedAt": datetime.now(UTC).isoformat(),
                    }
                ),
            )
            _admit_source(self.root, self.request)
            _require(
                _runtime_files(self.dll.parent) == self.helper_files,
                "materialized helper runtime changed",
            )
            remaining = self.deadline - time.monotonic()
            _require(remaining > 0, "operator generation deadline expired")
            code = _supervise(
                _worker,
                (
                    self.request,
                    index,
                    directory,
                    self.dll,
                    self.dotnet,
                    self.token,
                ),
                min(remaining, item.limits.completion_timeout_seconds),
            )
            _require(code == 0, "capture process failed")
            output = directory / "capture" / "capture.json"
            document = _object(parse_canonical_json(output.read_bytes()))
            _require(
                document.get("requestDigest")
                == canonical_sha256(item.to_document()),
                "capture result request mismatch",
            )
            if label == "preflight":
                _require(
                    "scenarioPackage" in document
                    and document["scenarioPackage"] is None,
                    "preflight coordinate is not absent",
                )
            _require(
                time.monotonic() < self.deadline,
                "operator generation completed late",
            )
            self.evidence.write(
                label + "-completed.json",
                canonicalize(
                    {
                        "captureSha256": _sha(output.read_bytes()),
                        "workerReturnCode": code,
                        "completedAt": datetime.now(UTC).isoformat(),
                        "captureElapsedSeconds": time.monotonic() - started,
                        "generationElapsedSeconds": time.monotonic()
                        - self.started,
                    }
                ),
            )
        except BaseException as error:
            self.failed = True
            self.evidence.write(
                label + "-failed.json",
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "generationSpent": True,
                        "stoppedAt": datetime.now(UTC).isoformat(),
                        "captureElapsedSeconds": time.monotonic() - started,
                        "generationElapsedSeconds": time.monotonic()
                        - self.started,
                    }
                ),
            )
            raise
        return output


def main(argv: Sequence[str] | None = None) -> int:
    """Run only a separately reviewed one-capture read-only preflight."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--request", type=Path, required=True)
    parser.add_argument("--checkout", type=Path, required=True)
    parser.add_argument("--helper-archive", type=Path, required=True)
    parser.add_argument("--helper-audit", type=Path, required=True)
    parser.add_argument("--audit-directory", type=Path, required=True)
    parser.add_argument("--token-env", required=True)
    arguments = parser.parse_args(argv)
    try:
        request = read_request(arguments.request.read_bytes())
        _require(len(request.captures) == 1, "the CLI executes preflight only")
        token = os.environ.get(arguments.token_env, "")
        operator = NuGetReadOperator(
            request,
            checkout=arguments.checkout,
            helper_archive=arguments.helper_archive,
            helper_audit=arguments.helper_audit,
            audit_directory=arguments.audit_directory,
            token=token,
        )
        operator.capture("preflight")
    except Exception:  # noqa: BLE001
        # No exception message, request headers or secret value reaches stdout.
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
