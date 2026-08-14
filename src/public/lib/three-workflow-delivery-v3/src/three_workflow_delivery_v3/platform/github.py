"""Strict injectable GitHub REST clients for Governance and Actions history."""

# ruff: noqa: D107, E501, EM101, PLR2004, S310, TRY003

from __future__ import annotations

import base64
import json
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass
from io import BytesIO
from typing import TYPE_CHECKING, Protocol, cast

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.release.eligibility import GovernanceBlob


@dataclass(frozen=True, slots=True)
class GitHubPage:
    """One fully typed GitHub pagination page."""

    items: tuple[object, ...]
    next_cursor: str | None
    complete: bool = True


@dataclass(frozen=True, slots=True)
class GitHubRun:
    """Platform-owned workflow run facts kept separate from artifacts."""

    run_id: int
    node_id: str
    head_sha: str
    run_attempt: int
    metadata: tuple[tuple[str, str], ...]


@dataclass(frozen=True, slots=True)
class GitHubRunAttemptFact:
    """Artifact-independent facts for one exact workflow run attempt."""

    run_id: int
    node_id: str
    head_sha: str
    run_attempt: int
    status: str
    conclusion: str | None
    metadata: tuple[tuple[str, str], ...] = ()


@dataclass(frozen=True, slots=True)
class GitHubArtifact:
    """Immutable artifact metadata addressed only by numeric ID."""

    artifact_id: int
    metadata: tuple[tuple[str, str], ...]
    upload_digest: str | None = None


@dataclass(frozen=True, slots=True)
class GitHubJob:
    """Separately queried job/phase facts for a workflow run."""

    job_id: int
    conclusion: str
    phase: str
    metadata: tuple[tuple[str, str], ...] = ()


class GitHubActionsHistoryClient(Protocol):
    """Minimal injectable GitHub Actions API surface for live history."""

    def list_runs(self, cursor: str | None) -> GitHubPage | dict[str, object]:
        """List workflow runs using an opaque cursor."""
        ...

    def list_artifacts(
        self,
        run_id: int,
        cursor: str | None,
    ) -> GitHubPage | dict[str, object]:
        """List artifacts for one run using only the run ID."""
        ...

    def list_jobs(
        self,
        run_id: int,
        cursor: str | None,
    ) -> GitHubPage | dict[str, object]:
        """List jobs for one run using only the run ID."""
        ...

    def list_attempt_jobs(
        self,
        run_id: int,
        run_attempt: int,
        cursor: str | None,
    ) -> GitHubPage | dict[str, object]:
        """List jobs for one exact earlier run attempt."""
        ...

    def get_run_attempt(
        self,
        run_id: int,
        run_attempt: int,
    ) -> GitHubRunAttemptFact | dict[str, object]:
        """Read artifact-independent facts for one exact run attempt."""
        ...

    def download_artifact(self, artifact_id: int) -> bytes:
        """Download one immutable artifact by numeric ID only."""
        ...


