"""Consumer ordering, input integrity and owned-process failure scenarios."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004, SLF001
import hashlib
import os
import shutil
import sys
import time
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import nuget_consumer as consumer
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)

TOKEN = "controlled-consumer-read-capability"  # noqa: S105
ORIGINAL = b"controlled original archive"
WITNESS = b"controlled original witness"
HOST = "WorkflowDeliveryV3NuGetConsumer"
PACKAGE_ID = "hcoona.releasesmoke.githubpackages"


def _sha(content):
    return hashlib.sha256(content).hexdigest()


def _read(path):
    return parse_json_strict(path.read_bytes())


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    runtime = tmp_path / "runtime"
    runtime.mkdir()
    files = {
        HOST + ".dll": b"controlled restore host",
        HOST + ".deps.json": b"{}",
        HOST + ".runtimeconfig.json": canonicalize(
            {
                "runtimeOptions": {
                    "rollForward": "Disable",
                    "framework": {
                        "name": "Microsoft.NETCore.App",
                        "version": "10.0.8",
                    },
                }
            }
        ),
        "NuGet.Commands.dll": b"controlled native restore dependency",
        "NuGet.Protocol.dll": b"controlled native protocol dependency",
    }
    for name, content in files.items():
        (runtime / name).write_bytes(content)
    checkout = tmp_path / "checkout"
    checkout.mkdir()
    request = consumer.NuGetConsumerRequest(
        "controlled-consumer",
        "a" * 40,
        "1.2.3",
        _sha(ORIGINAL),
        _sha(WITNESS),
        _sha(b"controlled service index"),
        "https://nuget.pkg.github.com/hcoona/download/",
        tuple((name, _sha(content)) for name, content in files.items()),
        _sha(Path(sys.executable).read_bytes()),
        consumer.NuGetConsumerLimits(6, 100_000, 30, 60, 120, 100_000),
    )
    monkeypatch.setattr(consumer.shutil, "which", lambda _name: sys.executable)
    return request, {
        "original_package": ORIGINAL,
        "witness": WITNESS,
        "restore_host": runtime / (HOST + ".dll"),
        "checkout": checkout,
        "audit_directory": tmp_path / "audit",
        "token": TOKEN,
    }


def _write_http_transcript(directory, request, *, redirect=True):
    """Model raw response evidence independently of the production decoder."""
    package_url = (
        request["packageBaseAddress"]
        + PACKAGE_ID
        + "/1.2.3/"
        + PACKAGE_ID
        + ".1.2.3.nupkg"
    )
    urls = [
        "https://nuget.pkg.github.com/hcoona/index.json",
        request["packageBaseAddress"] + PACKAGE_ID + "/index.json",
        package_url,
    ]
    bodies = [b"controlled service index", b'{"versions":["1.2.3"]}', ORIGINAL]
    if redirect:
        urls.append(None)
        bodies[2] = b"redirect"
        bodies.append(ORIGINAL)
    total = 0
    location_digest = _sha(
        b"https://storage.example.invalid/package?sig=controlled"
    )
    for index, (url, body) in enumerate(zip(urls, bodies, strict=True), 1):
        is_redirect = redirect and index == 3
        is_storage = redirect and index == 4
        reserved = {
            "schema": "workflow-delivery/v3/nuget-consumer-http-v2",
            "method": "GET",
            "url": url,
            "origin": "https://storage.example.invalid"
            if is_storage
            else "https://nuget.pkg.github.com",
            "redirectedFrom": 3 if is_storage else None,
            "locationSha256": location_digest if is_storage else None,
            "startedAt": "2026-09-15T00:00:00+00:00",
            "maximumRemainingResponseBytes": request["maximumResponseBytes"]
            - total,
        }
        response = {
            "schema": "workflow-delivery/v3/nuget-consumer-http-v2",
            "status": 302 if is_redirect else 200,
            "headers": {},
            "sha256": None if is_redirect else _sha(body),
            "bytes": len(body),
            "bodyRetention": "omitted" if is_redirect else "original",
            "redirectOrigin": "https://storage.example.invalid"
            if is_redirect
            else None,
            "locationSha256": location_digest if is_redirect else None,
            "completedAt": "2026-09-15T00:00:00+00:00",
        }
        total += len(body)
        prefix = f"{index:03d}"
        (directory / (prefix + "-reserved.json")).write_bytes(
            canonicalize(reserved)
        )
        (directory / (prefix + "-response.json")).write_bytes(
            canonicalize(response)
        )
        if not is_redirect:
            (directory / (prefix + "-body.bin")).write_bytes(body)
    return len(urls), total


def _controlled_command(argv, *, cwd, evidence, **_kwargs):
    label = evidence.directory.name
    if label == "sdk":
        return b"10.0.300\n"
    if label == "graph":
        (cwd / "graph.json").write_bytes(b'{"controlledGraph":true}')
        return b""
    if label == "restore":
        request = _read(Path(argv[-1]))
        directory = cwd / "restore-evidence"
        directory.mkdir()
        content = canonicalize(request)
        (directory / "request.json").write_bytes(content)
        (directory / "started.json").write_bytes(b"{}")
        requests, response_bytes = _write_http_transcript(directory, request)
        selected = cwd / "packages" / PACKAGE_ID / "1.2.3"
        selected.mkdir(parents=True)
        (selected / (PACKAGE_ID + ".1.2.3.nupkg")).write_bytes(ORIGINAL)
        (selected / "workflow-delivery").mkdir()
        (selected / "workflow-delivery/provenance.json").write_bytes(WITNESS)
        (cwd / "obj").mkdir(exist_ok=True)
        assets = b'{"controlledAssets":true}'
        (cwd / "obj/project.assets.json").write_bytes(assets)
        result = {
            "schema": "workflow-delivery/v3/nuget-consumer-restore-result-v2",
            "packageRedirectPolicy": "nuget-package-location-v1",
            "httpEvidenceSchema": "workflow-delivery/v3/nuget-consumer-http-v2",
            "packageResponseIndex": requests,
            "completed": True,
            "packageId": PACKAGE_ID,
            "version": "1.2.3",
            "packageSha256": _sha(ORIGINAL),
            "witnessSha256": _sha(WITNESS),
            "graphSha256": request["graphSha256"],
            "requestSha256": _sha(content),
            "assetsSha256": _sha(assets),
            "requests": requests,
            "responseBytes": response_bytes,
        }
        output = canonicalize(result)
        (directory / "result.json").write_bytes(output)
        return output
    if label == "build":
        return b"controlled build completed"
    assert label == "invoke"
    return b"hcoona-release-smoke-github-packages"


def _inline_supervise(target, arguments, _timeout):
    # Exercise the real worker handshake while replacing only process creation.
    connection = Mock()
    connection.recv.return_value = "start"
    try:
        target(connection, *arguments)
    except SystemExit as stopped:
        return stopped.code
    connection.send.assert_called_once_with("ready")
    connection.close.assert_called_once_with()
    return 0


@pytest.fixture
def controlled(monkeypatch):
    command = Mock(side_effect=_controlled_command)
    command._real_command = consumer.process._command
    monkeypatch.setattr(consumer.process, "_command", command)
    monkeypatch.setattr(consumer.os, "setsid", Mock())
    monkeypatch.setattr(consumer.process, "_supervise", _inline_supervise)
    return command


@pytest.mark.parametrize("requests", [6, 2**31 - 1])
def test_consumer_completes_only_after_restore_build_and_marker(
    inputs, controlled, monkeypatch, requests
):
    request, kwargs = inputs
    request = replace(
        request, limits=replace(request.limits, requests=requests)
    )
    monkeypatch.setenv("GITHUB_TOKEN", TOKEN)
    monkeypatch.setenv("NuGetPackageSourceCredentials_selected", TOKEN)
    output = consumer.run_nuget_consumer(request, **kwargs)
    document = _read(output)
    assert document["commands"] == [
        "sdk",
        "graph",
        "restore",
        "build",
        "invoke",
    ]
    assert document["requestDigest"] == canonical_sha256(request.to_document())
    assert document["packageSha256"] == _sha(ORIGINAL)
    assert document["witnessSha256"] == _sha(WITNESS)
    assert document["marker"] == "hcoona-release-smoke-github-packages"
    workspace = output.parent / "consumer"
    selected = workspace / "packages" / PACKAGE_ID / "1.2.3"
    assert (selected / (PACKAGE_ID + ".1.2.3.nupkg")).read_bytes() == ORIGINAL
    assert (
        selected / "workflow-delivery/provenance.json"
    ).read_bytes() == WITNESS
    calls = controlled.call_args_list
    assert len(calls) == 5
    for index, call in enumerate(calls):
        environment = call.kwargs["environment"]
        assert environment.get("WDV3_NUGET_CONSUMER_READ_TOKEN") == (
            TOKEN if index == 2 else None
        )
        assert "GITHUB_TOKEN" not in environment
        assert "NuGetPackageSourceCredentials_selected" not in environment
        assert environment["NUGET_PACKAGES"] == str(workspace / "packages")
        assert environment["DOTNET_CLI_HOME"] == str(workspace / "home")
        assert 0 < call.kwargs["timeout"] <= 60
        assert call.kwargs["output_limit"] == 100_000
        assert all(TOKEN not in argument for argument in call.args[0])
    assert "--no-restore" in calls[3].args[0]
    assert all(
        TOKEN.encode() not in path.read_bytes()
        for path in output.parent.rglob("*")
        if path.is_file()
    )
    with pytest.raises(FileExistsError):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == 5


@pytest.mark.parametrize("redirect", [False, True])
def test_consumer_validates_direct_and_redirected_native_http_evidence(
    inputs, controlled, redirect
):
    request, kwargs = inputs

    def invoke(argv, **options):
        output = _controlled_command(argv, **options)
        if options["evidence"].directory.name != "restore":
            return output
        directory = options["cwd"] / "restore-evidence"
        if not redirect:
            for path in directory.glob("0*"):
                path.unlink()
            count, total = _write_http_transcript(
                directory, _read(Path(argv[-1])), redirect=False
            )
            result = parse_json_strict(output)
            result.update(
                requests=count, responseBytes=total, packageResponseIndex=count
            )
            output = canonicalize(result)
            (directory / "result.json").write_bytes(output)
        return output

    controlled.side_effect = invoke
    output = consumer.run_nuget_consumer(request, **kwargs)
    assert (
        _read(output)["schema"]
        == "workflow-delivery/v3/nuget-consumer-result-v2"
    )
    outer_request = _read(output.parent / "request.json")
    assert (
        outer_request["schema"]
        == "workflow-delivery/v3/nuget-consumer-request-v2"
    )
    assert outer_request["packageRedirectPolicy"] == "nuget-package-location-v1"
    native_request = _read(output.parent / "restore-request.json")
    assert (
        native_request["schema"]
        == "workflow-delivery/v3/nuget-consumer-restore-request-v2"
    )
    assert (
        native_request["packageRedirectPolicy"] == "nuget-package-location-v1"
    )
    assert controlled.call_count == 5


@pytest.mark.parametrize(
    "change",
    [
        "old-result",
        "wrong-policy",
        "old-http",
        "missing-hop",
        "orphan-hop",
        "wrong-origin",
        "wrong-digest",
        "wrong-source",
        "second-hop",
        "metadata-redirect",
        "redirect-body",
        "extra-file",
        "missing-body",
        "substituted-package",
        "substituted-index",
        "bytes",
        "reservation",
        "total",
        "terminal-index",
        "unsafe-header",
        "bool-status",
    ],
)
def test_consumer_rejects_inconsistent_http_evidence_before_build(
    inputs, controlled, change
):
    request, kwargs = inputs

    def invoke(argv, **options):
        output = _controlled_command(argv, **options)
        if options["evidence"].directory.name != "restore":
            return output
        directory = options["cwd"] / "restore-evidence"
        result = parse_json_strict(output)
        result_changes = {
            "old-result": (
                "schema",
                "workflow-delivery/v3/nuget-consumer-restore-result",
            ),
            "wrong-policy": ("packageRedirectPolicy", "automatic"),
            "old-http": (
                "httpEvidenceSchema",
                "workflow-delivery/v3/nuget-consumer-http",
            ),
            "total": ("responseBytes", result["responseBytes"] + 1),
            "terminal-index": ("packageResponseIndex", 3),
        }
        if change in result_changes:
            key, value = result_changes[change]
            result[key] = value
        elif change in {"missing-hop", "missing-body"}:
            (
                directory
                / (
                    "004-reserved.json"
                    if change == "missing-hop"
                    else "004-body.bin"
                )
            ).unlink()
        elif change in {"redirect-body", "extra-file"}:
            (
                directory
                / (
                    "003-body.bin"
                    if change == "redirect-body"
                    else "005-response.json"
                )
            ).write_bytes(b"unmatched bytes")
        else:
            name, key, value = {
                "orphan-hop": ("003-response.json", "status", 200),
                "wrong-origin": (
                    "004-reserved.json",
                    "origin",
                    "https://other.example.invalid",
                ),
                "wrong-digest": (
                    "004-reserved.json",
                    "locationSha256",
                    "a" * 64,
                ),
                "wrong-source": ("004-reserved.json", "redirectedFrom", 2),
                "second-hop": ("004-response.json", "status", 302),
                "metadata-redirect": (
                    "003-reserved.json",
                    "url",
                    consumer._INDEX,
                ),
                "bytes": ("004-response.json", "bytes", len(ORIGINAL) + 1),
                "reservation": (
                    "004-reserved.json",
                    "maximumRemainingResponseBytes",
                    request.limits.response_bytes,
                ),
                "unsafe-header": (
                    "004-response.json",
                    "headers",
                    {"location": ["https://secret.invalid/p"]},
                ),
                "bool-status": ("004-response.json", "status", True),
                "substituted-package": (
                    "004-response.json",
                    "sha256",
                    _sha(b"substituted"),
                ),
                "substituted-index": (
                    "001-response.json",
                    "sha256",
                    _sha(b"substituted"),
                ),
            }[change]
            document = _read(directory / name)
            document[key] = value
            if change.startswith("substituted-"):
                body_name = name.replace("response.json", "body.bin")
                previous = (directory / body_name).stat().st_size
                (directory / body_name).write_bytes(b"substituted")
                document["bytes"] = len(b"substituted")
                result["responseBytes"] += len(b"substituted") - previous
            (directory / name).write_bytes(canonicalize(document))
        output = canonicalize(result)
        (directory / "result.json").write_bytes(output)
        return output

    controlled.side_effect = invoke
    with pytest.raises(ValueError, match="consumer process failed"):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == 3
    assert not (kwargs["audit_directory"] / "consumer.json").exists()
    assert (
        _read(kwargs["audit_directory"] / "consumer-failed.json")[
            "consumerSpent"
        ]
        is True
    )


@pytest.mark.parametrize("changed", ["archive", "witness", "runtime", "dotnet"])
def test_consumer_rejects_substituted_inputs_before_effects(
    inputs, monkeypatch, changed
):
    request, kwargs = inputs
    supervise = Mock()
    monkeypatch.setattr(consumer.process, "_supervise", supervise)
    if changed in {"archive", "witness"}:
        key = "original_package" if changed == "archive" else "witness"
        kwargs[key] = b"changed original"
    elif changed == "runtime":
        kwargs["restore_host"].write_bytes(b"changed host")
    else:
        request = replace(request, dotnet_executable_sha256="c" * 64)
    with pytest.raises(ValueError, match="mismatch"):
        consumer.run_nuget_consumer(request, **kwargs)
    supervise.assert_not_called()
    assert not kwargs["audit_directory"].exists()


@pytest.mark.parametrize(
    "changes",
    [
        {"version": "$(MSBuildProjectDirectory)"},
        {"version": "1.2.3;2.0.0"},
        {"package_base_address": "https://example.invalid/hcoona/"},
        {"restore_host_files": ((HOST + ".dll", "a" * 64),)},
    ],
)
def test_consumer_request_rejects_expansion_and_unselected_inputs(
    inputs, changes
):
    request, _kwargs = inputs
    with pytest.raises(ValueError, match=r"unsafe|unselected|incomplete"):
        replace(request, **changes)


@pytest.mark.parametrize(
    "changes",
    [
        {"requests": 0},
        {"requests": 2**31},
        {"response_bytes": True},
        {"completion_timeout_seconds": float("inf")},
    ],
)
def test_consumer_limits_require_finite_unambiguous_allowances(inputs, changes):
    request, _kwargs = inputs
    with pytest.raises(ValueError, match=r"invalid|unsupported"):
        replace(request.limits, **changes)


@pytest.mark.parametrize(
    "changed",
    [
        "archive",
        "witness",
        "assets",
        "request",
        "result",
        "accounting",
        "marker",
    ],
)
def test_consumer_rejects_changed_native_evidence_before_completion(
    inputs, controlled, changed
):
    request, kwargs = inputs

    def invoke(argv, **options):
        result = _controlled_command(argv, **options)
        label = options["evidence"].directory.name
        cwd = options["cwd"]
        if changed == "marker" and label == "invoke":
            return b"wrong-project-marker"
        if label != "restore":
            return result
        selected = cwd / "packages" / PACKAGE_ID / "1.2.3"
        paths = {
            "archive": selected / (PACKAGE_ID + ".1.2.3.nupkg"),
            "witness": selected / "workflow-delivery/provenance.json",
            "assets": cwd / "obj/project.assets.json",
            "request": cwd / "restore-evidence/request.json",
            "result": cwd / "restore-evidence/result.json",
        }
        if changed in paths:
            paths[changed].write_bytes(b"{}")
        elif changed == "accounting":
            document = parse_json_strict(result)
            assert isinstance(document, dict)
            document["requests"] = request.limits.requests + 1
            result = canonicalize(document)
            paths["result"].write_bytes(result)
        return result

    controlled.side_effect = invoke
    with pytest.raises(ValueError, match="consumer process failed"):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == (5 if changed == "marker" else 3)
    directory = kwargs["audit_directory"]
    assert _read(directory / "consumer-failed.json")["consumerSpent"] is True
    assert not (directory / "consumer.json").exists()


@pytest.mark.parametrize(
    "stage", ["sdk", "graph", "restore", "build", "invoke"]
)
def test_consumer_retains_partial_failure_without_retry(
    inputs, controlled, stage
):
    request, kwargs = inputs

    def invoke(argv, **options):
        evidence = options["evidence"]
        if evidence.directory.name == stage:
            evidence.write("safe-partial.bin", b"safe partial observation")
            raise ValueError(TOKEN)
        return _controlled_command(argv, **options)

    controlled.side_effect = invoke
    with pytest.raises(ValueError, match="consumer process failed"):
        consumer.run_nuget_consumer(request, **kwargs)
    count = ["sdk", "graph", "restore", "build", "invoke"].index(stage) + 1
    assert controlled.call_count == count
    directory = kwargs["audit_directory"]
    assert (directory / stage / "safe-partial.bin").read_bytes() == (
        b"safe partial observation"
    )
    assert _read(directory / "consumer-failed.json")["completed"] is False
    assert not (directory / "consumer.json").exists()
    assert all(
        TOKEN.encode() not in path.read_bytes()
        for path in directory.rglob("*")
        if path.is_file()
    )
    with pytest.raises(FileExistsError):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == count


@pytest.mark.parametrize("changed", ["runtime", "dotnet"])
def test_consumer_rechecks_runtime_before_credential_process(
    inputs, controlled, monkeypatch, tmp_path, changed
):
    request, kwargs = inputs
    dotnet = tmp_path / "controlled-dotnet"
    dotnet.write_bytes(b"controlled dotnet executable")
    monkeypatch.setattr(consumer.shutil, "which", lambda _name: str(dotnet))
    request = replace(
        request, dotnet_executable_sha256=_sha(dotnet.read_bytes())
    )

    def invoke(argv, **options):
        output = _controlled_command(argv, **options)
        if options["evidence"].directory.name == "graph":
            path = kwargs["restore_host"] if changed == "runtime" else dotnet
            path.write_bytes(b"changed during evaluation")
        return output

    controlled.side_effect = invoke
    with pytest.raises(ValueError, match="consumer process failed"):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == 2
    assert not (kwargs["audit_directory"] / "restore").exists()
    assert (
        _read(kwargs["audit_directory"] / "consumer-failed.json")[
            "consumerSpent"
        ]
        is True
    )
    assert all(
        "WDV3_NUGET_CONSUMER_READ_TOKEN" not in call.kwargs["environment"]
        for call in controlled.call_args_list
    )


@pytest.mark.parametrize("changed", ["package", "assets"])
def test_consumer_rechecks_inputs_after_product_execution(
    inputs, controlled, changed
):
    request, kwargs = inputs

    def invoke(argv, **options):
        output = _controlled_command(argv, **options)
        if options["evidence"].directory.name == "build":
            cwd = options["cwd"]
            path = (
                cwd
                / "packages"
                / PACKAGE_ID
                / "1.2.3"
                / (PACKAGE_ID + ".1.2.3.nupkg")
                if changed == "package"
                else cwd / "obj/project.assets.json"
            )
            path.write_bytes(b"changed during build")
        return output

    controlled.side_effect = invoke
    with pytest.raises(ValueError, match="consumer process failed"):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == 5
    assert not (kwargs["audit_directory"] / "consumer.json").exists()


@pytest.mark.parametrize(
    ("rejection", "message"),
    [
        ("deadline", "consumer completed late"),
        ("binding", "consumer completion request mismatch"),
    ],
)
def test_consumer_rejects_unadmitted_parent_completion(
    inputs, controlled, monkeypatch, rejection, message
):
    request, kwargs = inputs
    directory = kwargs["audit_directory"]
    elapsed = [0.0]
    retained: dict[Path, bytes] = {}
    monkeypatch.setattr(consumer.time, "monotonic", lambda: elapsed[0])

    def supervise(target, arguments, timeout):
        code = _inline_supervise(target, arguments, timeout)
        result = directory / "consumer.json"
        retained.update(
            {
                path: path.read_bytes()
                for path in directory.rglob("*")
                if path.is_file() and path != result
            }
        )
        if rejection == "deadline":
            elapsed[0] = timeout + 1
        else:
            document = _read(result)
            assert isinstance(document, dict)
            document["requestDigest"] = "sha256:" + "0" * 64
            result.write_bytes(canonicalize(document))
        return code

    monkeypatch.setattr(consumer.process, "_supervise", supervise)
    with pytest.raises(ValueError, match=message):
        consumer.run_nuget_consumer(request, **kwargs)
    assert controlled.call_count == 5
    failure = _read(directory / "consumer-failed.json")
    assert failure["consumerSpent"] is True
    assert failure["completed"] is False
    assert not (directory / "consumer.json").exists()
    assert (directory / "consumer/restore-evidence/result.json") in retained
    assert all(
        path.read_bytes() == content for path, content in retained.items()
    )


@pytest.mark.skipif(sys.platform != "linux", reason="Linux process inspection")
def test_consumer_deadline_stops_owned_process(inputs, tmp_path, monkeypatch):
    request, kwargs = inputs
    executable = tmp_path / "controlled-dotnet"
    executable.write_text(
        "#!" + sys.executable + "\n"
        "import os,time\nfrom pathlib import Path\n"
        "Path('sdk.pid').write_text(str(os.getpid()))\n"
        "time.sleep(60)\n",
        encoding="utf-8",
    )
    executable.chmod(0o700)
    monkeypatch.setattr(consumer.shutil, "which", lambda _name: str(executable))
    request = replace(
        request,
        dotnet_executable_sha256=_sha(executable.read_bytes()),
        limits=replace(request.limits, completion_timeout_seconds=3),
    )
    with pytest.raises(ValueError, match="deadline expired"):
        consumer.run_nuget_consumer(request, **kwargs)
    directory = kwargs["audit_directory"]
    pid = int((directory / "consumer/sdk.pid").read_text())
    status = Path(f"/proc/{pid}/stat")
    deadline = time.monotonic() + 3
    while True:
        try:
            process_state = status.read_text().split()[2]
        except FileNotFoundError:
            break
        if process_state == "Z":
            break
        assert time.monotonic() < deadline, "consumer descendant survived"
        time.sleep(0.01)
    assert (directory / "sdk/command.json").exists()
    assert not (directory / "graph").exists()
    assert _read(directory / "consumer-failed.json")["consumerSpent"] is True


@pytest.mark.skipif(
    shutil.which("dotnet") is None, reason="Native SDK required"
)
def test_consumer_generates_native_graph_without_credentials(
    inputs, controlled, monkeypatch
):
    request, kwargs = inputs
    # The native restore engine is covered by ConsumerRestoreTests. Here the
    # actual SDK evaluates this Python template, before the controlled restore.
    dotnet = Path(os.environ.get("DOTNET_ROOT", "")) / "dotnet"
    if not dotnet.is_file():
        # which() is replaced; locate the installed executable directly.
        dotnet = next(
            Path(part) / "dotnet"
            for part in os.get_exec_path()
            if (Path(part) / "dotnet").is_file()
        )
    monkeypatch.setattr(consumer.shutil, "which", lambda _name: str(dotnet))
    request = replace(
        request, dotnet_executable_sha256=_sha(dotnet.read_bytes())
    )
    real_command = controlled._real_command

    def invoke(argv, **options):
        if options["evidence"].directory.name in {"sdk", "graph"}:
            assert (
                "WDV3_NUGET_CONSUMER_READ_TOKEN" not in options["environment"]
            )
            if options["evidence"].directory.name == "graph":
                library_packs = options["cwd"] / "library-packs"
                library_packs.mkdir()
                argv = [
                    *argv,
                    f"-property:_WorkloadLibraryPacksFolder={library_packs}",
                ]
            return real_command(argv, **options)
        return _controlled_command(argv, **options)

    controlled.side_effect = invoke
    output = consumer.run_nuget_consumer(request, **kwargs)
    graph = _read(output.parent / "consumer/graph.json")
    project = next(iter(graph["projects"].values()))
    assert list(project["restore"]["sources"]) == [
        "https://nuget.pkg.github.com/hcoona/index.json"
    ]
    assert (
        project["restore"]["restoreAuditProperties"]["enableAudit"] == "false"
    )
    assert project["frameworks"]["net10.0"]["dependencies"] == {
        "Hcoona.ReleaseSmoke.GithubPackages": {
            "target": "Package",
            "version": "[1.2.3, 1.2.3]",
        }
    }
