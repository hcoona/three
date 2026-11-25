"""Self-authored CPU fixtures for W6 recognition and W7 handoff contracts."""
# ruff: noqa: PLR2004

from __future__ import annotations

import hashlib
import json
import math
import os
from dataclasses import replace
from pathlib import Path
from typing import Any, cast

import numpy as np
import pytest
from PIL import Image

from sheet_music_omr import (
    AssemblyStatus,
    assemble,
    carry_out,
    parse_document,
    validate_document,
)
from sheet_music_omr.model import SystemDocument
from sheet_music_omr.training.decoding import (
    MAX_MEASURE_TOKENS,
    MAX_SYSTEM_TOKENS,
    TRAINDEV_FOLDS_4,
    BeamCandidate,
    CalibrationPrediction,
    InnerValidationPredictions,
    SidecarConfidence,
    SystemPromptState,
    build_w7_sidecar_rows,
    decode_piece_systems_blind,
    decode_system_constrained,
    emit_aligned_discrepancy_rows,
    fit_nested_calibrators,
    validate_w7_sidecar_row,
)
from sheet_music_omr.training.models import (
    CATEGORY_EXCLUSIVE_TOKEN_IDS,
    DetectorTarget,
    TextScore2DomainTokenizer,
    apply_category_supervision_logit_mask,
    compute_class_ap75,
    compute_stride32_resize,
    evaluate_operating_point,
    format_arm_a_chat,
    format_arm_b_prompt_prefix,
    generate_autoregressive_candidates,
    load_packaged_crop_manifest,
    load_packaged_page_boxes_manifest,
    postprocess_detector_raw_outputs,
    run_arm_a_manifest_train_step,
    run_arm_b_manifest_train_step,
    run_w6_neural_cpu_smoke,
    unpermute_spatial_merge_2x2,
    verovio_row_eligible_for_bpe_loss,
)
from sheet_music_omr.writer import write_carry_state

INITIAL = (
    '{"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave",'
    '"accidentals":[],"open":[]}'
)
TOKENIZER = TextScore2DomainTokenizer()


def _system(
    index: int,
    body: str,
    *,
    prior: str | None = None,
    meter: str = "2/4",
    page: int = 1,
) -> str:
    carry = (
        INITIAL if prior is None else write_carry_state(carry_out(_doc(prior)))
    )
    return (
        f"format: text-score/2\nkey: 0\ntime: {meter}\nclefs: G2 F4\n"
        f"carry: octave\npage: {page}\nsystem: {index}\ncarry-in: {carry}\n"
        f"{body}\n"
    )


def _doc(text: str) -> SystemDocument:
    doc = parse_document(text)
    assert isinstance(doc, SystemDocument)
    return doc


def _beam(text: str, score: float = -1.0) -> BeamCandidate:
    return BeamCandidate(score, TOKENIZER.encode(text))


def _decode(text: str, *, final: bool = True):
    return decode_system_constrained(
        [_beam(text)], expected_page_system=(1, 1), is_final_system=final
    )


def _follow(previous: str, *texts: str, final: bool):
    beams = [_beam(text, -i - 1) for i, text in enumerate(texts)]
    return decode_system_constrained(
        reversed(beams),
        [_doc(previous)],
        expected_page_system=(1, 2),
        is_final_system=final,
    )


def test_open_arc_prefix_resolves_in_strict_assembly() -> None:
    """Keep an open arc at the prefix, then resolve it as a slur."""
    first = _system(1, "m 1\n1.1: C4{arc@r1:start}/2")
    second = _system(2, "m 2\n1.1: D4/2[rel=slur@r1:end]", prior=first)
    piece = decode_piece_systems_blind(
        [(1, 1), (1, 2)],
        lambda p: [_beam((first, second)[p.page_system_tuple[1] - 1])],
    )
    a, b = piece.systems
    assert a.valid
    assert b.valid
    assert a.document
    assert b.document
    assert a.carry_out_text
    assert '"kind":"arc"' in a.carry_out_text
    joined = assemble([a.document, b.document])
    assert joined.status == AssemblyStatus.SUCCESS
    assert joined.text
    assert "rel=slur@r1:start" in joined.text


def test_open_tie_and_other_voice_tremolo_prefix() -> None:
    """Defer only unseen tails and accept both open boundary relations."""
    first = _system(
        1,
        "m 1 to=1/4\n1.1: C4{tie@r1:start}/4\n"
        "2.1: C3/2[rel=tremolo@r2:two-start:2]\n2.2: F3/8 G3/8",
    )
    second = _system(
        2,
        "m 1 from=1/4\n1.1: C4{tie@r1:end}/4\n"
        "2.1: D3/2[rel=tremolo@r2:two-end:2]\n2.2: A3/4",
        prior=first,
    )
    a = _decode(first, final=False)
    b = _follow(first, second, final=True)
    assert a.valid
    assert b.valid
    assert a.document
    assert b.document
    assert a.carry_out_text
    assert '"offset":"1/4"' in a.carry_out_text
    assert assemble([a.document, b.document]).status == AssemblyStatus.SUCCESS


def test_prefix_closes_ending_before_unfinished_unbracketed_measure() -> None:
    """Normalize at the already-observed boundary even with an unseen tail."""
    a = _system(1, "m 1 right=repeat ending=1:start\n1.1: C4/2")
    b = _system(2, "m 2 to=1/4\n1.1: D4/4", prior=a)
    c = _system(3, "m 2 from=1/4\n1.1: E4/4", prior=b)
    prefix = _follow(a, b, final=False)
    assert prefix.valid
    joined = assemble([_doc(t) for t in (a, b, c)])
    assert joined.status == AssemblyStatus.SUCCESS
    assert joined.text
    assert "ending=1:single" in joined.text


