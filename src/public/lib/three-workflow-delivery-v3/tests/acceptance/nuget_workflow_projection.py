"""Bounded official-parser projection for the three NuGet workflow tests."""

from __future__ import annotations

import json
import shutil
import subprocess
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from pathlib import Path

_MODULE_ROOT = "three_workflow_delivery_v3.acceptance."
_PYTHON_COMMANDS = {"python", "python3", "python.exe"}
_ROLE_VARIABLES = {
    "GITHUB_WORKSPACE",
    "GITHUB_RUN_ID",
    "WDV3_TOOLING",
    "WDV3_TARGET",
    "WDV3_GENERATION",
    "WDV3_REQUEST_NAME",
    "WDV3_REQUEST_ID",
    "WDV3_REQUEST_TRANSPORT_DIGEST",
    "WDV3_REQUEST_URL",
    "WDV3_HELPER_NAME",
    "WDV3_HELPER_ID",
    "WDV3_HELPER_TRANSPORT_DIGEST",
    "WDV3_HELPER_URL",
    "WDV3_SETUP_NAME",
    "WDV3_SETUP_ID",
    "WDV3_SETUP_TRANSPORT_DIGEST",
    "WDV3_SETUP_URL",
    "WDV3_INPUT_NAME",
    "WDV3_FIXTURE_NAME",
}

_PARSER = r"""
using namespace System.Management.Automation.Language

param([string]$InputPath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'

function Element-Fact($element) {
    $fact = @{
        kind = $element.GetType().Name
        text = $element.Extent.Text
        start = $element.Extent.StartOffset
        end = $element.Extent.EndOffset
    }
    if ($element -is [StringConstantExpressionAst]) {
        $fact.value = $element.Value
    } elseif ($element -is [ExpandableStringExpressionAst]) {
        $fact.value = $element.Value
        $fact.nested = @($element.NestedExpressions | ForEach-Object {
            Element-Fact $_
        })
    } elseif ($element -is [VariableExpressionAst]) {
        $fact.variable = $element.VariablePath.UserPath
        $fact.splatted = $element.Splatted
    } elseif ($element -is [CommandParameterAst]) {
        $fact.parameter = $element.ParameterName
        $fact.argument = if ($null -ne $element.Argument) {
            Element-Fact $element.Argument
        } else { $null }
    }
    return $fact
}

function Array-Elements($node) {
    if ($node -is [StatementBlockAst]) {
        if ($node.Statements.Count -ne 1 -or $node.Traps.Count) {
            throw 'Unsupported splat statement block'
        }
        return Array-Elements $node.Statements[0]
    }
    if ($node -is [PipelineAst]) {
        if ($node.PipelineElements.Count -ne 1) {
            throw 'Unsupported splat pipeline'
        }
        return Array-Elements $node.PipelineElements[0]
    }
    if ($node -is [CommandExpressionAst]) {
        return Array-Elements $node.Expression
    }
    if ($node -is [ArrayExpressionAst]) {
        return Array-Elements $node.SubExpression
    }
    if ($node -is [ArrayLiteralAst]) {
        return @($node.Elements | ForEach-Object { Element-Fact $_ })
    }
    if ($node -is [StringConstantExpressionAst] -or
        $node -is [ExpandableStringExpressionAst] -or
        $node -is [VariableExpressionAst]) {
        return ,(Element-Fact $node)
    }
    throw 'Unsupported splat value'
}

$results = @()
$inputs = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
foreach ($record in $inputs) {
    $source = [string]$record.source
    $tokens = $null
    $errors = $null
    $tree = [Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
    $masked = $source.ToCharArray()
    $comments = @()
    foreach ($token in $tokens) {
        if ($token.Kind -eq [TokenKind]::Comment) {
            $comments += @{
                start = $token.Extent.StartOffset
                end = $token.Extent.EndOffset
                text = $token.Text
            }
            $start = $token.Extent.StartOffset
            $end = $token.Extent.EndOffset
            for ($offset = $start; $offset -lt $end; $offset++) {
                if ($masked[$offset] -ne "`r" -and $masked[$offset] -ne "`n") {
                    $masked[$offset] = ' '
                }
            }
        }
    }
    $commands = @($tree.FindAll({
        param($node) $node -is [CommandAst]
    }, $true) | ForEach-Object {
        @{
            name = $_.GetCommandName()
            start = $_.Extent.StartOffset
            end = $_.Extent.EndOffset
            text = $_.Extent.Text
            elements = @($_.CommandElements | ForEach-Object {
                Element-Fact $_
            })
        }
    })
    $assignments = @($tree.FindAll({
        param($node) $node -is [AssignmentStatementAst]
    }, $true) | ForEach-Object {
        $assignment = $_
        $literalArray = @($assignment.Right.FindAll({
            param($node) $node -is [ArrayExpressionAst]
        }, $true))
        $values = @()
        $supported = $false
        if ($literalArray.Count -eq 1) {
            try {
                $values = @(Array-Elements $assignment.Right)
                $supported = $true
            } catch { $supported = $false }
        }
        @{
            left = Element-Fact $assignment.Left
            operator = $assignment.Operator.ToString()
            start = $assignment.Extent.StartOffset
            end = $assignment.Extent.EndOffset
            topLevel = ($assignment.Parent -eq $tree.EndBlock)
            supportedArray = $supported
            values = $values
        }
    })
    $writers = @($tree.FindAll({
        param($node) $node -is [InvokeMemberExpressionAst]
    }, $true) | Where-Object {
        $_.Static -and $_.Expression -is [TypeExpressionAst] -and
        $_.Expression.TypeName.FullName -in @('IO.File', 'System.IO.File') -and
        $_.Member -is [StringConstantExpressionAst] -and
        $_.Member.Value -eq 'WriteAllText'
    } | ForEach-Object {
        @{
            kind = 'WriteAllText'
            start = $_.Extent.StartOffset
            path = Element-Fact $_.Arguments[0]
        }
    })
    $results += @{
        job = $record.job
        index = $record.index
        original = $source
        executable = -join $masked
        comments = $comments
        errors = @($errors | ForEach-Object { $_.Message })
        commands = $commands
        assignments = $assignments
        writers = $writers
    }
}
[IO.File]::WriteAllText(
    $OutputPath,
    (ConvertTo-Json -InputObject @($results) -Depth 30),
    [Text.UTF8Encoding]::new($false)
)
"""


