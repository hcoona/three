"""Concrete operation DAG, permission separation and finite action counts."""

import re
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[5]
RECORD = "./.github/actions/workflow-delivery-v3-ruby-operation-record"
JOB_MINUTES = 30
STEP_MINUTES = 10
RETENTION_DAYS = 45


def load(kind):
    """Read the concrete manually dispatched caller, not a copied manifest."""
    return yaml.safe_load(
        (
            ROOT / f".github/workflows/workflow-delivery-v3-ruby-{kind}.yml"
        ).read_text()
    )


def stage(steps, name):
    """Select the one public command in the actual step sequence."""
    matches = [
        index
        for index, step in enumerate(steps)
        if re.search(
            r"ruby_operation_cli " + re.escape(name) + r"(?: |$)",
            step.get("run", ""),
        )
    ]
    assert len(matches) == 1
    return matches[0]


def persist(steps, role):
    """Locate the singular immutable upload of a known role."""
    matches = [
        index
        for index, step in enumerate(steps)
        if step.get("uses") == RECORD and step["with"]["role"] == role
    ]
    assert len(matches) == 1
    return matches[0]


@pytest.mark.parametrize("kind", ["bootstrap"])
def test_operation_workflow_scopes_privileges_and_manual_admission(
    kind,
):
    """Only the selected Environment publisher possesses mutation capability."""
    workflow = load(kind)
    assert workflow["permissions"] == {}
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert set(workflow["on"]["workflow_dispatch"]["inputs"]) == {
        "operation",
        "destination",
    }
    assert workflow["concurrency"]["cancel-in-progress"] is False
    assert "inputs.destination" in workflow["concurrency"]["group"]
    request = workflow["jobs"]["request-ruby-operation"]
    for requirement in (
        "github.run_attempt == 1",
        "github.actor_id == '712433'",
        "github.ref == 'refs/heads/main'",
        "github.event_name == 'workflow_dispatch'",
    ):
        assert requirement in request["if"]
    publishers = {}
    for name, job in workflow["jobs"].items():
        permission = job["permissions"]
        if name.startswith("publish-ruby-"):
            destination = (
                "github-packages"
                if name.endswith("github-packages")
                else "rubygems"
            )
            publishers[destination] = name
            assert "environment" in job
            assert f"outputs.destination == '{destination}'" in job["if"]
            assert "result == 'success'" in job["if"]
            assert permission.get("packages") == (
                "write" if destination == "github-packages" else None
            )
            assert permission.get("id-token") == (
                "write" if destination == "rubygems" else None
            )
            assert all(
                not re.search(
                    r"ruby_operation_cli "
                    r"(build|quality|provider|remote)(?: |$)",
                    step.get("run", ""),
                )
                for step in job["steps"]
            )
        else:
            assert "environment" not in job
            assert "id-token" not in permission
            assert permission.get("packages") != "write"
    assert set(publishers) == {"github-packages", "rubygems"}
    prefix = "publish-ruby-bootstrap-"
    assert publishers == {name: prefix + name for name in publishers}


@pytest.mark.parametrize("kind", ["bootstrap"])
def test_operation_workflow_orders_readback_and_reservations(
    kind,
):
    """Preparation precedes Environment admission; marker precedes execution."""
    workflow = load(kind)
    prepare_name = "prepare-ruby-bootstrap-publication"
    preparation = workflow["jobs"][prepare_name]["steps"]
    assert (
        stage(preparation, "initialize")
        < persist(preparation, "phase-eligibility")
        < stage(preparation, "observe")
    )
    assert (
        stage(preparation, "summary")
        < persist(preparation, "summary")
        < stage(preparation, "bundle")
        < persist(preparation, "bundle")
    )
    for name, job in workflow["jobs"].items():
        if not name.startswith("publish-ruby-"):
            continue
        assert prepare_name in job["needs"]
        steps = job["steps"]
        assert (
            stage(steps, "initialize")
            < stage(steps, "authorize")
            < persist(steps, "authorization")
            < persist(steps, "phase-pre-marker")
            < stage(steps, "marker")
            < persist(steps, "marker")
            < persist(steps, "phase-execute")
            < stage(steps, "execute")
            < persist(steps, "result")
        )
        assert steps[stage(steps, "export")]["if"] == "always()"
        assert (
            "steps.execute.outcome"
            in job["outputs"]["publication-step-outcome"]
        )


