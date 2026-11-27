"""Scenario tests for the supply-chain release-age and public-floor checker."""

from __future__ import annotations

import importlib.util
import json
import re
import shutil
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "eng/scripts/check_supply_chain_policy.py"


def _load_checker():
    spec = importlib.util.spec_from_file_location(
        "check_supply_chain_policy",
        SCRIPT,
    )
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


CHECKER = _load_checker()

FIXTURE_PATHS = (
    "mise.toml",
    "pnpm-workspace.yaml",
    "eng/renovate/.npmrc",
    "pyproject.toml",
    "uv.lock",
    "renovate.json",
    "src/public/lib/Directory.Packages.props",
    (
        "src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/"
        "MicrosoftExtensions.Options.DedupChangeExtensions.csproj"
    ),
    "src/public/lib/WebHdfs.Extensions.FileProviders/WebHdfs.Extensions.FileProviders.csproj",
    "src/public/lib/Memoization.Generators/Memoization.Generators.csproj",
    "src/public/lib/nbgv-python/pyproject.toml",
    "src/public/lib/hexo-renderer-asciidoc/package.json",
    "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/pnpm-workspace.yaml",
    "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/package.json",
    "src/public/lib/steam-account-history-to-csv/package.json",
    "src/public/lib/asciidoctor-latexmath/Gemfile",
    "src/public/lib/asciidoctor-latexmath/Gemfile.lock",
    "src/public/lib/asciidoctor-latexmath/.bundle/config",
    "src/public/lib/asciidoctor-latexmath/asciidoctor-latexmath.gemspec",
    "src/public/lib/asciidoctor-latexmath/lib/asciidoctor/latexmath/version.rb",
    "tests/private/app/workflow-delivery/Native/Ruby/Gemfile",
    "tests/private/app/workflow-delivery/Native/Ruby/Gemfile.lock",
    "tests/private/app/workflow-delivery/Native/Ruby/.bundle/config",
    "tests/private/app/workflow-delivery/Native/Ruby/contexts/bundler-2.4/Gemfile",
    "tests/private/app/workflow-delivery/Native/Ruby/contexts/bundler-2.4/Gemfile.lock",
)


def _seed_workspace(tmp_path: Path) -> Path:
    for rel in FIXTURE_PATHS:
        source = ROOT / rel
        target = tmp_path / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    return tmp_path


def _fallback_ruby_inspector(
    lockfiles: dict[str, str],
    gemspecs: dict[str, str],
) -> dict[str, object]:
    """Minimal test double when PythonCiCaller isolates PATH without Ruby."""
    locks: dict[str, str | None] = {}
    for rel, path in lockfiles.items():
        text = Path(path).read_text(encoding="utf-8")
        match = re.search(r"BUNDLED WITH\r?\n\s+(\S+)", text)
        locks[rel] = match.group(1) if match else None
    specs: dict[str, object] = {}
    for rel, path in gemspecs.items():
        text = Path(path).read_text(encoding="utf-8")
        dep_op = "~>" if "~> 2.0.26" in text else ">="
        deps = (
            [{"name": "asciidoctor", "requirements": [["~>", "2.0.26"]]}]
            if dep_op == "~>"
            else [
                {
                    "name": "asciidoctor",
                    "requirements": [[">=", "2.0"], ["<", "3.0"]],
                },
            ]
        )
        specs[rel] = {
            "required_ruby_version": [[">=", "3.1"]],
            "runtime_dependencies": deps,
        }
    return {"lockfiles": locks, "gemspecs": specs}


def _active_inspector():
    if shutil.which("ruby") is not None:
        return CHECKER.run_ruby_inspector
    return _fallback_ruby_inspector


def test_committed_repository_satisfies_supply_chain_policy() -> None:
    """The real repository passes every release-age and public-floor check."""
    assert (
        CHECKER.check_repository(ROOT, ruby_inspector=_active_inspector()) == []
    )


def test_native_ruby_inspector_validates_committed_repository() -> None:
    """When Ruby is on PATH, the native Bundler/RubyGems inspector succeeds."""
    if shutil.which("ruby") is None:
        pytest.skip("Ruby is not installed on the isolated test PATH")
    assert CHECKER.main(["--root", str(ROOT)]) == 0


def test_missing_ruby_executable_fails_closed() -> None:
    """Absent Ruby on PATH fails closed instead of skipping Ruby contracts."""
    errors = CHECKER.check_repository(ROOT, ruby_inspector=lambda _l, _g: None)
    assert errors == [
        (
            "ruby executable is required on PATH to inspect Gemfile.lock "
            "and .gemspec contracts."
        ),
    ]


