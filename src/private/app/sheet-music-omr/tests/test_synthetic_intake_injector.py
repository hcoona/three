"""W4 intake and injection scenarios using self-authored local notation."""

from __future__ import annotations

import csv
import hashlib
import io
import json
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter
from dataclasses import replace

import pytest

from sheet_music_omr.evaluation.alignment import TEST_GOLD_IDS
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import to_musicxml
from sheet_music_omr.musicxml_reverse import from_musicxml_text
from sheet_music_omr.parser import parse_document
from sheet_music_omr.semantics import validate_document
from sheet_music_omr.supplementary import compare_file
from sheet_music_omr.synthetic import intake
from sheet_music_omr.synthetic.injector import (
    has_unchecked_verovio_supplementary_facet,
    inject_annotations,
)

_HEADER = (
    'format: text-score/2\ntitle: "Twilight Bicycle"\n'
    'composer: "Mira Fixture"\nkey: 0\ntime: 4/4\n'
    "clefs: G2 F4\ncarry: octave\n"
)
_UPPER = "C6+E6/4 D6+F6/4 E6/4 F6/4"
_LOWER = "C2/4 D2/4 E2/4 F2/4"
_LICENSE = "https://creativecommons.org/publicdomain/zero/1.0/"


def _score(*, measures=8, upper=_UPPER, lower=_LOWER, first=None):
    source = _HEADER + "".join(
        f"m {index}\n1.1: {first if index == 1 and first else upper}\n"
        f"2.1: {lower}\n"
        for index in range(1, measures + 1)
    )
    document = parse_document(source)
    assert isinstance(document, AssembledScore)
    return document


def _sha(data):
    return hashlib.sha256(data).hexdigest()


@pytest.fixture
def frozen_inputs(tmp_path, monkeypatch):
    """Make seven frozen hashes of deliberately non-XML TEST payloads."""
    manifest_path = tmp_path / "test-exclusion.json"
    status_path = tmp_path / "batch-status.json"
    cache = tmp_path / "gold-cache"
    excluded = []
    status = []
    paths = {}
    for gid in sorted(TEST_GOLD_IDS):
        number = int(gid.removeprefix("gold-"))
        workspace = f"{number:02d} - Example - Winding River Op. {number}"
        path = cache / workspace / "build" / "score.musicxml"
        path.parent.mkdir(parents=True)
        path.write_bytes(f"{gid}: deliberately not notation".encode())
        paths[gid] = path
        excluded.append(
            {
                "gold_id": gid,
                "accepted_xml_sha256": _sha(path.read_bytes()),
                "facsimile_pdf_sha256": _sha(f"{gid}: pdf".encode()),
            }
        )
        status.append(
            {
                "number": number,
                "accepted": True,
                "workspace": workspace,
                "source": f"{number:02d} - Example - Moonlight Orchard.pdf",
                "latest_build": "build",
            }
        )
    manifest_path.write_text(
        json.dumps(
            {
                "schema": "omr-test-exclusion-v1",
                "test_piece_count": len(TEST_GOLD_IDS),
                "pieces": excluded,
            }
        ),
        encoding="utf-8",
    )
    status_path.write_text(json.dumps({"batches": status}), encoding="utf-8")
    monkeypatch.setattr(
        intake,
        "EXPECTED_TEST_EXCLUSION_SHA256",
        _sha(manifest_path.read_bytes()),
    )
    return manifest_path, status_path, cache, paths


def _candidate(source_id, xml):
    return intake.SourceCandidate(
        source_id, "fixture", "permissive", _LICENSE, xml
    )


