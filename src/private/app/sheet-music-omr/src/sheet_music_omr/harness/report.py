"""Aggregate a run's results into the public and local reports.

The public report holds only what the protocol lets a public carrier hold:
per-arm figures with numerators and denominators, counts by category, check,
status and reason, the run record's versions and commits, and the public file
lists. It is scanned for private content before it is written. The local
report adds per-file details and stays in the run directory.
"""

from __future__ import annotations

import statistics
from collections import Counter
from typing import TYPE_CHECKING

from sheet_music_omr.harness.privacy import private_strings, scan
from sheet_music_omr.harness.records import (
    RecordError,
    load_run_record,
    read_object,
)
from sheet_music_omr.harness.run import GOLD_ARM

if TYPE_CHECKING:
    from collections.abc import Iterable, Mapping, Sequence
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement

type Data = dict[str, object]

PDMX_CITATION = (
    "PDMX: A Large-Scale Public Domain MusicXML Dataset for Symbolic Music "
    "Processing, version v9, Zenodo, doi:10.5281/zenodo.15571083; the "
    "deposit is licensed CC BY 4.0"
)
"""The citation of the PDMX deposit."""

LIEDER_SOURCE = (
    "OpenScore Lieder v3.0.0, commit f44e962a1ff85ddcc2f6f42bed3b0c529555ee15, "
    "doi:10.5281/zenodo.15450144, CC0"
)
"""The pinned OpenScore Lieder release."""

OBSERVATION = "runtime observation"
SOURCE_FINDING = "source finding"
INCOMPLETE = "not computable (incomplete)"
"""A pooled figure of an arm with fewer completed files than planned."""

_FIGURES = (
    "measure_edit_cost",
    "global_edit_cost",
    "whole_score_omr_edit_distance",
    "whole_score_symbols_t",
    "whole_score_symbols_y",
)


def _dict(value: object) -> Data:
    return value if isinstance(value, dict) else {}


def _int(value: object) -> int:
    return (
        value if isinstance(value, int) and not isinstance(value, bool) else 0
    )


def _pool(tallies: Iterable[object], *, incomplete: bool = False) -> Data:
    """Pool element tallies; a missing tally makes the figure uncomputable.

    An incomplete arm, with fewer completed files than planned, has no
    pooled figure either.
    """
    elements = identical = extras = missing = 0
    per_file: list[float] = []
    for value in tallies:
        tally = _dict(value)
        if not tally:
            missing += 1
            continue
        elements += _int(tally.get("elements"))
        identical += _int(tally.get("identical"))
        extras += _int(tally.get("extras"))
        fidelity = tally.get("fidelity")
        if isinstance(fidelity, (int, float)) and not isinstance(
            fidelity, bool
        ):
            per_file.append(float(fidelity))
    denominator = elements + extras
    return {
        "elements": elements,
        "identical": identical,
        "extras": extras,
        "denominator": denominator,
        "fidelity": (
            identical / denominator
            if denominator and not missing and not incomplete
            else None
        ),
        "uncomputable_files": missing,
        "incomplete": incomplete,
        "per_file": (
            {"files": len(per_file)} if incomplete else _distribution(per_file)
        ),
    }


def _distribution(values: Sequence[float]) -> Data:
    if not values:
        return {"files": 0}
    return {
        "files": len(values),
        "mean": statistics.fmean(values),
        "min": min(values),
        "median": statistics.median(values),
        "max": max(values),
    }


def _load_results(directory: Path) -> list[Data]:
    if not directory.is_dir():
        return []
    return [
        read_object(path) for path in sorted(directory.glob("*/result.json"))
    ]


def _excluded(contents: Iterable[Data]) -> Data:
    """Sum the excluded reference voices of content passes, by reason."""
    reasons: Counter[str] = Counter()
    for content in contents:
        reasons.update(
            {
                k: _int(v)
                for k, v in _dict(content.get("excluded_voices")).items()
            },
        )
    return {
        "total": sum(reasons.values()),
        "by_reason": dict(sorted(reasons.items())),
    }


