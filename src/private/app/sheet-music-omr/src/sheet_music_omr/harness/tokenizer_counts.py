"""Tokenizer-count comparison protocol execution for W1 (OMR-REQ-014).

Counts subword tokens with a named Hugging Face ``tokenizer.json`` (defaulting
to ``Qwen/Qwen3-VL-2B-Instruct`` at commit
``89644892e4d85e24eaac8bacfd4f463576704203``, downloaded without model weights
under the shared W1 byte ledger) across matched Text Score 2 (``v2``), ABC
(``converter21`` ``xml2abc``), source MusicXML (``X``) and forward MusicXML
(``T(X)``) representations of a completed formal run's inputs.
"""

from __future__ import annotations

import codecs
import contextlib
import json
import math
import statistics
from collections import Counter
from pathlib import Path
from typing import TYPE_CHECKING, Final
from xml.parsers import expat

import tokenizers as tok_pkg
from converter21.abc import xml2abc
from tokenizers import Tokenizer

from sheet_music_omr.harness.downloads import (
    QWEN3_VL_TOKENIZER,
    fetch_hf_file,
    sha256_file,
    urllib_fetch,
)
from sheet_music_omr.harness.preflight import tool_versions
from sheet_music_omr.harness.privacy import GOLD_MAPPING, private_strings, scan
from sheet_music_omr.harness.records import load_run_record, read_object
from sheet_music_omr.harness.run import GOLD_ARM, file_directory
from sheet_music_omr.harness.selection import inputs_dir
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import (
    SplitError,
    split_score,
    system_starts,
)
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_reverse import _unzip
from sheet_music_omr.parser import parse_document
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Mapping, Sequence

    from sheet_music_omr.harness.confine import Confinement
    from sheet_music_omr.harness.downloads import HuggingFaceFile, Ledger

type Data = dict[str, object]

SCHEMA: Final = "w1-tokenizer-counts-v1"


class TokenizerCountsStopError(StopConditionError):
    """Raised when run provenance or input integrity fails before counting."""


def _dict(value: object) -> Data:
    return value if isinstance(value, dict) else {}


def _percentile(sorted_vals: Sequence[float], q: float) -> float:
    if len(sorted_vals) == 1:
        return sorted_vals[0]
    pos = (len(sorted_vals) - 1) * q
    lo = math.floor(pos)
    hi = math.ceil(pos)
    if lo == hi:
        return sorted_vals[lo]
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (pos - lo)


def _stats(values: Sequence[int | float]) -> Data:
    if not values:
        return {"count": 0}
    ordered = sorted(float(v) for v in values)
    return {
        "count": len(ordered),
        "total": sum(values),
        "mean": round(statistics.fmean(ordered), 2),
        "median": round(statistics.median(ordered), 2),
        "p90": round(_percentile(ordered, 0.9), 2),
        "min": round(ordered[0], 2),
        "max": round(ordered[-1], 2),
    }


def _decode_musicxml_bytes(raw: bytes) -> tuple[bytes, str]:
    xml_bytes = _unzip(raw) if raw.startswith(b"PK") else raw
    declared: list[str] = []
    parser = expat.ParserCreate()

    def on_decl(_version: str, encoding: str | None, _standalone: int) -> None:
        if encoding:
            declared.append(encoding)

    parser.XmlDeclHandler = on_decl
    with contextlib.suppress(expat.ExpatError):
        parser.Parse(xml_bytes[:1024])
    if xml_bytes.startswith(codecs.BOM_UTF8):
        return xml_bytes, xml_bytes.decode("utf-8-sig")
    if xml_bytes.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
        return xml_bytes, xml_bytes.decode("utf-16")
    enc = declared[0] if declared else "utf-8"
    return xml_bytes, xml_bytes.decode(enc)


def _xml_to_abc(xml_bytes: bytes) -> str | None:
    try:
        abc_out, _ = xml2abc.vertaal(
            xml_bytes,
            u=0,
            b=0,
            n=0,
            c=0,
            v=0,
            d=0,
            m=0,
            x=0,
            t=False,
        )
    except Exception:  # noqa: BLE001 - third-party converter21 xml2abc failure
        return None
    stripped = abc_out.strip()
    return stripped if stripped else None


def _count_tokens(tokenizer: Tokenizer, text: str) -> int:
    return len(tokenizer.encode(text, add_special_tokens=False).ids)


def _gold_paths(confinement: Confinement) -> dict[str, Path]:
    mapping_path = confinement.run / GOLD_MAPPING
    if not mapping_path.is_file():
        return {}
    pieces = read_object(mapping_path).get("pieces")
    if not isinstance(pieces, list):
        return {}
    return {
        str(p.get("id")): Path(str(p["path"]))
        for p in pieces
        if isinstance(p, dict) and isinstance(p.get("path"), str)
    }


