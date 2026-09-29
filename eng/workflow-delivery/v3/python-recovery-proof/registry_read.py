# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native reader contracts retain their dynamic external schema.
# ruff: noqa: ANN401, E501, PLR0913, PLR2004, S101, PT018, TRY300
"""Scoped GET-only registry audit shared by new callers and a read continuation."""

import base64
import hashlib
import http.client
import ssl
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

from campaign import digest, save_json
from campaign import gate as read_gate
from proof_checks import diagnostic_gate
from read_policy import ReadPacer, ReadPending, ReadStopped
from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpResponse,
    read_python_index,
)
from three_workflow_delivery_v3.canonical import parse_json_strict


def successful_index(result: dict) -> bytes | None:
    """Recover only an explicitly successful retained runtime readback index."""
    selected = None
    for operation in result.get("operations", []):
        if (
            operation.get("status") == "succeeded"
            and operation.get("readback-exact") is True
        ):
            phase = operation["observation"]["phase"]
            assert phase["terminal"] == "exact"
            response = phase["reads"][-1]["response"]
            raw = base64.b64decode(response["body"], validate=True)
            assert response["status"] == 200
            assert (
                response["digest"]
                == "sha256:" + hashlib.sha256(raw).hexdigest()
            )
            selected = raw
    return selected


def classify(
    observation: Any,
    expected: tuple,
    baseline: bytes | None,
    *,
    diagnostic: bool = False,
) -> str:
    """Require exact bytes; accept only demonstrable regression as pending."""
    assert all(item in expected for item in observation.files), (
        "conflicting target distribution"
    )
    if observation.files == expected:
        return "complete"
    if diagnostic and baseline is None:
        return "complete"
    assert baseline is not None, (
        "incompatible state without successful baseline"
    )
    before = parse_json_strict(baseline)
    baseline_files = {item["filename"]: item for item in before["files"]}
    missing = {item.filename for item in expected} - {
        item.filename for item in observation.files
    }
    if diagnostic:
        known = {item.filename for item in expected} & baseline_files.keys()
        assert known, "successful baseline lacks admitted target files"
        missing &= known
        if not missing:
            return "complete"
    response = observation.index_response
    assert response is not None and response.status == 200, (
        "unexplained target disappearance"
    )
    current = parse_json_strict(response.body)
    older = current["meta"].get("_last-serial")
    newer = before["meta"].get("_last-serial")
    assert type(older) is int and type(newer) is int and older < newer, (
        "non-regressed incompatible target state"
    )
    # A prior successful index must actually contain the missing expected file.
    assert missing and missing <= baseline_files.keys(), (
        "serial alone cannot establish target regression"
    )
    for item in expected:
        if item.filename in missing:
            assert baseline_files[item.filename]["hashes"][
                "sha256"
            ] == item.digest.removeprefix("sha256:")
            assert baseline_files[item.filename]["yanked"] is False
    return "regressed"


def https_get(url: str, headers: dict, maximum_bytes: int) -> tuple:
    """Perform one verified HTTPS GET, retaining bounded response headers."""
    parsed = urlsplit(url)
    context = ssl.create_default_context()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    connection = http.client.HTTPSConnection(
        parsed.hostname, timeout=30, context=context
    )
    response_headers = {}
    content = bytearray()
    status = None
    try:
        connection.request(
            "GET",
            parsed.path,
            headers={**headers, "Accept-Encoding": "identity"},
        )
        response = connection.getresponse()
        status = response.status
        response_headers = dict(response.getheaders())
        while len(content) <= maximum_bytes:
            chunk = response.read(min(65536, maximum_bytes + 1 - len(content)))
            if not chunk:
                break
            content.extend(chunk)
        return status, response_headers, bytes(content)
    except BaseException as error:
        partial = getattr(error, "partial", b"")
        error.retained_body = (
            bytes(content) + (partial if isinstance(partial, bytes) else b"")
        )[: maximum_bytes + 1]
        error.retained_headers = response_headers
        error.retained_status = status
        raise
    finally:
        connection.close()


