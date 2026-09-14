"""One-use dispatch and exact-run collection for the fixed NuGet sequence."""

from __future__ import annotations

import re
import time
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance import nuget_operator as process
from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    _object,
    _require,
)
from three_workflow_delivery_v3.acceptance.nuget_probe_evidence import (
    NuGetProbeEvidence,
    _matches,
    read_probe_evidence,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.acceptance.nuget_github import (
        NuGetGitHubClient,
    )
    from three_workflow_delivery_v3.acceptance.nuget_suite import NuGetSuitePlan

_REPO = "/repos/hcoona/three"
_WORKFLOW = (
    _REPO + "/actions/workflows/" + probe.WORKFLOW_PATH.rsplit("/", 1)[1]
)


class NuGetProbeCollector:
    """Spend each of three positions before dispatch, with no recovery path."""

    def __init__(
        self, plan: NuGetSuitePlan, client: NuGetGitHubClient, directory: Path
    ) -> None:
        """Bind inputs; the containing operator admits native authority."""
        self.plan, self.client, self.directory = plan, client, directory
        directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.next_index = 0
        self.failed = False
        self.runs: set[int] = set()

    def collect(self, spec: bytes, *, deadline: float) -> NuGetProbeEvidence:
        """Retain original dispatch/run/artifact bytes and partial failures."""
        _require(not self.failed, "probe generation already failed")
        _require(
            self.next_index < len(probe.SCENARIOS),
            "probe dispatch allowance exhausted",
        )
        scenario = probe.SCENARIOS[self.next_index]
        supplied = _object(parse_canonical_json(spec))
        before = supplied.get("beforeCaptureSha256")
        expected = _object(parse_canonical_json(self.plan.probe_inputs))
        expected.update(
            schema=probe.SPEC_SCHEMA,
            scenario=scenario,
            beforeCaptureSha256=before,
        )
        _require(
            type(before) is str
            and re.fullmatch(r"[0-9a-f]{64}", before) is not None
            and canonicalize(expected) == spec,
            "probe spec changed from its fixed position or static plan",
        )
        self.next_index += 1
        directory = self.directory / scenario
        directory.mkdir(mode=0o700)
        evidence = process._Evidence(directory, self.client.token)  # noqa: SLF001
        evidence.write("spec.json", spec)
        dispatch = canonicalize(
            {
                "ref": "main",
                "return_run_details": True,
                "inputs": {"probe_spec": spec.decode()},
            }
        )
        evidence.write("dispatch-request.json", dispatch)
        # The position is never refunded, including a failed freshness check.
        evidence.write(
            "reserved.json",
            canonicalize(
                {
                    "scenario": scenario,
                    "maximumDispatches": 1,
                    "maximumPublicationInvocations": 1,
                    "retries": 0,
                    "reruns": 0,
                }
            ),
        )
        try:
            self._fresh_control(deadline)
            raw = self.client.request(
                _WORKFLOW + "/dispatches", deadline=deadline, body=dispatch
            )
            evidence.write("dispatch-response.json", raw)
            result = _object(parse_json_strict(raw))
            run_id = result.get("workflow_run_id")
            _require(
                type(run_id) is int and run_id > 0 and run_id not in self.runs,
                "missing or reused dispatch run",
            )
            run_id = cast("int", run_id)
            self.runs.add(run_id)
            route = f"{_REPO}/actions/runs/{run_id}"
            _matches(
                result,
                {
                    "run_url": "https://api.github.com" + route,
                    "html_url": f"https://github.com/hcoona/three/actions/runs/{run_id}",
                },
            )
            current = dict(supplied)
            current.update(schema=probe.REQUEST_SCHEMA, workflowRunId=run_id)
            request = probe.read_probe_request(canonicalize(current))
            evidence.write("request.json", canonicalize(request.to_document()))
            raw_run = self._completed_run(route, run_id, deadline)
            evidence.write("run.json", raw_run)
            raw_artifacts = self.client.request(
                route + "/artifacts?per_page=100", deadline=deadline
            )
            evidence.write("artifacts.json", raw_artifacts)
            self._artifacts(directory, raw_artifacts, request, deadline)
            return read_probe_evidence(
                directory,
                request,
                raw_run_metadata=raw_run,
                raw_artifact_metadata=raw_artifacts,
                maximum_artifact_bytes=self.client.limits.artifact_bytes,
            )
        except BaseException as error:
            self.failed = True
            evidence.write(
                "probe-failed.json",
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "generationSpent": True,
                        "remoteCompletionUnknown": True,
                        "nativeAdmissionEstablished": False,
                    }
                ),
            )
            raise

    def _fresh_control(self, deadline: float) -> None:
        # Actual protection configuration and its independent admission remain
        # part of the caller's concrete request. This checks current freshness.
        user = _object(
            parse_json_strict(self.client.request("/user", deadline=deadline))
        )
        _matches(user, {"id": 712433, "login": "hcoona"})
        workflow = _object(
            parse_json_strict(self.client.request(_WORKFLOW, deadline=deadline))
        )
        _matches(workflow, {"path": probe.WORKFLOW_PATH, "state": "active"})
        branch = _object(
            parse_json_strict(
                self.client.request(_REPO + "/branches/main", deadline=deadline)
            )
        )
        _matches(branch, {"name": "main", "protected": True})
        _matches(
            _object(branch.get("commit")),
            {"sha": self.plan.consumer.tooling_sha},
        )

    def _completed_run(self, route: str, run_id: int, deadline: float) -> bytes:
        for index in range(self.client.limits.polls_per_probe):
            raw = self.client.request(route, deadline=deadline)
            run = _object(parse_json_strict(raw))
            _matches(
                run,
                {
                    "id": run_id,
                    "run_attempt": 1,
                    "head_sha": self.plan.consumer.tooling_sha,
                    "head_branch": "main",
                    "event": "workflow_dispatch",
                    "path": probe.WORKFLOW_PATH,
                },
            )
            for field in ("repository", "head_repository"):
                _matches(
                    _object(run.get(field)),
                    {"id": 1102295886, "full_name": "hcoona/three"},
                )
            for field in ("actor", "triggering_actor"):
                _matches(_object(run.get(field)), {"id": 712433})
            if run.get("status") == "completed":
                # Even a failed run's original metadata/artifacts are useful;
                # only the evidence reader can admit the expected outcome.
                return raw
            _require(
                run.get("status")
                in {"queued", "in_progress", "waiting", "pending", "requested"}
                and run.get("conclusion") is None,
                "unexpected current run state",
            )
            if index + 1 < self.client.limits.polls_per_probe:
                delay = self.client.limits.poll_interval_seconds
                _require(
                    time.monotonic() + delay < deadline,
                    "probe observation deadline exhausted",
                )
                time.sleep(delay)
        message = (
            "probe observation allowance exhausted; "
            "remote completion remains unknown"
        )
        raise ValueError(message)

    def _artifacts(
        self,
        directory: Path,
        raw: bytes,
        request: probe.NuGetProbeRequest,
        deadline: float,
    ) -> None:
        listing = _object(parse_json_strict(raw))
        items = listing.get("artifacts")
        _require(
            isinstance(items, list)
            and type(listing.get("total_count")) is int
            and listing["total_count"] == len(items) <= len(probe.SCENARIOS),
            "incomplete or unexpected probe artifact inventory",
        )
        assert isinstance(items, list)  # noqa: S101
        identities: set[int] = set()
        names: set[str] = set()
        for item in items:
            artifact = _object(item)
            identity, name, digest = (
                artifact.get("id"),
                artifact.get("name"),
                artifact.get("digest"),
            )
            _require(
                type(identity) is int
                and identity > 0
                and identity not in identities
                and type(name) is str
                and name not in names
                and type(digest) is str
                and re.fullmatch(r"sha256:[0-9a-f]{64}", digest) is not None,
                "invalid probe artifact identity",
            )
            identity, name, digest = (
                cast("int", identity),
                cast("str", name),
                cast("str", digest),
            )
            identities.add(identity)
            names.add(name)
            run_id = request.workflow_run_id
            _require(
                name
                in {
                    f"wdv3-nuget-probe-input-r{run_id}-{digest[7:]}.zip",
                    f"wdv3-nuget-probe-prepare-diagnostics-r{run_id}.zip",
                    f"wdv3-nuget-probe-publish-diagnostics-r{run_id}.zip",
                },
                "unexpected probe artifact name",
            )
            route = f"{_REPO}/actions/artifacts/{identity}"
            _matches(
                artifact,
                {
                    "expired": False,
                    "url": "https://api.github.com" + route,
                    "archive_download_url": "https://api.github.com"
                    + route
                    + "/zip",
                },
            )
            _matches(
                _object(artifact.get("workflow_run")),
                {
                    "id": run_id,
                    "repository_id": 1102295886,
                    "head_repository_id": 1102295886,
                    "head_branch": "main",
                    "head_sha": request.tooling_sha,
                },
            )
            body = self.client.request(
                route + "/zip", deadline=deadline, download=True
            )
            process._Evidence(directory, self.client.token).write(name, body)  # noqa: SLF001
