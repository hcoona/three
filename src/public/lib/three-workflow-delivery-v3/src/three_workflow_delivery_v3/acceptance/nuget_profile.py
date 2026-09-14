"""Observe the current Windows NuGet profile without destination credentials.

Input hashes identify separately audited preflight evidence. They do not prove
its provenance, authorize execution, or establish native admission.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3 import canonical
from three_workflow_delivery_v3.acceptance.nuget_operator import _git
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from collections.abc import Mapping

WORKFLOW_PATH = ".github/workflows/workflow-delivery-v3-nuget-profile.yml"
_SPEC_LIMIT = 65535
_SOURCE = (
    "src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3"
)


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _digest(value: JsonValue, length: int = 64) -> str:
    _require(
        type(value) is str
        and re.fullmatch(rf"[0-9a-f]{{{length}}}", value) is not None,
        "invalid profile observation input digest",
    )
    return cast("str", value)


def _read_spec(
    content: bytes,
) -> tuple[dict[str, JsonValue], native.NuGetServiceResources]:
    _require(0 < len(content) <= _SPEC_LIMIT, "profile specification size")
    spec = parse_canonical_json(content)
    _require(
        set(spec)
        == {"toolingSha", "preflightSha256", "resources", "resourcesDigest"},
        "unexpected profile specification fields",
    )
    _digest(spec["toolingSha"], 40)
    _digest(spec["preflightSha256"])
    resources = spec["resources"]
    _require(type(resources) is dict, "profile resources must be an object")
    resources = cast("dict[str, JsonValue]", resources)
    _require(
        set(resources)
        == {"serviceIndexSha256", "packageBaseAddress", "packagePublish"},
        "unexpected captured resource fields",
    )
    _require(
        canonical_sha256(resources) == spec["resourcesDigest"],
        "captured resource digest mismatch",
    )
    admitted = native.nuget_service_resources_from_projection(
        {
            "packageBaseAddress": resources["packageBaseAddress"],
            "packagePublish": resources["packagePublish"],
        },
        index_sha256=_digest(resources["serviceIndexSha256"]),
    )
    _require(
        resources
        == {
            "serviceIndexSha256": admitted.index_sha256,
            "packageBaseAddress": admitted.package_base_address,
            "packagePublish": admitted.package_publish,
        },
        "captured resources are not the original normalized projection",
    )
    return spec, admitted


def _platform_context(
    tooling_sha: str, platform: Mapping[str, str]
) -> dict[str, JsonValue]:
    run_id = platform.get("GITHUB_RUN_ID", "")
    _require(
        re.fullmatch(r"[1-9][0-9]*", run_id) is not None,
        "profile observation requires a current run",
    )
    expected = {
        "GITHUB_REPOSITORY": native.NUGET_REPOSITORY,
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": tooling_sha,
        "GITHUB_WORKFLOW_SHA": tooling_sha,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW_PATH}@refs/heads/main",
        "GITHUB_RUN_ID": run_id,
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }
    _require(
        all(platform.get(key) == value for key, value in expected.items()),
        "profile observation platform binding mismatch",
    )
    return dict(expected)


def _admit_source(root: Path, tooling_sha: str) -> str:
    _require(
        _git(root, "rev-parse", "HEAD").decode().strip() == tooling_sha
        and not _git(root, "status", "--porcelain", "--untracked-files=all"),
        "profile observation requires its exact clean checkout",
    )
    package = root / _SOURCE
    _require(
        all(
            Path(module).resolve().is_relative_to(package.resolve())
            for module in (__file__, native.__file__, canonical.__file__)
        ),
        "profile observer imported outside its pinned checkout",
    )
    for path in package.rglob("*.py"):
        relative = path.relative_to(root).as_posix()
        _require(
            not path.is_symlink()
            and path.read_bytes()
            == _git(root, "show", f"{tooling_sha}:{relative}"),
            "profile observer source bytes mismatch",
        )
    return _git(root, "rev-parse", "HEAD^{tree}").decode().strip()


def observe_profile(
    content: bytes,
    *,
    checkout: Path,
    platform: Mapping[str, str],
    evidence: Path,
) -> Path:
    """Retain one current profile; external audit and dispatch gates remain."""
    evidence.mkdir(parents=True, exist_ok=False)
    try:
        spec, resources = _read_spec(content)
        tooling_sha = cast("str", spec["toolingSha"])
        context = _platform_context(tooling_sha, platform)
        tree = _admit_source(checkout.resolve(strict=True), tooling_sha)
        profile = native.nuget_operation_profile(resources)
        native.validate_nuget_operation_profile(profile)
        _require(
            profile["platform"] == "Windows"
            and profile["packagePublish"] == resources.package_publish,
            "profile observation runtime or endpoint mismatch",
        )
        files = {
            "spec.json": content,
            "resources.json": canonicalize(spec["resources"]),
            "operation-profile.json": canonicalize(profile),
        }
        for name, data in files.items():
            with (evidence / name).open("xb") as stream:
                stream.write(data)
        manifest = {
            "specDigest": canonical_sha256(spec),
            "resourcesDigest": spec["resourcesDigest"],
            "preflightSha256": spec["preflightSha256"],
            "toolingSha": tooling_sha,
            "toolingTree": tree,
            "platform": context,
            "operationProfileDigest": canonical_sha256(profile),
            "files": {
                name: "sha256:" + hashlib.sha256(data).hexdigest()
                for name, data in files.items()
            },
            "evidenceLevel": (
                "local Windows profile observation; original preflight and "
                "actual run/artifact provenance require independent audit"
            ),
            "nativeAdmissionEstablished": False,
        }
        pending = evidence / "observation.pending"
        with pending.open("xb") as stream:
            stream.write(canonicalize(manifest))
        completed = evidence / "observation.json"
        pending.rename(completed)
    except Exception as error:
        with (evidence / "failure.json").open("xb") as stream:
            stream.write(
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "inputSha256": hashlib.sha256(content).hexdigest(),
                        "nativeAdmissionEstablished": False,
                        "retryPermitted": False,
                    }
                )
            )
        raise
    return completed


def main(argv: list[str] | None = None) -> int:
    """Run only the credential-free profile observation entry."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spec", type=Path, required=True)
    parser.add_argument("--checkout", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    arguments = parser.parse_args(argv)
    try:
        with arguments.spec.open("rb") as stream:
            content = stream.read(_SPEC_LIMIT + 1)
        observe_profile(
            content,
            checkout=arguments.checkout,
            platform=os.environ,
            evidence=arguments.evidence,
        )
    except Exception:  # noqa: BLE001
        # Typed failure metadata is retained without dumping input or secrets.
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