def test_detects_cross_ecosystem_release_age_drift(tmp_path: Path) -> None:
    """Dropping 3-day cooldowns, exclusions, or unapproved exemptions fails."""
    ws = _seed_workspace(tmp_path)
    (ws / "mise.toml").write_text(
        '[settings]\nminimum_release_age = "1d"\n',
        encoding="utf-8",
    )
    (ws / "pnpm-workspace.yaml").write_text(
        "minimumReleaseAge: 1440\n",
        encoding="utf-8",
    )
    (ws / "eng/renovate/.npmrc").write_text(
        "min-release-age=3\nmin-release-age-exclude=left-pad\n",
        encoding="utf-8",
    )
    (ws / "src/public/lib/asciidoctor-latexmath/.bundle/config").write_text(
        '---\nBUNDLE_COOLDOWN: "1"\n',
        encoding="utf-8",
    )
    lock_path = (
        ws / "tests/private/app/workflow-delivery/Native/Ruby/Gemfile.lock"
    )
    lock_path.write_text(
        lock_path.read_text(encoding="utf-8").replace(
            "  4.0.20\n",
            "  4.0.12\n",
        ),
        encoding="utf-8",
    )
    renovate_path = ws / "renovate.json"
    renovate = json.loads(renovate_path.read_text(encoding="utf-8"))
    renovate["packageRules"].append(
        {
            "matchManagers": ["npm"],
            "matchPackageNames": ["left-pad"],
            "minimumReleaseAge": None,
        },
    )
    renovate_path.write_text(json.dumps(renovate), encoding="utf-8")

    errors = CHECKER.check_repository(ws, ruby_inspector=_active_inspector())
    assert any("mise.toml" in err for err in errors)
    assert any("pnpm-workspace.yaml" in err for err in errors)
    assert any("min-release-age-exclude" in err for err in errors)
    assert any("BUNDLE_COOLDOWN" in err for err in errors)
    assert any(
        "Native/Ruby/Gemfile.lock: Bundler version" in err for err in errors
    )
    assert not any("bundler-2.4/Gemfile.lock" in err for err in errors)
    assert any("unapproved release-age exemption" in err for err in errors)


def test_detects_public_library_floor_and_cpm_isolation_violations(
    tmp_path: Path,
) -> None:
    """Floor overrides, non-SemVer specs, and conditioned CPM leaks fail."""
    ws = _seed_workspace(tmp_path)

    # 1. Narrowing a floor rule or appending broad enabled=true fails.
    renovate_path = ws / "renovate.json"
    renovate = json.loads(renovate_path.read_text(encoding="utf-8"))
    for rule in renovate["packageRules"]:
        if (
            rule.get("matchManagers") == ["npm"]
            and rule.get("matchFileNames")
            == [
                "src/public/lib/**/package.json",
                "!src/public/lib/**/examples/**",
            ]
            and rule.get("enabled") is False
        ):
            rule["matchPackageNames"] = ["cheerio"]
    renovate["packageRules"].append(
        {
            "matchManagers": ["npm"],
            "matchPackageNames": ["cheerio"],
            "enabled": True,
        },
    )
    renovate_path.write_text(json.dumps(renovate), encoding="utf-8")

    # 2. Pessimistic/PEP-440-only caps fail while SemVer prerelease floors pass.
    py_path = ws / "src/public/lib/nbgv-python/pyproject.toml"
    py_path.write_text(
        py_path.read_text(encoding="utf-8").replace(
            "packaging>=24.0",
            "packaging==24.0",
        ),
        encoding="utf-8",
    )
    npm_path = ws / "src/public/lib/hexo-renderer-asciidoc/package.json"
    npm_data = json.loads(npm_path.read_text(encoding="utf-8"))
    npm_data["dependencies"]["cheerio"] = ">=1!2.3"
    npm_data["dependencies"]["entities"] = "^9.0.0-canary.1"
    npm_path.write_text(json.dumps(npm_data), encoding="utf-8")

    gemspec_path = ws / (
        "src/public/lib/asciidoctor-latexmath/asciidoctor-latexmath.gemspec"
    )
    gemspec_path.write_text(
        gemspec_path.read_text(encoding="utf-8").replace(
            '">= 2.0", "< 3.0"',
            '"~> 2.0.26"',
        ),
        encoding="utf-8",
    )

    # 3. Conditioned metadata, extra Imports, Choose blocks, or conditioned
    #    PrivateAssets in NuGet public-library files fail closed.
    props_path = ws / "src/public/lib/Directory.Packages.props"
    props_text = props_path.read_text(encoding="utf-8")
    stj_entry = (
        '    <PackageVersion Update="System.Text.Json"'
        ' Version="[10.0.14,)" />\n'
    )
    props_text = (
        props_text.replace(stj_entry, "")
        .replace(
            'Include="System.ValueTuple" Version="[4.4.0,)" />',
            'Include="System.ValueTuple" Version="[4.4.0,)">'
            "<Version Condition=\"'1'=='1'\">[9.0.0,)</Version>"
            "</PackageVersion>",
        )
        .replace(
            "</Project>",
            "  <ItemGroup Condition=\"'$(TargetFramework)' == 'net9.0'\">\n"
            + stj_entry
            + "  </ItemGroup>\n"
            + '  <Import Project="extra.props" />\n'
            + "  <Choose><When Condition=\"'1' == '1'\"><ItemGroup>"
            + '<PackageVersion Update="System.Memory" Version="[9.0.0,)" />'
            + "</ItemGroup></When></Choose>\n</Project>",
        )
    )
    props_path.write_text(props_text, encoding="utf-8")

    csproj_path = ws / (
        "src/public/lib/WebHdfs.Extensions.FileProviders/"
        "WebHdfs.Extensions.FileProviders.csproj"
    )
    csproj_path.write_text(
        csproj_path.read_text(encoding="utf-8").replace(
            "</Project>",
            "  <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">"
            "<IsPackable>false</IsPackable></PropertyGroup>\n"
            '  <ItemGroup><PackageReference Include="Unisolated.Dep">'
            "<PrivateAssets Condition=\"'$(TargetFramework)'=='net10.0'\">"
            "all</PrivateAssets></PackageReference></ItemGroup>\n</Project>",
        ),
        encoding="utf-8",
    )

    errors = CHECKER.check_repository(ws, ruby_inspector=_active_inspector())
    assert any(
        "missing unnarrowed enabled=false public-library floor rule" in err
        for err in errors
    )
    assert any(
        "enabled=true rule after public-library floor protection" in err
        for err in errors
    )
    assert any("nbgv-python/pyproject.toml" in err for err in errors)
    assert any(
        "hexo-renderer-asciidoc/package.json dependencies.cheerio" in err
        for err in errors
    )
    assert not any("dependencies.entities" in err for err in errors)
    assert any(
        "asciidoctor-latexmath.gemspec runtime_dependency(asciidoctor)" in err
        for err in errors
    )
    assert any("attributes ['Condition'] on ItemGroup" in err for err in errors)
    assert any(
        "Directory.Packages.props (System.ValueTuple): extra attributes" in err
        for err in errors
    )
    assert any("unsupported element <Import>" in err for err in errors)
    assert any("unsupported element <Choose>" in err for err in errors)
    assert any(
        "WebHdfs.Extensions.FileProviders.csproj (System.Text.Json)" in err
        for err in errors
    )
    assert any(
        "WebHdfs.Extensions.FileProviders.csproj (Unisolated.Dep)" in err
        for err in errors
    )