def test_open_slur_cannot_hide_split_len_mismatch() -> None:
    """Reject the bad split length while the later slur end is unobserved."""
    first = _system(1, "m 1 to=1/4\n1.1: C4/4")
    good = _system(
        2, "m 1 from=1/4 to=3/8\n1.1: D4/8[rel=slur@r1:start]", prior=first
    )
    bad = good.replace("m 1 from=", "m 1 len=3/4 from=")
    assert not validate_document(_doc(bad))
    result = _follow(first, bad, good, final=False)
    assert result.valid
    assert result.text == good
    assert result.rejections[0].reason == "assembly_prefix_invalid"
    assert result.rejections[0].diagnostic_codes == ("assembly/split_len",)


def test_observed_gap_inside_unfinished_measure_rejected() -> None:
    """Do not defer a gap in a voice inside the observed prefix interval."""
    first = _system(1, "m 1 to=1/8\n1.1: C4/8\n2.1: C3/8")
    second = _system(
        2, "m 1 from=1/8 to=1/4\n1.1: D4/8[rel=slur@r1:start]", prior=first
    )
    assert not validate_document(_doc(second))
    result = _follow(first, second, final=False)
    assert not result.valid
    assert result.abstention_reason == "assembly_prefix_invalid"
    assert result.rejections[0].diagnostic_codes == ("assembly/measure_fill",)


def test_intermediate_volta_beam_fallback_before_next_system() -> None:
    """Reject an invalid adjacent ending despite a slur and unfinished bar."""
    first = _system(1, "m 1 right=repeat ending=1:start\n1.1: C4/2")
    good = _system(
        2,
        "m 2 ending=end\n1.1: D4/2[rel=slur@r1:start]\nm 3 to=1/4\n1.1: E4/4",
        prior=first,
    )
    bad = good.replace("ending=end", "ending=2:single")
    assert not validate_document(_doc(bad))
    result = _follow(first, bad, good, final=False)
    assert result.valid
    assert result.text == good
    assert "semantic/ending" in result.rejections[0].diagnostic_codes
    last = _system(3, "m 3 from=1/4\n1.1: F4/4[rel=slur@r1:end]", prior=good)
    assert assemble([_doc(t) for t in (first, good, last)]).status == (
        AssemblyStatus.SUCCESS
    )


def test_final_joined_fill_fallback_preserves_needs_person() -> None:
    """Use strict final assembly; accept a valid marker as needs-person."""
    first = _system(1, 'm 1 to=1/4\n1.1: <?>["p","pp"] C4/4\n2.1: C3/4')
    good = _system(2, "m 1 from=1/4\n1.1: D4/4\n2.1: D3/4", prior=first)
    bad = good.replace("\n2.1: D3/4", "")
    result = _follow(first, bad, good, final=True)
    assert result.valid
    assert result.needs_person
    assert result.text == good
    assert result.document is not None
    assert result.rejections[0].diagnostic_codes == ("assembly/measure_fill",)
    assert assemble([_doc(first), result.document]).status == (
        AssemblyStatus.NEEDS_PERSON
    )


def test_blind_header_propagates_unprinted_meter_to_following_system() -> None:
    """Use the accepted end header, not just carry-in or the initial meter."""
    first = _system(1, "m 1\n1.1: C4/2\nm 2 time=3/4\n1.1: E4/2.")
    second = _system(1, "m 3\n1.1: D4/2.", prior=first, meter="3/4", page=2)
    prompts = []

    def generate(index, prompt):
        prompts.append(prompt)
        return [_beam((first, second)[index])]

    piece = decode_piece_systems_blind([(1, 1), (2, 1)], generate)
    assert all(r.valid for r in piece.systems)
    assert piece.gate_eligible
    assert piece.conditioning == "blind"
    assert (
        prompts[0].incoming_header_state_text == prompts[0].carry_in_text == ""
    )
    assert prompts[1].page_system_tuple == (2, 1)
    assert (
        prompts[1].incoming_header_state_text
        == "key: 0\ntime: 3/4\nclefs: G2 F4"
    )
    assert prompts[1].carry_in_text == piece.systems[0].carry_out_text
    diagnostic = decode_piece_systems_blind(
        [(1, 1), (2, 1)],
        generate,
        use_oracle_carry=True,
        oracle_prompts=prompts[:2],
    )
    assert diagnostic.conditioning == "oracle_carry_conditioned"
    assert not diagnostic.gate_eligible


def test_arm_b_generation_conditions_on_predicted_unprinted_meter() -> None:
    """Include the predicted header without charging it to the output budget."""
    first = _system(1, "m 1\n1.1: C4/2.", meter="3/4")
    second = _system(2, "m 2\n1.1: E4/2.", prior=first, meter="3/4")
    bos, eos, sep = (
        TOKENIZER.bos_token_id,
        TOKENIZER.eos_token_id,
        TOKENIZER.prompt_sep_token_id,
    )

    def generate(index: int, prompt: SystemPromptState, carry_tokens: int):
        prefix = format_arm_b_prompt_prefix(prompt)
        prompt_ids = TOKENIZER.encode(prefix)
        assert TOKENIZER.decode(prompt_ids) == prefix
        expected = (first, second)[index]
        target_ids = TOKENIZER.encode(expected)
        boundary = len(prompt_ids) + 2

        def step(ids):
            assert ids[:boundary] == (bos, *prompt_ids, sep)
            conditioning = TOKENIZER.decode(ids[1 : boundary - 1])
            if index:
                assert conditioning == (
                    "page: 1\nsystem: 2\n"
                    f"carry-in: {write_carry_state(carry_out(_doc(first)))}\n"
                    "key: 0\ntime: 3/4\nclefs: G2 F4\n"
                )
            generated = len(ids) - boundary
            return {(*target_ids, eos)[generated]: -0.1}

        return generate_autoregressive_candidates(
            step,
            lambda token: TOKENIZER.decode([token]),
            bos_token_id=bos,
            eos_token_id=eos,
            prompt_ids=prompt_ids,
            prompt_sep_token_id=sep,
            carry_measure_tokens=carry_tokens,
            max_system_tokens=len(target_ids),
        )

    result = decode_piece_systems_blind([(1, 1), (1, 2)], generate)
    assert [system.text for system in result.systems] == [first, second]