@pytest.mark.parametrize("kind", ["bootstrap"])
def test_operation_terminal_and_separate_remote_are_failure_aware(kind):
    """Cancellation and missing DAG edges still reach the terminal command."""
    jobs = load(kind)["jobs"]
    name = "finalize-ruby-bootstrap"
    finalizer = jobs[name]
    assert finalizer["if"] == "always()"
    assert set(finalizer["needs"]) == set(jobs) - {
        name,
        "verify-ruby-bootstrap-remote",
    }
    assert (
        finalizer["steps"][stage(finalizer["steps"], "finalize")]["if"]
        == "always()"
    )
    assert (
        "always()"
        in finalizer["steps"][persist(finalizer["steps"], "terminal")]["if"]
    )
    remote = jobs["verify-ruby-bootstrap-remote"]
    assert remote["needs"] == name
    assert "result == 'success'" in remote["if"]
    assert "terminal-result" in remote["if"]
    assert (
        persist(remote["steps"], "phase-remote-consumer")
        < stage(remote["steps"], "remote")
        < persist(remote["steps"], "remote-consumer")
    )


@pytest.mark.parametrize("kind", ["bootstrap"])
def test_operation_actions_pin_deadlines_ids_and_checkout(
    kind,
):
    """No artifact discovery, implicit target or unbounded action is allowed."""
    workflow = load(kind)
    assert workflow["env"]["ACTIONS_ARTIFACT_UPLOAD_CONCURRENCY"] == "1"
    assert workflow["env"]["ACTIONS_ARTIFACT_UPLOAD_TIMEOUT_MS"] == "60000"
    for job in workflow["jobs"].values():
        assert job["timeout-minutes"] == JOB_MINUTES
        for step in job["steps"]:
            assert 0 < step["timeout-minutes"] <= STEP_MINUTES
            action = step.get("uses", "")
            if action and not action.startswith("./"):
                assert re.fullmatch(r"[^@]+@[a-f0-9]{40}", action)
            if action.startswith("actions/checkout@"):
                assert step["with"] == {
                    "ref": "${{ github.sha }}",
                    "fetch-depth": 0,
                    "persist-credentials": False,
                    "submodules": False,
                    "lfs": False,
                }
            if action.startswith("actions/download-artifact@"):
                assert step["if"] == "env.WDV3_ARTIFACT_IDS != ''"
                assert (
                    step["with"]["artifact-ids"]
                    == "${{ env.WDV3_ARTIFACT_IDS }}"
                )
                assert step["with"]["skip-decompress"] is True
                assert step["with"]["digest-mismatch"] == "error"
                assert not {"name", "pattern"} & set(step["with"])


@pytest.mark.parametrize(
    ("_path", "expected"),
    [
        ("bootstrap", (10, 23, 32, 96)),
    ],
)
@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_operation_dag_matches_reviewed_action_and_id_bounds(
    _path, expected, destination
):
    """Count actual roles and selected predecessor IDs on each success path."""
    workflow = load("bootstrap")
    edges = {}
    counts = [0, 0, 0, 0]
    for name, job in workflow["jobs"].items():
        if name.startswith("publish-ruby-") and (
            not name.endswith(destination)
        ):
            continue
        candidates = re.findall(
            r"needs\.([a-z0-9-]+)\.outputs\.references",
            job["env"]["WDV3_REFERENCES"],
        )
        incoming = next(
            (
                edges[predecessor]
                for predecessor in candidates
                if predecessor in edges
            ),
            set(),
        )
        roles = []
        for step in job["steps"]:
            action = step.get("uses", "")
            if action.startswith("actions/checkout@"):
                counts[0] += 1
            elif action.startswith("actions/download-artifact@"):
                counts[2] += 1
                counts[3] += len(incoming)
            elif action == RECORD:
                role = step["with"]["role"]
                roles.append(role)
        counts[1] += len(roles)
        counts[2] += len(roles)
        counts[3] += len(roles)
        assert not set(roles) & incoming
        edges[name] = incoming | set(roles)
    assert tuple(counts) == expected


