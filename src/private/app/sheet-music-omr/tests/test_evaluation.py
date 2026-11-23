"""Core algorithm and scenario tests for W2 evaluation and W3 alignment."""

from __future__ import annotations

import http.client
import io
import json
import xml.etree.ElementTree as ET
from typing import TYPE_CHECKING, Any, Self

import numpy as np
import pytest
from PIL import Image

from sheet_music_omr.evaluation import alignment as aln_mod
from sheet_music_omr.evaluation import copista as cop_mod
from sheet_music_omr.evaluation.alignment import (
    approve_overlays_and_export_crops,
    classify_aspect_convergence,
    detect_grand_staff_systems,
    export_normalized_crop,
    sha256_bytes,
    sha256_file,
)
from sheet_music_omr.evaluation.cli import (
    _piece_table_from_score,
    main,
)
from sheet_music_omr.evaluation.copista import append_test_touch
from sheet_music_omr.evaluation.scorer import score_prediction
from sheet_music_omr.evaluation.stats import (
    BlindDraftMeasure,
    PieceMeasureTable,
    apply_platt,
    calibration_metrics,
    cluster_bootstrap_paired,
    expected_review_passes,
    fit_monotone_platt,
    fit_pass_model_from_t01,
    g2_recall_table,
    overall_rate,
    rank_second_reader_flags,
)
from sheet_music_omr.evaluation.xml_merge import (
    concat_page_predictions,
    merge_two_part_grand_staff,
)
from sheet_music_omr.harness.records import PROJECT_DIR
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from pathlib import Path


def _two_part_xml(rh_pitch: str = "C", m_count: int = 2) -> str:
    rh_measures = []
    lh_measures = []
    for m in range(1, m_count + 1):
        attrs_rh = (
            "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
            "<time><beats>4</beats><beat-type>4</beat-type></time>"
            "<clef><sign>G</sign><line>2</line></clef></attributes>"
            if m == 1
            else ""
        )
        attrs_lh = (
            "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
            "<time><beats>4</beats><beat-type>4</beat-type></time>"
            "<clef><sign>F</sign><line>4</line></clef></attributes>"
            if m == 1
            else ""
        )
        rh_measures.append(
            f'<measure number="{m}">{attrs_rh}'
            f"<note><pitch><step>{rh_pitch}</step><octave>4</octave></pitch>"
            "<duration>4</duration><voice>1</voice><type>whole</type></note></measure>"
        )
        lh_measures.append(
            f'<measure number="{m}">{attrs_lh}'
            "<note><pitch><step>C</step><octave>3</octave></pitch>"
            "<duration>4</duration><voice>1</voice><type>whole</type></note></measure>"
        )
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        "<score-partwise><part-list>"
        '<score-part id="P1"><part-name>Right</part-name></score-part>'
        '<score-part id="P2"><part-name>Left</part-name></score-part>'
        f'</part-list><part id="P1">{"".join(rh_measures)}</part>'
        f'<part id="P2">{"".join(lh_measures)}</part></score-partwise>'
    )


def _one_part_grand_staff_xml(measures_body: list[str]) -> str:
    joined = "".join(measures_body)
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        "<score-partwise><part-list>"
        '<score-part id="P1"><part-name>Piano</part-name></score-part>'
        f'</part-list><part id="P1">{joined}</part></score-partwise>'
    )


def _grand_measure(
    num: int, *, rh_step: str = "C", first: bool = False, beam_tag: str = ""
) -> str:
    attrs = (
        "<attributes><divisions>2</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<staves>2</staves>"
        '<clef number="1"><sign>G</sign><line>2</line></clef>'
        '<clef number="2"><sign>F</sign><line>4</line></clef></attributes>'
        if first
        else ""
    )
    return (
        f'<measure number="{num}">{attrs}'
        f"<note><pitch><step>{rh_step}</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>half</type>"
        f"<staff>1</staff>{beam_tag}</note>"
        "<note><pitch><step>E</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>half</type>"
        "<staff>1</staff></note>"
        "<backup><duration>8</duration></backup>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>8</duration><voice>5</voice><type>whole</type>"
        "<staff>2</staff></note>"
        "</measure>"
    )


