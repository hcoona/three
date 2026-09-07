"""Scripted SYNTHETIC gh and state only: no native calls or native proof."""

# ruff: noqa: D103, PLR2004

from __future__ import annotations

import hashlib
import json
import runpy
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import (
    npm_fixture,
    npm_operator,
)
from three_workflow_delivery_v3.acceptance.native_npm import (
    AcceptanceState,
    PackageControl,
    RestorabilityEvidence,
    TombstoneState,
    VersionIdentity,
)
from three_workflow_delivery_v3.acceptance.npm_capture import (
    NpmStateCapture,
    OriginalDeletionContext,
)
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    build_npm_fixture,
)
from three_workflow_delivery_v3.acceptance.npm_probe import (
    NpmProbeRequest,
    NpmProbeResult,
    parse_request,
)
from three_workflow_delivery_v3.acceptance.npm_suite import NpmSuitePlan
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.records.release import ProfileMatchEvidence
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

ROOT = Path(__file__).resolve().parents[6]
PACKAGE = "@hcoona/synthetic-local-operator"
SHA = "c" * 40
NOW = datetime(2026, 9, 7, tzinfo=UTC)
TOKEN = "SYNTHETIC_LOCAL_READ_CREDENTIAL"  # noqa: S105
REPO_API = "/repos/hcoona/three"
WORKFLOW = "workflow-delivery-v3-native-npm-acceptance.yml"
WORKFLOW_API = f"{REPO_API}/actions/workflows/{WORKFLOW}"
CONTROL = PackageControl(
    700,
    PACKAGE,
    "hcoona",
    "public",
    "hcoona/three",
    (),
)
PRECONDITIONS = DisposablePackagePreconditions(
    PACKAGE,
    preexisting_container=True,
    operator_controlled=True,
    production_dependency=False,
)
PLAN = NpmSuitePlan(
    *(
        NpmProbeRequest(
            NpmFixtureSpec(
                PACKAGE,
                f"0.0.0-native.synthetic.{suffix}",
                target * 40,
                "synthetic",
            ),
            PRECONDITIONS,
        )
        for suffix, target in zip("awvd", "abbd", strict=True)
    )
)
RUN_IDS = (91817, 20003, 77447, 40307, 33289, 85009, 66293, 50923)


def _success(output=b""):
    if not isinstance(output, bytes):
        output = json.dumps(output, indent=2).encode()
    return NpmProcessOutcome("definitive-success", output, returncode=0)


@pytest.fixture(scope="module")
def fixtures():
    return {
        (request.fixture.version, variant): build_npm_fixture(
            replace(request.fixture, variant=variant),
            repository_root=ROOT,
        )
        for request in PLAN.requests
        for variant in ("original", "different")
    }


