"""Credential-free stdlib caller contracts; native RubyGems is not simulated."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
HELPER = (
    ROOT / "src/private/app/workflow-delivery/Native/Ruby/release_caller.rb"
)
DECLARATION = (
    "tests/private/app/workflow-delivery/fixtures/products/"
    "hcoona-release-smoke-ruby/workflow-delivery.release-unit.yml"
)


def capture(arguments, **options):
    """Retain original process output when a work carrier is selected."""
    result = subprocess.run(  # noqa: S603 - Explicit owned executable/inputs.
        arguments,
        capture_output=True,
        text=True,
        timeout=90,
        check=False,
        **options,
    )
    if directory := os.environ.get("RUBY_TRANSFER_TEST_RECEIPTS"):
        carrier = Path(directory)
        carrier.mkdir(parents=True, exist_ok=True)
        number = len(list(carrier.glob("*.json")))
        (carrier / f"{number:04d}.json").write_text(
            json.dumps(
                {
                    "arguments": list(map(str, arguments)),
                    "returncode": result.returncode,
                    "stdout": result.stdout,
                    "stderr": result.stderr,
                },
                indent=2,
            )
            + "\n"
        )
    return result


class Caller:
    """Real Ruby process with explicit finite runner inputs and owned state."""

    def __init__(self, root):
        """Own each runner directory and select explicit prepared tools."""
        self.root = root
        (root / "control").mkdir()
        self.env = {
            "PATH": os.defpath,
            "RUNNER_TEMP": str(root),
            "GITHUB_WORKSPACE": str(ROOT),
            "GITHUB_REPOSITORY": "hcoona/three",
            "GITHUB_SHA": "a" * 40,
            "GITHUB_REF": "refs/heads/development",
            "GITHUB_RUN_ID": "1234567",
            "GITHUB_RUN_ATTEMPT": "1",
            "DESTINATION": "github-packages",
            "DRY_RUN": "false",
            "GITHUB_OUTPUT": str(root / "github-output"),
            "RUBY_EXECUTABLE": shutil.which("ruby"),
            "GEM_EXECUTABLE": shutil.which("gem"),
            "DOTNET_EXECUTABLE": shutil.which("dotnet"),
        }

    def run(self, *arguments):
        """Invoke the frozen helper with Ruby stdlib and no inherited tokens."""
        return capture(
            [
                self.env["RUBY_EXECUTABLE"],
                "--disable-gems",
                str(HELPER),
                *arguments,
            ],
            env=self.env,
            cwd=self.root,
        )

    def ok(self, *arguments):
        """Require clean success, including empty original diagnostics."""
        result = self.run(*arguments)
        assert result.returncode == 0, result.stderr
        assert result.stdout == ""
        assert result.stderr == ""

    def rejected(self, *arguments, reason):
        """Require the reached failure and its original diagnostic."""
        result = self.run(*arguments)
        assert result.returncode != 0
        assert reason in result.stderr
        assert result.stdout == ""

    def write(self, path, value):
        """Write controlled transfer input into an owned phase directory."""
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value))

    def read(self, path):
        """Read the actual emitted transfer without an intermediate adapter."""
        return json.loads((self.root / path).read_text())

    @property
    def subject(self):
        """Use the request emitted by the real helper as subject context."""
        return self.read("prepared/request.json")["subject"]

    def preparation(self, files):
        """Provide a transfer shape without claiming native qualification."""
        return {
            "prepared": {
                "subject": self.subject,
                "files": files,
                "requiredChecks": [],
                "checks": [],
            },
            "native": {},
            "commands": [],
            "errors": [],
        }


@pytest.fixture
def caller(tmp_path):
    """Create a fresh runner layout for each subprocess scenario."""
    return Caller(tmp_path)


@pytest.fixture(scope="module")
def typed_transfer(tmp_path_factory):
    """Use the application's actual generated JSON context, offline."""
    directory = tmp_path_factory.mktemp("ruby-transfer-producer")
    assembly = (
        ROOT
        / "src/private/app/workflow-delivery/bin/Debug/net10.0"
        / "WorkflowDelivery.dll"
    )
    build_control = capture(
        [
            shutil.which("dotnet"),
            "build",
            "src/private/app/workflow-delivery/WorkflowDelivery.csproj",
            "--no-restore",
            "-v:q",
        ],
        cwd=ROOT,
    )
    assert build_control.returncode == 0, (
        build_control.stdout + build_control.stderr
    )
    assert assembly.is_file()
    (directory / "empty-feed").mkdir()
    (directory / "Probe.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        "<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>"
        "</PropertyGroup></Project>"
    )
    (directory / "Program.cs").write_text("""
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
var location = Path.GetDirectoryName(args[0])!;
AssemblyLoadContext.Default.Resolving += (_, name) => {
    var path = Path.Combine(location, name.Name + ".dll");
    return File.Exists(path)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(args[0]);
var contextType = assembly.GetType("WorkflowDelivery.TransferJson", true)!;
var context = (JsonSerializerContext)contextType
    .GetProperty("Default")!.GetValue(null)!;
var type = assembly.GetType(args[1], true)!;
var info = context.GetTypeInfo(type)!;
var typed = JsonSerializer.Deserialize(File.ReadAllText(args[2]), info)!;
Console.WriteLine(JsonSerializer.Serialize(typed, info));
""")
    build = capture(
        [
            shutil.which("dotnet"),
            "build",
            "Probe.csproj",
            "--source",
            str(directory / "empty-feed"),
            "-v:q",
        ],
        cwd=directory,
    )
    assert build.returncode == 0, build.stdout + build.stderr
    probe = directory / "bin/Debug/net10.0/Probe.dll"

    def serialize(type_name, value):
        """Emit canonical JSON through the actual typed producer context."""
        path = directory / "controlled.json"
        path.write_text(json.dumps(value))
        result = capture(
            [
                shutil.which("dotnet"),
                str(probe),
                str(assembly),
                type_name,
                str(path),
            ],
            cwd=directory,
        )
        assert result.returncode == 0, result.stderr
        return json.loads(result.stdout)

    return serialize


