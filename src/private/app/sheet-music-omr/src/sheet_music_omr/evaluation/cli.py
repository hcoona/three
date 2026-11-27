"""CLI and S0 orchestration for W2 evaluation and W3 real-data alignment."""

from __future__ import annotations

import argparse
import contextlib
import importlib.metadata
import json
import os
import sys
from collections import Counter
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any

from sheet_music_omr.comparison import (
    CONTENT_CATEGORIES,
    ProtocolDefectError,
    ScoreParseError,
)
from sheet_music_omr.evaluation.alignment import (
    approve_overlays_and_export_crops,
    atomic_write_text,
    build_alignment_and_overlays,
    check_frozen_run_masks,
    check_no_symlink_under,
    extract_t01_archive_join,
    sha256_bytes,
    sha256_file,
    verify_approved_overlays,
)
from sheet_music_omr.evaluation.copista import (
    COPISTA_HF_REVISION,
    COPISTERIA_COMMIT,
    S0_TEST_TOUCH_CAP,
    claim_or_append_s0_touches,
    copista_provenance_sha256,
    run_copista_inference_and_assemble,
    setup_copista_environment,
)
from sheet_music_omr.evaluation.scorer import (
    PredictionScore,
    _validate_score_slur_durations,
    score_prediction,
)
from sheet_music_omr.evaluation.stats import (
    ASPECT_NAMES,
    COPISTA_EXCLUDED_CATEGORIES,
    LAYER1_CATEGORIES,
    LAYER2_CATEGORIES,
    W1_TO_ASPECT,
    BlindDraftMeasure,
    PieceMeasureTable,
    apply_platt,
    aspect_rate,
    calibration_metrics,
    category_rate,
    cluster_bootstrap_paired,
    expected_review_passes,
    fit_monotone_platt,
    fit_pass_model_from_t01,
    g2_recall_table,
    one_way_icc,
    overall_rate,
    rank_second_reader_flags,
)
from sheet_music_omr.evaluation.xml_merge import (
    CandidateNormalizationError,
    merge_two_part_grand_staff,
    validate_candidate_score,
)
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.privacy import private_strings, scan
from sheet_music_omr.harness.records import (
    check_outside_repository,
    load_run_record,
    record_stop,
    run_git,
    source_state,
    write_json,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.worker import STEP_SECONDS, WorkerPool
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
)
from sheet_music_omr.musicxml_reverse import (
    _load as _load_musicxml,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

type JsonDict = dict[str, Any]
type JsonMap = Mapping[str, Any]

STOP_STATUS = 2

DISCREPANCY_SCHEMA: dict[str, object] = {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "title": "omr-discrepancy-log-v1",
    "type": "object",
    "additionalProperties": False,
    "required": [
        "schema",
        "run_id",
        "reader_id",
        "split",
        "gold_id",
        "group",
        "page_index",
        "system_index",
        "measure_ordinal",
        "draft_measure_ordinal",
        "insbar_anchor_draft_ordinal",
        "delbar_attributed_gold_ordinal",
        "parse_valid",
        "capability_excluded_categories",
        "open_reading_masked_categories",
        "converged_aspects",
        "layer0_categories",
        "layer1_a_categories",
        "layer2_invocab_categories",
        "layout_c_count",
        "raw_confidence",
        "calibrated_confidence",
        "second_reader_disagreement_count",
        "second_reader_score",
        "flagged_at_20pct",
        "archive_round_counts",
    ],
    "properties": {
        "schema": {"const": "omr-discrepancy-log-v1"},
        "run_id": {"type": "string"},
        "reader_id": {"type": "string"},
        "split": {"enum": ["TEST", "TRAIN_DEV"]},
        "gold_id": {"type": "string"},
        "group": {"type": "string"},
        "page_index": {"type": ["integer", "null"], "minimum": 1},
        "system_index": {"type": ["integer", "null"], "minimum": 1},
        "measure_ordinal": {"type": ["integer", "null"], "minimum": 1},
        "draft_measure_ordinal": {"type": ["integer", "null"], "minimum": 1},
        "insbar_anchor_draft_ordinal": {
            "type": ["integer", "null"],
            "minimum": 1,
        },
        "delbar_attributed_gold_ordinal": {
            "type": ["integer", "null"],
            "minimum": 1,
        },
        "parse_valid": {"type": "boolean"},
        "capability_excluded_categories": {
            "type": "array",
            "items": {"type": "string"},
        },
        "open_reading_masked_categories": {
            "type": "array",
            "items": {"type": "string"},
        },
        "converged_aspects": {"type": "array", "items": {"type": "string"}},
        "layer0_categories": {
            "type": "object",
            "additionalProperties": {"type": "integer", "minimum": 1},
        },
        "layer1_a_categories": {
            "type": "object",
            "additionalProperties": {"type": "integer", "minimum": 1},
        },
        "layer2_invocab_categories": {
            "type": "object",
            "additionalProperties": {"type": "integer", "minimum": 1},
        },
        "layout_c_count": {"type": "integer", "minimum": 0},
        "raw_confidence": {"type": ["number", "null"]},
        "calibrated_confidence": {"type": ["number", "null"]},
        "second_reader_disagreement_count": {
            "type": ["integer", "null"],
            "minimum": 0,
        },
        "second_reader_score": {"type": ["number", "null"]},
        "flagged_at_20pct": {"type": ["boolean", "null"]},
        "archive_round_counts": {
            "type": "array",
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": [
                    "round",
                    "source",
                    "unlocated_piece_level",
                    "by_class_category",
                ],
                "properties": {
                    "round": {"type": ["integer", "null"], "minimum": 1},
                    "source": {"enum": ["t01_round", "req012_post_t01"]},
                    "unlocated_piece_level": {"type": "boolean"},
                    "by_class_category": {
                        "type": "object",
                        "additionalProperties": {
                            "type": "object",
                            "additionalProperties": {
                                "type": "integer",
                                "minimum": 1,
                            },
                        },
                    },
                },
            },
        },
    },
}


def _workspace_versions() -> dict[str, str]:
    pkgs = ("musicdiff", "music21", "converter21", "numpy", "pillow")
    out = {"python": sys.version.split()[0]}
    for p in pkgs:
        out[p] = importlib.metadata.version(p)
    return out


def _is_int(v: object) -> bool:
    return isinstance(v, int) and not isinstance(v, bool)


def _is_opt_pos_int(v: object) -> bool:
    return v is None or (_is_int(v) and int(str(v)) >= 1)


def _is_opt_nonneg_int(v: object) -> bool:
    return v is None or (_is_int(v) and int(str(v)) >= 0)


def _is_opt_num(v: object) -> bool:
    return v is None or (
        isinstance(v, (int, float)) and not isinstance(v, bool)
    )


def _is_str_list(v: object) -> bool:
    return isinstance(v, list) and all(isinstance(x, str) for x in v)


def _is_cat_counts(v: object) -> bool:
    return isinstance(v, dict) and all(
        isinstance(k, str) and _is_int(val) and int(str(val)) >= 1
        for k, val in v.items()
    )


def _is_archive_round_list(v: object) -> bool:
    if not isinstance(v, list):
        return False
    req_keys = {"round", "source", "unlocated_piece_level", "by_class_category"}
    for entry in v:
        if (
            not isinstance(entry, dict)
            or set(entry.keys()) != req_keys
            or not _is_opt_pos_int(entry["round"])
            or entry["source"] not in {"t01_round", "req012_post_t01"}
            or not isinstance(entry["unlocated_piece_level"], bool)
        ):
            return False
        bcc = entry["by_class_category"]
        if not isinstance(bcc, dict) or not all(
            isinstance(cls_k, str) and _is_cat_counts(cat_map)
            for cls_k, cat_map in bcc.items()
        ):
            return False
    return True


def _validate_discrepancy_row(  # noqa: C901
    row: Mapping[str, object],
) -> None:
    req = set(DISCREPANCY_SCHEMA["required"])  # type: ignore[arg-type]
    if set(row.keys()) != req or row.get("schema") != "omr-discrepancy-log-v1":
        msg = "invalid omr-discrepancy-log-v1 row keys or schema tag"
        raise StopConditionError(msg)
    if row["measure_ordinal"] is None and row["draft_measure_ordinal"] is None:
        msg = "row has both measure_ordinal and draft_measure_ordinal null"
        raise StopConditionError(msg)
    for k in ("run_id", "reader_id", "gold_id", "group"):
        if not isinstance(row[k], str) or not row[k]:
            msg = f"invalid string field {k} in discrepancy row"
            raise StopConditionError(msg)
    if row["split"] not in {"TEST", "TRAIN_DEV"} or not isinstance(
        row["parse_valid"], bool
    ):
        msg = "invalid split or parse_valid in discrepancy row"
        raise StopConditionError(msg)
    for k in (
        "page_index",
        "system_index",
        "measure_ordinal",
        "draft_measure_ordinal",
        "insbar_anchor_draft_ordinal",
        "delbar_attributed_gold_ordinal",
    ):
        if not _is_opt_pos_int(row[k]):
            msg = f"invalid optional positive int {k}={row[k]!r}"
            raise StopConditionError(msg)
    for k in (
        "capability_excluded_categories",
        "open_reading_masked_categories",
        "converged_aspects",
    ):
        if not _is_str_list(row[k]):
            msg = f"invalid string list {k} in discrepancy row"
            raise StopConditionError(msg)
    for k in (
        "layer0_categories",
        "layer1_a_categories",
        "layer2_invocab_categories",
    ):
        if not _is_cat_counts(row[k]):
            msg = f"invalid category count dict {k} in discrepancy row"
            raise StopConditionError(msg)
    if (
        not _is_int(row["layout_c_count"])
        or int(str(row["layout_c_count"])) < 0
        or not _is_opt_num(row["raw_confidence"])
        or not _is_opt_num(row["calibrated_confidence"])
        or not _is_opt_nonneg_int(row["second_reader_disagreement_count"])
        or not _is_opt_num(row["second_reader_score"])
        or (
            row["flagged_at_20pct"] is not None
            and not isinstance(row["flagged_at_20pct"], bool)
        )
        or not _is_archive_round_list(row["archive_round_counts"])
    ):
        msg = "invalid numeric/boolean/array value in discrepancy row"
        raise StopConditionError(msg)