class AuditAdmission:
    """Load independent read authority and retain its verified provenance."""

    def __init__(self, directory: Any, binding: dict, run: Any) -> None:
        """Verify the concrete gate instead of accepting an author trust flag."""
        filename = "independent-diagnostic-read-gate.json"
        proof = read_gate(
            directory, filename, binding, "diagnostic-read", run=run
        )
        diagnostic_gate(proof, run)
        self.purpose = proof["purpose"]
        assert self.purpose in ("diagnostic", "seed-proof", "recovery-proof")
        if self.purpose == "seed-proof":
            assert binding["mode"] == "stop-after-wheel"
        if self.purpose == "recovery-proof":
            assert binding["mode"] == "none"
        self.identity = {
            key: proof[key]
            for key in (
                "binding_sha256",
                "target",
                "scenario",
                "attempt",
                "mode",
                "run",
            )
        }
        self.provenance = {
            "gate_sha256": digest(Path(directory) / filename),
            "gate": proof,
        }


class RegistryAudit:
    """Persist an exact native audit; expose no mutation or authentication API."""

    def __init__(
        self,
        directory: Any,
        ledger: dict,
        deadline: str,
        registry: Any,
        originals: tuple,
        *,
        save: Any,
        now: Any = None,
        wire: Any = None,
        baseline: bytes | None = None,
        purpose: str = "exact-inventory",
        admission: AuditAdmission | None = None,
    ) -> None:
        """Bind the retained audit and injected deterministic dependencies."""
        self.directory = Path(directory)
        self.ledger = ledger
        self.deadline = deadline
        self.registry = registry
        self.originals = originals
        self.save = save
        self.now = now or (lambda: datetime.now(UTC))
        self.wire = wire or https_get
        self.baseline = baseline
        assert purpose in (
            "exact-inventory",
            "diagnostic",
            "seed-proof",
            "recovery-proof",
        )
        self.purpose = purpose
        assert registry.name == "testpypi"
        assert originals and originals[0].variant == "wheel"
        self.directory.mkdir(exist_ok=True)
        (self.directory / "responses").mkdir(exist_ok=True)
        (self.directory / "observations").mkdir(exist_ok=True)
        self.ledger.setdefault("requests", [])
        self.ledger.setdefault("classification", "pending")
        self.pacer = ReadPacer(
            self.ledger.setdefault("pacing", {}),
            deadline,
            now=self.now,
            save=self.save,
        )
        self._admit_purpose(admission)

    def _admit_purpose(self, admission: AuditAdmission | None) -> None:
        """Strengthen only independently bound diagnosis without resetting history."""
        purpose = self.purpose
        previous = self.ledger.setdefault("purpose", purpose)
        self.ledger.setdefault("initial_purpose", previous)
        subject = {
            "index_url": self.registry.index_url,
            "target": self.originals[0].witness.target,
            "files": [[item.filename, item.digest] for item in self.originals],
        }
        if admission is not None:
            assert isinstance(admission, AuditAdmission)
            assert admission.purpose == purpose
            assert admission.identity["target"] == subject["target"]
            existing = self.ledger.get("admission_identity")
            if existing is None:
                assert not self.ledger["requests"], "unbound audit history"
                assert previous == purpose
                self.ledger.update(
                    admission_identity=admission.identity,
                    admission_subject=subject,
                    admission_deadline=self.deadline,
                    purpose_history=[
                        {"purpose": purpose, **admission.provenance}
                    ],
                )
            else:
                assert existing == admission.identity, (
                    "audit admission identity changed"
                )
                assert self.ledger["admission_deadline"] == self.deadline
                original = self.ledger["admission_subject"]
                assert original["index_url"] == subject["index_url"]
                assert original["target"] == subject["target"]
                assert subject["files"] == original["files"] or (
                    purpose == "seed-proof"
                    and subject["files"] == original["files"][:1]
                ), "audit originals changed"
        if previous == purpose:
            return
        assert admission is not None, (
            "purpose strengthening requires independent admission"
        )
        assert previous == "diagnostic" and purpose in (
            "seed-proof",
            "recovery-proof",
        )
        if self.ledger["classification"] == "stopped" or self.pacer.state.get(
            "stopped"
        ):
            raise ReadStopped(
                self.ledger.get("reason", "audit already stopped")
            )
        if self.now() >= datetime.fromisoformat(self.deadline):
            self.ledger.update(
                classification="stopped",
                reason="original read deadline exhausted",
            )
            self.pacer.stop("original read deadline exhausted")
        self.ledger["purpose_history"].append(
            {"purpose": purpose, **admission.provenance}
        )
        self.ledger["purpose"] = purpose
        self.save()

    def _retained(self, entry: dict) -> Any:
        path = self.directory / entry["body"]
        assert path.resolve().is_relative_to(self.directory.resolve())
        assert digest(path) == entry["sha256"], "retained registry body changed"
        headers = self.directory / entry["headers"]
        assert headers.resolve().is_relative_to(self.directory.resolve())
        assert digest(headers) == entry["headers_sha256"]
        return PythonHttpResponse(
            entry["status"], path.read_bytes(), entry["content_type"]
        )

    def request(
        self,
        method: str,
        url: str,
        headers: dict,
        body: Any,
        maximum_bytes: int,
    ) -> Any:
        """Serve pinned parser requests using only its scoped GET capabilities."""
        assert method == "GET" and body is None
        assert set(headers) <= {"Accept"}
        index = url == self.registry.index_url
        parsed = urlsplit(url)
        assert index or (
            parsed.scheme == "https"
            and parsed.netloc == self.registry.file_host
            and parsed.path.startswith("/packages/")
            and not parsed.query
            and not parsed.fragment
            and not any(c in url for c in "\r\n\\")
        )
        assert maximum_bytes <= (2 << 20 if index else 8 << 20)
        cache = self.ledger.setdefault("current_responses", {})
        if url in cache:
            return self._retained(self.ledger["requests"][cache[url] - 1])
        assert index or self.registry.index_url in cache
        if not index:
            retained_index = self._retained(
                self.ledger["requests"][cache[self.registry.index_url] - 1]
            )
            entries = parse_json_strict(retained_index.body)["files"]
            matches = [item for item in entries if item.get("url") == url]
            assert len(matches) == 1
            candidate = matches[0]
            expected = {item.filename: item for item in self.originals}
            assert candidate["filename"] in expected
            assert candidate["yanked"] is False
            assert candidate["hashes"]["sha256"] == expected[
                candidate["filename"]
            ].digest.removeprefix("sha256:")
        scheduler = (
            self.pacer
            if index
            else ReadPacer(
                self.ledger.setdefault("file_pacing", {}),
                self.deadline,
                now=self.now,
                save=self.save,
            )
        )
        scheduler.before("registry" if index else "transfer")
        records = self.ledger["requests"]
        assert sum(r.get("bytes", 0) for r in records) < 512 << 20, (
            "retained registry byte safeguard"
        )
        entry = {
            "ordinal": len(records) + 1,
            "kind": "index" if index else "file",
            "url": url,
            "started": self.now().isoformat(),
            "status": "pending",
        }
        records.append(entry)
        self.save()
        try:
            assert self.now() < datetime.fromisoformat(self.deadline)
            status, received_headers, content = self.wire(
                url, headers, maximum_bytes
            )
        except BaseException as error:
            entry.update(
                error=type(error).__name__, finished=self.now().isoformat()
            )
            response_headers = getattr(error, "retained_headers", {})
            self._record_response(
                entry,
                getattr(error, "retained_status", None),
                response_headers,
                getattr(error, "retained_body", b"")[: maximum_bytes + 1],
            )
            if isinstance(error, ReadStopped) or not isinstance(
                error, Exception
            ):
                reason = (
                    str(error)
                    if isinstance(error, ReadStopped)
                    else f"terminating registry read: {type(error).__name__}"
                )
                scheduler.state["stopped"] = reason
                self.ledger.update(classification="stopped", reason=reason)
                self.save()
                raise
            scheduler.after(
                "registry" if index else "transfer",
                status=getattr(error, "retained_status", None),
                headers=response_headers,
                error=error,
            )
            raise
        safe_headers = self._record_response(
            entry, status, received_headers, content
        )
        assert len(content) <= maximum_bytes, "registry response body limit"
        scheduler.after(
            "registry" if index else "transfer",
            status=status,
            headers=safe_headers,
        )
        assert self.now() < datetime.fromisoformat(self.deadline), (
            "original deadline exhausted during GET"
        )
        cache[url] = entry["ordinal"]
        self.save()
        return self._retained(entry)

    def _record_response(
        self, entry: dict, status: Any, received_headers: dict, content: bytes
    ) -> dict:
        """Retain sanitized headers and complete or partial response bytes."""
        safe_headers = {
            k.lower(): v
            for k, v in received_headers.items()
            if k.lower()
            in {
                "content-type",
                "retry-after",
                "x-ratelimit-remaining",
                "x-ratelimit-reset",
                "x-poll-interval",
                "x-pypi-last-serial",
                "date",
                "age",
                "etag",
            }
        }
        stem = f"responses/{entry['ordinal']:04d}"
        body_path = self.directory / (stem + ".body")
        with body_path.open("xb") as stream:
            stream.write(content)
        header_path = self.directory / (stem + ".headers.json")
        assert not header_path.exists()
        save_json(header_path, safe_headers)
        entry.update(
            status=status,
            body=body_path.relative_to(self.directory).as_posix(),
            sha256=digest(body_path),
            headers=header_path.relative_to(self.directory).as_posix(),
            headers_sha256=digest(header_path),
            bytes=len(content),
            content_type=safe_headers.get("content-type", ""),
            finished=self.now().isoformat(),
        )
        return safe_headers

    def step(self) -> Any:
        """Advance one audit observation or yield its persisted eligible time."""
        if self.ledger["classification"] == "stopped":
            raise ReadStopped(self.ledger["reason"])
        try:
            if self.now() >= datetime.fromisoformat(self.deadline):
                self.pacer.stop("original read deadline exhausted")
            observation = read_python_index(
                self.registry, self.originals[0].witness, self
            )
            classification = classify(
                observation,
                self.originals[:1]
                if self.purpose == "seed-proof"
                else self.originals,
                self.baseline,
                diagnostic=self.purpose == "diagnostic",
            )
            if self.ledger["classification"] == "complete":
                assert classification == "complete"
                for item in self.ledger["observations"]:
                    assert (
                        digest(self.directory / item["path"]) == item["sha256"]
                    )
                return observation
            document = observation.to_document()
            ordinal = len(self.ledger.setdefault("observations", [])) + 1
            filename = f"observations/{ordinal:04d}.json"
            save_json(self.directory / filename, document)
            self.ledger["observations"].append(
                {
                    "path": filename,
                    "sha256": digest(self.directory / filename),
                    "classification": classification,
                }
            )
            self.ledger["classification"] = (
                "complete" if classification == "complete" else "pending"
            )
            if classification == "complete":
                variants = tuple(item.variant for item in observation.files)
                self.ledger["destination_state"] = {
                    (): "absent",
                    ("wheel",): "wheel-only",
                    ("sdist",): "sdist-only",
                    ("wheel", "sdist"): "complete",
                }[variants]
            self.save()
            if classification == "regressed":
                self.ledger["current_responses"] = {}
                self.save()
                self.pacer.pending("registry")
            return observation
        except ReadPending:
            raise
        except Exception as error:
            self.ledger.update(classification="stopped", reason=str(error))
            self.save()
            raise
