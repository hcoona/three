"""Isolated complete-history Git reads for protected Governance."""

# ruff: noqa: EM101, TRY003

from __future__ import annotations

import base64
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Mapping

_GOVERNANCE_REF = "refs/heads/main"
_LOCAL_REF = "refs/wdv3/governance-main"
_REGULAR_NONEXECUTABLE_MODE = "100644"
_OBJECT_ID_LENGTHS = {"sha1": 40, "sha256": 64}
_TREE_ENTRY_FIELD_COUNT = 3


class GovernanceGitReadError(RuntimeError):
    """An isolated Governance Git proof failed closed."""


@dataclass(frozen=True, slots=True)
class GovernanceGitRead:
    """One exact protected-main Governance read."""

    main_sha: str
    object_format: str
    blob_oid: str
    content: bytes


class IsolatedGovernanceGitReader:
    """Fetch and prove protected Governance in a fresh bare repository."""

    def __init__(
        self,
        *,
        repository: str,
        token: str | None,
        remote_url: str | None = None,
        timeout_seconds: int = 120,
        parent: Path | None = None,
    ) -> None:
        """Create a reader for one fixed repository and remote."""
        if (
            type(repository) is not str
            or repository.count("/") != 1
            or any(not part for part in repository.split("/"))
        ):
            raise ValueError("Governance Git repository is malformed")
        if token is not None and (type(token) is not str or not token):
            raise ValueError("Governance Git token is malformed")
        if type(timeout_seconds) is not int or timeout_seconds <= 0:
            raise ValueError("Governance Git timeout is malformed")
        if remote_url is not None:
            if (
                type(remote_url) is not str
                or not remote_url
                or remote_url != remote_url.strip()
            ):
                raise ValueError("Governance Git remote URL is malformed")
            if token is not None:
                raise ValueError(
                    "Governance Git test remote cannot receive a token"
                )
        resolved_parent: Path | None = None
        if parent is not None:
            resolved_parent = parent.resolve(strict=True)
            if not resolved_parent.is_dir():
                raise ValueError(
                    "Governance Git parent must be an existing directory"
                )
        executable = shutil.which("git")
        if executable is None:
            raise GovernanceGitReadError("Git executable is unavailable")
        self._repository = repository
        self._token = token
        self._remote_url = remote_url or f"https://github.com/{repository}.git"
        self._timeout_seconds = timeout_seconds
        self._parent = resolved_parent
        self._git_executable = executable

    def _environment(self, root: Path) -> dict[str, str]:
        environment: dict[str, str] = {}
        for name in (
            "COMSPEC",
            "HTTPS_PROXY",
            "HTTP_PROXY",
            "LANG",
            "LC_ALL",
            "NO_PROXY",
            "PATH",
            "PATHEXT",
            "SSL_CERT_DIR",
            "SSL_CERT_FILE",
            "SYSTEMROOT",
            "WINDIR",
            "https_proxy",
            "http_proxy",
            "no_proxy",
        ):
            value = os.environ.get(name)
            if value:
                environment[name] = value
        home = root / "home"
        scratch = root / "scratch"
        home.mkdir(exist_ok=False)
        scratch.mkdir(exist_ok=False)
        environment.update(
            {
                "GIT_CONFIG_GLOBAL": os.devnull,
                "GIT_CONFIG_NOSYSTEM": "1",
                "GIT_NO_LAZY_FETCH": "1",
                "GIT_TERMINAL_PROMPT": "0",
                "HOME": os.fspath(home),
                "TEMP": os.fspath(scratch),
                "TMP": os.fspath(scratch),
                "TMPDIR": os.fspath(scratch),
                "USERPROFILE": os.fspath(home),
            }
        )
        if self._token is not None:
            basic = base64.b64encode(
                f"x-access-token:{self._token}".encode()
            ).decode("ascii")
            environment.update(
                {
                    "GIT_CONFIG_COUNT": "2",
                    "GIT_CONFIG_KEY_0": (
                        "http.https://github.com/.extraHeader"
                    ),
                    "GIT_CONFIG_VALUE_0": f"AUTHORIZATION: basic {basic}",
                    "GIT_CONFIG_KEY_1": "credential.helper",
                    "GIT_CONFIG_VALUE_1": "",
                }
            )
        return environment

    def _run(
        self,
        argv: tuple[str, ...],
        *,
        cwd: Path,
        environment: Mapping[str, str],
        failure: str,
        check: bool = True,
    ) -> subprocess.CompletedProcess[bytes]:
        try:
            completed = subprocess.run(  # noqa: S603
                (self._git_executable, "--no-replace-objects", *argv),
                cwd=cwd,
                env=environment,
                check=False,
                capture_output=True,
                timeout=self._timeout_seconds,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise GovernanceGitReadError(failure) from error
        if check and completed.returncode != 0:
            raise GovernanceGitReadError(failure)
        return completed

    def _git(
        self,
        git_dir: Path,
        *arguments: str,
        environment: Mapping[str, str],
        failure: str,
        check: bool = True,
    ) -> subprocess.CompletedProcess[bytes]:
        return self._run(
            ("--git-dir", os.fspath(git_dir), *arguments),
            cwd=git_dir.parent,
            environment=environment,
            failure=failure,
            check=check,
        )

    @staticmethod
    def _ascii_line(value: bytes, *, failure: str) -> str:
        if not value.endswith(b"\n") or value.count(b"\n") != 1:
            raise GovernanceGitReadError(failure)
        try:
            return value[:-1].decode("ascii", "strict")
        except UnicodeDecodeError as error:
            raise GovernanceGitReadError(failure) from error

    @staticmethod
    def _require_object_id(
        value: str,
        *,
        object_format: str,
        failure: str,
    ) -> str:
        length = _OBJECT_ID_LENGTHS.get(object_format)
        if (
            length is None
            or len(value) != length
            or any(character not in "0123456789abcdef" for character in value)
        ):
            raise GovernanceGitReadError(failure)
        return value

    def _require_complete_history(
        self,
        git_dir: Path,
        main_sha: str,
        *,
        environment: Mapping[str, str],
    ) -> None:
        shallow = self._ascii_line(
            self._git(
                git_dir,
                "rev-parse",
                "--is-shallow-repository",
                environment=environment,
                failure="Governance Git shallow state is inconclusive",
            ).stdout,
            failure="Governance Git shallow state is inconclusive",
        )
        if shallow != "false":
            raise GovernanceGitReadError("Governance Git history is shallow")
        if (git_dir / "info" / "grafts").exists():
            raise GovernanceGitReadError("Governance Git grafts are forbidden")
        if (git_dir / "objects" / "info" / "alternates").exists():
            raise GovernanceGitReadError(
                "Governance Git object alternates are forbidden"
            )
        replacements = self._git(
            git_dir,
            "for-each-ref",
            "--format=%(refname)",
            "refs/replace/",
            environment=environment,
            failure="Governance Git replacement-ref check failed",
        ).stdout
        if replacements:
            raise GovernanceGitReadError(
                "Governance Git replacement refs are forbidden"
            )
        ancestry = self._git(
            git_dir,
            "rev-list",
            "--parents",
            main_sha,
            environment=environment,
            failure="Governance Git ancestry is incomplete",
        ).stdout
        first_line = ancestry.splitlines()[0] if ancestry else b""
        encoded_main = main_sha.encode("ascii")
        if first_line != encoded_main and not first_line.startswith(
            encoded_main + b" "
        ):
            raise GovernanceGitReadError(
                "Governance Git ancestry is incomplete"
            )

    def _remote_object_format(
        self,
        root: Path,
        *,
        environment: Mapping[str, str],
    ) -> str:
        advertised = self._ascii_line(
            self._run(
                (
                    "ls-remote",
                    "--refs",
                    "--exit-code",
                    "--",
                    self._remote_url,
                    _GOVERNANCE_REF,
                ),
                cwd=root,
                environment=environment,
                failure="Governance main ref is unavailable",
            ).stdout,
            failure="Governance main ref advertisement is malformed",
        )
        raw_oid, separator, advertised_ref = advertised.partition("\t")
        if not separator or advertised_ref != _GOVERNANCE_REF:
            raise GovernanceGitReadError(
                "Governance main ref advertisement is malformed"
            )
        object_format = next(
            (
                candidate
                for candidate, length in _OBJECT_ID_LENGTHS.items()
                if len(raw_oid) == length
            ),
            "",
        )
        self._require_object_id(
            raw_oid,
            object_format=object_format,
            failure="Governance main ref advertisement is malformed",
        )
        return object_format

    def _require_continuity(  # noqa: PLR0913
        self,
        git_dir: Path,
        *,
        eligibility_main_sha: str,
        current_main_sha: str,
        object_format: str,
        path: str,
        environment: Mapping[str, str],
    ) -> None:
        self._require_object_id(
            eligibility_main_sha,
            object_format=object_format,
            failure="Eligibility main SHA is malformed",
        )
        exists = self._git(
            git_dir,
            "cat-file",
            "-e",
            f"{eligibility_main_sha}^{{commit}}",
            environment=environment,
            failure="Eligibility main commit is unavailable",
            check=False,
        )
        if exists.returncode != 0:
            raise GovernanceGitReadError(
                "Eligibility main commit is unavailable"
            )
        ancestry = self._git(
            git_dir,
            "merge-base",
            "--is-ancestor",
            eligibility_main_sha,
            current_main_sha,
            environment=environment,
            failure="Governance Git ancestry proof failed",
            check=False,
        )
        if ancestry.returncode == 1:
            raise GovernanceGitReadError(
                "Current main is not a descendant of eligibility main"
            )
        if ancestry.returncode != 0:
            raise GovernanceGitReadError("Governance Git ancestry proof failed")
        touches = self._git(
            git_dir,
            "rev-list",
            "--full-history",
            f"{eligibility_main_sha}..{current_main_sha}",
            "--",
            path,
            environment=environment,
            failure="Governance protected-path history proof failed",
        ).stdout
        if touches:
            raise GovernanceGitReadError(
                "Governance protected path changed after eligibility"
            )

    def _read_blob(
        self,
        git_dir: Path,
        *,
        main_sha: str,
        object_format: str,
        path: str,
        environment: Mapping[str, str],
    ) -> GovernanceGitRead:
        entry = self._git(
            git_dir,
            "ls-tree",
            "-z",
            "--full-tree",
            main_sha,
            "--",
            path,
            environment=environment,
            failure="Governance path lookup failed",
        ).stdout
        entries = entry.removesuffix(b"\0").split(b"\0") if entry else []
        if len(entries) != 1 or not entry.endswith(b"\0"):
            raise GovernanceGitReadError(
                "Governance path did not resolve to exactly one entry"
            )
        metadata, separator, raw_path = entries[0].partition(b"\t")
        if not separator:
            raise GovernanceGitReadError("Governance tree entry is malformed")
        try:
            fields = metadata.decode("ascii", "strict").split()
            resolved_path = raw_path.decode("utf-8", "strict")
        except UnicodeDecodeError as error:
            raise GovernanceGitReadError(
                "Governance tree entry is malformed"
            ) from error
        if (
            len(fields) != _TREE_ENTRY_FIELD_COUNT
            or fields[0] != _REGULAR_NONEXECUTABLE_MODE
            or fields[1] != "blob"
            or resolved_path != path
        ):
            raise GovernanceGitReadError(
                "Governance path is not the exact regular non-executable blob"
            )
        blob_oid = self._require_object_id(
            fields[2],
            object_format=object_format,
            failure="Governance blob OID is malformed",
        )
        content = self._git(
            git_dir,
            "cat-file",
            "blob",
            blob_oid,
            environment=environment,
            failure="Governance blob is unreadable",
        ).stdout
        return GovernanceGitRead(
            main_sha=main_sha,
            object_format=object_format,
            blob_oid=blob_oid,
            content=content,
        )

    def read(
        self,
        *,
        repository: str,
        ref: str,
        path: str,
        eligibility_main_sha: str | None = None,
    ) -> GovernanceGitRead:
        """Read current Governance and optionally prove continuity."""
        if repository != self._repository:
            raise GovernanceGitReadError("Governance repository mismatch")
        if ref != _GOVERNANCE_REF:
            raise GovernanceGitReadError("Governance ref mismatch")
        if (
            type(path) is not str
            or not path
            or path.startswith(("/", ".git/"))
            or "\\" in path
            or any(part in {"", ".", ".."} for part in path.split("/"))
        ):
            raise GovernanceGitReadError("Governance path is malformed")
        if eligibility_main_sha is not None and (
            type(eligibility_main_sha) is not str or not eligibility_main_sha
        ):
            raise GovernanceGitReadError("Eligibility main SHA is malformed")

        parent = os.fspath(self._parent) if self._parent is not None else None
        with tempfile.TemporaryDirectory(
            prefix="wdv3-governance-git-",
            dir=parent,
        ) as raw_root:
            root = Path(raw_root)
            git_dir = root / "repository.git"
            environment = self._environment(root)
            advertised_object_format = self._remote_object_format(
                root,
                environment=environment,
            )
            self._run(
                (
                    "init",
                    "--bare",
                    "--quiet",
                    f"--object-format={advertised_object_format}",
                    os.fspath(git_dir),
                ),
                cwd=root,
                environment=environment,
                failure="Governance Git initialization failed",
            )
            self._git(
                git_dir,
                "fetch",
                "--quiet",
                "--no-tags",
                "--no-write-fetch-head",
                "--force",
                "--",
                self._remote_url,
                f"+{ref}:{_LOCAL_REF}",
                environment=environment,
                failure="Governance Git fetch failed",
            )
            object_format = self._ascii_line(
                self._git(
                    git_dir,
                    "rev-parse",
                    "--show-object-format",
                    environment=environment,
                    failure="Governance Git object format is unavailable",
                ).stdout,
                failure="Governance Git object format is malformed",
            )
            main_sha = self._require_object_id(
                self._ascii_line(
                    self._git(
                        git_dir,
                        "rev-parse",
                        "--verify",
                        "--end-of-options",
                        f"{_LOCAL_REF}^{{commit}}",
                        environment=environment,
                        failure="Governance main ref is unavailable",
                    ).stdout,
                    failure="Governance main SHA is malformed",
                ),
                object_format=object_format,
                failure="Governance main SHA is malformed",
            )
            if object_format != advertised_object_format:
                raise GovernanceGitReadError(
                    "Governance Git object format changed during isolated read"
                )
            self._require_complete_history(
                git_dir,
                main_sha,
                environment=environment,
            )
            if eligibility_main_sha is not None:
                self._require_continuity(
                    git_dir,
                    eligibility_main_sha=eligibility_main_sha,
                    current_main_sha=main_sha,
                    object_format=object_format,
                    path=path,
                    environment=environment,
                )
            return self._read_blob(
                git_dir,
                main_sha=main_sha,
                object_format=object_format,
                path=path,
                environment=environment,
            )