def _piece_table_from_score(
    piece_meta: Mapping[str, object],
    score: PredictionScore,
    *,
    l1_converged_only: bool = False,
) -> PieceMeasureTable:
    m_cnt = int(piece_meta["measure_count"])  # type: ignore[arg-type]
    mask_map: dict[int, set[str]] = {m: set() for m in range(1, m_cnt + 1)}
    for entry in piece_meta.get("open_reading_masks", []):  # type: ignore[attr-defined]
        m_ord = int(entry["measure_ordinal"])
        mask_map[m_ord].update(str(c) for c in entry["categories"])

    if l1_converged_only:
        conv = set(piece_meta.get("converged_aspects", []))  # type: ignore[arg-type]
        non_conv_cats = {
            c for c in CONTENT_CATEGORIES if W1_TO_ASPECT[c] not in conv
        }
        for masks in mask_map.values():
            masks.update(non_conv_cats)

    unmasked_totals: Counter[str] = Counter()
    unmasked_ident: Counter[str] = Counter()
    for ms in score.measures:
        m_masks = mask_map[ms.measure_ordinal]
        for cat, tot in ms.element_totals.items():
            if cat not in m_masks:
                unmasked_totals[cat] += tot
                unmasked_ident[cat] += ms.element_identical_layer1.get(cat, 0)

    errs = tuple(frozenset(ms.layer1_counts.keys()) for ms in score.measures)
    masks = tuple(frozenset(mask_map[m]) for m in range(1, m_cnt + 1))
    pres = tuple(ms.present_categories for ms in score.measures)
    return PieceMeasureTable(
        gold_id=str(piece_meta["gold_id"]),
        book_cohort=str(piece_meta["book_cohort"]),
        measure_count=m_cnt,
        layer1_errors=errs,
        masked_categories=masks,
        present_categories=pres,
        element_totals=dict(unmasked_totals),
        element_identical=dict(unmasked_ident),
    )


REQ012_MANIFEST_SHA256 = (
    "968fcdac6c0ecf5c1f8d720f954a29bbf52ca88cf548d812b4f8b01f7c4b8f55"
)


def _load_and_verify_req012_manifest(  # noqa: C901
    req012_manifest_path: Path,
    measure_counts: Mapping[str, int],
    *,
    formal: bool,
) -> tuple[set[tuple[str, int, str]], dict[tuple[str, int], Counter[str]]]:
    """Verify canonical ``req012-private-manifest.json`` before TEST touch."""
    try:
        raw_bytes = req012_manifest_path.read_bytes()
    except OSError as exc:
        msg = f"invalid req012 manifest {req012_manifest_path.name}: {exc}"
        raise StopConditionError(msg) from exc
    if formal and sha256_bytes(raw_bytes) != REQ012_MANIFEST_SHA256:
        msg = "req012-private-manifest.json SHA-256 mismatch vs requirements.md"
        raise StopConditionError(msg)
    try:
        req012_raw = json.loads(raw_bytes.decode("utf-8"))
        req012_loci = req012_raw["set_b_full_archive_deduplicated"]["loci"]
    except (UnicodeDecodeError, ValueError, KeyError, TypeError) as exc:
        msg = f"invalid req012 manifest {req012_manifest_path.name}: {exc}"
        raise StopConditionError(msg) from exc
    if not isinstance(req012_loci, list) or len(req012_loci) != 326:  # noqa: PLR2004
        msg = "expected 326 confirmed-B loci in req012-private-manifest.json"
        raise StopConditionError(msg)

    span_triples: set[tuple[str, tuple[int, ...], str]] = set()
    req012_triples: set[tuple[str, int, str]] = set()
    req012_post_by_m: dict[tuple[str, int], Counter[str]] = {}
    for locus in req012_loci:
        if not isinstance(locus, dict):
            msg = "non-object locus in req012-private-manifest.json"
            raise StopConditionError(msg)
        gid = str(locus.get("gold_id", ""))
        w1_cat = str(locus.get("w1_category", ""))
        t01_cat = str(locus.get("t01_category") or w1_cat)
        rep = locus.get("replay_target")
        ords = (
            rep.get("target_measure_ordinals")
            if isinstance(rep, dict)
            else None
        )
        in_set_a = locus.get("in_set_a")
        if (
            gid not in measure_counts
            or w1_cat not in CONTENT_CATEGORIES
            or not isinstance(ords, list)
            or not ords
            or not isinstance(in_set_a, bool)
        ):
            msg = f"malformed req012 locus for {gid!r} / {w1_cat!r}"
            raise StopConditionError(msg)
        m_tuple: list[int] = []
        for m_ord in ords:
            if (
                not isinstance(m_ord, int)
                or isinstance(m_ord, bool)
                or not (1 <= m_ord <= measure_counts[gid])
            ):
                msg = f"invalid measure ordinal {m_ord!r} in req012 {gid}"
                raise StopConditionError(msg)
            m_tuple.append(m_ord)
            req012_triples.add((gid, m_ord, w1_cat))
            if not in_set_a:
                req012_post_by_m.setdefault((gid, m_ord), Counter())[
                    t01_cat
                ] += 1
        span_triples.add((gid, tuple(m_tuple), w1_cat))
    if len(span_triples) != 324:  # noqa: PLR2004
        msg = f"expected 324 unique req012 triples, got {len(span_triples)}"
        raise StopConditionError(msg)
    return req012_triples, req012_post_by_m


def _req012_filtered_layer1_rate(  # noqa: C901
    pieces_meta: Sequence[Mapping[str, object]],
    scores_by_gid: Mapping[str, PredictionScore],
    req012_triples: set[tuple[str, int, str]],
) -> dict[str, object]:
    """Diagnostic breakdown filtering 326 confirmed-B loci on build-01."""
    active = frozenset(LAYER1_CATEGORIES)
    out: dict[str, object] = {}
    for split_name in ("TEST", "TRAIN_DEV"):
        erred = 0
        denom = 0
        filtered_hits = 0
        for pm in pieces_meta:
            if pm["split"] != split_name:
                continue
            gid = str(pm["gold_id"])
            sc = scores_by_gid[gid]
            mask_map: dict[int, set[str]] = {
                m: set()
                for m in range(1, int(pm["measure_count"]) + 1)  # type: ignore[arg-type]
            }
            for entry in pm.get("open_reading_masks", []):  # type: ignore[attr-defined]
                mask_map[int(entry["measure_ordinal"])].update(
                    str(c) for c in entry["categories"]
                )
            for ms in sc.measures:
                m_ord = ms.measure_ordinal
                unmasked = active - mask_map[m_ord]
                if not unmasked:
                    continue
                denom += 1
                rem_cats = set()
                for c in ms.layer1_counts:
                    if c not in unmasked:
                        continue
                    if (gid, m_ord, c) in req012_triples:
                        filtered_hits += 1
                    else:
                        rem_cats.add(c)
                if rem_cats:
                    erred += 1
        out[split_name] = {
            "erred_measures": erred,
            "unmasked_measures": denom,
            "rate": round(erred / denom, 6) if denom > 0 else None,
            "filtered_b_measure_category_hits": filtered_hits,
        }
    return out


def _inspect_draft_independently(draft_xml: Path) -> tuple[bool, int]:
    """Validate and count ``draft_xml`` without consulting ``Y_gold``."""
    try:
        norm = merge_two_part_grand_staff(draft_xml)
        ann = validate_candidate_score(norm)
        _validate_score_slur_durations(ann)
        m_cnt = len(ann.xml.measure_lengths)
        return True, max(1, m_cnt)
    except (
        CandidateNormalizationError,
        ScoreParseError,
        ProtocolDefectError,
        MusicXmlError,
    ):
        try:
            root = _load_musicxml(draft_xml.read_bytes())
            part = root.find("part")
            cnt = len(part.findall("measure")) if part is not None else 1
            return False, max(1, cnt)
        except Exception:  # noqa: BLE001
            return False, 1


def _score_bounded(
    pool: WorkerPool,
    candidate_xml: Path | str | None,
    reference_xml: Path | str,
    *,
    context: str,
    forbidden_ref_ordinals: frozenset[int] = frozenset(),
) -> PredictionScore:
    outcome = pool.call(
        score_prediction,
        {
            "candidate_xml": candidate_xml,
            "reference_xml": reference_xml,
            "forbidden_ref_ordinals": forbidden_ref_ordinals,
        },
        context=context,
        timeout=STEP_SECONDS,
    )
    if outcome.ok and isinstance(outcome.value, PredictionScore):
        return outcome.value
    # Candidate timed out or crashed worker -> fail closed on candidate
    fallback = pool.call(
        score_prediction,
        {"candidate_xml": None, "reference_xml": reference_xml},
        context=f"{context}:fallback",
        timeout=STEP_SECONDS,
    )
    if fallback.ok and isinstance(fallback.value, PredictionScore):
        return fallback.value
    msg = f"fallback scoring failed for {context}: {fallback.message}"
    raise StopConditionError(msg)


