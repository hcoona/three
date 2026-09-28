"""Native backend authority and deterministic projection contracts."""

import importlib.util
import subprocess
from pathlib import Path

import pytest
import tomli_w
from three_workflow_delivery_v3.repository.python_backend import (
    backend_constraints,
    backend_version,
)

_ROOT = Path(__file__).resolve().parents[6]
_FUTURE_VERSION = "9.8.7"
_HASH_A = "sha256:" + "a" * 64
_HASH_B = "sha256:" + "b" * 64


def _manifest():
    return {
        "build-system": {
            "requires": [f"hatchling=={_FUTURE_VERSION}", "nbgv-python"],
            "build-backend": "hatchling.build",
        }
    }


def _package(name, version, dependencies=()):
    return {
        "name": name,
        "version": version,
        "source": {"registry": "https://pypi.org/simple"},
        "dependencies": [{"name": dependency} for dependency in dependencies],
        "wheels": [{"hash": _HASH_B}, {"hash": _HASH_A}, {"hash": _HASH_B}],
    }


def _lock():
    return {
        "package": [
            _package("hatchling", _FUTURE_VERSION, ("new-helper",)),
            _package("new-helper", "2.3.4", ("leaf",)),
            _package("leaf", "3.4.5", ("hatchling",)),
            _package("unrelated", "99.0.0"),
        ]
    }


def _toml(document):
    return tomli_w.dumps(document).encode()


@pytest.fixture
def synchronizer():
    """Load the repository entry point without an installed script package."""
    spec = importlib.util.spec_from_file_location(
        "_test_sync_python_build_backend",
        _ROOT / "eng/scripts/sync_python_build_backend.py",
    )
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _repository(root, synchronizer):
    manifest = root / synchronizer.MANIFEST
    manifest.parent.mkdir(parents=True)
    manifest.write_bytes(_toml(_manifest()))
    (root / "uv.lock").write_bytes(_toml(_lock()))


def _snapshot(root):
    return {
        str(path.relative_to(root)): path.read_bytes()
        for path in root.rglob("*")
        if path.is_file()
    }


def test_backend_version_accepts_future_exact_pin():
    """A manifest upgrade does not require another handwritten version."""
    assert backend_version(_toml(_manifest())) == _FUTURE_VERSION


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("requires", []),
        ("requires", ["hatchling>=9.8.7", "nbgv-python"]),
        ("requires", ["hatchling==9.8.7rc1", "nbgv-python"]),
        ("requires", ["hatchling==9.8.7"]),
        ("requires", ["hatchling==9.8.7", "nbgv-python", "extra"]),
        ("requires", ["nbgv-python", "hatchling==9.8.7"]),
        ("requires", [1, "nbgv-python"]),
        ("requires", "hatchling==9.8.7"),
        ("build-backend", "another.backend"),
    ],
)
def test_backend_version_rejects_noncanonical_build_system(field, value):
    """A range, missing hook or replacement backend cannot enter Build."""
    manifest = _manifest()
    manifest["build-system"][field] = value
    with pytest.raises(ValueError, match="exact Hatchling pin and NBGV hook"):
        backend_version(_toml(manifest))


def test_backend_constraints_projects_changed_transitive_graph():
    """Traverse new dependencies and produce only their normalized hashes."""
    content = backend_constraints(_toml(_lock()), _FUTURE_VERSION)
    assert content.splitlines()[1:] == [
        f"hatchling==9.8.7 --hash={_HASH_A} --hash={_HASH_B}",
        f"leaf==3.4.5 --hash={_HASH_A} --hash={_HASH_B}",
        f"new-helper==2.3.4 --hash={_HASH_A} --hash={_HASH_B}",
    ]
    assert content.endswith("\n")
    lock = _lock()
    lock["package"].reverse()
    assert backend_constraints(_toml(lock), _FUTURE_VERSION) == content


@pytest.mark.parametrize(
    ("change", "message"),
    [
        ("missing", "incomplete or ambiguous: leaf"),
        ("ambiguous", "incomplete or ambiguous: leaf"),
        ("private-index", "public PyPI index"),
        ("version", "manifest pin differs"),
        ("no-wheels", "exact wheel hashes"),
        ("wrong-algorithm", "exact wheel hashes"),
        ("short-hash", "exact wheel hashes"),
    ],
)
def test_backend_constraints_rejects_invalid_closure(change, message):
    """Reject broken transitive inputs as well as a stale direct backend pin."""
    lock = _lock()
    leaf = lock["package"][2]
    if change == "missing":
        lock["package"].remove(leaf)
    elif change == "ambiguous":
        lock["package"].append(_package("leaf", "4.0.0"))
    elif change == "private-index":
        leaf["source"] = {"registry": "https://private.invalid/simple"}
    elif change == "version":
        lock["package"][0]["version"] = "9.8.6"
    elif change == "no-wheels":
        leaf["wheels"] = []
    elif change == "wrong-algorithm":
        leaf["wheels"][0]["hash"] = "sha512:" + "b" * 128
    else:
        leaf["wheels"][0]["hash"] = "sha256:abc"
    with pytest.raises(ValueError, match=message):
        backend_constraints(_toml(lock), _FUTURE_VERSION)