def test_two_part_merge_and_page_concat_scenario(tmp_path: Path) -> None:
    """Two-part pages merge into a single-part grand staff with 0 diffs."""
    p1 = merge_two_part_grand_staff(_two_part_xml("C", m_count=2))
    p2 = merge_two_part_grand_staff(_two_part_xml("G", m_count=1))
    merged_piece = concat_page_predictions([p1, p2])
    assert merged_piece.parse_valid is True
    assert merged_piece.xml_text is not None

    cand_p = tmp_path / "cand.musicxml"
    cand_p.write_text(merged_piece.xml_text, encoding="utf-8")

    sc = score_prediction(cand_p, cand_p)
    assert sc.parse_valid is True
    assert sc.ref_measure_count == 3  # noqa: PLR2004
    assert sc.cand_measure_count == 3  # noqa: PLR2004
    assert sc.d_norm_aligned == 0.0
    assert all(not ms.layer0_counts for ms in sc.measures)
    # Multi-measure self-comparison preserves 100% D3 voice element recall
    assert sc.element_identical_layer1.get("voice") == sc.element_totals.get(
        "voice"
    )
    assert all(
        ms.element_identical_layer1.get("voice")
        == ms.element_totals.get("voice")
        for ms in sc.measures
    )

    # Effective meter carry-forward rejects bar-2 conflict on one staff only
    conflicting_meter_2p = (
        '<?xml version="1.0" encoding="utf-8"?>'
        "<score-partwise><part-list>"
        '<score-part id="P1"><part-name>Right</part-name></score-part>'
        '<score-part id="P2"><part-name>Left</part-name></score-part>'
        '</part-list><part id="P1"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>G</sign><line>2</line></clef></attributes>"
        "<note><pitch><step>C</step><octave>4</octave></pitch>"
        "<duration>4</duration><type>whole</type></note></measure>"
        '<measure number="2"><attributes>'
        "<time><beats>3</beats><beat-type>4</beat-type></time></attributes>"
        "<note><pitch><step>D</step><octave>4</octave></pitch>"
        "<duration>3</duration><type>half</type><dot/></note></measure></part>"
        '<part id="P2"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>F</sign><line>4</line></clef></attributes>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><type>whole</type></note></measure>"
        '<measure number="2">'
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><type>whole</type></note></measure></part>"
        "</score-partwise>"
    )
    with pytest.raises(
        aln_mod.MusicXmlError.__bases__[0], match="conflicting effective time"
    ):
        merge_two_part_grand_staff(conflicting_meter_2p)

    # Mid-measure clef change stays at its timed onset after first note
    mid_clef_2p = (
        '<?xml version="1.0" encoding="utf-8"?>'
        "<score-partwise><part-list>"
        '<score-part id="P1"><part-name>Right</part-name></score-part>'
        '<score-part id="P2"><part-name>Left</part-name></score-part>'
        '</part-list><part id="P1"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>G</sign><line>2</line></clef></attributes>"
        "<note><pitch><step>C</step><octave>4</octave></pitch>"
        "<duration>2</duration><type>half</type></note>"
        "<attributes><clef><sign>F</sign><line>4</line></clef></attributes>"
        "<note><pitch><step>E</step><octave>3</octave></pitch>"
        "<duration>2</duration><type>half</type></note></measure></part>"
        '<part id="P2"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>F</sign><line>4</line></clef></attributes>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><type>whole</type></note></measure></part>"
        "</score-partwise>"
    )
    merged_mid_clef = merge_two_part_grand_staff(mid_clef_2p)
    tags_m1 = [
        (
            ch.tag,
            ch.findtext("pitch/step"),
            ch.findtext("clef[@number='1']/sign"),
        )
        for ch in ET.fromstring(merged_mid_clef).findall("./part/measure/*")  # noqa: S314
    ]
    assert ("note", "C", None) in tags_m1
    assert ("attributes", None, "F") in tags_m1
    assert ("note", "E", None) in tags_m1
    assert (
        tags_m1.index(("note", "C", None))
        < tags_m1.index(("attributes", None, "F"))
        < tags_m1.index(("note", "E", None))
    )

    # Common time vs numeric 4/4 is B-normalized out of Layer 1, and deleting
    # a 3-note chord marks 0/2 chord_membership constituents identical!
    ref_chord_m1 = (
        '<measure number="1"><attributes><divisions>1</divisions>'
        "<key><fifths>0</fifths></key>"
        '<time symbol="common"><beats>4</beats>'
        "<beat-type>4</beat-type></time><staves>2</staves>"
        '<clef number="1"><sign>G</sign><line>2</line></clef>'
        '<clef number="2"><sign>F</sign><line>4</line></clef></attributes>'
        "<note><pitch><step>C</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>whole</type>"
        "<staff>1</staff></note>"
        "<note><chord/><pitch><step>E</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>whole</type>"
        "<staff>1</staff></note>"
        "<note><chord/><pitch><step>G</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>whole</type>"
        "<staff>1</staff></note>"
        "<backup><duration>4</duration></backup>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><voice>5</voice><type>whole</type>"
        "<staff>2</staff></note></measure>"
    )
    ref_chord_common = _one_part_grand_staff_xml([ref_chord_m1])
    cand_numeric_44 = ref_chord_common.replace(
        '<time symbol="common">', "<time>"
    )
    sc_ts = score_prediction(cand_numeric_44, ref_chord_common)
    assert sc_ts.measures[0].layer0_counts.get("time_signature", 0) == 2  # noqa: PLR2004
    assert "time_signature" not in sc_ts.measures[0].layer1_counts

    # Contradictory symbol="common" on 3/4 MUST NOT be suppressed in Layer 1!
    cand_bad_34_common = ref_chord_common.replace(
        "<beats>4</beats>", "<beats>3</beats>"
    )
    sc_bad_ts = score_prediction(cand_bad_34_common, cand_numeric_44)
    assert sc_bad_ts.measures[0].layer1_counts.get("time_signature", 0) >= 1

    cand_rest_m1 = (
        '<measure number="1"><attributes><divisions>1</divisions>'
        "<key><fifths>0</fifths></key>"
        '<time symbol="common"><beats>4</beats>'
        "<beat-type>4</beat-type></time><staves>2</staves>"
        '<clef number="1"><sign>G</sign><line>2</line></clef>'
        '<clef number="2"><sign>F</sign><line>4</line></clef></attributes>'
        "<note><rest/><duration>4</duration><voice>1</voice>"
        "<type>whole</type><staff>1</staff></note>"
        "<backup><duration>4</duration></backup>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><voice>5</voice><type>whole</type>"
        "<staff>2</staff></note></measure>"
    )
    cand_rest_rh = _one_part_grand_staff_xml([cand_rest_m1])
    sc_ch = score_prediction(cand_rest_rh, ref_chord_common)
    assert sc_ch.element_totals["chord_membership"] == 2  # noqa: PLR2004
    assert sc_ch.element_identical_layer1["chord_membership"] == 0
    assert sc_ch.category_edit_distance.get("chord_membership", 0) >= 1