def test_ignores_installed_bundle_trees_while_enforcing_vendored_and_owned_ruby(
    tmp_path: Path,
) -> None:
    """Installed .bundle and vendor/bundle gems are skipped."""
    ws = _seed_workspace(tmp_path)
    ruby_root = ws / "src/public/lib/asciidoctor-latexmath"
    for installed_dir in (
        ruby_root / ".bundle/ruby/4.0.0/gems/json_schemer-2.4.0",
        ruby_root / "vendor/bundle/ruby/4.0.0/gems/json_schemer-2.4.0",
    ):
        installed_dir.mkdir(parents=True, exist_ok=True)
        (installed_dir / "Gemfile").write_text(
            'source "https://rubygems.org"\n',
            encoding="utf-8",
        )
        (installed_dir / "json_schemer.gemspec").write_text(
            "Gem::Specification.new do |s|\n"
            '  s.name = "json_schemer"\n'
            '  s.version = "2.4.0"\n'
            '  s.summary = "third-party"\n'
            '  s.authors = ["third-party"]\n'
            "  s.files = []\n"
            '  s.required_ruby_version = ">= 3.1"\n'
            '  s.add_runtime_dependency "asciidoctor", "~> 2.0.26"\n'
            "end\n",
            encoding="utf-8",
        )
    assert (
        CHECKER.check_repository(ws, ruby_inspector=_active_inspector()) == []
    )

    # Owned .bundle/config and non-bundle vendored source remain enforced.
    (ruby_root / ".bundle/config").write_text(
        '---\nBUNDLE_COOLDOWN: "1"\n',
        encoding="utf-8",
    )
    vendored_tool = ruby_root / "vendor/internal-tool"
    vendored_tool.mkdir(parents=True, exist_ok=True)
    (vendored_tool / "Gemfile").write_text(
        'source "https://rubygems.org"\n',
        encoding="utf-8",
    )
    errors = CHECKER.check_repository(ws, ruby_inspector=_active_inspector())
    assert any(
        "asciidoctor-latexmath/.bundle/config: BUNDLE_COOLDOWN" in err
        for err in errors
    )
    assert any(
        "vendor/internal-tool/.bundle/config: missing active Bundler config"
        in err
        for err in errors
    )