def test_operation_record_rejects_empty_id_before_readback():
    """A missing upload ID must never turn into a download-all invocation."""
    action = yaml.safe_load(
        (
            ROOT
            / ".github/actions"
            / "workflow-delivery-v3-ruby-operation-record/action.yml"
        ).read_text()
    )
    steps = action["runs"]["steps"]
    assert stage(steps, "upload-check") == 0
    upload = next(
        index for index, step in enumerate(steps) if step.get("id") == "upload"
    )
    download = next(
        index
        for index, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/download-artifact@")
    )
    guard = next(
        index
        for index, step in enumerate(steps)
        if step.get("run") == 'test -n "${RECORD_ID}"'
    )
    assert upload < guard < download < stage(steps, "bind")
    assert steps[upload]["with"]["archive"] is False
    assert steps[upload]["with"]["overwrite"] is False
    assert steps[upload]["with"]["if-no-files-found"] == "error"
    assert (
        steps[download]["with"]["artifact-ids"]
        == "${{ steps.upload.outputs.artifact-id }}"
    )
    assert steps[download]["with"]["digest-mismatch"] == "error"
    assert steps[download]["with"]["skip-decompress"] is True
    assert (
        ".wdv3/readback/${RECORD_ROLE}/" in steps[stage(steps, "bind")]["run"]
    )
    assert all(
        not step.get("continue-on-error", False) and "if" not in step
        for step in steps
    )


def ordinary():
    """Current ordinary entry, distinct from the retained bootstrap reader."""
    return load("smoke")


def step_matching(job, fragment):
    """Select a singular observable command in the real job sequence."""
    matches = [step for step in job["steps"] if fragment in step.get("run", "")]
    assert len(matches) == 1, fragment
    return matches[0]


def test_ordinary_publishers_keep_actual_identity_environment_and_capability():
    """Ordinary publishers keep actual identity environment and capability."""
    workflow = ordinary()
    assert workflow["permissions"] == {}
    assert set(workflow["on"]) == {"workflow_dispatch"}
    inputs = workflow["on"]["workflow_dispatch"]["inputs"]
    assert set(inputs) == {"destination", "dry-run"}
    assert inputs["destination"]["options"] == ["github-packages", "rubygems"]
    assert inputs["dry-run"]["type"] == "boolean"
    assert inputs["dry-run"]["default"] is True
    jobs = workflow["jobs"]
    assert set(jobs) == {
        "prepare",
        "observe",
        "publish-ruby-github-packages",
        "publish-ruby-rubygems",
        "verify-result",
    }
    expected = {
        "publish-ruby-github-packages": (
            "workflow-delivery-v3-ruby-github-packages",
            {"contents": "read", "packages": "write"},
        ),
        "publish-ruby-rubygems": (
            "workflow-delivery-v3-ruby-rubygems",
            {"contents": "read", "id-token": "write"},
        ),
    }
    for name, job in jobs.items():
        if name in expected:
            environment, permissions = expected[name]
            assert job["environment"] == {"name": environment}
            assert job["permissions"] == permissions
            assert job["env"]["GITHUB_TOKEN"] == "${{ github.token }}"  # noqa: S105 - Native expression, no static token.
        else:
            assert "environment" not in job
            assert job["permissions"] == (
                {"contents": "read"}
                if name == "prepare"
                else {"contents": "read", "packages": "read"}
            )
    credential_steps = [
        (name, step)
        for name, job in jobs.items()
        for step in job["steps"]
        if step.get("uses", "").startswith(
            "rubygems/configure-rubygems-credentials@"
        )
    ]
    assert len(credential_steps) == 1
    name, credential = credential_steps[0]
    assert name == "publish-ruby-rubygems"
    assert credential["uses"] == (
        "rubygems/configure-rubygems-credentials@8674aee7f9f9055a8b771794d6f1e66b5fad36a9"
    )
    assert credential["with"] == {
        "gem-server": "https://rubygems.org",
        "audience": "rubygems.org",
        "trusted-publisher": True,
    }
    assert credential["env"] == {
        "HOME": "${{ runner.temp }}/ruby-credential-home"
    }
    job = jobs[name]
    assert job["steps"].index(
        step_matching(job, 'mkdir "$RUNNER_TEMP/ruby-credential-home"')
    ) < job["steps"].index(credential)
    cleanup = step_matching(
        job, 'rm -rf -- "$RUNNER_TEMP/ruby-credential-home"'
    )
    assert cleanup["if"] == "always()"
    for candidate in jobs.values():
        assert all("secrets." not in str(step) for step in candidate["steps"])
        for upload in candidate["steps"]:
            if upload.get("uses", "").startswith("actions/upload-artifact@"):
                assert "credential" not in upload["with"]["path"]


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_ordinary_dry_and_exact_omit_publisher_and_credential_gate(destination):
    """Ordinary dry and exact omit publisher and credential gate."""
    jobs = ordinary()["jobs"]
    publisher = jobs[f"publish-ruby-{destination}"]
    # Assert GitHub guard syntax without reimplementing its evaluator.
    assert publisher["if"] == (
        "inputs.dry-run == false && "
        "needs.observe.outputs.write-needed == 'true'"
        f" && inputs.destination == '{destination}'"
    )
    assert publisher["needs"] == ["prepare", "observe"]
    assert (
        jobs["observe"]["outputs"]["write-needed"]
        == "${{ steps.state.outputs.write-needed }}"
    )
    observe = step_matching(jobs["observe"], "release describe-ruby")
    assert 'if [ "$DRY_RUN" = true ]; then' in observe["run"]
    assert 'release_caller.rb" state' in observe["run"]