@pytest.mark.parametrize("projection", ["CONSTRAINTS", "PROJECTION"])
@pytest.mark.parametrize("change", ["missing", "stale"])
def test_check_rejects_stale_projection_without_writing(
    tmp_path, synchronizer, projection, change
):
    """Read-only validation reports either missing or stale generated output."""
    _repository(tmp_path, synchronizer)
    synchronizer.synchronize(tmp_path, write=True)
    path = tmp_path / getattr(synchronizer, projection)
    if change == "missing":
        path.unlink()
    else:
        path.write_text("stale projection\n", encoding="utf-8")
    before = _snapshot(tmp_path)
    with pytest.raises(ValueError, match="run mise run update-uv-lock"):
        synchronizer.synchronize(tmp_path)
    assert _snapshot(tmp_path) == before


def test_update_resolves_manifest_pin_before_projection(
    tmp_path, synchronizer, monkeypatch
):
    """Resolve the manifest requirement before deriving both outputs."""
    _repository(tmp_path, synchronizer)
    old_lock = _lock()
    old_lock["package"][0]["version"] = "1.0.0"
    (tmp_path / "uv.lock").write_bytes(_toml(old_lock))
    calls = []

    def resolve(command, *, cwd, check):
        calls.append((command, cwd, check))
        (cwd / "uv.lock").write_bytes(_toml(_lock()))

    monkeypatch.setattr(synchronizer.subprocess, "run", resolve)
    assert (
        synchronizer.synchronize(tmp_path, write=True, update_lock=True)
        == _FUTURE_VERSION
    )
    assert calls == [
        (
            ("uv", "lock", "--upgrade-package", "hatchling==9.8.7"),
            tmp_path,
            True,
        )
    ]
    constraints = (tmp_path / synchronizer.CONSTRAINTS).read_text()
    assert "hatchling==9.8.7 --hash=" in constraints
    assert "new-helper==2.3.4 --hash=" in constraints
    assert "unrelated" not in constraints
    projection = (tmp_path / synchronizer.PROJECTION).read_text()
    assert 'HATCHLING_VERSION = "9.8.7"' in projection
    assert (
        'HATCHLING_REQUIREMENT = f"hatchling=={HATCHLING_VERSION}"'
        in projection
    )
    before = _snapshot(tmp_path)
    assert synchronizer.synchronize(tmp_path) == _FUTURE_VERSION
    assert _snapshot(tmp_path) == before
    assert len(calls) == 1


def test_update_requires_explicit_write(tmp_path, synchronizer, monkeypatch):
    """An invalid update cannot resolve or change any repository file."""
    _repository(tmp_path, synchronizer)
    calls = []
    monkeypatch.setattr(
        synchronizer.subprocess,
        "run",
        lambda *args, **_kwargs: calls.append(args),
    )
    before = _snapshot(tmp_path)
    with pytest.raises(ValueError, match="--update-lock requires --write"):
        synchronizer.synchronize(tmp_path, update_lock=True)
    assert calls == []
    assert _snapshot(tmp_path) == before


def test_failed_resolution_preserves_projections(
    tmp_path, synchronizer, monkeypatch
):
    """A resolver failure cannot publish updated projections."""
    _repository(tmp_path, synchronizer)
    synchronizer.synchronize(tmp_path, write=True)
    manifest = _manifest()
    manifest["build-system"]["requires"][0] = "hatchling==9.8.8"
    (tmp_path / synchronizer.MANIFEST).write_bytes(_toml(manifest))
    before = _snapshot(tmp_path)

    def fail(command, **_kwargs):
        raise subprocess.CalledProcessError(1, command)

    monkeypatch.setattr(synchronizer.subprocess, "run", fail)
    with pytest.raises(subprocess.CalledProcessError):
        synchronizer.synchronize(tmp_path, write=True, update_lock=True)
    assert _snapshot(tmp_path) == before