def _exactly_n(tokens_in_measure: int, text: str) -> list[str]:
    header, body = text.split("\nm ", 1)
    return [
        header + "\n",
        "m",
        " " + body,
        "//",
        *("x" for _ in range(tokens_in_measure - 4)),
        "\n",
    ]


@pytest.mark.parametrize(("tail", "valid"), [(72, True), (80, False)])
def test_cumulative_measure_cap_across_systems_and_resets(tail, valid) -> None:
    """Reject token 193 online, and reset only after closure at exactly 192."""
    first = _system(1, "m 1 to=1/4\n1.1: C4/4")
    second = _system(2, "m 1 from=1/4\n1.1: D4/4", prior=first)
    third = _system(3, "m 2\n1.1: E4/2", prior=second)
    consumed = []

    def stream(max_steps, step_validator):
        assert max_steps == MAX_SYSTEM_TOKENS
        for token in _exactly_n(tail, second):
            step_validator(token)
            consumed.append(token)
            yield token

    candidates = (
        BeamCandidate(-1, _exactly_n(120, first)),
        BeamCandidate(-1, stream),
        BeamCandidate(-1, _exactly_n(192, third)),
    )
    result = decode_piece_systems_blind(
        [(1, 1), (1, 2), (1, 3)],
        lambda p: (candidates[p.page_system_tuple[1] - 1],),
    )
    a, b, c = result.systems
    assert a.valid
    assert a.carry_measure_tokens == 120
    assert b.valid is valid
    assert c.valid is valid
    assert len(consumed) == 73
    if valid:
        assert b.carry_measure_tokens == c.carry_measure_tokens == 0
    else:
        assert b.abstention_reason == "length_cap"
        assert c.abstention_reason == "upstream_carry_gap"


def test_system_cap_and_beam_limit_with_canonical_failure_reasons() -> None:
    """Check hard caps, canonical rejection mapping, and score ordering."""
    good = _system(1, "m 1\n1.1: C4/2")
    too_long = ["//", *("x" for _ in range(MAX_SYSTEM_TOKENS)), "\n" + good]
    invalid_duration = good.replace("C4/2", "C4/4")
    invalid_layout = good.replace("page: 1", "page: 2")
    result = decode_system_constrained(
        [
            _beam(good, -4),
            _beam(invalid_duration, -2),
            BeamCandidate(-1, too_long),
            _beam(invalid_layout, -3),
        ],
        expected_page_system=(1, 1),
        is_final_system=True,
    )
    assert not result.valid
    assert result.abstention_reason == "length_cap"
    assert [r.reason for r in result.rejections] == [
        "length_cap",
        "duration_invalid",
        "assembly_prefix_invalid",
    ]
    assert result.rejections[2].diagnostic_codes == ("assembly/order",)
    assembled_fixture = good.replace("page: 1\nsystem: 1\n", "").replace(
        f"carry-in: {INITIAL}\n", ""
    )
    assert _decode(assembled_fixture).abstention_reason == "parse_invalid"


def test_system_cap_is_inclusive_and_measure_cap_resets_inside_system() -> None:
    """Apply both caps independently to a continuous valid source stream."""
    good = _system(1, "m 1\n1.1: C4/2\nm 2\n1.1: D4/2")
    header, first = good.split("\nm 1", 1)
    first, second = first.split("\nm 2", 1)
    first_bar = ["m ", "1" + first + "\n//", *("x" for _ in range(189)), "\n"]
    second_bar = ["m", " 2" + second]
    n_padding = MAX_SYSTEM_TOKENS - len(first_bar) - len(second_bar) - 2
    tokens = ["//", *("x" for _ in range(n_padding)), "\n" + header + "\n"]
    tokens.extend(first_bar + second_bar)
    assert len(tokens) == MAX_SYSTEM_TOKENS
    result = decode_system_constrained(
        [BeamCandidate(-1, tokens)],
        expected_page_system=(1, 1),
        is_final_system=True,
    )
    assert result.valid
    assert result.document is not None
    assert [b.number for b in result.document.blocks] == [1, 2]
    assert result.carry_measure_tokens == 0


@pytest.mark.parametrize("carry_tokens", [0, 7])
def test_autoregressive_search_prunes_runaway_before_decoder(
    carry_tokens,
) -> None:
    """Keep the shorter beam without scoring a prefix beyond the bar cap."""
    good = _system(1, "m 1\n1.1: C4/2")
    header, body = good.split("\nm ", 1)
    pieces = ["", "", header + "\n", "m", " ", body, "1\n//", "x"]
    bos, eos, header_id, marker, space, valid, runaway, tail = range(
        len(pieces)
    )
    next_token = {bos: header_id, header_id: marker, marker: space, valid: eos}
    runaway_prefixes = []

    def step(ids):
        scores = np.full(len(pieces), -np.inf)
        if ids[-1] == space:
            scores[runaway], scores[valid] = -0.1, -2.0
        elif runaway in ids:
            # An unbounded search would eventually score a prefix beyond cap.
            remaining = MAX_MEASURE_TOKENS - carry_tokens
            assert len(ids) - 2 <= remaining
            runaway_prefixes.append(ids)
            scores[tail] = -0.1
        else:
            scores[next_token[ids[-1]]] = -0.1
        return scores

    candidates = generate_autoregressive_candidates(
        step,
        pieces.__getitem__,
        bos_token_id=bos,
        eos_token_id=eos,
        carry_measure_tokens=carry_tokens,
    )
    assert len(runaway_prefixes[-1]) - 2 == MAX_MEASURE_TOKENS - carry_tokens
    candidate = candidates[0]
    assert not candidate.length_capped
    assert any(c.length_capped for c in candidates)
    assert candidate.raw_logprob == pytest.approx(-2.4)
    piece = decode_piece_systems_blind([(1, 1)], lambda _: candidates)
    assert piece.gate_eligible
    assert piece.systems[0].valid
    assert piece.systems[0].text == good
    assert not piece.systems[0].rejections


