"""Proof wrappers and replay classifiers for Three workflow releases."""

from __future__ import annotations

from three_workflow_release_proof.proof import (
    ProofError,
    classify_github_release_observations,
    classify_immutable_observations,
    github_release_asset_proofs,
    immutable_proofs,
)

__all__ = [
    "ProofError",
    "classify_github_release_observations",
    "classify_immutable_observations",
    "github_release_asset_proofs",
    "immutable_proofs",
]
