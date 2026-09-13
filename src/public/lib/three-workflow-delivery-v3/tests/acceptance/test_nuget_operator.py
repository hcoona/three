"""Operator scenarios with controlled destination and helper dependencies."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004, SLF001
import base64
import hashlib
import importlib
import json
import os
import sys
import time
import zipfile
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock
from urllib.parse import quote

import pytest
from three_workflow_delivery_v3.acceptance import nuget_capture
from three_workflow_delivery_v3.acceptance import nuget_operator as operator
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference

TOKEN = "controlled-operator-read-credential"  # noqa: S105
INFO = b"controlled dotnet runtime information\n"
DLL = "WorkflowDeliveryV3DotnetProvider.dll"


def _sha(content):
    return hashlib.sha256(content).hexdigest()


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    archive = tmp_path / "helper.zip"
    files = {
        DLL: b"controlled prebuilt helper bytes",
        "WorkflowDeliveryV3DotnetProvider.deps.json": b"{}",
        "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json": b"{}",
        "NuGet.Packaging.dll": b"controlled complete dependency",
    }
    with zipfile.ZipFile(archive, "x") as stream:
        for name, content in files.items():
            stream.writestr(name, content)
    digest = _sha(archive.read_bytes())
    audit = tmp_path / "audit.md"
    audit.write_bytes(b"Controlled independent audit, not native evidence.\n")
    reference = ArtifactReference(
        123,
        "sha256:" + digest,
        "https://api.github.com/repos/hcoona/three/actions/artifacts/123/zip",
        "helper.zip",
        "sha256:" + digest,
    )
    capture = nuget_capture.NuGetCaptureRequest(
        "controlled-generation",
        "preflight",
        "a" * 40,
        digest,
        12024661,
        "1.0.0-beta.12",
        nuget_capture.NuGetCaptureLimits(12, 4, 1_000_000, 4, 10),
    )
    request = operator.NuGetReadRequest(
        (capture,),
        120,
        reference,
        "b" * 40,
        456,
        _sha(audit.read_bytes()),
        tuple(
            (operator._HELPER_ROOT + name, _sha(b"source"))
            for name in (
                "Program.cs",
                "WorkflowDeliveryV3DotnetProvider.csproj",
                "packages.lock.json",
            )
        ),
        20,
        1_000_000,
        5,
        _sha(Path(sys.executable).read_bytes()),
        _sha(INFO),
        nuget_capture._reader_runtime(),
    )
    checkout = tmp_path / "checkout"
    checkout.mkdir()
    monkeypatch.setattr(operator.shutil, "which", lambda _name: sys.executable)
    return (
        request,
        {
            "checkout": checkout,
            "helper_archive": archive,
            "helper_audit": audit,
            "audit_directory": tmp_path / "operator",
            "token": TOKEN,
        },
        files,
    )


def _read(path):
    return parse_canonical_json(path.read_bytes())


def _completed(_target, arguments, _timeout):
    request, index, directory, *_rest = arguments
    output = directory / "capture"
    output.mkdir()
    (output / "capture.json").write_bytes(
        canonicalize(
            {
                "requestDigest": canonical_sha256(
                    request.captures[index].to_document()
                ),
                "controlled": True,
                "scenarioPackage": None,
            }
        )
    )
    return 0


def test_operator_retains_original_helper_and_exact_capture_request(
    inputs, monkeypatch
):
    request, kwargs, files = inputs
    admit = Mock()
    monkeypatch.setattr(operator, "_admit_source", admit)
    supervise = Mock(side_effect=_completed)
    monkeypatch.setattr(operator, "_supervise", supervise)
    collector = operator.NuGetReadOperator(request, **kwargs)
    path = collector.capture("preflight")
    assert _read(path)["requestDigest"] == canonical_sha256(
        request.captures[0].to_document()
    )
    retained = _read(collector.directory / "request.json")
    assert retained["helper"]["producerToolingSha"] == "b" * 40
    assert retained["helper"]["producerRunId"] == 456
    assert retained["captures"][0]["callerToolingSha"] == "a" * 40
    assert _read(collector.directory / "helper-files.json") == {
        name: _sha(body) for name, body in files.items()
    }
    assert all(
        (collector.dll.parent / name).read_bytes() == body
        for name, body in files.items()
    )
    assert _read(collector.directory / "preflight-completed.json")[
        "captureSha256"
    ] == _sha(path.read_bytes())
    assert (
        _read(collector.directory / "preflight-completed.json")[
            "workerReturnCode"
        ]
        == 0
    )
    assert admit.call_count == 2
    assert all(
        call.args[0] == kwargs["checkout"] for call in admit.call_args_list
    )
    assert all(
        call.args[1].to_document() == request.to_document()
        for call in admit.call_args_list
    )
    assert supervise.call_count == 1
    with pytest.raises(ValueError, match="allowance exhausted"):
        collector.capture("preflight")
    with pytest.raises(FileExistsError):
        operator.NuGetReadOperator(request, **kwargs)
    assert supervise.call_count == 1


def test_operator_orders_six_positions_with_cumulative_reservations(
    inputs, monkeypatch
):
    request, kwargs, _files = inputs
    request = replace(
        request,
        captures=tuple(
            replace(request.captures[0], label=label)
            for label in operator._POSITIONS
        ),
    )
    monkeypatch.setattr(operator, "_admit_source", Mock())
    supervise = Mock(side_effect=_completed)
    monkeypatch.setattr(operator, "_supervise", supervise)
    collector = operator.NuGetReadOperator(request, **kwargs)
    with pytest.raises(ValueError, match="unexpected capture position"):
        collector.capture("after-create")
    assert not list(collector.directory.glob("*-reserved.json"))
    for label in operator._POSITIONS:
        collector.capture(label)
    assert supervise.call_count == 6
    assert len(list(collector.directory.glob("*-reserved.json"))) == 6
    assert request.to_document()["totalAllowances"] == {
        "requests": 72,
        "versionPages": 24,
        "responseBytes": 6_000_000,
        "helperCalls": 120,
        "helperOutputBytes": 120_000_120,
    }
    assert [call.args[1][1] for call in supervise.call_args_list] == list(
        range(6)
    )


@pytest.mark.parametrize(
    "failure", ["nonzero", "timeout", "mismatched-result", "late"]
)
def test_operator_failure_spends_generation_and_preserves_partial_evidence(
    inputs, monkeypatch, failure
):
    request, kwargs, _files = inputs
    monkeypatch.setattr(operator, "_admit_source", Mock())
    collector = operator.NuGetReadOperator(request, **kwargs)

    def invoke(target, arguments, timeout):
        directory = arguments[2]
        (directory / "safe-partial.bin").write_bytes(
            b"original safe partial evidence"
        )
        if failure == "nonzero":
            return 1
        if failure == "timeout":
            raise TimeoutError
        _completed(target, arguments, timeout)
        if failure == "mismatched-result":
            (directory / "capture/capture.json").write_bytes(
                canonicalize({"requestDigest": "sha256:" + "c" * 64})
            )
        if failure == "late":
            collector.deadline = 0
        return 0

    supervise = Mock(side_effect=invoke)
    monkeypatch.setattr(operator, "_supervise", supervise)
    with pytest.raises((ValueError, TimeoutError)):
        collector.capture("preflight")
    assert (
        collector.directory / "preflight/safe-partial.bin"
    ).read_bytes() == b"original safe partial evidence"
    assert (
        _read(collector.directory / "preflight-failed.json")["generationSpent"]
        is True
    )
    assert (collector.directory / "preflight-reserved.json").exists()
    assert not (collector.directory / "preflight-completed.json").exists()
    with pytest.raises(ValueError, match="already failed"):
        collector.capture("preflight")
    assert supervise.call_count == 1


@pytest.mark.parametrize("changed", ["audit", "archive", "dotnet", "source"])
def test_operator_rejects_substituted_inputs_before_execution(
    inputs, monkeypatch, changed
):
    request, kwargs, _files = inputs
    admit = Mock()
    monkeypatch.setattr(operator, "_admit_source", admit)
    supervise = Mock()
    monkeypatch.setattr(operator, "_supervise", supervise)
    if changed in {"audit", "archive"}:
        kwargs["helper_" + changed].write_bytes(b"substituted bytes")
    elif changed == "dotnet":
        request = replace(request, dotnet_executable_sha256="c" * 64)
    else:
        admit.side_effect = ValueError("source mismatch")
    with pytest.raises(ValueError, match="mismatch"):
        operator.NuGetReadOperator(request, **kwargs)
    supervise.assert_not_called()
    assert not list(kwargs["audit_directory"].glob("*-reserved.json"))


def test_operator_source_admission_checks_actual_python_and_helper_inputs(
    inputs, monkeypatch
):
    request, kwargs, _files = inputs
    root = kwargs["checkout"]
    source = (
        root
        / operator._SOURCE
        / "three_workflow_delivery_v3/acceptance/nuget_operator.py"
    )
    source.parent.mkdir(parents=True)
    source.write_bytes(b"controlled Python source")
    monkeypatch.setattr(operator, "__file__", str(source))
    state = {"head": ("a" * 40).encode(), "status": b"", "helper": b"source"}

    def git(_root, *args):
        if args[0] == "rev-parse":
            return state["head"] + b"\n"
        if args[0] == "status":
            return state["status"]
        if args[1].endswith("nuget_operator.py"):
            return b"controlled Python source"
        if args[1].startswith("a" * 40 + ":"):
            return state["helper"]
        return b"source"

    monkeypatch.setattr(operator, "_git", git)
    operator._admit_source(root, request)
    source.write_bytes(b"changed imported Python")
    with pytest.raises(ValueError, match="source bytes mismatch"):
        operator._admit_source(root, request)
    source.write_bytes(b"controlled Python source")
    bad = replace(
        request,
        helper_source_inputs=tuple(
            (name, "c" * 64) for name, _digest in request.helper_source_inputs
        ),
    )
    with pytest.raises(
        ValueError, match="dependency source compatibility mismatch"
    ):
        operator._admit_source(root, bad)
    for key, replacement, message in (
        ("head", ("c" * 40).encode(), "checkout revision mismatch"),
        ("status", b" M changed.py", "clean checkout"),
        ("helper", b"changed build input", "source compatibility mismatch"),
    ):
        original = state[key]
        state[key] = replacement
        with pytest.raises(ValueError, match=message):
            operator._admit_source(root, request)
        state[key] = original


def test_operator_request_roundtrip_rejects_scope_and_budget_changes(inputs):
    request, _kwargs, _files = inputs
    assert (
        operator.read_request(canonicalize(request.to_document())).to_document()
        == request.to_document()
    )
    for key, value in (
        ("schema", "wrong"),
        ("totalAllowances", {}),
        ("unexpected", True),
    ):
        document = request.to_document()
        document[key] = value
        with pytest.raises(ValueError, match="closure mismatch"):
            operator.read_request(canonicalize(document))
    for changes in (
        {"helper_calls_per_capture": 0},
        {"helper_output_bytes_per_call": True},
        {"generation_timeout_seconds": float("inf")},
        {"helper_timeout_seconds": 0},
        {
            "captures": (
                replace(request.captures[0], label="unapproved-position"),
            )
        },
        {"captures": (replace(request.captures[0], container_id=999),)},
    ):
        with pytest.raises(
            ValueError, match=r"invalid|operator requires|unselected"
        ):
            replace(request, **changes)


@pytest.mark.parametrize("form", ["raw", "basic", "quoted-basic"])
def test_operator_rejects_reflected_credentials_before_persistence(
    tmp_path, form
):
    secret = (
        TOKEN
        if form == "raw"
        else base64.b64encode(f"hcoona:{TOKEN}".encode()).decode()
    )
    if form == "quoted-basic":
        secret = quote(secret, safe="")
    evidence = operator._Evidence(tmp_path, TOKEN)
    with pytest.raises(ValueError, match="credential reflected"):
        evidence.write("unsafe.bin", secret.encode())
    assert not (tmp_path / "unsafe.bin").exists()


@pytest.mark.skipif(os.name != "posix", reason="POSIX local operator contract")
def test_helper_command_retains_separate_output_and_excludes_credentials(
    tmp_path, monkeypatch
):
    monkeypatch.setenv("WDV3_TEST_SECRET", TOKEN)
    monkeypatch.setenv("GITHUB_TOKEN", TOKEN)
    environment = operator.neutral_dotnet_environment()
    result = operator._command(
        (
            sys.executable,
            "-c",
            (
                "import os,sys,json; print(json.dumps({"
                "'secret':os.getenv('WDV3_TEST_SECRET'), "
                "'token':os.getenv('GITHUB_TOKEN')})); "
                "sys.stderr.write('safe diagnostic\\n')"
            ),
        ),
        cwd=tmp_path,
        environment=environment,
        timeout=10,
        output_limit=2048,
        evidence=operator._Evidence(tmp_path, TOKEN),
    )
    assert json.loads(result) == {"secret": None, "token": None}
    assert (tmp_path / "stderr.bin").read_bytes() == b"safe diagnostic\n"
    assert _read(tmp_path / "process.json")["returnCode"] == 0
    assert all(
        TOKEN.encode() not in path.read_bytes() for path in tmp_path.iterdir()
    )


@pytest.mark.skipif(os.name != "posix", reason="POSIX local operator contract")
@pytest.mark.parametrize("kind", ["overflow", "failure", "timeout"])
def test_helper_command_stops_and_retains_bounded_partial_output(
    tmp_path, kind
):
    script = {
        "overflow": "import os; os.write(1, b'x' * 10000)",
        "failure": "import sys; print('safe partial'); sys.exit(7)",
        "timeout": (
            "import time; print('safe partial', flush=True); time.sleep(60)"
        ),
    }[kind]
    with pytest.raises(
        ValueError, match=r"bound exceeded|command failed|deadline expired"
    ):
        operator._command(
            (sys.executable, "-c", script),
            cwd=tmp_path,
            environment={},
            timeout=0.5 if kind == "timeout" else 10,
            output_limit=128,
            evidence=operator._Evidence(tmp_path, TOKEN),
        )
    assert (
        sum(
            (tmp_path / name).stat().st_size
            for name in ("stdout.bin", "stderr.bin")
        )
        <= 128
    )
    process = _read(tmp_path / "process.json")
    assert process["outputComplete"] is (kind == "failure")
    if kind == "failure":
        assert process["returnCode"] == 7
    if kind != "overflow":
        assert (tmp_path / "stdout.bin").read_bytes() == b"safe partial\n"


@pytest.mark.parametrize("mismatch", ["reader", "dotnet", "info"])
def test_worker_admits_local_runtime_before_any_destination_read(
    inputs, tmp_path, monkeypatch, mismatch
):
    request, _kwargs, _files = inputs
    connection = Mock()
    connection.recv.return_value = "start"
    monkeypatch.setattr(operator.os, "setsid", Mock())
    helper_command = Mock(return_value=INFO)
    monkeypatch.setattr(operator._Helper, "command", helper_command)
    capture = Mock()
    monkeypatch.setattr(operator.nuget_capture, "capture_nuget_state", capture)
    operator._worker(
        connection,
        request,
        0,
        tmp_path,
        tmp_path / DLL,
        Path(sys.executable),
        TOKEN,
    )
    assert capture.call_count == 1
    assert capture.call_args.kwargs["token"] == TOKEN
    assert capture.call_args.args == (request.captures[0],)
    helper_command.assert_called_once_with(("--info",))
    capture.reset_mock()
    changed = {
        "reader": {"reader_runtime": {"unexpected": True}},
        "dotnet": {"dotnet_executable_sha256": "c" * 64},
        "info": {"dotnet_info_sha256": "c" * 64},
    }[mismatch]
    with pytest.raises(SystemExit) as stopped:
        operator._worker(
            connection,
            replace(request, **changed),
            0,
            tmp_path,
            tmp_path / DLL,
            Path(sys.executable),
            TOKEN,
        )
    assert stopped.value.code == 1
    capture.assert_not_called()
    assert _read(tmp_path / "worker-failure.json") == {
        "errorType": "ValueError"
    }


@pytest.mark.skipif(
    sys.platform != "linux", reason="Linux process-group integration evidence"
)
@pytest.mark.parametrize("kind", ["complete", "timeout", "descendant"])
def test_supervisor_terminates_owned_process_group(tmp_path, monkeypatch, kind):
    module_name = "wdv3_controlled_supervision_fixture"
    (tmp_path / (module_name + ".py")).write_text(
        "import os,sys,subprocess,time\n"
        "from pathlib import Path\n"
        "def run(connection, directory, kind):\n"
        "    os.setsid()\n"
        "    connection.send('ready')\n"
        "    if connection.recv() != 'start': return\n"
        "    Path(directory, 'worker.pid').write_text(str(os.getpid()))\n"
        "    if kind == 'descendant':\n"
        "        child = subprocess.Popen([sys.executable, '-c', "
        "'import time; time.sleep(60)'], env={})\n"
        "        Path(directory, 'descendant.pid').write_text(str(child.pid))\n"
        "    if kind == 'timeout': time.sleep(60)\n",
        encoding="utf-8",
    )
    monkeypatch.syspath_prepend(str(tmp_path))
    sys.modules.pop(module_name, None)
    fixture = importlib.import_module(module_name)
    try:
        if kind == "timeout":
            with pytest.raises(ValueError, match="deadline expired"):
                operator._supervise(fixture.run, (str(tmp_path), kind), 3)
        else:
            assert (
                operator._supervise(fixture.run, (str(tmp_path), kind), 10) == 0
            )
        for name in ("worker.pid", "descendant.pid"):
            if not (tmp_path / name).exists():
                continue
            pid = int((tmp_path / name).read_text())
            status = Path(f"/proc/{pid}/stat")
            deadline = time.monotonic() + 3
            while (
                status.exists()
                and status.read_text().split()[2] != "Z"
                and time.monotonic() < deadline
            ):
                time.sleep(0.01)
            assert not status.exists() or status.read_text().split()[2] == "Z"
    finally:
        sys.modules.pop(module_name, None)


def test_operator_cli_rejects_suite_without_execution(
    inputs, tmp_path, monkeypatch, capsys
):
    request, kwargs, _files = inputs
    request = replace(
        request,
        captures=tuple(
            replace(request.captures[0], label=label)
            for label in operator._POSITIONS
        ),
    )
    request_path = tmp_path / "request.json"
    request_path.write_bytes(canonicalize(request.to_document()))
    constructor = Mock()
    monkeypatch.setattr(operator, "NuGetReadOperator", constructor)
    assert (
        operator.main(
            [
                "--request",
                str(request_path),
                "--checkout",
                str(kwargs["checkout"]),
                "--helper-archive",
                str(kwargs["helper_archive"]),
                "--helper-audit",
                str(kwargs["helper_audit"]),
                "--audit-directory",
                str(kwargs["audit_directory"]),
                "--token-env",
                "WDV3_UNSET_TEST_TOKEN",
            ]
        )
        == 1
    )
    constructor.assert_not_called()
    assert capsys.readouterr().err == ""


@pytest.mark.parametrize("present", [False, True])
def test_operator_preflight_collects_state_and_requires_absence(  # noqa: C901, PLR0915
    inputs, monkeypatch, present
):
    request, kwargs, _files = inputs
    monkeypatch.setattr(operator, "_admit_source", Mock())
    native = operator.native
    package = native.NUGET_PACKAGE_ID
    version = request.captures[0].version
    base = "https://nuget.pkg.github.com/hcoona/download/"

    def helper_command(argv, **_kwargs):
        arguments = argv[1:]
        if arguments == ("--info",):
            return INFO
        operation = arguments[1]
        if operation == "normalize-identity":
            result = {
                "displayPackageId": arguments[2],
                "displayVersion": arguments[3],
                "normalizedPackageId": package.lower(),
                "normalizedVersion": version,
            }
        elif operation == "service-resources":
            result = {
                "packageBaseAddress": base,
                "packagePublish": "https://nuget.pkg.github.com/hcoona/",
            }
        else:
            result = {
                "identity": {
                    "displayPackageId": package,
                    "displayVersion": version,
                    "normalizedPackageId": package.lower(),
                    "normalizedVersion": version,
                },
                "witnessBase64": base64.b64encode(
                    b'{"purpose":"destination-acceptance"}'
                ).decode(),
                "entries": ["workflow-delivery/provenance.json"],
            }
        return canonicalize(result)

    def get(url, **_bounds):
        if url == native.NUGET_SERVICE_INDEX:
            body = canonicalize({"resources": []})
        elif "state=active" in url:
            body = canonicalize([{"id": 7, "name": version}] if present else [])
        elif url.startswith("https://api.github.com/"):
            body = canonicalize(
                {
                    "id": 12024661,
                    "name": package,
                    "package_type": "nuget",
                    "visibility": "public",
                    "repository": {"full_name": "hcoona/three"},
                }
            )
        elif url.endswith(".nupkg"):
            body = b"controlled original package bytes"
        else:
            body = canonicalize({"versions": [version] if present else []})
        return native.NuGetHttpResponse(url, 200, (), body)

    transport = Mock(spec=native.NuGetReadTransport)
    transport.get.side_effect = get
    monkeypatch.setattr(
        native, "NuGetHttpTransport", Mock(return_value=transport)
    )
    monkeypatch.setattr(operator, "_command", helper_command)
    monkeypatch.setattr(operator.os, "setsid", Mock())

    def supervise(target, arguments, _timeout):
        connection = Mock()
        connection.recv.return_value = "start"
        target(connection, *arguments)
        return 0

    monkeypatch.setattr(operator, "_supervise", supervise)
    collector = operator.NuGetReadOperator(request, **kwargs)
    if present:
        with pytest.raises(
            ValueError, match="preflight coordinate is not absent"
        ):
            collector.capture("preflight")
        output = collector.directory / "preflight/capture/capture.json"
        assert collector.failed is True
        assert (collector.directory / "preflight-failed.json").exists()
    else:
        output = collector.capture("preflight")
        assert (collector.directory / "preflight-completed.json").exists()
    capture = _read(output)
    assert capture["coordinate"] == package.lower() + "@" + version
    assert capture["activeCoordinates"] == (
        [capture["coordinate"]] if present else []
    )
    assert capture["counts"]["requests"] == (5 if present else 4)
    for name, reference in capture["files"].items():
        assert _sha((output.parent / name).read_bytes()) == reference["sha256"]
    assert all(
        TOKEN.encode() not in path.read_bytes()
        for path in collector.directory.rglob("*")
        if path.is_file()
    )


def test_helper_allowance_counts_runtime_check_and_never_retries(
    inputs, tmp_path, monkeypatch
):
    request, _kwargs, _files = inputs
    request = replace(request, helper_calls_per_capture=2)
    monkeypatch.setenv("GITHUB_TOKEN", TOKEN)
    command = Mock(return_value=b"{}")
    monkeypatch.setattr(operator, "_command", command)
    helper = operator._Helper(
        request,
        tmp_path / DLL,
        Path(sys.executable),
        operator._Evidence(tmp_path, TOKEN),
    )
    assert helper.command(("--info",)) == b"{}"
    assert helper.normalize_identity("selected", "1.0.0") == {}
    with pytest.raises(ValueError, match="call allowance exhausted"):
        helper.command(("--info",))
    assert command.call_count == 2
    assert len(list(tmp_path.glob("helper-*"))) == 2
    assert (
        command.call_args.kwargs["output_limit"]
        == request.helper_output_bytes_per_call
    )
    assert command.call_args.kwargs["timeout"] == request.helper_timeout_seconds
    assert "GITHUB_TOKEN" not in command.call_args.kwargs["environment"]


def test_operator_rejects_unsupported_host_before_input_or_process_effects(
    inputs, monkeypatch
):
    request, kwargs, _files = inputs
    admit = Mock()
    monkeypatch.setattr(operator, "_admit_source", admit)
    monkeypatch.setattr(operator, "os", SimpleNamespace(name="nt"))
    with pytest.raises(ValueError, match="requires POSIX"):
        operator.NuGetReadOperator(request, **kwargs)
    admit.assert_not_called()
    assert not kwargs["audit_directory"].exists()


@pytest.mark.parametrize("remaining", [2, 0])
def test_operator_applies_remaining_generation_deadline(
    inputs, monkeypatch, remaining
):
    request, kwargs, _files = inputs
    monkeypatch.setattr(operator, "_admit_source", Mock())
    collector = operator.NuGetReadOperator(request, **kwargs)
    collector.started = 0
    collector.deadline = 100
    monkeypatch.setattr(operator.time, "monotonic", lambda: 100 - remaining)
    supervise = Mock(side_effect=_completed)
    monkeypatch.setattr(operator, "_supervise", supervise)
    if remaining:
        collector.capture("preflight")
        assert supervise.call_args.args[2] == remaining
        assert (collector.directory / "preflight-completed.json").exists()
    else:
        with pytest.raises(ValueError, match="generation deadline expired"):
            collector.capture("preflight")
        supervise.assert_not_called()
        assert collector.failed is True
        assert (collector.directory / "preflight-reserved.json").exists()
        assert not (collector.directory / "preflight-completed.json").exists()


@pytest.mark.parametrize("change", ["helper", "source", "evidence-collision"])
def test_operator_stops_generation_when_admitted_inputs_drift(
    inputs, monkeypatch, change
):
    request, kwargs, _files = inputs
    admit = Mock()
    monkeypatch.setattr(operator, "_admit_source", admit)
    collector = operator.NuGetReadOperator(request, **kwargs)
    supervise = Mock()
    monkeypatch.setattr(operator, "_supervise", supervise)
    if change == "helper":
        collector.dll.write_bytes(b"changed helper")
    elif change == "source":
        admit.side_effect = ValueError("source changed")
    else:
        (collector.directory / "preflight").mkdir()
    with pytest.raises((ValueError, FileExistsError)):
        collector.capture("preflight")
    assert collector.failed is True
    assert (
        _read(collector.directory / "preflight-failed.json")["generationSpent"]
        is True
    )
    supervise.assert_not_called()