def test_autoregressive_search_resets_measure_but_not_system_budget() -> None:
    """Allow a second measure after a full one, including split openers."""
    pieces = ["", "", "m", " 1", "\n", "m", " 2", "\n", "extra", " x"]
    bos, eos, extra, malformed = 0, 1, 8, 9
    prefixes = []

    def step(ids):
        assert malformed not in ids
        prefixes.append(ids)
        if ids[-1] == 5:
            return {malformed: -0.01, 6: -0.1}
        if len(ids) == 7:
            # The higher score cannot extend an already full system.
            return {extra: -0.1, eos: -1.0}
        return {len(ids) + 1: -0.1}

    candidates = generate_autoregressive_candidates(
        step,
        pieces.__getitem__,
        bos_token_id=bos,
        eos_token_id=eos,
        max_measure_tokens=4,
        max_system_tokens=6,
        carry_measure_tokens=1,
    )
    candidate = next(c for c in candidates if not c.length_capped)
    assert prefixes[-1] == (bos, 2, 3, 4, 5, 6, 7)
    assert candidate.tokens == tuple(pieces[2:extra])
    assert candidate.raw_logprob == pytest.approx(-1.6)


def _sidecar(result, index=1, **opts):
    return build_w7_sidecar_rows(
        result,
        run_id="smoke",
        sample_or_piece_id="gold-07",
        page_index=1,
        system_index=index,
        page_system_bbox_px=(1, 2, 10, 20),
        **opts,
    )


@pytest.mark.parametrize("cap", ["measure", "system"])
def test_online_split_measure_cap_reaches_abstention_sidecar(cap) -> None:
    """Carry the open measure into online search and retain cap failures."""
    first = _system(1, "m 1 to=1/4\n1.1: C4/4")
    second = _system(2, "m 1 from=1/4\n1.1: E4/4", prior=first)
    pieces = (
        _exactly_n(80, second)
        if cap == "measure"
        else ["//", *(["x"] * MAX_SYSTEM_TOKENS)]
    )
    max_steps = (
        MAX_MEASURE_TOKENS - 120 + 1 if cap == "measure" else MAX_SYSTEM_TOKENS
    )
    visited = []

    def generate(index, prompt, carry_tokens):
        assert prompt.page_system_tuple == (1, index + 1)
        assert carry_tokens == (0, 120)[index]
        if not index:
            return [BeamCandidate(-1, _exactly_n(120, first))]

        def step(ids):
            assert len(ids) - 1 <= max_steps
            visited.append(len(ids) - 1)
            return {len(ids) + 1: -0.1}

        candidates = generate_autoregressive_candidates(
            step,
            lambda token: pieces[token - 2],
            bos_token_id=0,
            eos_token_id=1,
            carry_measure_tokens=carry_tokens,
        )
        (candidate,) = candidates
        assert candidate.tokens == tuple(pieces[: max_steps + 1])
        return candidates

    piece = decode_piece_systems_blind([(1, 1), (1, 2)], generate)
    first_result, rejected = piece.systems
    assert first_result.valid
    assert first_result.carry_measure_tokens == 120
    assert visited[-1] == max_steps
    assert not rejected.valid
    assert rejected.abstention_reason == "length_cap"
    assert [item.reason for item in rejected.rejections] == ["length_cap"]
    (row,) = _sidecar(rejected, 2, split="synth_val")
    validate_w7_sidecar_row(row)
    assert row["abstention_reason"] == "length_cap"
    assert row["mandatory_abstention_review"] is True


def test_failed_piece_stops_generation_and_emits_single_sentinels() -> None:
    """Review abstentions separately from a measure-based flag budget."""

    def generate(prompt):
        assert prompt.page_system_tuple == (1, 1)
        return [_beam("broken"), _beam("bad", -2), _beam("invalid", -3)]

    piece = decode_piece_systems_blind([(1, 1), (1, 2), (1, 3)], generate)
    assert [r.abstention_reason for r in piece.systems] == [
        "parse_invalid",
        "upstream_carry_gap",
        "upstream_carry_gap",
    ]
    for i, result in enumerate(piece.systems, 1):
        assert not result.valid
        (row,) = _sidecar(result, i, split="synth_val")
        for field in (
            "pred_measure_ordinal",
            "staff",
            "text_score_v2_slice",
            "flagged_at_20pct",
            "aligned_gold_measure_ordinal",
        ):
            assert row[field] is None
        assert row["system_index"] == i
        assert row["mandatory_abstention_review"] is True
        assert row["calibrated_confidence"] == 0.0
        assert row["abstention_reason"] == result.abstention_reason
        with pytest.raises(ValueError, match="payload"):
            validate_w7_sidecar_row({**row, "flagged_at_20pct": False})


def test_sidecar_uses_exact_draft_flag_not_gold_aggregation() -> None:
    """One flagged draft bar must not flag neighboring predictions."""
    result = _decode(
        _system(1, "m 1\n1.1: C4/2\n2.1: C3/2\nm 2\n1.1: D4/2\nm 3\n1.1: E4/2")
    )
    alignment = {
        "pred_to_draft_ordinal": {1: 42, 2: 43},
        "pred_to_gold_ordinal": {1: 12, 2: 12},
        "budgeted_draft_flags": {("gold-07", 42): False, ("gold-07", 43): True},
    }
    rows = _sidecar(
        result,
        split="TRAIN_DEV",
        confidences={
            (1, 2): SidecarConfidence(0.75, coverage_mask={"clef": False})
        },
        **alignment,
    )
    assert [(r["pred_measure_ordinal"], r["staff"]) for r in rows] == [
        (1, 1),
        (1, 2),
        (2, 1),
        (3, 1),
    ]
    assert [r["flagged_at_20pct"] for r in rows] == [False, False, True, None]
    assert all(r["mandatory_abstention_review"] is False for r in rows)
    assert rows[1]["text_score_v2_slice"] == "m 1\n2.1: C3/2\n"
    assert rows[1]["calibrated_confidence"] == 0.75
    assert rows[1]["coverage_mask"] == {"clef": False}
    for row in _sidecar(result, split="synth_val", **alignment):
        assert (
            row["flagged_at_20pct"]
            is row["aligned_gold_measure_ordinal"]
            is None
        )


