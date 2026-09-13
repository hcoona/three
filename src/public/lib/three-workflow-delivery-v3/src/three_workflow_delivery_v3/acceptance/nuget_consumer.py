"""A bounded destination consumer over caller-admitted original inputs.

The caller owns actual source/artifact provenance, read authority and the closed
native generation. It does not acquire credentials, dispatch, publish or admit
Governance. Supplied hashes bind inputs; they do not authenticate them.
Only the restore child receives the supplied read credential. The coordinator
retains it for safe evidence checks and never evaluates product code itself.
"""

from __future__ import annotations

import hashlib
import math
import os
import re
import shutil
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING
from urllib.parse import urlsplit
from xml.sax.saxutils import quoteattr

from three_workflow_delivery_v3.acceptance import nuget_operator as process
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_RELEASE_UNIT,
    neutral_dotnet_environment,
)

if TYPE_CHECKING:
    from multiprocessing.connection import Connection

_HOST = "WorkflowDeliveryV3NuGetConsumer"
_TOKEN_ENV = "WDV3_NUGET_CONSUMER_READ_TOKEN"  # noqa: S105
_MAX_COMMANDS = 5
_MAX_RESTORE_SECONDS = 3600
_ID = "Hcoona.ReleaseSmoke.GithubPackages"
_INDEX = "https://nuget.pkg.github.com/hcoona/index.json"
_CONFIG = (
    '<configuration><packageSources><clear/><add key="selected" value="'
    + _INDEX
    + '"/></packageSources><packageSourceMapping><clear/>'
    '<packageSource key="selected"><package pattern="'
    + _ID
    + '"/></packageSource></packageSourceMapping></configuration>'
)


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _sha(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _object(value: JsonValue) -> dict[str, JsonValue]:
    _require(isinstance(value, dict), "consumer evidence requires an object")
    assert isinstance(value, dict)  # noqa: S101
    return value


@dataclass(frozen=True)
class NuGetConsumerLimits:
    """One consumer's fixed cumulative allowances, never a reusable grant."""

    requests: int
    response_bytes: int
    restore_timeout_seconds: int
    command_timeout_seconds: float
    completion_timeout_seconds: float
    process_output_bytes: int

    def __post_init__(self) -> None:
        """Reject nonfinite or ambiguous bounds before any process starts."""
        for value in (
            self.requests,
            self.response_bytes,
            self.process_output_bytes,
        ):
            _require(
                type(value) is int and value > 0, "invalid consumer allowance"
            )
        _require(
            self.requests <= 2**31 - 1
            and self.response_bytes < 2**31 - 1
            and type(self.restore_timeout_seconds) is int
            and 0 < self.restore_timeout_seconds <= _MAX_RESTORE_SECONDS,
            "unsupported native consumer bound",
        )
        for value in (
            self.command_timeout_seconds,
            self.completion_timeout_seconds,
        ):
            _require(
                type(value) in (int, float)
                and math.isfinite(value)
                and value > 0,
                "invalid consumer deadline",
            )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain separate command and response-body allowances."""
        return {
            "requests": self.requests,
            "responseBodyBytes": self.response_bytes,
            "restoreTimeoutSeconds": self.restore_timeout_seconds,
            "commandTimeoutSeconds": self.command_timeout_seconds,
            "completionTimeoutSeconds": self.completion_timeout_seconds,
            "maximumCommands": _MAX_COMMANDS,
            "outputBytesPerCommand": self.process_output_bytes,
            "maximumProcessOutputBytes": _MAX_COMMANDS
            * (self.process_output_bytes + 1),
            "terminationGraceSeconds": 5,
        }


@dataclass(frozen=True)
class NuGetConsumerRequest:
    """Caller-supplied originals and runtime bytes, without provenance proof."""

    generation: str
    tooling_sha: str
    version: str
    package_sha256: str
    witness_sha256: str
    service_index_sha256: str
    package_base_address: str
    restore_host_files: tuple[tuple[str, str], ...]
    dotnet_executable_sha256: str
    limits: NuGetConsumerLimits

    def __post_init__(self) -> None:
        """Close the inert request shape without certifying caller admission."""
        _require(
            type(self.generation) is str
            and re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", self.generation)
            is not None,
            "invalid consumer generation",
        )
        # This is an MSBuild expansion guard, not a NuGet version parser.
        # The native host owns semantic/native-coordinate validation.
        _require(
            type(self.version) is str
            and re.fullmatch(r"[0-9a-z][0-9a-z.+-]{0,255}", self.version)
            is not None,
            "unsafe consumer version input",
        )
        for value, size in (
            (self.tooling_sha, 40),
            (self.package_sha256, 64),
            (self.witness_sha256, 64),
            (self.service_index_sha256, 64),
            (self.dotnet_executable_sha256, 64),
        ):
            _require(
                type(value) is str
                and re.fullmatch(rf"[0-9a-f]{{{size}}}", value) is not None,
                "invalid consumer input digest",
            )
        _require(
            type(self.limits) is NuGetConsumerLimits, "missing consumer limits"
        )
        _require(
            type(self.package_base_address) is str, "missing consumer resource"
        )
        resource = urlsplit(self.package_base_address)
        _require(
            resource.scheme == "https"
            and resource.netloc == "nuget.pkg.github.com"
            and resource.path.startswith("/hcoona/")
            and resource.path.endswith("/")
            and not resource.query
            and not resource.fragment,
            "unselected consumer resource",
        )
        _require(
            type(self.restore_host_files) is tuple,
            "missing consumer host files",
        )
        files = {}
        for name, digest in self.restore_host_files:
            path = PurePosixPath(name)
            _require(
                bool(name)
                and not path.is_absolute()
                and path.as_posix() == name
                and ".." not in path.parts
                and "\\" not in name
                and ":" not in name
                and name not in files
                and re.fullmatch(r"[0-9a-f]{64}", digest) is not None,
                "invalid consumer host runtime binding",
            )
            files[name] = digest
        _require(
            {
                _HOST + ".dll",
                _HOST + ".deps.json",
                _HOST + ".runtimeconfig.json",
                "NuGet.Commands.dll",
                "NuGet.Protocol.dll",
            }.issubset(files),
            "incomplete consumer restore runtime",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Record exact caller bindings without treating them as authority."""
        return {
            "schema": "workflow-delivery/v3/nuget-consumer-request",
            "generation": self.generation,
            "callerToolingSha": self.tooling_sha,
            "packageId": _ID,
            "version": self.version,
            "packageSha256": self.package_sha256,
            "witnessSha256": self.witness_sha256,
            "serviceIndex": _INDEX,
            "serviceIndexSha256": self.service_index_sha256,
            "packageBaseAddress": self.package_base_address,
            "restoreHostFiles": dict[str, JsonValue](self.restore_host_files),
            "dotnetExecutableSha256": self.dotnet_executable_sha256,
            "limits": self.limits.to_document(),
        }


def _steps(  # noqa: PLR0913, PLR0917
    request: NuGetConsumerRequest,
    original: bytes,
    witness: bytes,
    host: Path,
    dotnet: Path,
    directory: Path,
    token: str,
) -> None:
    # Reuse the existing acceptance command/evidence machinery. Its caller
    # must run this function inside the supervised POSIX worker session.
    evidence = process._Evidence(directory, token)  # noqa: SLF001
    workspace = directory / "consumer"
    workspace.mkdir(mode=0o700)
    (workspace / "home").mkdir()
    for name in (
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
    ):
        (workspace / name).write_text("<Project/>", encoding="utf-8")
    (workspace / "global.json").write_text(
        '{"sdk":{"version":"10.0.300","rollForward":"disable"}}',
        encoding="utf-8",
    )
    (workspace / "nuget.config").write_text(_CONFIG, encoding="utf-8")
    (workspace / "consumer.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        "<OutputType>Exe</OutputType>"
        "<TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>"
        "<RestoreFallbackFolders></RestoreFallbackFolders>"
        "<RestoreAdditionalProjectFallbackFolders></RestoreAdditionalProjectFallbackFolders>"
        '</PropertyGroup><ItemGroup><PackageReference Include="'
        + _ID
        + '" Version='
        + quoteattr("[" + request.version + "]")
        + "/></ItemGroup></Project>",
        encoding="utf-8",
    )
    (workspace / "Program.cs").write_text(
        "System.Console.Write(HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId);",
        encoding="utf-8",
    )
    environment = neutral_dotnet_environment()
    environment.update(
        HOME=str(workspace / "home"),
        USERPROFILE=str(workspace / "home"),
        DOTNET_CLI_HOME=str(workspace / "home"),
        NUGET_PACKAGES=str(workspace / "packages"),
        NUGET_HTTP_CACHE_PATH=str(workspace / "http-cache"),
        NUGET_PLUGINS_CACHE_PATH=str(workspace / "plugin-cache"),
        DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1",
    )
    deadline = time.monotonic() + request.limits.completion_timeout_seconds
    commands: list[str] = []

    def run(
        label: str, arguments: tuple[str, ...], *, credential: bool = False
    ) -> bytes:
        commands.append(label)
        _require(
            len(commands) <= _MAX_COMMANDS,
            "consumer command allowance exhausted",
        )
        remaining = deadline - time.monotonic()
        _require(remaining > 0, "consumer completion deadline expired")
        path = directory / label
        path.mkdir()
        child_environment = dict(environment)
        if credential:
            child_environment[_TOKEN_ENV] = token
        return process._command(  # noqa: SLF001
            (str(dotnet), *arguments),
            cwd=workspace,
            environment=child_environment,
            timeout=min(remaining, request.limits.command_timeout_seconds),
            output_limit=request.limits.process_output_bytes,
            evidence=process._Evidence(path, token),  # noqa: SLF001
        )

    _require(
        run("sdk", ("--version",)).strip() == b"10.0.300",
        "consumer SDK mismatch",
    )
    graph = workspace / "graph.json"
    run(
        "graph",
        (
            "msbuild",
            str(workspace / "consumer.csproj"),
            "-target:GenerateRestoreGraphFile",
            "-property:RestoreGraphOutputPath=" + str(graph),
            "-property:RestoreConfigFile=" + str(workspace / "nuget.config"),
            "-nodeReuse:false",
            "-noAutoResponse",
            "-bl:" + str(directory / "graph" / "graph.binlog"),
        ),
    )
    native_request: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/nuget-consumer-restore-request",
        "workspace": str(workspace),
        "version": request.version,
        "packageSha256": request.package_sha256,
        "witnessSha256": request.witness_sha256,
        "graphSha256": _sha(graph.read_bytes()),
        "serviceIndexSha256": request.service_index_sha256,
        "packageBaseAddress": request.package_base_address,
        "maximumRequests": request.limits.requests,
        "maximumResponseBytes": request.limits.response_bytes,
        "timeoutSeconds": request.limits.restore_timeout_seconds,
    }
    evidence.write("restore-request.json", canonicalize(native_request))
    runtime = process._runtime_files(host.parent)  # noqa: SLF001
    _require(
        runtime == dict(request.restore_host_files)
        and _sha(dotnet.read_bytes()) == request.dotnet_executable_sha256,
        "consumer runtime or dotnet changed",
    )
    output = run(
        "restore",
        (str(host), "restore", str(directory / "restore-request.json")),
        credential=True,
    )
    result = _object(parse_json_strict(output))
    native_directory = workspace / "restore-evidence"
    native_bytes = (native_directory / "request.json").read_bytes()
    _require(
        _object(parse_json_strict(native_bytes)) == native_request
        and result
        == parse_json_strict((native_directory / "result.json").read_bytes())
        and result.get("schema")
        == "workflow-delivery/v3/nuget-consumer-restore-result"
        and result.get("completed") is True
        and result.get("requestSha256") == _sha(native_bytes)
        and result.get("graphSha256") == native_request["graphSha256"]
        and result.get("packageId") == _ID.lower()
        and result.get("version") == request.version,
        "consumer native result binding mismatch",
    )
    for field, limit in (
        ("requests", request.limits.requests),
        ("responseBytes", request.limits.response_bytes),
    ):
        value = result.get(field)
        _require(
            type(value) is int and 0 < value <= limit,
            "consumer native accounting mismatch",
        )
    selected = workspace / "packages" / _ID.lower() / request.version
    package = selected / (_ID.lower() + "." + request.version + ".nupkg")
    actual_witness = selected / "workflow-delivery/provenance.json"
    assets = workspace / "obj/project.assets.json"
    _require(
        package.read_bytes() == original
        and actual_witness.read_bytes() == witness
        and result.get("packageSha256") == request.package_sha256
        and result.get("witnessSha256") == request.witness_sha256
        and result.get("assetsSha256") == _sha(assets.read_bytes()),
        "consumer installed bytes, witness or assets changed",
    )
    run(
        "build",
        (
            "build",
            str(workspace / "consumer.csproj"),
            "--no-restore",
            "-property:UseSharedCompilation=false",
            "-nodeReuse:false",
            "-noAutoResponse",
            "-bl:" + str(directory / "build" / "build.binlog"),
        ),
    )
    marker = run("invoke", (str(workspace / "bin/Debug/net10.0/consumer.dll"),))
    _require(marker == DOTNET_RELEASE_UNIT.encode(), "consumer marker mismatch")
    _require(
        package.read_bytes() == original
        and actual_witness.read_bytes() == witness
        and _sha(assets.read_bytes()) == result["assetsSha256"],
        "consumer inputs changed during build or invocation",
    )
    _require(time.monotonic() < deadline, "consumer completed late")
    evidence.write(
        "consumer.json",
        canonicalize(
            {
                "schema": "workflow-delivery/v3/nuget-consumer-result",
                "requestDigest": canonical_sha256(request.to_document()),
                "restoreResultSha256": _sha(
                    (native_directory / "result.json").read_bytes()
                ),
                "packageSha256": request.package_sha256,
                "witnessSha256": request.witness_sha256,
                "assetsSha256": _sha(assets.read_bytes()),
                "marker": marker.decode(),
                "commands": list[JsonValue](commands),
                "completedAt": datetime.now(UTC).isoformat(),
                "evidenceLevel": (
                    "supplied-input consumer; independent native provenance "
                    "and audit required"
                ),
            }
        ),
    )


