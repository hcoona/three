"""Fixture-only tests for W4 renderer geometry, Tier R and W5 decisions."""

from __future__ import annotations

import json
from dataclasses import replace
from pathlib import Path
from unittest.mock import MagicMock, Mock

import numpy as np
import pytest
from PIL import Image, ImageDraw

from sheet_music_omr.assembly import AssemblyStatus
from sheet_music_omr.evaluation import w5_detectors
from sheet_music_omr.evaluation.w5_detectors import (
    PageMatchReport,
    detect_grand_staff_systems_blind,
    evaluate_no_train_decision,
    match_page_boxes,
    validate_candidate_system_assembly,
    verify_w3_snapshot_attestation,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import (
    AssemblyTestStatus,
    SystemStart,
    compare_assembly,
    split_score,
    system_starts,
)
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.parser import parse_document
from sheet_music_omr.synthetic.degrade import (
    apply_tier_r_degradation,
    compare_crop_statistics,
    compute_crop_statistics,
    crop_normalized_system,
)
from sheet_music_omr.synthetic.renderers import (
    RenderRejected,
    compare_mei_supplementary,
    encode_layout_breaks,
    parse_musescore_geometry,
    parse_verovio_svg_geometry,
    sample_layout_and_style,
)
from sheet_music_omr.writer import write_document

_FIXTURES = Path(__file__).parent / "fixtures"
_GOLD = (20, 20, 220, 120)
_TWO_GOLD = ((20, 20, 220, 120), (20, 155, 220, 255))
_SYSTEMS = ((40, 50, 560, 275), (40, 360, 560, 585))
_EXPECTED = ((1, 1), (1, 2), (2, 1))
_DARK_STAFF = 115


def test_half_width_is_truncated_at_iou_boundary():
    """IoU 0.50 alone does not cover the required oracle width."""
    report = match_page_boxes([(20, 20, 120, 120, 2)], [_GOLD])
    assert report.split_or_truncated_gold == 1
    assert report.exact_two_staff_match == 0
    assert report.missed_gold == report.spurious_fp == 0


def test_separated_treble_and_bass_are_one_split_gold():
    """Partial staff predictions remain associated, not spurious."""
    report = match_page_boxes(
        [(20, 26, 220, 56, 1), (20, 78, 220, 108, 1)], [_GOLD]
    )
    assert report.split_or_truncated_gold == 1
    assert report.spurious_fp == report.missed_gold == 0
    assert report.n_matched == 0


def test_merge_has_precedence_and_partition_is_exact():
    """One spanning pred taints two oracles even with a competing full match."""
    report = match_page_boxes([(20, 20, 220, 255, 4), (*_GOLD, 2)], _TWO_GOLD)
    assert report.merged_preds == 1
    assert report.merged_gold == 2  # noqa: PLR2004
    assert report.spurious_fp == 0
    assert report.n_gold == sum(
        (
            report.missed_gold,
            report.merged_gold,
            report.split_or_truncated_gold,
            report.exact_two_staff_match,
            report.exact_wrong_staff_count,
        )
    )


def test_wrong_staff_exact_match_still_checks_ink_clipping():
    """Only dark interior ink contributes to the clipping numerator."""
    gray = np.full((145, 250), 255, dtype=np.uint8)
    gray[50, 26:210] = 0
    report = match_page_boxes([(30, 20, 220, 120, 1)], [_GOLD], gray)
    assert report.exact_wrong_staff_count == report.n_matched == 1
    assert report.ink_clipped_matches == 1
    gray[50, 20:30] = 255
    gray[50, 21] = 0
    assert (
        match_page_boxes(
            [(30, 20, 220, 120, 1)], [_GOLD], gray
        ).ink_clipped_matches
        == 0
    )


def test_no_train_gate_requires_checked_eligible_train_dev():
    """Require eligibility and clipping evidence from TRAIN_DEV."""
    good = PageMatchReport(323, 323, 0, 0, 0, 323, 0, 0, 0, 0)
    name = "comb_dp_blind"
    assert not evaluate_no_train_decision({name: good}).recommend_training
    for row in (
        replace(good, ink_clipped_matches=None),
        replace(good, ink_clipped_matches=1),
        replace(good, merged_preds=4),
        replace(good, n_gold=434),
    ):
        assert evaluate_no_train_decision({name: row}).recommend_training
    assert evaluate_no_train_decision({"homr_cli": good}).recommend_training


def _draw_page():
    page = Image.new("L", (600, 700), 255)
    draw = ImageDraw.Draw(page)
    for _, top, _, _ in _SYSTEMS:
        for start in (top + 20, top + 132):
            for line in range(5):
                y = start + line * 14
                draw.line((55, y, 545, y), fill=0, width=2)
        draw.line((55, top + 20, 55, top + 188), fill=0, width=3)
    return page


def test_blind_comb_pairs_connected_staffs_without_an_oracle():
    """Pick only supported pairs and do not infer a system on a white page."""
    page = _draw_page()
    predictions = detect_grand_staff_systems_blind(page)
    assert [row[-1] for row in predictions] == [2, 2]
    assert predictions[0][3] < predictions[1][1]
    assert (
        detect_grand_staff_systems_blind(Image.new("L", page.size, 255)) == []
    )


def test_homr_position_rows_without_page_mapping_are_unscorable(monkeypatch):
    """Numeric normalized output cannot establish the input page frame."""
    position_file = Mock(spec=Path)
    position_file.is_file.return_value = True
    position_file.read_text.return_value = "1 0.5 0.5 0.8 0.1\n"
    scratch = MagicMock(spec=Path)
    scratch.__truediv__.return_value = position_file
    page = Path("illustrative-page.png")

    def run(command, **kwargs):
        assert command[1:] == [
            "{SCRATCH_PNG}",
            "--no-title",
            "--write-staff-positions",
        ]
        assert kwargs["input_page_png"] == page
        kwargs["on_before_cleanup"](scratch)
        return Mock(returncode=0)

    monkeypatch.setattr(
        w5_detectors, "verify_loopback_only_unshare", lambda: None
    )
    monkeypatch.setattr(w5_detectors, "run_offline_isolated_subprocess", run)
    result = w5_detectors.run_homr_cli_detector(
        [page], Path("downloads"), Path("scratch")
    )[page]
    assert result.unscorable_page_frame
    assert result.position_row_count == 1
    assert not result.boxes


def test_snapshot_binds_test_digests_without_reading_test_rasters(monkeypatch):
    """TRAIN_DEV checks test metadata, never the corresponding page bytes."""
    pieces = [
        {
            "gold_id": ident,
            "split": split,
            "pages": [
                {
                    "page_index": 1,
                    "page_png_sha256": f"{ident}-raw",
                    "overlay_sha256": f"{ident}-marked",
                }
            ],
            "systems": [],
        }
        for ident, split in (("dev", "TRAIN_DEV"), ("holdout", "TEST"))
    ]
    digests = {
        "pages_sha256": w5_detectors.sha256_bytes(
            b"dev-p01:dev-raw\nholdout-p01:holdout-raw"
        ),
        "overlays_sha256": w5_detectors.sha256_bytes(
            b"dev-p01:dev-marked\nholdout-p01:holdout-marked"
        ),
        "geometry_sha256": "fixture-geometry",
        "test_exclusion_sha256": "fixture-exclusion",
    }
    approval = {
        **digests,
        "pages": {"dev-p01": "approved", "holdout-p01": "approved"},
        "attested_overlays_sha256": digests["overlays_sha256"],
        "crop_export_completed": True,
    }
    payloads = {
        "manifest.json": json.dumps({**digests, "pieces": pieces}),
        "overlay-qa.json": json.dumps(approval),
    }
    hashes = {
        ("pages", "dev-p01.png"): "dev-raw",
        ("overlays", "dev-p01.png"): "dev-marked",
        ("splits", "test-exclusion.json"): "fixture-exclusion",
    }
    reads = []

    def hash_file(path):
        key = path.parent.name, path.name
        reads.append(key)
        return hashes[key]

    monkeypatch.setattr(Path, "read_text", lambda self, *_: payloads[self.name])
    monkeypatch.setattr(w5_detectors, "_W3_DIGESTS", digests)
    monkeypatch.setattr(
        w5_detectors, "_validate_manifest_inventory", lambda _: None
    )
    monkeypatch.setattr(
        w5_detectors, "_compute_geometry_sha256", lambda _: "fixture-geometry"
    )
    monkeypatch.setattr(w5_detectors, "sha256_file", hash_file)
    assert verify_w3_snapshot_attestation(Path("snapshot")) == digests
    assert set(reads) == hashes.keys()

    hashes["pages", "dev-p01.png"] = "changed"
    with pytest.raises(StopConditionError, match="has changed"):
        verify_w3_snapshot_attestation(Path("snapshot"))


def test_tier_r_clutter_preserves_labels_and_co_warped_ink():
    """Unlabeled marks change pixels but not normalized boxes or mapped ink."""
    page = _draw_page()
    measures = ((46, 50, 302, 275), (302, 50, 553, 275))
    clean = apply_tier_r_degradation(
        page, _SYSTEMS, measures, seed=19, enable_clutter=False
    )
    cluttered = apply_tier_r_degradation(
        page, _SYSTEMS, measures, seed=19, enable_clutter=True
    )
    assert clean.yolo_text.encode() == cluttered.yolo_text.encode()
    assert clean.image.tobytes() != cluttered.image.tobytes()
    assert clean.overlay_check.passed
    assert [row.split()[0] for row in clean.yolo_rows] == ["0", "0", "1", "1"]
    for row in clean.yolo_rows:
        assert all(0 <= float(value) <= 1 for value in row.split()[1:])
    yy, xx = np.nonzero(np.asarray(clean.image.convert("L")) < _DARK_STAFF)
    assert len(yy) > 0
    inside = np.zeros(xx.shape, dtype=bool)
    for x0, y0, x1, y1 in clean.system_boxes:
        inside |= (x0 <= xx) & (xx < x1) & (y0 <= yy) & (yy < y1)
    assert inside.all()


@pytest.mark.parametrize("target", [10.0, 14.0, 20.0])
def test_staff_crops_measure_and_report_target_spacing(target):
    """Report spacing from the pixels and compare real/synthetic intervals."""
    page = _draw_page()
    crop = crop_normalized_system(
        page, _SYSTEMS[0], 14.0, target_staff_space_px=target
    )
    stat = compute_crop_statistics(crop)
    assert stat.staff_space_px == pytest.approx(target, abs=0.5)
    assert stat.staff_line_thickness_px > 0
    assert stat.laplacian_blur_var > 0
    assert stat.p95_p05_contrast > 0
    same = compare_crop_statistics([stat], [stat])
    assert same["staff_space_px"].train_dev_median_in_synthetic_interval
    different = compare_crop_statistics(
        [stat], [replace(stat, staff_space_px=stat.staff_space_px + 2)]
    )
    assert not different[
        "staff_space_px"
    ].train_dev_median_in_synthetic_interval


@pytest.fixture
def split_fixture():
    """Use the self-authored study with spanners crossing its breaks."""
    whole = parse_document(
        (_FIXTURES / "assembly" / "study.expected.txt").read_text("utf-8")
    )
    assert isinstance(whole, AssembledScore)
    docs = split_score(
        whole,
        (SystemStart(0, 1, 1), SystemStart(2, 1, 2), SystemStart(5, 2, 1)),
    )
    return whole, docs, tuple(map(write_document, docs))


def test_candidate_system_assembly_matches_oracle_roundtrip(split_fixture):
    """Exact sequence, carry state, and round-trip content all survive."""
    whole, docs, texts = split_fixture
    report = validate_candidate_system_assembly(texts, _EXPECTED)
    assert report.status is AssemblyStatus.SUCCESS
    assert report.schema == "omr-assembly-validation-v1"
    assert report.observed_sequence == _EXPECTED
    assert not report.diagnostics
    assert compare_assembly(whole, docs).status is AssemblyTestStatus.EQUAL


@pytest.mark.parametrize(
    ("fault", "status", "code"),
    [
        ("missing", AssemblyStatus.FAILED, "system_sequence"),
        ("swapped", AssemblyStatus.FAILED, "system_sequence"),
        ("wrong_tuple", AssemblyStatus.FAILED, "system_sequence"),
        ("measure_gap", AssemblyStatus.FAILED, "semantic/measure_id"),
        ("carry_mismatch", AssemblyStatus.FAILED, "assembly/carry_in"),
        ("unclosed", AssemblyStatus.FAILED, "assembly/relation_open"),
        ("review", AssemblyStatus.NEEDS_PERSON, "review_marker"),
    ],
)
def test_candidate_system_assembly_rejects_integrity_faults(
    split_fixture, fault, status, code
):
    """Each corrupted input fails deterministically without score repair."""
    _, _, (a, b, c) = split_fixture
    fault_texts = {
        "missing": (a, c),
        "swapped": (b, a, c),
        "wrong_tuple": (a, b, c.replace("page: 2", "page: 3")),
        "measure_gap": (a, b, c.replace("m 6 ", "m 7 ")),
        "carry_mismatch": (
            a,
            b.replace('"event_index":1', '"event_index":2', 1),
            c,
        ),
        "unclosed": (a, b, c.replace("C3/2.", "C3/2.[rel=slur@r1:start]")),
        "review": (a, b, c.replace("C3/2.", "<?> C3/2.")),
    }[fault]
    report = validate_candidate_system_assembly(fault_texts, _EXPECTED)
    assert report.status is status
    assert code in {diagnostic.code for diagnostic in report.diagnostics}
    assert report == validate_candidate_system_assembly(fault_texts, _EXPECTED)


def test_verovio_cumulative_viewbox_and_page_margin_transform():
    """Map all nested viewports and affine operations into raster pixels."""
    lines = "".join(f'<path d="M 10,{y} L 150,{y}"/>' for y in range(10, 31, 5))
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" width="200" height="400"
        viewBox="0 0 100 200">
        <svg class="definition-scale" viewBox="0 0 500 1000">
          <g class="page-margin" transform="translate(25,40) scale(2)">
            <g class="system" id="s1" transform="matrix(1 0 0 1 5 7)">
              <g class="measure" id="omr-measure-1">
                <g class="staff">{lines}</g>
              </g>
            </g>
          </g>
        </svg>
    </svg>"""
    page = parse_verovio_svg_geometry(
        svg, expected_measure_ids=["omr-measure-1"]
    )
    (system,) = page.systems
    (measure,) = page.measures
    assert measure.box == pytest.approx((22, 29.6, 134, 45.6))
    assert system.staff_space_px == pytest.approx(4.0)
    assert (system.first_ordinal, system.last_ordinal) == (1, 1)
    with pytest.raises(RenderRejected, match="order"):
        parse_verovio_svg_geometry(svg, expected_measure_ids=["omr-measure-2"])


def test_mpos_duplicate_ids_and_wrong_system_spans_fail_independently():
    """Correct sidecar counts alone do not prove the printed system mapping."""
    xml = """<score-partwise><part id="P1"><measure number="1"/>
        <measure number="2"><print new-system="yes"/></measure>
        </part></score-partwise>"""
    a = '<element id="0" x="540" y="720" sx="6120" sy="2448" page="0"/>'
    b = '<element id="1" x="540" y="4440" sx="6120" sy="2448" page="0"/>'
    page = _draw_page()
    for second, error in (
        (b.replace('id="1"', 'id="0"'), "bijection"),
        (b.replace('y="4440"', 'y="720"'), "spans"),
    ):
        with pytest.raises(RenderRejected, match=error):
            parse_musescore_geometry(
                f"<score><elements>{a}{second}</elements></score>", xml, [page]
            )
    (result,) = parse_musescore_geometry(
        f"<score><elements>{a}{b}</elements></score>", xml, [page]
    )
    assert [s.first_ordinal for s in result.systems] == [1, 2]
    assert result.measures[0].box[0::2] == (45, 555)
    assert result.systems[0].box[3] == result.systems[1].box[1]


def test_sampled_layout_replaces_breaks_and_keeps_mixed_pedal_rendering():
    """All seven density groups are reachable and encoded layout round-trips."""
    catalog = [sample_layout_and_style(seed, 52) for seed in range(60)]
    assert {style.plate_group for style in catalog} == {
        "G1",
        "G2",
        "G3",
        "G4",
        "G5",
        "G6",
        "G7",
    }
    style = catalog[19]
    assert style == sample_layout_and_style(19, 52)
    assert style.verovio_options["pedalStyle"] == "auto"
    body = '<measure number="1"><print new-page="yes"/></measure>' * 52
    xml = f'<score-partwise><part id="P1">{body}</part></score-partwise>'
    assert (
        system_starts(encode_layout_breaks(xml, style.layout)) == style.layout
    )


def test_mei_pedal_forms_resolve_locally_before_score_default():
    """A surviving sign and line are distinguished without explicit form."""
    xml = """<score-partwise><part id="P1"><measure id="m1" number="1">
      <attributes><divisions>1</divisions></attributes>
      <direction><direction-type><pedal type="start" line="yes"/>
      </direction-type><staff>2</staff></direction>
      <direction><direction-type><pedal type="start" line="no"/>
      </direction-type><staff>1</staff></direction>
      <note id="n1"><rest/><duration>4</duration><voice>1</voice>
      <staff>1</staff>
      </note><backup><duration>4</duration></backup>
      <note id="n2"><rest/><duration>4</duration><voice>2</voice>
      <staff>2</staff>
      </note></measure></part></score-partwise>"""
    mei = """<mei xmlns="http://www.music-encoding.org/ns/mei">
      <score><scoreDef pedal.style="line"/><section><measure xml:id="m1">
      <staff n="1"><layer n="1"><mRest xml:id="n1"/></layer></staff>
      <staff n="2"><layer n="1"><mRest xml:id="n2"/></layer></staff>
      <pedal staff="2" tstamp="1" dir="down"/>
      <pedal staff="1" tstamp="1" dir="down" form="pedstar"/>
      </measure></section></score></mei>"""
    assert not compare_mei_supplementary(xml, mei)
    changed = compare_mei_supplementary(xml, mei.replace(' form="pedstar"', ""))
    assert set(changed) == {"pedal"}
    assert changed["pedal"] > 0


def test_clean_box_expansion_survival_and_verovio_drop_threshold():
    """Verify clean box expansion, quarter-ink survival and >5% drops."""
    from sheet_music_omr.synthetic.degrade import (  # noqa: PLC0415
        Displacement,
        verify_warped_overlay,
    )
    from sheet_music_omr.synthetic.renderers import (  # noqa: PLC0415
        PageGeometry,
        RenderedMeasure,
        RenderedSystem,
        RenderGate,
        RenderGateLedger,
        _expand_vertical,
    )

    clean = Image.new("L", (400, 360), 255)
    draw = ImageDraw.Draw(clean)
    # Page title far above system 1 (> 4 * sp) stays outside system 1.
    draw.rectangle((140, 12, 260, 22), fill=0)
    draw.rectangle((60, 110, 340, 210), outline=0, width=2)
    # Detached tempo above staff + left brace overhang are captured.
    draw.rectangle((80, 88, 110, 94), fill=0)
    draw.rectangle((46, 112, 54, 208), fill=0)
    geom = PageGeometry(
        400,
        360,
        (RenderedSystem(1, 1, 1, 1, (60.0, 110.0, 340.0, 210.0), 10.0),),
        (RenderedMeasure(1, (60.0, 110.0, 340.0, 210.0)),),
    )
    expanded = _expand_vertical(geom, clean).systems[0].box
    assert 22 < expanded[1] <= 88  # noqa: PLR2004
    assert expanded[0] <= 46  # noqa: PLR2004

    ident = Displacement(400, 360, 0.0, 0.0, 0.0, 0.0, 0.0)
    warped = (
        round(expanded[0]),
        round(expanded[1]),
        round(expanded[2]),
        round(expanded[3]),
    )
    assert verify_warped_overlay(
        clean, clean.convert("RGB"), [expanded], [warped], ident
    ).passed

    washed = Image.new("RGB", (400, 360), (255, 255, 255))
    washed.putpixel((60, 110), (0, 0, 0))
    missed_check = verify_warped_overlay(
        clean, washed, [expanded], [warped], ident
    )
    assert not missed_check.passed
    assert missed_check.missed_systems == 1

    ledger = RenderGateLedger("verovio")
    ledger.record(RenderGate("dropped", {"slur": 1}), {"slur", "pitch"})
    assert ledger.disabled_categories == {"slur"}


def test_musescore_four_side_pixel_scale_calibration(tmp_path, monkeypatch):
    """Pass nonzero-y 2-measure sidecars and reject >2px axis shifts."""
    from sheet_music_omr.synthetic import renderers as rend_mod  # noqa: PLC0415
    from sheet_music_omr.synthetic.renderers import (  # noqa: PLC0415
        calibrate_musescore_pixel_scale,
    )

    cal_im = Image.new("L", (400, 320), 255)
    cdraw = ImageDraw.Draw(cal_im)
    for origin in (100, 170):
        for ln in range(5):
            cdraw.line((50, origin + ln * 10, 350, origin + ln * 10), fill=0)
    for bx in (50, 200, 350):
        cdraw.line((bx, 100, bx, 210), fill=0, width=2)

    class _FakeMs:
        version = "4.8.0"
        executable_sha256 = "fixture-sha"

    def _write_fake(out_dir: Path, dy: float = 0.0, dx: float = 0.0) -> None:
        out_dir.mkdir(parents=True, exist_ok=True)
        cal_im.save(out_dir / "score-1.png", format="PNG")
        sy = (100.0 + dy) * 12.0
        sh = 110.0 * 12.0
        sx1 = (50.0 + dx) * 12.0
        mpos = (
            f'<score><elements><element id="0" x="{sx1}" y="{sy}" '
            f'sx="1800" sy="{sh}" page="0"/>'
            f'<element id="1" x="2400" y="{sy}" sx="1800" sy="{sh}" page="0"/>'
            "</elements></score>"
        )
        (out_dir / "score.mpos").write_text(mpos, encoding="utf-8")

    monkeypatch.setattr(
        rend_mod, "_musescore_job", lambda _m, out_d, *_a: _write_fake(out_d)
    )
    assert calibrate_musescore_pixel_scale(
        _FakeMs(),  # type: ignore[arg-type]
        tmp_path / "cal-ok",
    ) == ("4.8.0", "fixture-sha")
    for label, kwargs in (
        ("cal-bad-y", {"dy": 5.0}),
        ("cal-bad-x", {"dx": 5.0}),
    ):
        monkeypatch.setattr(
            rend_mod,
            "_musescore_job",
            lambda _m, out_d, *_a, kw=kwargs: _write_fake(out_d, **kw),
        )
        with pytest.raises(StopConditionError, match="four-side tolerance"):
            calibrate_musescore_pixel_scale(
                _FakeMs(),  # type: ignore[arg-type]
                tmp_path / label,
            )