def _arm_summary(results: Sequence[Data], planned: int) -> Data:
    """Aggregate one arm's file results."""
    passed = [r for r in results if not r.get("failed")]
    contents = [_dict(r.get("content_pass")) for r in passed]
    categories: Counter[str] = Counter()
    checks: dict[str, list[int]] = {}
    figures: Counter[str] = Counter()
    normalized: list[float] = []
    counted = 0
    for content in contents:
        categories.update(
            {
                k: _int(v)
                for k, v in _dict(content.get("category_counts")).items()
            },
        )
        for name, pair in _dict(content.get("check_differences")).items():
            if isinstance(pair, list) and len(pair) == 2:  # noqa: PLR2004
                totals = checks.setdefault(name, [0, 0])
                totals[0] += _int(pair[0])
                totals[1] += _int(pair[1])
        numbers = _dict(content.get("figures"))
        figures.update({name: _int(numbers.get(name)) for name in _FIGURES})
        distance = numbers.get("whole_score_normalized_distance")
        if isinstance(distance, (int, float)):
            normalized.append(float(distance))
        counted += _int(content.get("counted_differences"))
    incomplete = len(results) < planned
    return {
        "planned": planned,
        "completed": len(results),
        "incomplete": incomplete,
        "failed": sum(1 for r in results if r.get("failed")),
        "content": _pool(
            (r.get("content") for r in results), incomplete=incomplete
        ),
        "layout": _pool(
            (r.get("layout") for r in results), incomplete=incomplete
        ),
        "counted_differences": counted,
        "categories": dict(sorted(categories.items())),
        "checks": dict(sorted(checks.items())),
        "voice_order": _pool(
            (_dict(c.get("checks")).get("voice_order") for c in contents),
            incomplete=incomplete,
        ),
        "excluded_voices": _excluded(contents),
        "figures": dict(figures),
        "normalized_distance": _distribution(normalized),
        "failures": dict(
            Counter(str(r.get("failure")) for r in results if r.get("failed")),
        ),
        "import_check": dict(
            Counter(str(r.get("import_check")) for r in results)
        ),
        "text_space": dict(Counter(str(r.get("text_space")) for r in results)),
        "returns_to_owner": sum(
            1 for r in results if r.get("returns_to_owner")
        ),
    }


def _baseline_summary(results: Sequence[Data]) -> Data:
    content = [_dict(r.get("content")) for r in results]
    layout = [_dict(r.get("layout")) for r in results]
    categories: Counter[str] = Counter()
    for part in content:
        categories.update(
            {k: _int(v) for k, v in _dict(part.get("category_counts")).items()},
        )
    ok = [p for p in content if p.get("status") == "ok"]
    return {
        "files": len(results),
        "status": dict(Counter(str(r.get("status")) for r in results)),
        "content_status": dict(
            Counter(str(p.get("status")) for p in content if p)
        ),
        "layout_status": dict(
            Counter(str(p.get("status")) for p in layout if p)
        ),
        "content": _pool(p.get("tally") for p in ok),
        "layout": _pool(
            p.get("tally") for p in layout if p.get("status") == "ok"
        ),
        "categories": dict(sorted(categories.items())),
        "excluded_voices": _excluded(ok),
    }


def _assembly_summary(results: Sequence[Data]) -> Data:
    systems = [_int(r.get("systems")) for r in results if "systems" in r]
    return {
        "pieces": len(results),
        "passed": sum(1 for r in results if r.get("passed") is True),
        "passed_with_excluded_voices": sum(
            1
            for r in results
            if r.get("passed") is True and _int(r.get("excluded_voices"))
        ),
        "excluded_voices": sum(_int(r.get("excluded_voices")) for r in results),
        "texts_differ": sum(
            1 for r in results if r.get("texts_differ") is True
        ),
        "no_breaks": sum(1 for r in results if r.get("status") == "no-breaks"),
        "status": dict(Counter(str(r.get("status")) for r in results)),
        "systems": {
            "total": sum(systems),
            **(
                {
                    "mean": statistics.fmean(systems),
                    "min": min(systems),
                    "max": max(systems),
                }
                if systems
                else {}
            ),
        },
    }