def test_discrepancy_log_keeps_existing_schema_and_real_split_only() -> None:
    """Validate existing scorer rows; never issue synthetic discrepancies."""
    row: dict[str, object] = {
        "schema": "omr-discrepancy-log-v1",
        "run_id": "run",
        "reader_id": "w6",
        "split": "TRAIN_DEV",
        "gold_id": "gold-07",
        "group": "G5",
        "page_index": 1,
        "system_index": 1,
        "measure_ordinal": 12,
        "draft_measure_ordinal": 42,
        "insbar_anchor_draft_ordinal": None,
        "delbar_attributed_gold_ordinal": None,
        "parse_valid": True,
        "capability_excluded_categories": [],
        "open_reading_masked_categories": [],
        "converged_aspects": [],
        "layer0_categories": {},
        "layer1_a_categories": {},
        "layer2_invocab_categories": {},
        "layout_c_count": 0,
        "raw_confidence": None,
        "calibrated_confidence": 0.75,
        "second_reader_disagreement_count": 1,
        "second_reader_score": 0.2,
        "flagged_at_20pct": True,
        "archive_round_counts": [],
    }
    assert emit_aligned_discrepancy_rows(
        [row], split="TRAIN_DEV", sample_or_piece_id="gold-07"
    ) == [row]
    with pytest.raises(ValueError, match="aligned real gold"):
        emit_aligned_discrepancy_rows(
            [row], split="synth_val", sample_or_piece_id="gold-07"
        )
    with pytest.raises(ValueError, match="row keys"):
        emit_aligned_discrepancy_rows(
            [{**row, "staff": 1}],
            split="TRAIN_DEV",
            sample_or_piece_id="gold-07",
        )


def _predictions(fold: int) -> list[CalibrationPrediction]:
    return [
        *(
            CalibrationPrediction(-2 + fold * 0.1, has_error=True)
            for _ in range(10 + fold)
        ),
        *(
            CalibrationPrediction(2 + fold * 0.1, has_error=False)
            for _ in range(12)
        ),
        CalibrationPrediction(-4, has_error=True, category="ottava"),
        *(
            CalibrationPrediction(
                -4, has_error=True, category="ottava", supervised=False
            )
            for _ in range(20)
        ),
    ]


def test_preregistered_traindev_folds_are_group_disjoint() -> None:
    """Retain every preregistered plate group with the exact complements."""
    observed = [
        (
            f.plate_groups,
            f.pieces,
            f.pages,
            f.systems,
            f.measures,
            f.real_train_systems,
        )
        for f in TRAINDEV_FOLDS_4
    ]
    assert observed == [
        (("G5",), 5, 17, 79, 339, 244),
        (("G6",), 5, 21, 82, 294, 241),
        (("G1", "G2"), 3, 17, 87, 487, 236),
        (("G4", "G7"), 3, 14, 75, 481, 248),
    ]
    assert sum(f.systems for f in TRAINDEV_FOLDS_4) == 323
    assert [f.piece_ids for f in TRAINDEV_FOLDS_4] == [
        ("gold-07", "gold-08", "gold-09", "gold-11", "gold-12"),
        ("gold-16", "gold-17", "gold-19", "gold-20", "gold-21"),
        ("gold-10", "gold-14", "gold-15"),
        ("gold-13", "gold-22", "gold-23"),
    ]
    pieces = [piece for f in TRAINDEV_FOLDS_4 for piece in f.piece_ids]
    assert len(pieces) == len(set(pieces)) == 16
    assert "gold-18" not in pieces


@pytest.mark.parametrize("method", ["temperature", "platt"])
def test_synthetic_calibration_never_fits_on_its_outer_fold(method) -> None:
    """Change held-out scores and verify only other folds' fits can change."""
    folds = {f: _predictions(f) for f in range(4)}
    original = fit_nested_calibrators(folds, method=method)
    folds[0] = [CalibrationPrediction(0.5, has_error=True) for _ in range(17)]
    changed = fit_nested_calibrators(folds, method=method)
    assert original.by_fold[0] == changed.by_fold[0]
    assert original.by_fold[1] != changed.by_fold[1]
    assert original.out_of_fold[0] != changed.out_of_fold[0]
    assert original.selection_conditional
    assert original.metrics_by_category["overall"]["auroc"] == pytest.approx(
        1.0
    )
    supported = original.by_fold[0]["overall"]
    assert supported.error_instances == 36
    assert supported.slope is not None
    z = supported.slope * -0.5 + supported.intercept
    assert supported.predict(-0.5) == pytest.approx(1 / (1 + math.exp(-z)))
    if method == "temperature":
        assert supported.temperature == pytest.approx(1 / supported.slope)
    unsupported = original.by_fold[0]["ottava"]
    assert unsupported.status == "insufficient_support"
    assert unsupported.error_instances == 3
    assert unsupported.predict(1.0) is None


def test_replay_rejects_outer_weight_leak_and_incomplete_pairs() -> None:
    """Authenticate each inner training complement before fitting replay."""
    folds = frozenset(range(4))
    inner = {
        (f, g): InnerValidationPredictions(folds - {f, g}, _predictions(g))
        for f in folds
        for g in folds - {f}
    }
    replay = fit_nested_calibrators(
        replay_predictions=inner, method="temperature"
    )
    assert replay.selection_conditional
    assert replay.mode == "replay"
    assert replay.by_fold[0]["overall"].error_instances == 36
    leaked = {
        **inner,
        (0, 1): replace(inner[(0, 1)], trained_folds=folds - {1}),
    }
    with pytest.raises(ValueError, match="leaked"):
        fit_nested_calibrators(replay_predictions=leaked)
    del inner[(3, 2)]
    with pytest.raises(ValueError, match="held-out pairs"):
        fit_nested_calibrators(replay_predictions=inner)