class ScriptedGh:
    """One deterministic service script, asserting every IO boundary."""

    def __init__(self, fixtures, audit):
        """Seed synthetic service state and command observations."""
        self.fixtures = fixtures
        self.audit = audit
        self.calls = []
        self.active = {}
        self.tags = {}
        self.deleted = {}
        self.runs = {}
        self.latest = 0
        self.fault = ""
        self.main_reads = 0
        self.capture_reads = 0
        self.restores = 0
        self.delete_attempts = 0

    def run(self, argv, *, cwd, environment, timeout, output_limit):  # noqa: PLR0911
        """Never delegate any unknown argv to a real process."""
        self.calls.append(argv)
        assert cwd.name == "checkout"
        assert environment["GH_TOKEN"] == TOKEN
        assert environment["GITHUB_TOKEN"] == "EXISTING_UNCHANGED"  # noqa: S105
        assert environment["GH_PROMPT_DISABLED"] == "1"
        assert environment["SSL_CERT_FILE"] == "/synthetic/trusted-ca.pem"
        assert environment["SSL_CERT_DIR"] == "/synthetic/trusted-certs"
        assert "UNRELATED_APPLICATION_SECRET" not in environment
        assert TOKEN not in repr(argv)
        assert timeout > 0
        assert output_limit > 0
        if argv == ("git", "rev-parse", "--verify", "HEAD"):
            return _success(
                (("f" * 40 if self.fault == "head" else SHA) + "\n").encode()
            )
        if argv == ("git", "status", "--porcelain=v1", "--untracked-files=all"):
            return _success(
                b"?? unexpected\n" if self.fault == "dirty" else b""
            )
        if argv == ("gh", "auth", "token", "--hostname", "github.com"):
            if self.fault == "token":
                return NpmProcessOutcome(
                    "definitive-non-success", TOKEN.encode(), returncode=1
                )
            return _success((TOKEN + "\n").encode())
        if argv[:3] == ("gh", "run", "watch"):
            assert argv == (
                "gh",
                "run",
                "watch",
                str(self.latest),
                "--repo",
                "hcoona/three",
                "--interval",
                "15",
                "--compact",
            )
            assert timeout == 1200
            if self.fault == "watch":
                return NpmProcessOutcome("ambiguous")
            return NpmProcessOutcome(
                "definitive-success",
                b"ignored progress",
                truncated=True,
                returncode=0,
            )
        if argv[:3] == ("gh", "run", "download"):
            name = f"wdv3-native-npm-probe-{self.latest}"
            assert argv == (
                "gh",
                "run",
                "download",
                str(self.latest),
                "--repo",
                "hcoona/three",
                "--name",
                name,
                "--dir",
                argv[-1],
            )
            bundle = Path(argv[-1])
            assert bundle.is_dir()
            assert not list(bundle.iterdir())
            assert bundle.parent.name.startswith("probe-")
            self._bundle(bundle)
            return _success()
        assert argv[:6] == (
            "gh",
            "api",
            "--hostname",
            "github.com",
            "--method",
            argv[5],
        )
        assert argv[6:10] == (
            "-H",
            "Accept: application/vnd.github+json",
            "-H",
            "X-GitHub-Api-Version: 2026-03-10",
        )
        return self._api(argv[5], argv[10], argv[11:])

    def _api(self, method, route, options):  # noqa: C901, PLR0911, PLR0912, PLR0915
        if route == "/user":
            assert method == "GET"
            assert not options
            return _success(
                {"id": 1 if self.fault == "user" else 712433, "login": "hcoona"}
            )
        if route == f"{REPO_API}/git/ref/heads/main":
            assert method == "GET"
            self.main_reads += 1
            moved = self.fault == "main" or (
                self.fault == "main-moves" and self.main_reads == 3
            )
            return _success(
                {
                    "ref": "refs/heads/main",
                    "object": {"sha": "f" * 40 if moved else SHA},
                }
            )
        if route == WORKFLOW_API:
            assert method == "GET"
            assert not options
            return _success(
                {
                    "path": ".github/workflows/" + WORKFLOW,
                    "state": "disabled_manually"
                    if self.fault == "workflow"
                    else "active",
                }
            )
        if route == WORKFLOW_API + "/dispatches":
            assert method == "POST"
            assert options[0] == "--input"
            payload = Path(options[1])
            document = json.loads(payload.read_bytes())
            assert canonicalize(document) == payload.read_bytes()
            assert set(document) == {"ref", "return_run_details", "inputs"}
            assert document["ref"] == "main"
            assert document["return_run_details"] is True
            assert document["inputs"]["authorized_disposable"] is True
            request_bytes = document["inputs"]["request_json"].encode()
            assert (
                payload.parent / "request.json"
            ).read_bytes() == request_bytes
            requested = parse_request(request_bytes)
            self.latest = RUN_IDS[len(self.runs)]
            version = requested.fixture.version
            duplicate = version in self.active or version in self.deleted
            if not duplicate:
                self.active[version] = self.fixtures[
                    version, "original"
                ].content
                self.tags["buddy-sha-" + requested.fixture.target] = version
            self.runs[self.latest] = (requested, duplicate)
            if self.fault == "dispatch-truncated":
                return NpmProcessOutcome(
                    "definitive-success",
                    b'{"workflow_run_id":9',
                    truncated=True,
                    returncode=0,
                )
            if self.fault == "dispatch-empty":
                return _success()
            if self.fault == "dispatch-ambiguous":
                return NpmProcessOutcome("ambiguous", TOKEN.encode())
            if self.fault == "dispatch-id":
                return _success({"workflow_run_id": True})
            return _success(
                {
                    "workflow_run_id": self.latest,
                    "run_url": f"https://api.github.com{REPO_API}/actions/runs/{self.latest}",
                    "html_url": (
                        "https://github.com/hcoona/three/actions/runs/1"
                        if self.fault == "dispatch-url"
                        else f"https://github.com/hcoona/three/actions/runs/{self.latest}"
                    ),
                }
            )
        if (
            method == "GET"
            and route == f"{REPO_API}/actions/runs/{self.latest}"
        ):
            assert not options
            return _success(self._run_metadata())
        if route == "/users/hcoona/packages/npm/synthetic-local-operator":
            assert method == "GET"
            return _success({"SYNTHETIC": "capture-adapter-read"})
        if (
            route
            == f"{REPO_API}/actions/runs/{self.latest}/artifacts?per_page=100"
        ):
            assert method == "GET"
            assert options == ("--paginate", "--slurp")
            artifact = self._artifact()
            selected = [] if self.fault == "artifact-missing" else [artifact]
            if self.fault == "artifact-duplicate":
                selected *= 2
            return _success(
                [
                    {"artifacts": [{"id": 999, "name": "unrelated"}]},
                    {"artifacts": selected},
                ]
            )
        if route == f"{REPO_API}/actions/artifacts/{self.latest + 100000}":
            assert method == "GET"
            assert not options
            return _success(self._artifact())
        d_version = PLAN.deleted_original.fixture.version
        d_id = 404
        exact = (
            "/users/hcoona/packages/npm/synthetic-local-operator"
            f"/versions/{d_id}"
        )
        if method == "DELETE":
            assert route == exact
            assert not options
            self.delete_attempts += 1
            context = json.loads(
                (self.audit / "delete-d/original-context.json").read_bytes()
            )
            assert context["original_control"] == CONTROL.to_document()
            assert context["original_version"] == {
                "version_id": d_id,
                "name": d_version,
            }
            assert context["deletion_lower_bound_at"] == NOW.isoformat()
            self.deleted[d_version] = self.active.pop(d_version)
            self.tags.pop("buddy-sha-" + PLAN.deleted_original.fixture.target)
            if self.fault == "delete-nonzero":
                return NpmProcessOutcome(
                    "definitive-non-success", TOKEN.encode(), returncode=1
                )
            if self.fault == "delete-timeout":
                return NpmProcessOutcome("ambiguous", TOKEN.encode())
            return _success(
                b"unexpected" if self.fault == "delete-body" else b""
            )
        if method == "POST":
            assert route == exact + "/restore"
            assert not options
            self.restores += 1
            assert (
                self.audit / "restore-d/original-context.json"
            ).read_bytes() == (
                self.audit / "delete-d/original-context.json"
            ).read_bytes()
            if self.fault == "restore-nonzero":
                return NpmProcessOutcome(
                    "definitive-non-success", TOKEN.encode(), returncode=1
                )
            self.active[d_version] = self.deleted.pop(d_version)
            if self.fault == "restore-body":
                return _success(TOKEN.encode())
            return _success()
        pytest.fail(f"unrecognized synthetic command: {method} {route}")

    def _artifact(self):
        return {
            "id": self.latest + 100000,
            "name": f"wdv3-native-npm-probe-{self.latest}",
            "expired": self.fault == "artifact-expired",
            "digest": "sha256:" + "d" * 64,
            "url": (
                f"https://api.github.com{REPO_API}/actions/artifacts/"
                f"{self.latest + 100000}"
            ),
            "workflow_run": {"id": self.latest, "head_sha": SHA},
        }

    def _run_metadata(self):
        return {
            "id": self.latest,
            "run_attempt": 1,
            "head_sha": "f" * 40 if self.fault == "run-binding" else SHA,
            "head_branch": "main",
            "path": ".github/workflows/" + WORKFLOW,
            "event": "workflow_dispatch",
            "status": "completed",
            "conclusion": "failure" if self.runs[self.latest][1] else "success",
            "actor": {"id": 712433},
            "repository": {"full_name": "hcoona/three"},
        }

    def _bundle(self, bundle):
        requested, duplicate = self.runs[self.latest]
        profile = github_packages_destination_operation_profile()
        fixture = self.fixtures[
            requested.fixture.version, requested.fixture.variant
        ]
        tag = "buddy-sha-" + requested.fixture.target
        match = ProfileMatchEvidence(
            profile.profile_digest,
            profile.node_version,
            profile.npm_version,
            tuple(
                {
                    "{tarball-path}": (
                        f"/runner/wdv3-native-npm-{self.latest}/runtime/"
                        "fixture.tgz"
                    ),
                    "{tag}": tag,
                }.get(word, word)
                for word in profile.command_template
            ),
            tuple(
                sorted(
                    {
                        "@hcoona:registry": profile.registry,
                        "registry": profile.registry + "/",
                        "tag": tag,
                        "ignore-scripts": "true",
                        "fetch-retries": "0",
                        "access": "null",
                    }.items()
                )
            ),
            "2026-09-07T00:00:00Z",
        )
        result = NpmProbeResult(
            canonical_sha256(requested.to_document()),
            match.match_digest,
            fixture.content,
            "definitive-non-success" if duplicate else "definitive-success",
            1 if duplicate else 0,
            truncated=False,
        )
        evidence = bundle / "evidence"
        evidence.mkdir()
        (bundle / "platform.json").write_bytes(
            canonicalize(
                {
                    "schema": (
                        "workflow-delivery-v3/native-npm-actions-context/v1"
                    ),
                    "run_id": str(self.latest),
                    "run_attempt": "1",
                    "sha": SHA,
                    "ref": "refs/heads/main",
                    "actor_id": "712433",
                    "repository": "hcoona/three",
                    "event_name": "workflow_dispatch",
                    "workflow_ref": (
                        f"hcoona/three/.github/workflows/{WORKFLOW}"
                        "@refs/heads/main"
                    ),
                }
            )
        )
        for name, body in {
            "request.json": canonicalize(requested.to_document()),
            "fixture.tgz": fixture.tarball,
            "profile-match.json": canonicalize(match.to_document()),
            "command-started": b"",
            "result.json": canonicalize(result.to_document()),
        }.items():
            (evidence / name).write_bytes(body)

    def capture(self, **kwargs):
        """Synthetic state seam; actual collector is separately tested."""
        assert (
            kwargs["approved_disposable_package_preconditions"] == PRECONDITIONS
        )
        assert kwargs["scenarios"] == tuple(
            item.fixture for item in PLAN.requests
        )
        assert kwargs["token"] == TOKEN
        assert kwargs["repository_root"].name == "checkout"
        raw = kwargs["gh_runner"].run(
            (
                "gh",
                "api",
                "--hostname",
                "github.com",
                "--method",
                "GET",
                "-H",
                "Accept: application/vnd.github+json",
                "-H",
                "X-GitHub-Api-Version: 2026-03-10",
                "/users/hcoona/packages/npm/synthetic-local-operator",
            ),
            max_bytes=512,
        )
        assert json.loads(raw) == {"SYNTHETIC": "capture-adapter-read"}
        self.capture_reads += 1
        context = kwargs["original_deletion"]
        identities = {
            request.fixture.version: VersionIdentity(
                index * 101, request.fixture.version
            )
            for index, request in enumerate(PLAN.requests, 1)
        }
        tombstone = None
        if context is not None:
            assert (
                context.original_version
                == identities[PLAN.deleted_original.fixture.version]
            )
            tombstone = TombstoneState(
                tuple(identities[name] for name in sorted(self.deleted)),
                context.original_version,
                RestorabilityEvidence(
                    CONTROL,
                    context.original_version,
                    context.deletion_lower_bound_at,
                    NOW,
                )
                if self.deleted
                else None,
            )
        state = AcceptanceState(
            CONTROL,
            tuple(sorted(self.active)),
            tuple(sorted(self.tags.items())),
            tuple(self.active[name] for name in sorted(self.active)),
            tombstone,
        )
        directory = kwargs["audit_directory"]
        directory.mkdir(mode=0o700)
        (directory / "SYNTHETIC-state.json").write_bytes(
            canonicalize(state.to_document())
        )
        if self.fault == "deleted-capture" and context is not None:
            message = "synthetic post-delete capture failed"
            raise ValueError(message)
        return NpmStateCapture(
            state,
            NOW,
            context,
            (),
            tuple(identities[name] for name in sorted(self.active)),
        )


