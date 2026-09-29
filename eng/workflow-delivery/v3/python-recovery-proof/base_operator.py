# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# Only fixed Git/gh argv invoke already admitted local tools.
# ruff: noqa: ANN401, C901, E501, PLR0912, PLR0913, PLR0915, PLR2004, S101, S607, TRY300

"""Single-use operator request ledger for the reviewed normal-publication protocol.

Preparation only: importing this module performs no network or credential access.
Runtime publication/registry requests remain in the accepted hosted implementation.
"""

import datetime
import hashlib
import http.client
import json
import signal
import subprocess
from contextlib import contextmanager
from pathlib import Path
from typing import Any, Never
from urllib.parse import urlsplit

from campaign import save_json
from read_policy import ReadPacer, ReadPending, ReadStopped


class OperatorDeadlineExceeded(BaseException):
    """Stop supervision even if audited code catches ordinary consumer exceptions."""


class Operator:
    """Enforce the retained caller lifetime and effects."""

    def __init__(self, directory: Path, target: str) -> None:
        """Load or construct the reserved caller state."""
        self.directory = directory
        self.target = target
        self.ledger_path = directory / "ledger.json"
        if self.ledger_path.exists():
            self.ledger = json.loads(self.ledger_path.read_text())
            assert self.ledger["target"] == target
        else:
            directory.mkdir(mode=0o700)
            self.ledger = {
                "target": target,
                "started": self.now(),
                "requests": [],
            }
            self.save()

    @staticmethod
    def now() -> Any:
        """Return the current UTC timestamp."""
        return datetime.datetime.now(datetime.UTC).isoformat()

    def save(self) -> None:
        """Persist the original ledger durably."""
        save_json(self.ledger_path, self.ledger)

    def remaining(self) -> Any:
        """Return the original remaining lifetime or stop."""
        elapsed = (
            datetime.datetime.now(datetime.UTC)
            - datetime.datetime.fromisoformat(self.ledger["started"])
        ).total_seconds()
        if "deadline_exhausted_at" in self.ledger or not 0 <= elapsed < 14400:
            msg = "original operator deadline exhausted"
            raise OperatorDeadlineExceeded(msg)
        return 14400 - elapsed

    @contextmanager
    def deadline(self) -> Any:
        """Bound requests and caller-owned local audit/consumer work on this POSIX host."""
        remaining = self.remaining()
        if getattr(self, "_deadline_active", False):
            yield
            self.remaining()
            return
        assert signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0)
        previous_handler = signal.getsignal(signal.SIGALRM)

        def expired(_signum: Any, _frame: Any) -> Never:
            """Validate the expired caller boundary."""
            msg = "original operator deadline exhausted"
            raise OperatorDeadlineExceeded(msg)

        signal.signal(signal.SIGALRM, expired)
        self._deadline_active = True
        signal.setitimer(signal.ITIMER_REAL, remaining)
        try:
            yield
            self.remaining()
        except OperatorDeadlineExceeded:
            self.ledger["deadline_exhausted_at"] = self.now()
            self.save()
            raise
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
            signal.signal(signal.SIGALRM, previous_handler)
            self._deadline_active = False

    def request(
        self,
        category: Any,
        name: Any,
        url: Any,
        *,
        method: Any = "GET",
        document: Any = None,
        poll: Any = False,
    ) -> Any:
        """Validate the request caller boundary."""
        with self.deadline():
            return self._request(
                category, name, url, method=method, document=document, poll=poll
            )

    def _request(
        self,
        category: Any,
        name: Any,
        url: Any,
        *,
        method: Any = "GET",
        document: Any = None,
        poll: Any = False,
    ) -> Any:
        """Validate the request caller boundary."""
        limits = {
            "json": (400, 8 << 20, None),
            "artifact": (128, 16 << 20, 512 << 20),
            "log": (16, 32 << 20, 128 << 20),
            "dispatch": (1, 8 << 20, None),
            "approval": (1, 8 << 20, None),
        }
        count_limit, body_limit, byte_limit = limits[category]
        now = datetime.datetime.now(datetime.UTC)
        assert (
            0
            <= (
                now - datetime.datetime.fromisoformat(self.ledger["started"])
            ).total_seconds()
            < 14400
        )
        requests = self.ledger["requests"]
        previous = [r for r in requests if r["category"] == category]
        if method == "POST":
            assert len(previous) < count_limit
        if byte_limit is not None:
            body_limit = min(
                body_limit,
                byte_limit - sum(r.get("bytes", 0) for r in previous),
            )
            assert body_limit > 0
        if poll:
            polls = [r for r in requests if r.get("poll")]
            assert category == "json"
            if polls:
                assert (
                    now - datetime.datetime.fromisoformat(polls[-1]["started"])
                ).total_seconds() >= 30
        parsed = urlsplit(url)
        assert parsed.scheme == "https"
        assert not parsed.username
        assert not parsed.password
        assert parsed.port in (None, 443)
        assert not parsed.fragment
        api = parsed.hostname == "api.github.com"
        assert api or (
            category in ("artifact", "log")
            and parsed.hostname is not None
            and (
                parsed.hostname.endswith(".blob.core.windows.net")
                or parsed.hostname.endswith(".actions.githubusercontent.com")
            )
        )
        if api:
            assert parsed.path.startswith("/repos/hcoona/three/")
        assert method == (
            "POST" if category in ("dispatch", "approval") else "GET"
        )
        if category == "dispatch":
            assert (
                parsed.path
                == "/repos/hcoona/three/actions/workflows/workflow-delivery-v3-python-smoke.yml/dispatches"
            )
            assert document == self.dispatch_document()
        if category == "approval":
            assert (
                parsed.path
                == f"/repos/hcoona/three/actions/runs/{self.ledger['run']}/pending_deployments"
            )
            assert document["environment_ids"] == [22765954016]
            assert document["state"] == "approved"
        assert name
        assert Path(name).name == name
        pacer = None
        if method == "GET":
            control = self.ledger.setdefault("read_control", {})
            if control.get("stopped"):
                raise ReadStopped(control["stopped"])
            if (
                control.get("next_not_before")
                and (
                    datetime.datetime.fromisoformat(control["next_not_before"])
                    - now
                ).total_seconds()
                >= self.remaining()
            ):
                control["stopped"] = "original read deadline exhausted"
                self.save()
                raise ReadStopped(control["stopped"])
            if control.get(
                "next_not_before"
            ) and now < datetime.datetime.fromisoformat(
                control["next_not_before"]
            ):
                raise ReadPending(control["next_not_before"])
            key = (
                "github-polls"
                if poll
                else getattr(self, "_active_transfer_key", None)
                or hashlib.sha256(url.encode()).hexdigest()
            )
            pacing = self.ledger.setdefault("read_pacing", {}).setdefault(
                key, {}
            )
            pacing["consecutive_errors"] = control.get("consecutive_errors", 0)
            deadline = (
                self.ledger.get("deadline")
                or (
                    datetime.datetime.fromisoformat(self.ledger["started"])
                    + datetime.timedelta(hours=4)
                ).isoformat()
            )

            def save_pacing() -> None:
                """Keep the stage breaker and transient wait across endpoints."""
                control["consecutive_errors"] = pacing.get(
                    "consecutive_errors", 0
                )
                if pacing.get("stopped"):
                    control["stopped"] = pacing["stopped"]
                service_due = max(
                    (
                        datetime.datetime.fromisoformat(value)
                        for value in (
                            control.get("service_not_before"),
                            pacing.get("service_not_before"),
                        )
                        if value
                    ),
                    default=None,
                )
                waits = []
                if service_due and service_due > datetime.datetime.now(
                    datetime.UTC
                ):
                    control["service_not_before"] = service_due.isoformat()
                    waits.append(service_due)
                else:
                    control.pop("service_not_before", None)
                if control["consecutive_errors"]:
                    waits.append(
                        datetime.datetime.fromisoformat(
                            pacing["next_not_before"]
                        )
                    )
                if waits:
                    control["next_not_before"] = max(waits).isoformat()
                else:
                    control.pop("next_not_before", None)
                self.save()

            pacer = ReadPacer(pacing, deadline, save=save_pacing)
            if not getattr(self, "_transfer_redirect", False):
                pacer.before("github" if poll else "transfer")
        ordinal = len(requests) + 1
        stem = f"{ordinal:03d}-{name}"
        entry = {
            "ordinal": ordinal,
            "name": name,
            "category": category,
            "method": method,
            "started": now.isoformat(),
            "poll": poll,
            "url_sha256": hashlib.sha256(url.encode()).hexdigest(),
            "status": "pending",
            "bytes": body_limit if category in ("artifact", "log") else 0,
            "send_started": False,
            "document_sha256": None
            if document is None
            else hashlib.sha256(
                json.dumps(document, sort_keys=True).encode()
            ).hexdigest(),
        }
        requests.append(entry)
        self.save()  # Reserve every reached slot before credentials or transport.
        connection = None
        response_status = None
        response_headers = {}
        try:
            headers = {
                "Accept": "application/vnd.github+json" if api else "*/*",
                "Accept-Encoding": "identity",
                "User-Agent": "wdv3-reviewed-hosted-recovery-operator",
            }
            if api and not getattr(self, "_transfer_redirect", False):
                credential = getattr(self, "_credential", None)
                if credential is None:
                    credential = subprocess.run(
                        ["gh", "auth", "token"],
                        check=True,
                        capture_output=True,
                        text=True,
                        timeout=min(30, self.remaining()),
                    ).stdout.strip()
                    assert credential
                    self._credential = credential
                headers["Authorization"] = "Bearer " + credential
                headers["X-GitHub-Api-Version"] = "2022-11-28"
            body = None if document is None else json.dumps(document).encode()
            if body is not None:
                headers["Content-Type"] = "application/json"
            self.remaining()
            connection = http.client.HTTPSConnection(
                parsed.hostname, timeout=min(30, self.remaining())
            )
            entry["send_started"] = (
                True  # Conservatively uncertain from this durable point.
            )
            self.save()
            self.remaining()  # Last check after credentials and reservation, before transport.
            connection.request(
                method,
                parsed.path + ("?" + parsed.query if parsed.query else ""),
                body,
                headers,
            )
            response = connection.getresponse()
            response_status = response.status
            response_headers = dict(response.getheaders())
            header_path = self.directory / (stem + ".headers.json")
            header_path.write_text(
                json.dumps(response_headers, indent=2) + "\n"
            )
            header_path.chmod(
                0o600
            )  # Signed Location URLs are private, never public evidence.
            entry["status"] = response.status
            body_path = self.directory / (stem + ".body")
            self.remaining()
            with body_path.open("xb") as stream:
                body_path.chmod(0o600)
                size = 0
                while size <= body_limit:
                    chunk = response.read(min(65536, body_limit + 1 - size))
                    if not chunk:
                        break
                    stream.write(chunk)
                    stream.flush()
                    size += len(chunk)
                    self.remaining()
            content = body_path.read_bytes()
            entry.update(
                status=response.status,
                bytes=len(content),
                finished=self.now(),
                sha256=hashlib.sha256(content).hexdigest(),
                body=body_path.name,
            )
            self.remaining()
            assert len(content) <= body_limit, (
                "operator response budget exceeded"
            )
            if pacer is not None:
                pacer.after(
                    "github" if poll else "transfer",
                    status=response.status,
                    headers=response_headers,
                    github=api,
                    reset_errors=response.status
                    not in (301, 302, 303, 307, 308),
                )
            else:
                self.save()
            assert response.status in (200, 201, 204) or (
                category in ("artifact", "log")
                and response.status in (301, 302, 303, 307, 308)
            ), f"operator HTTP {response.status}; no retry"
            return response.status, response_headers, content
        except BaseException as error:
            entry.update(finished=self.now(), error_type=type(error).__name__)
            partial = self.directory / (stem + ".body")
            if partial.exists():
                retained = partial.read_bytes()
                entry.update(
                    retained_bytes=len(retained),
                    body=partial.name,
                    retained_sha256=hashlib.sha256(retained).hexdigest(),
                )
            if pacer is not None and not isinstance(
                error, (ReadPending, ReadStopped)
            ):
                pacer.after(
                    "github" if poll else "transfer",
                    status=response_status,
                    headers=response_headers,
                    error=error,
                    github=api,
                )
            else:
                self.save()
            raise
        finally:
            if connection is not None:
                connection.close()

    def get(self, endpoint: Any, name: Any, *, poll: Any = False) -> Any:
        """Fetch one counted GitHub JSON response."""
        _, _, content = self.request(
            "json", name, "https://api.github.com/" + endpoint, poll=poll
        )
        try:
            return json.loads(content)
        except (ValueError, UnicodeError):
            key = (
                "github-polls"
                if poll
                else hashlib.sha256(
                    ("https://api.github.com/" + endpoint).encode()
                ).hexdigest()
            )
            self.ledger["read_pacing"][key]["stopped"] = "malformed GitHub JSON"
            self.ledger.setdefault("read_control", {})["stopped"] = (
                "malformed GitHub JSON"
            )
            self.save()
            raise

    def dispatch_document(self) -> Any:
        """Bind the actual workflow dispatch input."""
        return {
            "ref": "main",
            "inputs": {"registry": "testpypi", "recovery-proof": self.mode},
        }

    def dispatch(self) -> Any:
        """Send the single reserved workflow dispatch."""
        return self.request(
            "dispatch",
            "dispatch",
            "https://api.github.com/repos/hcoona/three/actions/workflows/workflow-delivery-v3-python-smoke.yml/dispatches",
            method="POST",
            document=self.dispatch_document(),
        )

    def approve(self, comment: Any) -> Any:
        """Send the single admitted Environment approval."""
        return self.request(
            "approval",
            "approve",
            f"https://api.github.com/repos/hcoona/three/actions/runs/{self.ledger['run']}/pending_deployments",
            method="POST",
            document={
                "environment_ids": [22765954016],
                "state": "approved",
                "comment": comment,
            },
        )

    def transfer(self, category: Any, endpoint: Any, name: Any) -> Any:
        """Retain one immutable service transfer with counted redirects."""
        assert category in ("artifact", "log")
        transfers = self.ledger.setdefault("transfers", [])
        completed = self.ledger.setdefault("completed_transfers", {})
        if endpoint in completed:
            retained = completed[endpoint]
            path = self.directory / retained["body"]
            assert path.parent == self.directory
            content = path.read_bytes()
            assert hashlib.sha256(content).hexdigest() == retained["sha256"]
            return content
        if category == "artifact":
            assert "/actions/artifacts/" in endpoint
            assert (
                endpoint in transfers
                or len([x for x in transfers if "/actions/artifacts/" in x])
                < 32
            )
        if endpoint not in transfers:
            transfers.append(endpoint)
        self.save()
        url = "https://api.github.com/" + endpoint
        for hop in range(3):
            self._transfer_redirect = hop > 0
            self._active_transfer_key = hashlib.sha256(
                endpoint.encode()
            ).hexdigest()
            try:
                status, headers, content = self.request(
                    category, f"{name}-{hop}", url
                )
            finally:
                self._transfer_redirect = False
                self._active_transfer_key = None
            if status == 200:
                entry = self.ledger["requests"][-1]
                completed[endpoint] = {
                    "body": entry["body"],
                    "sha256": entry["sha256"],
                }
                self.save()
                return content
            locations = [
                v for k, v in headers.items() if k.lower() == "location"
            ]
            assert len(locations) == 1
            url = locations[0]
        msg = "operator redirect bound exhausted"
        raise RuntimeError(msg)

    def capture_get(self, endpoint: Any, name: Any) -> Any:
        """Reuse verified retained stage facts during immutable capture replay."""
        captures = self.ledger.setdefault("captured_json", {})
        if name in captures:
            item = captures[name]
            assert item["endpoint"] == endpoint
            path = self.directory / item["body"]
            assert path.parent == self.directory
            content = path.read_bytes()
            assert hashlib.sha256(content).hexdigest() == item["sha256"]
            return json.loads(content)
        result = self.get(endpoint, name)
        entry = self.ledger["requests"][-1]
        captures[name] = {
            "endpoint": endpoint,
            "body": entry["body"],
            "sha256": entry["sha256"],
        }
        self.save()
        return result