@pytest.mark.parametrize(
    ("original", "resized"),
    [((2300, 3185), (736, 1024)), ((600, 1024), (608, 1024))],
)
def test_stride32_unpadded_resize(original, resized) -> None:
    """Align the dimensions without padding or forcing squares."""
    assert compute_stride32_resize(*original) == resized


def test_independent_detector_top_k_ties_and_original_frame_clamping() -> None:
    """Protect system proposals and stable query order from measure scores."""
    logits = np.zeros((150, 2))
    logits[:100, 1] = 10
    logits[140, 0] = 0.5
    boxes = np.tile([0.5, 0.5, 0.5, 0.5], (150, 1))
    boxes[140] = [0.1, 0.9, 0.6, 0.8]
    boxes[0] = [-1.0, 0.5, 0.25, 0.25]
    preds = postprocess_detector_raw_outputs(logits, boxes, 100, 200)
    system = [p for p in preds if p.class_name == "system"]
    measure = [p for p in preds if p.class_name == "measure"]
    assert [p.query_idx for p in system] == [140, *range(1, 31)]
    assert [p.query_idx for p in measure] == list(range(1, 128))
    assert system[0].bbox == pytest.approx((0, 100, 40, 200))
    assert system[0].probability < measure[0].probability


def test_ap75_uses_low_scores_which_operating_point_excludes() -> None:
    """Use ranked, page-local one-to-one AP before confidence thresholding."""
    box = [0.5, 0.5, 0.5, 0.5]
    weaker_correct = postprocess_detector_raw_outputs(
        [[-2, -3], [-3, -3]], [box, box], 100, 100, k_measure=0, page_id="p2"
    )
    false_page = postprocess_detector_raw_outputs(
        [[3, -3]], [box], 100, 100, k_measure=0, page_id="p1"
    )
    targets = [DetectorTarget("p2", (25, 25, 75, 75))]
    predictions = weaker_correct + false_page
    assert compute_class_ap75(predictions, targets) == pytest.approx(0.5)
    selected = evaluate_operating_point(predictions, targets, threshold=0.5)
    assert selected.recall == selected.precision == 0
    all_matches = evaluate_operating_point(predictions, targets, threshold=0)
    assert all_matches.true_positives == 1
    assert all_matches.false_positives == 2
    assert all_matches.recall == 1


def test_monkey_unpermutes_rectangular_block_grid_and_batches() -> None:
    """Reconstruct raster rather than just reshaping in block order."""
    raster = np.arange(2 * 4 * 6 * 3).reshape(2, 4, 6, 3)
    blocked = np.stack(
        [
            raster[:, y : y + 2, x : x + 2].reshape(2, 4, 3)
            for y in range(0, 4, 2)
            for x in range(0, 6, 2)
        ],
        axis=1,
    ).reshape(2, 24, 3)
    np.testing.assert_array_equal(
        unpermute_spatial_merge_2x2(blocked, 4, 6), raster
    )
    with pytest.raises(ValueError, match="even grid"):
        unpermute_spatial_merge_2x2(blocked, 3, 8)


def test_lexical_supervision_excludes_exact_tokens_and_shared_bpe() -> None:
    """Reject overlapping BPE; keep header clefs and free text supervised."""
    categories = {
        "clef": "<clef G2>",
        "cross_staff": "C4/8[xs2]",
        "grace": "C4/8[grace/]",
        "hairpin": "<cresc(>[rel=hairpin@r1:start:cresc]",
        "ottava": "<8va(>[rel=ottava@r1:start:8va]",
    }
    ids = set().union(*CATEGORY_EXCLUSIVE_TOKEN_IDS.values())
    assert len(ids) == sum(map(len, CATEGORY_EXCLUSIVE_TOKEN_IDS.values()))
    assert len(TOKENIZER) < 512
    for category, spelling in categories.items():
        enc = set(TOKENIZER.encode(spelling)) & ids
        assert enc
        assert enc <= CATEGORY_EXCLUSIVE_TOKEN_IDS[category]
        assert not set(TOKENIZER.encode(f'heading: "{spelling} ♫"')) & ids
    text = "clefs: G2 F4\n" + " ".join(categories.values()) + '\nheading: "♫"\n'
    assert (
        TOKENIZER.decode(TOKENIZER.encode(text, add_special_tokens=True))
        == text
    )
    assert not set(TOKENIZER.encode("clefs: G2 F4\n")) & ids
    off = dict.fromkeys(categories, False)
    assert not verovio_row_eligible_for_bpe_loss(off)
    assert verovio_row_eligible_for_bpe_loss(off, CATEGORY_EXCLUSIVE_TOKEN_IDS)
    shared = next(iter(CATEGORY_EXCLUSIVE_TOKEN_IDS["clef"]))
    assert not verovio_row_eligible_for_bpe_loss(
        off, CATEGORY_EXCLUSIVE_TOKEN_IDS, shared_token_ids=[shared]
    )
    assert not verovio_row_eligible_for_bpe_loss(
        off, {**CATEGORY_EXCLUSIVE_TOKEN_IDS, "unmasked_bpe_use": [shared]}
    )


def test_supervision_mask_removes_disabled_softmax_and_negative_gradient() -> (
    None
):
    """Verify exact zero mass and finite-difference loss derivative."""
    off = dict.fromkeys(CATEGORY_EXCLUSIVE_TOKEN_IDS, False)
    disabled = sorted(set().union(*CATEGORY_EXCLUSIVE_TOKEN_IDS.values()))
    target = TOKENIZER.encode("clefs: ")[0]
    logits = np.linspace(-2, 3, len(TOKENIZER))[None, :]

    def loss(raw):
        masked = apply_category_supervision_logit_mask(raw, off)
        return np.logaddexp.reduce(masked[0]) - masked[0, target]

    masked = apply_category_supervision_logit_mask(logits, off)
    assert np.isneginf(masked[:, disabled]).all()
    probs = np.exp(masked - np.logaddexp.reduce(masked, axis=-1, keepdims=True))
    assert np.count_nonzero(probs[:, disabled]) == 0
    perturbed = logits.copy()
    perturbed[:, disabled] += 10
    assert loss(perturbed) == loss(logits)
    perturbed[:, target] += 0.1
    assert loss(perturbed) < loss(logits)
    np.testing.assert_array_equal(
        apply_category_supervision_logit_mask(logits, dict.fromkeys(off, True)),
        logits,
    )