def test_exclusion_uses_only_frozen_bytes_and_non_notation_metadata(
    frozen_inputs,
):
    """Reject by either digest, work alias, catalogue or anonymized ID."""
    manifest, status, cache, paths = frozen_inputs
    gate = intake.TestExclusionGate(manifest, status, cache)
    work = intake.normalize_work_identity("J. S. Bách", "Air on the G String")
    assert work == intake.WorkIdentity(
        "bach", frozenset({"air", "string"}), frozenset()
    )
    assert (
        gate.exclusion_reason("A. Example", "The Moonlight Orchard")
        == "excluded_test_match"
    )
    assert (
        gate.exclusion_reason("Example, A.", "Another Title, Opus 18")
        == "excluded_test_match"
    )
    assert (
        gate.exclusion_reason("Fixture", "Something Else", gold_id="gold-06")
        == "excluded_test_match"
    )
    for digest in (_sha(paths["gold-18"].read_bytes()), _sha(b"gold-01: pdf")):
        assert (
            gate.exclusion_reason(
                "Fixture", "Something Else", digests=(digest,)
            )
            == "excluded_test_match"
        )
    for composer, title in (
        ("9", "String"),
        ("Bach", "A"),
        ("Bach", "Piano Solo"),
        ("", "Sonata"),
    ):
        assert (
            gate.exclusion_reason(composer, title)
            == "excluded_missing_metadata"
        )
    assert gate.exclusion_reason("Fixture", "Moonlight Orchard") is None
    with pytest.raises(StopConditionError, match="failed TEST audit"):
        gate.audit_admitted_manifest(
            (
                {
                    "composer": "Fixture",
                    "title": "Bicycle",
                    "raw_sha256": _sha(b"gold-01: pdf"),
                },
            )
        )


@pytest.mark.parametrize("changed", ["manifest", "cache", "identity"])
def test_exclusion_setup_stops_when_a_frozen_input_changes(
    frozen_inputs, changed
):
    """Require the manifest hash, raw byte hashes and all seven identities."""
    manifest, status, cache, paths = frozen_inputs
    if changed == "manifest":
        manifest.write_bytes(manifest.read_bytes() + b"\n")
    elif changed == "cache":
        paths["gold-18"].write_bytes(b"changed")
    else:
        record = json.loads(status.read_bytes())
        record["batches"].pop()
        status.write_text(json.dumps(record), encoding="utf-8")
    with pytest.raises(StopConditionError, match="TEST"):
        intake.TestExclusionGate(manifest, status, cache)


def test_intake_persists_only_audited_canonical_sources(
    tmp_path, frozen_inputs
):
    """Cover metadata rejection, canonical dedup, coverage and output."""
    manifest, status, cache, paths = frozen_inputs
    gate = intake.TestExclusionGate(manifest, status, cache)
    raw = to_musicxml(_score(measures=4, first="C6/4[xs2] D6/4 E6/4 F6/4"))
    candidates = [
        _candidate(
            "rest",
            to_musicxml(_score(measures=4, upper="mrest", lower="mrest")),
        ),
        _candidate("short", to_musicxml(_score(measures=3))),
        replace(_candidate("missing", raw), composer="Untitled"),
        replace(_candidate("forbidden", raw), corpus="Camera-GrandStaff"),
        replace(_candidate("unverified", raw), license_url="unknown"),
        _candidate("leak", paths["gold-01"].read_bytes()),
        _candidate("source", raw),
        _candidate("serialization", raw + b"\n"),
    ]
    report = intake.intake_sources(
        candidates,
        run_dir=tmp_path / "run",
        rights_variant="permissive",
        gate=gate,
        min_measures=4,
        reference_rates={"D4": 0.5},
    )
    assert report.counts == {
        "admitted": 1,
        "excluded_missing_metadata": 1,
        "excluded_test_match": 1,
        "all_rest": 1,
        "too_few_measures": 1,
        "forbidden_label_source": 1,
        "rights_unverified": 1,
        "duplicate_canonical": 1,
    }
    assert report.manifest_path.parent.parts[-2:] == ("synthetic", "permissive")
    [line] = report.manifest_path.read_text(encoding="utf-8").splitlines()
    row = json.loads(line)
    assert row["source_id"] == "source"
    expected_density = 1 / 4
    assert (
        row["category_densities"]["D4"]
        == row["category_densities"]["cross_staff"]
        == expected_density
    )
    assert row["category_densities"]["multi_voice_share"] == 0
    assert row["coverage_shortfalls"] == {"D4": 0.25}
    assert row["sampling_weight"] > 1.0
    partition = report.manifest_path.parent
    assert (
        _sha((partition / row["canonical_v2_path"]).read_bytes())
        == row["canonical_v2_sha256"]
    )
    assert (
        (partition / row["canonical_xml_path"])
        .read_bytes()
        .startswith(b"<?xml")
    )
    assert (
        json.loads(report.summary_path.read_bytes())["counts"] == report.counts
    )