def _unique(values, description):
    assert len(values) == 1, (description, len(values))
    return values[0]


def _literal(element):
    if element["kind"] == "StringConstantExpressionAst":
        return element["value"]
    if element["kind"] == "CommandParameterAst":
        assert element["argument"] is None, element
        return element["text"]
    return None


def _operand(element):
    literal = _literal(element)
    if literal is not None:
        return literal
    if element["kind"] == "VariableExpressionAst":
        variable = element["variable"]
        assert not element["splatted"], element
        assert variable.startswith("env:"), element
        assert variable[4:] in _ROLE_VARIABLES, element
        return "${" + variable + "}"
    assert element["kind"] == "ExpandableStringExpressionAst", element
    value = element["value"]
    for nested in element["nested"]:
        replacement = _operand(nested)
        value = value.replace(nested["text"], replacement)
    return value


def _flag(operands, name):
    positions = [index for index, value in enumerate(operands) if value == name]
    index = _unique(positions, f"flag {name}")
    assert index + 1 < len(operands), name
    value = operands[index + 1]
    assert not value.startswith("--"), (name, value)
    return value


def _path(value):
    return value.replace(
        "${{ github.run_id }}", "${env:GITHUB_RUN_ID}"
    ).replace("\\", "/")


@dataclass(frozen=True)
class StepRole:
    """Keep the configured step and its job-local identity together."""

    job: str
    index: int
    step: dict