def test_qwen35_chat_template_disables_thinking() -> None:
    """Pass the no-thinking option to the processor, not to the model."""

    class Processor:
        def apply_chat_template(self, messages, **opts):
            assert messages == [{"role": "user", "content": "fixture"}]
            assert opts == {
                "tokenize": False,
                "add_generation_prompt": True,
                "enable_thinking": False,
            }
            return "assistant:"

    assert (
        format_arm_a_chat(
            "qwen3_5", Processor(), [{"role": "user", "content": "fixture"}]
        )
        == "assistant:"
    )


def test_arm_a_manifest_step_skips_unisolated_verovio_supervision() -> None:
    """Do not open the crop or use the model without audited BPE IDs."""
    row: dict[str, object] = {
        "engine": "verovio",
        "category_supervision_mask": {"hairpin": False},
    }
    report = run_arm_a_manifest_train_step(None, None, "qwen3_vl", row, Path())
    assert report["skipped"] is True
    assert report["loss"] is None


@pytest.fixture
def crop_manifest(tmp_path):
    """Write two PNGs in the dataset root rather than in the split."""
    root = tmp_path / "synthetic_dataset"
    split = root / "w6-smoke" / "synth_val"
    split.mkdir(parents=True)
    rows = []
    for index in range(2):
        relative = f"crop-{index}.png"
        image_path = root / relative
        Image.new("RGB", (16, 8), "white").save(image_path)
        rows.append(
            {
                "schema": "omr-w6-crop-dataset-v1",
                "variant_id": "one",
                "system_index": index,
                "expected_systems": 2,
                "crop_relpath": relative,
                "sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(),
                "crop_height": 8,
                "crop_width": 16,
            }
        )
    return split, rows


def _write_rows(path, rows):
    path.write_text(
        "".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8"
    )


def test_arm_b_training_masks_prompt_and_disabled_categories(
    crop_manifest,
) -> None:
    """Train the first output at SEP while ignoring prompt-only targets."""
    torch = pytest.importorskip("torch")
    split, rows = crop_manifest
    first = _system(1, "m 1\n1.1: C4/2.", meter="3/4")
    target = _system(2, "m 2\n1.1: E4/2.", prior=first, meter="3/4")
    carry = write_carry_state(carry_out(_doc(first)))
    header = "key: 0\ntime: 3/4\nclefs: G2 F4"
    row = {
        **rows[1],
        "page_system_tuple": (1, 2),
        "carry_in_text": carry,
        "incoming_header_state_text": header,
        "target_text_v2": target,
        "category_supervision_mask": dict.fromkeys(
            CATEGORY_EXCLUSIVE_TOKEN_IDS, False
        ),
    }
    prefix = [
        TOKENIZER.bos_token_id,
        *TOKENIZER.encode(
            format_arm_b_prompt_prefix(SystemPromptState((1, 2), carry, header))
        ),
        TOKENIZER.prompt_sep_token_id,
    ]
    target_ids = TOKENIZER.encode(target)
    observed = []

    class FixtureModel(torch.nn.Embedding):
        def forward(self, _pixels, tokens):
            assert tokens.tolist() == [[*prefix, *target_ids]]
            logits = super().forward(tokens)
            observed.append(logits)
            return logits

    model = FixtureModel(len(TOKENIZER), len(TOKENIZER))
    run_arm_b_manifest_train_step(model, TOKENIZER, row, split.parent.parent)
    (logits,) = observed
    disabled = sorted(set().union(*CATEGORY_EXCLUSIVE_TOKEN_IDS.values()))
    assert torch.count_nonzero(logits.grad[:, : len(prefix) - 1]) == 0
    assert torch.count_nonzero(logits.grad[..., disabled]) == 0
    assert logits.grad[0, len(prefix) - 1, target_ids[0]] < 0
    assert logits.grad[0, -1, TOKENIZER.eos_token_id] < 0


def test_packaged_crop_manifest_reads_complete_dataset_relative_sequence(
    crop_manifest,
) -> None:
    """Preserve both systems from the same complete variant."""
    split, rows = crop_manifest
    _write_rows(split / "crops.jsonl", rows)
    assert load_packaged_crop_manifest(split) == rows


@pytest.mark.parametrize(
    ("invalid_fields", "message"),
    [
        ({"schema": "another-schema"}, "omr-w6-crop-dataset-v1"),
        ({"system_index": 0}, "sequence"),
        ({"expected_systems": 3}, "sequence"),
        ({"sha256": "wrong-digest"}, "SHA-256"),
        ({"crop_height": 9}, "dimensions"),
        ({"crop_relpath": "missing.png"}, "missing"),
    ],
)
def test_packaged_crop_manifest_rejects_bad_rows(
    crop_manifest, invalid_fields, message
) -> None:
    """Reject row corruption before providing any training inputs."""
    split, rows = crop_manifest
    _write_rows(split / "crops.jsonl", [rows[0], rows[1] | invalid_fields])
    with pytest.raises(ValueError, match=message):
        load_packaged_crop_manifest(split)


def test_packaged_crop_manifest_rejects_truncated_variant(
    crop_manifest,
) -> None:
    """Fail closed when the manifest ends partway through a variant."""
    split, rows = crop_manifest
    _write_rows(split / "crops.jsonl", rows[:1])
    with pytest.raises(ValueError, match="incomplete"):
        load_packaged_crop_manifest(split)


