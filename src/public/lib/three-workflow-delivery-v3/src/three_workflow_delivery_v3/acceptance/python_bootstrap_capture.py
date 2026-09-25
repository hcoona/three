"""Exact first-project observations and single-send original upload sequence."""

from __future__ import annotations

from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_contract import require
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_INDEX_BYTES,
    PythonHttpResponse,
    PythonRegistry,
    read_python_index,
    upload_python_once,
)
from three_workflow_delivery_v3.canonical import parse_json_strict

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.pypi import PythonHttpTransport
    from three_workflow_delivery_v3.adapters.python import PythonDistribution

REGISTRY = PythonRegistry("testpypi")


def require_absent(http: PythonHttpTransport) -> None:
    """P0/P1 require the exact project-level 404 prerequisite."""
    response = http.request(
        "GET",
        REGISTRY.index_url,
        {"Accept": "application/vnd.pypi.simple.v1+json"},
        None,
        MAX_INDEX_BYTES,
    )
    require(
        response.status == HTTPStatus.NOT_FOUND,
        "bootstrap project is not absent",
    )


class _IndexReadback:
    def __init__(
        self, response: PythonHttpResponse, http: PythonHttpTransport
    ) -> None:
        self.response = response
        self.http = http
        self.used = False

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        if not self.used:
            require(
                method == "GET"
                and url == REGISTRY.index_url
                and maximum_bytes == MAX_INDEX_BYTES,
                "bootstrap index readback differs",
            )
            self.used = True
            return self.response
        return self.http.request(method, url, headers, body, maximum_bytes)


def capture_exact(
    http: PythonHttpTransport, originals: tuple[PythonDistribution, ...]
) -> tuple[PythonDistribution, ...]:
    """Reject any foreign project inventory before downloading an exact set."""
    response = http.request(
        "GET",
        REGISTRY.index_url,
        {"Accept": "application/vnd.pypi.simple.v1+json"},
        None,
        MAX_INDEX_BYTES,
    )
    require(
        response.status == HTTPStatus.OK,
        "bootstrap project readback unavailable",
    )
    document = parse_json_strict(response.body)
    require(
        isinstance(document, dict) and isinstance(document.get("files"), list),
        "bootstrap project inventory unavailable",
    )
    entries = cast("dict", document)["files"]
    require(
        len(entries) == len(originals)
        and all(isinstance(entry, dict) for entry in entries)
        and {entry.get("filename") for entry in entries}
        == {item.filename for item in originals},
        "bootstrap complete project inventory differs",
    )
    observation = read_python_index(
        REGISTRY, originals[0].witness, _IndexReadback(response, http)
    )
    require(
        {item.filename: item.content for item in observation.files}
        == {item.filename: item.content for item in originals},
        "bootstrap downloaded originals differ",
    )
    return observation.files


def upload_pair(
    http: PythonHttpTransport,
    originals: dict[str, PythonDistribution],
    token: str,
) -> None:
    """U1/P2 must succeed before U2/P3; no duplicate success or repair."""
    wheel, sdist = originals["wheel"], originals["sdist"]
    for item, expected in ((wheel, (wheel,)), (sdist, (wheel, sdist))):
        response = upload_python_once(REGISTRY, item, token, http)
        require(
            response.status == HTTPStatus.OK
            and response.classification == "definitive-success",
            "bootstrap original upload failed or ambiguous",
        )
        capture_exact(http, expected)
