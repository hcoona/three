"""Replay verification of the frozen OMR-REQ-012 toolchain-class finding set.

Reads the private local manifest (verified against its frozen SHA-256 digest)
and a completed formal run's gold results read-only, verifies that every gold
piece SHA-256 matches the frozen run plan and the manifest, checks that each
of the 327 frozen ``(gold_id, measure, w1_category, mechanism)`` loci
reproduces with zero score-file edits and zero in-scope differences at its
target measures and category, scans the public output for private strings under
OMR-REQ-015, and writes ``report/req012-replay.json`` and
``report/req012-replay.md`` inside the confined run directory.
"""

from __future__ import annotations

import json
from collections import Counter
from pathlib import Path
from typing import TYPE_CHECKING, Final

from sheet_music_omr.harness.downloads import sha256_file
from sheet_music_omr.harness.privacy import GOLD_MAPPING, private_strings, scan
from sheet_music_omr.harness.records import load_run_record, read_object
from sheet_music_omr.harness.run import GOLD_ARM, file_directory
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.supplementary import CHECKS, compare_file

if TYPE_CHECKING:
    from collections.abc import Mapping

    from sheet_music_omr.harness.confine import Confinement

type Data = dict[str, object]

FROZEN_MANIFEST_SHA256: Final = (
    "968fcdac6c0ecf5c1f8d720f954a29bbf52ca88cf548d812b4f8b01f7c4b8f55"
)
"""SHA-256 of the attested local private OMR-REQ-012 manifest."""

FROZEN_LOCUS_COUNT: Final = 327
"""Deduplicated locus count of the owner-approved Set A union Set B."""

FROZEN_RAW_COUNT: Final = 356
"""Raw entry count of the owner-approved Set A union Set B."""


class Req012ReplayStopError(StopConditionError):
    """Raised when OMR-REQ-012 manifest or gold provenance fails."""


def _dict(value: object) -> Data:
    return value if isinstance(value, dict) else {}


def _verify_on_disk_gold(
    confinement: Confinement,
    run_hashes: Mapping[str, str],
) -> dict[str, Path]:
    mapping_path = confinement.run / GOLD_MAPPING
    if not mapping_path.is_file():
        msg = f"the gold mapping file is absent: {mapping_path}"
        raise Req012ReplayStopError(msg)
    gold_paths = _gold_paths_from_mapping(confinement)
    for gid, expected_sha in run_hashes.items():
        x_path = gold_paths.get(gid)
        if x_path is None or not x_path.is_file():
            msg = f"mapped gold score file is absent on disk for {gid}"
            raise Req012ReplayStopError(msg)
        actual = sha256_file(x_path)
        if actual != expected_sha:
            msg = (
                f"on-disk gold SHA-256 {actual} for {gid} differs from "
                f"frozen {expected_sha}"
            )
            raise Req012ReplayStopError(msg)
    return gold_paths


def _verify_manifest_and_gold(
    confinement: Confinement,
    manifest_path: Path,
    expected_sha256: str | None,
) -> tuple[Data, str, dict[str, str], dict[str, Path]]:
    if not manifest_path.is_file():
        msg = f"the OMR-REQ-012 manifest is absent: {manifest_path}"
        raise Req012ReplayStopError(msg)
    actual_sha = sha256_file(manifest_path)
    if expected_sha256 is not None and actual_sha != expected_sha256:
        msg = (
            f"OMR-REQ-012 manifest SHA-256 {actual_sha} does not match "
            f"frozen {expected_sha256}"
        )
        raise Req012ReplayStopError(msg)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, dict):
        msg = "the OMR-REQ-012 manifest root is not a JSON object"
        raise Req012ReplayStopError(msg)
    record = load_run_record(confinement)
    run_meta = _dict(record.get("run"))
    if not run_meta.get("finished"):
        msg = "the formal run has not finished"
        raise Req012ReplayStopError(msg)
    plan = _dict(run_meta.get("plan"))
    gold_plan = plan.get("gold")
    if not isinstance(gold_plan, list) or not gold_plan:
        msg = "the run record has no frozen gold plan"
        raise Req012ReplayStopError(msg)
    run_hashes = {
        str(pair[0]): str(pair[1])
        for pair in gold_plan
        if isinstance(pair, list) and len(pair) == 2  # noqa: PLR2004
    }
    manifest_scores = _dict(
        _dict(manifest.get("input_provenance")).get("gold_scores")
    )
    if set(manifest_scores) != set(run_hashes):
        msg = "manifest gold piece set differs from the frozen run gold plan"
        raise Req012ReplayStopError(msg)
    for gid, info_raw in manifest_scores.items():
        info = _dict(info_raw)
        expected_gold_sha = str(info.get("score_musicxml_sha256", ""))
        if run_hashes.get(gid) != expected_gold_sha:
            msg = (
                f"gold hash mismatch for {gid}: run has "
                f"{run_hashes.get(gid)!r}, manifest has {expected_gold_sha!r}"
            )
            raise Req012ReplayStopError(msg)
    gold_paths = _verify_on_disk_gold(confinement, run_hashes)
    return manifest, actual_sha, run_hashes, gold_paths