PINNED_TOKENIZERS_VERSION: Final = "0.23.3"


type _PieceOutcome = tuple[
    str | None,
    tuple[dict[str, int], dict[str, float]] | None,
    list[int],
    str | None,
]


def _split_gold_systems(
    tokenizer: Tokenizer, doc: AssembledScore, x_path: Path
) -> tuple[list[int], str | None]:
    try:
        layout = system_starts(x_path.read_bytes())
        if len(doc.blocks) != layout.measures:
            return [], "measure_count_mismatch"
        return [
            _count_tokens(tokenizer, write_document(sys_doc))
            for sys_doc in split_score(doc, layout.starts)
        ], None
    except SplitError:
        return [], "split_error"


def _score_piece(
    tokenizer: Tokenizer,
    x_path: Path,
    res_dir: Path,
    *,
    include_systems: bool,
) -> _PieceOutcome:
    res = read_object(res_dir / "result.json")
    v2_path = res_dir / "reverse.txt"
    t_path = res_dir / "t.musicxml"
    if res.get("failed") or not v2_path.is_file() or not t_path.is_file():
        return "roundtrip_failed", None, [], None
    v2_text = v2_path.read_text(encoding="utf-8")
    doc = parse_document(v2_text)
    if not isinstance(doc, AssembledScore) or not doc.blocks:
        return "invalid_v2_document", None, [], None
    sys_toks, sys_skip = (
        _split_gold_systems(tokenizer, doc, x_path)
        if include_systems
        else ([], None)
    )
    try:
        x_xml_bytes, x_text = _decode_musicxml_bytes(x_path.read_bytes())
    except UnicodeDecodeError, LookupError:
        return "invalid_x_encoding", None, sys_toks, sys_skip
    t_text = t_path.read_text(encoding="utf-8")
    abc_text = _xml_to_abc(x_xml_bytes)
    if abc_text is None:
        return "xml2abc_failed", None, sys_toks, sys_skip
    measures = len(doc.blocks)
    piece_toks = {
        "v2": _count_tokens(tokenizer, v2_text),
        "abc_xml2abc": _count_tokens(tokenizer, abc_text),
        "musicxml_source": _count_tokens(tokenizer, x_text),
        "musicxml_forward": _count_tokens(tokenizer, t_text),
        "measures": measures,
    }
    per_m = {
        k: piece_toks[k] / measures
        for k in ("v2", "abc_xml2abc", "musicxml_source", "musicxml_forward")
    }
    return None, (piece_toks, per_m), sys_toks, sys_skip


def _summarize_cohort(
    tokenizer: Tokenizer,
    items: Sequence[tuple[Path, Path]],
    *,
    include_systems: bool,
) -> Data:
    keys = ("v2", "abc_xml2abc", "musicxml_source", "musicxml_forward")
    by_piece: dict[str, list[int]] = {k: [] for k in keys}
    by_measure: dict[str, list[float]] = {k: [] for k in keys}
    systems: list[int] = []
    skipped_reasons: Counter[str] = Counter()
    sys_skipped: Counter[str] = Counter()
    total_measures = 0
    for x_path, res_dir in items:
        skip_reason, scored, sys_toks, sys_skip = _score_piece(
            tokenizer, x_path, res_dir, include_systems=include_systems
        )
        systems.extend(sys_toks)
        if sys_skip is not None:
            sys_skipped[sys_skip] += 1
        if skip_reason is not None or scored is None:
            skipped_reasons[skip_reason or "unknown"] += 1
            continue
        p_toks, m_toks = scored
        total_measures += p_toks["measures"]
        for k in keys:
            by_piece[k].append(p_toks[k])
            by_measure[k].append(m_toks[k])
    v2_tot = sum(by_piece["v2"])
    abc_tot = sum(by_piece["abc_xml2abc"])
    x_tot = sum(by_piece["musicxml_source"])
    t_tot = sum(by_piece["musicxml_forward"])
    out: Data = {
        "evaluated_pieces": len(by_piece["v2"]),
        "skipped_pieces": sum(skipped_reasons.values()),
        "skipped_by_reason": dict(sorted(skipped_reasons.items())),
        "total_measures": total_measures,
        "per_piece_tokens": {k: _stats(by_piece[k]) for k in keys},
        "per_measure_tokens": {k: _stats(by_measure[k]) for k in keys},
        "pooled_tokens_per_measure": {
            k: round(sum(by_piece[k]) / total_measures, 2)
            if total_measures
            else None
            for k in keys
        },
        "ratios": {
            "v2_over_abc_xml2abc": round(v2_tot / abc_tot, 4)
            if abc_tot
            else None,
            "musicxml_source_over_v2": round(x_tot / v2_tot, 2)
            if v2_tot
            else None,
            "musicxml_forward_over_v2": round(t_tot / v2_tot, 2)
            if v2_tot
            else None,
        },
    }
    if include_systems:
        out["v2_per_system_tokens"] = _stats(systems)
        out["v2_system_split_skipped"] = dict(sorted(sys_skipped.items()))
    return out


