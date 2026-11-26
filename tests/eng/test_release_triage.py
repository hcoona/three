"""Exercise public release triage with real Git history and fake services."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "release_triage", ROOT / "eng/scripts/release_triage.py"
)
assert SPEC is not None
assert SPEC.loader is not None
triage = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = triage
SPEC.loader.exec_module(triage)

REPO = "hcoona/three"
API = f"repos/{REPO}"
SEARCH = (
    f"search/issues?q=repo:{REPO}+is:pr+is:merged+label:security"
    "&sort=updated&order=desc&per_page=100"
)
ISSUES = f"{API}/issues?labels=release-triage&state=all&per_page=100&page=1"
APP = "src/public/app/ImageOcclusionEditor"
LIB = "src/public/lib"
NPM = f"{LIB}/hexo-renderer-asciidoc"
NUGET = f"{LIB}/Memoization"
GENERATOR = f"{LIB}/Memoization.Generators"
PICO = f"{LIB}/Hjg.Pngcs"
EXTENSION = f"{LIB}/steam-account-history-to-csv"
REGISTRY = "https://api.nuget.org"


def git(root: Path, *args: str) -> str:
    """Run Git in the scenario repository."""
    return subprocess.run(  # noqa: S603
        ["git", "-c", "tag.gpgSign=false", *args],  # noqa: S607
        cwd=root,
        capture_output=True,
        text=True,
        check=True,
    ).stdout.strip()


def write(root: Path, path: str, text: str) -> None:
    """Add or replace a scenario file."""
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8")


def commit(root: Path) -> str:
    """Commit scenario inputs at a deterministic time."""
    git(root, "add", ".")
    git(root, "commit", "-qm", "Set inputs")
    return git(root, "rev-parse", "HEAD")


@pytest.fixture
def repo(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    """Build the directory skeleton required by the inventory check."""
    git(tmp_path, "init", "-q")
    git(tmp_path, "config", "user.name", "Release Triage Test")
    git(tmp_path, "config", "user.email", "triage@example.invalid")
    monkeypatch.setenv("GIT_AUTHOR_DATE", "2026-03-01T12:00:00Z")
    monkeypatch.setenv("GIT_COMMITTER_DATE", "2026-03-01T12:00:00Z")
    (tmp_path / "src/public/app").mkdir(parents=True, exist_ok=True)
    for path in triage.NON_RELEASE_PUBLIC_DIRS:
        (tmp_path / path).mkdir(parents=True, exist_ok=True)
    return tmp_path


def declare(
    root: Path, path: str, uid: str, build: str, manifest: tuple[str, str]
) -> None:
    """Give a public directory a release declaration and its manifest."""
    entry, text = manifest
    write(root, f"{path}/{entry}", text)
    write(
        root,
        f"{path}/{triage.DESCRIPTOR}",
        f"schema: workflow-delivery/v3/release-unit\nrelease-unit: {uid}\n"
        f"builds:\n  - definition: {build}\n    entry-point: {entry}\n",
    )


class Services:
    """Stand-ins for registry reads and the repository's GitHub endpoints."""

    def __init__(self) -> None:
        """Initialize independent response and ticket stores."""
        self.api: dict = {
            SEARCH: {"items": []},
            f"{API}/releases?per_page=100&page=1": [],
        }
        self.registry: dict[str, dict | list | bytes] = {}
        self.issues: list[dict] = []
        self.comments: dict[str, list[dict]] = {}
        self.writes: list[tuple[str, str, dict | None]] = []
        self.reads: list[str] = []

    def gh(
        self, method: str, path: str, body: dict | None
    ) -> dict | list | None:
        """Return configured reads and reflect ticket writes in the fake."""
        if method == "GET":
            self.reads.append(path)
            if path.startswith(f"{API}/issues?labels=release-triage"):
                page = int(path.rsplit("&page=", 1)[1])
                return self.issues[(page - 1) * 100 : page * 100]
            if "/comments?per_page=100&page=" in path:
                base, _, raw_page = path.partition("?per_page=100&page=")
                page = int(raw_page)
                items = self.comments.get(base, [])
                return items[(page - 1) * 100 : page * 100]
            return self.api[path]
        self.writes.append((method, path, body))
        bot = {"user": {"login": "github-actions[bot]"}}
        if path.endswith("/comments"):
            self.comments.setdefault(path, []).append({**bot, **body})
        if path == f"{API}/issues":
            self.issues.insert(
                0,
                {
                    **bot,
                    **body,
                    "number": len(self.issues) + 1,
                    "state": "open",
                },
            )
        if method == "PATCH":
            target = next(
                i for i in self.issues if path.endswith(f"/{i['number']}")
            )
            target.update(body)
        return None

    def fetch(self, url: str) -> bytes | None:
        """Serve the configured public package metadata."""
        value = self.registry.get(url)
        if value is None or isinstance(value, bytes):
            return value
        return json.dumps(value).encode()

    def evaluate(self, root: Path) -> list:
        """Run triage end to end without contacting GitHub or registries."""
        return triage.evaluate(
            root, triage.discover_public_units(root), REPO, self.gh, self.fetch
        )

    def npm(self, sha: str | None) -> None:
        """Publish the npm fixture at a local commit or leave it unreleased."""
        url = "https://registry.npmjs.org/hexo-renderer-asciidoc"
        if sha is None:
            self.registry.pop(url, None)
            return
        self.registry[url] = {
            "dist-tags": {"latest": "1.0.0"},
            "versions": {"1.0.0": {"gitHead": sha}},
        }

    def store(  # noqa: PLR0913 - independent deployment and run evidence.
        self,
        name: str,
        number: int,
        sha: str,
        *,
        state: str = "success",
        conclusion: str = "success",
        run: int | None = None,
    ) -> None:
        """Set the deployment's latest status and its linked Actions result."""
        run = run or number
        query = (
            f"deployments?environment=workflow-delivery-v3-{name}"
            "&per_page=100&page=1"
        )
        self.api[f"{API}/{query}"] = [{"id": number, "sha": sha}]
        self.api[f"{API}/deployments/{number}/statuses?per_page=10"] = [
            {
                "state": state,
                "target_url": f"https://github.com/{REPO}/actions/runs/{run}",
            }
        ]
        self.api[f"{API}/actions/runs/{run}"] = {
            "path": f".github/workflows/{triage.STORE_WORKFLOW}",
            "status": "completed",
            "conclusion": conclusion,
        }