def _collect_bad_loci(t_path: Path, x_path: Path) -> set[tuple[int, str]]:
    cmp = compare_file(t_path, x_path)
    bad: set[tuple[int, str]] = set()
    for diff in cmp.content.counted:
        m_indices = {
            loc.measure_index
            for op in diff.operations
            for loc in (op.location1, op.location2)
            if loc is not None and loc.measure_index is not None
        }
        for cat in diff.categories:
            if m_indices:
                for m_idx in m_indices:
                    bad.add((m_idx, cat))
            else:
                bad.add((-1, cat))
    for check_name, (non_ident, extra) in cmp.check_differences().items():
        if non_ident > 0 or extra > 0:
            bad.add((-1, CHECKS[check_name]))
    return bad


def _piece_measure_diffs(
    confinement: Confinement,
    gold_id: str,
    gold_paths: Mapping[str, Path],
) -> tuple[bool, set[tuple[int, str]]]:
    """Return ``(piece_ok, {(measure_index, category), ...})`` for one piece."""
    directory = file_directory(confinement, GOLD_ARM, gold_id)
    res_path = directory / "result.json"
    if not res_path.is_file():
        msg = f"missing gold result for {gold_id}"
        raise Req012ReplayStopError(msg)
    result = read_object(res_path)
    t_path = directory / "t.musicxml"
    rev_path = directory / "reverse.txt"
    x_path = gold_paths.get(gold_id)
    if (
        result.get("failed")
        or result.get("import_check") != "passed"
        or result.get("text_space") != "consistent"
        or not t_path.is_file()
        or not rev_path.is_file()
        or x_path is None
        or not x_path.is_file()
    ):
        return False, set()
    content_pass = result.get("content_pass")
    if not isinstance(content_pass, dict) or content_pass.get("status") != "ok":
        return False, set()
    cat_counts = content_pass.get("category_counts")
    check_diffs = content_pass.get("check_differences")
    if (
        isinstance(cat_counts, dict)
        and isinstance(check_diffs, dict)
        and content_pass.get("counted_differences") == 0
        and not check_diffs
        and not any(isinstance(v, int) and v > 0 for v in cat_counts.values())
    ):
        return True, set()
    return True, _collect_bad_loci(t_path, x_path)


def _gold_paths_from_mapping(confinement: Confinement) -> dict[str, Path]:
    mapping_path = confinement.run / GOLD_MAPPING
    if not mapping_path.is_file():
        return {}
    raw = read_object(mapping_path).get("pieces")
    if not isinstance(raw, list):
        return {}
    out: dict[str, Path] = {}
    for entry in raw:
        if isinstance(entry, dict) and isinstance(entry.get("path"), str):
            out[str(entry.get("id"))] = Path(str(entry["path"]))
    return out


def _locus_passes(
    locus: Mapping[str, object],
    piece_state: Mapping[str, tuple[bool, set[tuple[int, str]]]],
) -> bool:
    gid = str(locus.get("gold_id"))
    ok, bad = piece_state[gid]
    if not ok:
        return False
    if not bad:
        return True
    target = _dict(locus.get("replay_target"))
    cat = str(
        target.get("expected_zero_diff_category") or locus.get("w1_category")
    )
    if (-1, cat) in bad:
        return False
    ordinals = target.get("target_measure_ordinals")
    m_single = locus.get("measure")
    measures = (
        [m for m in ordinals if isinstance(m, int)]
        if isinstance(ordinals, list) and ordinals
        else [m_single]
        if isinstance(m_single, int)
        else []
    )
    return all((m - 1, cat) not in bad for m in measures)


