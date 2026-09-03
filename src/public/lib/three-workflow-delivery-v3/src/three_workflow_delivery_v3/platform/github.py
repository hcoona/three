"""Strict injectable GitHub REST client for Governance reads."""

# ruff: noqa: D107, EM101, S310, TRY003

from __future__ import annotations

import base64
import json
import urllib.error
import urllib.parse
import urllib.request
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Callable
    from http.client import HTTPMessage
    from typing import IO

    from three_workflow_delivery_v3.release.eligibility import GovernanceBlob


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


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Surface redirect responses without parsing or following Location."""

    def redirect_request(
        self,
        *_args: object,
        **_kwargs: object,
    ) -> None:
        return None

    def http_error_302(
        self,
        req: urllib.request.Request,
        fp: IO[bytes],
        code: int,
        msg: str,
        headers: HTTPMessage,
    ) -> None:
        raise urllib.error.HTTPError(
            req.full_url,
            code,
            msg,
            headers,
            fp,
        )

    http_error_301 = http_error_302
    http_error_303 = http_error_302
    http_error_307 = http_error_302
    http_error_308 = http_error_302


class GitHubRestClient:
    """Fixed-origin GitHub REST implementation for live admission reads."""

    _MAX_REDIRECTS = 5
    _REDIRECT_STATUSES = frozenset({301, 302, 303, 307, 308})

    def __init__(
        self,
        *,
        repository: str,
        token: str,
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
        self._timeout = timeout
        self._opener = opener or self._open

    def _open(self, request: urllib.request.Request, timeout: int) -> bytes:
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
                location = (
                    error.headers.get("Location")
                    if error.headers is not None
                    else None
                )
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

    def _request(
        self,
        path: str,
    ) -> bytes:
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
            decoded = base64.b64decode(
                content.replace("\r", "").replace("\n", ""),
                validate=True,
            )
        except ValueError as error:
            raise GitHubRestError(
                "Governance content base64 is malformed"
            ) from error
        return GovernanceBlob(blob_oid=oid, content=decoded)