def run_stage_s0_scoring(  # noqa: C901, PLR0912, PLR0913, PLR0915
    *,
    confinement: Confinement,
    immich_dir: Path,
    desk_study_dir: Path,
    req012_manifest_path: Path,
    command_argv: Sequence[str],
    formal: bool,
) -> JsonDict:
    """Execute Stage S0 scoring, calibration, pass-model fit and reporting."""
    from sheet_music_omr.evaluation import copista as cop_mod  # noqa: PLC0415

    if cop_mod._W2_S0_CAMPAIGN_RETIRED:  # noqa: SLF001
        msg = "Stage S0 scoring campaign (#1489/#1490) is retired"
        raise StopConditionError(msg)
    run_dir = confinement.run
    downloads_dir = confinement.downloads
    src_state = source_state(run_git)
    if formal and src_state.get("dirty"):
        msg = "project source tree has uncommitted changes during formal S0 run"
        raise StopConditionError(msg)

    prov_p = downloads_dir / "w2-copista-cache" / "provenance.json"
    if not prov_p.exists():
        msg = f"missing prerequisite {prov_p.name}"
        raise StopConditionError(msg)

    manifest, qa = verify_approved_overlays(run_dir)
    run_id = run_dir.name
    git_commit = str(src_state["commit"])
    tree_sha = str(src_state["tree"])
    if formal and manifest.get("producer_dirty") is not False:
        msg = "manifest produced on a dirty source tree"
        raise StopConditionError(msg)
    frozen_masks_sha = str(manifest["open_reading_masks_sha256"])
    frozen_file = run_dir / "alignment" / "frozen-open-reading-masks-sha256.txt"
    if not frozen_file.is_file():
        msg = "frozen-open-reading-masks-sha256.txt missing; run 'run-copista'"
        raise StopConditionError(msg)
    check_frozen_run_masks(run_dir, downloads_dir, frozen_masks_sha)
    prov = json.loads(prov_p.read_text(encoding="utf-8"))
    expected_prov_sha = copista_provenance_sha256(prov)
    pieces_meta: list[JsonDict] = manifest["pieces"]
    preds_dir = run_dir / "preds" / "copista-28m"
    pages_dir = run_dir / "pages"
    logs_dir = run_dir / "logs"
    report_dir = run_dir / "report"
    for out_target in (
        run_dir / "tmp",
        logs_dir / "omr-discrepancy-log-v1.schema.json",
        logs_dir / "discrepancy-log.jsonl",
        report_dir / "s0-results.json",
        report_dir / "s0-public.json",
        report_dir / "s0-public.md",
        run_dir / "run.json",
    ):
        check_no_symlink_under(out_target, run_dir)

    # Stat-only existence preflight (ZERO TEST file/sidecar content reads!)
    if (
        not req012_manifest_path.is_file()
        or not (immich_dir / "batch-status.json").is_file()
    ):
        msg = "missing req012 manifest or batch-status.json"
        raise StopConditionError(msg)
    for pm in pieces_meta:
        gid = str(pm["gold_id"])
        check_no_symlink_under(preds_dir / f"{gid}.meta.json", run_dir)
        check_no_symlink_under(preds_dir / f"{gid}.musicxml", run_dir)
        if (
            not Path(str(pm["accepted_xml_path"])).is_file()
            or not Path(str(pm["initial_draft_path"])).is_file()
            or not (preds_dir / f"{gid}.meta.json").is_file()
        ):
            msg = f"missing required XML or prediction sidecar for {gid}"
            raise StopConditionError(msg)

    # Load & validate non-TEST archive metadata BEFORE appending TEST touches!
    measure_counts = {
        str(pm["gold_id"]): int(pm["measure_count"]) for pm in pieces_meta
    }  # type: ignore[arg-type]
    req012_triples, req012_post_by_m = _load_and_verify_req012_manifest(
        req012_manifest_path, measure_counts, formal=formal
    )
    try:
        status_raw = json.loads(
            (immich_dir / "batch-status.json").read_text(encoding="utf-8")
        )
        ws_by_num = {
            int(p["number"]): str(p["workspace"]) for p in status_raw["pieces"]
        }
    except (
        OSError,
        UnicodeDecodeError,
        ValueError,
        KeyError,
        TypeError,
    ) as exc:
        msg = f"invalid batch-status.json: {exc}"
        raise StopConditionError(msg) from exc
    t01_join = extract_t01_archive_join(
        desk_study_dir / "omr-finetune-research" / "reports",
        immich_dir,
        ws_by_num,
        measure_counts,
    )

    touch_log_p = downloads_dir / "w2-touch-log.jsonl"
    ledger_lock = downloads_dir / ".ledger.lock"
    n_test_pieces = sum(1 for pm in pieces_meta if pm["split"] == "TEST")

    # Claim verified run-copista touch & append agent-build-01 BEFORE TEST read!
    copista_reader_id = f"copista-28m@{COPISTA_HF_REVISION[:7]}"
    with confinement.lock(ledger_lock):
        t_count_a, t_count_b = claim_or_append_s0_touches(
            touch_log_path=touch_log_p,
            run_id=run_id,
            git_commit=git_commit,
            code_tree_sha256=tree_sha,
            copista_reader_id=copista_reader_id,
            copista_reader_sha256=str(prov["copisteria_archive_sha256"]),
            copista_weights_sha256=str(prov["combined_weights_sha256"]),
            piece_count=n_test_pieces,
            command=command_argv,
            open_reading_masks_sha256=frozen_masks_sha,
        )

    # Now read sidecars and verify gold, draft, and assembled copista digests!
    copista_meta_by_gid: dict[str, JsonDict] = {}
    for pm in pieces_meta:
        gid = str(pm["gold_id"])
        gp = Path(str(pm["accepted_xml_path"]))
        dp = Path(str(pm["initial_draft_path"]))
        if sha256_file(gp) != pm["accepted_xml_sha256"]:
            msg = f"{gid} accepted XML hash changed on disk"
            raise StopConditionError(msg)
        if sha256_file(dp) != pm["initial_draft_sha256"]:
            msg = f"{gid} initial draft hash changed on disk"
            raise StopConditionError(msg)

        meta_p = preds_dir / f"{gid}.meta.json"
        c_meta = json.loads(meta_p.read_text(encoding="utf-8"))
        if formal and c_meta.get("producer_dirty") is not False:
            msg = f"{gid} prediction produced on a dirty source tree"
            raise StopConditionError(msg)
        piece_pg_shas = []
        for pg in pm["pages"]:
            stem = f"{gid}-p{int(pg['page_index']):02d}"
            piece_pg_shas.append(
                f"{stem}:{sha256_file(pages_dir / f'{stem}.png')}"
            )
        exp_piece_pg_sha = sha256_bytes(
            "\n".join(piece_pg_shas).encode("utf-8")
        )
        if (
            c_meta.get("piece_pages_sha256") != exp_piece_pg_sha
            or c_meta.get("provenance_sha256") != expected_prov_sha
            or c_meta.get("open_reading_masks_sha256")
            != manifest["open_reading_masks_sha256"]
        ):
            msg = (
                f"stale assembled prediction or frozen masks changed for "
                f"{gid}; re-run 'run-copista'"
            )
            raise StopConditionError(msg)
        c_xml_p = preds_dir / f"{gid}.musicxml"
        if c_meta.get("parse_valid") and (
            not c_xml_p.is_file()
            or sha256_file(c_xml_p) != c_meta.get("assembled_xml_sha256")
        ):
            msg = f"{gid}.musicxml missing or hash mismatch vs meta.json"
            raise StopConditionError(msg)
        copista_meta_by_gid[gid] = c_meta

    tmp_dir = run_dir / "tmp"
    tmp_dir.mkdir(parents=True, exist_ok=True)
    draft_scores: dict[str, PredictionScore] = {}
    copista_scores: dict[str, PredictionScore] = {}
    blind_scores: dict[str, PredictionScore] = {}
    blind_draft_info: dict[str, tuple[bool, int]] = {}

    pool = WorkerPool(temp=tmp_dir, jobs=1)
    try:
        # Phase 1: Blind Second-Reader Scoring & Flags BEFORE touching Y_gold
        for pm in pieces_meta:
            gid = str(pm["gold_id"])
            draft_xml = Path(str(pm["initial_draft_path"]))
            d_out = pool.call(
                _inspect_draft_independently,
                {"draft_xml": draft_xml},
                context=f"{gid}:inspect-draft",
                timeout=STEP_SECONDS,
            )
            d_valid, d_mcnt = (
                d_out.value  # type: ignore[misc]
                if (d_out.ok and isinstance(d_out.value, tuple))
                else (False, 1)
            )
            blind_draft_info[gid] = (bool(d_valid), int(d_mcnt))
            c_meta = copista_meta_by_gid[gid]
            c_xml_p = preds_dir / f"{gid}.musicxml"
            c_xml: Path | None = (
                c_xml_p
                if (c_meta["parse_valid"] and c_xml_p.exists())
                else None
            )
            failed_pgs = {int(x) for x in c_meta.get("failed_pages", [])}
            if d_valid and not failed_pgs and c_xml is not None:
                blind_scores[gid] = _score_bounded(
                    pool, c_xml, draft_xml, context=f"{gid}:blind-sr"
                )

        blind_draft_by_split: dict[str, list[BlindDraftMeasure]] = {
            "TEST": [],
            "TRAIN_DEV": [],
        }
        blind_lookup: dict[tuple[str, int], BlindDraftMeasure] = {}
        for pm in pieces_meta:
            gid = str(pm["gold_id"])
            split_name = str(pm["split"])
            d_valid, m_draft_cnt = blind_draft_info[gid]
            c_meta = copista_meta_by_gid[gid]
            failed_pgs = c_meta.get("failed_pages", [])
            p_confs = [
                float(x) for x in c_meta.get("pred_measure_raw_conf", [])
            ]

            if (
                not d_valid
                or failed_pgs
                or gid not in blind_scores
                or not blind_scores[gid].parse_valid
            ):
                for m_d in range(1, m_draft_cnt + 1):
                    bm = BlindDraftMeasure(gid, m_d, delta=1, uncertainty=1.0)
                    blind_draft_by_split[split_name].append(bm)
                    blind_lookup[(gid, m_d)] = bm
            else:
                b_sc = blind_scores[gid]
                for ms in b_sc.measures:
                    m_d = ms.measure_ordinal
                    delta = sum(ms.layer2_counts.values())
                    c_ord = ms.aligned_cand_ordinal
                    u_val = (
                        round(1.0 - p_confs[c_ord - 1], 6)
                        if (c_ord is not None and 1 <= c_ord <= len(p_confs))
                        else 1.0
                    )
                    bm = BlindDraftMeasure(
                        gid, m_d, delta=delta, uncertainty=u_val
                    )
                    blind_draft_by_split[split_name].append(bm)
                    blind_lookup[(gid, m_d)] = bm

        flags_by_split = {
            sp: rank_second_reader_flags(ms_list)
            for sp, ms_list in blind_draft_by_split.items()
        }

        # Phase 2: Score Arm S0-A (D_piece vs Y_gold) & S0-B (P_piece vs Y_gold)
        for pm in pieces_meta:
            gid = str(pm["gold_id"])
            gold_xml = Path(str(pm["accepted_xml_path"]))
            draft_xml = Path(str(pm["initial_draft_path"]))
            c_meta = copista_meta_by_gid[gid]
            c_xml_p = preds_dir / f"{gid}.musicxml"
            c_xml = (
                c_xml_p
                if (c_meta["parse_valid"] and c_xml_p.exists())
                else None
            )
            failed_pgs = {int(x) for x in c_meta.get("failed_pages", [])}
            forb_ords: set[int] = set()
            for pg_rec in pm["pages"]:
                if int(pg_rec["page_index"]) in failed_pgs:
                    fo, lo = pg_rec["ordinal_range"]
                    forb_ords.update(range(int(fo), int(lo) + 1))

            draft_scores[gid] = _score_bounded(
                pool, draft_xml, gold_xml, context=f"{gid}:draft"
            )
            copista_scores[gid] = _score_bounded(
                pool,
                c_xml,
                gold_xml,
                context=f"{gid}:copista",
                forbidden_ref_ordinals=frozenset(forb_ords),
            )
    finally:
        pool.close()

    # Fit non-TEST Platt calibration on TRAIN_DEV (leave-one-piece-out + full)
    l2_set = frozenset(LAYER2_CATEGORIES)
    train_gids = [
        str(pm["gold_id"]) for pm in pieces_meta if pm["split"] == "TRAIN_DEV"
    ]
    test_gids = [
        str(pm["gold_id"]) for pm in pieces_meta if pm["split"] == "TEST"
    ]

    raw_conf_by_gm: dict[tuple[str, int], float] = {}
    l2_err_by_gm: dict[tuple[str, int], bool] = {}
    for pm in pieces_meta:
        gid = str(pm["gold_id"])
        c_sc = copista_scores[gid]
        c_meta = copista_meta_by_gid[gid]
        p_confs = [float(x) for x in c_meta.get("pred_measure_raw_conf", [])]
        mask_map: dict[int, set[str]] = {
            m: set() for m in range(1, int(pm["measure_count"]) + 1)
        }
        for entry in pm.get("open_reading_masks", []):
            mask_map[int(entry["measure_ordinal"])].update(
                str(c) for c in entry["categories"]
            )
        for ms in c_sc.measures:
            m_ord = ms.measure_ordinal
            c_ord = ms.aligned_cand_ordinal
            rc = (
                p_confs[c_ord - 1]
                if (
                    c_sc.parse_valid
                    and c_ord is not None
                    and 1 <= c_ord <= len(p_confs)
                )
                else 0.0
            )
            raw_conf_by_gm[(gid, m_ord)] = rc
            unmasked_l2 = l2_set - mask_map[m_ord]
            has_l2_err = bool(set(ms.layer2_counts.keys()) & unmasked_l2)
            l2_err_by_gm[(gid, m_ord)] = has_l2_err

    cal_conf_by_gm: dict[tuple[str, int], float] = {}
    # Out-of-fold LOPO for TRAIN_DEV
    for hold_gid in train_gids:
        fold_raws = [
            raw_conf_by_gm[(g, m)]
            for g in train_gids
            if g != hold_gid
            for m in range(1, measure_counts[g] + 1)
        ]
        fold_ok = [
            not l2_err_by_gm[(g, m)]
            for g in train_gids
            if g != hold_gid
            for m in range(1, measure_counts[g] + 1)
        ]
        f_params = fit_monotone_platt(fold_raws, fold_ok)
        for m in range(1, measure_counts[hold_gid] + 1):
            cal_conf_by_gm[(hold_gid, m)] = round(
                apply_platt(raw_conf_by_gm[(hold_gid, m)], f_params), 6
            )

    # Full TRAIN_DEV fit applied to TEST (0 TEST labels used!)
    all_tr_raws = [
        raw_conf_by_gm[(g, m)]
        for g in train_gids
        for m in range(1, measure_counts[g] + 1)
    ]
    all_tr_ok = [
        not l2_err_by_gm[(g, m)]
        for g in train_gids
        for m in range(1, measure_counts[g] + 1)
    ]
    full_platt_params = fit_monotone_platt(all_tr_raws, all_tr_ok)
    for g in test_gids:
        for m in range(1, measure_counts[g] + 1):
            cal_conf_by_gm[(g, m)] = round(
                apply_platt(raw_conf_by_gm[(g, m)], full_platt_params), 6
            )

    # Build PieceMeasureTables & Split Aggregates
    pm_by_gid = {str(pm["gold_id"]): pm for pm in pieces_meta}
    draft_tables = {
        gid: _piece_table_from_score(pm_by_gid[gid], draft_scores[gid])
        for gid in pm_by_gid
    }
    copista_tables = {
        gid: _piece_table_from_score(pm_by_gid[gid], copista_scores[gid])
        for gid in pm_by_gid
    }
    draft_l1conv_tables = {
        gid: _piece_table_from_score(
            pm_by_gid[gid], draft_scores[gid], l1_converged_only=True
        )
        for gid in train_gids
    }
    copista_l1conv_tables = {
        gid: _piece_table_from_score(
            pm_by_gid[gid], copista_scores[gid], l1_converged_only=True
        )
        for gid in train_gids
    }

    indep_draft_counts = {g: blind_draft_info[g][1] for g in blind_draft_info}
    split_summaries: dict[str, object] = {}
    for split_label, gids in (
        ("TEST", test_gids),
        (
            "TEST_BOOK1",
            [g for g in test_gids if pm_by_gid[g]["book_cohort"] == "book1"],
        ),
        (
            "TEST_BOOK2",
            [g for g in test_gids if pm_by_gid[g]["book_cohort"] == "book2"],
        ),
        ("TRAIN_DEV", train_gids),
    ):
        d_list = [draft_tables[g] for g in gids]
        c_list = [copista_tables[g] for g in gids]
        split_summaries[split_label] = _summarize_split(
            gids=gids,
            pm_by_gid=pm_by_gid,
            d_list=d_list,
            c_list=c_list,
            draft_scores=draft_scores,
            copista_scores=copista_scores,
            indep_draft_counts=indep_draft_counts,
            raw_conf_by_gm=raw_conf_by_gm,
            cal_conf_by_gm=cal_conf_by_gm,
            l2_err_by_gm=l2_err_by_gm,
            flags=(
                flags_by_split["TEST"]
                if split_label.startswith("TEST")
                else flags_by_split["TRAIN_DEV"]
            ),
            survivor_anchors={
                tuple(x)
                for x in t01_join["test_survivor_anchor_measures"]  # type: ignore[attr-defined]
            },
            survivor_spans={
                tuple(x)
                for x in t01_join["test_survivor_span_measures"]  # type: ignore[attr-defined]
            },
        )

    # Also record L1-converged-aspect-only overall rates on TRAIN_DEV
    split_summaries["TRAIN_DEV_L1_CONVERGED_ONLY"] = {
        "draft_layer1": overall_rate(list(draft_l1conv_tables.values()))[0],
        "copista_layer1": overall_rate(list(copista_l1conv_tables.values()))[0],
    }

    # Reconcile gold-01, 04, 06 Layer-0 raw changed measures vs T09#4 (64/99)
    t09_reconcile = {}
    for gid in ("gold-01", "gold-04", "gold-06"):
        sc = draft_scores[gid]
        l0_chg = sum(1 for ms in sc.measures if ms.layer0_counts)
        l1_chg = sum(1 for ms in sc.measures if ms.layer1_counts)
        t09_reconcile[gid] = {
            "measures": sc.ref_measure_count,
            "layer0_changed": l0_chg,
            "layer1_changed": l1_chg,
        }
    t09_reconcile["pooled_99"] = {
        "measures": sum(v["measures"] for v in t09_reconcile.values()),
        "layer0_changed": sum(
            v["layer0_changed"] for v in t09_reconcile.values()
        ),
        "layer1_changed": sum(
            v["layer1_changed"] for v in t09_reconcile.values()
        ),
    }

    # Review-pass model fit & projections
    pass_fit_ex_burst = fit_pass_model_from_t01(
        t01_join["round_a_ex_burst"],  # type: ignore[arg-type]
        t01_join["late_non_a_per_piece_round"],  # type: ignore[arg-type]
    )
    pass_fit_with_burst = fit_pass_model_from_t01(
        t01_join["round_a_all"],  # type: ignore[arg-type]
        t01_join["late_non_a_per_piece_round"],  # type: ignore[arg-type]
    )
    e_test_draft = split_summaries["TEST"]["overall_layer1"]["e_baseline"]  # type: ignore[index]
    e_test_cop = split_summaries["TEST"]["overall_layer1"]["e_candidate"]  # type: ignore[index]
    d_emp = pass_fit_ex_burst["d_hat"] or 0.6
    phi_emp = (
        pass_fit_ex_burst["phi_hat"]
        if pass_fit_ex_burst["phi_hat"] is not None
        else 0.5
    )
    pass_projections = {
        "t09_reference_params": {"d": 0.6, "phi": 0.5, "r_reg": 0.10},
        "empirical_t01_ex_burst": pass_fit_ex_burst,
        "empirical_t01_with_burst": pass_fit_with_burst,
        "projections_88m": {
            "agent_build01_test": {
                "e": e_test_draft,
                "passes_ref_linear": expected_review_passes(
                    e_test_draft, d=0.6, phi=0.5
                ),
                "passes_ref_occupancy": expected_review_passes(
                    e_test_draft, d=0.6, phi=0.5, occupancy=True
                ),
                "passes_emp_linear": expected_review_passes(
                    e_test_draft, d=d_emp, phi=phi_emp
                ),
                "passes_emp_occupancy": expected_review_passes(
                    e_test_draft, d=d_emp, phi=phi_emp, occupancy=True
                ),
            },
            "copista_28m_test": {
                "e": e_test_cop,
                "passes_ref_linear": expected_review_passes(
                    e_test_cop, d=0.6, phi=0.5
                ),
                "passes_ref_occupancy": expected_review_passes(
                    e_test_cop, d=0.6, phi=0.5, occupancy=True
                ),
                "passes_emp_linear": expected_review_passes(
                    e_test_cop, d=d_emp, phi=phi_emp
                ),
                "passes_emp_occupancy": expected_review_passes(
                    e_test_cop, d=d_emp, phi=phi_emp, occupancy=True
                ),
            },
        },
    }

    req012_aux = _req012_filtered_layer1_rate(
        pieces_meta, draft_scores, req012_triples
    )

    # Emit and validate RUN/logs/discrepancy-log.jsonl + schema atomically
    logs_dir.mkdir(parents=True, exist_ok=True)
    atomic_write_text(
        logs_dir / "omr-discrepancy-log-v1.schema.json",
        json.dumps(DISCREPANCY_SCHEMA, indent=2) + "\n",
    )
    log_rows = _build_discrepancy_log_rows(
        run_id=run_id,
        copista_reader_id=copista_reader_id,
        pieces_meta=pieces_meta,
        draft_scores=draft_scores,
        copista_scores=copista_scores,
        raw_conf_by_gm=raw_conf_by_gm,
        cal_conf_by_gm=cal_conf_by_gm,
        blind_lookup=blind_lookup,
        flags_by_split=flags_by_split,
        t01_join=t01_join,
        req012_post_by_m=req012_post_by_m,
    )
    jsonl_lines: list[str] = []
    for row in log_rows:
        _validate_discrepancy_row(row)
        jsonl_lines.append(json.dumps(row, separators=(",", ":")))
    atomic_write_text(
        logs_dir / "discrepancy-log.jsonl", "\n".join(jsonl_lines) + "\n"
    )

    # Post-run SHA-256 verification that private inputs remained untouched!
    for pm in pieces_meta:
        if (
            sha256_file(Path(str(pm["accepted_xml_path"])))
            != pm["accepted_xml_sha256"]
        ):
            msg = f"{pm['gold_id']} accepted XML mutated during run"
            raise StopConditionError(msg)
        if (
            sha256_file(Path(str(pm["initial_draft_path"])))
            != pm["initial_draft_sha256"]
        ):
            msg = f"{pm['gold_id']} initial draft mutated during run"
            raise StopConditionError(msg)

    ws_versions = _workspace_versions()
    masked_measure_count = sum(
        len(
            {
                int(e["measure_ordinal"])
                for e in pm.get("open_reading_masks", [])
            }
        )
        for pm in pieces_meta
    )
    results_payload = {
        "schema": "omr-w2-s0-results-v1",
        "run_id": run_id,
        "utc": datetime.now(UTC).isoformat(),
        "git_commit": git_commit,
        "code_tree_sha256": tree_sha,
        "dirty": bool(src_state.get("dirty")),
        "workspace_versions": ws_versions,
        "copista_provenance": {
            "copisteria_commit": COPISTERIA_COMMIT,
            "copisteria_archive_sha256": prov["copisteria_archive_sha256"],
            "copista_hf_revision": COPISTA_HF_REVISION,
            "weight_sha256": prov["weight_sha256"],
            "combined_weights_sha256": prov["combined_weights_sha256"],
            "venv_versions": prov["venv_versions"],
            "w2_cumulative_download_bytes": prov[
                "w2_cumulative_download_bytes"
            ],
        },
        "test_touches_recorded": {
            "agent-build-01": t_count_a,
            copista_reader_id: t_count_b,
            "cap": S0_TEST_TOUCH_CAP,
        },
        "alignment_summary": {
            "total_pieces": manifest["total_pieces"],
            "total_pages": manifest["total_pages"],
            "total_measures": manifest["total_measures"],
            "total_printed_systems": manifest["total_printed_systems"],
            "open_reading_masked_measures": masked_measure_count,
            "open_reading_masks_sha256": manifest["open_reading_masks_sha256"],
            "overlay_qa_approved_pages": qa["approved_pages"],
            "slice_roundtrip_zero_diff_pieces": manifest[
                "slice_roundtrip_verified"
            ],
        },
        "platt_full_train_dev_params": {
            "a": round(full_platt_params[0], 6),
            "b": round(full_platt_params[1], 6),
            "constant_fallback": full_platt_params[2],
        },
        "t09_99m_reconciliation": t09_reconcile,
        "req012_auxiliary_build01_breakdown": req012_aux,
        "t01_archive_join_summary": {
            "total_live_rows": t01_join["total_live_rows"],
            "located_rows": t01_join["located_rows"],
            "unlocated_rows": t01_join["unlocated_rows"],
            "round_a_ex_burst": t01_join["round_a_ex_burst"],
            "round_a_all": t01_join["round_a_all"],
            "test_survivor_anchor_measure_count": len(
                t01_join["test_survivor_anchor_measures"]  # type: ignore[arg-type]
            ),
            "test_survivor_span_measure_count": len(
                t01_join["test_survivor_span_measures"]  # type: ignore[arg-type]
            ),
        },
        "pass_model": pass_projections,
        "splits": split_summaries,
        "discrepancy_log_rows": len(log_rows),
    }

    # Build sanitized public JSON (multi-piece split aggregates ONLY: omit
    # per_piece, private mask digest, AND TEST_BOOK1/TEST_BOOK2!)
    public_splits: JsonDict = {}
    for sk in ("TEST", "TRAIN_DEV", "TRAIN_DEV_L1_CONVERGED_ONLY"):
        sv = split_summaries[sk]
        if isinstance(sv, dict):
            public_splits[sk] = {
                k: v for k, v in sv.items() if k != "per_piece"
            }
        else:
            public_splits[sk] = sv
    public_payload: JsonDict = {
        k: v
        for k, v in results_payload.items()
        if k
        not in {
            "run_id",
            "splits",
            "t09_99m_reconciliation",
            "alignment_summary",
        }
    }
    public_payload["alignment_summary"] = {
        k: v
        for k, v in results_payload["alignment_summary"].items()  # type: ignore[attr-defined]
        if k != "open_reading_masks_sha256"
    }
    public_payload["t09_99m_reconciliation"] = {
        "pooled_99": t09_reconcile["pooled_99"]
    }
    public_payload["splits"] = public_splits

    md_report = _render_markdown_report(public_payload)

    # Mandatory OMR-REQ-015 privacy scan derived from runtime inputs/env!
    dyn_tokens: set[str] = {
        *private_strings(confinement),
        *(str(ws) for ws in ws_by_num.values()),
        str(Path.home()),
    }
    if len(Path.home().name) >= 3:  # noqa: PLR2004
        dyn_tokens.add(Path.home().name)
    for env_k in ("USER", "USERNAME", "LOGNAME"):
        val = os.environ.get(env_k, "")
        if len(val) >= 3:  # noqa: PLR2004
            dyn_tokens.add(val)
    for p_in in (immich_dir, desk_study_dir, req012_manifest_path):
        dyn_tokens.add(str(p_in))
        if len(p_in.name) >= 4:  # noqa: PLR2004
            dyn_tokens.add(p_in.name)
    forbidden = sorted(t for t in dyn_tokens if t)
    local_json_str = json.dumps(results_payload, indent=2) + "\n"
    public_json_str = json.dumps(public_payload, indent=2) + "\n"
    scan(local_json_str, forbidden)
    scan(public_json_str, forbidden)
    scan(md_report, forbidden)

    report_dir.mkdir(parents=True, exist_ok=True)
    atomic_write_text(report_dir / "s0-results.json", local_json_str)
    atomic_write_text(report_dir / "s0-public.json", public_json_str)
    atomic_write_text(report_dir / "s0-public.md", md_report)

    run_rec = load_run_record(confinement)
    run_rec.update(
        {
            "schema": "omr-w2-run-v1",
            "status": "completed",
            "utc": results_payload["utc"],
            "git_commit": git_commit,
            "code_tree_sha256": tree_sha,
            "dirty": results_payload["dirty"],
            "workspace_versions": ws_versions,
            "copista_provenance": results_payload["copista_provenance"],
        }
    )
    write_json(confinement, run_dir / "run.json", run_rec)
    return results_payload