def test_ordinary_complete_control_precedes_product_execution_and_transfers():
    """Ordinary complete control precedes product execution and transfers."""
    prepare = ordinary()["jobs"]["prepare"]
    control = step_matching(prepare, "dotnet build")
    qualify = step_matching(prepare, "release prepare-ruby")
    assert prepare["steps"].index(control) < prepare["steps"].index(qualify)
    assert "-p:RestoreLockedMode=true" in control["run"]
    assert ' -o "$RUNNER_TEMP/control"' in " ".join(control["run"].split())
    assert (
        "{facts.rb,source.rb,consumer.rb,release_caller.rb}" in control["run"]
    )
    assert control["run"].index("dotnet build") < control["run"].index(
        'release_caller.rb" request'
    )
    assert 'release_caller.rb" originals' in qualify["run"]
    assert qualify["run"].index("release prepare-ruby") < qualify["run"].index(
        'release_caller.rb" originals'
    )
    checkout = next(
        step
        for step in prepare["steps"]
        if step.get("uses", "").startswith("actions/checkout@")
    )
    assert checkout["with"]["ref"] == "${{ github.sha }}"
    assert checkout["with"]["fetch-depth"] == 0
    assert checkout["with"]["persist-credentials"] is False
    for name, job in ordinary()["jobs"].items():
        if name != "prepare":
            assert all(
                not step.get("uses", "").startswith("actions/checkout@")
                for step in job["steps"]
            )


def test_ordinary_artifacts_bind_ids_retention_and_producers():
    """Ordinary artifacts bind ids retention and producers."""
    jobs = ordinary()["jobs"]
    incoming = {
        "${{ needs.prepare.outputs.control-id }}": "control",
        "${{ needs.prepare.outputs.preparation-id }}": "prepared",
        "${{ needs.observe.outputs.observation-id }}": "observation",
    }
    for name, job in jobs.items():
        for step in job["steps"]:
            action = step.get("uses", "")
            if action:
                assert re.fullmatch(r"[^@]+@[a-f0-9]{40}", action)
            if action.startswith("actions/upload-artifact@"):
                options = step["with"]
                assert step["if"] == "always()"
                assert options["retention-days"] == RETENTION_DAYS
                assert options["overwrite"] is False
                assert options["if-no-files-found"] == "error"
                assert (
                    "${{ github.run_id }}-${{ github.run_attempt }}"
                    in options["name"]
                )
                if name != "verify-result":
                    assert any(
                        f"steps.{step['id']}.outputs.artifact-id" in output
                        for output in job["outputs"].values()
                    )
            elif action.startswith("actions/download-artifact@"):
                options = step["with"]
                assert options["digest-mismatch"] == "error"
                assert not {"name", "pattern"} & set(options)
                if options["artifact-ids"] in incoming:
                    assert (
                        options["path"]
                        == "${{ runner.temp }}/"
                        + incoming[options["artifact-ids"]]
                    )
                else:
                    assert name == "verify-result"
                    assert (
                        "needs.publish-ruby-github-packages.outputs.publication-id"
                        in options["artifact-ids"]
                    )
                    assert (
                        "needs.publish-ruby-rubygems.outputs.publication-id"
                        in options["artifact-ids"]
                    )
                    assert options["path"] == "${{ runner.temp }}/publication"
        if name != "prepare":
            validate = step_matching(job, "validate control prepared")
            product = next(
                step
                for step in job["steps"]
                if "release " in step.get("run", "")
            )
            assert job["steps"].index(validate) < job["steps"].index(product)
            if name != "observe":
                assert (
                    "validate control prepared observation" in validate["run"]
                )