def test_nc_sa_partition_requires_a_repository_license(tmp_path, frozen_inputs):
    """Reject wrong-version proof and preserve the rights partition."""
    manifest, status, cache, _ = frozen_inputs
    gate = intake.TestExclusionGate(manifest, status, cache)
    repo = tmp_path / "repository"
    repo.mkdir()
    license_path = repo / "LICENSE.txt"
    license_path.write_text("CC BY-NC-SA 3.0", encoding="utf-8")
    candidate = replace(
        _candidate("score", to_musicxml(_score())),
        rights_variant="nc_sa",
        repo_dir=repo,
    )
    counts = Counter()
    assert (
        intake.normalize_and_profile_candidate(
            candidate, gate=gate, seen_canonical=set(), counts=counts
        )
        is None
    )
    assert counts == {"rights_unverified": 1}
    license_path.write_text(
        "https://creativecommons.org/licenses/by-nc-sa/4.0/", encoding="utf-8"
    )
    report = intake.intake_sources(
        (candidate,),
        run_dir=tmp_path / "run",
        rights_variant="nc_sa",
        gate=gate,
    )
    assert report.manifest_path.parent.name == "nc_sa"
    assert (
        json.loads(report.manifest_path.read_bytes())["rights_variant"]
        == "nc_sa"
    )
    with pytest.raises(StopConditionError, match="output partition"):
        intake.intake_sources(
            (candidate,),
            run_dir=tmp_path / "run",
            rights_variant="permissive",
            gate=gate,
        )


def test_private_partition_requires_the_accepted_train_dev_bytes(
    tmp_path, frozen_inputs
):
    """Do not accept an unrelated payload merely claiming a TRAIN_DEV ID."""
    manifest, status, cache, _ = frozen_inputs
    workspace = "07 - Fixture - Twilight Bicycle"
    records = json.loads(status.read_bytes())
    records["batches"].append(
        {
            "number": 7,
            "accepted": True,
            "workspace": workspace,
            "latest_build": "build",
        }
    )
    status.write_text(json.dumps(records), encoding="utf-8")
    entry = cache / workspace / "build" / "score.musicxml"
    entry.parent.mkdir(parents=True)
    xml = to_musicxml(_score())
    entry.write_bytes(xml)
    candidate = replace(
        _candidate("private", xml),
        rights_variant="private_traindev",
        gold_id="gold-07",
    )
    report = intake.intake_sources(
        (replace(candidate, raw=xml + b"\n"), candidate),
        run_dir=tmp_path / "run",
        rights_variant="private_traindev",
        gate=intake.TestExclusionGate(manifest, status, cache),
    )
    assert report.counts["rights_unverified"] == 1
    assert report.counts["admitted"] == 1
    row = json.loads(report.manifest_path.read_bytes())
    assert row["rights_variant"] == "private_traindev"
    assert row["gold_id"] == "gold-07"


def _mxl(xml):
    payload = io.BytesIO()
    with zipfile.ZipFile(payload, mode="w") as archive:
        archive.writestr(
            "META-INF/container.xml",
            '<container><rootfiles><rootfile full-path="score.xml"/>'
            "</rootfiles></container>",
        )
        archive.writestr("score.xml", xml)
    return payload.getvalue()