@dataclass(frozen=True)
class ModuleInvocation:
    """One actual module command with bounded statically bound operands."""

    role: StepRole
    module: str
    operands: tuple[str, ...]
    command_operands: tuple[str, ...]


@dataclass(frozen=True)
class ArchiveUpload:
    """A producer/consumer pair, without a display-name dependency."""

    archive: StepRole
    upload: StepRole
    path: str


class WorkflowRoles:
    """Resolve only the accepted roles from one official parser projection."""

    def __init__(self, workflow, observations):
        """Bind parser facts to every original configured run scalar."""
        self.workflow = workflow
        self.observations = {
            (row["job"], row["index"]): row for row in observations
        }
        expected = {
            (job, index)
            for job, definition in workflow["jobs"].items()
            for index, step in enumerate(definition["steps"])
            if "run" in step
        }
        assert len(observations) == len(self.observations)
        assert set(self.observations) == expected
        for (job, index), row in self.observations.items():
            assert (
                row["original"] == workflow["jobs"][job]["steps"][index]["run"]
            )
            assert not row["errors"], row["errors"]

    def steps(self, job=None):
        """Enumerate configured positions without collapsing equal mappings."""
        return [
            StepRole(name, index, step)
            for name, definition in self.workflow["jobs"].items()
            if job is None or name == job
            for index, step in enumerate(definition["steps"])
        ]

    def source(self, role):
        """Retain executable source and comments with Actions expressions."""
        row = self.observations[(role.job, role.index)]
        return "\n".join(
            [
                row["executable"],
                *(
                    comment["text"]
                    for comment in row["comments"]
                    if "${{" in comment["text"]
                ),
            ]
        )

    def configuration(self, job=None):
        """Exclude inert display fields at their exact configured positions."""

        def project(value, path):
            if isinstance(value, list):
                return [
                    project(item, (*path, index))
                    for index, item in enumerate(value)
                ]
            if not isinstance(value, dict):
                return value
            result = {}
            for key, item in value.items():
                position = (*path, key)
                display = (
                    position == ("name",)
                    or (
                        len(position) == 3  # noqa: PLR2004 - exact schema position
                        and position[0] == "jobs"
                        and key == "name"
                    )
                    or (
                        len(position) == 5  # noqa: PLR2004 - exact schema position
                        and position[0] == "jobs"
                        and position[2] == "steps"
                        and key == "name"
                    )
                    or (
                        len(position) == 5  # noqa: PLR2004 - exact schema position
                        and position[:3]
                        == ("on", "workflow_dispatch", "inputs")
                        and key == "description"
                    )
                )
                if display and isinstance(item, str) and "${{" not in item:
                    continue
                if (
                    len(position) == 5  # noqa: PLR2004 - exact schema position
                    and position[0] == "jobs"
                    and position[2] == "steps"
                    and key == "run"
                ):
                    result[key] = self.source(
                        StepRole(position[1], position[3], value)
                    )
                else:
                    result[key] = project(item, position)
            return result

        if job is None:
            return project(self.workflow, ())
        return project(self.workflow["jobs"][job], ("jobs", job))

    def _elements(self, row, command, elements):
        result = []
        for element in elements:
            if (
                element["kind"] == "VariableExpressionAst"
                and element["splatted"]
            ):
                writes = [
                    assignment
                    for assignment in row["assignments"]
                    if assignment["left"].get("variable") == element["variable"]
                    and assignment["start"] < command["start"]
                ]
                assert writes, ("unbound module splat", element)
                writes.sort(key=lambda assignment: assignment["start"])
                assert [assignment["operator"] for assignment in writes] == [
                    "Equals",
                    *(["PlusEquals"] * (len(writes) - 1)),
                ], writes
                for assignment in writes:
                    assert assignment["topLevel"], assignment
                    assert assignment["supportedArray"], assignment
                    result.extend(
                        _operand(item) for item in assignment["values"]
                    )
            else:
                result.append(_operand(element))
        return tuple(result)

    def invocations(self, module):
        """Count actual module commands across all jobs and Python launchers."""
        found = []
        for role in self.steps():
            if "run" not in role.step:
                continue
            row = self.observations[(role.job, role.index)]
            for command in row["commands"]:
                if command["name"] not in {"uv", *_PYTHON_COMMANDS}:
                    continue
                elements = command["elements"]
                literals = [_literal(element) for element in elements]
                if (
                    command["name"] == "uv"
                    and len(literals) > 1
                    and literals[1] == "run"
                ):
                    python = [
                        index
                        for index, value in enumerate(literals)
                        if value in _PYTHON_COMMANDS
                        and index + 2 < len(literals)
                        and literals[index + 1] == "-m"
                    ]
                    if not python:
                        continue
                    start = _unique(python, "uv Python module operand")
                elif command["name"] in _PYTHON_COMMANDS:
                    start = 0
                else:
                    continue
                if literals[start + 1 : start + 3] != ["-m", module]:
                    continue
                operands = self._elements(row, command, elements[start + 3 :])
                prefix = self._elements(row, command, elements[: start + 3])
                found.append(ModuleInvocation(role, module, operands, prefix))
        return found

    def _module(self, job, module, subcommand=None):
        matches = [
            item
            for item in self.invocations(_MODULE_ROOT + module)
            if item.role.job == job
            and (subcommand is None or item.operands[:1] == (subcommand,))
        ]
        return _unique(matches, f"{job}/{module}/{subcommand}")

    def diagnostics(self, job):
        """Associate evidence archives with their same-job raw uploads."""
        pairs = []
        for role in self.steps(job):
            if "run" not in role.step:
                continue
            for command in self.observations[(job, role.index)]["commands"]:
                if command["name"] != "Compress-Archive":
                    continue
                elements = command["elements"][1:]
                source = _flag(
                    [_literal(item) or item["text"] for item in elements],
                    "-Path",
                )
                if _path(source) != ".wdv3/evidence/*":
                    continue
                operands = tuple(_operand(item) for item in elements)
                destination = _path(_flag(operands, "-DestinationPath"))
                for upload in self.steps(job):
                    if (
                        upload.step.get("uses", "").startswith(
                            "actions/upload-artifact@"
                        )
                        and upload.step.get("with", {}).get("archive") is False
                        and _path(upload.step["with"]["path"]) == destination
                    ):
                        pairs.append(ArchiveUpload(role, upload, destination))
        return _unique(pairs, f"{job} diagnostic archive/upload")

    def sync_operands(self):
        """Bind each locked-sync flag to its original actual operand."""
        candidates = []
        for role in self.steps("observe"):
            if "run" not in role.step:
                continue
            for command in self.observations[(role.job, role.index)][
                "commands"
            ]:
                values = [_literal(item) for item in command["elements"]]
                if command["name"] == "uv" and values[1:2] == ["sync"]:
                    assert all(value is not None for value in values), command
                    candidates.append((role, values[2:]))
        role, operands = _unique(candidates, "profile sync")
        expected = {
            "--directory": "tooling",
            "--python": "3.13.12",
            "--package": "three-workflow-delivery-v3",
        }
        assert operands.count("--locked") == 1, operands
        assert len(operands) == 2 * len(expected) + 1, operands
        assert {flag: _flag(operands, flag) for flag in expected} == expected
        return role, tuple(operands)

    # Each branch represents one of the nine bounded workflow roles.
    def select(self, name, job=None):  # noqa: C901, PLR0911
        """Resolve exactly one of the nine accepted workflow roles."""
        if name in {"fixture.diagnostics", "probe.diagnostics"}:
            assert job is not None
            return self.diagnostics(job)
        if name == "fixture.prepare":
            return self._module("prepare", "nuget_preparation", "prepare").role
        if name == "probe.publish":
            return self._module("publish", "nuget_probe", "publish").role
        if name == "profile.observe":
            return self._module("observe", "nuget_profile").role
        if name == "profile.sync":
            return self.sync_operands()[0]
        if name == "profile.seal-upload":
            seal = _unique(
                [
                    role
                    for role in self.steps("observe")
                    if role.step.get("id") == "seal"
                ],
                "profile seal",
            )
            upload = _unique(
                [
                    role
                    for role in self.steps("observe")
                    if role.step.get("uses", "").startswith(
                        "actions/upload-artifact@"
                    )
                    and role.step.get("with", {}).get("name")
                    == "${{ steps.seal.outputs.name }}"
                    and role.step["with"].get("path")
                    == "${{ steps.seal.outputs.path }}"
                ],
                "profile seal consumer",
            )
            return ArchiveUpload(seal, upload, "${{ steps.seal.outputs.path }}")
        if name == "probe.reference":
            publish = self._module("publish", "nuget_probe", "publish")
            path = _path(_flag(publish.operands, "--reference"))
            writers = [
                role
                for role in self.steps("publish")
                if "run" in role.step
                for writer in self.observations[(role.job, role.index)][
                    "writers"
                ]
                if _path(_operand(writer["path"])) == path
            ]
            return _unique(writers, "probe publication reference writer")
        assert name == "profile.context", name
        writers = []
        for role in self.steps("observe"):
            if "run" not in role.step:
                continue
            for command in self.observations[(role.job, role.index)][
                "commands"
            ]:
                if command["name"] == "Set-Content":
                    operands = [
                        _literal(item) or item["text"]
                        for item in command["elements"][1:]
                    ]
                    if (
                        _flag(operands, "-LiteralPath")
                        == ".wdv3/evidence/platform.json"
                    ):
                        writers.append(role)
        return _unique(writers, "public platform context writer")

    def context_predecessors(self):
        """Retain context before every current fallible preparation role."""
        context = self.select("profile.context")
        later = [
            role
            for role in self.steps("observe")
            if role.step.get("uses", "").startswith(
                ("actions/checkout@", "astral-sh/setup-uv@", "actions/setup-")
            )
        ]
        later.extend(
            [
                self.select("profile.sync"),
                self.select("profile.observe"),
                self.select("profile.seal-upload").archive,
            ]
        )
        assert all(context.index < role.index for role in later), (
            context,
            later,
        )
        return tuple((context.index, role.index) for role in later)