def _summarize_split(  # noqa: PLR0913
    *,
    gids: Sequence[str],
    pm_by_gid: Mapping[str, JsonMap],
    d_list: Sequence[PieceMeasureTable],
    c_list: Sequence[PieceMeasureTable],
    draft_scores: Mapping[str, PredictionScore],
    copista_scores: Mapping[str, PredictionScore],
    indep_draft_counts: Mapping[str, int],
    raw_conf_by_gm: Mapping[tuple[str, int], float],
    cal_conf_by_gm: Mapping[tuple[str, int], float],
    l2_err_by_gm: Mapping[tuple[str, int], bool],
    flags: Mapping[str, set[tuple[str, int]]],
    survivor_anchors: set[tuple[str, int]],
    survivor_spans: set[tuple[str, int]],
) -> JsonDict:
    overall_boot = cluster_bootstrap_paired(c_list, d_list, overall_rate)
    l2_boot = cluster_bootstrap_paired(
        c_list,
        d_list,
        lambda ps: overall_rate(
            ps, active_categories=frozenset(LAYER2_CATEGORIES)
        ),
    )

    # Layer 0 raw rate
    d_l0_err = sum(
        sum(1 for ms in draft_scores[g].measures if ms.layer0_counts)
        for g in gids
    )
    c_l0_err = sum(
        sum(1 for ms in copista_scores[g].measures if ms.layer0_counts)
        for g in gids
    )
    m_tot = sum(int(pm_by_gid[g]["measure_count"]) for g in gids)  # type: ignore[arg-type]

    aspects_boot = {
        asp: cluster_bootstrap_paired(
            c_list, d_list, lambda ps, a=asp: aspect_rate(ps, a)
        )
        for asp in ASPECT_NAMES
    }
    cats_boot: dict[str, object] = {}
    for cat in LAYER1_CATEGORIES:
        cb = cluster_bootstrap_paired(
            c_list, d_list, lambda ps, c=cat: category_rate(ps, c)
        )
        _, _, m_c, pres_c = category_rate(d_list, cat)
        cb["unmasked_measures"] = m_c
        cb["gold_present_measures"] = pres_c
        cats_boot[cat] = cb

    # Per-piece breakdown
    per_piece = {}
    for dp, cp in zip(d_list, c_list, strict=True):
        gid = dp.gold_id
        ed, err_d, den_d = overall_rate([dp])
        ec, err_c, _ = overall_rate([cp])
        ed2, _, _ = overall_rate(
            [dp], active_categories=frozenset(LAYER2_CATEGORIES)
        )
        ec2, _, _ = overall_rate(
            [cp], active_categories=frozenset(LAYER2_CATEGORIES)
        )
        per_piece[gid] = {
            "group": pm_by_gid[gid]["group"],
            "book_cohort": dp.book_cohort,
            "label_tier": pm_by_gid[gid]["label_tier"],
            "measures": dp.measure_count,
            "unmasked_measures": den_d,
            "draft_l0_erred": sum(
                1 for ms in draft_scores[gid].measures if ms.layer0_counts
            ),
            "draft_l1_erred": err_d,
            "draft_l1_rate": round(ed, 6) if ed is not None else None,
            "draft_l2_rate": round(ed2, 6) if ed2 is not None else None,
            "copista_parse_valid": copista_scores[gid].parse_valid,
            "copista_l0_erred": sum(
                1 for ms in copista_scores[gid].measures if ms.layer0_counts
            ),
            "copista_l1_erred": err_c,
            "copista_l1_rate": round(ec, 6) if ec is not None else None,
            "copista_l2_rate": round(ec2, 6) if ec2 is not None else None,
            "draft_d_norm": draft_scores[gid].d_norm_aligned,
            "copista_d_norm": copista_scores[gid].d_norm_aligned,
        }

    # Pooled musicdiff d_norm
    d_ed = sum(draft_scores[g].omr_ed_aligned for g in gids)
    d_den = sum(
        draft_scores[g].symbols_pred + draft_scores[g].symbols_gt for g in gids
    )
    c_ed = sum(copista_scores[g].omr_ed_aligned for g in gids)
    c_den = sum(
        copista_scores[g].symbols_pred + copista_scores[g].symbols_gt
        for g in gids
    )
    d_ed_w = sum(draft_scores[g].omr_ed_whole for g in gids)
    c_ed_w = sum(copista_scores[g].omr_ed_whole for g in gids)

    # Calibration on copista
    raw_seq = [
        raw_conf_by_gm[(g, m)]
        for g in gids
        for m in range(1, int(pm_by_gid[g]["measure_count"]) + 1)  # type: ignore[arg-type]
    ]
    cal_seq = [
        cal_conf_by_gm[(g, m)]
        for g in gids
        for m in range(1, int(pm_by_gid[g]["measure_count"]) + 1)  # type: ignore[arg-type]
    ]
    err_seq = [
        l2_err_by_gm[(g, m)]
        for g in gids
        for m in range(1, int(pm_by_gid[g]["measure_count"]) + 1)  # type: ignore[arg-type]
    ]

    # Post-flag Second-Reader Recall & c'
    sr_eval = _evaluate_second_reader_recall(
        gids=gids,
        d_list=d_list,
        c_list=c_list,
        draft_scores=draft_scores,
        indep_draft_counts=indep_draft_counts,
        flags=flags,
        survivor_anchors=survivor_anchors,
        survivor_spans=survivor_spans,
    )

    return {
        "piece_count": len(gids),
        "measure_count": m_tot,
        "layer0_raw": {
            "draft_erred": d_l0_err,
            "draft_rate": round(d_l0_err / m_tot, 6) if m_tot > 0 else None,
            "copista_erred": c_l0_err,
            "copista_rate": round(c_l0_err / m_tot, 6) if m_tot > 0 else None,
        },
        "overall_layer1": overall_boot,
        "overall_layer2_invocab": l2_boot,
        "icc": {
            "draft_layer1": one_way_icc(d_list),
            "copista_layer1": one_way_icc(c_list),
        },
        "musicdiff_d_norm": {
            "draft_aligned": round(d_ed / d_den, 6) if d_den > 0 else 0.0,
            "draft_whole": round(d_ed_w / d_den, 6) if d_den > 0 else 0.0,
            "copista_aligned": round(c_ed / c_den, 6) if c_den > 0 else 0.0,
            "copista_whole": round(c_ed_w / c_den, 6) if c_den > 0 else 0.0,
        },
        "g2_element_recall": g2_recall_table(c_list, d_list),
        "copista_calibration": {
            "raw_conf": calibration_metrics(raw_seq, err_seq),
            "calibrated_conf": calibration_metrics(cal_seq, err_seq),
        },
        "second_reader": sr_eval,
        "by_aspect": aspects_boot,
        "by_category": cats_boot,
        "per_piece": per_piece,
    }