def test_pdmx_stream_filters_rows_and_stops_at_admission_limit(
    tmp_path, frozen_inputs
):
    """Stream local members while honoring license, validity and dedup flags."""
    manifest, status, cache, paths = frozen_inputs
    gate = intake.TestExclusionGate(manifest, status, cache)
    constraints = [
        "mxl",
        "subset:no_license_conflict",
        "subset:all_valid",
        "license_url",
        "n_tracks",
        "subset:deduplicated",
    ]
    with (tmp_path / "PDMX.csv").open(
        "w", newline="", encoding="utf-8"
    ) as stream:
        writer = csv.writer(stream)
        writer.writerow(constraints)
        writer.writerows(
            [
                ["./mxl/leak.mxl", "True", "True", _LICENSE, "1", "True"],
                ["./mxl/bad.mxl", "True", "True", _LICENSE, "1", "False"],
                ["./mxl/good.mxl", "True", "True", _LICENSE, "1", "True"],
                ["./mxl/later.mxl", "True", "True", _LICENSE, "1", "True"],
            ]
        )
    with tarfile.open(tmp_path / "mxl.tar.gz", mode="w:gz") as archive:
        for name, raw in (
            ("leak", _mxl(paths["gold-18"].read_bytes())),
            ("bad", b"unreadable"),
            ("good", _mxl(to_musicxml(_score()))),
            ("later", b"unreadable"),
        ):
            entry = tarfile.TarInfo(f"./mxl/{name}.mxl")
            entry.size = len(raw)
            archive.addfile(entry, io.BytesIO(raw))
    report = intake.intake_sources(
        intake.iter_pdmx_candidates(tmp_path),
        run_dir=tmp_path / "run",
        rights_variant="permissive",
        gate=gate,
        max_admitted=1,
    )
    assert report.counts == {
        "admitted": 1,
        "excluded_missing_metadata": 0,
        "excluded_test_match": 1,
    }
    assert (
        json.loads(report.manifest_path.read_bytes())["source_id"]
        == "mxl/good.mxl"
    )


def test_lieder_export_callback_and_single_piano_extract(
    tmp_path, frozen_inputs
):
    """Keep only the accompaniment from a local staged score export."""
    manifest, status, cache, _ = frozen_inputs
    with zipfile.ZipFile(tmp_path / "Lieder-v3.0.0.zip", mode="w") as archive:
        archive.writestr("fixture/scores/self-authored.mscx", b"self-authored")

    def export(payload):
        assert payload == b"self-authored"
        xml = to_musicxml(_score())
        return xml.replace(
            b"</part-list>",
            b'<score-part id="singer"><part-name>Voice</part-name>'
            b"</score-part></part-list>",
        ).replace(b"</score-partwise>", b'<part id="singer"/></score-partwise>')

    result = intake.intake_sources(
        intake.iter_lieder_candidates(tmp_path, export),
        run_dir=tmp_path / "run",
        rights_variant="permissive",
        gate=intake.TestExclusionGate(manifest, status, cache),
    )
    row = json.loads(result.manifest_path.read_bytes())
    xml = (result.manifest_path.parent / row["canonical_xml_path"]).read_bytes()
    assert row["license_url"] == "CC0-1.0"
    assert xml.count(b"<part id=") == 1
    assert b"singer" not in xml


def _xml(data):
    return ET.fromstring(data)  # noqa: S314 - local MusicXML produced by the converter


def _sounding(xml):
    return [
        (
            pitch.findtext("step"),
            pitch.findtext("alter", "0"),
            pitch.findtext("octave"),
        )
        for pitch in _xml(xml).findall(".//note/pitch")
    ]