def test_packaged_page_boxes_convert_to_targets_and_coco(tmp_path) -> None:
    """Preserve detector classes and use COCO width, height, and area."""
    page = {
        "schema": "omr-w6-page-boxes-v1",
        "page_id": "page",
        "page_width_px": 32,
        "page_height_px": 16,
        "boxes": [
            {"class_id": 0, "box_px": [2, 4, 22, 12]},
            {"class_id": 1, "box_px": [2, 4, 12, 12]},
        ],
    }
    manifest = tmp_path / "page-boxes.jsonl"
    _write_rows(manifest, [page])
    [loaded] = load_packaged_page_boxes_manifest(tmp_path)
    assert loaded["targets"] == {
        0: [DetectorTarget("page", (2, 4, 22, 12))],
        1: [DetectorTarget("page", (2, 4, 12, 12))],
    }
    assert loaded["annotations"] == {
        "image_id": 0,
        "annotations": [
            {
                "bbox": [2, 4, 20, 8],
                "category_id": 0,
                "area": 160,
                "iscrowd": 0,
            },
            {"bbox": [2, 4, 10, 8], "category_id": 1, "area": 80, "iscrowd": 0},
        ],
    }
    for bad_page, message in (
        (page | {"schema": "wrong"}, "omr-w6-page-boxes-v1"),
        (
            page | {"boxes": [{"class_id": 2, "box_px": [2, 4, 12, 12]}]},
            "class_id",
        ),
        (
            page | {"boxes": [{"class_id": 0, "box_px": [2, 4, 33, 12]}]},
            "outside",
        ),
    ):
        _write_rows(manifest, [bad_page])
        with pytest.raises(ValueError, match=message):
            load_packaged_page_boxes_manifest(tmp_path)


def _assert_smoke_datasets(work_dir: Path) -> None:
    dataset = work_dir / "runs/w6-slice-a-smoke/datasets/fixture-crops-full"
    val_dir = dataset / "synth_val"
    train_dir = dataset / "synth_train"
    assert len(load_packaged_page_boxes_manifest(val_dir)) == 1
    assert len(load_packaged_page_boxes_manifest(train_dir)) == 1
    first, second = load_packaged_crop_manifest(val_dir)
    assert first["engine"] == second["engine"] == "musescore"
    assert first["incoming_header_state_text"] == ""
    assert (
        second["incoming_header_state_text"]
        == "key: 0\ntime: 3/4\nclefs: G2 F4"
    )
    (verovio,) = load_packaged_crop_manifest(train_dir)
    assert verovio["engine"] == "verovio"
    mask = cast("dict[str, bool]", verovio["category_supervision_mask"])
    assert {name for name, enabled in mask.items() if not enabled} == set(
        CATEGORY_EXCLUSIVE_TOKEN_IDS
    )


@pytest.mark.parametrize("with_metadata", [False, True])
def test_neural_smoke_emits_calibrated_predictions_from_resumed_models(
    tmp_path,
    with_metadata,
) -> None:
    """Exercise the locked interpreter and durable W7 output end to end."""
    downloads = os.environ.get("W6_SMOKE_DOWNLOADS")
    if not downloads:
        pytest.skip("Set W6_SMOKE_DOWNLOADS to staged Slice A assets")
    root = Path(downloads)
    report = cast(
        "dict[str, Any]",
        run_w6_neural_cpu_smoke(
            root / "w6-train-venv/bin/python",
            tmp_path,
            root / "w6-model-meta" if with_metadata else None,
        ),
    )
    for flag in (
        "manifest_loader_verified",
        "constrained_decode_verified",
        "w7_sidecar_verified",
    ):
        assert report[flag] is True
    assert report["processor_verified"] is with_metadata
    _assert_smoke_datasets(tmp_path)
    processors = report["processors"]
    if with_metadata:
        assert processors["qwen3_vl"]["pixel_values_shape"][1:] == [1536]
        assert processors["qwen3_5"]["input_ids_shape"][0] == 1
        assert processors["paddleocr_vl"]["pixel_values_shape"][-3:] == [
            3,
            14,
            14,
        ]
    else:
        assert processors == {}
    assert report["arm_a"]["qwen3_5"]["enable_thinking"] is False
    for model in report["arm_a"].values():
        assert model["checkpoint_reloaded"] is True
        assert model["skipped"] is False
        assert math.isfinite(model["loss"])
        assert model["loss"] > 0
    for model in report["arm_b"].values():
        assert model["checkpoint_reloaded"] is True
        assert model["disabled_category_gradients_zero"] is True
        assert math.isfinite(model["loss"])
        assert model["loss"] > 0
        assert math.isfinite(model["continuation_loss"])
        assert model["continuation_loss"] > 0
    for family, pages in report["detectors"].items():
        assert (tmp_path / f"{family}.pt").stat().st_size > 0
        assert [p["resized_size"] for p in pages] == [[736, 1024], [608, 1024]]
        for page in pages:
            assert page["checkpoint_reloaded"] is True
            assert page["eval_inference_verified"] is True
            assert all(
                page["prediction_counts"][kind] > 0
                for kind in ("system", "measure")
            )
    rows = [
        json.loads(line)
        for line in (tmp_path / "w7-sidecar.jsonl")
        .read_text(encoding="utf-8")
        .splitlines()
    ]
    assert len(rows) == 10
    for name in (*report["arm_a"], *report["arm_b"]):
        systems = [row for row in rows if row["run_id"] == name]
        assert [row["page_index"] for row in systems] == [1, 1]
        assert [row["system_index"] for row in systems] == [1, 2]
        for system, row in enumerate(systems, 1):
            validate_w7_sidecar_row(row)
            assert row["text_score_v2_slice"] == f"m {system}\n1.1: C4/2.\n"
            assert json.loads(row["carry_out_text"])["measure"] == system + 1
            assert row["raw_logprob"] < 0
            assert 0 < row["calibrated_confidence"] < 1
            assert row["aligned_gold_measure_ordinal"] is None
    discrepancy = json.loads(
        (tmp_path / "w7-discrepancies.jsonl").read_text(encoding="utf-8")
    )
    assert emit_aligned_discrepancy_rows(
        [discrepancy], split="TRAIN_DEV", sample_or_piece_id="gold-07"
    ) == [discrepancy]
