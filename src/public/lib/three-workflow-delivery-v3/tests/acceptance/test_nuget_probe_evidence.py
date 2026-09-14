"""Original probe lineage using controlled preparation/publication producers."""

from __future__ import annotations

# ruff: noqa: D103
import json
from dataclasses import replace
from importlib import import_module
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    _archive_files,
    write_archive,
)
from three_workflow_delivery_v3.acceptance.nuget_probe_evidence import (
    read_probe_evidence,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

_fixtures = import_module(".test_nuget_probe", __package__)
A, B = _fixtures.A, _fixtures.B
_invocation = _fixtures._invocation  # noqa: SLF001
_invoke = _fixtures._invoke  # noqa: SLF001
_platform = _fixtures._platform  # noqa: SLF001
_prepare = _fixtures._prepare  # noqa: SLF001
_reference = _fixtures._reference  # noqa: SLF001
_sha = _fixtures._sha  # noqa: SLF001
inputs = _fixtures.inputs


@pytest.fixture
def evidence(inputs):
    def prepare(scenario="create"):
        request = replace(inputs.request, scenario=scenario)
        inputs.request = request
        prepared, _ = _prepare(inputs)
        directory = inputs.root / "original-uploads"
        directory.mkdir()
        path = (
            directory
            / f"wdv3-nuget-probe-input-r81-{_sha(prepared.read_bytes())}.zip"
        )
        path.write_bytes(prepared.read_bytes())
        reference = _reference(path, 201, 81)
        selected = B if scenario == "equivalent-duplicate" else A
        inputs.publisher.return_value = replace(
            _invocation(inputs, status=201 if scenario == "create" else 409),
            package_sha256=_sha(selected),
        )
        result = _invoke(inputs, (path, reference))
        spec = request.to_document()
        spec.pop("workflowRunId")
        spec["schema"] = probe.SPEC_SCHEMA
        preparation = directory / "wdv3-nuget-probe-prepare-diagnostics-r81.zip"
        write_archive(
            preparation,
            {
                "platform.json": json.dumps(
                    _platform(request), indent=2
                ).encode(),
                "spec.json": canonicalize(spec),
                "request.json": canonicalize(request.to_document()),
            },
        )
        publication = directory / "wdv3-nuget-probe-publish-diagnostics-r81.zip"
        files = {
            "publish/" + file.name: file.read_bytes()
            for file in result.parent.iterdir()
        }
        files.update(
            {
                "platform.json": json.dumps(
                    _platform(request), indent=2
                ).encode(),
                "reference.json": json.dumps(
                    reference.to_document(), indent=2
                ).encode(),
            }
        )
        write_archive(publication, files)
        artifacts = []
        for identity, original in enumerate(
            (path, preparation, publication), 201
        ):
            api = f"https://api.github.com/repos/hcoona/three/actions/artifacts/{identity}"
            artifacts.append(
                {
                    "id": identity,
                    "name": original.name,
                    "size_in_bytes": original.stat().st_size,
                    "digest": "sha256:" + _sha(original.read_bytes()),
                    "expired": False,
                    "url": api,
                    "archive_download_url": api + "/zip",
                    "workflow_run": {
                        "id": 81,
                        "repository_id": 1102295886,
                        "head_repository_id": 1102295886,
                        "head_branch": "main",
                        "head_sha": request.tooling_sha,
                    },
                }
            )
        run = {
            "id": 81,
            "run_attempt": 1,
            "head_sha": request.tooling_sha,
            "head_branch": "main",
            "event": "workflow_dispatch",
            "path": probe.WORKFLOW_PATH,
            "status": "completed",
            "conclusion": "success",
            "repository": {"id": 1102295886, "full_name": "hcoona/three"},
            "head_repository": {"id": 1102295886, "full_name": "hcoona/three"},
            "actor": {"id": 712433},
            "triggering_actor": {"id": 712433},
        }
        inputs.helper.reset_mock()
        inputs.publisher.reset_mock()
        return SimpleNamespace(
            directory=directory,
            request=request,
            run=run,
            listing={"total_count": 3, "artifacts": artifacts},
            selected=selected,
            producer=inputs,
        )

    return prepare


def _read(item, limit=1_000_000):
    return read_probe_evidence(
        item.directory,
        item.request,
        raw_run_metadata=json.dumps(item.run).encode(),
        raw_artifact_metadata=json.dumps(item.listing).encode(),
        maximum_artifact_bytes=limit,
    )


@pytest.mark.parametrize(
    "scenario", ["create", "identical-duplicate", "equivalent-duplicate"]
)
def test_original_probe_evidence_binds_create_and_both_duplicates(
    evidence, scenario
):
    original = evidence(scenario)
    admitted = _read(original)
    assert admitted.request == original.request
    assert admitted.status == (201 if scenario == "create" else 409)
    assert admitted.http_created is (scenario == "create")
    assert admitted.possibly_mutated is (scenario != "create")
    assert admitted.response_body == b"created"
    assert [reference.artifact_id for reference in admitted.artifacts] == [
        201,
        202,
        203,
    ]
    prepared = _archive_files(
        (original.directory / admitted.artifacts[0].payload_path).read_bytes()
    )
    assert prepared["package.nupkg"] == original.selected
    assert admitted.raw_run_sha256 == _sha(json.dumps(original.run).encode())
    assert admitted.raw_artifacts_sha256 == _sha(
        json.dumps(original.listing).encode()
    )
    assert all(
        (original.directory / reference.payload_path).is_file()
        for reference in admitted.artifacts
    )
    original.producer.helper.assert_not_called()
    assert original.producer.helper.mock_calls == []
    original.producer.publisher.assert_not_called()


def _change_envelope(item, index, operation):
    metadata = item.listing["artifacts"][index]
    path = item.directory / metadata["name"]
    files = _archive_files(path.read_bytes())
    operation(files)
    path.unlink()
    write_archive(path, files)
    metadata.update(
        size_in_bytes=path.stat().st_size,
        digest="sha256:" + _sha(path.read_bytes()),
    )


def _change_document(files, name, **updates):
    document = parse_canonical_json(files[name])
    document.update(updates)
    files[name] = canonicalize(document)


@pytest.mark.parametrize(
    "change",
    [
        "run",
        "attempt",
        "actor",
        "tooling",
        "artifact-run",
        "missing-list-entry",
        "missing-archive",
        "raw-bytes",
        "expired",
        "spec",
        "request",
        "marker",
        "reference",
        "response-body",
        "response-status",
        "ambiguous",
        "byte-bound",
    ],
)
def test_probe_evidence_rejects_lineage_or_terminal_substitution(  # noqa: C901, PLR0912
    evidence, change
):
    item = evidence()
    limit = 1_000_000
    if change in {"run", "attempt", "tooling"}:
        key, value = {
            "run": ("id", 82),
            "attempt": ("run_attempt", 2),
            "tooling": ("head_sha", "f" * 40),
        }[change]
        item.run[key] = value
    elif change == "actor":
        item.run["triggering_actor"]["id"] = 999
    elif change == "artifact-run":
        item.listing["artifacts"][0]["workflow_run"]["id"] = 82
    elif change == "missing-list-entry":
        item.listing["artifacts"].pop()
    elif change == "missing-archive":
        (item.directory / item.listing["artifacts"][2]["name"]).unlink()
    elif change == "raw-bytes":
        (item.directory / item.listing["artifacts"][2]["name"]).write_bytes(
            b"changed"
        )
    elif change == "expired":
        item.listing["artifacts"][1]["expired"] = True
    elif change == "spec":
        _change_envelope(
            item,
            1,
            lambda files: _change_document(
                files, "spec.json", preflightSha256="0" * 64
            ),
        )
    elif change == "request":
        _change_envelope(
            item,
            2,
            lambda files: _change_document(
                files, "publish/request.json", beforeCaptureSha256="0" * 64
            ),
        )
    elif change == "marker":
        _change_envelope(
            item,
            2,
            lambda files: _change_document(
                files, "publish/invocation-started.json", requestDigest="0" * 64
            ),
        )
    elif change == "reference":
        _change_envelope(
            item,
            2,
            lambda files: files.update({"reference.json": canonicalize({})}),
        )
    elif change == "response-body":
        _change_envelope(
            item,
            2,
            lambda files: files.update(
                {"publish/response.body": b"different response"}
            ),
        )
    elif change == "response-status":

        def alter(files):
            _change_document(files, "publish/response.json", status=409)
            _change_document(
                files,
                "publish/result.json",
                response=parse_canonical_json(files["publish/response.json"]),
            )

        _change_envelope(item, 2, alter)
    elif change == "ambiguous":
        _change_envelope(
            item,
            2,
            lambda files: _change_document(
                files, "publish/result.json", transportError=True
            ),
        )
    else:
        limit = 1
    with pytest.raises(ValueError, match=r"probe|publisher|NuGet"):
        _read(item, limit)
    assert item.producer.helper.mock_calls == []
    item.producer.publisher.assert_not_called()