@pytest.fixture
def case(tmp_path, monkeypatch, fixtures):
    checkout = tmp_path / "checkout"
    checkout.mkdir()
    audit = tmp_path / "audit"
    script = ScriptedGh(fixtures, audit)
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.setenv("GH_TOKEN", TOKEN)
    monkeypatch.setenv("GITHUB_TOKEN", "EXISTING_UNCHANGED")
    monkeypatch.setenv("SSL_CERT_FILE", "/synthetic/trusted-ca.pem")
    monkeypatch.setenv("SSL_CERT_DIR", "/synthetic/trusted-certs")
    monkeypatch.setenv("UNRELATED_APPLICATION_SECRET", "must not inherit")
    monkeypatch.setattr(npm_operator, "capture_npm_state", script.capture)
    monkeypatch.setattr(
        npm_operator,
        "_validate_npm_coordinates",
        lambda spec, _: npm_fixture._validate_npm_coordinates(spec, ROOT),  # noqa: SLF001
    )
    real_reader = npm_operator.read_npm_evidence

    def read_evidence(bundle, **kwargs):
        assert kwargs["repository_root"] == checkout
        kwargs["repository_root"] = ROOT
        return real_reader(bundle, **kwargs)

    monkeypatch.setattr(npm_operator, "read_npm_evidence", read_evidence)
    return {
        "plan": PLAN,
        "expected_tooling_sha": SHA,
        "repository_root": checkout,
        "audit_directory": audit,
        "authorized_disposable": True,
        "authorized_delete_restore": True,
        "runner": script,
        "clock": lambda: NOW,
    }