def test_scorer_dp_alignment_forbidden_pages_and_fail_closed(
    tmp_path: Path,
) -> None:
    """DP alignment handles missing/extra bars, failed pages and bad XML."""
    ref_xml = _one_part_grand_staff_xml(
        [
            _grand_measure(1, rh_step="C", first=True),
            _grand_measure(2, rh_step="D"),
            _grand_measure(3, rh_step="F"),
        ]
    )
    # 1) Candidate drops measure 2 -> [m1(C), m3(F)] (insbar on ref m2)
    cand_drop = _one_part_grand_staff_xml(
        [
            _grand_measure(1, rh_step="C", first=True),
            _grand_measure(2, rh_step="F"),
        ]
    )
    ref_p = tmp_path / "ref.musicxml"
    cand_p = tmp_path / "cand.musicxml"
    ref_p.write_text(ref_xml, encoding="utf-8")
    cand_p.write_text(cand_drop, encoding="utf-8")

    sc = score_prediction(cand_p, ref_p)
    assert sc.parse_valid is True
    assert sc.measures[0].aligned_cand_ordinal == 1
    assert not sc.measures[0].layer1_counts
    assert sc.measures[1].aligned_cand_ordinal is None
    assert sc.measures[1].layer1_counts.get("repeat_ending", 0) >= 1
    assert sc.measures[1].insbar_anchor_cand_ordinal == 1
    assert sc.measures[2].aligned_cand_ordinal == 2  # noqa: PLR2004
    assert not sc.measures[2].layer1_counts

    # Forbidden failed-page ordinals prevent cand m2 from stealing ref m3
    sc_forb = score_prediction(
        cand_p, ref_p, forbidden_ref_ordinals=frozenset({3})
    )
    assert sc_forb.measures[2].aligned_cand_ordinal is None
    assert sc_forb.measures[2].layer1_counts.get("repeat_ending", 0) >= 1

    # 2) Candidate adds an extra bar at the end -> 4 measures vs 3 ref measures
    cand_extra = _one_part_grand_staff_xml(
        [
            _grand_measure(1, rh_step="C", first=True),
            _grand_measure(2, rh_step="D"),
            _grand_measure(3, rh_step="F"),
            _grand_measure(4, rh_step="A"),
        ]
    )
    extra_p = tmp_path / "extra.musicxml"
    extra_p.write_text(cand_extra, encoding="utf-8")
    sc_ex = score_prediction(extra_p, ref_p)
    assert sc_ex.measures[2].delbar_cand_ordinals == (4,)
    assert sc_ex.measures[2].layer1_counts.get("repeat_ending", 0) >= 1
    assert len(sc_ex.extra_cand_bars) == 1
    assert sc_ex.extra_cand_bars[0].attributed_ref_ordinal == 3  # noqa: PLR2004

    # Unparsable candidate fails closed on candidate only
    bad_p = tmp_path / "bad.musicxml"
    bad_p.write_text("<not-xml", encoding="utf-8")
    sc_bad = score_prediction(bad_p, ref_p)
    assert sc_bad.parse_valid is False
    assert sc_bad.d_norm_aligned == 1.0
    assert all("repeat_ending" in ms.layer1_counts for ms in sc_bad.measures)


def test_grand_staff_detector_and_crop_normalization(tmp_path: Path) -> None:
    """Grand-staff detector pairs 2 staves and normalizes crop to 14 px."""
    img = np.full((1800, 1400), 255, dtype=np.uint8)
    for sys_top in (250, 1000):
        for st_off in (0, 190):
            for k in range(5):
                y = sys_top + st_off + k * 16
                img[y : y + 2, 160:1240] = 0
        img[sys_top : sys_top + 256, 160:164] = 0

    pil_img = Image.fromarray(img, mode="L")
    systems = detect_grand_staff_systems(pil_img, expected_systems=2)
    assert len(systems) == 2  # noqa: PLR2004
    assert abs(systems[0].staff_space_px - 16.0) < 1.0

    crop_p = tmp_path / "sys0.png"
    w_out, h_out, scale = export_normalized_crop(
        pil_img, systems[0].box_px, systems[0].staff_space_px, crop_p
    )
    assert crop_p.exists()
    assert 0.80 < scale < 0.95  # noqa: PLR2004
    assert w_out > 900  # noqa: PLR2004
    assert h_out > 200  # noqa: PLR2004