def _evaluate_second_reader_recall(  # noqa: C901, PLR0912, PLR0913
    *,
    gids: Sequence[str],
    d_list: Sequence[PieceMeasureTable],
    c_list: Sequence[PieceMeasureTable],
    draft_scores: Mapping[str, PredictionScore],
    indep_draft_counts: Mapping[str, int],
    flags: Mapping[str, set[tuple[str, int]]],
    survivor_anchors: set[tuple[str, int]],
    survivor_spans: set[tuple[str, int]],
) -> dict[str, object]:
    gid_set = set(gids)
    l1_active = frozenset(LAYER1_CATEGORIES)
    l2_active = frozenset(LAYER2_CATEGORIES)

    # Conditional error correlation c' over unmasked (m_gold, category) pairs
    d_mc_total = 0
    both_mc_err = 0
    d_l2_mc_total = 0
    both_l2_mc_err = 0

    gold_l1_err_measures: set[tuple[str, int]] = set()
    gold_l2_err_measures: set[tuple[str, int]] = set()
    catch_triggers: dict[tuple[str, int], set[tuple[str, int]]] = {}

    for dp, cp in zip(d_list, c_list, strict=True):
        gid = dp.gold_id
        d_sc = draft_scores[gid]
        for m_idx, ms in enumerate(d_sc.measures):
            m_ord = m_idx + 1
            unmasked_l1 = l1_active - dp.masked_categories[m_idx]
            unmasked_l2 = l2_active - dp.masked_categories[m_idx]
            d_e1 = dp.layer1_errors[m_idx] & unmasked_l1
            c_e1 = cp.layer1_errors[m_idx] & unmasked_l1
            d_e2 = dp.layer1_errors[m_idx] & unmasked_l2
            c_e2 = cp.layer1_errors[m_idx] & unmasked_l2

            for cat in d_e1:
                d_mc_total += 1
                if cat in c_e1:
                    both_mc_err += 1
            for cat in d_e2:
                d_l2_mc_total += 1
                if cat in c_e2:
                    both_l2_mc_err += 1

            if d_e1:
                gold_l1_err_measures.add((gid, m_ord))
            if d_e2:
                gold_l2_err_measures.add((gid, m_ord))

            # Symmetric catch triggers from D_piece <-> Y_gold alignment
            trigs: set[tuple[str, int]] = set()
            if ms.aligned_cand_ordinal is not None:
                trigs.add((gid, ms.aligned_cand_ordinal))
            elif ms.insbar_anchor_cand_ordinal is not None:
                trigs.add((gid, ms.insbar_anchor_cand_ordinal))
            else:
                trigs.add((gid, 1))
            for del_c in ms.delbar_cand_ordinals:
                trigs.add((gid, del_c))
            catch_triggers[(gid, m_ord)] = trigs

    split_surv_a = {gm for gm in survivor_anchors if gm[0] in gid_set}
    split_surv_s = {gm for gm in survivor_spans if gm[0] in gid_set}
    total_draft_m = sum(indep_draft_counts[g] for g in gids)

    budgets_out: dict[str, object] = {}
    for b_name, f_set in flags.items():
        f_sub = {gm for gm in f_set if gm[0] in gid_set}

        def recall_of(
            target_set: set[tuple[str, int]],
            flagged: set[tuple[str, int]] = f_sub,
        ) -> float | None:
            if not target_set:
                return None
            caught = sum(
                1
                for gm in target_set
                if catch_triggers.get(gm, set()) & flagged
            )
            return round(caught / len(target_set), 6)

        budgets_out[b_name] = {
            "flagged_draft_measures": len(f_sub),
            "flagged_share": round(len(f_sub) / total_draft_m, 6)
            if total_draft_m > 0
            else 0.0,
            "recall_all_l1_errors": recall_of(gold_l1_err_measures),
            "recall_invocab_l2_errors": recall_of(gold_l2_err_measures),
            "recall_late_survivors_anchor23": recall_of(split_surv_a),
            "recall_late_survivors_span25": recall_of(split_surv_s),
        }

    return {
        "draft_l1_error_measures": len(gold_l1_err_measures),
        "draft_l2_error_measures": len(gold_l2_err_measures),
        "late_survivor_anchor_measures": len(split_surv_a),
        "late_survivor_span_measures": len(split_surv_s),
        "c_prime_all_l1": round(both_mc_err / d_mc_total, 6)
        if d_mc_total > 0
        else None,
        "c_prime_invocab_l2": round(both_l2_mc_err / d_l2_mc_total, 6)
        if d_l2_mc_total > 0
        else None,
        "budgets": budgets_out,
    }