def test_seeded_injection_round_trips_and_logs_every_added_category(tmp_path):
    """Exercise all categories while preserving sounding pitch and the base."""
    score = _score()
    result = inject_annotations(score, seed="scenario")
    assert result == inject_annotations(score, seed="scenario")
    assert (
        result.canonical_v2 != inject_annotations(score, seed="w4").canonical_v2
    )
    assert validate_document(result.score) == ()
    assert _sounding(result.musicxml) == _sounding(to_musicxml(score))
    rebuilt = parse_document(from_musicxml_text(result.musicxml))
    assert isinstance(rebuilt, AssembledScore)
    assert not compare_file(
        to_musicxml(rebuilt).decode("utf-8"), result.musicxml.decode("utf-8")
    ).category_counts()
    assert all(result.injected_counts.values())
    assert not any(result.native_counts.values())
    root = _xml(result.musicxml)
    assert {
        digit.get("placement")
        for digit in root.findall(".//notations/technical/fingering")
    } == {"above", "below"}
    assert all(
        slur.get("placement") in {"above", "below"}
        for slur in root.findall(".//notations/slur[@type='start']")
    )
    result.write_log(tmp_path)
    log = json.loads((tmp_path / "injection-log.json").read_bytes())
    by_measure = Counter()
    for row in log["injected_by_measure"].values():
        by_measure.update(row)
    assert log["injected_counts"] == by_measure == result.injected_counts


@pytest.mark.parametrize(
    ("native", "style", "line"),
    [
        (
            (
                "<ped(>[rel=pedal@r1:line-start] "
                "C2/4 <ped)>[rel=pedal@r1:line-release]"
            ),
            "line",
            "yes",
        ),
        ("<pedal-sign down> C2/4 <pedal-sign up>", "pedstar", "no"),
    ],
)
def test_injection_respects_native_pedal_style_and_distinct_onsets(
    native, style, line
):
    """Preserve native pedals while adding only their style."""
    source = _HEADER + f"m 1\n1.1: {_UPPER}\n2.1: {native} D2/4 E2/4 F2/4\n"
    source += "".join(
        f"m {i}\n1.1: {_UPPER}\n2.1: {_LOWER}\n" for i in range(2, 9)
    )
    score = parse_document(source)
    assert isinstance(score, AssembledScore)
    result = inject_annotations(score, seed="pedal", enabled_categories={"N5"})
    assert result.pedal_style_used == style
    assert result.native_counts["N5"] == 1
    assert result.injected_counts["N5"] > 0
    pedals = _xml(result.musicxml).findall(".//direction-type/pedal")
    assert {pedal.get("line", "no") for pedal in pedals} == {line}
    assert len(pedals) == 2 * (result.injected_counts["N5"] + 1)


def test_injection_leaves_tied_continuations_and_native_fingering_untouched():
    """Preserve native digits and omit ottavas in a measure with ties."""
    first = "C6f5{tie@r1:start}/4 C6{tie@r1:end}/4 E6/4 F6/4"
    score = _score(first=first)
    result = inject_annotations(
        score, seed="zero", enabled_categories={"N6", "P3"}
    )
    notes = _xml(result.musicxml).findall("./part/measure[@number='1']/note")
    tag = "notations/technical/fingering"
    assert notes[0].findtext(tag) == "5"
    assert notes[1].find(tag) is None
    assert result.native_counts["N6"] == 1
    assert result.injected_counts["N6"] > 0
    assert result.injected_counts["P3"] > 0
    assert (
        _xml(result.musicxml).find(".//measure[@number='1']//octave-shift")
        is None
    )


