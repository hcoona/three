"""Actual filtered NBGV history and Ruby control manifest closure."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import parse_canonical_json
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_CONTROL_PATHS,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    compile_ruby_repository_model,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    provide_ruby_repository_facts,
    ruby_input_candidates,
    validate_ruby_inputs,
)

from ..ruby_fixtures import EPOCH, ROOT, binding, commit, git, repository
from ..ruby_integration_fixtures import (
    admitted,
    committed_source,
    contents,
    context,
)

CONTROL_PATHS = (
    ".github/workflow-delivery/governance/hcoona-release-smoke-ruby-github-packages.json",
    ".github/workflow-delivery/governance/hcoona-release-smoke-ruby-rubygems.json",
    ".github/workflow-delivery/requests/hcoona-release-smoke-ruby.json",
    ".github/workflow-delivery/configuration/hcoona-release-smoke-ruby-github-packages.json",
    ".github/workflow-delivery/configuration/hcoona-release-smoke-ruby-rubygems.json",
    "src/public/lib/three-workflow-delivery-v3/docs/ruby-operation-protocol.md",
)
UNRELATED_PATHS = (
    ".github/workflow-delivery/governance/hcoona-release-smoke-python.json",
    "src/public/lib/unrelated-fixture/module.py",
)


def _control_content(path):
    """Copy maintained controls; future paths use inert fixture data only."""
    actual = ROOT / path
    if actual.is_file():
        return actual.read_bytes()
    return (
        b"{}"
        if path.endswith(".json")
        else b"Synthetic protocol selection fixture.\n"
    )


@pytest.fixture(scope="module")
def control_history(tmp_path_factory):
    """Run the actual pinned Provider over one isolated public-main history."""
    root = tmp_path_factory.mktemp("ruby-control-version")
    source, origin, _target = repository(root)
    for path in CONTROL_PATHS:
        target = origin / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(_control_content(path))
    baseline = commit(origin, epoch=EPOCH + 1)

    def discover(target):
        git(source, "fetch", "--quiet", "origin")
        git(source, "checkout", "--quiet", "--detach", target)
        return provide_ruby_repository_facts(
            source,
            binding(target, "live-release"),
            CheckoutMaterialization(0, credentials_persisted=False),
        )

    before = discover(baseline)
    rows = {}
    for ordinal, path in enumerate((*CONTROL_PATHS, *UNRELATED_PATHS), 2):
        changed = origin / path
        changed.parent.mkdir(parents=True, exist_ok=True)
        previous_bytes = changed.read_bytes() if changed.exists() else b""
        # The filtered-height claim concerns exact tracked bytes. This neutral
        # change does not pretend to install or activate a configuration.
        changed.write_bytes(previous_bytes + b"\n")
        target = commit(origin, epoch=EPOCH + ordinal)
        after = discover(target)
        rows[path] = (before, after, changed.read_bytes())
        before = after
    return rows


@pytest.mark.parametrize("path", CONTROL_PATHS)
def test_ruby_control_only_change_advances_actual_nbgv_and_manifest(
    control_history, path
):
    """Each relevant control-only commit advances the public lineage."""
    before, after, content = control_history[path]
    old = parse_canonical_json(before.nbgv.raw_bytes)
    new = parse_canonical_json(after.nbgv.raw_bytes)
    assert old["PublicRelease"] is new["PublicRelease"] is True
    assert new["VersionHeight"] == old["VersionHeight"] + 1
    assert new["SemVer2"] != old["SemVer2"]
    assert after.nbgv.native_version == new["SemVer2"].replace("-", ".pre.")
    assert after.nbgv.native_version != before.nbgv.native_version
    assert after.nbgv.target == after.binding.target
    assert before.binding.target != after.binding.target
    previous = dict(before.source_input_manifest)
    current = dict(after.source_input_manifest)
    assert current[path] == ruby_digest(content)
    assert previous[path] != current[path]
    assert {key: value for key, value in previous.items() if key != path} == {
        key: value for key, value in current.items() if key != path
    }


@pytest.mark.parametrize("path", UNRELATED_PATHS)
def test_unrelated_nonruby_change_preserves_coordinate_and_ruby_manifest(
    control_history, path
):
    """Fresh targets do not force a version increment for unrelated inputs."""
    before, after, _content = control_history[path]
    old = parse_canonical_json(before.nbgv.raw_bytes)
    new = parse_canonical_json(after.nbgv.raw_bytes)
    assert new["PublicRelease"] is True
    assert new["VersionHeight"] == old["VersionHeight"]
    assert new["SemVer2"] == old["SemVer2"]
    assert after.nbgv.native_version == before.nbgv.native_version
    assert after.binding.target != before.binding.target
    assert after.source_input_manifest == before.source_input_manifest
    assert path not in dict(after.source_input_manifest)


def test_ruby_control_selector_and_manifest_import_share_exact_closed_paths():
    """Tracked future controls participate without broad path inclusion."""
    assert set(RUBY_CONTROL_PATHS) == set(CONTROL_PATHS)
    source = contents()
    source.update({path: _control_content(path) for path in CONTROL_PATHS})
    tracked = tuple(sorted((*source, *UNRELATED_PATHS)))
    selected = ruby_input_candidates(tracked)
    assert selected == tuple(sorted(source))
    manifest = tuple((path, ruby_digest(source[path])) for path in selected)
    validate_ruby_inputs(manifest)
    foreign = tuple(
        sorted((*manifest, (UNRELATED_PATHS[0], ruby_digest(b"{}"))))
    )
    with pytest.raises(ValueError, match="manifest"):
        validate_ruby_inputs(foreign)


def test_ruby_model_rejects_provider_omission_of_tracked_control(tmp_path):
    """Compilation cannot drop a tracked control from source closure."""
    source = contents()
    source.update({path: _control_content(path) for path in CONTROL_PATHS})
    root, target = committed_source(tmp_path / "source", source)
    facts = admitted(context(target), source)
    model = compile_ruby_repository_model(root, facts)
    assert set(CONTROL_PATHS) <= set(dict(model.provider.source_input_manifest))
    missing = dict(source)
    del missing[CONTROL_PATHS[0]]
    incomplete = admitted(context(target), missing)
    with pytest.raises(ValueError, match="Ruby"):
        compile_ruby_repository_model(root, incomplete)
    with pytest.raises(ValueError, match="Ruby"):
        replace(model, provider=incomplete.result)