def _build_discrepancy_log_rows(  # noqa: C901, PLR0913
    *,
    run_id: str,
    copista_reader_id: str,
    pieces_meta: Sequence[JsonMap],
    draft_scores: Mapping[str, PredictionScore],
    copista_scores: Mapping[str, PredictionScore],
    raw_conf_by_gm: Mapping[tuple[str, int], float],
    cal_conf_by_gm: Mapping[tuple[str, int], float],
    blind_lookup: Mapping[tuple[str, int], BlindDraftMeasure],
    flags_by_split: Mapping[str, Mapping[str, set[tuple[str, int]]]],
    t01_join: JsonMap,
    req012_post_by_m: Mapping[tuple[str, int], Counter[str]],
    candidate_excluded_categories: Sequence[str] | None = None,
) -> list[JsonDict]:
    rows: list[JsonDict] = []
    pm_rounds: Mapping[tuple[str, int, int], Mapping[str, Counter[str]]] = (
        t01_join["per_measure_rounds"]
    )
    unloc_rounds: Mapping[tuple[str, int], Mapping[str, Counter[str]]] = (
        t01_join["unlocated_rounds"]
    )

    for pm in pieces_meta:
        gid = str(pm["gold_id"])
        split_name = str(pm["split"])
        group_id = str(pm["group"])
        conv_aspects = list(pm["converged_aspects"])  # type: ignore[arg-type]
        m_cnt = int(pm["measure_count"])  # type: ignore[arg-type]

        # Map measure_ordinal -> (page_index, system_index)
        m_to_ps: dict[int, tuple[int, int]] = {}
        for sys_rec in pm["systems"]:  # type: ignore[attr-defined]
            fo, lo = sys_rec["ordinal_range"]
            for m_o in range(int(fo), int(lo) + 1):
                m_to_ps[m_o] = (
                    int(sys_rec["page_index"]),
                    int(sys_rec["system_index"]),
                )

        mask_map: dict[int, list[str]] = {m: [] for m in range(1, m_cnt + 1)}
        for entry in pm.get("open_reading_masks", []):  # type: ignore[attr-defined]
            m_o = int(entry["measure_ordinal"])
            mask_map[m_o] = sorted(
                set(mask_map[m_o]) | {str(c) for c in entry["categories"]}
            )

        d_sc = draft_scores[gid]
        c_sc = copista_scores[gid]
        f20 = flags_by_split[split_name]["at_20pct"]

        cand_excl = (
            sorted(COPISTA_EXCLUDED_CATEGORIES)
            if candidate_excluded_categories is None
            else sorted(candidate_excluded_categories)
        )
        for reader_id, r_sc, cap_excl in (
            ("agent-build-01", d_sc, []),
            (copista_reader_id, c_sc, cand_excl),
        ):
            is_cop = reader_id == copista_reader_id
            for m_idx, ms in enumerate(r_sc.measures):
                m_ord = m_idx + 1
                d_ms = d_sc.measures[m_idx]
                d_ord = d_ms.aligned_cand_ordinal
                ins_anchor = (
                    d_ms.insbar_anchor_cand_ordinal
                    if d_ord is None
                    and d_ms.insbar_anchor_cand_ordinal is not None
                    else (1 if d_ord is None else None)
                )
                pg_i, sy_i = m_to_ps[m_ord]

                arc_list = _measure_archive_rounds(
                    gid, m_ord, pm_rounds, unloc_rounds, req012_post_by_m
                )

                bm = (
                    blind_lookup.get((gid, d_ord))
                    if (is_cop and d_ord is not None)
                    else (
                        blind_lookup.get((gid, ins_anchor))
                        if (is_cop and ins_anchor is not None)
                        else None
                    )
                )
                flagged = None
                if is_cop:
                    trigs = set()
                    if d_ord is not None:
                        trigs.add((gid, d_ord))
                    elif ins_anchor is not None:
                        trigs.add((gid, ins_anchor))
                    for dc in d_ms.delbar_cand_ordinals:
                        trigs.add((gid, dc))
                    flagged = bool(trigs & f20)

                rows.append(
                    {
                        "schema": "omr-discrepancy-log-v1",
                        "run_id": run_id,
                        "reader_id": reader_id,
                        "split": split_name,
                        "gold_id": gid,
                        "group": group_id,
                        "page_index": pg_i,
                        "system_index": sy_i,
                        "measure_ordinal": m_ord,
                        "draft_measure_ordinal": d_ord,
                        "insbar_anchor_draft_ordinal": ins_anchor,
                        "delbar_attributed_gold_ordinal": None,
                        "parse_valid": r_sc.parse_valid,
                        "capability_excluded_categories": cap_excl,
                        "open_reading_masked_categories": mask_map[m_ord],
                        "converged_aspects": conv_aspects,
                        "layer0_categories": ms.layer0_counts,
                        "layer1_a_categories": ms.layer1_counts,
                        "layer2_invocab_categories": ms.layer2_counts,
                        "layout_c_count": ms.layout_c_count,
                        "raw_confidence": raw_conf_by_gm[(gid, m_ord)]
                        if is_cop
                        else None,
                        "calibrated_confidence": cal_conf_by_gm[(gid, m_ord)]
                        if is_cop
                        else None,
                        "second_reader_disagreement_count": bm.delta
                        if bm is not None
                        else None,
                        "second_reader_score": round(bm.score, 6)
                        if bm is not None
                        else None,
                        "flagged_at_20pct": flagged,
                        "archive_round_counts": arc_list,
                    }
                )

            # Extra unaligned delbar draft rows (from D_piece <-> Y_gold)
            for ex_bar in d_sc.extra_cand_bars:
                d_ord = ex_bar.cand_ordinal
                bm = blind_lookup.get((gid, d_ord)) if is_cop else None
                rows.append(
                    {
                        "schema": "omr-discrepancy-log-v1",
                        "run_id": run_id,
                        "reader_id": reader_id,
                        "split": split_name,
                        "gold_id": gid,
                        "group": group_id,
                        "page_index": None,
                        "system_index": None,
                        "measure_ordinal": None,
                        "draft_measure_ordinal": d_ord,
                        "insbar_anchor_draft_ordinal": None,
                        "delbar_attributed_gold_ordinal": (
                            ex_bar.attributed_ref_ordinal
                        ),
                        "parse_valid": r_sc.parse_valid,
                        "capability_excluded_categories": cap_excl,
                        "open_reading_masked_categories": [],
                        "converged_aspects": conv_aspects,
                        "layer0_categories": ex_bar.layer0_counts
                        if not is_cop
                        else {},
                        "layer1_a_categories": ex_bar.layer1_counts
                        if not is_cop
                        else {},
                        "layer2_invocab_categories": ex_bar.layer2_counts
                        if not is_cop
                        else {},
                        "layout_c_count": 0,
                        "raw_confidence": None,
                        "calibrated_confidence": None,
                        "second_reader_disagreement_count": bm.delta
                        if bm is not None
                        else None,
                        "second_reader_score": round(bm.score, 6)
                        if bm is not None
                        else None,
                        "flagged_at_20pct": ((gid, d_ord) in f20)
                        if is_cop
                        else None,
                        "archive_round_counts": [],
                    }
                )
    return rows


