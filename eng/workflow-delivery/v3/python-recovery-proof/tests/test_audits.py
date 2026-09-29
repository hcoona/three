# Fixture identities and limits are literal expected contract values.
# Delayed caller import occurs after controlled dependency injection.
# ruff: noqa: E501, PLR2004, PLC0415, N814

"""Caller audit behavior; controlled native observations, no external requests."""

import hashlib
import importlib
import sys
import zipfile
from contextlib import nullcontext
from pathlib import Path
from types import SimpleNamespace as NS

import pytest
from campaign import digest, save_json
from proof_checks import (
    diagnostic_gate,
    preparation,
    result_contract,
    seed_archives,
)
from terminal_controls import FIELDS, extract
from test_campaign import (
    campaign as campaign,  # noqa: PLC0414 - pytest fixture re-export.
)
from test_campaign import prepare, write_gate


def snapshot(mode, dispositions, classification="absent"):
    """Provide the controlled snapshot fixture."""
    return NS(
        registry=NS(name="testpypi"),
        action_required=True,
        proof_mode=mode,
        dispositions=dispositions,
        observation=NS(classification=classification),
    )


def test_fresh_recovery_requires_both_rebuilt_originals_and_exact_plan() -> (
    None
):
    """Verify fresh recovery requires both rebuilt originals and exact plan."""
    binding = {"mode": "none"}
    snap = snapshot("none", ("already-present", "upload"), "exact-subset")
    assert preparation(
        snap,
        binding,
        (b"fresh-wheel", b"fresh-sdist"),
        seed_contents=(b"fresh-wheel", b"fresh-sdist"),
    ) == ("already-present", "upload")
    with pytest.raises(AssertionError, match="rebuild bytes changed"):
        preparation(
            snap,
            binding,
            (b"fresh-wheel", b"different-sdist"),
            seed_contents=(b"fresh-wheel", b"fresh-sdist"),
        )
    with pytest.raises(AssertionError):
        preparation(
            snapshot("none", ("upload", "already-present")),
            binding,
            (b"w", b"s"),
            seed_contents=(b"w", b"s"),
        )


@pytest.mark.parametrize(
    ("mode", "classification", "registry"),
    [
        ("none", "absent", "testpypi"),
        ("stop-after-wheel", "exact-subset", "testpypi"),
        ("stop-after-wheel", "absent", "pypi"),
    ],
)
def test_seed_preparation_rejects_wrong_mode_destination_or_nonabsence(
    mode, classification, registry
) -> None:
    """Verify seed preparation rejects wrong mode destination or nonabsence."""
    snap = snapshot(mode, ("upload", "upload"), classification)
    snap.registry.name = registry
    with pytest.raises(AssertionError):
        preparation(snap, {"mode": "stop-after-wheel"}, (b"w", b"s"))


def test_seed_failed_result_and_recovery_success_require_exact_operations() -> (
    None
):
    """Verify seed failed result and recovery success require exact operations."""
    seed = NS(
        result="failed",
        operations=(NS(status="succeeded"), NS(status="not-attempted")),
    )
    result_contract(seed, "stop-after-wheel")
    recovery = NS(
        result="published",
        operations=(NS(status="already-present"), NS(status="succeeded")),
    )
    result_contract(recovery, "none")
    recovery.operations = (NS(status="succeeded"), NS(status="succeeded"))
    with pytest.raises(AssertionError):
        result_contract(recovery, "none")
    seed.result = "published"
    with pytest.raises(AssertionError):
        result_contract(seed, "stop-after-wheel")


@pytest.mark.parametrize(
    "field",
    [
        "terminal",
        "dispatch_resolved",
        "publisher_quiescent",
        "native_read_admitted",
    ],
)
def test_diagnostic_reads_require_resolved_quiescent_run(field) -> None:
    """Verify diagnostic reads require resolved quiescent run."""
    proof = {
        "run": 15,
        "terminal": True,
        "dispatch_resolved": True,
        "publisher_quiescent": True,
        "native_read_admitted": True,
    }
    diagnostic_gate(proof, 15)
    proof[field] = False
    with pytest.raises(AssertionError):
        diagnostic_gate(proof, 15)