def _replay_markdown(summary: Mapping[str, object]) -> str:
    by_cat = _dict(summary.get("by_category"))
    by_piece = _dict(summary.get("by_gold_id"))
    lines = [
        "# W1 OMR-REQ-012 toolchain-class replay",
        "",
        "Each statement is a runtime observation unless noted.",
        "",
        (
            f"- Converter and harness commit: `{summary.get('commit')}`; "
            f"code tree: `{summary.get('tree')}`."
        ),
        (
            f"- Private manifest SHA-256: `{summary.get('manifest_sha256')}` "
            "(local read-only runtime observation; expected digest fixed in "
            "`docs/requirements.md`)."
        ),
        (
            f"- Frozen set (`Set A union Set B`): "
            f"`{summary.get('total_loci')}` deduplicated loci "
            f"(`{summary.get('raw_entries')}` raw entries); passed without "
            f"score-file edits: `{summary.get('passed_loci')} / "
            f"{summary.get('total_loci')}` "
            f"(`failed: {summary.get('failed_loci')}`)."
        ),
        (
            f"- Desk-study `T01` subset (`Set A`): "
            f"`{summary.get('set_a_passed')} / {summary.get('set_a_loci')}` "
            "loci."
        ),
        (
            f"- Post-`T01` confirmed-`B` subset: "
            f"`{summary.get('post_t01_passed')} / "
            f"{summary.get('post_t01_loci')}` loci."
        ),
        "",
        "## Replay results by W1 category",
        "",
        "| Category | Passed | Total |",
        "| --- | ---: | ---: |",
    ]
    for cat, pair in by_cat.items():
        if isinstance(pair, list) and len(pair) == 2:  # noqa: PLR2004
            lines.append(f"| `{cat}` | {pair[0]} | {pair[1]} |")
    lines += [
        "",
        "## Replay results by anonymized gold batch",
        "",
        "| Gold ID | Passed | Total |",
        "| --- | ---: | ---: |",
    ]
    for gid, pair in by_piece.items():
        if isinstance(pair, list) and len(pair) == 2:  # noqa: PLR2004
            lines.append(f"| `{gid}` | {pair[0]} | {pair[1]} |")
    return "\n".join(lines) + "\n"


def replay_req012(
    confinement: Confinement,
    manifest_path: Path,
    *,
    expected_sha256: str | None = FROZEN_MANIFEST_SHA256,
) -> Data:
    """Verify and score the frozen OMR-REQ-012 set on a completed run.

    Args:
        confinement: The confined formal-run directory.
        manifest_path: Local read-only path to ``req012-private-manifest.json``.
        expected_sha256: Expected manifest SHA-256, or ``None`` in unit tests.

    Returns:
        The sanitized public summary dictionary written to
        ``report/req012-replay.json``.
    """
    manifest, actual_sha, run_hashes, gold_paths = _verify_manifest_and_gold(
        confinement, manifest_path, expected_sha256
    )
    union = _dict(manifest.get("union_set_a_all_34_and_set_b"))
    loci_raw = union.get("loci")
    if not isinstance(loci_raw, list) or not loci_raw:
        msg = "the OMR-REQ-012 manifest contains no union loci"
        raise Req012ReplayStopError(msg)
    if expected_sha256 == FROZEN_MANIFEST_SHA256 and (
        len(loci_raw) != FROZEN_LOCUS_COUNT
        or union.get("raw_count") != FROZEN_RAW_COUNT
    ):
        msg = "the OMR-REQ-012 manifest locus counts differ from frozen values"
        raise Req012ReplayStopError(msg)
    piece_state = {
        gid: _piece_measure_diffs(confinement, gid, gold_paths)
        for gid in sorted(run_hashes)
    }
    cat_tot: Counter[str] = Counter()
    cat_pass: Counter[str] = Counter()
    piece_tot: Counter[str] = Counter()
    piece_pass: Counter[str] = Counter()
    passed_total = set_a_tot = set_a_pass = post_tot = post_pass = 0
    for item in loci_raw:
        locus = _dict(item)
        gid = str(locus.get("gold_id"))
        cat = str(locus.get("w1_category"))
        ok = _locus_passes(locus, piece_state)
        cat_tot[cat] += 1
        piece_tot[gid] += 1
        if ok:
            passed_total += 1
            cat_pass[cat] += 1
            piece_pass[gid] += 1
        if locus.get("in_set_a"):
            set_a_tot += 1
            set_a_pass += int(ok)
        if locus.get("in_post_t01"):
            post_tot += 1
            post_pass += int(ok)
    run_meta = _dict(load_run_record(confinement).get("run"))
    summary: Data = {
        "schema": "w1-req012-replay-v1",
        "commit": run_meta.get("commit"),
        "tree": run_meta.get("tree"),
        "manifest_sha256": actual_sha,
        "raw_entries": union.get("raw_count", len(loci_raw)),
        "total_loci": len(loci_raw),
        "passed_loci": passed_total,
        "failed_loci": len(loci_raw) - passed_total,
        "all_passed": passed_total == len(loci_raw),
        "set_a_loci": set_a_tot,
        "set_a_passed": set_a_pass,
        "post_t01_loci": post_tot,
        "post_t01_passed": post_pass,
        "by_category": {c: [cat_pass[c], cat_tot[c]] for c in sorted(cat_tot)},
        "by_gold_id": {
            g: [piece_pass[g], piece_tot[g]] for g in sorted(piece_tot)
        },
    }
    secrets = private_strings(confinement)
    json_text = json.dumps(summary, indent=2, ensure_ascii=False) + "\n"
    md_text = _replay_markdown(summary)
    scan(json_text, secrets)
    scan(md_text, secrets)
    report_dir = confinement.run / "report"
    confinement.write_text(report_dir / "req012-replay.json", json_text)
    confinement.write_text(report_dir / "req012-replay.md", md_text)
    return summary