def test_complete_synthetic_backend_uses_exact_runs_and_retains_bound_manifest(
    case, capsys
):
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    path, digest = operator.execute()
    script = case["runner"]
    manifest = json.loads(path.read_bytes())
    assert canonicalize(manifest) == path.read_bytes()
    assert digest == "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
    assert manifest["schema"] == (
        "workflow-delivery-v3/native-npm-suite-evidence/v1"
    )
    assert manifest["scenario_verdict"] == "passed"
    profile = github_packages_destination_operation_profile()
    assert manifest["destination_operation_profile_id"] == profile.profile_id
    assert (
        manifest["destination_operation_profile_digest"]
        == profile.profile_digest
    )
    assert (
        manifest["native_acceptance_suite_version"]
        == npm_operator.NPM_SUITE_VERSION
    )
    assert manifest["github_api_version"] == "2026-03-10"
    assert (
        manifest["lower_layer_contract_revision"]
        == npm_operator.LOWER_LAYER_CONTRACT_REVISION
        == "wdv3/github-packages-npm-documented-contract/v1"
    )
    assert (
        "https://docs.github.com/en/rest/packages/packages"
        "#restore-package-version-for-a-user"
        in manifest["lower_layer_contract_sources"]
    )
    assert (
        manifest["disposable_package_preconditions"]
        == PRECONDITIONS.to_document()
    )
    assert manifest["generation"] == "synthetic"
    assert manifest["tooling_sha"] == SHA
    assert manifest["captured_at"] == NOW.isoformat()
    assert manifest["original_restoration_verified"] is True
    assert [probe["run_id"] for probe in manifest["probes"]] == list(RUN_IDS)
    assert [probe["classification"] for probe in manifest["probes"]] == [
        "definitive-success",
        "definitive-non-success",
        "definitive-non-success",
        "definitive-success",
        "definitive-success",
        "definitive-success",
        "definitive-non-success",
        "definitive-non-success",
    ]
    assert script.capture_reads == 11
    assert script.delete_attempts == script.restores == 1
    assert script.main_reads == 9
    assert not script.deleted
    assert len(script.active) == 4
    assert case["audit_directory"].stat().st_mode & 0o777 == 0o700
    retained = {item["filename"] for item in manifest["files"]}
    actual = {
        item.relative_to(path.parent).as_posix()
        for item in path.parent.rglob("*")
        if item.is_file() and item != path
    }
    assert retained == actual
    for item in manifest["files"]:
        body = (path.parent / item["filename"]).read_bytes()
        assert item["sha256"] == "sha256:" + hashlib.sha256(body).hexdigest()
    for item in path.parent.rglob("*"):
        if item.is_file():
            body = item.read_bytes()
            assert TOKEN.encode() not in body
            assert b"EXISTING_UNCHANGED" not in body
    assert "candidate only" in manifest["admission"]
    assert capsys.readouterr().out == ""