def test_malformed_comma_composer_and_barline_repedaling():
    """Strip composer lifespans, reject bad surnames and add barline pedals."""
    assert intake.normalize_work_identity(", A. Composer", "Sonata") is None
    with_dates = intake.normalize_work_identity(
        "Johann Sebastian Bach (1685-1750)", "Invention BWV 772"
    )
    plain = intake.normalize_work_identity("J. S. Bach", "Invention BWV 772")
    assert with_dates is not None
    assert plain is not None
    assert with_dates.matches(plain)
    whole_bass = _HEADER + "".join(
        f"m {i}\n1.1: {_UPPER}\n2.1: C2/1\n" for i in range(1, 7)
    )
    score = parse_document(whole_bass)
    assert isinstance(score, AssembledScore)
    result = inject_annotations(
        score, seed="barline-pedal", enabled_categories={"N5"}
    )
    assert result.injected_counts["N5"] >= 2  # noqa: PLR2004
    assert validate_document(result.score) == ()
    rebuilt = parse_document(from_musicxml_text(result.musicxml))
    assert isinstance(rebuilt, AssembledScore)
    assert not compare_file(
        to_musicxml(rebuilt).decode("utf-8"), result.musicxml.decode("utf-8")
    ).category_counts()

    for extra_m1 in (
        "m 1\n1.1: r/2. x/4\n2.1: r/2. x/4\n",
        "m 1 len=1/2\n1.1: C6/4 D6/4\n2.1: mrest\n",
    ):
        gapped = (
            _HEADER
            + extra_m1
            + "".join(
                f"m {i}\n1.1: C6/4 r/4 E6/4 F6/4\n2.1: {_LOWER}\n"
                for i in range(2, 8)
            )
        )
        rg_score = parse_document(gapped)
        assert isinstance(rg_score, AssembledScore)
        rg_inj = inject_annotations(
            rg_score, seed="0", enabled_categories={"P3"}
        )
        assert rg_inj.injected_counts["P3"] == 0
        assert not compare_file(
            rg_inj.musicxml.decode("utf-8"), rg_inj.musicxml.decode("utf-8")
        ).category_counts()
    assert _sounding(result.musicxml) == _sounding(to_musicxml(score))


def test_fingering_mixture_includes_zero_and_chord_stacked_digits():
    """Keep the sparse mode empty and place digits per chord pitch."""
    score = _score()
    sparse = inject_annotations(score, seed="one", enabled_categories={"N6"})
    assert sparse.injected_counts["N6"] == 0
    dense = inject_annotations(score, seed="zero", enabled_categories={"N6"})
    assert dense.injected_counts["N6"] > 5 * len(score.blocks)
    root = _xml(dense.musicxml)
    digits = root.findall(".//notations/technical/fingering")
    assert len(digits) == dense.injected_counts["N6"]
    assert {digit.text for digit in digits} <= set("12345")
    assert root.findall(".//note[chord]/notations/technical/fingering")


def test_injection_can_avoid_all_categories_or_mark_cross_staff_slurs():
    """Verify opt-out and a real cross-staff slur on a cross-staff voice."""
    score = _score(first="C6/4[xs2] D6/4 E6/4[xs2] F6/4")
    unchanged = inject_annotations(score, seed="zero", enabled_categories=set())
    assert unchanged.score == score
    assert not any(unchanged.injected_counts.values())
    result = inject_annotations(
        score, seed="scenario", enabled_categories={"L2"}
    )
    endpoints = {}
    for note in _xml(result.musicxml).findall(".//measure[@number='1']/note"):
        for slur in note.findall("./notations/slur"):
            endpoints.setdefault(slur.get("number"), []).append(
                note.findtext("staff")
            )
    assert any(
        staffs in (["1", "2"], ["2", "1"]) for staffs in endpoints.values()
    )


def test_unchecked_facets_combine_native_ast_and_exported_musicxml():
    """Find uncovered facets without rejecting ordinary dynamics."""
    score = _score(first="(C(#)6)/4[tremolo=single:3] D6/4 E6/4 F6/4")
    xml = b"""<score-partwise><part><measure>
      <direction><direction-type><bracket/><dashes/>
        <metronome parentheses="yes"/><pedal type="discontinue"/>
        <dynamics><other-dynamics>sempre</other-dynamics></dynamics>
      </direction-type></direction>
      <note><beam>backward hook</beam><notations><ornaments><wavy-line/>
        </ornaments></notations></note>
      </measure></part></score-partwise>"""
    assert has_unchecked_verovio_supplementary_facet(score, xml) == [
        "beam_hook",
        "dashes",
        "dynamic_text",
        "extended_trill",
        "parenthesized_accidental",
        "parenthesized_metronome",
        "parenthesized_notehead",
        "pedal_discontinue",
        "tremolo",
        "voice_leading_line",
    ]
    clean = _score()
    assert (
        has_unchecked_verovio_supplementary_facet(clean, to_musicxml(clean))
        == []
    )