def _worker(  # noqa: PLR0913, PLR0917
    connection: Connection,
    request: NuGetConsumerRequest,
    original: bytes,
    witness: bytes,
    host: Path,
    dotnet: Path,
    directory: Path,
    token: str,
) -> None:
    os.setsid()
    connection.send("ready")
    if connection.recv() != "start":
        return
    connection.close()
    try:
        _steps(request, original, witness, host, dotnet, directory, token)
    except BaseException:  # noqa: BLE001
        raise SystemExit(1) from None


def run_nuget_consumer(  # noqa: PLR0913
    request: NuGetConsumerRequest,
    *,
    original_package: bytes,
    witness: bytes,
    restore_host: Path,
    checkout: Path,
    audit_directory: Path,
    token: str,
) -> Path:
    """Consume once with no retry, using already admitted originals/tooling.

    Source, artifact, access and generation admission belong to the caller.
    Host hashes are checked here, but neither they nor a successful return
    establish authenticated provenance, Windows support or native acceptance.
    """
    _require(
        os.name == "posix",
        "consumer requires POSIX; use configured WSL on Windows",
    )
    _require(type(request) is NuGetConsumerRequest, "missing consumer request")
    _require(
        type(token) is str
        and bool(token)
        and "\n" not in token
        and "\r" not in token,
        "invalid consumer read credential",
    )
    _require(
        _sha(original_package) == request.package_sha256
        and _sha(witness) == request.witness_sha256,
        "consumer original input bytes mismatch",
    )
    host = restore_host.resolve(strict=True)
    runtime = process._runtime_files(host.parent)  # noqa: SLF001
    _require(
        host.name == _HOST + ".dll"
        and runtime == dict(request.restore_host_files),
        "consumer prebuilt runtime mismatch",
    )
    configuration = _object(
        parse_json_strict(
            (host.parent / (_HOST + ".runtimeconfig.json")).read_bytes()
        )
    )
    options = _object(configuration["runtimeOptions"])
    _require(
        options.get("rollForward") == "Disable"
        and options.get("framework")
        == {"name": "Microsoft.NETCore.App", "version": "10.0.8"},
        "consumer runtime configuration mismatch",
    )
    executable = shutil.which("dotnet")
    _require(executable is not None, "missing consumer dotnet host")
    assert executable is not None  # noqa: S101
    dotnet = Path(executable).resolve(strict=True)
    _require(
        _sha(dotnet.read_bytes()) == request.dotnet_executable_sha256,
        "consumer dotnet host mismatch",
    )
    root = checkout.resolve(strict=True)
    directory = audit_directory.absolute()
    _require(
        directory == directory.resolve()
        and not directory.is_relative_to(root)
        and not root.is_relative_to(directory),
        "consumer evidence must be outside checkout",
    )
    directory.mkdir(mode=0o700, parents=False, exist_ok=False)
    evidence = process._Evidence(directory, token)  # noqa: SLF001
    evidence.write("request.json", canonicalize(request.to_document()))
    deadline = time.monotonic() + request.limits.completion_timeout_seconds
    try:
        code = process._supervise(  # noqa: SLF001
            _worker,
            (
                request,
                original_package,
                witness,
                host,
                dotnet,
                directory,
                token,
            ),
            request.limits.completion_timeout_seconds,
        )
        _require(code == 0, "consumer process failed")
        _require(time.monotonic() < deadline, "consumer completed late")
        result = directory / "consumer.json"
        document = _object(parse_canonical_json(result.read_bytes()))
        _require(
            document.get("requestDigest")
            == canonical_sha256(request.to_document()),
            "consumer completion request mismatch",
        )
    except BaseException as error:
        evidence.write(
            "consumer-failed.json",
            canonicalize(
                {
                    "consumerSpent": True,
                    "completed": False,
                    "errorType": type(error).__name__,
                    "stoppedAt": datetime.now(UTC).isoformat(),
                }
            ),
        )
        raise
    return result
