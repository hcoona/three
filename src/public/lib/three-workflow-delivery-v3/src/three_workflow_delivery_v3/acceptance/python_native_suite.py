"""Fixed ten-upload Python acceptance suite and supplied-fact audit."""

from __future__ import annotations

import threading
import time
from concurrent.futures import ThreadPoolExecutor
from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_capture import (
    Capture,
    NativeTransport,
    collect_capture,
    replay_capture,
)
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    NativeRequest,
    require,
    write_exclusive,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    NativeFixtures,
    consumer_evidence,
)
from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpResponse,
    PythonHttpTransport,
    upload_python_once,
)
from three_workflow_delivery_v3.adapters.python import (
    PythonConsumerResult,
    PythonDistribution,
    qualify_python_consumer,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

_CREATION_STEPS = 2
_LAST_DUPLICATE_STEP = 6
_FIRST_RACE_STEP = 7
_RACE_WIDTH = 2
_UPLOAD_COUNT = 10
_DOWNLOAD_COUNT = 18
_FINAL_FILES = 4
_DEADLINE_SECONDS = 600

SCHEDULE = (
    ("a/original/wheel",),
    ("a/original/sdist",),
    ("a/original/wheel",),
    ("a/original/sdist",),
    ("a/comparison/wheel",),
    ("a/comparison/sdist",),
    ("b/original/wheel", "b/comparison/wheel"),
    ("b/original/sdist", "b/comparison/sdist"),
)


def duplicate_response(response: PythonHttpResponse) -> bool:
    """Require explicit filename-reuse evidence, never generic HTTP failure."""
    body = response.body.lower()
    return response.status == HTTPStatus.BAD_REQUEST and (
        b"file already exists" in body
        or b"https://pypi.org/help/#file-name-reuse" in body
        or b"https://test.pypi.org/help/#file-name-reuse" in body
    )


def _marker(
    request: NativeRequest, fixtures: NativeFixtures, ordinal: int
) -> dict[str, JsonValue]:
    return {
        "request-digest": request.digest,
        "ordinal": ordinal,
        "files": [
            {
                "key": key,
                "filename": fixtures.distributions[key].filename,
                "digest": fixtures.distributions[key].digest,
            }
            for key in SCHEDULE[ordinal - 1]
        ],
    }


def _initial(capture: Capture, fixtures: NativeFixtures) -> None:
    names = {item.filename for item in fixtures.distributions.values()}
    require(
        not capture.distributions and not names.intersection(capture.inventory),
        "acceptance source versions are not absent",
    )


def validate_step(
    ordinal: int,
    before: Capture,
    after: Capture,
    results: tuple[tuple[dict[str, JsonValue], PythonHttpResponse], ...],
    fixtures: NativeFixtures,
) -> None:
    """Share the fixed success/duplicate/winner contract with offline replay."""
    keys = SCHEDULE[ordinal - 1]
    require(len(results) == len(keys), "incomplete acceptance upload step")
    winners = []
    for key, (record, response) in zip(keys, results, strict=True):
        item = fixtures.distributions[key]
        require(
            record["key"] == key
            and record["digest"] == item.digest
            and record["status"] == response.status
            and type(record["start"]) in {int, float}
            and type(record["finish"]) in {int, float}
            and 0
            <= cast("float", record["start"])
            < cast("float", record["finish"]),
            "upload response or interval binding differs",
        )
        if response.status == HTTPStatus.OK:
            winners.append(item)
        else:
            require(
                duplicate_response(response),
                "upload is not definitive filename-duplicate evidence",
            )
    if ordinal <= _CREATION_STEPS:
        require(len(winners) == 1, "initial upload did not succeed")
    elif ordinal <= _LAST_DUPLICATE_STEP:
        require(not winners, "duplicate filename was accepted")
    else:
        require(len(winners) == 1, "race needs one success and one duplicate")
        require(
            max(cast("float", r[0]["start"]) for r in results)
            < min(cast("float", r[0]["finish"]) for r in results),
            "competing requests did not overlap",
        )
    additions = {item.filename: item.digest for item in winners}
    require(
        not additions.keys() & before.inventory.keys(),
        "successful upload replaced an existing filename",
    )
    require(
        set(after.inventory) == set(before.inventory) | additions.keys()
        and all(
            after.inventory.get(name) == entry
            for name, entry in before.inventory.items()
        ),
        "unexpected complete inventory mutation",
    )
    expected = {
        item.filename: item.digest for item in before.distributions
    } | additions
    require(
        {item.filename: item.digest for item in after.distributions}
        == expected,
        "readback did not preserve original or winner bytes",
    )


class _UploadRecorder:
    def __init__(
        self,
        transport: NativeTransport,
        key: str,
        distribution: PythonDistribution,
        barrier: threading.Barrier | None,
    ) -> None:
        self.transport = transport
        self.barrier = barrier
        self.response: PythonHttpResponse | None = None
        self.record: dict[str, JsonValue] = {
            "key": key,
            "digest": distribution.digest,
            "start": None,
            "finish": None,
            "status": None,
        }

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        if self.barrier is not None:
            self.barrier.wait(timeout=30)
        self.record["start"] = self.transport.clock()
        try:
            self.response = self.transport.request(
                method, url, headers, body, maximum_bytes
            )
            self.record["status"] = self.response.status
            return self.response
        finally:
            self.record["finish"] = self.transport.clock()
            actual = getattr(self.transport.local, "call", None)
            if actual is not None:
                self.record["start"] = actual["start"]
                self.record["finish"] = actual["finish"]


def run_suite(  # noqa: C901, PLR0913, PLR0915 - fixed protocol with bounded external seams
    request: NativeRequest,
    fixtures: NativeFixtures,
    transport: PythonHttpTransport,
    token: str,
    output: Path,
    *,
    secrets: tuple[str, ...] = (),
    clock: Callable[[], float] = time.monotonic,
    deadline: float | None = None,
) -> dict[str, bytes]:
    """Execute the fixed schedule once, retaining partial facts on any stop."""
    fixtures.match(request)
    output.mkdir(parents=True, exist_ok=False)
    native = NativeTransport(
        request.registry,
        transport,
        secrets=(*secrets, token),
        clock=clock,
        deadline=deadline,
    )
    witnesses = (
        fixtures.distributions["a/original/wheel"].witness,
        fixtures.distributions["b/original/wheel"].witness,
    )
    files: dict[str, bytes] = {}

    def retain(name: str, data: bytes) -> None:
        if name in files:
            require(files[name] == data, "retained acceptance fact changed")
            return
        write_exclusive(output, name, data)
        files[name] = data

    try:
        retain("request.json", request.content)
        retain(
            "budget.json",
            canonicalize(
                {"deadline": native.deadline, "start": native.clock()}
            ),
        )
        before = collect_capture(
            request.registry, witnesses, native, retain=retain, ordinal=0
        )
        for name, content in before.files(0).items():
            retain(name, content)
        _initial(before, fixtures)
        for ordinal, keys in enumerate(SCHEDULE, 1):
            retain(
                f"upload/step-{ordinal}.marker.json",
                canonicalize(_marker(request, fixtures, ordinal)),
            )
            barrier = threading.Barrier(2) if len(keys) == _RACE_WIDTH else None
            recorders = tuple(
                _UploadRecorder(
                    native, key, fixtures.distributions[key], barrier
                )
                for key in keys
            )

            def send(recorder: _UploadRecorder) -> None:
                upload_python_once(
                    request.registry,
                    fixtures.distributions[cast("str", recorder.record["key"])],
                    token,
                    recorder,
                )

            if barrier is None:
                send(recorders[0])
            else:
                with ThreadPoolExecutor(max_workers=2) as pool:
                    futures = [
                        pool.submit(send, recorder) for recorder in recorders
                    ]
                    for future in futures:
                        future.result()
            results = []
            for number, recorder in enumerate(recorders):
                prefix = f"upload/step-{ordinal}-{number}"
                retain(prefix + ".json", canonicalize(recorder.record))
                if recorder.response is not None:
                    retain(prefix + ".body", recorder.response.body)
                    results.append((recorder.record, recorder.response))
            require(len(results) == len(keys), "ambiguous acceptance upload")
            # Reject failed upload outcomes before spending another read budget.
            statuses = [response.status for _, response in results]
            require(
                all(
                    response.status == HTTPStatus.OK
                    or duplicate_response(response)
                    for _, response in results
                ),
                "unrecognized acceptance upload outcome",
            )
            require(
                statuses.count(200)
                == (
                    1
                    if ordinal <= _CREATION_STEPS or ordinal >= _FIRST_RACE_STEP
                    else 0
                ),
                "unexpected acceptance upload outcome",
            )
            if ordinal >= _FIRST_RACE_STEP:
                require(
                    max(cast("float", r[0]["start"]) for r in results)
                    < min(cast("float", r[0]["finish"]) for r in results),
                    "competing requests did not overlap",
                )
            after = collect_capture(
                request.registry,
                witnesses,
                native,
                retain=retain,
                ordinal=ordinal,
            )
            for name, content in after.files(ordinal).items():
                retain(name, content)
            validate_step(ordinal, before, after, tuple(results), fixtures)
            before = after
        require(
            native.counts == {"upload": 10, "index": 9, "file": 18},
            "acceptance effect totals differ",
        )
        retain("requests.json", canonicalize(cast("JsonValue", native.calls)))
        retain(
            "suite.json",
            canonicalize(
                {
                    "request-digest": request.digest,
                    "counts": cast("JsonValue", native.counts),
                    "result": "supplied-facts-pass",
                }
            ),
        )
    except Exception:  # noqa: BLE001 - never retain credential-bearing exceptions
        retain(
            "failure.json",
            canonicalize(
                {
                    "result": "spent-possibly-mutated",
                    "category": "acceptance-step-failed",
                    "counts": cast("JsonValue", native.counts),
                }
            ),
        )
        msg = "Python native acceptance stopped; generation remains spent"
        raise ValueError(msg) from None
    return files


def _audit_timing(files: dict[str, bytes]) -> None:
    """Check finite timing, effect order and the two admitted race pairs."""
    budget = parse_canonical_json(files["budget.json"])
    require(
        type(budget["deadline"]) in {int, float}
        and type(budget["start"]) in {int, float},
        "invalid acceptance timing type",
    )
    deadline = cast("float", budget["deadline"])
    start = cast("float", budget["start"])
    require(
        0 < deadline - start <= _DEADLINE_SECONDS, "invalid acceptance deadline"
    )
    requests = parse_json_strict(files["requests.json"])
    require(
        isinstance(requests, list)
        and len(requests) == _UPLOAD_COUNT + _DOWNLOAD_COUNT + 9,
        "incomplete request timing evidence",
    )
    counts = {"upload": 0, "index": 0, "file": 0}
    for item in cast("list[dict[str, JsonValue]]", requests):
        require(
            set(item) == {"kind", "start", "finish"}
            and type(item["start"]) in {int, float}
            and type(item["finish"]) in {int, float},
            "invalid request timing fields",
        )
        kind = cast("str", item["kind"])
        begin = cast("float", item["start"])
        finish = cast("float", item["finish"])
        require(
            kind in counts
            and start <= begin < deadline
            and begin <= finish <= deadline + 30,
            "request exceeded its native deadline",
        )
        counts[kind] += 1
    require(
        counts == {"upload": 10, "index": 9, "file": 18},
        "request budget evidence differs",
    )
    expected_kinds = ["index"]
    for ordinal, keys in enumerate(SCHEDULE, 1):
        expected_kinds.extend(["upload"] * len(keys))
        expected_kinds.extend(
            ["index"] + ["file"] * min(ordinal, 2)
            if ordinal < _FIRST_RACE_STEP
            else ["index"] + ["file"] * (ordinal - 4)
        )
    timing = cast("list[dict[str, JsonValue]]", requests)
    require(
        [item["kind"] for item in timing] == expected_kinds,
        "native request schedule differs",
    )
    for index, item in enumerate(timing):
        for prior in timing[:index]:
            if cast("float", prior["finish"]) > cast("float", item["start"]):
                require(
                    prior["kind"] == item["kind"] == "upload"
                    and index > 0
                    and prior is timing[index - 1],
                    "unadmitted request concurrency",
                )


def audit_suite(
    request: NativeRequest,
    fixtures: NativeFixtures,
    files: dict[str, bytes],
    *,
    consumer: Callable[
        [PythonDistribution], PythonConsumerResult
    ] = qualify_python_consumer,
) -> dict[str, bytes]:
    """Replay supplied facts, then freshly clean-consume both final pairs."""
    fixtures.match(request)
    require(
        "failure.json" not in files
        and files["request.json"] == request.content,
        "failed or foreign acceptance evidence",
    )
    _audit_timing(files)
    summary = parse_canonical_json(files["suite.json"])
    require(
        summary
        == {
            "request-digest": request.digest,
            "counts": {"upload": 10, "index": 9, "file": 18},
            "result": "supplied-facts-pass",
        },
        "incomplete acceptance suite",
    )
    witnesses = (
        fixtures.distributions["a/original/wheel"].witness,
        fixtures.distributions["b/original/wheel"].witness,
    )
    before = replay_capture(files, 0, request.registry, witnesses)
    _initial(before, fixtures)
    downloads = 0
    uploads = 0
    expected_paths = {
        "request.json",
        "suite.json",
        "requests.json",
        "budget.json",
        *before.files(0),
    }
    for ordinal, keys in enumerate(SCHEDULE, 1):
        require(
            parse_canonical_json(files[f"upload/step-{ordinal}.marker.json"])
            == _marker(request, fixtures, ordinal),
            "missing or foreign started marker",
        )
        expected_paths.add(f"upload/step-{ordinal}.marker.json")
        results = []
        for number, _key in enumerate(keys):
            prefix = f"upload/step-{ordinal}-{number}"
            expected_paths.update((prefix + ".json", prefix + ".body"))
            record = parse_canonical_json(files[prefix + ".json"])
            results.append(
                (
                    record,
                    PythonHttpResponse(
                        cast("int", record["status"]),
                        files[prefix + ".body"],
                        "",
                    ),
                )
            )
            uploads += 1
        after = replay_capture(files, ordinal, request.registry, witnesses)
        expected_paths.update(after.files(ordinal))
        downloads += len(after.downloads)
        validate_step(ordinal, before, after, tuple(results), fixtures)
        before = after
    require(
        uploads == _UPLOAD_COUNT and downloads == _DOWNLOAD_COUNT,
        "replayed acceptance effect totals differ",
    )
    require(
        set(files) == expected_paths, "unexpected acceptance evidence inventory"
    )
    result = {}
    for item in before.distributions:
        proof = consumer(item)
        require(
            proof.original_digest == item.digest
            and proof.variant == item.variant,
            "fresh consumer proof differs",
        )
        result[f"consumer/{item.filename}.json"] = consumer_evidence(proof)
    require(len(result) == _FINAL_FILES, "four fresh native consumers required")
    result["audit.json"] = canonicalize(
        {
            "request-digest": request.digest,
            "result": "supplied-facts-pass",
            "native-admission": False,
            "reason": (
                "Independent native provenance and configuration "
                "audit required."
            ),
        }
    )
    return result