@pytest.mark.parametrize("token_name", ["GH_TOKEN", "GITHUB_TOKEN"])
def test_successful_json_with_local_secret_is_rejected_without_retention(
    case, monkeypatch, token_name
):
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    secret = operator.environment[token_name]
    monkeypatch.setattr(
        operator.runner,
        "run",
        lambda *_args, **_kwargs: _success(
            {"id": 712433, "login": "hcoona", "unexpected": secret}
        ),
    )

    with pytest.raises(ValueError, match="operator command failed"):
        operator.command(
            case["audit_directory"],
            "secret-response",
            ("gh", "api", "/user"),
        )

    facts = json.loads(
        (case["audit_directory"] / "secret-response.process.json").read_bytes()
    )
    assert facts["classification"] == "definitive-success"
    assert facts["output_retained"] is False
    assert not (case["audit_directory"] / "secret-response.raw").exists()
    for path in case["audit_directory"].rglob("*"):
        if path.is_file():
            body = path.read_bytes()
            assert TOKEN.encode() not in body
            assert b"EXISTING_UNCHANGED" not in body


@pytest.mark.parametrize(
    "flag", ["authorized_disposable", "authorized_delete_restore"]
)
def test_missing_authorization_stops_before_any_calls_or_files(case, flag):
    case[flag] = False
    with pytest.raises(ValueError, match="explicit prior"):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert case["runner"].calls == []
    assert not case["audit_directory"].exists()


