"""Azure Artifacts Python endpoint validation."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from unicodedata import category
from urllib.parse import SplitResult, unquote, urlsplit

DIRECT_FEED_SEGMENT_COUNT = 4
PROJECT_FEED_SEGMENT_COUNT = 5
CONTROL_CHARACTER_CATEGORY = "Cc"
SUPPORTED_PYPI_ENDPOINT_KINDS = {"simple", "upload"}
RECOGNIZED_MODERN_HOSTS = {"pkgs.dev.azure.com", "dev.azure.com"}
RECOGNIZED_LEGACY_HOST_SUFFIXES = (
    ".pkgs.visualstudio.com",
    ".visualstudio.com",
)


class EndpointStatus(Enum):
    """Python feed endpoint classification."""

    SUPPORTED = "supported"
    UNSUPPORTED = "unsupported"
    INVALID = "invalid"


@dataclass(frozen=True)
class EndpointCheck:
    """Result of classifying a keyring service URL."""

    status: EndpointStatus
    host: str | None = None
    organization: str | None = None
    project: str | None = None
    feed: str | None = None


def classify_python_feed_endpoint(service: str) -> EndpointCheck:
    """Classify a service URL for Python keyring behavior."""
    if _contains_control_character(service):
        return EndpointCheck(EndpointStatus.INVALID)

    try:
        parsed = urlsplit(service)
    except ValueError:
        status = (
            EndpointStatus.INVALID
            if _targets_recognized_azure_domain(service)
            else EndpointStatus.UNSUPPORTED
        )
        result = EndpointCheck(status)
    else:
        result = _classify_parsed_endpoint(parsed)

    return result


def _classify_parsed_endpoint(parsed: SplitResult) -> EndpointCheck:
    host = parsed.hostname.lower() if parsed.hostname else None
    if host is None:
        status = (
            EndpointStatus.INVALID
            if _targets_recognized_azure_domain(parsed.path)
            else EndpointStatus.UNSUPPORTED
        )
        return EndpointCheck(status)

    legacy_organization = _legacy_organization(host)
    if host not in RECOGNIZED_MODERN_HOSTS and legacy_organization is None:
        return EndpointCheck(EndpointStatus.UNSUPPORTED, host=host)

    try:
        port = parsed.port
    except ValueError:
        port = -1

    if (
        parsed.scheme.lower() != "https"
        or port not in {None, 443}
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
    ):
        return EndpointCheck(EndpointStatus.INVALID, host=host)

    segments = _decode_path_segments(parsed.path)
    if segments is None:
        return EndpointCheck(EndpointStatus.INVALID, host=host)

    if legacy_organization is None:
        return _classify_modern_host(host, segments)

    return _classify_legacy_host(host, legacy_organization, segments)


def _classify_modern_host(host: str, segments: list[str]) -> EndpointCheck:
    if not segments or not segments[0].strip():
        return EndpointCheck(EndpointStatus.INVALID, host=host)

    organization = segments[0]
    shape = _parse_python_feed_segments(segments[1:])
    if shape is None:
        return EndpointCheck(EndpointStatus.INVALID, host=host)

    project, feed = shape
    return EndpointCheck(
        EndpointStatus.SUPPORTED,
        host=host,
        organization=organization,
        project=project,
        feed=feed,
    )


def _classify_legacy_host(
    host: str,
    organization: str,
    segments: list[str],
) -> EndpointCheck:
    resource_segments = (
        segments[1:]
        if segments and segments[0].lower() == "defaultcollection"
        else segments
    )
    shape = _parse_python_feed_segments(resource_segments)
    if shape is None:
        return EndpointCheck(EndpointStatus.INVALID, host=host)

    project, feed = shape
    return EndpointCheck(
        EndpointStatus.SUPPORTED,
        host=host,
        organization=organization,
        project=project,
        feed=feed,
    )


def _parse_python_feed_segments(
    segments: list[str],
) -> tuple[str | None, str] | None:
    if (
        len(segments) == DIRECT_FEED_SEGMENT_COUNT
        and segments[0].lower() == "_packaging"
        and segments[2].lower() == "pypi"
        and segments[3].lower() in SUPPORTED_PYPI_ENDPOINT_KINDS
        and segments[1].strip()
    ):
        return None, segments[1]

    if (
        len(segments) == PROJECT_FEED_SEGMENT_COUNT
        and segments[1].lower() == "_packaging"
        and segments[3].lower() == "pypi"
        and segments[4].lower() in SUPPORTED_PYPI_ENDPOINT_KINDS
        and segments[0].strip()
        and segments[2].strip()
    ):
        return segments[0], segments[2]

    return None


def _decode_path_segments(path: str) -> list[str] | None:
    raw_segments = (
        path[1:].split("/") if path.startswith("/") else path.split("/")
    )
    if raw_segments == [""]:
        return []

    if raw_segments and raw_segments[-1] == "":
        raw_segments = raw_segments[:-1]

    segments: list[str] = []
    for raw_segment in raw_segments:
        decoded = unquote(raw_segment)
        if (
            not decoded
            or _contains_control_character(decoded)
            or "/" in decoded
            or "\\" in decoded
        ):
            return None
        segments.append(decoded)

    return segments


def _legacy_organization(host: str) -> str | None:
    for suffix in RECOGNIZED_LEGACY_HOST_SUFFIXES:
        if host.endswith(suffix) and len(host) > len(suffix):
            organization = host[: -len(suffix)]
            if organization.strip() and "." not in organization:
                return organization

    return None


def _targets_recognized_azure_domain(service: str) -> bool:
    candidate = service.strip().casefold()
    if "://" in candidate:
        candidate = candidate.split("://", maxsplit=1)[1]
    candidate = candidate.split("/", maxsplit=1)[0]
    candidate = candidate.split("?", maxsplit=1)[0]
    candidate = candidate.split("#", maxsplit=1)[0]
    candidate = candidate.rsplit("@", maxsplit=1)[-1]
    if candidate.startswith("["):
        candidate = candidate[1:].split("]", maxsplit=1)[0]
    else:
        candidate = candidate.split(":", maxsplit=1)[0]
    candidate = candidate.rstrip(".")

    if candidate in RECOGNIZED_MODERN_HOSTS:
        return True
    return any(
        candidate.endswith(suffix) for suffix in RECOGNIZED_LEGACY_HOST_SUFFIXES
    )


def _contains_control_character(value: str) -> bool:
    return any(
        category(character) == CONTROL_CHARACTER_CATEGORY for character in value
    )