class GitHubRestError(RuntimeError):
    """A GitHub REST request failed closed."""

    def __init__(
        self,
        message: str,
        *,
        status_code: int | None = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code


class GitHubRestClient:
    """Fixed-origin GitHub REST implementation for live admission reads."""

    _MAX_REDIRECTS = 5
    _REDIRECT_STATUSES = frozenset({301, 302, 303, 307, 308})

    def __init__(
        self,
        *,
        repository: str,
        token: str,
        workflow_path: str | None = None,
        timeout: int = 20,
        opener: Callable[[urllib.request.Request, int], bytes] | None = None,
    ) -> None:
        if (
            type(repository) is not str
            or repository.count("/") != 1
            or type(token) is not str
            or not token
            or type(timeout) is not int
            or timeout <= 0
        ):
            message = "GitHub REST client configuration is malformed"
            raise ValueError(message)
        self._repository = repository
        self._token = token
        self._workflow_path = workflow_path
        self._timeout = timeout
        self._opener = opener or self._open
        self._run_attempts: dict[int, int] = {}
        self._page_totals: dict[str, int] = {}
        self._page_counts: dict[str, int] = {}

    def _open(self, request: urllib.request.Request, timeout: int) -> bytes:
        class _NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(
                self,
                *_args: object,
                **_kwargs: object,
            ) -> None:
                return None

        opener = urllib.request.build_opener(_NoRedirect)
        current = request
        seen: set[str] = set()
        for _hop in range(self._MAX_REDIRECTS + 1):
            url = current.full_url
            self._validate_api_url(url)
            if url in seen:
                raise GitHubRestError("GitHub REST redirect cycle rejected")
            seen.add(url)
            try:
                with opener.open(current, timeout=timeout) as response:
                    return response.read()
            except urllib.error.HTTPError as error:
                if error.code not in self._REDIRECT_STATUSES:
                    raise GitHubRestError(
                        str(error),
                        status_code=error.code,
                    ) from error
                location = error.headers.get("Location")
                if location is None:
                    raise GitHubRestError(
                        "GitHub REST redirect location is missing"
                    ) from error
                target = urllib.parse.urljoin(url, location)
                self._validate_api_url(target)
                current = urllib.request.Request(
                    target,
                    headers=dict(current.header_items()),
                )
            except OSError as error:
                raise GitHubRestError(str(error)) from error
        raise GitHubRestError("GitHub REST redirect limit exceeded")

    @staticmethod
    def _validate_api_url(url: str) -> None:
        parsed = urllib.parse.urlparse(url)
        try:
            port = parsed.port
        except ValueError as error:
            raise GitHubRestError(
                "GitHub REST URL port is malformed"
            ) from error
        if (
            parsed.scheme != "https"
            or parsed.hostname != "api.github.com"
            or parsed.username is not None
            or parsed.password is not None
            or port not in {None, 443}
            or parsed.fragment
        ):
            raise GitHubRestError("GitHub REST redirect left api.github.com")

    def _request(self, path: str) -> bytes:
        if not path.startswith("/"):
            raise GitHubRestError("GitHub REST path is malformed")
        request = urllib.request.Request(
            f"https://api.github.com{path}",
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self._token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "three-workflow-delivery-v3",
            },
        )
        return self._opener(request, self._timeout)

    def _json(self, path: str) -> dict[str, object]:
        try:
            value = json.loads(self._request(path))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            message = "GitHub REST returned malformed JSON"
            raise GitHubRestError(message) from error
        if type(value) is not dict:
            message = "GitHub REST returned a non-object response"
            raise GitHubRestError(message)
        return cast("dict[str, object]", value)

    @staticmethod
    def _page_number(cursor: str | None) -> int:
        if cursor is None:
            return 1
        if not cursor.startswith("page:") or not cursor[5:].isdigit():
            message = "GitHub REST pagination cursor is malformed"
            raise GitHubRestError(message)
        return int(cursor[5:])

    def _page(
        self,
        document: dict[str, object],
        *,
        field: str,
        page: int,
        context: str,
    ) -> GitHubPage:
        items = document.get(field)
        if type(items) is not list:
            message = f"GitHub REST {field} page is malformed"
            raise GitHubRestError(message)
        total = document.get("total_count")
        if type(total) is not int or total < 0:
            message = f"GitHub REST {field} total_count is malformed"
            raise GitHubRestError(message)
        if page == 1:
            self._page_totals[context] = total
            self._page_counts[context] = 0
        elif self._page_totals.get(context) != total:
            message = f"GitHub REST {field} total_count changed"
            raise GitHubRestError(message)
        if context not in self._page_counts:
            message = f"GitHub REST {field} pagination did not start at page 1"
            raise GitHubRestError(message)
        if len(items) > 100:
            message = f"GitHub REST {field} page exceeds per_page"
            raise GitHubRestError(message)
        previous = self._page_counts[context]
        consumed = previous + len(items)
        if consumed > total:
            message = f"GitHub REST {field} cumulative count exceeds total"
            raise GitHubRestError(message)
        if consumed < total and len(items) != 100:
            message = f"GitHub REST {field} non-final page is short"
            raise GitHubRestError(message)
        if consumed == total:
            self._page_counts.pop(context, None)
            self._page_totals.pop(context, None)
        else:
            self._page_counts[context] = consumed
        return GitHubPage(
            items=tuple(items),
            next_cursor=f"page:{page + 1}" if consumed < total else None,
            complete=True,
        )

    def list_runs(self, cursor: str | None) -> GitHubPage:
        """List every run of the fixed reusable workflow."""
        if self._workflow_path is None:
            message = "Actions history requires a fixed workflow path"
            raise GitHubRestError(message)
        page = self._page_number(cursor)
        workflow = urllib.parse.quote(self._workflow_path, safe="")
        document = self._json(
            f"/repos/{self._repository}/actions/workflows/{workflow}/runs"
            f"?per_page=100&page={page}"
        )
        result = self._page(
            document,
            field="workflow_runs",
            page=page,
            context="workflow_runs",
        )
        normalized: list[GitHubRun] = []
        for item in result.items:
            if type(item) is not dict:
                raise GitHubRestError("GitHub REST workflow run is malformed")
            run_id = item.get("id")
            run_attempt = item.get("run_attempt")
            head_sha = item.get("head_sha")
            node_id = item.get("node_id")
            if (
                type(run_id) is not int
                or type(run_attempt) is not int
                or type(head_sha) is not str
                or type(node_id) is not str
            ):
                raise GitHubRestError("GitHub REST workflow run is malformed")
            self._run_attempts[run_id] = run_attempt
            normalized.append(
                GitHubRun(
                    run_id=run_id,
                    node_id=node_id,
                    head_sha=head_sha,
                    run_attempt=run_attempt,
                    metadata=(
                        ("conclusion", str(item.get("conclusion"))),
                        ("status", str(item.get("status"))),
                    ),
                )
            )
        return GitHubPage(
            tuple(normalized), result.next_cursor, result.complete
        )

    def list_artifacts(self, run_id: int, cursor: str | None) -> GitHubPage:
        """List every artifact for one immutable run ID."""
        page = self._page_number(cursor)
        document = self._json(
            f"/repos/{self._repository}/actions/runs/{run_id}/artifacts"
            f"?per_page=100&page={page}"
        )
        result = self._page(
            document,
            field="artifacts",
            page=page,
            context=f"artifacts:{run_id}",
        )
        normalized: list[GitHubArtifact] = []
        for item in result.items:
            if type(item) is not dict or type(item.get("id")) is not int:
                raise GitHubRestError("GitHub REST artifact is malformed")
            digest = item.get("digest")
            normalized.append(
                GitHubArtifact(
                    artifact_id=cast("int", item["id"]),
                    metadata=(
                        ("expired", str(item.get("expired")).lower()),
                        ("name", str(item.get("name"))),
                    ),
                    upload_digest=digest if type(digest) is str else None,
                )
            )
        return GitHubPage(
            tuple(normalized), result.next_cursor, result.complete
        )

    def list_jobs(self, run_id: int, cursor: str | None) -> GitHubPage:
        """List every job for the exact run attempt returned by run discovery."""
        attempt = self._run_attempts.get(run_id)
        if attempt is None:
            raise GitHubRestError("GitHub REST run attempt was not discovered")
        return self.list_attempt_jobs(run_id, attempt, cursor)

    def list_attempt_jobs(
        self,
        run_id: int,
        run_attempt: int,
        cursor: str | None,
    ) -> GitHubPage:
        """List every job for one separately proven exact run attempt."""
        if type(run_attempt) is not int or run_attempt <= 0:
            raise GitHubRestError("GitHub REST run attempt is malformed")
        page = self._page_number(cursor)
        document = self._json(
            f"/repos/{self._repository}/actions/runs/{run_id}/attempts/"
            f"{run_attempt}/jobs?per_page=100&page={page}"
        )
        result = self._page(
            document,
            field="jobs",
            page=page,
            context=f"jobs:{run_id}:{run_attempt}",
        )
        normalized: list[GitHubJob] = []
        for item in result.items:
            if type(item) is not dict or type(item.get("id")) is not int:
                raise GitHubRestError("GitHub REST job is malformed")
            normalized.append(
                GitHubJob(
                    job_id=cast("int", item["id"]),
                    conclusion=str(item.get("conclusion")),
                    phase=str(item.get("name")),
                    metadata=(("status", str(item.get("status"))),),
                )
            )
        return GitHubPage(
            tuple(normalized), result.next_cursor, result.complete
        )

    def get_run_attempt(
        self,
        run_id: int,
        run_attempt: int,
    ) -> GitHubRunAttemptFact:
        """Read and validate one exact workflow run-attempt endpoint."""
        if (
            type(run_id) is not int
            or run_id <= 0
            or type(run_attempt) is not int
            or run_attempt <= 0
        ):
            raise GitHubRestError("GitHub REST run attempt key is malformed")
        item = self._json(
            f"/repos/{self._repository}/actions/runs/{run_id}/attempts/"
            f"{run_attempt}"
        )
        returned_run_id = item.get("id")
        returned_attempt = item.get("run_attempt")
        head_sha = item.get("head_sha")
        node_id = item.get("node_id")
        status = item.get("status")
        conclusion = item.get("conclusion")
        if (
            type(returned_run_id) is not int
            or type(returned_attempt) is not int
            or type(head_sha) is not str
            or not head_sha
            or type(node_id) is not str
            or not node_id
            or type(status) is not str
            or not status
            or (conclusion is not None and type(conclusion) is not str)
        ):
            raise GitHubRestError("GitHub REST exact run attempt is malformed")
        return GitHubRunAttemptFact(
            run_id=returned_run_id,
            node_id=node_id,
            head_sha=head_sha,
            run_attempt=returned_attempt,
            status=status,
            conclusion=cast("str | None", conclusion),
            metadata=(
                ("event", str(item.get("event"))),
                ("workflow-id", str(item.get("workflow_id"))),
            ),
        )

    def download_artifact(self, artifact_id: int) -> bytes:
        """Download one raw single-file artifact by numeric ID."""
        payload = self._request(
            f"/repos/{self._repository}/actions/artifacts/{artifact_id}/zip"
        )
        if payload.startswith(b"PK"):
            try:
                with zipfile.ZipFile(BytesIO(payload)) as archive:
                    files = [
                        name
                        for name in archive.namelist()
                        if not name.endswith("/")
                    ]
                    if len(files) != 1:
                        raise GitHubRestError(
                            "history artifact must contain exactly one file"
                        )
                    return archive.read(files[0])
            except zipfile.BadZipFile as error:
                raise GitHubRestError(
                    "history artifact ZIP is malformed"
                ) from error
        return payload

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        """Return the exact branch protection state."""
        if repository != self._repository:
            raise GitHubRestError("Governance repository mismatch")
        if not ref.startswith("refs/heads/"):
            raise GitHubRestError("Governance protected ref is malformed")
        branch = urllib.parse.quote(ref.removeprefix("refs/heads/"), safe="")
        document = self._json(f"/repos/{repository}/branches/{branch}")
        protected = document.get("protected")
        if type(protected) is not bool:
            raise GitHubRestError(
                "GitHub REST branch protection response is malformed"
            )
        return protected

    def resolve_ref(self, repository: str, ref: str) -> str:
        """Resolve the fixed ref to one immutable commit SHA."""
        if repository != self._repository:
            raise GitHubRestError("Governance repository mismatch")
        encoded = urllib.parse.quote(ref.removeprefix("refs/"), safe="/")
        document = self._json(f"/repos/{repository}/git/ref/{encoded}")
        target = document.get("object")
        if type(target) is not dict or type(target.get("sha")) is not str:
            raise GitHubRestError("Governance ref response is malformed")
        return cast("str", target["sha"])

    def read_blob(
        self,
        repository: str,
        commit: str,
        path: str,
    ) -> GovernanceBlob:
        """Read exact protected content at an immutable commit."""
        from three_workflow_delivery_v3.release.eligibility import (  # noqa: PLC0415
            GovernanceBlob,
        )

        if repository != self._repository:
            raise GitHubRestError("Governance repository mismatch")
        encoded_path = urllib.parse.quote(path, safe="/")
        document = self._json(
            f"/repos/{repository}/contents/{encoded_path}?ref={commit}"
        )
        oid = document.get("sha")
        content = document.get("content")
        encoding = document.get("encoding")
        if (
            type(oid) is not str
            or type(content) is not str
            or encoding != "base64"
        ):
            raise GitHubRestError("Governance content response is malformed")
        try:
            decoded = base64.b64decode(content, validate=True)
        except ValueError as error:
            raise GitHubRestError(
                "Governance content base64 is malformed"
            ) from error
        return GovernanceBlob(blob_oid=oid, content=decoded)