def test_stats_bootstrap_calibration_ranking_and_pass_model() -> None:
    """Bootstrap, Platt scaling, blind ranking and pass-model fit contracts."""
    t_base = [
        PieceMeasureTable(
            gold_id=f"gold-0{i}",
            book_cohort="book1",
            measure_count=4,
            layer1_errors=(
                frozenset({"pitch"}),
                frozenset(),
                frozenset({"rhythm_rest"}),
                frozenset(),
            ),
            masked_categories=(frozenset(),) * 4,
            present_categories=(frozenset({"pitch", "rhythm_rest"}),) * 4,
            element_totals={"pitch": 10},
            element_identical={"pitch": 8},
        )
        for i in range(1, 4)
    ]
    t_cand = [
        PieceMeasureTable(
            gold_id=f"gold-0{i}",
            book_cohort="book1",
            measure_count=4,
            layer1_errors=(
                frozenset({"pitch"}),
                frozenset(),
                frozenset(),
                frozenset(),
            ),
            masked_categories=(frozenset(),) * 4,
            present_categories=(frozenset({"pitch", "rhythm_rest"}),) * 4,
            element_totals={"pitch": 10},
            element_identical={"pitch": 9},
        )
        for i in range(1, 4)
    ]
    boot = cluster_bootstrap_paired(
        t_cand, t_base, overall_rate, replicates=500, seed=7
    )
    assert boot["e_baseline"] == 0.5  # noqa: PLR2004
    assert boot["e_candidate"] == 0.25  # noqa: PLR2004
    assert boot["ratio"] == 0.5  # noqa: PLR2004

    g2 = g2_recall_table(t_cand, t_base)
    assert g2["P1"]["passes_g2"] is True

    # Monotone Platt calibration preserves AUROC rank and maps into (0, 1)
    raws = [0.2, 0.3, 0.4, 0.8, 0.9, 0.95]
    errs = [True, True, False, False, False, False]
    params = fit_monotone_platt(raws, [not e for e in errs])
    assert params[0] >= 0.0
    cals = [apply_platt(r, params) for r in raws]
    cm = calibration_metrics(cals, errs)
    assert cm["auroc"] is not None
    assert cm["auroc"] > 0.8  # noqa: PLR2004

    # Blind second-reader deterministic ranking
    bms = [
        BlindDraftMeasure(
            "gold-01",
            i,
            delta=(2 if i == 3 else 0),  # noqa: PLR2004
            uncertainty=0.1 * i,
        )
        for i in range(1, 11)
    ]
    flags = rank_second_reader_flags(bms)
    assert ("gold-01", 3) in flags["at_10pct"]
    assert flags["unthresholded"] == {("gold-01", 3)}

    # Pass model & T01 fit
    pm_fit = fit_pass_model_from_t01({1: 60, 2: 24, 3: 16}, [1, 2, 1, 0])
    assert pm_fit["q_hat"] == 0.4  # noqa: PLR2004
    assert expected_review_passes(0.4, d=0.6, phi=0.5) > 2  # noqa: PLR2004


def test_test_touch_circuit_breaker(tmp_path: Path) -> None:
    """w2-touch-log.jsonl enforces the 6-touch Stage S0 TEST cap."""
    log_p = tmp_path / "w2-touch-log.jsonl"
    for idx in range(6):
        cnt = append_test_touch(
            touch_log_path=log_p,
            run_id=f"r-{idx}",
            git_commit="abc",
            code_tree_sha256="def",
            reader_id="agent-build-01",
            reader_sha256=None,
            weights_sha256=None,
            piece_count=7,
            command=["score-s0"],
            cap=6,
        )
        assert cnt == idx + 1

    with pytest.raises(StopConditionError, match="exceed 6-touch"):
        append_test_touch(
            touch_log_path=log_p,
            run_id="r-7",
            git_commit="abc",
            code_tree_sha256="def",
            reader_id="agent-build-01",
            reader_sha256=None,
            weights_sha256=None,
            piece_count=7,
            command=["score-s0"],
            cap=6,
        )