@pytest.mark.parametrize(
    "selection",
    [
        (
            "github-packages",
            "refs/heads/development",
            "false",
            "buddy",
            "github-packages-ruby",
        ),
        ("rubygems", "refs/heads/main", "false", "official", "rubygems"),
        ("rubygems", "refs/tags/preview", "true", "official", "rubygems"),
    ],
)
def test_request_binds_current_declaration_ref_channel_destination_and_mode(
    caller,
    typed_transfer,
    selection,
):
    """Request binds current declaration ref channel destination and mode."""
    destination, ref, dry, channel, target = selection
    caller.env.update(DESTINATION=destination, GITHUB_REF=ref, DRY_RUN=dry)
    caller.ok("request")
    expected = {
        "revision": "a" * 40,
        "selectedRef": ref,
        "unit": "hcoona-release-smoke-ruby",
        "declaration": DECLARATION,
        "channel": channel,
        "destination": target,
        "dryRun": dry == "true",
    }
    request = typed_transfer(
        "WorkflowDelivery.Release.RubyPreparationRequest",
        caller.read("prepared/request.json"),
    )
    assert request["subject"] == expected
    assert request["checkout"] == str(ROOT)
    assert request["scratch"] == str(caller.root / "preparation-scratch")
    assert request["tools"] == {
        name: caller.env[name.upper() + "_EXECUTABLE"]
        for name in ("ruby", "gem", "dotnet")
    }
    for kind in ("prepared", "control"):
        assert caller.read(f"{kind}/producer.json") == {
            "subject": expected,
            "run": "1234567",
            "attempt": "1",
        }