def test_ordinary_verify_survives_publisher_failure_or_missing_transfer():
    """Ordinary verify survives publisher failure or missing transfer."""
    jobs = ordinary()["jobs"]
    verify = jobs["verify-result"]
    assert set(verify["needs"]) == {
        "prepare",
        "observe",
        "publish-ruby-github-packages",
        "publish-ruby-rubygems",
    }
    guard = " ".join(verify["if"].split())
    assert guard == (
        "always() && !cancelled() && inputs.dry-run == false && "
        "needs.prepare.result == 'success' && needs.observe.result == 'success'"
    )
    download = next(
        step
        for step in verify["steps"]
        if step.get("with", {}).get("path") == "${{ runner.temp }}/publication"
    )
    assert " ".join(download["if"].split()) == (
        "needs.publish-ruby-github-packages.outputs.publication-id != '' || "
        "needs.publish-ruby-rubygems.outputs.publication-id != ''"
    )
    validate = step_matching(verify, "validate publication")
    assert 'if [ -d "$RUNNER_TEMP/publication" ]; then' in validate["run"]
    completion = step_matching(verify, "release complete-ruby")
    commands = completion["run"]
    assert (
        commands.index("release consume-ruby")
        < commands.index("release observe-ruby")
        < commands.index("release complete-ruby")
    )
    assert "publication=-" in commands
    assert 'if [ -s "$RUNNER_TEMP/publication/publication.json" ]' in commands
    assert '"$publication"' in commands
    assert "|| true" in commands
    assert all(
        "publish-ruby" not in step.get("run", "") for step in verify["steps"]
    )
    assert "environment" not in verify
    assert verify["permissions"] == {"contents": "read", "packages": "read"}


def test_ordinary_and_bootstrap_share_registry_package_concurrency():
    """Ordinary and bootstrap share registry package concurrency."""
    current, bootstrap = (
        ordinary()["concurrency"],
        load("bootstrap")["concurrency"],
    )
    assert (
        current["cancel-in-progress"]
        is bootstrap["cancel-in-progress"]
        is False
    )
    assert " ".join(current["group"].split()) == " ".join(
        bootstrap["group"].split()
    )
    assert " ".join(current["group"].split()) == (
        "${{ format('workflow-delivery-{0}-hcoona-release-smoke-ruby', "
        "inputs.destination == 'github-packages' && "
        "'rubygems.pkg.github.com-hcoona' || 'rubygems.org') }}"
    )
    for partition in (
        "github.ref",
        "run_id",
        "run_attempt",
        "dry-run",
        "github.workflow",
    ):
        assert partition not in current["group"]


def test_ordinary_native_preparation_gate_uses_existing_actual_method():
    """Protect the existing native gate route without claiming qualification."""
    gate_path = ROOT / "eng/scripts/check_ruby_release_preparation.py"
    import ast  # noqa: PLC0415 - Bounded existing gate configuration inspection.
    import tomllib  # noqa: PLC0415 - Existing root declaration.

    assignments = {
        node.targets[0].id: ast.literal_eval(node.value)
        for node in ast.parse(gate_path.read_text()).body
        if isinstance(node, ast.Assign)
        and isinstance(node.targets[0], ast.Name)
        and node.targets[0].id == "METHOD"
    }
    assert (
        assignments["METHOD"]
        == "NativePreparationQualifiesCommittedOriginalAndInstalledOrigin"
    )
    native = (
        ROOT
        / "tests/private/app/workflow-delivery/Release"
        / "RubyPreparationNativeTests.cs"
    )
    assert "await RubyPreparation.PrepareAsync(" in native.read_text()
    declaration = tomllib.loads((ROOT / "pyproject.toml").read_text())
    assert (
        "tests/eng/test_ruby_release_preparation_gate.py"
        in declaration["tool"]["pytest"]["ini_options"]["testpaths"]
    )
    ci = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
    retained = next(
        step
        for step in ci["jobs"]["python-tests"]["steps"]
        if step.get("with", {}).get("name") == "ruby-release-preparation"
    )
    assert (
        retained["if"]
        == "always() && steps.plan.outputs.native_ruby_preparation == 'true'"
    )
    assert retained["with"]["if-no-files-found"] == "error"