def summarize(confinement: Confinement) -> Data:
    """Aggregate every phase of a run from its run directory.

    Raises:
        RecordError: If the run record has no frozen plan.
    """
    record = load_run_record(confinement)
    run = _dict(record.get("run"))
    plan = _dict(run.get("plan"))
    if not plan:
        message = "the run record has no frozen plan"
        raise RecordError(message)
    corpora = [str(c) for c in _list(plan.get("corpora"))]
    public = [p for p in _list(plan.get("public")) if isinstance(p, list)]
    gold = _list(plan.get("gold"))
    planned = {
        f"{corpus}-{kind}": sum(1 for p in public if p[1] == corpus)
        for corpus in corpora
        for kind in ("x", "rx")
    }
    if isinstance(gold, list) and gold:
        planned[GOLD_ARM] = len(gold)
    keys = {str(p[0]): str(p[2]) for p in public}
    arm_results = {
        arm: _load_results(confinement.run / "files" / arm) for arm in planned
    }
    failed_files = sorted(
        {
            (arm, keys.get(str(r.get("id")), ""), str(r.get("failure")))
            for arm, results in arm_results.items()
            if arm != GOLD_ARM
            for r in results
            if r.get("failed")
        },
    )
    selections: list[Data] = []
    for corpus in corpora:
        path = confinement.run / "selection" / f"{corpus}.json"
        if path.exists():
            selections.append(read_object(path))
    return {
        "source": {
            "commit": run.get("commit"),
            "dirty": run.get("dirty"),
            "started": run.get("started"),
            "finished": run.get("finished"),
        },
        "preflight": _dict(record.get("preflight")),
        "stops": record.get("stops", []),
        "arms": {
            arm: _arm_summary(results, planned[arm])
            for arm, results in arm_results.items()
        },
        "baseline": {
            corpus: _baseline_summary(
                [
                    r
                    for r in _load_results(
                        confinement.run / "files" / "baseline"
                    )
                    if any(
                        p[0] == r.get("id") and p[1] == corpus for p in public
                    )
                ],
            )
            for corpus in corpora
        },
        "assembly": _assembly_summary(
            _load_results(confinement.run / "files" / "assembly"),
        ),
        "selections": [
            {
                "corpus": s.get("corpus"),
                "candidates": s.get("candidates"),
                "screened": len(_list(s.get("visited"))),
                "selected": [
                    str(_dict(e).get("key")) for e in _list(s.get("selected"))
                ],
                "exclusions": dict(
                    Counter(
                        str(_dict(v).get("reason"))
                        for v in _list(s.get("visited"))
                        if _dict(v).get("reason")
                    ),
                ),
            }
            for s in selections
        ],
        "failed_public_files": [list(entry) for entry in failed_files],
        "corpora": corpora,
    }


def _list(value: object) -> list[object]:
    return value if isinstance(value, list) else []


def _percent(value: object) -> str:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return f"{100 * value:.4f}%"
    return "not computable"


def _ratio(pool: Mapping[str, object]) -> str:
    if pool.get("incomplete"):
        return INCOMPLETE
    return (
        f"{_percent(pool.get('fidelity'))} "
        f"({pool.get('identical')} / {pool.get('denominator')})"
    )


def _counts(counts: Mapping[str, object]) -> str:
    if not counts:
        return "none"
    return ", ".join(f"{name} {value}" for name, value in counts.items())