def test_actions_context_stops_before_any_calls(case, monkeypatch):
    monkeypatch.setenv("GITHUB_ACTIONS", "true")
    with pytest.raises(ValueError, match="operator-local"):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert case["runner"].calls == []
    assert not case["audit_directory"].exists()


@pytest.mark.parametrize(
    ("fault", "error"),
    [
        ("user", "authenticated gh user"),
        ("head", "local HEAD"),
        ("dirty", "must be clean"),
        ("main", "protected main moved"),
        ("workflow", "registered native workflow"),
    ],
)
def test_failed_preflight_never_dispatches_or_reads_credentials(
    case, fault, error
):
    case["runner"].fault = fault
    with pytest.raises(ValueError, match=error):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert not case["runner"].runs
    assert case["runner"].delete_attempts == case["runner"].restores == 0
    assert not any(
        call[:3] == ("gh", "auth", "token") for call in case["runner"].calls
    )


def test_main_moving_between_dispatches_stops_without_second_mutation(case):
    case["runner"].fault = "main-moves"
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    with pytest.raises(ValueError, match="protected main moved"):
        operator.execute()
    assert list(case["runner"].runs) == [RUN_IDS[0]]
    assert case["runner"].restores == 0
    assert not (case["audit_directory"] / "suite-evidence.json").exists()


