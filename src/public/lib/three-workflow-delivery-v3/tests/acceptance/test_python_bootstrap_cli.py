"""CLI admission, immutable original payloads and capability-free transport."""

import hashlib
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import python_bootstrap
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    pack_bundle,
    unpack_bundle,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

from . import test_python_bootstrap_suite as suite_tests

scenario = suite_tests.scenario
bootstrap_fixtures = suite_tests.bootstrap_fixtures
deny_real_registry = suite_tests.deny_real_registry

_ROOT = Path(__file__).resolve().parents[6]
_TOOLING = "c" * 40
_ROLES = ("prepared", "authorization", "marker", "result", "audit")
_HOSTED = {
    "prepare": (),
    "authorize": ("prepared",),
    "marker": ("prepared", "authorization"),
    "execute": ("prepared", "authorization", "marker"),
    "audit": ("prepared", "authorization", "marker", "result"),
}


def _denied(*_args, **_kwargs):
    pytest.fail("Rejected bootstrap CLI input reached a capability boundary")


def _hosted_args(command, output):
    args = [
        command,
        "--root",
        str(_ROOT),
        "--request-digest",
        "sha256:" + "f" * 64,
        "--tooling-sha",
        _TOOLING,
        "--output",
        str(output),
    ]
    for role in _HOSTED[command]:
        args.extend((f"--{role}", str(output.parent / f"{role}.zip")))
    return args


@pytest.mark.parametrize("command", _HOSTED)
def test_bootstrap_cli_null_slot_blocks_before_hosted_or_network(
    command,
    tmp_path,
    monkeypatch,
    capsys,
):
    """The delivered null slot admits none of the hosted phase commands."""
    monkeypatch.setattr(python_bootstrap, "validate_hosted_targets", _denied)
    monkeypatch.setattr(python_bootstrap, "PythonHttpsTransport", _denied)
    output = tmp_path / "uncreated"
    assert python_bootstrap.main(_hosted_args(command, output)) == 1
    assert not output.exists()
    assert "grants no retry" in capsys.readouterr().err


@pytest.mark.parametrize(
    "flag", ["--registry", "--account", "--project", "--run-id", "--deadline"]
)
def test_bootstrap_cli_has_no_hosted_authority_override(
    flag, tmp_path, monkeypatch
):
    """Unknown command arguments cannot substitute for protected authority."""
    monkeypatch.setattr(python_bootstrap, "load_bootstrap_request", _denied)
    args = _hosted_args("prepare", tmp_path / "uncreated")
    with pytest.raises(SystemExit) as error:
        python_bootstrap.main([*args, flag, "override"])
    assert error.value.code == 2  # noqa: PLR2004 - argparse invalid-input exit


@pytest.mark.parametrize(
    "command", [*_HOSTED, "build-fixtures", "archive", "replay"]
)
def test_bootstrap_cli_rejects_existing_output_before_any_work(
    command, tmp_path, monkeypatch
):
    """Retained originals are not overwritten or reused as resumed phases."""
    output = tmp_path / "original"
    output.write_bytes(b"preserved original")
    if command in _HOSTED:
        args = _hosted_args(command, output)
    elif command == "build-fixtures":
        args = [command, "--target", _TOOLING, "--output", str(output)]
    elif command == "archive":
        args = [command, "--directory", str(tmp_path), "--output", str(output)]
    else:
        args = [
            command,
            "--references",
            str(tmp_path / "missing.json"),
            "--output",
            str(output),
        ]
        for role in _ROLES:
            args.extend((f"--{role}", str(tmp_path / f"{role}.zip")))
    for name in (
        "load_bootstrap_request",
        "build_bootstrap_fixture",
        "PythonHttpsTransport",
        "replay_audit",
    ):
        monkeypatch.setattr(python_bootstrap, name, _denied)
    assert python_bootstrap.main(args) == 1
    assert output.read_bytes() == b"preserved original"


def test_bootstrap_cli_archive_and_digest_preserve_exact_originals(
    tmp_path, monkeypatch, capsys
):
    """Packed payload preserves raw bytes and the action output digest."""
    evidence = tmp_path / "evidence"
    (evidence / "http").mkdir(parents=True)
    (evidence / "http" / "0.body").write_bytes(b"exact raw\x00response\n")
    (evidence / "requests.json").write_bytes(b"[]")
    output = tmp_path / "result.zip"
    monkeypatch.setattr(python_bootstrap, "PythonHttpsTransport", _denied)
    assert (
        python_bootstrap.main(
            ["archive", "--directory", str(evidence), "--output", str(output)]
        )
        == 0
    )
    content = output.read_bytes()
    assert unpack_bundle(content) == {
        "http/0.body": b"exact raw\x00response\n",
        "requests.json": b"[]",
    }
    action_output = tmp_path / "github-output"
    action_output.write_text("earlier=value\n")
    monkeypatch.setenv("GITHUB_OUTPUT", str(action_output))
    expected = "sha256:" + hashlib.sha256(content).hexdigest()
    assert python_bootstrap.main(["digest", "--output", str(output)]) == 0
    assert action_output.read_text() == f"earlier=value\ndigest={expected}\n"
    assert capsys.readouterr().out == expected + "\n"
    assert output.read_bytes() == content