def test_real_inventory_is_complete_and_offline(
    capsys: pytest.CaptureFixture[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify repository declarations, exemptions and a silent offline gate."""

    def forbidden(*args: object) -> None:
        pytest.fail(f"unexpected network call: {args}")

    monkeypatch.setattr(triage, "gh", forbidden)
    monkeypatch.setattr(triage, "fetch", forbidden)
    assert triage.main(["--root", str(ROOT), "--check-inventory"]) == 0
    assert capsys.readouterr() == ("", "")
    expected = {
        p.parent.relative_to(ROOT).as_posix()
        for p in (ROOT / "src/public").rglob(triage.DESCRIPTOR)
    }
    assert expected
    assert {u.directory for u in triage.discover_public_units(ROOT)} == expected


@pytest.mark.parametrize(
    ("defect", "message"),
    [
        ("unregistered", "unregistered-pkg: missing"),
        ("missing exemption", "Memoization.Generators: stale"),
        ("stale exemption", "Memoization.Generators: stale"),
        ("unknown build", "unsupported build definition 'unknown'"),
        ("invalid manifest", "package name must be a non-empty string"),
        ("empty gemspec", "non-empty UTF-8 .gemspec"),
    ],
)
def test_inventory_rejects_unclassified_public_directories(
    repo: Path, defect: str, message: str, capsys: pytest.CaptureFixture[str]
) -> None:
    """Reject uncovered directories, stale exemptions and malformed units."""
    match defect:
        case "unregistered":
            (repo / LIB / "unregistered-pkg").mkdir()
        case "missing exemption":
            (repo / GENERATOR).rmdir()
        case "stale exemption":
            write(repo, f"{GENERATOR}/{triage.DESCRIPTOR}", "")
        case "unknown build":
            declare(
                repo,
                NPM,
                "hexo",
                "node/npm-package-v1",
                ("package.json", npm_manifest()),
            )
            declare(
                repo, f"{NPM}/nested", "nested", "unknown", ("manifest", "")
            )
        case "empty gemspec":
            declare(
                repo, f"{LIB}/gem", "gem", "ruby/gem-v1", ("gem.gemspec", " \n")
            )
        case _:
            declare(
                repo,
                NPM,
                "hexo",
                "node/npm-package-v1",
                ("package.json", '{"name": ""}'),
            )
    assert triage.main(["--check-inventory", "--root", str(repo)]) == 1
    out, err = capsys.readouterr()
    assert out == ""
    assert message in err


def npm_manifest(
    runtime: str = "1.0.0",
    development: str = "1",
    module_type: str = "module",
    scripts: dict[str, str] | None = None,
) -> str:
    """Produce packaging inputs and a development-only version."""
    return json.dumps(
        {
            "name": "hexo-renderer-asciidoc",
            "type": module_type,
            "scripts": scripts
            or {"version:stamp": "node stamp.mjs", "lint:biome": "biome lint"},
            "dependencies": {"@asciidoctor/core": runtime},
            "devDependencies": {"ruffle": development},
        }
    )


@pytest.fixture
def services(repo: Path) -> Services:
    """Seed configurable Official baselines for a mixed-tier repository."""
    declare(
        repo,
        APP,
        "image-occlusion-editor",
        "dotnet/github-release-assets-v1",
        (
            "nested/App.csproj",
            (
                "<Project><ItemGroup>"
                '<PackageReference Include="IO.Github.Hcoona.Pngcs"/>'
                "</ItemGroup></Project>"
            ),
        ),
    )
    declare(
        repo,
        NPM,
        "hexo-renderer-asciidoc",
        "node/npm-package-v1",
        ("package.json", npm_manifest()),
    )
    declare(
        repo,
        NUGET,
        "memoization",
        "dotnet/nuget-package-v1",
        (
            "Memoization.csproj",
            (
                '<Project xmlns="urn:msbuild">'
                "<PropertyGroup><PackageId>Memoization</PackageId></PropertyGroup>"
                '<ItemGroup><PackageReference Include="Runtime"/>'
                '<ProjectReference Include="..\\Memoization.Generators'
                '\\Generator.csproj"/></ItemGroup></Project>'
            ),
        ),
    )
    declare(
        repo,
        PICO,
        "hjg-pngcs",
        "dotnet/nuget-package-v1",
        (
            "Pngcs.csproj",
            (
                "<Project><PropertyGroup>"
                "<PackageId>IO.Github.Hcoona.Pngcs</PackageId>"
                "</PropertyGroup></Project>"
            ),
        ),
    )
    write(repo, f"{GENERATOR}/Generator.csproj", "<Project/>")
    base = commit(repo)
    tag = "release/image-occlusion-editor/v1.0.0"
    git(repo, "tag", "-am", "Official", tag)
    service = Services()
    service.api[f"{API}/releases?per_page=100&page=1"] = [
        {"tag_name": f"release/other/v{i}", "draft": False, "prerelease": False}
        for i in range(100)
    ]
    service.api[f"{API}/releases?per_page=100&page=2"] = [
        {"tag_name": tag, "draft": False, "prerelease": False}
    ]
    service.npm(base)
    for pkg in ("memoization", "io.github.hcoona.pngcs"):
        flat = f"{REGISTRY}/v3-flatcontainer/{pkg}"
        service.registry[f"{flat}/index.json"] = {
            "versions": ["1.0.0", "2.0.0-beta"]
        }
        service.registry[f"{flat}/1.0.0/{pkg}.nuspec"] = (
            '<package xmlns="urn:nuget"><metadata>'
            f'<repository commit="{base}"/></metadata></package>'
        ).encode()
    return service


@pytest.mark.parametrize(
    ("path", "text", "units"),
    [
        (f"{NPM}/package.json", npm_manifest(development="2"), set()),
        (f"{APP}/nested/README.md", "Instructions", set()),
        (f"{NUGET}/tests/Sample.cs", "test", set()),
        (f"{NUGET}/ReleaseNotes.md", "Notes", set()),
        (f"{NUGET}/packages.lock.json", "{}", set()),
        (
            f"{LIB}/Directory.Packages.props",
            '<Project><PackageVersion Include="Other" Version="1"/></Project>',
            set(),
        ),
        ("global.json", "{}", {"image-occlusion-editor"}),
        (f"{APP}/nested/packages.lock.json", "{}", {"image-occlusion-editor"}),
        (f"{PICO}/Image.cs", "source", {"hjg-pngcs"}),
        (
            f"{NPM}/package.json",
            npm_manifest(runtime="2.0.0"),
            {"hexo-renderer-asciidoc"},
        ),
        (
            f"{NPM}/package.json",
            npm_manifest(module_type="commonjs"),
            {"hexo-renderer-asciidoc"},
        ),
        (
            f"{NPM}/package.json",
            npm_manifest(
                scripts={
                    "version:stamp": "node v2.mjs",
                    "lint:biome": "biome lint",
                }
            ),
            {"hexo-renderer-asciidoc"},
        ),
        (
            f"{NPM}/package.json",
            npm_manifest(
                scripts={
                    "version:stamp": "node stamp.mjs",
                    "lint:biome": "biome check",
                }
            ),
            set(),
        ),
        (
            f"{LIB}/Directory.Packages.props",
            (
                '<Project><PackageVersion Include="runtime" '
                'Version="[2.0,)"/></Project>'
            ),
            {"memoization"},
        ),
        (f"{GENERATOR}/Generator.cs", "source", {"memoization"}),
    ],
)
def test_filters_tier_specific_delivery_inputs(
    repo: Path, services: Services, path: str, text: str, units: set[str]
) -> None:
    """Keep noisy changes quiet and report the owning units for real inputs."""
    write(repo, path, text)
    commit(repo)
    assert {item.unit.id for item in services.evaluate(repo)} == units


def test_security_pr_can_surface_a_library_lockfile(
    repo: Path, services: Services
) -> None:
    """Recognize a merged security PR even when only a lockfile changed."""
    write(repo, f"{NUGET}/packages.lock.json", "{}")
    merge = commit(repo)
    services.api[SEARCH] = {"items": [{"number": 17}]}
    services.api[f"{API}/pulls/17"] = {
        "merge_commit_sha": merge,
        "head": {"sha": "1" * 40},
    }
    assert [item.unit.id for item in services.evaluate(repo)] == ["memoization"]
    assert services.reads.count(SEARCH) == 1

    # Also advances the dismissal token when the library is unreleased.
    services.registry.pop(f"{REGISTRY}/v3-flatcontainer/memoization/index.json")
    services.api[SEARCH] = {"items": []}
    [before] = services.evaluate(repo)
    services.api[SEARCH] = {"items": [{"number": 17}]}
    [after] = services.evaluate(repo)
    assert after.token != before.token


def test_browser_stores_need_verified_current_runs(repo: Path) -> None:
    """Reject status-only success and detect a verified but lagging store."""
    declare(
        repo,
        EXTENSION,
        "steam-account-history-to-csv",
        "node/browser-extension-v1",
        ("package.json", '{"name":"example"}'),
    )
    older = commit(repo)
    write(repo, f"{EXTENSION}/src/background.ts", "new delivery input")
    head = commit(repo)
    git(repo, "tag", "release/steam-account-history-to-csv/v1.0.0")
    fake = Services()
    chrome, firefox, edge = triage.STORES
    fake.store(chrome, 1, head, state="failure")
    fake.store(firefox, 2, head, conclusion="failure")
    fake.store(edge, 3, older)
    [item] = fake.evaluate(repo)
    assert item.reasons == (
        "chrome-web-store: No verified v3 run yet",
        "firefox-amo: No verified v3 run yet",
        f"edge-addons: Delivery inputs changed ({older[:7]})",
    )
    fake.store(chrome, 4, head)
    fake.store(firefox, 5, head, run=4)
    fake.reads.clear()
    [item] = fake.evaluate(repo)
    assert item.reasons == (
        f"edge-addons: Delivery inputs changed ({older[:7]})",
    )
    assert fake.reads.count(f"{API}/actions/runs/4") == 1
    fake.store(edge, 6, head)
    edge_run = fake.api[f"{API}/actions/runs/6"]
    edge_run["path"] = ".github/workflows/ci.yml"
    [item] = fake.evaluate(repo)
    assert item.reasons == ("edge-addons: No verified v3 run yet",)
    edge_run["path"] = f".github/workflows/{triage.STORE_WORKFLOW}"
    assert fake.evaluate(repo) == []


@pytest.mark.parametrize(
    ("path", "before", "after", "pending"),
    [
        ("pnpm-lock.yaml", "", "wxt", True),
        ("pnpm-lock.yaml", "", "unrelated", False),
        (
            f"{EXTENSION}/version.json",
            '{"version":"1.2.0","inherit":true}',
            '{"version":"1.3.0","inherit":true}',
            True,
        ),
        (f"{EXTENSION}/LICENSE", "old", "new", True),
    ],
)
def test_browser_store_watches_only_delivery_lock_inputs(
    repo: Path, path: str, before: str, after: str, pending: bool
) -> None:
    """Track importer lock entries without unrelated workspace noise."""
    declare(
        repo,
        EXTENSION,
        "browser",
        "node/browser-extension-v1",
        ("package.json", '{"name":"example"}'),
    )
    if path == "pnpm-lock.yaml":
        before, after = browser_lock(), browser_lock(after)
    write(repo, path, before)
    head = commit(repo)
    fake = Services()
    for number, store in enumerate(triage.STORES, 1):
        fake.store(store, number, head)
    assert fake.evaluate(repo) == []
    write(repo, path, after)
    commit(repo)
    assert [item.unit.id for item in fake.evaluate(repo)] == (
        ["browser"] if pending else []
    )


def browser_lock(update: str = "") -> str:
    """Build a minimal PNPM v9 lock with importer and unrelated entries."""
    wxt = "0.21.6" if update == "wxt" else "0.21.5"
    return json.dumps(
        {
            "lockfileVersion": "9.0",
            "importers": {
                EXTENSION: {
                    "devDependencies": {"wxt": {"version": f"{wxt}(vite@8)"}}
                },
                ".": {
                    "dependencies": {
                        "lint": {
                            "version": "2.0.0"
                            if update == "unrelated"
                            else "1.0.0"
                        }
                    }
                },
            },
        }
    )


def test_dismissed_browser_ticket_ignores_docs_and_partial_verification(
    repo: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Keep the original delivery head until a delivery-affecting edit lands."""
    declare(
        repo,
        EXTENSION,
        "browser",
        "node/browser-extension-v1",
        ("package.json", '{"name":"example"}'),
    )
    head = commit(repo)
    fake = Services()
    for number, store in enumerate(triage.STORES, 1):
        fake.store(store, number, head, state="failure")
    monkeypatch.setattr(triage, "gh", fake.gh)
    monkeypatch.setattr(triage, "fetch", fake.fetch)
    args = ["--root", str(repo), "--repo", REPO]
    [original] = fake.evaluate(repo)
    assert triage.main(args) == 0
    [issue] = fake.issues
    issue.update(state="closed", closed_at="2026-03-01T13:00:00Z")
    fake.writes.clear()

    write(repo, f"{EXTENSION}/README.md", "Instructions")
    commit(repo)
    [docs] = fake.evaluate(repo)
    assert docs.token == original.token
    assert triage.main(args) == 0
    assert fake.writes == []

    fake.store("chrome-web-store", 4, head)
    [partially_verified] = fake.evaluate(repo)
    assert partially_verified.reasons == (
        "firefox-amo: No verified v3 run yet",
        "edge-addons: No verified v3 run yet",
    )
    assert partially_verified.token == original.token
    assert triage.main(args) == 0
    assert fake.writes == []

    # Reverting one file while another stays changed relative to a lagging
    # store must keep a stable token when an unverified store catches up.
    write(repo, f"{EXTENSION}/src/a.ts", "v2")
    write(repo, f"{EXTENSION}/src/b.ts", "v2")
    mid = commit(repo)
    fake.store("firefox-amo", 5, head)
    (repo / f"{EXTENSION}/src/b.ts").unlink()
    latest = commit(repo)
    assert mid != latest
    assert triage.main(args) == 0
    assert len(fake.issues) == 2
    fake.issues[0].update(state="closed", closed_at="2026-03-01T14:00:00Z")
    fake.writes.clear()
    fake.store("edge-addons", 6, latest)
    assert triage.main(args) == 0
    assert fake.writes == []


@pytest.mark.parametrize(
    "version_path", [f"{NPM}/version.json", "version.json"]
)
def test_npm_official_beta_clears_the_unit_while_latest_is_older(
    repo: Path,
    version_path: str,
) -> None:
    """Use the declared prerelease dist-tag and choose by publication time."""
    declare(
        repo,
        NPM,
        "hexo-renderer-asciidoc",
        "node/npm-package-v1",
        ("package.json", npm_manifest()),
    )
    write(repo, version_path, '{"version":"4.0.0-beta.{height}"}')
    if version_path == "version.json":
        write(repo, f"{NPM}/version.json", '{"inherit":true}')
    stable = commit(repo)
    write(repo, f"{NPM}/src/index.ts", "new delivery")
    beta = commit(repo)
    fake = Services()
    dist = {
        "dist-tags": {"latest": "3.0.0"},
        "versions": {
            "3.0.0": {"gitHead": stable},
            "4.0.0-beta.1": {"gitHead": beta},
        },
        "time": {
            "3.0.0": "2026-03-01T00:00:00Z",
            "4.0.0-beta.1": "2026-03-02T00:00:00Z",
        },
    }
    fake.registry["https://registry.npmjs.org/hexo-renderer-asciidoc"] = dist
    assert [item.unit.id for item in fake.evaluate(repo)] == [
        "hexo-renderer-asciidoc"
    ]
    dist["dist-tags"]["beta"] = "4.0.0-beta.1"
    assert fake.evaluate(repo) == []
    dist["time"]["3.0.0"] = "2026-03-03T00:00:00Z"
    assert [item.unit.id for item in fake.evaluate(repo)] == [
        "hexo-renderer-asciidoc"
    ]


def test_nuget_prerelease_baseline_follows_the_declared_version_line(
    repo: Path,
    services: Services,
) -> None:
    """Use the prerelease artifact only for a unit on that release line."""
    flat = f"{REGISTRY}/v3-flatcontainer/memoization"
    services.registry[f"{flat}/index.json"] = {"versions": ["1.0.0"]}
    write(repo, f"{NUGET}/version.json", '{"version":"2.0.0-beta.{height}"}')
    sha = commit(repo)
    assert [item.unit.id for item in services.evaluate(repo)] == ["memoization"]
    services.registry[f"{flat}/index.json"] = {
        "versions": ["1.0.0", "2.0.0-rc.1"]
    }
    services.registry[f"{flat}/2.0.0-rc.1/memoization.nuspec"] = (
        f'<package><metadata><repository commit="{sha}"/></metadata></package>'
    ).encode()
    assert [item.unit.id for item in services.evaluate(repo)] == ["memoization"]
    services.registry[f"{flat}/index.json"] = {
        "versions": ["1.0.0", "2.0.0-beta", "2.0.0-rc.1"]
    }
    services.registry[f"{flat}/2.0.0-beta/memoization.nuspec"] = (
        f'<package><metadata><repository commit="{sha}"/></metadata></package>'
    ).encode()
    assert services.evaluate(repo) == []

    # RubyGems keeps alpha and alpha-preview prerelease lines distinct.
    gem_dir = f"{LIB}/gem-line"
    declare(
        repo,
        gem_dir,
        "gem-line",
        "ruby/gem-v1",
        (
            "gem-line.gemspec",
            'Gem::Specification.new do |s|\n  s.name = "gem-line"\nend\n',
        ),
    )
    write(repo, f"{gem_dir}/version.json", '{"version":"2.1.0-alpha.{height}"}')
    gem_sha = commit(repo)
    git(repo, "tag", "release/gem-line/v2.1.0.pre.alpha.pre.preview.1", gem_sha)
    gem_url = "https://rubygems.org/api/v1/versions/gem-line.json"
    services.registry[gem_url] = [
        {
            "number": "2.1.0.pre.alpha.pre.preview.1",
            "prerelease": True,
            "created_at": "2026-03-05T00:00:00Z",
        }
    ]
    assert [item.unit.id for item in services.evaluate(repo)] == ["gem-line"]
    git(repo, "tag", "release/gem-line/v2.1.0.pre.alpha.1", gem_sha)
    services.registry[gem_url].append(
        {
            "number": "2.1.0.pre.alpha.1",
            "prerelease": True,
            "created_at": "2026-03-04T00:00:00Z",
        }
    )
    assert services.evaluate(repo) == []

    # PyPI recognizes a published .dev version even while info.version is 2.0.0.
    py_dir = f"{LIB}/nbgv-python"
    declare(
        repo,
        py_dir,
        "nbgv-python",
        "python/distribution-set-v1",
        (
            "pyproject.toml",
            '[project]\nname = "nbgv-python"\nversion = "2.1.0-dev1"\n',
        ),
    )
    old_py = commit(repo)
    git(repo, "tag", "release/nbgv-python/v2.0.0", old_py)
    write(repo, f"{py_dir}/src/nbgv_python/cli.py", "updated")
    dev_py = commit(repo)
    py_url = "https://pypi.org/pypi/nbgv-python/json"
    services.registry[py_url] = {
        "info": {"version": "2.0.0"},
        "releases": {
            "2.0.0": [{"upload_time_iso_8601": "2026-03-01T00:00:00Z"}],
        },
    }
    assert [item.unit.id for item in services.evaluate(repo)] == ["nbgv-python"]
    git(repo, "tag", "release/nbgv-python/v2.1.0.dev1", dev_py)
    services.registry[py_url]["releases"]["2.1.0.dev1"] = [
        {"upload_time_iso_8601": "2026-03-02T00:00:00Z"}
    ]
    assert services.evaluate(repo) == []


def test_nuget_uses_publication_date_without_a_local_commit(
    repo: Path,
    services: Services,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Compare referenced versions only in commits after publication."""
    spec = f"{REGISTRY}/v3-flatcontainer/memoization/1.0.0/memoization.nuspec"
    services.registry[spec] = b"<package><metadata/></package>"
    leaf = f"{REGISTRY}/v3/registration5-gz-semver2/memoization/1.0.0.json"
    services.registry[leaf] = {"published": "2026-03-02T00:00:00Z"}
    assert services.evaluate(repo) == []

    monkeypatch.setenv("GIT_COMMITTER_DATE", "2026-03-03T00:00:00Z")
    write(repo, "Directory.Packages.props", "<Project/>")
    commit(repo)
    assert services.evaluate(repo) == []
    write(
        repo,
        "Directory.Packages.props",
        '<Project><PackageVersion Include="Runtime"'
        ' Version="[2.0,)"/></Project>',
    )
    commit(repo)
    assert [item.unit.id for item in services.evaluate(repo)] == ["memoization"]


@pytest.mark.parametrize(
    ("declaration", "registry", "manifests"),
    [
        (
            ("python/distribution-set-v1", "pyproject.toml"),
            (
                "https://pypi.org/pypi/example/json",
                {
                    "info": {"version": "1.0"},
                    "releases": {
                        "1.0": [
                            {"upload_time_iso_8601": "2026-03-03T00:00:00Z"},
                            {"upload_time_iso_8601": "2026-03-02T00:00:00Z"},
                        ]
                    },
                },
            ),
            (
                '[project]\nname = "example"\n',
                (
                    '[project]\nname = "example"\n'
                    '[dependency-groups]\ndev = ["ruff"]\n'
                    "[tool.ruff]\nline-length = 80\n"
                ),
                (
                    '[project]\nname = "example"\n'
                    '[tool.hatch.build.targets.wheel]\npackages = ["src/x"]\n'
                ),
            ),
        ),
        (
            ("ruby/gem-v1", "example.gemspec"),
            (
                "https://rubygems.org/api/v1/versions/example.json",
                [
                    {
                        "number": "2.0.alpha",
                        "prerelease": True,
                        "created_at": "2026-03-03T00:00:00Z",
                    },
                    {
                        "number": "1.0",
                        "prerelease": False,
                        "created_at": "2026-03-02T00:00:00Z",
                    },
                ],
            ),
            (
                'Gem::Specification.new do |s|\n  s.name = "example"\nend\n',
                (
                    "Gem::Specification.new do |s|\n"
                    '  s.name = "example"\n'
                    '  s.add_development_dependency "rubocop"\nend\n'
                ),
                (
                    "Gem::Specification.new do |s|\n"
                    '  s.name = "example"\n'
                    '  s.add_dependency "tool", ">= 2"\nend\n'
                ),
            ),
        ),
    ],
)
def test_python_and_ruby_use_runtime_inputs_after_publication(
    repo: Path,
    monkeypatch: pytest.MonkeyPatch,
    declaration: tuple[str, str],
    registry: tuple[str, dict | list],
    manifests: tuple[str, str, str],
) -> None:
    """Ignore development changes; detect runtime updates after publication."""
    definition, entry = declaration
    url, metadata = registry
    base, noise, runtime = manifests
    project = f"{LIB}/example"
    declare(repo, project, "example", definition, (entry, base))
    commit(repo)
    services = Services()
    services.registry[url] = metadata
    monkeypatch.setenv("GIT_COMMITTER_DATE", "2026-03-02T12:00:00Z")
    write(repo, f"{project}/{entry}", noise)
    commit(repo)
    assert services.evaluate(repo) == []
    write(repo, f"{project}/{entry}", runtime)
    commit(repo)
    assert [item.unit.id for item in services.evaluate(repo)] == ["example"]


def test_dry_run_writes_summary_without_touching_issues(
    repo: Path,
    services: Services,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Write owner commands or the all-clear without touching issues."""
    monkeypatch.setattr(triage, "gh", services.gh)
    monkeypatch.setattr(triage, "fetch", services.fetch)
    summary = repo / "summary.md"
    args = [
        "--root",
        str(repo),
        "--repo",
        REPO,
        "--dry-run",
        "--summary-file",
        str(summary),
    ]
    assert triage.main(args) == 0
    assert capsys.readouterr().out == summary.read_text(encoding="utf-8")
    assert summary.read_text(encoding="utf-8") == triage.NO_CHANGES

    write(repo, "global.json", '{"sdk": {"version": "11.0.100"}}')
    commit(repo)
    assert triage.main(args) == 0
    report = summary.read_text(encoding="utf-8")
    assert report == capsys.readouterr().out
    assert "gh workflow run workflow-delivery-v3-github-release.yml" in report
    assert "-f unit=image-occlusion-editor -f dry-run=true" in report
    assert "-f unit=image-occlusion-editor -f dry-run=false" in report
    assert services.writes == []
    assert ISSUES not in services.reads


def test_issue_lifecycle_dismisses_unchanged_inputs_and_notifies_advances(
    repo: Path, services: Services, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Exercise triage through the CLI against a single fake issue timeline."""
    monkeypatch.setattr(triage, "gh", services.gh)
    monkeypatch.setattr(triage, "fetch", services.fetch)
    args = ["--root", str(repo), "--repo", REPO]

    write(repo, f"{NPM}/src/index.ts", "deliverable")
    write(repo, f"{NUGET}/Library.cs", "deliverable")
    commit(repo)
    assert triage.main(args) == 0
    assert [path for _, path, _ in services.writes] == [
        f"{API}/labels",
        f"{API}/issues",
    ]
    [issue] = services.issues
    assert issue["assignees"] == ["hcoona"]
    assert issue["body"].startswith("@hcoona\n")
    assert issue["body"].index("Tier 1:") < issue["body"].index("Tier 2:")
    assert (
        "npm:registry=npmjs,unit=hexo-renderer-asciidoc,dry-run=true"
        in issue["body"]
    )
    assert (
        "nuget:registry=nugetorg,unit=memoization,dry-run=false"
        in issue["body"]
    )

    issue.update(state="closed", closed_at="2026-03-01T13:00:00Z")
    services.writes.clear()
    assert triage.main(args) == 0
    flat = f"{REGISTRY}/v3-flatcontainer/memoization/1.0.0/memoization.nuspec"
    head = git(repo, "rev-parse", "HEAD")
    services.registry[flat] = (
        f'<package><repository commit="{head}"/></package>'.encode()
    )
    write(repo, f"{NPM}/package.json", npm_manifest(development="2"))
    commit(repo)
    assert triage.main(args) == 0
    assert services.writes == []

    write(repo, f"{NPM}/src/index.ts", "new delivery")
    commit(repo)
    assert triage.main(args) == 0
    assert len(services.issues) == 2
    services.writes.clear()
    original_body = services.issues[0]["body"]
    write(repo, f"{NPM}/src/index.ts", "next delivery")
    commit(repo)
    assert triage.main(args) == 0
    comments = f"{API}/issues/2/comments"
    [write_call] = services.writes
    assert write_call[:2] == ("POST", comments)
    assert services.issues[0]["body"] == original_body
    updated = write_call[2]["body"]
    assert updated.startswith(
        "@hcoona New or updated release changes (`hexo-renderer-asciidoc`):"
    )
    assert "## Tier 1:" in updated
    assert "<!-- release-triage-state:" in updated
    services.writes.clear()
    # Pad page 1 with 105 human comments so the bot marker sits on page 2.
    services.comments[comments] = [
        *(
            {
                "user": {"login": "hcoona"},
                "body": (
                    "> <!-- release-triage-state: "
                    '{"hexo-renderer-asciidoc": "stale"} -->'
                ),
            }
            for _ in range(105)
        ),
        services.comments[comments][-1],
    ]
    assert triage.main(args) == 0
    assert services.writes == []

    services.issues[0].update(state="closed", closed_at="2026-03-01T14:00:00Z")
    assert triage.main(args) == 0
    assert services.writes == []
    assert f"{comments}?per_page=100&page=2" in services.reads


def test_issue_closes_when_all_official_baselines_catch_up(
    repo: Path,
    services: Services,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Close the issue, with a final comment, instead of rewriting its body."""
    monkeypatch.setattr(triage, "gh", services.gh)
    monkeypatch.setattr(triage, "fetch", services.fetch)
    args = ["--root", str(repo), "--repo", REPO]
    write(repo, f"{NPM}/src/index.ts", "next delivery")
    head = commit(repo)
    assert triage.main(args) == 0
    services.writes.clear()
    services.npm(head)
    assert triage.main(args) == 0
    assert [(method, path) for method, path, _ in services.writes] == [
        ("POST", f"{API}/issues/1/comments"),
        ("PATCH", f"{API}/issues/1"),
    ]
    assert services.writes[0][2]["body"] == (
        "All public release units are aligned with their Official baselines; "
        "closing this triage ticket."
    )
    assert services.issues[0]["state"] == "closed"