def _arm_lines(arm: str, data: Mapping[str, object], tag: str) -> list[str]:
    """Return one arm's counts section of the public report."""
    content = _dict(data.get("content"))
    per_file = _dict(content.get("per_file"))
    figures = _dict(data.get("figures"))
    checks = {
        name: f"{_list(pair)[0]} non-identical, {_list(pair)[1]} extra"
        for name, pair in _dict(data.get("checks")).items()
    }
    categories = _counts(_dict(data.get("categories")))
    voice_order = _dict(data.get("voice_order"))
    excluded = _dict(data.get("excluded_voices"))
    failures = _counts(_dict(data.get("failures")))
    imports = _counts(_dict(data.get("import_check")))
    text_space = _counts(_dict(data.get("text_space")))
    normalized = _dict(data.get("normalized_distance"))
    incomplete = bool(data.get("incomplete"))
    costs = (
        INCOMPLETE
        if incomplete
        else (
            f"measure pairs {figures.get('measure_edit_cost')}, "
            f"global-only {figures.get('global_edit_cost')}; whole-score "
            "OMR edit distance "
            f"{figures.get('whole_score_omr_edit_distance')} (a "
            "diagnostic, not a symbol error rate)"
        )
    )
    symbols = (
        INCOMPLETE
        if incomplete
        else (
            f"T {figures.get('whole_score_symbols_t')}, reference "
            f"{figures.get('whole_score_symbols_y')}; normalized "
            f"distance per file mean {normalized.get('mean')}, min "
            f"{normalized.get('min')}, median {normalized.get('median')}, "
            f"max {normalized.get('max')} over {normalized.get('files')} "
            "files"
        )
    )
    per_file_line = (
        INCOMPLETE
        if incomplete
        else (
            f"min {_percent(per_file.get('min'))}, median "
            f"{_percent(per_file.get('median'))}, max "
            f"{_percent(per_file.get('max'))} over {per_file.get('files')} "
            "files"
        )
    )
    lines = [
        f"### {arm}",
        "",
        f"- Differences by category: {categories} {tag}.",
        f"- Supplementary checks: {_counts(checks)} {tag}.",
        (
            "- Voice order of files without failure: "
            f"{_ratio(voice_order)}; excluded reference voices, neither "
            f"elements nor agreement: {excluded.get('total')} (by reason: "
            f"{_counts(_dict(excluded.get('by_reason')))}) {tag}."
        ),
        f"- Failures by status: {failures} {tag}.",
        f"- Import check: {imports} {tag}.",
        f"- Text-space check: {text_space} {tag}.",
        f"- Per-file content fidelity: {per_file_line} {tag}.",
        f"- Edit costs of files without failure: {costs} {tag}.",
        (f"- Whole-score symbols of files without failure: {symbols} {tag}."),
    ]
    if arm == GOLD_ARM:
        lines.append(
            f"- Gold pieces whose count returns to the owner: "
            f"{data.get('returns_to_owner')} {tag}.",
        )
    lines.append("")
    return lines


