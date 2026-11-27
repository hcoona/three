"""Isolated offline GPU training and inference worker for W6 Slice B."""

from __future__ import annotations

import json
import math
import os
import random
import sys
import time
from importlib import import_module
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

import numpy as np
from PIL import Image, ImageOps

from sheet_music_omr.training import models as m_mod
from sheet_music_omr.training.decoding import (
    BEAM_SIZE,
    MAX_MEASURE_TOKENS,
    MAX_SYSTEM_TOKENS,
    BeamCandidate,
    SystemPromptState,
    decode_piece_systems_blind,
)
from sheet_music_omr.training.models import (
    _ARM_A_NAMES,
    DETECTOR_CLASSES,
    ArmAFamily,
    ArmBDecoderConfig,
    DetectorFamily,
    DetectorPrediction,
    DetectorTarget,
    TextScore2DomainTokenizer,
    _arm_a_base,
    _arm_a_batch,
    _arm_a_messages,
    _crop_tensor,
    _MeasureBudget,
    _tiny_vlm_config,
    _TinyArmAProcessor,
    build_arm_a_lora_model,
    build_page_detector,
    build_page_detector_processor,
    compute_class_ap75,
    evaluate_operating_point,
    format_arm_a_chat,
    format_arm_b_prompt_prefix,
    load_arm_a_processor,
    load_packaged_crop_manifest,
    load_packaged_page_boxes_manifest,
    load_pretrained_arm_a_and_processor,
    load_pretrained_page_detector,
    postprocess_detector_raw_outputs,
    run_arm_a_manifest_train_step,
    run_arm_b_manifest_train_step,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

_E0_SYNTAX_GUIDE = (
    "Output ONLY valid Text Score 2 system text (no prose, no markdown). "
    "Required header lines in order:\n"
    "format: text-score/2\n"
    "key: <fifths int>\n"
    "time: <num>/<den>\n"
    "clefs: G2 F4\n"
    "carry: octave\n"
    "page: <P>\n"
    "system: <S>\n"
    "carry-in: <exact JSON carry-in object>\n"
    "Followed by measure blocks `m <N>` and voice lines `1.1: ...` / "
    "`2.1: ...` using pitches like `C4/4`, rests `r/4`, whole-bar rest "
    "`R!/1`, chords `(C4 E4 G4)/2`, beams `[beam1=start]`..`[beam1=end]`.\n"
    "Minimal 1-bar example:\n"
    "format: text-score/2\n"
    "key: 0\n"
    "time: 4/4\n"
    "clefs: G2 F4\n"
    "carry: octave\n"
    "page: 1\n"
    "system: 1\n"
    'carry-in: {"measure":1,"offset":"0","event_counts":{},'
    '"accidental_scope":"octave","accidentals":[],"open":[]}\n'
    "m 1\n"
    "1.1: C4/4 D4/4 E4/2\n"
    "2.1: C3/1\n"
)


def _pad_image_to_multiple(img: Image.Image, multiple: int) -> Image.Image:
    w, h = img.size
    pad_w = (multiple - (w % multiple)) % multiple
    pad_h = (multiple - (h % multiple)) % multiple
    if pad_w == 0 and pad_h == 0:
        return img
    return ImageOps.expand(
        img, border=(0, 0, pad_w, pad_h), fill=(255, 255, 255)
    )


def _strip_markdown_fences(text: str) -> str:
    stripped = text.strip()
    if stripped.startswith("```"):
        lines = stripped.splitlines()
        if lines:
            lines = lines[1:]
        if lines and lines[-1].strip().startswith("```"):
            lines = lines[:-1]
        return "\n".join(lines).strip() + "\n"
    return text if text.endswith("\n") else text + "\n"


def _interleave_musescore_rows(
    rows: Sequence[dict[str, object]],
    seed: int,
    *,
    musescore_min_share: float = 0.25,
) -> list[dict[str, object]]:
    rng = random.Random(seed)  # noqa: S311
    ms_rows = [r for r in rows if r.get("engine") == "musescore"]
    vv_rows = [r for r in rows if r.get("engine") != "musescore"]
    rng.shuffle(ms_rows)
    rng.shuffle(vv_rows)
    if not ms_rows or not vv_rows:
        combined = [*ms_rows, *vv_rows]
        rng.shuffle(combined)
        return combined
    vv_per_ms = max(1, round((1.0 - musescore_min_share) / musescore_min_share))
    schedule: list[dict[str, object]] = []
    ms_idx = 0
    for i, vv in enumerate(vv_rows):
        if i % vv_per_ms == 0:
            schedule.append(ms_rows[ms_idx % len(ms_rows)])
            ms_idx += 1
        schedule.append(vv)
    while ms_idx < len(ms_rows):
        schedule.append(ms_rows[ms_idx])
        ms_idx += 1
    return schedule


def _build_arm_b_model(
    cfg: Mapping[str, Any],
    *,
    weights_dir: Path | None,
    checkpoint_dir: Path | None,
) -> tuple[Any, int]:
    st_mod = import_module("safetensors.torch")
    arch = str(cfg["arm_b_arch"])
    dec_cfg = ArmBDecoderConfig(
        layers=int(cfg.get("decoder_layers", 6)),
        d_model=int(cfg.get("decoder_d_model", 512)),
        nhead=int(cfg.get("decoder_nhead", 8)),
        d_ff=int(cfg.get("decoder_d_ff", 2048)),
    )
    if arch == "convnext_tiny":
        fuse = bool(cfg.get("fuse_stride8", True))
        model = m_mod.ConvNeXtStride8And16EncoderDecoder(
            decoder=dec_cfg,
            fuse_stride8=fuse,
            weights_path=weights_dir if checkpoint_dir is None else None,
            normalize_imagenet=bool(
                weights_dir is not None or checkpoint_dir is not None
            ),
        )
        pad_mult = 32
    elif arch == "monkeyocr_v2":
        vision = m_mod.MonkeyOCRv2VisionTrunk(
            embed_dim=int(cfg.get("vision_embed_dim", 384)),
            num_hidden_layers=int(cfg.get("vision_layers", 12)),
            num_attention_heads=int(cfg.get("vision_heads", 6)),
            intermediate_size=int(cfg.get("vision_ffn", 1536)),
            weights_path=weights_dir if checkpoint_dir is None else None,
            normalize_clip=bool(
                weights_dir is not None or checkpoint_dir is not None
            ),
        )
        model = m_mod.MonkeyOCRv2EncoderDecoder(vision=vision, decoder=dec_cfg)
        pad_mult = 28
    else:
        msg = f"unsupported Arm B architecture: {arch!r}"
        raise ValueError(msg)
    if checkpoint_dir is not None:
        state = st_mod.load_file(str(checkpoint_dir / "model.safetensors"))
        model.load_state_dict(state, strict=True)
    return model, pad_mult


def _load_arm_a_for_worker(
    cfg: Mapping[str, Any],
    *,
    for_training: bool,
) -> tuple[Any, Any, ArmAFamily]:
    torch = import_module("torch")
    family = cast("ArmAFamily", str(cfg["arm_a_family"]))
    weights_dir_raw = cfg.get("weights_dir")
    adapter_dir_raw = cfg.get("checkpoint_dir")
    tiny_fixture = bool(cfg.get("tiny_fixture", False))
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    dtype = (
        torch.bfloat16
        if (device.type == "cuda" and not tiny_fixture)
        else torch.float32
    )
    if tiny_fixture:
        tr_mod = import_module("transformers")
        meta_dir = (
            Path(str(weights_dir_raw)) if weights_dir_raw is not None else None
        )
        if meta_dir is not None and (meta_dir / "tokenizer.json").is_file():
            proc: Any = load_arm_a_processor(meta_dir)
            t_cfg = _tiny_vlm_config(
                family,
                getattr(tr_mod, _ARM_A_NAMES[family] + "Config"),
                pinned=json.loads(
                    (meta_dir / "config.json").read_text(encoding="utf-8")
                ),
                tokenizer=proc.tokenizer,
            )
        else:
            proc = _TinyArmAProcessor(family)
            t_cfg = None
        if adapter_dir_raw is not None:
            peft = import_module("peft")
            base = _arm_a_base(family, t_cfg).to(device=device, dtype=dtype)
            model = peft.PeftModel.from_pretrained(
                base,
                Path(str(adapter_dir_raw)),
                is_trainable=for_training,
                local_files_only=True,
            )
        elif bool(cfg.get("lora", True)):
            model = build_arm_a_lora_model(
                family,
                config=t_cfg,
                rank=int(cfg.get("lora_rank", 8)),
                include_vision=bool(cfg.get("include_vision_lora", False)),
            ).to(device=device, dtype=dtype)
        else:
            model = _arm_a_base(family, t_cfg).to(device=device, dtype=dtype)
        return model, proc, family

    if weights_dir_raw is None:
        msg = "non-fixture Arm A worker requires weights_dir"
        raise ValueError(msg)
    weights_dir = Path(str(weights_dir_raw))
    use_lora = bool(cfg.get("lora", True)) and adapter_dir_raw is None
    model, proc = load_pretrained_arm_a_and_processor(
        family,
        weights_dir,
        rank=int(cfg.get("lora_rank", 32)),
        include_vision=bool(cfg.get("include_vision_lora", False)),
        lora=use_lora,
        dtype=dtype,
        gradient_checkpointing=for_training,
    )
    model = model.to(device)
    if adapter_dir_raw is not None:
        peft = import_module("peft")
        if for_training and hasattr(model, "gradient_checkpointing_enable"):
            model.gradient_checkpointing_enable()
        if for_training and hasattr(model, "enable_input_require_grads"):
            model.enable_input_require_grads()
        model = peft.PeftModel.from_pretrained(
            model,
            Path(str(adapter_dir_raw)),
            is_trainable=for_training,
            local_files_only=True,
        )
    return model, proc, family


def _generate_arm_a_beams(  # noqa: C901, PLR0913
    model: Any,  # noqa: ANN401
    processor: Any,  # noqa: ANN401
    family: ArmAFamily,
    crop: Image.Image,
    prompt: SystemPromptState,
    *,
    carry_measure_tokens: int,
    zero_shot_e0: bool = False,
    max_new_tokens: int = MAX_SYSTEM_TOKENS,
    num_beams: int = BEAM_SIZE,
) -> list[BeamCandidate]:
    torch = import_module("torch")
    messages = _arm_a_messages(prompt)
    if zero_shot_e0:
        orig_txt = str(
            cast("list[dict[str, object]]", messages[0]["content"])[1]["text"]
        )
        initial_carry = (
            prompt.carry_in_text
            or '{"measure":1,"offset":"0","event_counts":{},'
            '"accidental_scope":"octave","accidentals":[],"open":[]}'
        )
        e0_txt = (
            f"{_E0_SYNTAX_GUIDE}\nNow transcribe this crop:\n{orig_txt}\n"
            f"Use exact carry-in line: carry-in: {initial_carry}"
        )
        messages = [
            {
                "role": "user",
                "content": [
                    {"type": "image"},
                    {"type": "text", "text": e0_txt},
                ],
            }
        ]
    chat = format_arm_a_chat(family, processor, messages)
    inputs = _arm_a_batch(processor, crop, chat, model)
    prompt_len = int(inputs["input_ids"].shape[-1])
    tok = getattr(processor, "tokenizer", None)
    eos_id = (
        int(tok.eos_token_id)
        if (tok is not None and tok.eos_token_id is not None)
        else int(getattr(processor, "eos_token_id", 2))
    )
    pad_id = (
        int(tok.pad_token_id)
        if (tok is not None and tok.pad_token_id is not None)
        else eos_id
    )
    model.eval()
    if not hasattr(model, "generate") or tok is None:
        # Fallback for synthetic _TinyArmAProcessor smoke tests
        with torch.inference_mode():
            logits = model(**inputs, use_cache=False, return_dict=True).logits[
                0, -1
            ]
            lp = float(logits.float().log_softmax(-1).max().item())
        return [BeamCandidate(lp, ["invalid"], length_capped=False)]

    with torch.inference_mode():
        gen = model.generate(
            **inputs,
            max_new_tokens=min(max_new_tokens, MAX_SYSTEM_TOKENS),
            num_beams=num_beams,
            num_return_sequences=num_beams,
            do_sample=False,
            use_cache=True,
            pad_token_id=pad_id,
            eos_token_id=eos_id,
            return_dict_in_generate=True,
            output_scores=True,
        )
        trans_scores = model.compute_transition_scores(
            gen.sequences,
            gen.scores,
            gen.beam_indices if num_beams > 1 else None,
            normalize_logits=True,
        )
    candidates: list[BeamCandidate] = []
    for b_idx in range(gen.sequences.shape[0]):
        seq_ids = [int(t) for t in gen.sequences[b_idx, prompt_len:].tolist()]
        step_lps = [float(s) for s in trans_scores[b_idx].tolist()]
        pieces: list[str] = []
        total_lp = 0.0
        capped = False
        mb = _MeasureBudget(carry_measure_tokens)
        hit_eos = False
        for step_i, tid in enumerate(seq_ids):
            lp = step_lps[step_i] if step_i < len(step_lps) else 0.0
            if math.isfinite(lp):
                total_lp += lp
            if tid == eos_id:
                hit_eos = True
                if not mb.can_end(MAX_MEASURE_TOKENS):
                    capped = True
                break
            if tid == pad_id and step_i > 0:
                continue
            piece = str(tok.decode([tid], clean_up_tokenization_spaces=False))
            pieces.append(piece)
            if len(pieces) > MAX_SYSTEM_TOKENS or not mb.advance(
                piece, len(pieces), MAX_MEASURE_TOKENS
            ):
                capped = True
                break
        if not hit_eos and len(pieces) >= min(
            max_new_tokens, MAX_SYSTEM_TOKENS
        ):
            capped = True
        if zero_shot_e0 and pieces and not capped:
            cleaned = _strip_markdown_fences("".join(pieces))
            pieces = [
                str(tok.decode([t], clean_up_tokenization_spaces=False))
                for t in tok.encode(cleaned, add_special_tokens=False)
            ]
        candidates.append(
            BeamCandidate(
                raw_logprob=total_lp if math.isfinite(total_lp) else -1e9,
                tokens=tuple(pieces),
                length_capped=capped,
            )
        )
    return candidates


def _generate_arm_b_beams(  # noqa: C901, PLR0913, PLR0915
    model: Any,  # noqa: ANN401
    tokenizer: TextScore2DomainTokenizer,
    crop: Image.Image,
    prompt: SystemPromptState,
    *,
    pad_multiple: int,
    carry_measure_tokens: int,
    max_new_tokens: int = MAX_SYSTEM_TOKENS,
    beam_width: int = BEAM_SIZE,
) -> list[BeamCandidate]:
    torch = import_module("torch")
    device = next(model.parameters()).device
    padded = _pad_image_to_multiple(crop, pad_multiple)
    pixels = _crop_tensor(padded).unsqueeze(0).to(device=device)
    prefix_ids = (
        tokenizer.bos_token_id,
        *tokenizer.encode(format_arm_b_prompt_prefix(prompt)),
        tokenizer.prompt_sep_token_id,
    )
    model.eval()
    use_amp = device.type == "cuda"
    with (
        torch.inference_mode(),
        torch.autocast(
            device_type=device.type, dtype=torch.bfloat16, enabled=use_amp
        ),
    ):
        memory = model.encode_visual_memory(pixels)
        active: list[
            tuple[tuple[int, ...], list[bytes], float, _MeasureBudget]
        ] = [(prefix_ids, [], 0.0, _MeasureBudget(carry_measure_tokens))]
        completed: list[BeamCandidate] = []
        cap_pruned: list[BeamCandidate] = []
        limit_steps = min(max_new_tokens, MAX_SYSTEM_TOKENS)
        for _ in range(limit_steps + 1):
            if not active:
                break
            seq_tensor = torch.tensor(
                [b[0] for b in active], dtype=torch.long, device=device
            )
            mem_batch = memory.expand(len(active), -1, -1, -1)
            logits = model.decoder(seq_tensor, mem_batch)[:, -1, :].float()
            logprobs = logits.log_softmax(dim=-1)
            topk_vals, topk_ids = torch.topk(
                logprobs, k=min(beam_width + 4, logits.shape[-1]), dim=-1
            )
            vals_cpu = topk_vals.cpu().tolist()
            ids_cpu = topk_ids.cpu().tolist()
            next_active: list[
                tuple[tuple[int, ...], list[bytes], float, _MeasureBudget]
            ] = []
            for (ids, raw_bytes_list, score, mb), row_vals, row_ids in zip(
                active, vals_cpu, ids_cpu, strict=True
            ):
                kept = 0
                for lp, tid in zip(row_vals, row_ids, strict=True):
                    if tid in (
                        tokenizer.bos_token_id,
                        tokenizer.prompt_sep_token_id,
                        tokenizer.pad_token_id,
                    ):
                        continue
                    new_score = score + float(lp)
                    if tid == tokenizer.eos_token_id:
                        try:
                            _ = b"".join(raw_bytes_list).decode("utf-8")
                            gen_ids = ids[len(prefix_ids) :]
                            completed.append(
                                BeamCandidate(
                                    new_score,
                                    gen_ids,
                                    length_capped=not mb.can_end(
                                        MAX_MEASURE_TOKENS
                                    ),
                                )
                            )
                            kept += 1
                        except UnicodeDecodeError:
                            pass
                        if kept >= beam_width:
                            break
                        continue
                    tb = tokenizer.token_bytes(tid)
                    next_bytes = [*raw_bytes_list, tb]
                    next_step = len(next_bytes)
                    mb_copy = _MeasureBudget(
                        mb.seed,
                        mb.count,
                        mb.line_start,
                        mb.pending,
                        mb.saw_space,
                        mb.last_charged,
                    )
                    piece_str = tb.decode("latin-1")
                    if next_step > MAX_SYSTEM_TOKENS or not mb_copy.advance(
                        piece_str, next_step, MAX_MEASURE_TOKENS
                    ):
                        cap_pruned.append(
                            BeamCandidate(new_score, (), length_capped=True)
                        )
                        continue
                    if kept < beam_width:
                        next_active.append(
                            ((*ids, int(tid)), next_bytes, new_score, mb_copy)
                        )
                        kept += 1
                    if kept >= beam_width:
                        break
            next_active.sort(key=lambda item: -item[2])
            active = next_active[:beam_width]
            completed.sort(key=lambda c: -c.raw_logprob)
            del completed[beam_width:]
            if len(completed) >= beam_width and (
                not active or completed[-1].raw_logprob >= active[0][2]
            ):
                break
    return sorted([*completed, *cap_pruned[:1]], key=lambda c: -c.raw_logprob)[
        : max(beam_width, BEAM_SIZE)
    ]


def _eval_arm_a_val_loss(  # noqa: PLR0913
    model: Any,  # noqa: ANN401
    processor: Any,  # noqa: ANN401
    family: ArmAFamily,
    val_rows: Sequence[Mapping[str, object]],
    dataset_root: Path,
    *,
    max_eval_rows: int,
) -> float:
    torch = import_module("torch")
    losses: list[float] = []
    model.eval()
    with torch.no_grad():
        for row in val_rows[:max_eval_rows]:
            rep = run_arm_a_manifest_train_step(
                model,
                processor,
                family,
                row,
                dataset_root,
                allow_position_bpe_mask=True,
                backward=False,
            )
            if rep.get("loss") is not None:
                losses.append(float(cast("float", rep["loss"])))
    return float(np.mean(losses)) if losses else math.inf


def _run_train_arm_a(  # noqa: PLR0915
    cfg: Mapping[str, Any],
) -> dict[str, object]:
    torch = import_module("torch")
    t0 = time.monotonic()
    seed = int(cfg.get("seed", 42))
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    dataset_dir = Path(str(cfg["dataset_dir"]))
    dataset_root = dataset_dir.parent
    out_dir = Path(str(cfg["output_dir"]))
    out_dir.mkdir(parents=True, exist_ok=True)
    train_rows = load_packaged_crop_manifest(dataset_dir / "synth_train")
    val_rows = load_packaged_crop_manifest(dataset_dir / "synth_val")
    model, proc, family = _load_arm_a_for_worker(cfg, for_training=True)
    trainable = [p for p in model.parameters() if p.requires_grad]
    lr = float(cfg.get("lr", 2e-4))
    grad_accum = max(1, int(cfg.get("grad_accum", 8)))
    max_steps = int(cfg.get("max_steps", 1500))
    eval_every = max(1, int(cfg.get("eval_every", max(50, max_steps // 4))))
    max_val_rows = int(cfg.get("max_val_rows", 200))
    optimizer = torch.optim.AdamW(trainable, lr=lr, weight_decay=0.01)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
        optimizer, T_max=max(1, max_steps), eta_min=lr * 0.1
    )
    schedule = _interleave_musescore_rows(train_rows, seed)
    best_val = math.inf
    best_dir = out_dir / "best"
    last_dir = out_dir / "last"
    history: list[dict[str, object]] = []
    step = 0
    accum = 0
    running_loss = 0.0
    row_cursor = 0
    optimizer.zero_grad(set_to_none=True)
    while step < max_steps:
        row = schedule[row_cursor % len(schedule)]
        row_cursor += 1
        if row_cursor % len(schedule) == 0:
            schedule = _interleave_musescore_rows(
                train_rows, seed + row_cursor // len(schedule)
            )
        rep = run_arm_a_manifest_train_step(
            model,
            proc,
            family,
            row,
            dataset_root,
            allow_position_bpe_mask=True,
            backward=True,
            grad_scale=1.0 / grad_accum,
            strict_grad_check=False,
        )
        if rep.get("skipped") or rep.get("loss") is None:
            continue
        running_loss += float(cast("float", rep["loss"])) / grad_accum
        accum += 1
        if accum < grad_accum:
            continue
        torch.nn.utils.clip_grad_norm_(trainable, 1.0)
        optimizer.step()
        scheduler.step()
        optimizer.zero_grad(set_to_none=True)
        step += 1
        if step % eval_every == 0 or step == max_steps:
            val_loss = _eval_arm_a_val_loss(
                model,
                proc,
                family,
                val_rows,
                dataset_root,
                max_eval_rows=max_val_rows,
            )
            history.append(
                {
                    "step": step,
                    "train_loss": round(running_loss, 6),
                    "val_loss": round(val_loss, 6),
                    "elapsed_sec": round(time.monotonic() - t0, 2),
                }
            )
            if val_loss <= best_val:
                best_val = val_loss
                model.save_pretrained(
                    best_dir,
                    safe_serialization=True,
                    save_embedding_layers=False,
                )
        running_loss = 0.0
        accum = 0
    model.save_pretrained(
        last_dir, safe_serialization=True, save_embedding_layers=False
    )
    if not best_dir.exists():
        model.save_pretrained(
            best_dir, safe_serialization=True, save_embedding_layers=False
        )
    peak_vram = (
        int(torch.cuda.max_memory_allocated())
        if torch.cuda.is_available()
        else 0
    )
    summary = {
        "arm": "arm-a",
        "family": family,
        "model_repo": cfg.get("model_repo"),
        "dataset": dataset_dir.name,
        "steps": step,
        "grad_accum": grad_accum,
        "lr": lr,
        "lora_rank": int(cfg.get("lora_rank", 32)),
        "include_vision_lora": bool(cfg.get("include_vision_lora", False)),
        "best_val_loss": round(best_val, 6)
        if math.isfinite(best_val)
        else None,
        "peak_vram_bytes": peak_vram,
        "elapsed_sec": round(time.monotonic() - t0, 2),
        "best_checkpoint": str(best_dir),
        "last_checkpoint": str(last_dir),
        "history": history,
    }
    (out_dir / "train_summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8"
    )
    return summary


def _run_train_arm_b(  # noqa: C901, PLR0915
    cfg: Mapping[str, Any],
) -> dict[str, object]:
    torch = import_module("torch")
    st_save = import_module("safetensors.torch").save_file
    t0 = time.monotonic()
    seed = int(cfg.get("seed", 42))
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    dataset_dir = Path(str(cfg["dataset_dir"]))
    dataset_root = dataset_dir.parent
    out_dir = Path(str(cfg["output_dir"]))
    out_dir.mkdir(parents=True, exist_ok=True)
    train_rows = load_packaged_crop_manifest(dataset_dir / "synth_train")
    val_rows = load_packaged_crop_manifest(dataset_dir / "synth_val")
    w_dir = Path(str(cfg["weights_dir"])) if cfg.get("weights_dir") else None
    r_dir = (
        Path(str(cfg["checkpoint_dir"])) if cfg.get("checkpoint_dir") else None
    )
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model, pad_mult = _build_arm_b_model(
        cfg, weights_dir=w_dir, checkpoint_dir=r_dir
    )
    model = model.to(device)
    tokenizer = TextScore2DomainTokenizer()
    lr = float(cfg.get("lr", 3e-4))
    grad_accum = max(1, int(cfg.get("grad_accum", 8)))
    max_steps = int(cfg.get("max_steps", 3000))
    eval_every = max(1, int(cfg.get("eval_every", max(100, max_steps // 5))))
    max_val_rows = int(cfg.get("max_val_rows", 250))
    optimizer = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=0.01)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
        optimizer, T_max=max(1, max_steps), eta_min=lr * 0.05
    )
    schedule = _interleave_musescore_rows(train_rows, seed)
    best_dir = out_dir / "best"
    last_dir = out_dir / "last"
    best_dir.mkdir(parents=True, exist_ok=True)
    last_dir.mkdir(parents=True, exist_ok=True)
    meta_cfg = {
        "arm_b_arch": cfg["arm_b_arch"],
        "fuse_stride8": bool(cfg.get("fuse_stride8", True)),
        "decoder_layers": int(cfg.get("decoder_layers", 6)),
        "decoder_d_model": int(cfg.get("decoder_d_model", 512)),
        "decoder_nhead": int(cfg.get("decoder_nhead", 8)),
        "decoder_d_ff": int(cfg.get("decoder_d_ff", 2048)),
        "vision_embed_dim": int(cfg.get("vision_embed_dim", 384)),
        "vision_layers": int(cfg.get("vision_layers", 12)),
        "vision_heads": int(cfg.get("vision_heads", 6)),
        "vision_ffn": int(cfg.get("vision_ffn", 1536)),
    }
    best_val = math.inf
    history: list[dict[str, object]] = []
    step = 0
    accum = 0
    running_loss = 0.0
    row_cursor = 0
    use_amp = device.type == "cuda"
    optimizer.zero_grad(set_to_none=True)

    def _padded_step(
        row: Mapping[str, object], *, backward: bool, scale: float = 1.0
    ) -> float | None:
        with torch.autocast(
            device_type=device.type, dtype=torch.bfloat16, enabled=use_amp
        ):
            rep = run_arm_b_manifest_train_step(
                model,
                tokenizer,
                row,
                dataset_root,
                pad_to_multiple=pad_mult,
                backward=backward,
                grad_scale=scale,
                strict_grad_check=False,
            )
        return (
            float(cast("float", rep["loss"]))
            if rep.get("loss") is not None
            else None
        )

    while step < max_steps:
        row = schedule[row_cursor % len(schedule)]
        row_cursor += 1
        if row_cursor % len(schedule) == 0:
            schedule = _interleave_musescore_rows(
                train_rows, seed + row_cursor // len(schedule)
            )
        loss_val = _padded_step(row, backward=True, scale=1.0 / grad_accum)
        if loss_val is None:
            continue
        running_loss += loss_val / grad_accum
        accum += 1
        if accum < grad_accum:
            continue
        torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
        optimizer.step()
        scheduler.step()
        optimizer.zero_grad(set_to_none=True)
        step += 1
        if step % eval_every == 0 or step == max_steps:
            model.eval()
            v_losses: list[float] = []
            with torch.no_grad():
                for vrow in val_rows[:max_val_rows]:
                    vl = _padded_step(vrow, backward=False)
                    if vl is not None:
                        v_losses.append(vl)
            val_loss = float(np.mean(v_losses)) if v_losses else math.inf
            history.append(
                {
                    "step": step,
                    "train_loss": round(running_loss, 6),
                    "val_loss": round(val_loss, 6),
                    "elapsed_sec": round(time.monotonic() - t0, 2),
                }
            )
            if val_loss <= best_val:
                best_val = val_loss
                st_save(
                    {k: v.contiguous() for k, v in model.state_dict().items()},
                    str(best_dir / "model.safetensors"),
                )
                (best_dir / "arm_b_config.json").write_text(
                    json.dumps(meta_cfg, indent=2) + "\n", encoding="utf-8"
                )
        running_loss = 0.0
        accum = 0
    st_save(
        {k: v.contiguous() for k, v in model.state_dict().items()},
        str(last_dir / "model.safetensors"),
    )
    (last_dir / "arm_b_config.json").write_text(
        json.dumps(meta_cfg, indent=2) + "\n", encoding="utf-8"
    )
    if not (best_dir / "model.safetensors").is_file():
        st_save(
            {k: v.contiguous() for k, v in model.state_dict().items()},
            str(best_dir / "model.safetensors"),
        )
        (best_dir / "arm_b_config.json").write_text(
            json.dumps(meta_cfg, indent=2) + "\n", encoding="utf-8"
        )
    peak_vram = (
        int(torch.cuda.max_memory_allocated())
        if torch.cuda.is_available()
        else 0
    )
    summary = {
        "arm": "arm-b",
        "arch": cfg["arm_b_arch"],
        "fuse_stride8": bool(cfg.get("fuse_stride8", True)),
        "model_repo": cfg.get("model_repo"),
        "dataset": dataset_dir.name,
        "steps": step,
        "grad_accum": grad_accum,
        "lr": lr,
        "best_val_loss": round(best_val, 6)
        if math.isfinite(best_val)
        else None,
        "peak_vram_bytes": peak_vram,
        "elapsed_sec": round(time.monotonic() - t0, 2),
        "best_checkpoint": str(best_dir),
        "last_checkpoint": str(last_dir),
        "history": history,
    }
    (out_dir / "train_summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8"
    )
    return summary


def _select_detector_threshold(
    preds: Sequence[DetectorPrediction],
    targets: Sequence[DetectorTarget],
    *,
    min_iou: float = 0.85,
) -> tuple[float, dict[str, float | int]]:
    best_tau = 0.5
    best_f1 = -1.0
    best_op = evaluate_operating_point(
        preds, targets, threshold=0.5, min_iou=min_iou
    )
    for tau in np.linspace(0.20, 0.85, 27):
        op = evaluate_operating_point(
            preds, targets, threshold=float(tau), min_iou=min_iou
        )
        p, r = op.precision, op.recall
        f1 = (2 * p * r / (p + r)) if (p + r) > 0 else 0.0
        if f1 > best_f1:
            best_f1 = f1
            best_tau = round(float(tau), 4)
            best_op = op
    return best_tau, {
        "threshold": best_tau,
        "f1": round(best_f1, 6),
        "precision": round(best_op.precision, 6),
        "recall": round(best_op.recall, 6),
        "tp": best_op.true_positives,
        "fp": best_op.false_positives,
        "fn": best_op.false_negatives,
    }


def _evaluate_detector_on_manifest(
    model: Any,  # noqa: ANN401
    val_pages: Sequence[Mapping[str, object]],
    dataset_root: Path,
    *,
    longest_edge: int,
    max_pages: int,
) -> dict[str, object]:
    torch = import_module("torch")
    device = next(model.parameters()).device
    model.eval()
    all_preds: dict[int, list[DetectorPrediction]] = {0: [], 1: []}
    all_targets: dict[int, list[DetectorTarget]] = {0: [], 1: []}
    with torch.no_grad():
        for row in val_pages[:max_pages]:
            page_id = str(row["page_id"])
            w = int(cast("int", row["page_width_px"]))
            h = int(cast("int", row["page_height_px"]))
            t_map = cast("dict[int, list[DetectorTarget]]", row["targets"])
            all_targets[0].extend(t_map[0])
            all_targets[1].extend(t_map[1])
            proc = build_page_detector_processor(
                w, h, longest_edge=longest_edge
            )
            with Image.open(dataset_root / str(row["page_relpath"])) as png:
                batch = proc(images=png.convert("RGB"), return_tensors="pt")
            pv = batch.pixel_values.to(device=device)
            out = model(pixel_values=pv, return_dict=True)
            preds = postprocess_detector_raw_outputs(
                out.logits[0].detach().float().cpu().numpy(),
                out.pred_boxes[0].detach().float().cpu().numpy(),
                w,
                h,
                page_id=page_id,
            )
            for p in preds:
                all_preds[p.class_id].append(p)
    ap_sys = compute_class_ap75(all_preds[0], all_targets[0])
    ap_meas = compute_class_ap75(all_preds[1], all_targets[1])
    tau_sys, op_sys = _select_detector_threshold(all_preds[0], all_targets[0])
    tau_meas, op_meas = _select_detector_threshold(all_preds[1], all_targets[1])
    return {
        "longest_edge": longest_edge,
        "eval_pages": min(len(val_pages), max_pages),
        "system_ap75": round(ap_sys, 6) if ap_sys is not None else None,
        "measure_ap75": round(ap_meas, 6) if ap_meas is not None else None,
        "tau_system": tau_sys,
        "tau_measure": tau_meas,
        "system_iou85": op_sys,
        "measure_iou85": op_meas,
    }


def _run_train_detector(  # noqa: PLR0915
    cfg: Mapping[str, Any],
) -> dict[str, object]:
    torch = import_module("torch")
    t0 = time.monotonic()
    seed = int(cfg.get("seed", 42))
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    family = cast("DetectorFamily", str(cfg["detector_family"]))
    longest_edge = int(cfg.get("longest_edge", 1024))
    dataset_dir = Path(str(cfg["dataset_dir"]))
    dataset_root = dataset_dir.parent
    out_dir = Path(str(cfg["output_dir"]))
    out_dir.mkdir(parents=True, exist_ok=True)
    train_pages = load_packaged_page_boxes_manifest(dataset_dir / "synth_train")
    val_pages = load_packaged_page_boxes_manifest(dataset_dir / "synth_val")
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    if bool(cfg.get("tiny_fixture", False)):
        model = build_page_detector(family).to(device)
    else:
        model = load_pretrained_page_detector(
            family, Path(str(cfg["weights_dir"]))
        ).to(device)
    lr = float(cfg.get("lr", 1e-4))
    grad_accum = max(1, int(cfg.get("grad_accum", 8)))
    max_steps = int(cfg.get("max_steps", 2000))
    eval_every = max(1, int(cfg.get("eval_every", max(100, max_steps // 4))))
    max_val_pages = int(cfg.get("max_val_pages", 300))
    optimizer = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
        optimizer, T_max=max(1, max_steps), eta_min=lr * 0.05
    )
    rng = random.Random(seed)  # noqa: S311
    order = list(train_pages)
    rng.shuffle(order)
    best_dir = out_dir / "best"
    last_dir = out_dir / "last"
    best_ap = -1.0
    best_eval: dict[str, object] = {}
    history: list[dict[str, object]] = []
    step = 0
    accum = 0
    running_loss = 0.0
    cursor = 0
    optimizer.zero_grad(set_to_none=True)
    while step < max_steps:
        row = order[cursor % len(order)]
        cursor += 1
        if cursor % len(order) == 0:
            rng.shuffle(order)
        w = int(cast("int", row["page_width_px"]))
        h = int(cast("int", row["page_height_px"]))
        proc = build_page_detector_processor(w, h, longest_edge=longest_edge)
        with Image.open(dataset_root / str(row["page_relpath"])) as png:
            batch = proc(
                images=png.convert("RGB"),
                annotations=row["annotations"],
                return_tensors="pt",
            )
        if not len(batch.labels[0]["boxes"]):
            continue
        model.train()
        pv = batch.pixel_values.to(device=device)
        lbls = [
            {k: v.to(device=device) for k, v in lbl.items()}
            for lbl in batch.labels
        ]
        out = model(pixel_values=pv, labels=lbls, return_dict=True)
        loss = out.loss
        if not torch.isfinite(loss).item():
            optimizer.zero_grad(set_to_none=True)
            accum = 0
            running_loss = 0.0
            continue
        (loss / grad_accum).backward()
        running_loss += float(loss.detach()) / grad_accum
        accum += 1
        if accum < grad_accum:
            continue
        torch.nn.utils.clip_grad_norm_(model.parameters(), 0.5)
        optimizer.step()
        scheduler.step()
        optimizer.zero_grad(set_to_none=True)
        step += 1
        if step % eval_every == 0 or step == max_steps:
            ev = _evaluate_detector_on_manifest(
                model,
                val_pages,
                dataset_root,
                longest_edge=longest_edge,
                max_pages=max_val_pages,
            )
            ap_sys = float(cast("float", ev["system_ap75"] or 0.0))
            history.append(
                {
                    "step": step,
                    "train_loss": round(running_loss, 6),
                    "eval": ev,
                    "elapsed_sec": round(time.monotonic() - t0, 2),
                }
            )
            if ap_sys >= best_ap:
                best_ap = ap_sys
                best_eval = ev
                model.save_pretrained(best_dir, safe_serialization=True)
                (best_dir / "detector_operating_point.json").write_text(
                    json.dumps(ev, indent=2) + "\n", encoding="utf-8"
                )
        running_loss = 0.0
        accum = 0
    model.save_pretrained(last_dir, safe_serialization=True)
    if not best_dir.exists():
        best_eval = _evaluate_detector_on_manifest(
            model,
            val_pages,
            dataset_root,
            longest_edge=longest_edge,
            max_pages=max_val_pages,
        )
        model.save_pretrained(best_dir, safe_serialization=True)
        (best_dir / "detector_operating_point.json").write_text(
            json.dumps(best_eval, indent=2) + "\n", encoding="utf-8"
        )
    peak_vram = (
        int(torch.cuda.max_memory_allocated())
        if torch.cuda.is_available()
        else 0
    )
    summary = {
        "arm": "detector",
        "family": family,
        "longest_edge": longest_edge,
        "model_repo": cfg.get("model_repo"),
        "dataset": dataset_dir.name,
        "steps": step,
        "grad_accum": grad_accum,
        "lr": lr,
        "best_eval": best_eval,
        "peak_vram_bytes": peak_vram,
        "elapsed_sec": round(time.monotonic() - t0, 2),
        "best_checkpoint": str(best_dir),
        "last_checkpoint": str(last_dir),
        "history": history,
    }
    (out_dir / "train_summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8"
    )
    return summary


def _run_infer_detector(
    cfg: Mapping[str, Any], heartbeat_fd: int | None
) -> dict[str, object]:
    torch = import_module("torch")
    tr_mod = import_module("transformers")
    ckpt_dir = Path(str(cfg["checkpoint_dir"]))
    longest_edge = int(cfg.get("longest_edge", 1024))
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = tr_mod.AutoModelForObjectDetection.from_pretrained(
        ckpt_dir, local_files_only=True, trust_remote_code=False
    ).to(device)
    model.eval()
    op_file = ckpt_dir / "detector_operating_point.json"
    op_meta = (
        json.loads(op_file.read_text(encoding="utf-8"))
        if op_file.is_file()
        else {"tau_system": 0.5, "tau_measure": 0.5}
    )
    tau_sys = float(cfg.get("tau_system", op_meta.get("tau_system", 0.5)))
    tau_meas = float(cfg.get("tau_measure", op_meta.get("tau_measure", 0.5)))
    pages_out: list[dict[str, object]] = []
    with torch.inference_mode():
        for idx, item in enumerate(cast("list[dict[str, Any]]", cfg["pages"])):
            page_id = str(item["page_id"])
            png_path = Path(str(item["png_path"]))
            with Image.open(png_path) as im:
                rgb = im.convert("RGB")
                w, h = rgb.size
                proc = build_page_detector_processor(
                    w, h, longest_edge=longest_edge
                )
                batch = proc(images=rgb, return_tensors="pt")
            pv = batch.pixel_values.to(device=device)
            out = model(pixel_values=pv, return_dict=True)
            preds = postprocess_detector_raw_outputs(
                out.logits[0].detach().float().cpu().numpy(),
                out.pred_boxes[0].detach().float().cpu().numpy(),
                w,
                h,
                page_id=page_id,
            )
            pages_out.append(
                {
                    "page_id": page_id,
                    "width": w,
                    "height": h,
                    "tau_system": tau_sys,
                    "tau_measure": tau_meas,
                    "predictions": [
                        {
                            "class_id": p.class_id,
                            "class_name": DETECTOR_CLASSES[p.class_id],
                            "query_idx": p.query_idx,
                            "probability": p.probability,
                            "bbox": list(p.bbox),
                        }
                        for p in preds
                    ],
                }
            )
            if heartbeat_fd is not None:
                os.write(heartbeat_fd, f"{idx}\n".encode("ascii"))
    result: dict[str, object] = {
        "tau_system": tau_sys,
        "tau_measure": tau_meas,
        "longest_edge": longest_edge,
        "pages": pages_out,
    }
    Path(str(cfg["output_json"])).write_text(
        json.dumps(result, indent=2) + "\n", encoding="utf-8"
    )
    return result


def _run_infer_recognizer(  # noqa: C901, PLR0915
    cfg: Mapping[str, Any], heartbeat_fd: int | None
) -> dict[str, object]:
    torch = import_module("torch")
    arm = str(cfg["arm"])
    zero_shot = bool(cfg.get("zero_shot_e0", False))
    max_new = int(cfg.get("max_new_tokens", MAX_SYSTEM_TOKENS))
    beams = int(cfg.get("num_beams", BEAM_SIZE))
    gen_fn_for_crop: Callable[
        [Image.Image, SystemPromptState, int], list[BeamCandidate]
    ]
    if arm == "arm-a":
        model, proc, family = _load_arm_a_for_worker(cfg, for_training=False)

        def gen_arm_a(
            im: Image.Image, prompt: SystemPromptState, carry_toks: int
        ) -> list[BeamCandidate]:
            return _generate_arm_a_beams(
                model,
                proc,
                family,
                im,
                prompt,
                carry_measure_tokens=carry_toks,
                zero_shot_e0=zero_shot,
                max_new_tokens=max_new,
                num_beams=beams,
            )

        gen_fn_for_crop = gen_arm_a
    elif arm == "arm-b":
        ckpt_dir = Path(str(cfg["checkpoint_dir"]))
        arm_b_meta = json.loads(
            (ckpt_dir / "arm_b_config.json").read_text(encoding="utf-8")
        )
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        model, pad_mult = _build_arm_b_model(
            arm_b_meta, weights_dir=None, checkpoint_dir=ckpt_dir
        )
        model = model.to(device)
        tokenizer = TextScore2DomainTokenizer()

        def gen_arm_b(
            im: Image.Image, prompt: SystemPromptState, carry_toks: int
        ) -> list[BeamCandidate]:
            return _generate_arm_b_beams(
                model,
                tokenizer,
                im,
                prompt,
                pad_multiple=pad_mult,
                carry_measure_tokens=carry_toks,
                max_new_tokens=max_new,
                beam_width=beams,
            )

        gen_fn_for_crop = gen_arm_b
    else:
        msg = f"unknown recognizer arm: {arm!r}"
        raise ValueError(msg)

    pieces_cfg = cast("list[dict[str, Any]]", cfg["pieces"])
    page_counter = 0
    pieces_out: dict[str, object] = {}
    for p_cfg in pieces_cfg:
        gid = str(p_cfg["gold_id"])
        crops_cfg = cast("list[dict[str, Any]]", p_cfg["crops"])
        exp_pages_raw = cast("list[int] | None", p_cfg.get("expected_pages"))
        pages_in_piece = sorted(
            {int(p) for p in exp_pages_raw}
            if exp_pages_raw is not None
            else {int(c["page_index"]) for c in crops_cfg}
        )
        if not crops_cfg:
            pieces_out[gid] = {
                "gold_id": gid,
                "conditioning": "blind",
                "gate_eligible": True,
                "systems": [],
            }
            for _ in pages_in_piece:
                if heartbeat_fd is not None:
                    os.write(heartbeat_fd, f"{page_counter}\n".encode("ascii"))
                page_counter += 1
            continue
        coords = [
            (int(c["page_index"]), int(c["page_system_index"]))
            for c in crops_cfg
        ]
        crop_paths = [Path(str(c["crop_path"])) for c in crops_cfg]
        emitted_pages: set[int] = set()

        def _generate_step(
            sys_i: int,
            prompt: SystemPromptState,
            carry_toks: int,
            _paths: list[Path] = crop_paths,
            _coords: list[tuple[int, int]] = coords,
            _emitted: set[int] = emitted_pages,
            _all_pages: list[int] = pages_in_piece,
        ) -> list[BeamCandidate]:
            nonlocal page_counter
            pg = _coords[sys_i][0]
            # Emit heartbeats for any preceding zero-crop pages
            for earlier_pg in _all_pages:
                if earlier_pg < pg and earlier_pg not in _emitted:
                    _emitted.add(earlier_pg)
                    if heartbeat_fd is not None:
                        os.write(
                            heartbeat_fd, f"{page_counter}\n".encode("ascii")
                        )
                    page_counter += 1
            with Image.open(_paths[sys_i]) as raw_im:
                rgb = raw_im.convert("RGB")
            cands = gen_fn_for_crop(rgb, prompt, carry_toks)
            is_last_on_pg = (
                sys_i + 1 == len(_coords) or _coords[sys_i + 1][0] != pg
            )
            if is_last_on_pg and pg not in _emitted:
                _emitted.add(pg)
                if heartbeat_fd is not None:
                    os.write(heartbeat_fd, f"{page_counter}\n".encode("ascii"))
                page_counter += 1
            return cands

        decoded = decode_piece_systems_blind(
            coords, _generate_step, expected_systems=len(coords)
        )
        # Emit heartbeats for remaining pages skipped on upstream_carry_gap
        for pg in pages_in_piece:
            if pg not in emitted_pages:
                emitted_pages.add(pg)
                if heartbeat_fd is not None:
                    os.write(heartbeat_fd, f"{page_counter}\n".encode("ascii"))
                page_counter += 1
        pieces_out[gid] = {
            "gold_id": gid,
            "conditioning": decoded.conditioning,
            "gate_eligible": decoded.gate_eligible,
            "systems": [
                {
                    "page_index": coords[i][0],
                    "page_system_index": coords[i][1],
                    "valid": sys_res.valid,
                    "text": sys_res.text,
                    "raw_logprob": sys_res.raw_logprob,
                    "abstention_reason": sys_res.abstention_reason,
                    "needs_person": sys_res.needs_person,
                    "carry_out_text": sys_res.carry_out_text,
                }
                for i, sys_res in enumerate(decoded.systems)
            ],
        }
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

    out_payload: dict[str, object] = {
        "arm": arm,
        "zero_shot_e0": zero_shot,
        "pieces": pieces_out,
    }
    Path(str(cfg["output_json"])).write_text(
        json.dumps(out_payload, indent=2) + "\n", encoding="utf-8"
    )
    return out_payload


def _run_replay_branch(  # noqa: PLR0915
    cfg: Mapping[str, Any],
) -> dict[str, object]:
    """Run matched-step continuation vs REAL_TRAIN+synth replay on GPU."""
    torch = import_module("torch")
    st_save = import_module("safetensors.torch").save_file
    seed = int(cfg.get("seed", 42))
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    arm = str(cfg["arm"])
    branch = str(
        cfg["branch"]
    )  # "synthetic_continuation" | "real_plus_synth_replay"
    steps = max(1, int(cfg.get("matched_steps", 200)))
    grad_accum = max(1, int(cfg.get("grad_accum", 4)))
    lr = float(cfg.get("lr", 5e-5))
    synth_dir = Path(str(cfg["synth_dataset_dir"]))
    synth_root = synth_dir.parent
    synth_rows = load_packaged_crop_manifest(synth_dir / "synth_train")
    real_rows = cast("list[dict[str, object]]", cfg.get("real_train_rows", []))
    real_root = Path(str(cfg["real_crops_root"]))
    out_dir = Path(str(cfg["output_dir"]))
    out_dir.mkdir(parents=True, exist_ok=True)
    rng = random.Random(seed)  # noqa: S311
    synth_pool = list(synth_rows)
    rng.shuffle(synth_pool)
    real_pool = list(real_rows)
    rng.shuffle(real_pool)

    if arm == "arm-a":
        model, proc, family = _load_arm_a_for_worker(cfg, for_training=True)
        trainable = [p for p in model.parameters() if p.requires_grad]
        optimizer = torch.optim.AdamW(trainable, lr=lr, weight_decay=0.01)
        s_idx = r_idx = 0
        done = accum = 0
        optimizer.zero_grad(set_to_none=True)
        while done < steps:
            use_real = (
                branch == "real_plus_synth_replay"
                and bool(real_pool)
                and ((s_idx + r_idx) % 2 == 0)
            )
            if use_real:
                row = real_pool[r_idx % len(real_pool)]
                r_idx += 1
                root = real_root
                masked_ords = cast(
                    "list[int]", row.get("masked_measure_ordinals", [])
                )
            else:
                row = synth_pool[s_idx % len(synth_pool)]
                s_idx += 1
                root = synth_root
                masked_ords = []
            rep = run_arm_a_manifest_train_step(
                model,
                proc,
                family,
                row,
                root,
                allow_position_bpe_mask=True,
                masked_measure_ordinals=masked_ords,
                backward=True,
                grad_scale=1.0 / grad_accum,
                strict_grad_check=False,
            )
            if rep.get("skipped") or rep.get("loss") is None:
                continue
            accum += 1
            if accum < grad_accum:
                continue
            torch.nn.utils.clip_grad_norm_(trainable, 1.0)
            optimizer.step()
            optimizer.zero_grad(set_to_none=True)
            done += 1
            accum = 0
        ckpt = out_dir / "checkpoint"
        model.save_pretrained(
            ckpt, safe_serialization=True, save_embedding_layers=False
        )
        return {"branch": branch, "steps": done, "checkpoint": str(ckpt)}

    ckpt_in = Path(str(cfg["checkpoint_dir"]))
    arm_b_meta = json.loads(
        (ckpt_in / "arm_b_config.json").read_text(encoding="utf-8")
    )
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model, pad_mult = _build_arm_b_model(
        arm_b_meta, weights_dir=None, checkpoint_dir=ckpt_in
    )
    model = model.to(device)
    tokenizer = TextScore2DomainTokenizer()
    optimizer = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=0.01)
    s_idx = r_idx = 0
    done = accum = 0
    use_amp = device.type == "cuda"
    optimizer.zero_grad(set_to_none=True)
    while done < steps:
        use_real = (
            branch == "real_plus_synth_replay"
            and bool(real_pool)
            and ((s_idx + r_idx) % 2 == 0)
        )
        if use_real:
            row = real_pool[r_idx % len(real_pool)]
            r_idx += 1
            root = real_root
            masked_ords = cast(
                "list[int]", row.get("masked_measure_ordinals", [])
            )
        else:
            row = synth_pool[s_idx % len(synth_pool)]
            s_idx += 1
            root = synth_root
            masked_ords = []
        with torch.autocast(
            device_type=device.type, dtype=torch.bfloat16, enabled=use_amp
        ):
            rep = run_arm_b_manifest_train_step(
                model,
                tokenizer,
                row,
                root,
                pad_to_multiple=pad_mult,
                masked_measure_ordinals=masked_ords,
                backward=True,
                grad_scale=1.0 / grad_accum,
                strict_grad_check=False,
            )
        if rep.get("skipped") or rep.get("loss") is None:
            continue
        accum += 1
        if accum < grad_accum:
            continue
        torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
        optimizer.step()
        optimizer.zero_grad(set_to_none=True)
        done += 1
        accum = 0
    ckpt = out_dir / "checkpoint"
    ckpt.mkdir(parents=True, exist_ok=True)
    st_save(
        {k: v.contiguous() for k, v in model.state_dict().items()},
        str(ckpt / "model.safetensors"),
    )
    (ckpt / "arm_b_config.json").write_text(
        json.dumps(arm_b_meta, indent=2) + "\n", encoding="utf-8"
    )
    return {"branch": branch, "steps": done, "checkpoint": str(ckpt)}


def main(argv: Sequence[str] | None = None) -> int:
    """Entry point invoked inside isolated DOWNLOADS/w6-gpu-venv."""
    args = list(sys.argv[1:] if argv is None else argv)
    if len(args) not in {1, 2}:
        sys.stderr.write("usage: slice_b_worker <job.json> [heartbeat_fd]\n")
        return 2
    heartbeat_fd: int | None = None
    if len(args) == 2 and args[1] == "--heartbeat-stdout":  # noqa: PLR2004
        heartbeat_fd = os.dup(1)
        with Path(os.devnull).open("wb") as sink:
            os.dup2(sink.fileno(), 1)
    cfg = json.loads(Path(args[0]).read_text(encoding="utf-8"))
    task = str(cfg["task"])
    if task == "train_arm_a":
        _run_train_arm_a(cfg)
    elif task == "train_arm_b":
        _run_train_arm_b(cfg)
    elif task == "train_detector":
        _run_train_detector(cfg)
    elif task == "infer_detector":
        _run_infer_detector(cfg, heartbeat_fd)
    elif task == "infer_recognizer":
        _run_infer_recognizer(cfg, heartbeat_fd)
    elif task == "train_replay_branch":
        res = _run_replay_branch(cfg)
        Path(str(cfg["output_json"])).write_text(
            json.dumps(res, indent=2) + "\n", encoding="utf-8"
        )
    else:
        msg = f"unknown Slice B worker task: {task!r}"
        raise ValueError(msg)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