@pytest.mark.parametrize(
    "fault",
    [
        "dispatch-empty",
        "dispatch-id",
        "dispatch-url",
        "dispatch-ambiguous",
        "dispatch-truncated",
        "artifact-missing",
        "artifact-duplicate",
        "artifact-expired",
    ],
)
def test_ambiguous_dispatch_or_artifact_never_scans_history_or_retries(
    case, fault
):
    case["runner"].fault = fault
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    with pytest.raises(
        ValueError,
        match=(
            r"Expecting value|expected positive exact ID|"
            r"ambiguous exact run URLs|operator command failed|"
            r"exactly one nonexpired"
        ),
    ):
        operator.execute()
    assert list(case["runner"].runs) == [RUN_IDS[0]]
    assert case["runner"].restores == 0
    assert not (case["audit_directory"] / "suite-evidence.json").exists()
    assert not any(
        call[:3] == ("gh", "run", "download") for call in case["runner"].calls
    )
    for path in case["audit_directory"].rglob("*"):
        if path.is_file():
            assert TOKEN.encode() not in path.read_bytes()


@pytest.mark.parametrize(
    "fault",
    [
        "delete-nonzero",
        "delete-timeout",
        "delete-body",
        "deleted-capture",
        "restore-nonzero",
        "restore-body",
    ],
)
def test_admin_failure_preserves_context_without_repair_or_completed_manifest(
    case, fault
):
    case["runner"].fault = fault
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    with pytest.raises(
        ValueError,
        match=(
            r"operator command failed|unexpected mutation response body|"
            r"synthetic post-delete capture failed"
        ),
    ):
        operator.execute()
    script = case["runner"]
    assert script.delete_attempts == 1
    assert script.restores == (1 if fault.startswith("restore-") else 0)
    assert (case["audit_directory"] / "delete-d/original-context.json").exists()
    assert not (case["audit_directory"] / "suite-evidence.json").exists()
    for path in case["audit_directory"].rglob("*"):
        if path.is_file():
            assert TOKEN.encode() not in path.read_bytes()


@pytest.mark.parametrize(
    ("fault", "error", "dispatches"),
    [
        ("token", "local gh read credential unavailable", 0),
        ("watch", "operator command failed", 1),
        ("run-binding", "run.head_sha mismatch", 1),
    ],
)
def test_credential_wait_or_evidence_failure_stops_without_completion(
    case,
    fault,
    error,
    dispatches,
):
    case["runner"].fault = fault
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    with pytest.raises(ValueError, match=error):
        operator.execute()
    assert len(case["runner"].runs) == dispatches
    assert case["runner"].restores == case["runner"].delete_attempts == 0
    assert not (case["audit_directory"] / "suite-evidence.json").exists()
    for path in case["audit_directory"].rglob("*"):
        if path.is_file():
            assert TOKEN.encode() not in path.read_bytes()


def test_delete_rejects_wrong_identity_and_restore_rejects_foreign_context(
    case,
):
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    operator.capture("initial", plan=PLAN)
    with pytest.raises(ValueError, match="captured original"):
        operator.delete_exact(
            CONTROL, VersionIdentity(404, PLAN.deleted_original.fixture.version)
        )
    with pytest.raises(ValueError, match="original deletion context"):
        operator.restore_exact(
            OriginalDeletionContext(
                CONTROL,
                VersionIdentity(404, PLAN.deleted_original.fixture.version),
                NOW,
            )
        )
    assert case["runner"].delete_attempts == case["runner"].restores == 0