def test_seed_bytes_are_read_from_original_edges_not_artifact_names(
    tmp_path,
) -> None:
    """Verify seed bytes are read from original edges not artifact names."""
    root = tmp_path / "02"
    seed = tmp_path / "01/operation/inputs"
    seed.mkdir(parents=True)
    refs = {
        "wheel": {"payload-path": "original.whl"},
        "sdist": {"payload-path": "original.tar.gz"},
    }
    save_json(seed / "references.json", refs)
    (seed / "original.whl").write_bytes(b"seed-wheel")
    (seed / "original.tar.gz").write_bytes(b"seed-sdist")
    assert seed_archives(root, {"seed_attempt": "01", "attempt": "02"}) == (
        b"seed-wheel",
        b"seed-sdist",
    )
    with pytest.raises(AssertionError):
        seed_archives(root, {"seed_attempt": "02", "attempt": "02"})


def make_terminal(tmp_path, *, duplicate=False, missing=False):
    """Provide the controlled make terminal fixture."""
    (tmp_path / "terminal-logs").mkdir()
    values = {
        "references": '{"result":{"artifact-id":15}}',
        "publisher_conclusion": "failure",
        "publication_step_outcome": "failure",
        "terminal_reference": '{"artifact-id":15}',
        "observation_conclusion": "success",
    }
    text = "".join(
        f"2026-09-28T12:00:00Z   {env}: {values[field]}\n"
        for field, env in FIELDS.items()
        if not (missing and field == "terminal_reference")
    )
    if duplicate:
        text += "2026-09-28T12:00:01Z   PUBLISHER_CONCLUSION: success\n"
    (tmp_path / "terminal-logs/000.log").write_text(text)
    with zipfile.ZipFile(tmp_path / "terminal-logs.zip", "w") as archive:
        archive.writestr("finalize-attempt/7_finalize Python outcome.txt", text)
    save_json(tmp_path / "jobs.body", {"jobs": []})
    save_json(
        tmp_path / "ledger.json",
        {
            "requests": [
                {"name": "terminal-jobs", "status": 200, "body": "jobs.body"}
            ]
        },
    )
    return values


def test_terminal_controls_preserve_original_failed_scalar_and_exact_log_hash(
    tmp_path,
) -> None:
    """Verify terminal controls preserve original failed scalar and exact log hash."""
    expected = make_terminal(tmp_path)
    controls = extract(tmp_path, 15, "a" * 40)
    assert {k: controls[k] for k in FIELDS} == expected
    assert controls["files_sha256"]["terminal-logs/000.log"] == digest(
        tmp_path / "terminal-logs/000.log"
    )
    assert (
        controls["source_archive_path"]
        == "finalize-attempt/7_finalize Python outcome.txt"
    )


@pytest.mark.parametrize("change", ["duplicate", "missing", "changed-local"])
def test_terminal_controls_reject_missing_ambiguous_or_changed_original(
    tmp_path, change
) -> None:
    """Verify terminal controls reject missing ambiguous or changed original."""
    make_terminal(
        tmp_path, duplicate=change == "duplicate", missing=change == "missing"
    )
    if change == "changed-local":
        (tmp_path / "terminal-logs/000.log").write_text("fabricated")
    with pytest.raises(AssertionError):
        extract(tmp_path, 15, "a" * 40)


