"""Command-line entry point for workflow-release proof helpers."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from three_workflow_release_contracts import (
    ArtifactNameInputs,
    artifact_name,
    github_release_asset_binding_json,
    immutable_binding_json,
)
from three_workflow_release_proof import (
    ProofError,
    classify_github_release_observations,
    classify_immutable_observations,
    github_release_asset_proofs,
    immutable_proofs,
)


def main() -> int:
    """Run the proof helper CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    immutable = subparsers.add_parser("immutable-proofs")
    immutable.add_argument("--plan", required=True)
    immutable.add_argument("--build-result", required=True)
    immutable.add_argument("--publish-node-id", required=True)
    immutable.add_argument("--run", required=True)
    immutable.add_argument("--build-result-artifact-name", required=True)
    immutable.add_argument("--build-result-artifact-id", type=int, required=True)
    immutable.add_argument("--bundle-artifact-name", required=True)
    immutable.add_argument("--out-dir", required=True)

    github = subparsers.add_parser("github-release-asset-proofs")
    github.add_argument("--publish-request", required=True)
    github.add_argument("--publish-result", required=True)
    github.add_argument("--run", required=True)
    github.add_argument("--out-dir", required=True)

    classify_immutable = subparsers.add_parser("classify-immutable")
    classify_immutable.add_argument("--plan", required=True)
    classify_immutable.add_argument("--remote-members", required=True)
    classify_immutable.add_argument("--proof", action="append", default=[])
    classify_immutable.add_argument("--proof-dir")
    classify_immutable.add_argument(
        "--build-result-receipt", action="append", default=[]
    )
    classify_immutable.add_argument("--build-result-receipt-dir")
    classify_immutable.add_argument("--out", required=True)

    classify_github = subparsers.add_parser("classify-github-release")
    classify_github.add_argument("--plan", required=True)
    classify_github.add_argument("--remote-releases", required=True)
    classify_github.add_argument("--proof", action="append", default=[])
    classify_github.add_argument("--proof-dir")
    classify_github.add_argument("--out", required=True)

    args = parser.parse_args()
    try:
        if args.command == "immutable-proofs":
            proofs = immutable_proofs(
                plan=_load_json(Path(args.plan)),
                build_result=_load_json(Path(args.build_result)),
                publish_node_id=args.publish_node_id,
                run=_load_json(Path(args.run)),
                build_result_artifact_name=args.build_result_artifact_name,
                build_result_artifact_id=args.build_result_artifact_id,
                bundle_artifact_name=args.bundle_artifact_name,
            )
            _write_proofs(Path(args.out_dir), proofs, "immutable-proof")
        elif args.command == "github-release-asset-proofs":
            proofs = github_release_asset_proofs(
                publish_request=_load_json(Path(args.publish_request)),
                publish_result=_load_json(Path(args.publish_result)),
                run=_load_json(Path(args.run)),
            )
            _write_proofs(Path(args.out_dir), proofs, "github-release-asset-proof")
        elif args.command == "classify-immutable":
            observations = classify_immutable_observations(
                plan=_load_json(Path(args.plan)),
                remote_members=_load_json(Path(args.remote_members)),
                proofs=_load_proofs(args.proof, args.proof_dir),
                build_result_receipts=_load_proofs(
                    args.build_result_receipt,
                    args.build_result_receipt_dir,
                ),
            )
            _write_json(Path(args.out), observations)
        elif args.command == "classify-github-release":
            observations = classify_github_release_observations(
                plan=_load_json(Path(args.plan)),
                remote_releases=_load_json(Path(args.remote_releases)),
                proofs=_load_proofs(args.proof, args.proof_dir),
            )
            _write_json(Path(args.out), observations)
    except (OSError, json.JSONDecodeError, ProofError) as exc:
        sys.stderr.write(f"{exc}\n")
        return 1
    return 0


def _load_json(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        msg = f"{path} must contain a JSON object"
        raise ProofError(msg)
    return document


def _load_proofs(paths: list[str], proof_dir: str | None) -> list[dict[str, Any]]:
    proof_paths = [Path(path) for path in paths]
    if proof_dir:
        proof_paths.extend(sorted(Path(proof_dir).glob("*.json")))
    return [_load_json(path) for path in proof_paths]


def _write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def _write_proofs(out_dir: Path, proofs: list[dict[str, object]], kind: str) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    for proof in proofs:
        run = proof["run"]
        binding = proof["binding"]
        assert isinstance(run, dict)
        assert isinstance(binding, dict)
        run_id = int(run["run-id"])
        attempt = int(run["run-attempt"])
        if kind == "immutable-proof":
            binding_json = immutable_binding_json(
                publish_node_id=str(binding["publish-node-id"]),
                artifact_id=str(binding["artifact-id"]),
                package_name=str(binding["package-name"]),
                version=str(binding["version"]),
            )
        else:
            binding_json = github_release_asset_binding_json(
                publish_node_id=str(binding["publish-node-id"]),
                artifact_id=str(binding["artifact-id"]),
                release_tag=str(binding["release-tag"]),
                asset_name=str(binding["asset-name"]),
            )
        name = artifact_name(
            kind,  # type: ignore[arg-type]
            ArtifactNameInputs(
                run_id=run_id,
                attempt=attempt,
                binding_json=binding_json,
            ),
        )
        _write_json(out_dir / f"{name}.json", proof)


if __name__ == "__main__":
    raise SystemExit(main())