def _verify_gold_items(
    confinement: Confinement, gold_raw: object
) -> list[tuple[Path, Path]]:
    gold_entries = gold_raw if isinstance(gold_raw, list) else []
    if not gold_entries:
        return []
    if not (confinement.run / GOLD_MAPPING).is_file():
        msg = "the gold mapping file is absent"
        raise TokenizerCountsStopError(msg)
    gold_map = _gold_paths(confinement)
    items: list[tuple[Path, Path]] = []
    for pair in gold_entries:
        if not isinstance(pair, list) or len(pair) != 2:  # noqa: PLR2004
            continue
        gid, expected_sha = str(pair[0]), str(pair[1])
        xp = gold_map.get(gid)
        res_dir = file_directory(confinement, GOLD_ARM, gid)
        if (
            xp is None
            or not xp.is_file()
            or sha256_file(xp) != expected_sha
            or not (res_dir / "result.json").is_file()
        ):
            msg = f"gold input or result missing or hash changed for {gid}"
            raise TokenizerCountsStopError(msg)
        items.append((xp, res_dir))
    return items


def _verify_public_items(
    confinement: Confinement, public_raw: object
) -> dict[str, list[tuple[Path, Path]]]:
    public_entries = public_raw if isinstance(public_raw, list) else []
    public_by_corpus: dict[str, list[tuple[Path, Path]]] = {}
    for corpus in ("pdmx", "lieder"):
        c_items: list[tuple[Path, Path]] = []
        for p in public_entries:
            if not isinstance(p, list) or len(p) < 2 or p[1] != corpus:  # noqa: PLR2004
                continue
            fid = str(p[0])
            xp = inputs_dir(confinement, corpus) / fid / "x.musicxml"
            res_dir = file_directory(confinement, f"{corpus}-x", fid)
            if not xp.is_file() or not (res_dir / "result.json").is_file():
                msg = f"missing public input or result for {corpus} {fid}"
                raise TokenizerCountsStopError(msg)
            c_items.append((xp, res_dir))
        if c_items:
            public_by_corpus[corpus] = c_items
    return public_by_corpus


def verify_tokenizer_run_inputs(
    confinement: Confinement,
) -> tuple[Data, list[tuple[Path, Path]], dict[str, list[tuple[Path, Path]]]]:
    """Validate run completion, result files and gold hashes before download."""
    if tok_pkg.__version__ != PINNED_TOKENIZERS_VERSION:
        msg = (
            f"tokenizers version {tok_pkg.__version__} is not pinned "
            f"{PINNED_TOKENIZERS_VERSION}"
        )
        raise TokenizerCountsStopError(msg)
    record = load_run_record(confinement)
    run_meta = _dict(record.get("run"))
    if not run_meta.get("finished"):
        msg = "the formal run has not finished"
        raise TokenizerCountsStopError(msg)
    plan = _dict(run_meta.get("plan"))
    if not plan:
        msg = "the run record has no frozen plan"
        raise TokenizerCountsStopError(msg)
    return (
        run_meta,
        _verify_gold_items(confinement, plan.get("gold")),
        _verify_public_items(confinement, plan.get("public")),
    )