@pytest.mark.parametrize(
    ("purpose", "variants"),
    [
        ("seed-proof", ["wheel"]),
        ("recovery-proof", ["wheel", "sdist"]),
        ("diagnostic", ["wheel"]),
        ("diagnostic", []),
        ("diagnostic", ["wheel", "sdist"]),
    ],
)
def test_native_audit_separates_diagnostic_and_proof_consumer_requirements(  # noqa: C901 - complete caller fixture and purpose table.
    campaign, monkeypatch, purpose, variants
) -> None:
    """Diagnostic state evidence never substitutes for complete-pair consumer proof."""
    mode = "none" if purpose == "recovery-proof" else "stop-after-wheel"
    root, binding = prepare(campaign, mode=mode)
    directory = root / "operation"
    (directory / "inputs").mkdir(parents=True)
    (directory / "inputs/references.json").write_text("{}")
    # Deliberately no Result or terminal-reference file: diagnostic admission is separate.
    write_gate(
        root,
        binding,
        "diagnostic-read",
        run=15,
        terminal=True,
        dispatch_resolved=True,
        publisher_quiescent=True,
        native_read_admitted=True,
        purpose=purpose,
    )
    trust_path = Path("/etc/ssl/certs/ca-certificates.crt")
    monkeypatch.setenv("SSL_CERT_FILE", str(trust_path))
    save_json(
        root / "local-ca-preparation.json",
        {"bundle_sha256": digest(trust_path)},
    )
    ledger = {"run": 15}
    save_json(directory / "ledger.json", ledger)

    class LocalOperator:
        """Provide the controlled LocalOperator fixture."""

        def __init__(self, *_args) -> None:
            """Provide the controlled   init   fixture."""
            self.ledger = ledger
            self.original_deadline = "2026-09-28T16:00:00+00:00"

        def deadline(self):
            """Provide the controlled deadline fixture."""
            return nullcontext()

        def remaining(self) -> int:
            """Provide the controlled remaining fixture."""
            return 60

        def now(self) -> str:
            """Provide the controlled now fixture."""
            return "2026-09-28T12:00:00+00:00"

        def save(self) -> None:
            """Provide the controlled save fixture."""
            save_json(directory / "ledger.json", self.ledger)

    dispatch = NS(BINDING=binding, ROOT=root, TARGET=binding["target"])
    monkeypatch.setitem(sys.modules, "dispatch_normal", dispatch)
    sys.modules.pop("audit_registry", None)
    module = importlib.import_module("audit_registry")
    monkeypatch.setattr(module, "Operator", LocalOperator)
    registry = NS(
        name="testpypi",
        index_url="https://test.pypi.org/simple/hcoona-release-smoke-python/",
        file_host="test-files.pythonhosted.org",
    )
    originals = [
        NS(
            variant=v,
            filename=binding["filenames"][i],
            digest="sha256:" + hashlib.sha256(v.encode()).hexdigest(),
            witness=NS(),
            content=v.encode(),
        )
        for i, v in enumerate(("wheel", "sdist"))
    ]
    artifacts = [
        NS(variant=d.variant, inspect=lambda _content, d=d: d)
        for d in originals
    ]
    decision = NS(
        artifacts=artifacts,
        snapshot=NS(
            governance=NS(registry=registry),
            model=NS(provider=NS(nbgv=NS(pep440_version=binding["version"]))),
        ),
    )
    monkeypatch.setattr(
        module,
        "PythonInputs",
        lambda *_args: NS(
            decision=lambda: decision, content=lambda role: role.encode()
        ),
    )
    audits, consumers, admissions, states = [], [], [], []

    class Audit:
        """The native parser journey is exercised by test_registry_read."""

        def __init__(
            self, output, _state, _deadline, _registry, expected, **kwargs
        ):
            output.mkdir(exist_ok=True)
            states.append(_state)
            audits.append(
                (
                    tuple(distribution.variant for distribution in expected),
                    kwargs.get("purpose"),
                )
            )
            admission = kwargs["admission"]
            assert isinstance(admission, module.AuditAdmission)
            assert admission.purpose == kwargs["purpose"]
            assert admission.identity["binding_sha256"] == digest(
                root / "execution-binding.json"
            )
            admissions.append(admission)

        def step(self):
            selected = tuple(x for x in originals if x.variant in variants)
            return NS(files=selected, to_document=lambda: {"files": variants})

    monkeypatch.setattr(module, "RegistryAudit", Audit)

    def consumer(distribution):
        """Provide the controlled consumer fixture."""
        consumers.append(distribution.variant)
        return NS(
            original_digest=distribution.digest,
            variant=distribution.variant,
            installed=b"{}",
            command_evidence=(b'{"exit-code":0}',),
            rebuilt_wheel=None,
        )

    monkeypatch.setattr(module, "qualify_python_consumer", consumer)
    if purpose != "diagnostic":
        write_gate(
            root,
            binding,
            "diagnostic-read",
            run=15,
            terminal=True,
            dispatch_resolved=True,
            publisher_quiescent=True,
            native_read_admitted=True,
            purpose="diagnostic",
        )
        module.main()
        assert consumers == []
        write_gate(
            root,
            binding,
            "diagnostic-read",
            run=15,
            terminal=True,
            dispatch_resolved=True,
            publisher_quiescent=True,
            native_read_admitted=True,
            purpose=purpose,
        )
    module.main()
    assert audits == (
        [] if purpose == "diagnostic" else [(("wheel", "sdist"), "diagnostic")]
    ) + [
        (
            ("wheel",) if purpose == "seed-proof" else ("wheel", "sdist"),
            purpose,
        )
    ]
    assert consumers == (
        ["wheel", "sdist"] if purpose == "recovery-proof" else []
    )
    if purpose != "diagnostic":
        assert states[0] is states[1]
        assert admissions[0].identity == admissions[1].identity
        assert (
            admissions[0].provenance["gate_sha256"]
            != admissions[1].provenance["gate_sha256"]
        )
    assert "registry_audit_completed" in ledger