def _page(value: GitHubPage | dict[str, object], *, context: str) -> GitHubPage:
    if isinstance(value, GitHubPage):
        return value
    if type(value) is not dict:
        message = f"{context} returned a malformed page"
        raise TypeError(message)
    raw_items = value.get("items")
    if type(raw_items) not in {tuple, list}:
        message = f"{context} page is malformed"
        raise TypeError(message)
    items = cast("tuple[object, ...] | list[object]", raw_items)
    next_cursor = value.get("next")
    if next_cursor is not None and type(next_cursor) is not str:
        message = f"{context} next cursor is malformed"
        raise TypeError(message)
    complete = value.get("complete", True)
    if type(complete) is not bool:
        message = f"{context} completion flag is malformed"
        raise TypeError(message)
    return GitHubPage(
        items=tuple(items),
        next_cursor=next_cursor,
        complete=complete,
    )


def _iter_pages(
    first_context: str,
    fetch: Callable[[str | None], GitHubPage | dict[str, object]],
) -> tuple[object, ...]:
    cursor: str | None = None
    items: list[object] = []
    seen_cursors: set[str] = set()
    while True:
        try:
            page = _page(fetch(cursor), context=first_context)
        except RuntimeError as error:
            message = str(error)
            raise ValueError(message) from error
        if not page.complete:
            message = f"{first_context} truncated"
            raise ValueError(message)
        items.extend(page.items)
        if page.next_cursor is None:
            return tuple(items)
        if page.next_cursor in seen_cursors:
            message = f"{first_context} duplicate cursor"
            raise ValueError(message)
        seen_cursors.add(page.next_cursor)
        cursor = page.next_cursor


def iter_all_runs(client: GitHubActionsHistoryClient) -> tuple[object, ...]:
    """Return every run page item or fail closed on incomplete pagination."""
    return _iter_pages("runs", client.list_runs)


def iter_all_artifacts(
    client: GitHubActionsHistoryClient,
    run_id: int,
) -> tuple[object, ...]:
    """Return every artifact page item for one run."""
    return _iter_pages(
        f"artifacts:{run_id}",
        lambda cursor: client.list_artifacts(run_id, cursor),
    )


def iter_all_jobs(
    client: GitHubActionsHistoryClient,
    run_id: int,
) -> tuple[object, ...]:
    """Return every job page item for one run."""
    return _iter_pages(
        f"jobs:{run_id}",
        lambda cursor: client.list_jobs(run_id, cursor),
    )


def iter_all_attempt_jobs(
    client: GitHubActionsHistoryClient,
    run_id: int,
    run_attempt: int,
) -> tuple[object, ...]:
    """Return every job page item for one exact earlier run attempt."""
    return _iter_pages(
        f"jobs:{run_id}:attempt:{run_attempt}",
        lambda cursor: client.list_attempt_jobs(
            run_id,
            run_attempt,
            cursor,
        ),
    )