def _markdown(summary: Mapping[str, object]) -> str:
    tok = _dict(summary.get("tokenizer"))
    vers = _dict(summary.get("versions"))
    cohorts = _dict(summary.get("cohorts"))
    lines = [
        "# W1 tokenizer counts",
        "",
        (
            "Each count is a runtime observation for the named tokenizer on "
            "the named inputs; under OMR-REQ-014, tokenizer counts make no "
            "claim about model accuracy, learnability, review-effort savings, "
            "or base-model selection. `abc_xml2abc` is produced by "
            "`converter21` `xml2abc`, which omits unsupported notation "
            "categories, so its token counts measure that lossy projection "
            "rather than lossless semantic equivalence (source finding + "
            "runtime observation)."
        ),
        "",
        (
            f"- Converter and harness commit: `{summary.get('commit')}`; "
            f"code tree: `{summary.get('tree')}`."
        ),
        (
            f"- Environment: Python `{vers.get('python')}`, `converter21` "
            f"`{vers.get('converter21')}`, `tokenizers` "
            f"`{vers.get('tokenizers')}`."
        ),
        (
            f"- Named tokenizer: `{tok.get('repo')}@{tok.get('revision')}` "
            f"(`{tok.get('filename')}`, {tok.get('size')} bytes, git blob OID "
            f"`{tok.get('git_blob_oid')}`, SHA-256 `{tok.get('sha256')}`, "
            "`add_special_tokens=False`)."
        ),
        "",
        "## Summary by cohort",
        "",
        (
            "| Cohort | Pieces (skipped) | Measures | Pooled tok/measure: `v2` "
            "| `abc_xml2abc` | `X` (MusicXML) | `T(X)` (MusicXML) | "
            "`v2 / ABC` | `X / v2` |"
        ),
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for name, raw in cohorts.items():
        c = _dict(raw)
        pooled = _dict(c.get("pooled_tokens_per_measure"))
        ratios = _dict(c.get("ratios"))
        reasons = _dict(c.get("skipped_by_reason"))
        skip_text = (
            f"{c.get('skipped_pieces')}: "
            + ", ".join(f"{k}={v}" for k, v in reasons.items())
            if reasons
            else "0"
        )
        lines.append(
            f"| `{name}` | {c.get('evaluated_pieces')} ({skip_text}) | "
            f"{c.get('total_measures')} | {pooled.get('v2')} | "
            f"{pooled.get('abc_xml2abc')} | {pooled.get('musicxml_source')} | "
            f"{pooled.get('musicxml_forward')} | "
            f"{ratios.get('v2_over_abc_xml2abc')} | "
            f"{ratios.get('musicxml_source_over_v2')}x |"
        )
    gold_sys = _dict(_dict(cohorts.get("gold")).get("v2_per_system_tokens"))
    if gold_sys.get("count"):
        lines += [
            "",
            "## Private gold Text Score 2 system-document tokens",
            "",
            (
                f"- Systems: `{gold_sys.get('count')}`; total "
                f"`{gold_sys.get('total')}`, mean `{gold_sys.get('mean')}`, "
                f"median `{gold_sys.get('median')}`, p90 "
                f"`{gold_sys.get('p90')}`, min `{gold_sys.get('min')}`, max "
                f"`{gold_sys.get('max')}`."
            ),
        ]
    return "\n".join(lines) + "\n"


def run_tokenizer_counts(
    confinement: Confinement,
    ledger: Ledger,
    *,
    tokenizer_spec: HuggingFaceFile = QWEN3_VL_TOKENIZER,
    fetch: Callable[[str], Iterable[bytes]] = urllib_fetch,
) -> Data:
    """Validate completed run inputs, fetch ``tokenizer.json`` and count."""
    run_meta, gold_items, public_by_corpus = verify_tokenizer_run_inputs(
        confinement
    )
    tok_path = fetch_hf_file(
        tokenizer_spec, ledger=ledger, confinement=confinement, fetch=fetch
    )
    tok_sha256 = sha256_file(tok_path)
    tokenizer = Tokenizer.from_file(str(tok_path))
    cohorts: Data = {}
    if gold_items:
        cohorts["gold"] = _summarize_cohort(
            tokenizer, gold_items, include_systems=True
        )
    for corpus, c_items in public_by_corpus.items():
        cohorts[corpus] = _summarize_cohort(
            tokenizer, c_items, include_systems=False
        )
    versions = tool_versions()
    summary: Data = {
        "schema": SCHEMA,
        "commit": run_meta.get("commit"),
        "tree": run_meta.get("tree"),
        "versions": {
            "python": versions.get("python"),
            "converter21": versions.get("converter21"),
            "tokenizers": tok_pkg.__version__,
        },
        "tokenizer": {
            "repo": tokenizer_spec.repo,
            "revision": tokenizer_spec.revision,
            "filename": tokenizer_spec.filename,
            "size": tokenizer_spec.size,
            "git_blob_oid": tokenizer_spec.git_blob_oid,
            "sha256": tok_sha256,
            "library_version": tok_pkg.__version__,
        },
        "ledger_total_bytes": ledger.total,
        "cohorts": cohorts,
    }
    secrets = private_strings(confinement)
    json_text = json.dumps(summary, indent=2, ensure_ascii=False) + "\n"
    md_text = _markdown(summary)
    scan(json_text, secrets)
    scan(md_text, secrets)
    report_dir = confinement.run / "report"
    confinement.write_text(report_dir / "tokenizer-counts.json", json_text)
    confinement.write_text(report_dir / "tokenizer-counts.md", md_text)
    return summary