def test_confinement_bad_page_isolation_backup_fingering_and_masks(
    tmp_path: Path,
) -> None:
    """Verify confinement, bad-page isolation, backup, G2 masks and Platt."""
    # 1. CLI rejects in-worktree run-dir and accepts external tmp_path
    inside_dir = PROJECT_DIR / "forbidden-inside-run"
    rc = main(
        [
            "approve-overlays",
            "--run-dir",
            str(inside_dir),
            "--downloads",
            str(tmp_path / "dl"),
            "--overlays-sha256",
            "0" * 64,
            "--note",
            "test",
        ]
    )
    assert rc == 2  # noqa: PLR2004
    assert not inside_dir.exists()

    ext_run = tmp_path / "ext-run"
    rc_ext = main(
        [
            "approve-overlays",
            "--run-dir",
            str(ext_run),
            "--downloads",
            str(tmp_path / "dl"),
            "--overlays-sha256",
            "0" * 64,
            "--note",
            "test",
        ]
    )
    # External path passes confinement and records stop in ext_run/run.json
    assert rc_ext == 2  # noqa: PLR2004
    assert (ext_run / "run.json").exists()

    # Symlink run.json inside run_dir is rejected before Confinement/record_stop
    victim_json = tmp_path / "victim.json"
    victim_json.write_text('{"intact": true}\n', encoding="utf-8")
    sym_run = tmp_path / "sym-run"
    sym_run.mkdir()
    (sym_run / "run.json").symlink_to(victim_json)
    rc_sym = main(
        [
            "approve-overlays",
            "--run-dir",
            str(sym_run),
            "--downloads",
            str(tmp_path / "dl"),
            "--overlays-sha256",
            "0" * 64,
            "--note",
            "test",
        ]
    )
    assert rc_sym == 2  # noqa: PLR2004
    assert json.loads(victim_json.read_text(encoding="utf-8")) == {
        "intact": True
    }

    # 2. Invalid reference raises StopConditionError (not silent candidate fail)
    bad_ref = tmp_path / "bad_ref.musicxml"
    bad_ref.write_text("<broken", encoding="utf-8")
    with pytest.raises(StopConditionError, match="reference score defect"):
        score_prediction(None, bad_ref)

    # 3. Bad page with pitch step Q is isolated into failed_pages=(2,)
    good_page = _two_part_xml("C", m_count=1)
    bad_page = _two_part_xml("Q", m_count=1)
    assembled = concat_page_predictions([good_page, bad_page])
    assert assembled.parse_valid is True
    assert assembled.failed_pages == (2,)

    # 4. Two-voice RH ending after short backed-up voice aligns staff 2 at 0
    two_voice_2p = (
        '<?xml version="1.0" encoding="utf-8"?>'
        "<score-partwise><part-list>"
        '<score-part id="P1"><part-name>Right</part-name></score-part>'
        '<score-part id="P2"><part-name>Left</part-name></score-part>'
        '</part-list><part id="P1"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>G</sign><line>2</line></clef></attributes>"
        "<note><pitch><step>G</step><octave>4</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>whole</type></note>"
        "<backup><duration>4</duration></backup>"
        "<note><pitch><step>E</step><octave>4</octave></pitch>"
        "<duration>2</duration><voice>2</voice><type>half</type></note>"
        '</measure></part><part id="P2"><measure number="1">'
        "<attributes><divisions>1</divisions><key><fifths>0</fifths></key>"
        "<time><beats>4</beats><beat-type>4</beat-type></time>"
        "<clef><sign>F</sign><line>4</line></clef></attributes>"
        "<note><pitch><step>C</step><octave>3</octave></pitch>"
        "<duration>4</duration><voice>1</voice><type>whole</type></note>"
        "</measure></part></score-partwise>"
    )
    merged_tv = merge_two_part_grand_staff(two_voice_2p)
    sc_tv = score_prediction(merged_tv, merged_tv)
    assert sc_tv.parse_valid is True
    assert not sc_tv.measures[0].layer1_counts

    # 5. Partial substitution fingering 2-3 vs 2-4 gets 0/1 identical, not 1/1
    f_ref = _one_part_grand_staff_xml(
        [
            _grand_measure(
                1,
                rh_step="C",
                first=True,
                beam_tag="<notations><technical><fingering>2-3</fingering></technical></notations>",
            )
        ]
    )
    f_cand = _one_part_grand_staff_xml(
        [
            _grand_measure(
                1,
                rh_step="C",
                first=True,
                beam_tag="<notations><technical><fingering>2-4</fingering></technical></notations>",
            )
        ]
    )
    sc_f = score_prediction(f_cand, f_ref)
    assert sc_f.element_totals.get("fingering") == 1
    assert sc_f.element_identical_layer1.get("fingering") == 0
    assert sc_f.measures[0].layer1_counts.get("fingering", 0) >= 1

    # 6. Open-reading masks exclude masked measure elements from G2 recall
    pm_meta = {
        "gold_id": "gold-01",
        "book_cohort": "book1",
        "measure_count": 1,
        "open_reading_masks": [
            {"measure_ordinal": 1, "categories": ["fingering"]}
        ],
    }
    tbl = _piece_table_from_score(pm_meta, sc_f)
    assert tbl.element_totals.get("fingering", 0) == 0

    # 7. Anti-correlated Platt inputs converge to finite intercept at a == 0
    pa, pb, pconst = fit_monotone_platt(
        [0.01, 0.02, 0.99, 0.98], [True, True, False, True]
    )
    assert pconst is None
    assert pa == 0.0
    assert abs(pb) < 5.0  # noqa: PLR2004

    # 8. Missing convergence evidence fails closed
    with pytest.raises(StopConditionError):
        classify_aspect_convergence({})