@pytest.mark.parametrize(
    ("variable", "value", "reason"),
    [
        ("GITHUB_REPOSITORY", "another/three", "Unsupported Ruby repository"),
        ("DESTINATION", "pypi", "Unsupported Ruby destination"),
        ("GITHUB_RUN_ATTEMPT", "2", "Live rerun is unavailable"),
    ],
)
def test_request_rejects_other_repository_destination_and_live_rerun(
    caller,
    variable,
    value,
    reason,
):
    """Request rejects other repository destination and live rerun."""
    caller.env[variable] = value
    caller.rejected("request", reason=reason)
    assert not (caller.root / "prepared").exists()
    assert not (caller.root / "control/producer.json").exists()


def test_dry_rerun_keeps_actual_attempt_without_live_permission(caller):
    """Dry rerun keeps actual attempt without live permission."""
    caller.env.update(DRY_RUN="true", GITHUB_RUN_ATTEMPT="2")
    caller.ok("request")
    assert caller.subject["dryRun"] is True
    assert caller.read("prepared/producer.json")["attempt"] == "2"


def test_validate_accepts_complete_same_producer_transfers(
    caller, typed_transfer
):
    """Validate accepts complete same producer transfers."""
    caller.ok("request")
    value = typed_transfer(
        "WorkflowDelivery.Release.RubyPreparationResult", caller.preparation([])
    )
    caller.write("prepared/preparation.json", value)
    caller.ok("phase", "observation")
    caller.ok("phase", "publication")
    caller.ok("validate", "control", "prepared", "observation", "publication")
    assert caller.read("publication/producer.json") == {
        "subject": caller.subject,
        "run": "1234567",
        "attempt": "1",
    }


@pytest.mark.parametrize(
    "kind", ["control", "prepared", "observation", "publication"]
)
@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("run", "different-run"),
        ("attempt", "2"),
        ("revision", "b" * 40),
        ("selectedRef", "refs/heads/other"),
        ("unit", "other-gem"),
        ("declaration", "other.yml"),
        ("channel", "official"),
        ("destination", "rubygems"),
        ("dryRun", True),
    ],
)
def test_validate_rejects_mismatched_producer_subject_run_and_attempt(
    caller,
    kind,
    field,
    value,
):
    """Validate rejects mismatched producer subject run and attempt."""
    caller.ok("request")
    caller.write("prepared/preparation.json", caller.preparation([]))
    for phase in ("observation", "publication"):
        caller.ok("phase", phase)
    producer = caller.read(f"{kind}/producer.json")
    if field in ("run", "attempt"):
        producer[field] = value
    else:
        producer["subject"][field] = value
    caller.write(f"{kind}/producer.json", producer)
    caller.rejected("validate", kind, reason="Transferred producer mismatch")


@pytest.mark.parametrize("operation", ["validate", "originals", "state"])
def test_preparation_or_observation_subject_cannot_cross_request(
    caller, operation
):
    """Preparation or observation subject cannot cross request."""
    caller.ok("request")
    preparation = caller.preparation([])
    preparation["prepared"]["subject"]["destination"] = "rubygems"
    caller.write("prepared/preparation.json", preparation)
    caller.write(
        "observation/observation.json",
        {
            "observation": {
                "subject": preparation["prepared"]["subject"],
                "files": [],
            }
        },
    )
    if operation == "validate":
        caller.rejected(
            "validate", "prepared", reason="Transferred Ruby subject mismatch"
        )
    else:
        caller.rejected(
            operation,
            reason=(
                "Preparation" if operation == "originals" else "Observation"
            )
            + " subject mismatch",
        )
    assert not (caller.root / "github-output").exists()


@pytest.mark.parametrize(
    "missing",
    [
        "prepared/producer.json",
        "prepared/preparation.json",
        "observation/producer.json",
    ],
)
def test_missing_transfer_fails_without_replacement_or_retry(caller, missing):
    """Missing transfer fails without replacement or retry."""
    caller.ok("request")
    caller.ok("phase", "observation")
    caller.write("prepared/preparation.json", caller.preparation([]))
    (caller.root / missing).unlink()
    caller.rejected(
        "validate",
        "prepared",
        "observation",
        reason="No such file or directory",
    )
    assert not (caller.root / missing).exists()