def _measure_archive_rounds(
    gid: str,
    m_ord: int,
    pm_rounds: Mapping[tuple[str, int, int], Mapping[str, Counter[str]]],
    unloc_rounds: Mapping[tuple[str, int], Mapping[str, Counter[str]]],
    req012_post_by_m: Mapping[tuple[str, int], Counter[str]],
) -> list[dict[str, object]]:
    out: list[dict[str, object]] = []
    rounds_seen = sorted(
        {r for (g, m, r) in pm_rounds if g == gid and m == m_ord}
    )
    for rnd in rounds_seen:
        by_cls = pm_rounds[(gid, m_ord, rnd)]
        out.append(
            {
                "round": rnd,
                "source": "t01_round",
                "unlocated_piece_level": False,
                "by_class_category": {
                    cls: dict(sorted(cnt.items()))
                    for cls, cnt in sorted(by_cls.items())
                },
            }
        )
    if m_ord == 1:
        unloc_r = sorted({r for (g, r) in unloc_rounds if g == gid})
        for rnd in unloc_r:
            by_cls = unloc_rounds[(gid, rnd)]
            out.append(
                {
                    "round": rnd,
                    "source": "t01_round",
                    "unlocated_piece_level": True,
                    "by_class_category": {
                        cls: dict(sorted(cnt.items()))
                        for cls, cnt in sorted(by_cls.items())
                    },
                }
            )
    post_b = req012_post_by_m.get((gid, m_ord))
    if post_b:
        out.append(
            {
                "round": None,
                "source": "req012_post_t01",
                "unlocated_piece_level": False,
                "by_class_category": {"B": dict(sorted(post_b.items()))},
            }
        )
    return out


def _render_markdown_report(res: JsonMap) -> str:
    cop = res["copista_provenance"]
    aln = res["alignment_summary"]
    splits = res["splits"]
    pm = res["pass_model"]
    t09 = res["t09_99m_reconciliation"]

    ws_v = json.dumps(res["workspace_versions"], sort_keys=True)
    venv_v = json.dumps(cop["venv_versions"], sort_keys=True)
    touches_v = json.dumps(res["test_touches_recorded"])
    req12_v = json.dumps(res["req012_auxiliary_build01_breakdown"])

    lines = [
        "# OMR W2 & W3 Stage S0 Baseline and Real-Data Alignment Report",
        "",
        "## 1. Provenance, Environment and Operational Bounds",
        "",
        (
            f"- **Git commit / code tree:** `{res['git_commit']}` / "
            f"`{res['code_tree_sha256']}` (`dirty={res['dirty']}`)"
        ),
        f"- **Workspace versions:** `{ws_v}`",
        (
            f"- **Isolated `copista-28m` provenance:** "
            f"`kobimusic/copisteria@{cop['copisteria_commit']}` "
            f"(archive SHA-256 `{cop['copisteria_archive_sha256']}`), "
            f"`kobimusic/copista-28m@{cop['copista_hf_revision']}` "
            f"(combined weight SHA-256 `{cop['combined_weights_sha256']}`), "
            f"venv `{venv_v}`"
        ),
        (
            f"- **W2/W3 download ledger:** "
            f"`{cop['w2_cumulative_download_bytes']:,}` / "
            "`10,000,000,000` bytes"
        ),
        f"- **`TEST` touch ledger (`w2-touch-log.jsonl`):** `{touches_v}`",
        (
            f"- **W3 alignment & slice round-trip (`omr-real-manifest-v1`):** "
            f"`{aln['total_pieces']}` pieces, `{aln['total_pages']}` pages "
            f"(`{aln['overlay_qa_approved_pages']} / 92` overlays approved), "
            f"`{aln['total_measures']}` measures, "
            f"`{aln['total_printed_systems']}` physical printed grand-staff "
            f"systems, `{aln['slice_roundtrip_zero_diff_pieces']} / 23` "
            "printed-system `Text Score 2` slice round-trips at `0` content "
            f"differences (`{aln['open_reading_masked_measures']}` "
            "open-reading masked measures)."
        ),
        "",
        "## 2. Headline Stage S0 Drafter Summary (`TEST` vs. `TRAIN/DEV`)",
        "",
        (
            "| Cohort | Pieces | Measures | `build-01` L0 Raw | "
            "`build-01` L1 (`A`-err) | `copista-28m` L1 (`A`-err) | "
            "Ratio `r` (`95%` CI) | `build-01` L2 (`22` cat) | "
            "`copista-28m` L2 (`22` cat) | `build-01` `d_norm` | "
            "`copista-28m` `d_norm` |"
        ),
        (
            "| --- | ---: | ---: | ---: | ---: | ---: | --- | "
            "---: | ---: | ---: | ---: |"
        ),
    ]
    for sk in ("TEST", "TRAIN_DEV"):
        sp = splits[sk]
        l0 = sp["layer0_raw"]
        l1 = sp["overall_layer1"]
        l2 = sp["overall_layer2_invocab"]
        dn = sp["musicdiff_d_norm"]
        ci_s = (
            f"{l1['ratio']} ({l1['ratio_ci95'][0]}..{l1['ratio_ci95'][1]})"
            if l1["ratio_ci95"]
            else f"{l1['ratio']} (n/a)"
        )
        lines.append(
            f"| `{sk}` | {sp['piece_count']} | {sp['measure_count']} | "
            f"{l0['draft_rate']} | {l1['e_baseline']} | {l1['e_candidate']} | "
            f"{ci_s} | {l2['e_baseline']} | {l2['e_candidate']} | "
            f"{dn['draft_aligned']} | {dn['copista_aligned']} |"
        )

    p99 = t09["pooled_99"]
    icc_te = splits["TEST"]["icc"]
    icc_tr = splits["TRAIN_DEV"]["icc"]
    lines.extend(
        [
            "",
            (
                "- **`T09#4` `99`-measure (`gold-01, 04, 06`) "
                "reconciliation:** "
                f"Layer 0 raw changed = `{p99['layer0_changed']} / "
                f"{p99['measures']}`; Layer 1 (`b_normalized` + masks) = "
                f"`{p99['layer1_changed']} / {p99['measures']}`."
            ),
            (
                "- **Auxiliary `OMR-REQ-012` `326`-locus (`324`-triple) "
                f"breakdown on `build-01`:** `{req12_v}`."
            ),
            (
                "- **Intraclass correlation (`ICC`, Layer 1):** `TEST` "
                f"(`build-01`: `{icc_te['draft_layer1']}`, `copista-28m`: "
                f"`{icc_te['copista_layer1']}`), `TRAIN_DEV` (`build-01`: "
                f"`{icc_tr['draft_layer1']}`, `copista-28m`: "
                f"`{icc_tr['copista_layer1']}`)."
            ),
            "",
            (
                "## 3. Blind Second-Reader Mode and Confidence Calibration "
                "(`copista-28m`)"
            ),
            "",
            (
                "| Split | Budget | Flagged Draft Measures | "
                "Recall (All L1 `A`-Err) | Recall (`in_vocab` L2 `A`-Err) | "
                "Recall (`r>=3` Survivors, Anchor `23`) | "
                "Recall (`r>=3` Survivors, Span `25`) | `c'` (All L1 / L2) |"
            ),
            "| --- | --- | --- | ---: | ---: | ---: | ---: | --- |",
        ]
    )
    for sk in ("TEST", "TRAIN_DEV"):
        sr = splits[sk]["second_reader"]
        cp_s = f"{sr['c_prime_all_l1']} / {sr['c_prime_invocab_l2']}"
        for b_k in ("at_10pct", "at_20pct", "unthresholded"):
            b = sr["budgets"][b_k]
            f_cnt = f"{b['flagged_draft_measures']} ({b['flagged_share']})"
            lines.append(
                f"| `{sk}` | `{b_k}` | {f_cnt} | "
                f"{b['recall_all_l1_errors']} | "
                f"{b['recall_invocab_l2_errors']} | "
                f"{b['recall_late_survivors_anchor23']} | "
                f"{b['recall_late_survivors_span25']} | {cp_s} |"
            )

    for sk in ("TEST", "TRAIN_DEV"):
        cal = splits[sk]["copista_calibration"]
        raw_s = json.dumps(cal["raw_conf"])
        cal_s = json.dumps(cal["calibrated_conf"])
        lines.append(
            f"- **`{sk}` confidence calibration (`copista-28m`):** "
            f"raw `{raw_s}`, `TRAIN/DEV`-Platt calibrated `{cal_s}`."
        )

    lines.extend(
        [
            "",
            "## 4. Per-Aspect and `G2` Element-Recall Breakdown on `TEST`",
            "",
            (
                "| Aspect | `build-01` `e_a` | `copista-28m` `e_a` | "
                "Ratio `r` (`95%` CI) |"
            ),
            "| --- | ---: | ---: | --- |",
        ]
    )
    for asp in ASPECT_NAMES:
        ab = splits["TEST"]["by_aspect"][asp]
        ci_s = (
            f"{ab['ratio']} ({ab['ratio_ci95'][0]}..{ab['ratio_ci95'][1]})"
            if ab["ratio_ci95"]
            else f"{ab['ratio']} (n/a)"
        )
        lines.append(
            f"| `{asp}` | {ab['e_baseline']} | {ab['e_candidate']} | {ci_s} |"
        )

    lines.extend(
        [
            "",
            (
                "| `G2` Category | Gold Elements (`TEST`) | "
                "`build-01` Recall | `copista-28m` Recall | Recall Drop | "
                "`<= 0.05`? |"
            ),
            "| --- | ---: | ---: | ---: | ---: | --- |",
        ]
    )
    for g2_k, g2_v in splits["TEST"]["g2_element_recall"].items():
        lines.append(
            f"| `{g2_k}` | {g2_v['elements']} | {g2_v['recall_baseline']} | "
            f"{g2_v['recall_candidate']} | {g2_v['recall_drop']} | "
            f"`{g2_v['passes_g2']}` |"
        )

    lines.extend(
        [
            "",
            (
                "## 5. Per-Category Layer-1 `A`-Error Rates on `TEST` "
                "(`26` Non-`beam` Categories)"
            ),
            "",
            (
                "| Category | Unmasked `M_c` | Gold-Present `M` | "
                "`build-01` `e_c` | `copista-28m` `e_c` | "
                "Ratio `r` (`95%` CI) |"
            ),
            "| --- | ---: | ---: | ---: | ---: | --- |",
        ]
    )
    for cat in LAYER1_CATEGORIES:
        cb = splits["TEST"]["by_category"][cat]
        ci_s = (
            f"{cb['ratio']} ({cb['ratio_ci95'][0]}..{cb['ratio_ci95'][1]})"
            if cb["ratio_ci95"]
            else f"{cb['ratio']} (n/a)"
        )
        lines.append(
            f"| `{cat}` | {cb['unmasked_measures']} | "
            f"{cb['gold_present_measures']} | {cb['e_baseline']} | "
            f"{cb['e_candidate']} | {ci_s} |"
        )

    emp_ex = json.dumps(pm["empirical_t01_ex_burst"])
    emp_wb = json.dumps(pm["empirical_t01_with_burst"])
    proj_s = json.dumps(pm["projections_88m"])
    lines.extend(
        [
            "",
            (
                "## 6. Review-Pass Model Re-Fit (`T01` Whole-Round Join) and "
                "`88`-Measure Projections"
            ),
            "",
            (
                "- **Empirical `T01` fit (ex-burst):** "
                f"`{emp_ex}` (with burst: `{emp_wb}`)."
            ),
            f"- **Expected review passes (`88`-measure piece):** `{proj_s}`.",
            (
                "- **Validated `omr-discrepancy-log-v1` records emitted:** "
                f"`{res['discrepancy_log_rows']}`."
            ),
            "",
        ]
    )
    return "\n".join(lines)