def test_installer_wheel_decoding_provenance_and_overlay_geometry(  # noqa: C901, PLR0915
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify %2B wheel decoding, 2-step offline install and geometry hash."""
    # 1. Pinned wheel lock %2B decoding + 2-step offline install + socket meter
    cache_d = tmp_path / "cache"
    cache_d.mkdir()
    fetched_dests: list[str] = []
    subproc_cmds: list[list[str]] = []

    def fake_fetch(**kwargs: object) -> tuple[int, str]:
        dest = kwargs["dest"]
        assert hasattr(dest, "name")
        fetched_dests.append(str(dest.name))  # type: ignore[attr-defined]
        return 10, "aa"

    def fake_run(cmd: list[str], **_kw: object) -> object:
        subproc_cmds.append(cmd)
        return None

    compile_ledger_p = tmp_path / "ledger.json"
    monkeypatch.setattr(cop_mod, "_fetch_with_ledger", fake_fetch)
    monkeypatch.setattr(cop_mod.subprocess, "run", fake_run)
    cop_mod._install_copista_wheels(  # noqa: SLF001
        tmp_path / "py", tmp_path / "src", cache_d, compile_ledger_p
    )
    assert any(n.startswith("torch-") and "+cpu-" in n for n in fetched_dests)
    assert len(subproc_cmds) == 2  # noqa: PLR2004
    assert all("--offline" in c and "--no-index" in c for c in subproc_cmds)
    assert "-e" not in subproc_cmds[0]
    assert "--no-build-isolation" in subproc_cmds[1]
    assert "-e" in subproc_cmds[1]

    # 1b. _MeteredSocket retains fd after conn.close() on >8 KiB body + caps 302
    import socket  # noqa: PLC0415
    import threading  # noqa: PLC0415

    big_body = b"Z" * 20_000
    s_cli1, s_srv1 = socket.socketpair()

    def _serve_big() -> None:
        _ = s_srv1.recv(4096)
        hdr = (
            "HTTP/1.1 200 OK\r\nConnection: close\r\n"
            f"Content-Length: {len(big_body)}\r\n\r\n"
        )
        s_srv1.sendall(hdr.encode("ascii") + big_body)
        s_srv1.close()

    t_big = threading.Thread(target=_serve_big)
    t_big.start()
    wire_big = [0]
    conn_big = http.client.HTTPConnection("example.org")
    conn_big.sock = cop_mod._MeteredSocket(  # noqa: SLF001
        s_cli1, ceiling=25_000, wire=wire_big, label="big-probe"
    )
    conn_big.request("GET", "/big", headers={"Connection": "close"})
    r_big = conn_big.getresponse()
    conn_big.close()  # Exact urllib.request.AbstractHTTPHandler.do_open order!
    got_big = r_big.read()
    assert got_big == big_body
    assert wire_big[0] > len(big_body)
    r_big.close()
    t_big.join()

    s_cli, s_srv = socket.socketpair()

    def _serve_redir() -> None:
        _ = s_srv.recv(4096)
        s_srv.sendall(
            b"HTTP/1.1 302 Found\r\nLocation: https://x.invalid/b\r\n"
            b"Content-Length: 500\r\n\r\n" + (b"X" * 500)
        )
        s_srv.close()

    t_srv = threading.Thread(target=_serve_redir)
    t_srv.start()
    wire_cnt = [0]
    h_conn = http.client.HTTPConnection("example.org")
    h_conn.sock = cop_mod._MeteredSocket(  # noqa: SLF001
        s_cli, ceiling=120, wire=wire_cnt, label="redir-probe"
    )
    h_conn.request("GET", "/a")
    r_302 = h_conn.getresponse()
    assert r_302.status == 302  # noqa: PLR2004
    assert wire_cnt[0] == 120  # noqa: PLR2004
    r_302.close()
    h_conn.close()
    t_srv.join()

    # 1c. Chunked transfer without Content-Length + pinned expected_bytes works
    s_cli_ch, s_srv_ch = socket.socketpair()
    chunk_payload = b"hello-chunked"

    def _serve_chunked() -> None:
        _ = s_srv_ch.recv(4096)
        s_srv_ch.sendall(
            b"HTTP/1.1 200 OK\r\nConnection: close\r\n"
            b"Transfer-Encoding: chunked\r\n\r\n"
            + f"{len(chunk_payload):x}\r\n".encode("ascii")
            + chunk_payload
            + b"\r\n0\r\n\r\n"
        )
        s_srv_ch.close()

    t_ch = threading.Thread(target=_serve_chunked)
    t_ch.start()
    wire_ch = [0]
    conn_ch = http.client.HTTPConnection("example.org")
    conn_ch.sock = cop_mod._MeteredSocket(  # noqa: SLF001
        s_cli_ch, ceiling=2000, wire=wire_ch, label="chunk-probe"
    )
    conn_ch.request("GET", "/ch", headers={"Connection": "close"})
    r_ch = conn_ch.getresponse()
    conn_ch.close()
    monkeypatch.setattr(
        cop_mod,
        "_open_https_request",
        lambda *_a, **_k: (r_ch, True),
    )
    ch_out = tmp_path / "chunked.bin"
    b_ch, _ = cop_mod._stream_https_to_file(  # noqa: SLF001
        url="https://example.org/ch",
        tmp=ch_out,
        ceiling=2000,
        expected_bytes=len(chunk_payload),
        label="chunk-probe",
        received=wire_ch,
    )
    assert b_ch == len(chunk_payload)
    assert ch_out.read_bytes() == chunk_payload
    t_ch.join()

    # 1d. Campaign-wide frozen open-reading masks reject post-freeze changes
    frz_dl = tmp_path / "frz-dl"
    aln_mod.freeze_run_masks(tmp_path / "frz-run-1", frz_dl, "a" * 64)
    with pytest.raises(StopConditionError, match="campaign freeze"):
        aln_mod.check_frozen_run_masks(tmp_path / "frz-run-2", frz_dl, "b" * 64)

    # 2. Worker script changes alter copista_provenance_sha256
    base_prov: dict[str, object] = {
        "copisteria_commit": "c1",
        "copisteria_archive_sha256": "s1",
        "combined_weights_sha256": "w1",
        "venv_versions": {"python": "3.14"},
    }
    sha_before = cop_mod.copista_provenance_sha256(base_prov)
    monkeypatch.setattr(
        cop_mod,
        "_WORKER_SCRIPT",
        cop_mod._WORKER_SCRIPT + "\n# rev2",  # noqa: SLF001
    )
    assert cop_mod.copista_provenance_sha256(base_prov) != sha_before

    # 3. Edited manifest geometry fails BEFORE exporting any crops
    run_d = tmp_path / "geom-run"
    for sub in ("alignment", "pages", "overlays"):
        (run_d / sub).mkdir(parents=True)
    im = Image.new("L", (200, 200), 255)
    pg_file = run_d / "pages" / "gold-01-p01.png"
    ov_file = run_d / "overlays" / "gold-01-p01.png"
    im.save(pg_file)
    im.save(ov_file)
    pg_sha = sha256_bytes(f"gold-01-p01:{sha256_file(pg_file)}".encode())
    ov_sha = sha256_bytes(f"gold-01-p01:{sha256_file(ov_file)}".encode())
    bad_manifest: dict[str, Any] = {
        "pages_sha256": pg_sha,
        "overlays_sha256": ov_sha,
        "geometry_sha256": "stale-geom-hash",
        "pieces": [
            {
                "gold_id": "gold-01",
                "pages": [{"page_index": 1}],
                "systems": [
                    {
                        "system_index": 1,
                        "page_index": 1,
                        "ordinal_range": [1, 4],
                        "facsimile_box_px": [10, 10, 100, 100],
                        "staff_space_px": 14.0,
                    }
                ],
            }
        ],
    }
    (run_d / "alignment" / "manifest.json").write_text(
        json.dumps(bad_manifest), encoding="utf-8"
    )
    with pytest.raises(StopConditionError, match="geometry edited"):
        approve_overlays_and_export_crops(run_d, "qa", ov_sha)
    assert not (run_d / "crops" / "facsimile" / "gold-01-s01.png").exists()

    # 4. Atomic batch touch check rejects 2-reader batch at 5/6 cleanly
    batch_log = tmp_path / "batch-touch.jsonl"
    for idx in range(5):
        _ = append_test_touch(
            touch_log_path=batch_log,
            run_id=f"b-{idx}",
            git_commit="a",
            code_tree_sha256="b",
            reader_id="r1",
            reader_sha256=None,
            weights_sha256=None,
            piece_count=7,
            command=["score-s0"],
            cap=6,
        )
    with pytest.raises(StopConditionError, match="exceed 6-touch"):
        cop_mod.append_test_touches(
            touch_log_path=batch_log,
            run_id="b-fail",
            git_commit="a",
            code_tree_sha256="b",
            readers=[("r1", None, None), ("r2", None, None)],
            piece_count=7,
            command=["score-s0"],
            cap=6,
        )
    assert len(batch_log.read_text(encoding="utf-8").splitlines()) == 5  # noqa: PLR2004

    # 5. Over-cap stream transfer stops AT cap without reading extra byte!
    monkeypatch.undo()
    ledger_p = tmp_path / "cap-ledger.json"
    ledger_p.write_text(
        json.dumps(
            {
                "schema": "w2-download-ledger-v1",
                "cap_bytes": cop_mod.W2_DOWNLOAD_CAP_BYTES,
                "cumulative_bytes": cop_mod.W2_DOWNLOAD_CAP_BYTES - 2,
                "entries": [],
            }
        ),
        encoding="utf-8",
    )

    monkeypatch.setattr(
        cop_mod.urllib.request,
        "urlopen",
        lambda *_a, **_k: io.BytesIO(b"abcde"),
    )
    with pytest.raises(StopConditionError, match="ceiling"):
        cop_mod._fetch_with_ledger(  # noqa: SLF001
            url="https://example.org/blob",
            dest=tmp_path / "blob.bin",
            expected_bytes=None,
            expected_sha256=None,
            ledger_path=ledger_p,
            label="overcap-probe",
        )
    saved_ledger = json.loads(ledger_p.read_text(encoding="utf-8"))
    assert saved_ledger["entries"][-1]["bytes"] == 2  # noqa: PLR2004
    assert saved_ledger["cumulative_bytes"] == cop_mod.W2_DOWNLOAD_CAP_BYTES

    # 5b. Orphaned "reserved" ceiling is retained monotonically on reload
    orphan_p = tmp_path / "orphan-ledger.json"
    orphan_p.write_text(
        json.dumps(
            {
                "schema": "w2-download-ledger-v1",
                "cap_bytes": cop_mod.W2_DOWNLOAD_CAP_BYTES,
                "cumulative_bytes": 100,
                "entries": [{"bytes": 500, "status": "reserved"}],
            }
        ),
        encoding="utf-8",
    )
    reloaded = cop_mod._load_w2_ledger(orphan_p)  # noqa: SLF001
    assert reloaded["cumulative_bytes"] == 500  # noqa: PLR2004

    # 6. Truncated IncompleteRead charges partial received bytes
    ledger_p2 = tmp_path / "trunc-ledger.json"

    class _TruncStream:
        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_a: object) -> None:
            return None

        def read1(self, _n: int) -> bytes:
            part = b"xy"
            raise http.client.IncompleteRead(part, 10)

    monkeypatch.setattr(
        cop_mod.urllib.request, "urlopen", lambda *_a, **_k: _TruncStream()
    )
    with pytest.raises(StopConditionError, match="failed"):
        cop_mod._fetch_with_ledger(  # noqa: SLF001
            url="https://example.org/trunc",
            dest=tmp_path / "trunc.bin",
            expected_bytes=12,
            expected_sha256=None,
            ledger_path=ledger_p2,
            label="trunc-probe",
        )
    trunc_ledger = json.loads(ledger_p2.read_text(encoding="utf-8"))
    assert trunc_ledger["entries"][-1]["bytes"] == 2  # noqa: PLR2004

    # 6b. Premature EOF with Content-Length header on read1() fails & charges
    ledger_p3 = tmp_path / "cl-ledger.json"

    class _ShortContentLenStream:
        def __init__(self) -> None:
            self.headers = {"Content-Length": "12"}
            self._sent = False

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_a: object) -> None:
            return None

        def read1(self, _n: int) -> bytes:
            if not self._sent:
                self._sent = True
                return b"ab"
            return b""

    monkeypatch.setattr(
        cop_mod.urllib.request,
        "urlopen",
        lambda *_a, **_k: _ShortContentLenStream(),
    )
    with pytest.raises(StopConditionError, match="Content-Length"):
        cop_mod._fetch_with_ledger(  # noqa: SLF001
            url="https://example.org/short",
            dest=tmp_path / "short.bin",
            expected_bytes=None,
            expected_sha256=None,
            ledger_path=ledger_p3,
            label="short-probe",
        )
    assert not (tmp_path / "short.bin").exists()
    cl_ledger = json.loads(ledger_p3.read_text(encoding="utf-8"))
    assert cl_ledger["entries"][-1]["bytes"] == 2  # noqa: PLR2004

    # 7. Directory at crop path is rejected by approve_overlays & verify
    monkeypatch.setattr(aln_mod, "EXPECTED_TOTAL_PAGES", 1)
    monkeypatch.setattr(
        aln_mod, "_validate_manifest_inventory", lambda _m: None
    )
    geom_sha = aln_mod._compute_geometry_sha256(bad_manifest["pieces"])  # noqa: SLF001
    masks_sha = aln_mod._compute_masks_sha256(bad_manifest["pieces"])  # noqa: SLF001
    bad_manifest["geometry_sha256"] = geom_sha
    bad_manifest["open_reading_masks_sha256"] = masks_sha
    bad_manifest["total_printed_systems"] = 1
    (run_d / "alignment" / "manifest.json").write_text(
        json.dumps(bad_manifest), encoding="utf-8"
    )
    (run_d / "crops" / "facsimile" / "gold-01-s01.png").mkdir(parents=True)
    with pytest.raises(StopConditionError, match="non-empty"):
        approve_overlays_and_export_crops(run_d, "   ", ov_sha)
    with pytest.raises(StopConditionError, match="attested_overlays_sha256"):
        approve_overlays_and_export_crops(run_d, "qa", "   ")
    with pytest.raises(StopConditionError, match="overlays-sha256"):
        approve_overlays_and_export_crops(run_d, "qa", "wrong")
    with pytest.raises(StopConditionError, match="non-file destination"):
        approve_overlays_and_export_crops(run_d, "qa", ov_sha)
    assert not (run_d / "alignment" / "overlay-qa.json").exists()

    bad_manifest["pieces"][0]["systems"][0]["box_trust"] = "verified"  # type: ignore[index]
    (run_d / "alignment" / "manifest.json").write_text(
        json.dumps(bad_manifest), encoding="utf-8"
    )
    (run_d / "alignment" / "overlay-qa.json").write_text(
        json.dumps(
            {
                "pages_sha256": pg_sha,
                "overlays_sha256": ov_sha,
                "attested_overlays_sha256": ov_sha,
                "reviewer_note": "qa",
                "geometry_sha256": geom_sha,
                "open_reading_masks_sha256": masks_sha,
                "approved_pages": 92,
                "crop_export_completed": True,
                "pages": {f"p{i}": "approved" for i in range(91)}
                | {"gold-01-p01": "approved"},
            }
        ),
        encoding="utf-8",
    )
    with pytest.raises(StopConditionError, match="incomplete crop export"):
        aln_mod.verify_approved_overlays(run_d)

    # 8. Local open-reading masks schema + PAX archive commit check
    masks_file = tmp_path / "open-masks.json"
    masks_file.write_text(
        json.dumps(
            {
                "schema": "omr-open-reading-masks-v1",
                "pieces": {
                    "gold-01": [
                        {
                            "measure_ordinal": 2,
                            "categories": ["pitch", "accidental"],
                            "basis": "local archive note",
                        }
                    ]
                },
            }
        ),
        encoding="utf-8",
    )
    loaded_m, loaded_sha = aln_mod.load_open_reading_masks(
        masks_file, {"gold-01": 4, "gold-02": 3}
    )
    assert loaded_m["gold-01"][0]["categories"] == ["accidental", "pitch"]
    assert len(loaded_sha) == 64  # noqa: PLR2004