def test_originals_copy_complete_bytes_and_retain_preparation(
    caller, typed_transfer
):
    """Originals copy complete bytes and retain preparation."""
    caller.ok("request")
    original = caller.root / "preparation-scratch/package/original.gem"
    original.parent.mkdir()
    contents = b"original controlled transfer\x00\xff\n"
    original.write_bytes(contents)
    files = [
        {
            "build": "ruby-gem",
            "output": "gem",
            "kind": "ruby-gem",
            "name": "hcoona-release-smoke-ruby",
            "version": "0.1.0.pre.beta.1",
            "path": "package/original.gem",
            "size": len(contents),
            "sha256": hashlib.sha256(contents).hexdigest(),
        }
    ]
    caller.write(
        "prepared/preparation.json",
        typed_transfer(
            "WorkflowDelivery.Release.RubyPreparationResult",
            caller.preparation(files),
        ),
    )
    before = (caller.root / "prepared/preparation.json").read_bytes()
    caller.ok("originals")
    assert (
        caller.root / "prepared/package/original.gem"
    ).read_bytes() == contents
    assert original.read_bytes() == contents
    assert (caller.root / "prepared/preparation.json").read_bytes() == before


@pytest.mark.parametrize(
    "escape", ["parent", "absolute", "symlink", "sibling-prefix"]
)
def test_originals_reject_original_path_escape_before_copy(caller, escape):
    """Originals reject original path escape before copy."""
    caller.ok("request")
    outside = caller.root / "preparation-scratch-other"
    outside.mkdir()
    original = outside / "original.gem"
    original.write_bytes(b"outside original")
    relative = "../preparation-scratch-other/original.gem"
    if escape == "absolute":
        relative = str(original)
    elif escape == "symlink":
        (caller.root / "preparation-scratch/package").symlink_to(
            outside, target_is_directory=True
        )
        relative = "package/original.gem"
    elif escape == "sibling-prefix":
        (caller.root / "preparation-scratch/original.gem").symlink_to(original)
        relative = "original.gem"
    caller.write(
        "prepared/preparation.json", caller.preparation([{"path": relative}])
    )
    caller.rejected("originals", reason="Original transfer leaves preparation")
    assert original.read_bytes() == b"outside original"
    assert not (caller.root / "prepared/package").exists()
    assert not (caller.root / "prepared/original.gem").exists()


@pytest.mark.parametrize(
    ("state", "needed"),
    [
        ("absent", True),
        ("exact", False),
        ("conflict", False),
        ("unknown", False),
    ],
)
def test_state_uses_actual_transferjson_canonical_state(
    caller, typed_transfer, state, needed
):
    """State uses actual transferjson canonical state."""
    caller.ok("request")
    value = typed_transfer(
        "WorkflowDelivery.Destinations.RubyObservationResult",
        {
            "observation": {
                "subject": caller.subject,
                "files": [
                    {
                        "output": "gem",
                        "name": "hcoona-release-smoke-ruby",
                        "version": "0.1.0.pre.beta.1",
                        "state": state,
                        "size": None,
                        "sha256": None,
                    }
                ],
                "requiredChecks": [],
                "checks": [],
            },
            "read": {
                "state": "unknown",
                "original": None,
                "command": None,
                "errors": [],
            },
        },
    )
    assert value["observation"]["files"][0]["state"] == state.capitalize()
    caller.write("observation/observation.json", value)
    (caller.root / "github-output").write_text("prior-output=retained\n")
    caller.ok("state")
    assert (caller.root / "github-output").read_text() == (
        "prior-output=retained\nwrite-needed=" + str(needed).lower() + "\n"
    )


