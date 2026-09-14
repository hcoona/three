"""Exact dispatch lineage through controlled HTTP and original uploads."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import json
import time
from importlib import import_module

import pytest
from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_dispatch import (
    NuGetProbeCollector,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

_http_fixtures = import_module(".test_nuget_github", __package__)
_response = _http_fixtures._response  # noqa: SLF001
transport = _http_fixtures.transport
inputs = import_module(".test_nuget_probe", __package__).inputs
evidence = import_module(".test_nuget_probe_evidence", __package__).evidence
suite = import_module(".test_nuget_suite", __package__).suite


def _json(document):
    return _response(json.dumps(document, indent=2).encode())


def _control(item):
    return [
        _json({"id": 712433, "login": "hcoona"}),
        _json({"path": probe.WORKFLOW_PATH, "state": "active"}),
        _json(
            {
                "name": "main",
                "protected": True,
                "commit": {"sha": item.request.tooling_sha},
            }
        ),
    ]


def _dispatch():
    return {
        "workflow_run_id": 81,
        "run_url": "https://api.github.com/repos/hcoona/three/actions/runs/81",
        "html_url": "https://github.com/hcoona/three/actions/runs/81",
    }


def _spec(item):
    document = item.request.to_document()
    document.pop("workflowRunId")
    document["schema"] = probe.SPEC_SCHEMA
    return canonicalize(document)


def _uploads(item):
    responses = []
    for entry in item.listing["artifacts"]:
        responses.extend(
            [
                _response(
                    b"",
                    302,
                    "https://artifact-storage.example/original?sig=controlled",
                ),
                _response((item.directory / entry["name"]).read_bytes()),
            ]
        )
    return responses


def test_probe_collects_only_exact_dispatch_run(
    evidence, suite, transport, tmp_path
):
    item = evidence()
    pending = dict(item.run, status="in_progress", conclusion=None)
    http = transport(
        [
            *_control(item),
            _json(_dispatch()),
            _json(pending),
            _json(item.run),
            _json(item.listing),
            *_uploads(item),
        ]
    )
    collector = NuGetProbeCollector(
        suite.plan, http.client, tmp_path / "probes"
    )
    result = collector.collect(_spec(item), deadline=time.monotonic() + 10)
    assert result.request == item.request
    assert result.http_created is True
    assert result.possibly_mutated is False
    posts = [call for call in http.calls if call[1] == "POST"]
    assert len(posts) == 1
    posted = parse_canonical_json(posts[0][3]["body"])
    assert posted == {
        "ref": "main",
        "return_run_details": True,
        "inputs": {"probe_spec": _spec(item).decode()},
    }
    assert posts[0][2].endswith(
        "/workflow-delivery-v3-native-nuget-acceptance.yml/dispatches"
    )
    polls = [
        call[2] for call in http.calls if call[2].endswith("/actions/runs/81")
    ]
    assert len(polls) == 2
    assert not http.pending
    directory = tmp_path / "probes/create"
    assert (directory / "run.json").read_bytes() == json.dumps(
        item.run, indent=2
    ).encode()
    assert (directory / "artifacts.json").read_bytes() == json.dumps(
        item.listing, indent=2
    ).encode()
    for entry in item.listing["artifacts"]:
        assert (directory / entry["name"]).read_bytes() == (
            item.directory / entry["name"]
        ).read_bytes()
    assert not (directory / "probe-failed.json").exists()


@pytest.mark.parametrize(
    "failure",
    [
        "main",
        "lost-dispatch",
        "missing-id",
        "wrong-url",
        "rerun",
        "pending",
        "failed-run",
        "partial-list",
    ],
)
def test_probe_failure_spends_slot_and_stops_replacement(
    evidence, suite, transport, tmp_path, failure
):
    item = evidence()
    controls = _control(item)
    dispatch = _dispatch()
    run = dict(item.run)
    listing = item.listing
    if failure == "main":
        controls[-1] = _json(
            {"name": "main", "protected": True, "commit": {"sha": "f" * 40}}
        )
    if failure == "missing-id":
        dispatch.pop("workflow_run_id")
    if failure == "wrong-url":
        dispatch["run_url"] += "0"
    if failure == "rerun":
        run["run_attempt"] = 2
    if failure == "pending":
        run.update(status="queued", conclusion=None)
    if failure == "failed-run":
        run["conclusion"] = "failure"
    if failure == "partial-list":
        listing = {"total_count": 4, "artifacts": item.listing["artifacts"]}
    responses = [
        *controls,
        OSError("lost") if failure == "lost-dispatch" else _json(dispatch),
        _json(run),
    ]
    if failure == "pending":
        responses.append(_json(run))
    responses.extend([_json(listing), *_uploads(item)])
    http = transport(responses)
    collector = NuGetProbeCollector(
        suite.plan, http.client, tmp_path / "probes"
    )
    with pytest.raises(
        ValueError,
        match=(
            r"mismatch|GitHub call failed|dispatch run|"
            r"observation allowance|inventory"
        ),
    ):
        collector.collect(_spec(item), deadline=time.monotonic() + 10)
    assert collector.failed
    previous = len(http.calls)
    with pytest.raises(ValueError, match="generation already failed"):
        collector.collect(_spec(item), deadline=time.monotonic() + 10)
    assert len(http.calls) == previous
    assert len([call for call in http.calls if call[1] == "POST"]) == (
        0 if failure == "main" else 1
    )
    record = parse_canonical_json(
        (tmp_path / "probes/create/probe-failed.json").read_bytes()
    )
    assert record["generationSpent"] is True
    assert record["nativeAdmissionEstablished"] is False
    if failure == "failed-run":
        assert (tmp_path / "probes/create/run.json").is_file()
        assert len(list((tmp_path / "probes/create").glob("*.zip"))) == 3