@pytest.mark.parametrize("changed", ["none", "head", "tree", "dirty", "reader"])
def test_local_source_reader_is_exact_and_clean(monkeypatch, changed) -> None:
    """Verify local source reader is exact and clean."""
    import source_checks

    expected = (
        source_checks.CHECKOUT
        / "src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/__init__.py"
    )
    outputs = ["a" * 40, "b" * 40, ""]
    if changed == "head":
        outputs[0] = "c" * 40
    if changed == "tree":
        outputs[1] = "c" * 40
    if changed == "dirty":
        outputs[2] = " M modified.py"
    calls = []

    def git(argv, **_kwargs):
        """Provide the controlled git fixture."""
        calls.append(argv)
        return outputs.pop(0)

    monkeypatch.setattr(source_checks.subprocess, "check_output", git)
    monkeypatch.setattr(
        source_checks.importlib.util,
        "find_spec",
        lambda _name: NS(
            origin=str(
                expected
                if changed != "reader"
                else Path("/other/source/__init__.py")
            )
        ),
    )
    if changed == "none":
        source_checks.verify("a" * 40, "b" * 40)
        assert len(calls) == 3
    else:
        with pytest.raises(AssertionError):
            source_checks.verify("a" * 40, "b" * 40)
        assert calls


@pytest.mark.parametrize(
    "fault",
    [
        "none",
        "wrong-run",
        "wrong-digest",
        "expired",
        "escaping-path",
        "duplicate-id",
    ],
)
def test_immutable_artifact_edges_bind_run_hash_and_single_transfer(
    tmp_path, fault
):
    """Bind original IDs and bytes before retaining or reusing an archive."""
    from capture_artifact import retain_artifact, validate_artifacts

    content = b"original native artifact"
    content_digest = "sha256:" + hashlib.sha256(content).hexdigest()
    reference = {
        "artifact-id": 15,
        "payload-path": "wheel.whl",
        "artifact-digest": content_digest,
        "payload-digest": content_digest,
    }
    artifact = {
        "id": 15,
        "name": "wheel.whl",
        "expired": False,
        "workflow_run": {"id": 123, "head_sha": "a" * 40},
        "digest": content_digest,
    }
    refs = {"wheel": reference}
    metadata = {"total_count": 1, "artifacts": [artifact]}
    if fault == "wrong-run":
        artifact["workflow_run"]["id"] = 124
    elif fault == "wrong-digest":
        artifact["digest"] = "sha256:" + "b" * 64
    elif fault == "expired":
        artifact["expired"] = True
    elif fault == "escaping-path":
        reference["payload-path"] = "../outside.whl"
    elif fault == "duplicate-id":
        refs["sdist"] = dict(reference, **{"payload-path": "sdist.tar.gz"})
    transfers = []

    def transfer(*args):
        transfers.append(args)
        return content

    operator = NS(transfer=transfer)
    if fault != "none":
        with pytest.raises(AssertionError):
            validate_artifacts(refs, metadata, 123, "a" * 40)
        assert transfers == []
        assert list(tmp_path.iterdir()) == []
    else:
        validated = validate_artifacts(refs, metadata, 123, "a" * 40)
        retain_artifact(operator, tmp_path, "wheel", reference, validated[15])
        retain_artifact(
            operator, tmp_path, "wheel", reference, validated[15], previous=True
        )
        assert len(transfers) == 1
        assert (tmp_path / "wheel.whl").read_bytes() == content
        assert transfers[0][1].endswith("/actions/artifacts/15/zip")