@pytest.mark.parametrize(
    ("operation", "keys"),
    [
        ("source-config", {"ruby", "scratch", "helper"}),
        ("publisher-config", {"ruby", "scratch", "gem"}),
        (
            "consumer-config",
            {
                "ruby",
                "scratch",
                "factsHelper",
                "sourceHelper",
                "consumerHelper",
            },
        ),
    ],
)
def test_config_uses_selected_tools_and_owned_control_helpers(
    caller, operation, keys
):
    """Config uses selected tools and owned control helpers."""
    caller.ok(operation, "fresh-scratch")
    value = caller.read(operation.replace("config", "tools") + ".json")
    assert set(value) == keys
    assert value["ruby"] == caller.env["RUBY_EXECUTABLE"]
    assert value["scratch"] == str(caller.root / "fresh-scratch")
    assert (caller.root / "fresh-scratch").is_dir()
    for key, filename in {
        "helper": "source.rb",
        "factsHelper": "facts.rb",
        "sourceHelper": "source.rb",
        "consumerHelper": "consumer.rb",
    }.items():
        if key in value:
            assert value[key] == str(caller.root / "control" / filename)
    if "gem" in value:
        assert value["gem"] == caller.env["GEM_EXECUTABLE"]


@pytest.mark.parametrize(
    "arguments",
    [("validate",), ("phase",), ("source-config",), ("unsupported",)],
)
def test_unsupported_operation_cannot_create_transfer(caller, arguments):
    """Unsupported operation cannot create transfer."""
    caller.rejected(
        *arguments,
        reason={
            "validate": "Missing transfer kinds",
            "phase": "Unsupported phase input",
            "source-config": "Unsupported tools input",
        }.get(arguments[0], "Unsupported ordinary Ruby caller operation"),
    )
    assert list(caller.root.iterdir()) == [caller.root / "control"]


@pytest.mark.parametrize("kind", ["prepared", "observation"])
def test_invalid_json_transfer_preserves_original_failure(caller, kind):
    """Malformed reached transfers fail without replacing their evidence."""
    caller.ok("request")
    caller.ok("phase", "observation")
    caller.write("prepared/preparation.json", caller.preparation([]))
    target = caller.root / kind / "producer.json"
    target.write_text('{"subject":')
    caller.rejected("validate", kind, reason="JSON::ParserError")
    assert target.read_text() == '{"subject":'


def test_missing_original_cannot_create_prepared_archive(caller):
    """Unavailable original bytes cannot become an empty successful transfer."""
    caller.ok("request")
    caller.write(
        "prepared/preparation.json",
        caller.preparation([{"path": "package/unavailable.gem"}]),
    )
    caller.rejected("originals", reason="No such file or directory")
    assert not (caller.root / "prepared/package").exists()


@pytest.mark.parametrize(
    "operation",
    ["phase", "source-config", "publisher-config", "consumer-config"],
)
def test_fresh_owned_phase_or_tool_state_is_not_overwritten(caller, operation):
    """An occupied job directory retains its bytes instead of being reused."""
    caller.ok(operation, "scratch")
    witness = caller.root / "scratch/witness"
    witness.write_bytes(b"retained original")
    caller.rejected(operation, "scratch", reason="File exists")
    assert witness.read_bytes() == b"retained original"


def test_absolute_original_inside_scratch_still_cannot_be_a_transfer_path(
    caller,
):
    """Containment alone cannot authorize an absolute original transfer path."""
    caller.ok("request")
    original = caller.root / "preparation-scratch/original.gem"
    original.write_bytes(b"owned original")
    caller.write(
        "prepared/preparation.json",
        caller.preparation([{"path": str(original)}]),
    )
    caller.rejected("originals", reason="Original transfer leaves preparation")
    assert original.read_bytes() == b"owned original"
    assert not (caller.root / "prepared/original.gem").exists()


def test_missing_observation_cannot_emit_write_needed(caller):
    """Missing state evidence cannot supply permission for a write."""
    caller.ok("request")
    caller.rejected("state", reason="No such file or directory")
    assert not (caller.root / "github-output").exists()