def test_fresh_audit_and_operation_names_prevent_reinvocation(case):
    operator = npm_operator.OperatorLocalNpmOperations(**case)
    operator.capture("initial", plan=PLAN)
    count = len(case["runner"].calls)
    with pytest.raises(FileExistsError):
        operator.capture("initial", plan=PLAN)
    with pytest.raises(FileExistsError):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert len(case["runner"].calls) == count


def test_audit_inside_checkout_is_rejected_before_commands(case):
    case["audit_directory"] = case["repository_root"] / "audit"
    with pytest.raises(ValueError, match="outside"):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert case["runner"].calls == []


@pytest.mark.parametrize("generation", ["bad_name", "01", "bad..name"])
def test_generation_requires_official_semver_without_sanitizing(
    case, generation
):
    case["plan"] = NpmSuitePlan(
        *(
            replace(
                item,
                fixture=replace(
                    item.fixture,
                    generation=generation,
                    version=f"0.0.0-native.{generation}.{suffix}",
                ),
            )
            for item, suffix in zip(PLAN.requests, "awvd", strict=True)
        )
    )
    with pytest.raises(ValueError, match="official npm"):
        npm_operator.OperatorLocalNpmOperations(**case)
    assert not case["runner"].runs
    assert not any(call[0] == "gh" for call in case["runner"].calls)


def test_probe_and_suite_help_preserve_authorization_boundary(
    monkeypatch, capsys
):
    monkeypatch.setattr("sys.argv", ["acceptance", "probe", "--help"])
    with pytest.raises(SystemExit) as result:
        runpy.run_module(
            "three_workflow_delivery_v3.acceptance", run_name="__main__"
        )
    assert result.value.code == 0
    assert "--request" in capsys.readouterr().out
    monkeypatch.setattr("sys.argv", ["acceptance", "suite", "--help"])
    with pytest.raises(SystemExit) as result:
        runpy.run_module(
            "three_workflow_delivery_v3.acceptance", run_name="__main__"
        )
    assert result.value.code == 0
    help_text = capsys.readouterr().out
    assert "--authorized-disposable" in help_text
    assert "--authorized-delete-restore" in help_text
    assert "flags do not" in help_text
    assert "grant approval" in help_text
    assert "production dependency" in help_text


def test_cli_has_no_package_generation_or_target_defaults(case, capsys):
    with pytest.raises(SystemExit) as result:
        npm_operator.main(
            [
                "suite",
                "--authorized-disposable",
                "--authorized-delete-restore",
            ]
        )
    assert result.value.code == 2
    assert not case["runner"].calls
    output = capsys.readouterr()
    assert output.out == ""
    assert "--package" in output.err
    assert "--generation" in output.err
    assert "--creation-target" in output.err


def test_cli_explicit_plan_and_success_output_only(case, monkeypatch, capsys):
    script = case["runner"]
    monkeypatch.setattr(
        npm_operator, "IsolatedNpmProcessRunner", lambda: script
    )
    arguments = [
        "suite",
        "--package",
        PACKAGE,
        "--generation",
        "synthetic",
        "--tooling-sha",
        SHA,
        "--creation-target",
        "a" * 40,
        "--race-target",
        "b" * 40,
        "--deleted-target",
        "d" * 40,
        "--repository-root",
        str(case["repository_root"]),
        "--audit-directory",
        str(case["audit_directory"]),
    ]
    with pytest.raises(ValueError, match="explicit prior"):
        npm_operator.main(arguments)
    assert not script.calls
    assert capsys.readouterr().out == ""
    operator_class = npm_operator.OperatorLocalNpmOperations
    monkeypatch.setattr(
        npm_operator,
        "OperatorLocalNpmOperations",
        lambda **kwargs: operator_class(**kwargs, clock=lambda: NOW),
    )
    arguments.extend(["--authorized-disposable", "--authorized-delete-restore"])
    assert npm_operator.main(arguments) == 0
    output = json.loads(capsys.readouterr().out)
    path = Path(output["path"])
    assert path.name == "suite-evidence.json"
    assert path.is_file()
    assert (
        output["sha256"]
        == "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
    )