def project_workflow(workflow, directory: Path) -> WorkflowRoles:
    """Parse every original run scalar once; never execute workflow source."""
    inputs = []
    for job, definition in workflow["jobs"].items():
        for index, step in enumerate(definition["steps"]):
            if "run" not in step:
                continue
            shell = step.get(
                "shell",
                definition.get("defaults", {})
                .get("run", {})
                .get(
                    "shell",
                    workflow.get("defaults", {}).get("run", {}).get("shell"),
                ),
            )
            assert shell == "pwsh", (job, index, shell)
            inputs.append({"job": job, "index": index, "source": step["run"]})
    executable = shutil.which("pwsh")
    assert executable, "The repository's PowerShell parser is required"
    directory.mkdir(parents=True, exist_ok=True)
    input_path = directory / "workflow-source.json"
    output_path = directory / "workflow-projection.json"
    script_path = directory / "workflow-parser.ps1"
    input_path.write_text(json.dumps(inputs), encoding="utf-8")
    script_path.write_text(_PARSER, encoding="utf-8")
    subprocess.run(  # noqa: S603
        [
            executable,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(script_path),
            str(input_path),
            str(output_path),
        ],
        cwd=directory,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    return WorkflowRoles(
        workflow, json.loads(output_path.read_text(encoding="utf-8"))
    )