def test_bootstrap_cli_archive_does_not_follow_evidence_symlinks(tmp_path):
    """An evidence alias cannot silently include bytes outside its directory."""
    secret = tmp_path / "outside"
    secret.write_bytes(b"outside evidence")
    evidence = tmp_path / "evidence"
    evidence.mkdir()
    (evidence / "alias").symlink_to(secret)
    output = tmp_path / "uncreated.zip"
    assert (
        python_bootstrap.main(
            ["archive", "--directory", str(evidence), "--output", str(output)]
        )
        == 1
    )
    assert not output.exists()


@pytest.mark.parametrize(
    "change",
    ["service-digest", "payload-digest", "current-run", "payload-name"],
)
@pytest.mark.parametrize("changed_role", _ROLES)
def test_bootstrap_cli_replay_rejects_foreign_original_before_audit(
    change, changed_role, tmp_path, monkeypatch
):
    """Offline replay binds original bytes to every supplied reference."""
    content = pack_bundle({"untrusted": b"not yet admitted"})
    digest = "sha256:" + hashlib.sha256(content).hexdigest()
    lineage = {"run-id": 911, "tooling-sha": _TOOLING}
    args = [
        "replay",
        "--references",
        str(tmp_path / "references.json"),
        "--output",
        str(tmp_path / "uncreated.zip"),
    ]
    for number, role in enumerate(_ROLES, 1):
        path = tmp_path / f"{role}.zip"
        path.write_bytes(content)
        lineage[role] = {
            "artifact-id": number,
            "artifact-digest": digest,
            "artifact-url": f"https://github.com/hcoona/three/actions/runs/911/artifacts/{number}",
            "payload-path": path.name,
            "payload-digest": digest,
        }
        args.extend((f"--{role}", str(path)))
    reference = lineage[changed_role]
    if change in {"service-digest", "payload-digest"}:
        reference[
            "artifact-digest"
            if change == "service-digest"
            else "payload-digest"
        ] = "sha256:" + "f" * 64
    elif change == "current-run":
        reference["artifact-url"] = reference["artifact-url"].replace(
            "/911/", "/912/"
        )
    else:
        reference["payload-path"] = "foreign.zip"
    (tmp_path / "references.json").write_bytes(canonicalize(lineage))
    for name in (
        "load_bootstrap_request",
        "validate_hosted_targets",
        "PythonHttpsTransport",
        "replay_audit",
    ):
        monkeypatch.setattr(python_bootstrap, name, _denied)
    assert python_bootstrap.main(args) == 1
    assert not (tmp_path / "uncreated.zip").exists()


def test_bootstrap_cli_suppresses_credential_bearing_failure(
    tmp_path, monkeypatch, capsys
):
    """Unexpected dependency failures cannot print credentials or tracebacks."""
    secret = "fake-credential-must-never-escape"  # noqa: S105 - synthetic leak canary

    def fail_with_secret(*_args):
        raise RuntimeError(secret)

    monkeypatch.setattr(
        python_bootstrap, "load_bootstrap_request", fail_with_secret
    )
    monkeypatch.setattr(python_bootstrap, "PythonHttpsTransport", _denied)
    output = tmp_path / "uncreated"
    assert python_bootstrap.main(_hosted_args("prepare", output)) == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err == (
        "Bootstrap phase failed; retained evidence grants no retry, "
        "account ownership, native admission or publication authority.\n"
    )
    assert secret not in captured.err
    assert not output.exists()


def test_bootstrap_cli_replays_all_five_original_bundles_offline(
    scenario, monkeypatch
):
    """Actual CLI replay admits the complete durable lineage without HTTP."""
    scenario.authorize()
    scenario.execute()
    scenario.audit()
    context = scenario.context
    lineage = {
        "run-id": context.run_id,
        "tooling-sha": context.tooling_sha,
        **{
            role: reference.to_document()
            for role, reference in context.references.items()
        },
    }
    references = scenario.root / "references.json"
    references.write_bytes(canonicalize(lineage))
    output = scenario.root / "replay.zip"
    args = ["replay", "--references", str(references), "--output", str(output)]
    for role, reference in context.references.items():
        args.extend((f"--{role}", str(scenario.root / reference.payload_path)))
    for name in (
        "load_bootstrap_request",
        "validate_hosted_targets",
        "PythonHttpsTransport",
    ):
        monkeypatch.setattr(python_bootstrap, name, _denied)
    for name in scenario.environment:
        monkeypatch.delenv(name, raising=False)
    assert python_bootstrap.main(args) == 0
    replayed = unpack_bundle(output.read_bytes())
    assert replayed["lineage.json"] == canonicalize(lineage)
    assert parse_canonical_json(replayed["result.json"]) == {
        "status": "success",
        "account-ownership": False,
        "native-admission": False,
        "live-enabled": False,
        "evidence": "supplied-facts-only",
    }