def _build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m sheet_music_omr.evaluation")
    sub = p.add_subparsers(dest="command", required=True)

    p_al = sub.add_parser("align")
    p_al.add_argument("--run-dir", type=Path, required=True)
    p_al.add_argument("--downloads", type=Path, required=True)
    p_al.add_argument("--gold-status", type=Path, required=True)
    p_al.add_argument("--gold-cache", type=Path, required=True)
    p_al.add_argument("--scans-dir", type=Path, required=True)
    p_al.add_argument("--immich-dir", type=Path, required=True)
    p_al.add_argument("--open-reading-masks", type=Path, required=True)
    p_al.add_argument("--formal", action="store_true")

    p_ap = sub.add_parser("approve-overlays")
    p_ap.add_argument("--run-dir", type=Path, required=True)
    p_ap.add_argument("--downloads", type=Path, required=True)
    p_ap.add_argument("--overlays-sha256", type=str, required=True)
    p_ap.add_argument("--note", type=str, required=True)

    p_su = sub.add_parser("setup-copista")
    p_su.add_argument("--run-dir", type=Path, required=True)
    p_su.add_argument("--downloads", type=Path, required=True)

    p_rc = sub.add_parser("run-copista")
    p_rc.add_argument("--run-dir", type=Path, required=True)
    p_rc.add_argument("--downloads", type=Path, required=True)
    p_rc.add_argument("--jobs", type=int, default=4)
    p_rc.add_argument("--device", type=str, default="cpu")
    p_rc.add_argument("--formal", action="store_true")

    p_s0 = sub.add_parser("score-s0")
    p_s0.add_argument("--run-dir", type=Path, required=True)
    p_s0.add_argument("--downloads", type=Path, required=True)
    p_s0.add_argument("--immich-dir", type=Path, required=True)
    p_s0.add_argument("--desk-study-dir", type=Path, required=True)
    p_s0.add_argument("--req012-manifest", type=Path, required=True)
    p_s0.add_argument("--formal", action="store_true")

    return p


def _prepare_confinement(args: argparse.Namespace) -> Confinement:
    if args.run_dir.is_symlink() or args.downloads.is_symlink():
        msg = "refusing symlink --run-dir or --downloads"
        raise StopConditionError(msg)
    ext_paths = [args.run_dir, args.downloads]
    if getattr(args, "open_reading_masks", None) is not None:
        ext_paths.append(args.open_reading_masks)
    check_outside_repository(*ext_paths)
    if args.run_dir.exists():
        run_res = args.run_dir.resolve()
        for sub_t in ("tmp", "run.json", ".run.lock"):
            check_no_symlink_under(run_res / sub_t, run_res)
    return Confinement.create(args.run_dir, args.downloads)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the W2/W3 evaluation CLI and return process exit code."""
    raw_argv = list(sys.argv[1:] if argv is None else argv)
    args = _build_parser().parse_args(raw_argv)
    try:
        conf = _prepare_confinement(args)
    except StopConditionError as exc:
        print(f"stop: {exc}", file=sys.stderr)  # noqa: T201
        return STOP_STATUS

    try:
        with conf.lock(conf.run / ".run.lock", blocking=False):
            if args.command == "align":
                m_out = build_alignment_and_overlays(
                    run_dir=conf.run,
                    downloads_dir=conf.downloads,
                    gold_status_path=args.gold_status.resolve(),
                    gold_cache_dir=args.gold_cache.resolve(),
                    scans_dir=args.scans_dir.resolve(),
                    immich_dir=args.immich_dir.resolve(),
                    open_reading_masks_path=args.open_reading_masks.resolve(),
                    formal=bool(args.formal),
                )
                res = {
                    "status": "aligned",
                    "total_pieces": m_out["total_pieces"],
                    "total_pages": m_out["total_pages"],
                    "total_measures": m_out["total_measures"],
                    "total_printed_systems": m_out["total_printed_systems"],
                    "overlays_sha256": m_out["overlays_sha256"],
                    "slice_roundtrip_verified": m_out[
                        "slice_roundtrip_verified"
                    ],
                }
            elif args.command == "approve-overlays":
                res = approve_overlays_and_export_crops(
                    conf.run,
                    args.note,
                    args.overlays_sha256,
                    conf.downloads,
                )
            elif args.command == "setup-copista":
                p_out = setup_copista_environment(conf)
                res = {
                    "status": "ready",
                    "copisteria_commit": p_out["copisteria_commit"],
                    "copisteria_archive_sha256": p_out[
                        "copisteria_archive_sha256"
                    ],
                    "combined_weights_sha256": p_out["combined_weights_sha256"],
                    "venv_versions": p_out["venv_versions"],
                    "w2_cumulative_download_bytes": p_out[
                        "w2_cumulative_download_bytes"
                    ],
                }
            elif args.command == "run-copista":
                res = run_copista_inference_and_assemble(
                    confinement=conf,
                    command_argv=[
                        "python",
                        "-m",
                        "sheet_music_omr.evaluation",
                        *raw_argv,
                    ],
                    workers=args.jobs,
                    device=args.device,
                    formal=bool(args.formal),
                )
            else:
                out = run_stage_s0_scoring(
                    confinement=conf,
                    immich_dir=args.immich_dir.resolve(),
                    desk_study_dir=args.desk_study_dir.resolve(),
                    req012_manifest_path=args.req012_manifest.resolve(),
                    command_argv=[
                        "python",
                        "-m",
                        "sheet_music_omr.evaluation",
                        *raw_argv,
                    ],
                    formal=bool(args.formal),
                )
                res = {
                    "status": "completed",
                    "test_layer1": out["splits"]["TEST"]["overall_layer1"],
                    "train_dev_layer1": out["splits"]["TRAIN_DEV"][
                        "overall_layer1"
                    ],
                    "discrepancy_log_rows": out["discrepancy_log_rows"],
                }
            print(json.dumps(res, indent=2))  # noqa: T201
            return 0
    except StopConditionError as exc:
        with contextlib.suppress(StopConditionError):
            check_no_symlink_under(conf.run / "run.json", conf.run)
            record_stop(conf, args.command, exc)
        print(f"stop: {exc}", file=sys.stderr)  # noqa: T201
        return STOP_STATUS