def public_markdown(summary: Mapping[str, object]) -> str:
    """Return the public report's text."""
    tag = f"({OBSERVATION})"
    source = _dict(summary.get("source"))
    preflight = _dict(summary.get("preflight"))
    versions = _dict(preflight.get("versions"))
    musescore = _dict(preflight.get("musescore"))
    calibration = _dict(preflight.get("calibration"))
    lines = [
        "# W1 round-trip measurement",
        "",
        "Each statement is classified in parentheses.",
        "",
        "## Run record",
        "",
        (
            f"- Converter and harness commit: `{source.get('commit')}`; "
            f"uncommitted project changes: {source.get('dirty')} {tag}."
        ),
        (
            f"- Tools: Python {versions.get('python')}, musicdiff "
            f"{versions.get('musicdiff')}, music21 {versions.get('music21')}, "
            f"converter21 {versions.get('converter21')}, numpy "
            f"{versions.get('numpy')}, MuseScore {musescore.get('version')} "
            f"{tag}."
        ),
        f"- Calibration: {calibration.get('status')} {tag}.",
        f"- Datasets: {PDMX_CITATION}; {LIEDER_SOURCE} ({SOURCE_FINDING}).",
        (
            "- The Lieder arms measure piano-accompaniment extracts, not "
            f"original piano works ({SOURCE_FINDING})."
        ),
        "",
        "## Fidelity per arm",
        "",
        (
            "Fidelity is identical reference elements over reference elements "
            f"plus extra elements, pooled per arm {tag}. The layout score is "
            "reported only. The last column counts musicdiff differences "
            "only; the supplementary checks' non-identical and extra "
            "elements are listed under each arm's counts, and a file has no "
            "counted difference only when both are zero."
        ),
        "",
        (
            "| Arm | Files | Failed | Content fidelity | Mean per file | "
            "Layout score | musicdiff counted differences |"
        ),
        "| --- | ---: | ---: | --- | ---: | --- | ---: |",
    ]
    arms = _dict(summary.get("arms"))
    for arm, value in arms.items():
        data = _dict(value)
        content = _dict(data.get("content"))
        layout = _dict(data.get("layout"))
        per_file = _dict(content.get("per_file"))
        lines.append(
            f"| {arm} | {data.get('completed')} of {data.get('planned')} | "
            f"{data.get('failed')} | {_ratio(content)} | "
            f"{_percent(per_file.get('mean'))} | {_ratio(layout)} | "
            f"{data.get('counted_differences')} |",
        )
    lines += ["", "## Counts per arm", ""]
    for arm, value in arms.items():
        lines += _arm_lines(arm, _dict(value), tag)
    lines += ["## MuseScore-only baseline: X against R(X)", ""]
    for corpus, value in _dict(summary.get("baseline")).items():
        data = _dict(value)
        lines.append(
            f"- {corpus}: content {_ratio(_dict(data.get('content')))}, layout "
            f"{_ratio(_dict(data.get('layout')))}; status "
            f"{_counts(_dict(data.get('status')))}; content status "
            f"{_counts(_dict(data.get('content_status')))}; layout status "
            f"{_counts(_dict(data.get('layout_status')))}; differences by "
            f"category {_counts(_dict(data.get('categories')))}; excluded "
            "reference voices "
            f"{_dict(data.get('excluded_voices')).get('total')}. Never "
            f"counted as converter error {tag}.",
        )
    assembly = _dict(summary.get("assembly"))
    systems = _dict(assembly.get("systems"))
    lines += [
        "",
        "## System-assembly test",
        "",
        (
            f"- Pieces {assembly.get('pieces')}, passed "
            f"{assembly.get('passed')}, texts differ "
            f"{assembly.get('texts_differ')}, without a system or page break "
            f"{assembly.get('no_breaks')} (returns to the owner) {tag}."
        ),
        (
            f"- Systems per piece: total {systems.get('total')}, mean "
            f"{systems.get('mean')}, min {systems.get('min')}, max "
            f"{systems.get('max')} {tag}."
        ),
        f"- Status: {_counts(_dict(assembly.get('status')))} {tag}.",
        (
            "- Excluded reference voices in the compared texts: "
            f"{assembly.get('excluded_voices')}; passed pieces with any: "
            f"{assembly.get('passed_with_excluded_voices')} {tag}."
        ),
        "",
        "## Selection",
        "",
    ]
    if not _list(summary.get("selections")):
        lines.append(f"- No corpus sample was selected in this run {tag}.")
    for selection in _list(summary.get("selections")):
        data = _dict(selection)
        lines.append(
            f"- {data.get('corpus')}: {len(_list(data.get('selected')))} "
            f"selected of {data.get('screened')} screened candidates; "
            f"exclusions by first failing reason (screened prefix only): "
            f"{_counts(_dict(data.get('exclusions')))} {tag}.",
        )
    failed = _list(summary.get("failed_public_files"))
    lines += ["", "## Failed public files", ""]
    if not failed:
        lines.append(f"- None {tag}.")
    for entry in failed:
        arm, key, status = (str(part) for part in _list(entry))
        lines.append(f"- {arm}: `{key}`: {status} {tag}.")
    stops = _list(summary.get("stops"))
    if stops:
        lines += ["", f"The run stopped {len(stops)} times {tag}."]
    return "\n".join(lines) + "\n"


def local_markdown(
    confinement: Confinement, summary: Mapping[str, object]
) -> str:
    """Return the local report: the public figures and per-file details."""
    lines = [public_markdown(summary), "## Per-file results (local only)", ""]
    lines += [
        "| Arm | File | Failure | Content | Layout | Import | Text space |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for arm in _dict(summary.get("arms")):
        for result in _load_results(confinement.run / "files" / arm):
            content = _dict(result.get("content"))
            layout = _dict(result.get("layout"))
            lines.append(
                f"| {arm} | {result.get('id')} | {result.get('failure')} | "
                f"{content.get('identical')}/{content.get('elements')}"
                f"+{content.get('extras')} | "
                f"{layout.get('identical')}/{layout.get('elements')}"
                f"+{layout.get('extras')} | {result.get('import_check')} | "
                f"{result.get('text_space')} |",
            )
    return "\n".join(lines) + "\n"


def write_report(confinement: Confinement) -> Data:
    """Write the public and local reports; scan the public one first.

    Returns:
        The summary.

    Raises:
        PrivacyError: If the public text holds private content.
    """
    summary = summarize(confinement)
    public = public_markdown(summary)
    scan(public, private_strings(confinement))
    report = confinement.run / "report"
    confinement.write_text(report / "public.md", public)
    confinement.write_text(
        report / "local.md", local_markdown(confinement, summary)
    )
    return summary
