"""Closed modeled review bytes do not manufacture independent provenance."""

from datetime import timedelta

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_configuration import (
    validate_ruby_bootstrap_review,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_fixtures import (
    bootstrap_request,
    configuration,
    inspection,
    review_document,
)
from .ruby_operation_fixtures import admission_document, instant


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_bootstrap_review_binds_exact_request_inspection_and_admission(
    destination,
):
    """Bootstrap review binds exact request inspection and admission."""
    config = configuration(destination)
    inspected = inspection(config)
    request = bootstrap_request(config)
    doc = review_document(request, config, inspected)
    review = canonicalize(doc)
    admission = canonicalize(admission_document(request, review=review))
    assert (
        validate_ruby_bootstrap_review(
            review,
            request,
            config,
            inspected,
            admission,
            now=NOW + timedelta(minutes=1),
        )
        == doc
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "other"),
        *[
            (key, "sha256:" + "e" * 64)
            for key in (
                "request-digest",
                "configuration-digest",
                "first-project-inspection-digest",
                "caller-digest",
                "protocol-digest",
            )
        ],
        ("reviewer", "different-reviewer"),
        ("author", "different-author"),
        ("reviewed-at", instant(NOW)),
        ("verdict", "rejected"),
        (
            "carrier",
            "https://github.com/hcoona/three/issues/954#issuecomment-456",
        ),
    ],
)
def test_bootstrap_review_rejects_wrong_join_even_with_matching_byte_digest(
    field, value
):
    """Bootstrap review rejects wrong join even with matching byte digest."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    doc = review_document(request, config, inspected)
    doc[field] = value
    review = canonicalize(doc)
    admission = canonicalize(admission_document(request, review=review))
    with pytest.raises(ValueError, match="binding differs"):
        validate_ruby_bootstrap_review(
            review,
            request,
            config,
            inspected,
            admission,
            now=NOW + timedelta(minutes=2),
        )


@pytest.mark.parametrize("mode", ["extra", "missing", "noncanonical"])
def test_bootstrap_review_requires_closed_canonical_bytes(mode):
    """Bootstrap review requires closed canonical bytes."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    doc = review_document(request, config, inspected)
    if mode == "extra":
        doc["trusted"] = True
    elif mode == "missing":
        doc.pop("carrier")
    review = canonicalize(doc) + (b"\n" if mode == "noncanonical" else b"")
    admission = canonicalize(admission_document(request, review=review))
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        validate_ruby_bootstrap_review(
            review,
            request,
            config,
            inspected,
            admission,
            now=NOW + timedelta(minutes=2),
        )


def test_bootstrap_review_rejects_replaced_original_review_bytes():
    """Bootstrap review rejects replaced original review bytes."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    doc = review_document(request, config, inspected)
    admission = canonicalize(
        admission_document(request, review=canonicalize(doc))
    )
    doc["reviewer"] = "replacement"
    with pytest.raises(ValueError, match="closed authority"):
        validate_ruby_bootstrap_review(
            canonicalize(doc),
            request,
            config,
            inspected,
            admission,
            now=NOW + timedelta(minutes=2),
        )


def test_bootstrap_review_rejects_separate_configuration():
    """Bootstrap review rejects separate configuration."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    review = canonicalize(review_document(request, config, inspected))
    admission = canonicalize(admission_document(request, review=review))
    with pytest.raises(ValueError, match="different configuration"):
        validate_ruby_bootstrap_review(
            review,
            request,
            configuration("github-packages"),
            inspected,
            admission,
            now=NOW + timedelta(minutes=2),
        )


@pytest.mark.parametrize(
    ("offset", "reason"),
    [
        (timedelta(seconds=59), "not yet current"),
        (timedelta(hours=2), "inspection lifetime"),
    ],
)
def test_bootstrap_review_rejects_future_review_or_expired_request(
    offset, reason
):
    """Bootstrap review rejects future review or expired request."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    review = canonicalize(review_document(request, config, inspected))
    admission = canonicalize(admission_document(request, review=review))
    with pytest.raises(ValueError, match=reason):
        validate_ruby_bootstrap_review(
            review, request, config, inspected, admission, now=NOW + offset
        )


def test_bootstrap_review_cannot_relabel_self_review_as_independent():
    """Bootstrap review cannot relabel self review as independent."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    doc = review_document(request, config, inspected)
    doc["reviewer"] = doc["author"]
    review = canonicalize(doc)
    admission = admission_document(request, review=review)
    admission["reviewer"] = admission["author"]
    with pytest.raises(ValueError, match="independent admission"):
        validate_ruby_bootstrap_review(
            review,
            request,
            config,
            inspected,
            canonicalize(admission),
            now=NOW + timedelta(minutes=2),
        )
