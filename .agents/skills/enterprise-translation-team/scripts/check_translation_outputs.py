"""Validate enterprise translation skill eval outputs."""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ALLOWED_CATEGORIES = {
    "Accuracy",
    "Fluency",
    "Terminology",
    "Style",
    "Locale",
    "Non-translation",
}
ALLOWED_SEVERITIES = {"Major", "Minor", "Neutral"}
ALLOWED_RESOLUTION_STATUSES = {"open", "resolved", "waived"}
ALLOWED_TERM_STATUSES = {
    "approved",
    "candidate",
    "conflict",
    "forbidden",
    "needs_confirmation",
    "deprecated",
    "rejected",
}
ALLOWED_DELTA_OPS = {
    "propose_term",
    "approve_term",
    "reject_term",
    "add_forbidden",
    "raise_conflict",
    "resolve_conflict",
    "add_document_override",
    "waive_term_violation",
    "promote_to_global",
    "supersede_entry",
}
ALLOWED_CONFLICT_STATUSES = {"open", "resolved"}
ALLOWED_TBX_ADMIN_STATUSES = {
    "admittedTerm-admn-sts",
    "deprecatedTerm-admn-sts",
    "preferredTerm-admn-sts",
    "supersededTerm-admn-sts",
}
ALLOWED_TBX_PARTS_OF_SPEECH = {
    "abbreviation",
    "acronym",
    "adjective",
    "adverb",
    "conjunction",
    "interjection",
    "noun",
    "numeral",
    "particle",
    "phrase",
    "preposition",
    "pronoun",
    "properNoun",
    "verb",
}
ALLOWED_TBX_TERM_NOTE_TYPES = {
    "administrativeStatus",
    "partOfSpeech",
    "termType",
}
ALLOWED_TBX_TERM_TYPES = {
    "acronym",
    "abbreviation",
    "fullForm",
    "phrase",
    "shortForm",
    "variant",
}
REGULAR_GRANDFATHERED_BCP47_TAGS = {
    "art-lojban",
    "cel-gaulish",
    "no-bok",
    "no-nyn",
    "zh-guoyu",
    "zh-hakka",
    "zh-min",
    "zh-min-nan",
    "zh-xiang",
}
TBX_NAMESPACE = "urn:iso:std:iso:30042:ed-2"
TERM_REVIEW_HEADER = (
    "concept_id\tentry_id\tscope\tstatus\tsource_term\tpreferred_target\t"
    "allowed_variants\tforbidden_targets\tcontext_note\tpositive_example\t"
    "negative_example\tconflict_id\tblocking\tevidence_refs"
)
ASCII_ALPHA_RE = re.compile(r"^[A-Za-z]+$")
ASCII_ALNUM_RE = re.compile(r"^[A-Za-z0-9]+$")
HAN_CHARACTER_RE = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]")
TABLE_DELIMITER_CELL_RE = re.compile(r"^:?-{3,}:?$")
INLINE_CODE_RE = re.compile(r"`[^`\n]*`")
MARKDOWN_LINK_DESTINATION_RE = re.compile(r"\]\([^)]+\)")
MARKDOWN_LINK_RE = re.compile(r"(?<!!)\[[^\]\n]+\]\(([^)\n]+)\)")
URL_RE = re.compile(r"https?://\S+")
PLACEHOLDER_RE = re.compile(r"\{[^{}\n]+\}")
HEADING_RE = re.compile(r"^(#{1,6})[ \t]+")
FENCE_RE = re.compile(r"^[ \t]*(`{3,}|~{3,})[ \t]*([^ \t]*)[ \t]*$")


def load_eval_case(evals_path: Path, case_id: str) -> dict:
    data = json.loads(evals_path.read_text(encoding="utf-8"))
    for case in data.get("evals", []):
        if case.get("id") == case_id:
            return case
    raise ValueError(f"Unknown eval case: {case_id}")


def require_file(path: Path) -> str:
    if not path.exists():
        raise AssertionError(f"Missing expected file: {path}")
    if not path.is_file():
        raise AssertionError(f"Expected a file, got: {path}")
    content = path.read_text(encoding="utf-8")
    if not content.strip():
        raise AssertionError(f"Expected non-empty file: {path}")
    return content


def check_review_json(path: Path) -> None:
    payload = json.loads(require_file(path))
    issues = payload.get("issues")
    if not isinstance(issues, list):
        raise AssertionError("review.json must contain an issues array")
    summary = payload.get("summary")
    if not isinstance(summary, dict):
        raise AssertionError("review.json must contain a summary object")
    required = {
        "issue_id",
        "segment_id",
        "category",
        "severity",
        "source_quote",
        "target_quote",
        "explanation",
        "proposed_fix",
        "resolution_status",
    }
    seen_issue_ids: set[str] = set()
    for index, raw_issue in enumerate(issues, start=1):
        issue = require_dict(raw_issue, f"review.json issue {index}")
        missing = required.difference(issue)
        if missing:
            raise AssertionError(
                f"review.json issue {index} is missing fields: {sorted(missing)}"
            )
        for field in required:
            require_nonempty_string(
                issue.get(field), f"review.json issue {index}.{field}"
            )
        issue_id = issue["issue_id"]
        if issue_id in seen_issue_ids:
            raise AssertionError(f"Duplicate review.json issue_id: {issue_id}")
        seen_issue_ids.add(issue_id)
        if issue["category"] not in ALLOWED_CATEGORIES:
            raise AssertionError(
                f"review.json issue {index} has invalid category: {issue['category']}"
            )
        if issue["severity"] not in ALLOWED_SEVERITIES:
            raise AssertionError(
                f"review.json issue {index} has invalid severity: {issue['severity']}"
            )
        resolution_status = issue["resolution_status"]
        if resolution_status not in ALLOWED_RESOLUTION_STATUSES:
            raise AssertionError(
                f"review.json issue {index} has invalid resolution_status: "
                f"{resolution_status}"
            )
        if resolution_status in {"resolved", "waived"}:
            require_nonempty_string(
                issue.get("resolution_evidence"),
                f"review.json issue {index}.resolution_evidence",
            )
        if resolution_status == "waived":
            require_nonempty_string(
                issue.get("waiver_ref"),
                f"review.json issue {index}.waiver_ref",
            )
    expected_counts = {
        "major": sum(1 for issue in issues if issue["severity"] == "Major"),
        "minor": sum(1 for issue in issues if issue["severity"] == "Minor"),
        "neutral": sum(1 for issue in issues if issue["severity"] == "Neutral"),
    }
    for key, expected in expected_counts.items():
        actual = summary.get(key)
        if type(actual) is not int or actual < 0 or actual != expected:
            raise AssertionError(
                f"review.json summary.{key} must be integer {expected}, "
                f"got {actual!r}"
            )


def require_dict(payload: object, label: str) -> dict:
    if not isinstance(payload, dict):
        raise AssertionError(f"{label} must be an object")
    return payload


def require_list(payload: object, label: str) -> list:
    if not isinstance(payload, list):
        raise AssertionError(f"{label} must be an array")
    return payload


def require_nonempty_string(payload: object, label: str) -> str:
    if not isinstance(payload, str) or not payload.strip():
        raise AssertionError(f"{label} must be a non-empty string")
    return payload


def check_bcp47(value: object, label: str) -> None:
    text = require_nonempty_string(value, label)
    if text.casefold() in REGULAR_GRANDFATHERED_BCP47_TAGS:
        raise AssertionError(
            f"{label} must use a preferred replacement for grandfathered "
            f"tag {text!r}"
        )
    parts = text.split("-")
    if not is_supported_bcp47(parts):
        raise AssertionError(
            f"{label} must look like a BCP-47 language tag: {text!r}"
        )


def is_alpha_subtag(value: str, minimum: int, maximum: int) -> bool:
    return (
        minimum <= len(value) <= maximum
        and ASCII_ALPHA_RE.fullmatch(value) is not None
    )


def is_alnum_subtag(value: str, minimum: int, maximum: int) -> bool:
    return (
        minimum <= len(value) <= maximum
        and ASCII_ALNUM_RE.fullmatch(value) is not None
    )


def is_variant_subtag(value: str) -> bool:
    return is_alnum_subtag(value, 5, 8) or (
        len(value) == 4
        and value[0].isdigit()
        and ASCII_ALNUM_RE.fullmatch(value) is not None
    )


def is_supported_bcp47(parts: list[str]) -> bool:  # noqa: PLR0911
    if not parts or any(not part for part in parts):
        return False
    if parts[0].casefold() == "x":
        return len(parts) > 1 and all(
            is_alnum_subtag(part, 1, 8) for part in parts[1:]
        )

    language = parts[0]
    if not is_alpha_subtag(language, 2, 8):
        return False
    index = 1

    if len(language) <= 3:
        extlang_count = 0
        while (
            index < len(parts)
            and extlang_count < 3
            and is_alpha_subtag(parts[index], 3, 3)
        ):
            index += 1
            extlang_count += 1

    if index < len(parts) and is_alpha_subtag(parts[index], 4, 4):
        index += 1
    if index < len(parts) and (
        is_alpha_subtag(parts[index], 2, 2)
        or (len(parts[index]) == 3 and parts[index].isdigit())
    ):
        index += 1

    variants: set[str] = set()
    while index < len(parts) and is_variant_subtag(parts[index]):
        variant = parts[index].casefold()
        if variant in variants:
            return False
        variants.add(variant)
        index += 1

    extension_singletons: set[str] = set()
    while (
        index < len(parts)
        and len(parts[index]) == 1
        and parts[index].casefold() != "x"
        and ASCII_ALNUM_RE.fullmatch(parts[index]) is not None
    ):
        singleton = parts[index].casefold()
        if singleton in extension_singletons:
            return False
        extension_singletons.add(singleton)
        index += 1
        extension_start = index
        while index < len(parts) and is_alnum_subtag(parts[index], 2, 8):
            index += 1
        if index == extension_start:
            return False

    if index < len(parts) and parts[index].casefold() == "x":
        index += 1
        private_start = index
        while index < len(parts) and is_alnum_subtag(parts[index], 1, 8):
            index += 1
        if index == private_start:
            return False

    return index == len(parts)


def split_tsv_list(value: str) -> set[str]:
    return {item.strip() for item in value.split(";") if item.strip()}


def entry_scope_text(entry: dict) -> str:
    scope = require_dict(
        entry.get("scope"), f"{entry.get('concept_id', '<unknown>')}.scope"
    )
    level = require_nonempty_string(scope.get("level"), "scope.level").strip()
    domain = require_nonempty_string(
        scope.get("domain"), "scope.domain"
    ).strip()
    client_value = scope.get("client_id")
    client_id = (
        client_value.strip()
        if isinstance(client_value, str) and client_value.strip()
        else ""
    )
    project_value = scope.get("project_id")
    project_id = (
        project_value.strip()
        if isinstance(project_value, str) and project_value.strip()
        else ""
    )
    parts = [client_id, domain, project_id]
    while parts[-1] == "":
        parts.pop()
    return f"{level}:{parts[0]}" + "".join(f"/{part}" for part in parts[1:])


def entry_target_terms(entry: dict) -> set[str]:
    target = require_dict(
        entry.get("target"), f"{entry.get('concept_id', '<unknown>')}.target"
    )
    terms = {
        require_nonempty_string(target.get("preferred"), "target.preferred")
    }
    terms.update(
        require_nonempty_string(term, "target.allowed_variants[]")
        for term in require_list(
            target.get("allowed_variants", []), "target.allowed_variants"
        )
    )
    for forbidden in require_list(
        target.get("forbidden", []), "target.forbidden"
    ):
        forbidden_entry = require_dict(forbidden, "target.forbidden[]")
        terms.add(
            require_nonempty_string(
                forbidden_entry.get("term"), "forbidden.term"
            )
        )
    return terms


def first_example_text(context: dict, key: str, fields: list[str]) -> str:
    examples = require_list(context.get(key), f"context.{key}")
    example = require_dict(examples[0], f"context.{key}[0]")
    for field in fields:
        value = example.get(field)
        if isinstance(value, str) and value.strip():
            return value.strip()
    raise AssertionError(f"context.{key}[0] must include one of {fields}")


def conflicts_by_concept(termbase: dict) -> dict[str, list[dict]]:
    conflicts: dict[str, list[dict]] = {}
    for conflict in require_list(termbase.get("conflicts", []), "conflicts"):
        conflict_entry = require_dict(conflict, "conflicts[]")
        concept_id = require_nonempty_string(
            conflict_entry.get("concept_id"), "conflict.concept_id"
        )
        conflicts.setdefault(concept_id, []).append(conflict_entry)
    return conflicts


def entry_is_blocking(entry: dict, concept_conflicts: list[dict]) -> bool:
    status = require_nonempty_string(entry.get("status"), "entry.status")
    if status in {"candidate", "conflict", "needs_confirmation"}:
        return True
    if any(conflict.get("blocking") is True for conflict in concept_conflicts):
        return True
    target = require_dict(entry.get("target"), "entry.target")
    for forbidden in require_list(
        target.get("forbidden", []), "target.forbidden"
    ):
        forbidden_entry = require_dict(forbidden, "target.forbidden[]")
        if forbidden_entry.get("severity") == "blocking":
            return True
    return False


def check_terminology_review_tsv(
    path: Path, termbase: dict | None = None
) -> None:
    content = require_file(path)
    lines = content.splitlines()
    first_line = lines[0] if lines else ""
    if first_line != TERM_REVIEW_HEADER:
        raise AssertionError(
            f"terminology-review.tsv header must be {TERM_REVIEW_HEADER!r}, "
            f"got {first_line!r}"
        )
    rows = list(csv.DictReader(lines, delimiter="\t"))
    if any(None in row for row in rows):
        raise AssertionError(
            "terminology-review.tsv rows must not contain extra fields"
        )
    fields = TERM_REVIEW_HEADER.split("\t")
    if any(row.get(field) is None for row in rows for field in fields):
        raise AssertionError(
            "terminology-review.tsv rows must include every header field"
        )
    if not rows:
        raise AssertionError(
            "terminology-review.tsv must include at least one data row"
        )
    required_fields = [
        "concept_id",
        "entry_id",
        "scope",
        "status",
        "source_term",
        "preferred_target",
        "context_note",
        "blocking",
        "evidence_refs",
    ]
    for index, row in enumerate(rows, start=1):
        for field in required_fields:
            require_nonempty_string(
                row.get(field),
                f"terminology-review.tsv row {index} {field}",
            )
        if row.get("status") not in ALLOWED_TERM_STATUSES:
            raise AssertionError(
                f"terminology-review.tsv row {index} has invalid status"
            )
        if row.get("status") == "approved":
            for field in ["positive_example", "negative_example"]:
                require_nonempty_string(
                    row.get(field),
                    f"terminology-review.tsv row {index} {field}",
                )
        if row.get("blocking") not in {"true", "false"}:
            raise AssertionError(
                f"terminology-review.tsv row {index} blocking must be "
                "true or false"
            )
    row_entry_ids = [row["entry_id"] for row in rows]
    if len(row_entry_ids) != len(set(row_entry_ids)):
        raise AssertionError(
            "terminology-review.tsv must not contain duplicate entry_id rows"
        )
    if termbase is None:
        return
    entries = require_list(termbase.get("entries"), "entries")
    if len(rows) != len(entries):
        raise AssertionError(
            "terminology-review.tsv must include exactly one row per termbase entry"
        )
    expected_entry_ids = {
        require_nonempty_string(entry.get("entry_id"), "entry_id")
        for entry in entries
    }
    if set(row_entry_ids) != expected_entry_ids:
        raise AssertionError(
            "terminology-review.tsv entry_id set must exactly match "
            "termbase.job.json"
        )
    rows_by_entry = dict(zip(row_entry_ids, rows, strict=True))
    conflicts = conflicts_by_concept(termbase)
    for entry in entries:
        entry_id = require_nonempty_string(entry.get("entry_id"), "entry_id")
        concept_id = require_nonempty_string(
            entry.get("concept_id"), "concept_id"
        )
        row = rows_by_entry.get(entry_id)
        if row is None:
            raise AssertionError(
                f"terminology-review.tsv missing row for entry_id {entry_id}"
            )
        if row.get("concept_id") != concept_id:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} has wrong concept_id"
            )
        if row.get("scope") != entry_scope_text(entry):
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} has wrong scope"
            )
        if row.get("status") != entry.get("status"):
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} has wrong status"
            )
        source = require_dict(entry.get("source"), f"{entry_id}.source")
        target = require_dict(entry.get("target"), f"{entry_id}.target")
        context = require_dict(entry.get("context"), f"{entry_id}.context")
        provenance = require_dict(
            entry.get("provenance"), f"{entry_id}.provenance"
        )
        if row.get("source_term") != source.get("term"):
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} has wrong source_term"
            )
        if row.get("preferred_target") != target.get("preferred"):
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} has wrong preferred_target"
            )
        allowed = set(
            require_list(
                target.get("allowed_variants", []), "target.allowed_variants"
            )
        )
        if split_tsv_list(row.get("allowed_variants", "")) != allowed:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} allowed_variants mismatch"
            )
        forbidden = {
            require_nonempty_string(item.get("term"), "forbidden.term")
            for item in require_list(
                target.get("forbidden", []), "target.forbidden"
            )
        }
        if split_tsv_list(row.get("forbidden_targets", "")) != forbidden:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} forbidden_targets mismatch"
            )
        for field in [
            "context_note",
            "evidence_refs",
        ]:
            if not row.get(field, "").strip():
                raise AssertionError(
                    f"terminology-review.tsv row {entry_id} missing {field}"
                )
        context_note = context.get("usage_note") or context.get("definition")
        if row.get("context_note") != context_note:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} context_note mismatch"
            )
        positive_examples = require_list(
            context.get("positive_examples"), "context.positive_examples"
        )
        negative_examples = require_list(
            context.get("negative_examples"), "context.negative_examples"
        )
        if entry.get("status") == "approved" and (
            not positive_examples or not negative_examples
        ):
            raise AssertionError(
                f"Approved termbase entry {entry_id} must include examples"
            )
        expected_positive = (
            first_example_text(context, "positive_examples", ["target"])
            if positive_examples
            else ""
        )
        if row.get("positive_example") != expected_positive:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} positive_example mismatch"
            )
        expected_negative = (
            first_example_text(
                context,
                "negative_examples",
                ["correct_guidance", "reason", "bad_target"],
            )
            if negative_examples
            else ""
        )
        if row.get("negative_example") != expected_negative:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} negative_example mismatch"
            )
        evidence_refs = set(
            require_list(
                provenance.get("evidence_refs"), "provenance.evidence_refs"
            )
        )
        if split_tsv_list(row.get("evidence_refs", "")) != evidence_refs:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} evidence_refs mismatch"
            )
        entry_conflicts = conflicts.get(concept_id, [])
        expected_conflict_ids = {
            require_nonempty_string(
                conflict.get("conflict_id"), "conflict.conflict_id"
            )
            for conflict in entry_conflicts
        }
        row_conflict_ids = split_tsv_list(row.get("conflict_id", ""))
        if row_conflict_ids != expected_conflict_ids:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} conflict_id mismatch"
            )
        expected_blocking = (
            "true" if entry_is_blocking(entry, entry_conflicts) else "false"
        )
        if row.get("blocking") != expected_blocking:
            raise AssertionError(
                f"terminology-review.tsv row {entry_id} blocking mismatch"
            )


def check_termbase_json(path: Path) -> dict:
    payload = require_dict(json.loads(require_file(path)), "termbase.job.json")
    if payload.get("schema_version") != "enterprise-termbase-v2":
        raise AssertionError(
            "termbase.job.json schema_version must be enterprise-termbase-v2"
        )
    source_locale = require_nonempty_string(
        payload.get("source_locale"), "source_locale"
    )
    target_locale = require_nonempty_string(
        payload.get("target_locale"), "target_locale"
    )
    check_bcp47(source_locale, "source_locale")
    check_bcp47(target_locale, "target_locale")
    if source_locale.casefold() == target_locale.casefold():
        raise AssertionError("source_locale and target_locale must differ")
    standard = require_dict(payload.get("standard_basis"), "standard_basis")
    if "TBX" not in require_nonempty_string(
        standard.get("primary"), "standard_basis.primary"
    ):
        raise AssertionError("standard_basis.primary must reference TBX")
    for index, export_target in enumerate(
        require_list(
            standard.get("export_targets"),
            "standard_basis.export_targets",
        ),
        start=1,
    ):
        require_nonempty_string(
            export_target,
            f"standard_basis.export_targets[{index}]",
        )
    if type(standard.get("lossless_for_key_fields")) is not bool:
        raise AssertionError(
            "standard_basis.lossless_for_key_fields must be a boolean"
        )
    require_nonempty_string(payload.get("termbase_id"), "termbase_id")
    entries = require_list(payload.get("entries"), "entries")
    if not entries:
        raise AssertionError(
            "termbase.job.json must include at least one entry"
        )
    seen_concepts: set[str] = set()
    seen_entries: set[str] = set()
    entries_by_concept: dict[str, dict] = {}
    statuses: set[str] = set()
    forbidden_terms: list[str] = []
    for index, raw_entry in enumerate(entries, start=1):
        entry = require_dict(raw_entry, f"entries[{index}]")
        concept_id = require_nonempty_string(
            entry.get("concept_id"), f"entries[{index}].concept_id"
        )
        if concept_id in seen_concepts:
            raise AssertionError(f"Duplicate concept_id: {concept_id}")
        seen_concepts.add(concept_id)
        entry_id = require_nonempty_string(
            entry.get("entry_id"), f"entries[{index}].entry_id"
        )
        if entry_id in seen_entries:
            raise AssertionError(f"Duplicate entry_id: {entry_id}")
        seen_entries.add(entry_id)
        entries_by_concept[concept_id] = entry
        status = require_nonempty_string(
            entry.get("status"), f"entries[{index}].status"
        )
        if status not in ALLOWED_TERM_STATUSES:
            raise AssertionError(f"Invalid term status: {status}")
        statuses.add(status)
        scope = require_dict(entry.get("scope"), f"entries[{index}].scope")
        require_nonempty_string(
            scope.get("level"), f"entries[{index}].scope.level"
        )
        require_nonempty_string(
            scope.get("domain"), f"entries[{index}].scope.domain"
        )
        source = require_dict(entry.get("source"), f"entries[{index}].source")
        target = require_dict(entry.get("target"), f"entries[{index}].target")
        require_nonempty_string(
            source.get("term"), f"entries[{index}].source.term"
        )
        part_of_speech = require_nonempty_string(
            source.get("part_of_speech"),
            f"entries[{index}].source.part_of_speech",
        )
        if part_of_speech not in ALLOWED_TBX_PARTS_OF_SPEECH:
            raise AssertionError(
                f"Entry {concept_id} part_of_speech is not TBX-Basic compatible: {part_of_speech}"
            )
        term_type = require_nonempty_string(
            source.get("term_type"), f"entries[{index}].source.term_type"
        )
        if term_type not in ALLOWED_TBX_TERM_TYPES:
            raise AssertionError(
                f"Entry {concept_id} term_type is not TBX-Basic compatible: {term_type}"
            )
        require_nonempty_string(
            target.get("preferred"), f"entries[{index}].target.preferred"
        )
        for variant_index, variant in enumerate(
            require_list(
                target.get("allowed_variants"),
                f"entries[{index}].target.allowed_variants",
            ),
            start=1,
        ):
            require_nonempty_string(
                variant,
                f"entries[{index}].target.allowed_variants[{variant_index}]",
            )
        source_language = require_nonempty_string(
            source.get("language"), f"entries[{index}].source.language"
        )
        target_language = require_nonempty_string(
            target.get("language"), f"entries[{index}].target.language"
        )
        check_bcp47(source_language, f"entries[{index}].source.language")
        check_bcp47(target_language, f"entries[{index}].target.language")
        if source_language.casefold() != source_locale.casefold():
            raise AssertionError(
                f"Entry {concept_id} source.language must match source_locale"
            )
        if target_language.casefold() != target_locale.casefold():
            raise AssertionError(
                f"Entry {concept_id} target.language must match target_locale"
            )
        context = require_dict(
            entry.get("context"), f"entries[{index}].context"
        )
        require_nonempty_string(
            context.get("definition"), f"entries[{index}].context.definition"
        )
        positive = require_list(
            context.get("positive_examples"),
            f"entries[{index}].context.positive_examples",
        )
        negative = require_list(
            context.get("negative_examples"),
            f"entries[{index}].context.negative_examples",
        )
        if status == "approved" and (not positive or not negative):
            raise AssertionError(
                f"Approved entry {concept_id} must include positive and negative examples"
            )
        for label, examples in [
            ("positive_examples", positive),
            ("negative_examples", negative),
        ]:
            for example_index, raw_example in enumerate(examples, start=1):
                example = require_dict(
                    raw_example,
                    f"entries[{index}].context.{label}[{example_index}]",
                )
                if not example:
                    raise AssertionError(
                        f"entries[{index}].context.{label}[{example_index}] "
                        "must not be empty"
                    )
        if positive:
            first_example_text(context, "positive_examples", ["target"])
        if negative:
            first_example_text(
                context,
                "negative_examples",
                ["correct_guidance", "reason", "bad_target"],
            )
        for forbidden in require_list(
            target.get("forbidden"), f"entries[{index}].target.forbidden"
        ):
            forbidden_entry = require_dict(
                forbidden, f"entries[{index}].target.forbidden[]"
            )
            forbidden_terms.append(
                require_nonempty_string(
                    forbidden_entry.get("term"), "forbidden.term"
                )
            )
            require_nonempty_string(
                forbidden_entry.get("reason"), "forbidden.reason"
            )
            require_nonempty_string(
                forbidden_entry.get("match_mode"), "forbidden.match_mode"
            )
            require_nonempty_string(
                forbidden_entry.get("severity"), "forbidden.severity"
            )
        provenance = require_dict(
            entry.get("provenance"), f"entries[{index}].provenance"
        )
        require_nonempty_string(
            provenance.get("created_by"),
            f"entries[{index}].provenance.created_by",
        )
        require_nonempty_string(
            provenance.get("created_at"),
            f"entries[{index}].provenance.created_at",
        )
        evidence_refs = require_list(
            provenance.get("evidence_refs"),
            f"entries[{index}].provenance.evidence_refs",
        )
        if not evidence_refs:
            raise AssertionError(
                f"Entry {concept_id} must include evidence_refs"
            )
        for evidence_index, evidence_ref in enumerate(evidence_refs, start=1):
            require_nonempty_string(
                evidence_ref,
                f"entries[{index}].provenance.evidence_refs[{evidence_index}]",
            )
        maintenance = require_dict(
            entry.get("maintenance"), f"entries[{index}].maintenance"
        )
        revision = maintenance.get("revision")
        if type(revision) is not int:
            raise AssertionError(
                f"Entry {concept_id} maintenance.revision must be an integer"
            )
        for field in ["owner", "reviewer", "last_reviewed_at"]:
            require_nonempty_string(
                maintenance.get(field),
                f"entries[{index}].maintenance.{field}",
            )
        require_nonempty_string(
            maintenance.get("approval_status"),
            f"entries[{index}].maintenance.approval_status",
        )
        reliability = require_dict(
            maintenance.get("reliability"),
            f"entries[{index}].maintenance.reliability",
        )
        reliability_code = reliability.get("code")
        if type(reliability_code) is not int or not 1 <= reliability_code <= 5:
            raise AssertionError(
                f"Entry {concept_id} reliability.code must be an integer from 1 to 5"
            )
        require_nonempty_string(
            reliability.get("confidence"),
            f"entries[{index}].maintenance.reliability.confidence",
        )
    conflicts = require_list(payload.get("conflicts"), "conflicts")
    seen_conflict_ids: set[str] = set()
    for index, raw_conflict in enumerate(conflicts, start=1):
        conflict = require_dict(raw_conflict, f"conflicts[{index}]")
        conflict_id = require_nonempty_string(
            conflict.get("conflict_id"), f"conflicts[{index}].conflict_id"
        )
        if conflict_id in seen_conflict_ids:
            raise AssertionError(f"Duplicate conflict_id: {conflict_id}")
        seen_conflict_ids.add(conflict_id)
        concept_id = require_nonempty_string(
            conflict.get("concept_id"), f"conflicts[{index}].concept_id"
        )
        entry = entries_by_concept.get(concept_id)
        if entry is None:
            raise AssertionError(
                f"Conflict {conflict_id} references unknown concept_id {concept_id}"
            )
        source = require_dict(entry.get("source"), f"{concept_id}.source")
        source_term = require_nonempty_string(
            conflict.get("source_term"), f"conflicts[{index}].source_term"
        )
        if source_term != source.get("term"):
            raise AssertionError(
                f"Conflict {conflict_id} source_term does not match its entry"
            )
        require_nonempty_string(
            conflict.get("scope"), f"conflicts[{index}].scope"
        )
        competing_targets = [
            require_nonempty_string(
                value, f"conflicts[{index}].competing_targets[]"
            )
            for value in require_list(
                conflict.get("competing_targets"),
                f"conflicts[{index}].competing_targets",
            )
        ]
        if len(competing_targets) < 2 or len(competing_targets) != len(
            set(competing_targets)
        ):
            raise AssertionError(
                f"Conflict {conflict_id} must have at least two unique targets"
            )
        target = require_dict(entry.get("target"), f"{concept_id}.target")
        preferred = require_nonempty_string(
            target.get("preferred"), f"{concept_id}.target.preferred"
        )
        if preferred not in competing_targets:
            raise AssertionError(
                f"Conflict {conflict_id} must include the preferred target"
            )
        status = require_nonempty_string(
            conflict.get("status"), f"conflicts[{index}].status"
        )
        if status not in ALLOWED_CONFLICT_STATUSES:
            raise AssertionError(
                f"Conflict {conflict_id} has invalid status: {status}"
            )
        blocking = conflict.get("blocking")
        if type(blocking) is not bool:
            raise AssertionError(
                f"Conflict {conflict_id} blocking must be a boolean"
            )
        if status == "open" and blocking is not True:
            raise AssertionError(
                f"Open conflict {conflict_id} must be blocking"
            )
        if status == "open":
            overlap: set[str] = set()
            forbidden_targets = require_list(
                target.get("forbidden", []),
                f"{concept_id}.target.forbidden",
            )
            for candidate in competing_targets:
                for raw_forbidden in forbidden_targets:
                    forbidden = require_dict(
                        raw_forbidden, f"{concept_id}.target.forbidden[]"
                    )
                    forbidden_term = require_nonempty_string(
                        forbidden.get("term"),
                        f"{concept_id}.target.forbidden[].term",
                    )
                    match_mode = require_nonempty_string(
                        forbidden.get("match_mode"),
                        f"{concept_id}.target.forbidden[].match_mode",
                    )
                    if candidate == forbidden_term or (
                        match_mode == "case_insensitive"
                        and candidate.casefold() == forbidden_term.casefold()
                    ):
                        overlap.add(candidate)
            if overlap:
                raise AssertionError(
                    f"Open conflict {conflict_id} candidates must not be "
                    f"forbidden targets: {sorted(overlap)}"
                )
        if status == "resolved":
            if blocking is not False:
                raise AssertionError(
                    f"Resolved conflict {conflict_id} must not be blocking"
                )
            require_nonempty_string(
                conflict.get("selected_target"),
                f"conflicts[{index}].selected_target",
            )
            require_nonempty_string(
                conflict.get("resolution_ref"),
                f"conflicts[{index}].resolution_ref",
            )
        evidence_refs = [
            require_nonempty_string(
                value, f"conflicts[{index}].evidence_refs[]"
            )
            for value in require_list(
                conflict.get("evidence_refs"),
                f"conflicts[{index}].evidence_refs",
            )
        ]
        if not evidence_refs:
            raise AssertionError(
                f"Conflict {conflict_id} must include evidence_refs"
            )
    payload["_checked_statuses"] = sorted(statuses)
    payload["_checked_forbidden_terms"] = forbidden_terms
    return payload


def check_delta_jsonl(path: Path, termbase: dict | None = None) -> list[dict]:
    lines = [line for line in require_file(path).splitlines() if line.strip()]
    if not lines:
        raise AssertionError(
            "termbase.delta.jsonl must include at least one event"
        )
    events = []
    seen_event_ids: set[str] = set()
    canonical_conflicts = (
        {
            require_nonempty_string(
                conflict.get("conflict_id"), "conflict.conflict_id"
            ): require_dict(conflict, "conflicts[]")
            for conflict in require_list(
                termbase.get("conflicts", []), "conflicts"
            )
        }
        if termbase is not None
        else {}
    )
    for index, line in enumerate(lines, start=1):
        event = require_dict(
            json.loads(line), f"termbase.delta.jsonl line {index}"
        )
        events.append(event)
        op = require_nonempty_string(event.get("op"), f"line {index}.op")
        if op not in ALLOWED_DELTA_OPS:
            raise AssertionError(f"Invalid delta op on line {index}: {op}")
        for field in [
            "event_id",
            "job_id",
            "doc_id",
            "scope",
            "evidence_ref",
            "submitted_by",
            "status",
        ]:
            require_nonempty_string(event.get(field), f"line {index}.{field}")
        event_id = event["event_id"]
        if event_id in seen_event_ids:
            raise AssertionError(f"Duplicate delta event_id: {event_id}")
        seen_event_ids.add(event_id)
        if "concept_id" not in event and "source_term" not in event:
            raise AssertionError(
                f"Delta line {index} must include concept_id or source_term"
            )
        if "concept_id" in event:
            require_nonempty_string(
                event.get("concept_id"), f"line {index}.concept_id"
            )
        if "source_term" in event:
            require_nonempty_string(
                event.get("source_term"), f"line {index}.source_term"
            )
        if op == "add_forbidden":
            require_nonempty_string(
                event.get("forbidden_term"),
                f"line {index}.forbidden_term",
            )
        if op not in {"raise_conflict", "resolve_conflict"}:
            continue
        conflict_id = require_nonempty_string(
            event.get("conflict_id"), f"line {index}.conflict_id"
        )
        canonical = canonical_conflicts.get(conflict_id)
        if termbase is not None and canonical is None:
            raise AssertionError(
                f"Delta line {index} references unknown conflict_id {conflict_id}"
            )
        if canonical is not None:
            if "concept_id" in event and event.get(
                "concept_id"
            ) != canonical.get("concept_id"):
                raise AssertionError(
                    f"Delta line {index} concept_id does not match "
                    f"conflict {conflict_id}"
                )
            if "source_term" in event and event.get(
                "source_term"
            ) != canonical.get("source_term"):
                raise AssertionError(
                    f"Delta line {index} source_term does not match "
                    f"conflict {conflict_id}"
                )
        if op == "raise_conflict":
            competing_targets = [
                require_nonempty_string(
                    value, f"line {index}.competing_targets[]"
                )
                for value in require_list(
                    event.get("competing_targets"),
                    f"line {index}.competing_targets",
                )
            ]
            if len(competing_targets) < 2 or len(competing_targets) != len(
                set(competing_targets)
            ):
                raise AssertionError(
                    f"Delta line {index} must include at least two unique "
                    "competing_targets"
                )
            if canonical is not None and set(competing_targets) != set(
                require_list(
                    canonical.get("competing_targets"),
                    f"conflict {conflict_id}.competing_targets",
                )
            ):
                raise AssertionError(
                    f"Delta line {index} candidates do not match "
                    f"conflict {conflict_id}"
                )
        else:
            selected_target = require_nonempty_string(
                event.get("selected_target"),
                f"line {index}.selected_target",
            )
            resolution_ref = require_nonempty_string(
                event.get("resolution_ref"),
                f"line {index}.resolution_ref",
            )
            if canonical is not None and (
                canonical.get("status") != "resolved"
                or canonical.get("selected_target") != selected_target
                or canonical.get("resolution_ref") != resolution_ref
            ):
                raise AssertionError(
                    f"Delta line {index} resolution does not match "
                    f"conflict {conflict_id}"
                )
    return events


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def namespace_uri(tag: str) -> str:
    return tag[1:].split("}", 1)[0] if tag.startswith("{") else ""


def tbx_tag(name: str) -> str:
    return f"{{{TBX_NAMESPACE}}}{name}"


def require_single_child(
    parent: ET.Element, name: str, label: str
) -> ET.Element:
    children = parent.findall(tbx_tag(name))
    if len(children) != 1:
        raise AssertionError(f"{label} must include exactly one {name}")
    return children[0]


def element_ids(elements: list[ET.Element]) -> set[int]:
    return {id(element) for element in elements}


def check_tbx(path: Path, termbase: dict | None = None) -> None:
    # Eval artifacts are local files; external entity resolution is not used.
    root = ET.fromstring(require_file(path))  # noqa: S314
    if local_name(root.tag) != "tbx":
        raise AssertionError("termbase.tbx root element must be tbx")
    if namespace_uri(root.tag) != TBX_NAMESPACE:
        raise AssertionError(
            f"termbase.tbx root namespace must be {TBX_NAMESPACE}"
        )
    legacy_elements = {
        local_name(element.tag)
        for element in root.iter()
        if local_name(element.tag) in {"langSet", "tig"}
    }
    if legacy_elements:
        raise AssertionError(
            "termbase.tbx must not use legacy elements: "
            f"{sorted(legacy_elements)}"
        )
    if root.get("type") != "TBX-Basic":
        raise AssertionError("termbase.tbx type must be TBX-Basic")
    if root.get("style") not in {"dca", "dct"}:
        raise AssertionError("termbase.tbx style must be dca or dct")
    header = require_single_child(root, "tbxHeader", "termbase.tbx")
    file_description = require_single_child(
        header, "fileDesc", "termbase.tbx/tbxHeader"
    )
    require_single_child(
        file_description,
        "sourceDesc",
        "termbase.tbx/tbxHeader/fileDesc",
    )
    text = require_single_child(root, "text", "termbase.tbx")
    body = require_single_child(text, "body", "termbase.tbx/text")
    concepts = body.findall(tbx_tag("conceptEntry"))
    if not concepts:
        raise AssertionError("termbase.tbx must include conceptEntry")
    if element_ids(list(root.iter(tbx_tag("conceptEntry")))) != element_ids(
        concepts
    ):
        raise AssertionError(
            "Every conceptEntry must be a direct child of text/body"
        )

    all_lang_secs: list[ET.Element] = []
    all_term_secs: list[ET.Element] = []
    all_terms: list[ET.Element] = []
    all_term_notes: list[ET.Element] = []
    concepts_by_id: dict[str, dict[str, set[str]]] = {}
    concept_elements_by_id: dict[str, ET.Element] = {}
    administrative_status_by_term: dict[tuple[str, str, str], str | None] = {}
    xml_language = "{http://www.w3.org/XML/1998/namespace}lang"

    for concept in concepts:
        concept_id = require_nonempty_string(
            concept.get("id"), "conceptEntry.id"
        )
        if concept_id in concepts_by_id:
            raise AssertionError(f"Duplicate TBX conceptEntry id: {concept_id}")
        lang_secs = concept.findall(tbx_tag("langSec"))
        if not lang_secs:
            raise AssertionError(
                f"conceptEntry {concept_id} must include langSec"
            )
        if element_ids(list(concept.iter(tbx_tag("langSec")))) != element_ids(
            lang_secs
        ):
            raise AssertionError(
                f"conceptEntry {concept_id} has a non-direct langSec"
            )
        all_lang_secs.extend(lang_secs)
        terms_by_language: dict[str, set[str]] = {}

        for lang_sec in lang_secs:
            language = require_nonempty_string(
                lang_sec.get(xml_language),
                f"conceptEntry {concept_id} langSec xml:lang",
            )
            check_bcp47(language, f"conceptEntry {concept_id} langSec xml:lang")
            language_key = language.casefold()
            if language_key in terms_by_language:
                raise AssertionError(
                    f"conceptEntry {concept_id} has duplicate langSec "
                    f"for {language}"
                )
            term_secs = lang_sec.findall(tbx_tag("termSec"))
            if not term_secs:
                raise AssertionError(
                    f"conceptEntry {concept_id} langSec {language} "
                    "must include termSec"
                )
            if element_ids(
                list(lang_sec.iter(tbx_tag("termSec")))
            ) != element_ids(term_secs):
                raise AssertionError(
                    f"conceptEntry {concept_id} langSec {language} "
                    "has a non-direct termSec"
                )
            all_term_secs.extend(term_secs)
            language_terms = terms_by_language.setdefault(language_key, set())

            for term_sec in term_secs:
                terms = term_sec.findall(tbx_tag("term"))
                if len(terms) != 1:
                    raise AssertionError(
                        f"conceptEntry {concept_id} termSec must include "
                        "exactly one direct term"
                    )
                if element_ids(
                    list(term_sec.iter(tbx_tag("term")))
                ) != element_ids(terms):
                    raise AssertionError(
                        f"conceptEntry {concept_id} termSec has a "
                        "non-direct term"
                    )
                term = require_nonempty_string(
                    terms[0].text, f"conceptEntry {concept_id} term"
                )
                if term in language_terms:
                    raise AssertionError(
                        f"conceptEntry {concept_id} has duplicate term "
                        f"{term!r} under {language}"
                    )
                language_terms.add(term)
                all_terms.extend(terms)
                term_notes = term_sec.findall(tbx_tag("termNote"))
                if element_ids(
                    list(term_sec.iter(tbx_tag("termNote")))
                ) != element_ids(term_notes):
                    raise AssertionError(
                        f"conceptEntry {concept_id} termSec has a "
                        "non-direct termNote"
                    )
                all_term_notes.extend(term_notes)
                status_values = [
                    require_nonempty_string(
                        element.text,
                        f"conceptEntry {concept_id} term {term!r} "
                        "administrativeStatus",
                    )
                    for element in [
                        *[
                            note
                            for note in term_notes
                            if note.get("type") == "administrativeStatus"
                        ],
                        *[
                            admin
                            for admin in term_sec.findall(tbx_tag("admin"))
                            if admin.get("type") == "administrativeStatus"
                        ],
                    ]
                ]
                if len(status_values) > 1:
                    raise AssertionError(
                        f"termbase.tbx term {term!r} must have at most "
                        "one administrativeStatus"
                    )
                if (
                    status_values
                    and status_values[0] not in ALLOWED_TBX_ADMIN_STATUSES
                ):
                    raise AssertionError(
                        "Invalid TBX-Basic administrativeStatus value: "
                        f"{status_values[0]!r}"
                    )
                administrative_status_by_term[
                    (concept_id, language_key, term)
                ] = status_values[0] if status_values else None

        if element_ids(list(concept.iter(tbx_tag("termSec")))) != element_ids(
            [
                term_sec
                for lang_sec in lang_secs
                for term_sec in lang_sec.findall(tbx_tag("termSec"))
            ]
        ):
            raise AssertionError(
                f"conceptEntry {concept_id} has termSec outside langSec"
            )
        if element_ids(list(concept.iter(tbx_tag("term")))) != element_ids(
            [
                term
                for term_sec in concept.iter(tbx_tag("termSec"))
                for term in term_sec.findall(tbx_tag("term"))
            ]
        ):
            raise AssertionError(
                f"conceptEntry {concept_id} has term outside termSec"
            )
        concepts_by_id[concept_id] = terms_by_language
        concept_elements_by_id[concept_id] = concept

    for name, expected in [
        ("langSec", all_lang_secs),
        ("termSec", all_term_secs),
        ("term", all_terms),
        ("termNote", all_term_notes),
    ]:
        if element_ids(list(root.iter(tbx_tag(name)))) != element_ids(expected):
            raise AssertionError(
                f"Every {name} must follow the TBX concept hierarchy"
            )

    for term_note in all_term_notes:
        note_type = term_note.get("type")
        value = (term_note.text or "").strip()
        if note_type not in ALLOWED_TBX_TERM_NOTE_TYPES:
            raise AssertionError(
                f"Invalid TBX-Basic termNote type: {note_type!r}"
            )
        if (
            note_type == "partOfSpeech"
            and value not in ALLOWED_TBX_PARTS_OF_SPEECH
        ):
            raise AssertionError(
                f"Invalid TBX-Basic partOfSpeech value: {value!r}"
            )
        if note_type == "termType" and value not in ALLOWED_TBX_TERM_TYPES:
            raise AssertionError(f"Invalid TBX-Basic termType value: {value!r}")
    if termbase is None:
        return
    entries = require_list(termbase.get("entries"), "entries")
    expected_concept_ids = {
        require_nonempty_string(entry.get("concept_id"), "entry.concept_id")
        for entry in entries
    }
    if set(concepts_by_id) != expected_concept_ids:
        raise AssertionError(
            "termbase.tbx conceptEntry ids must exactly match termbase.job.json"
        )
    canonical_conflicts = conflicts_by_concept(termbase)
    for entry in entries:
        concept_id = require_nonempty_string(
            entry.get("concept_id"), "entry.concept_id"
        )
        concept = concept_elements_by_id[concept_id]
        terms_by_language = concepts_by_id.get(concept_id)
        if terms_by_language is None:
            raise AssertionError(
                f"termbase.tbx missing conceptEntry {concept_id}"
            )
        source = require_dict(entry.get("source"), f"{concept_id}.source")
        target = require_dict(entry.get("target"), f"{concept_id}.target")
        source_language = require_nonempty_string(
            source.get("language"), "source.language"
        )
        target_language = require_nonempty_string(
            target.get("language"), "target.language"
        )
        source_term = require_nonempty_string(source.get("term"), "source.term")
        expected_target_terms = entry_target_terms(entry)
        for conflict in canonical_conflicts.get(concept_id, []):
            expected_target_terms.update(
                require_nonempty_string(
                    value, f"conflict {concept_id}.competing_targets[]"
                )
                for value in require_list(
                    conflict.get("competing_targets"),
                    f"conflict {concept_id}.competing_targets",
                )
            )
        expected_terms_by_language = {
            source_language.casefold(): {source_term},
            target_language.casefold(): expected_target_terms,
        }
        if terms_by_language != expected_terms_by_language:
            raise AssertionError(
                f"termbase.tbx conceptEntry {concept_id} language and term "
                "sets must exactly match termbase.job.json"
            )
        target_language_key = target_language.casefold()
        target_statuses = {
            term: administrative_status_by_term[
                (concept_id, target_language_key, term)
            ]
            for term in terms_by_language[target_language_key]
        }
        forbidden_terms = {
            require_nonempty_string(item.get("term"), "forbidden.term")
            for item in require_list(
                target.get("forbidden", []), f"{concept_id}.target.forbidden"
            )
        }
        deprecated_statuses = {
            "deprecatedTerm-admn-sts",
            "supersededTerm-admn-sts",
        }
        protected_targets = {
            require_nonempty_string(
                target.get("preferred"), f"{concept_id}.target.preferred"
            ),
            *{
                require_nonempty_string(
                    value, f"{concept_id}.target.allowed_variants[]"
                )
                for value in require_list(
                    target.get("allowed_variants", []),
                    f"{concept_id}.target.allowed_variants",
                )
            },
        }
        for conflict in canonical_conflicts.get(concept_id, []):
            if conflict.get("status") == "open":
                protected_targets.update(
                    require_nonempty_string(
                        value, f"conflict {concept_id}.competing_targets[]"
                    )
                    for value in require_list(
                        conflict.get("competing_targets"),
                        f"conflict {concept_id}.competing_targets",
                    )
                )
            elif conflict.get("status") == "resolved":
                protected_targets.add(
                    require_nonempty_string(
                        conflict.get("selected_target"),
                        f"conflict {concept_id}.selected_target",
                    )
                )
        for term, status in target_statuses.items():
            if term in forbidden_terms:
                if status is not None and status not in deprecated_statuses:
                    raise AssertionError(
                        f"termbase.tbx forbidden target {term!r} has "
                        f"contradictory status {status!r}"
                    )
            elif term in protected_targets and status in deprecated_statuses:
                raise AssertionError(
                    f"termbase.tbx canonical target {term!r} has "
                    f"contradictory status {status!r}"
                )
        scope = require_dict(entry.get("scope"), f"{concept_id}.scope")
        context = require_dict(entry.get("context"), f"{concept_id}.context")
        expected_descrips = {
            "subjectField": require_nonempty_string(
                scope.get("domain"), f"{concept_id}.scope.domain"
            ),
            "definition": require_nonempty_string(
                context.get("definition"), f"{concept_id}.context.definition"
            ),
        }
        for description_type, expected_value in expected_descrips.items():
            values = [
                require_nonempty_string(
                    description.text,
                    f"conceptEntry {concept_id} {description_type}",
                )
                for description in concept.findall(tbx_tag("descrip"))
                if description.get("type") == description_type
            ]
            if values != [expected_value]:
                raise AssertionError(
                    f"termbase.tbx conceptEntry {concept_id} must include "
                    f"exact {description_type} metadata"
                )
        source_lang_secs = [
            lang_sec
            for lang_sec in concept.findall(tbx_tag("langSec"))
            if require_nonempty_string(
                lang_sec.get(xml_language),
                f"conceptEntry {concept_id} langSec xml:lang",
            ).casefold()
            == source_language.casefold()
        ]
        if len(source_lang_secs) != 1:
            raise AssertionError(
                f"termbase.tbx conceptEntry {concept_id} must include one "
                f"source langSec for {source_language}"
            )
        source_term_secs = [
            term_sec
            for term_sec in source_lang_secs[0].findall(tbx_tag("termSec"))
            if (term_sec.findtext(tbx_tag("term")) or "").strip() == source_term
        ]
        if len(source_term_secs) != 1:
            raise AssertionError(
                f"termbase.tbx conceptEntry {concept_id} must include one "
                "source termSec"
            )
        expected_source_notes = {
            "partOfSpeech": require_nonempty_string(
                source.get("part_of_speech"),
                f"{concept_id}.source.part_of_speech",
            ),
            "termType": require_nonempty_string(
                source.get("term_type"), f"{concept_id}.source.term_type"
            ),
        }
        for note_type, expected_value in expected_source_notes.items():
            values = [
                require_nonempty_string(
                    note.text,
                    f"conceptEntry {concept_id} source {note_type}",
                )
                for note in source_term_secs[0].findall(tbx_tag("termNote"))
                if note.get("type") == note_type
            ]
            if values != [expected_value]:
                raise AssertionError(
                    f"termbase.tbx conceptEntry {concept_id} source term "
                    f"must include exact {note_type}"
                )


def require_contains(content: str, needle: str, label: str) -> None:
    if needle not in content:
        raise AssertionError(f"{label} must contain {needle!r}")


def split_markdown_table_row(line: str) -> list[str]:
    stripped = line.strip()
    if not stripped.startswith("|") or not stripped.endswith("|"):
        return []
    return [cell.strip() for cell in stripped[1:-1].split("|")]


def split_fenced_content(
    content: str,
) -> tuple[list[tuple[int, str]], list[tuple[str, str]]]:
    outside: list[tuple[int, str]] = []
    blocks: list[tuple[str, str]] = []
    marker_character = ""
    marker_length = 0
    info = ""
    body: list[str] = []

    for line_number, line in enumerate(content.splitlines(), start=1):
        if not marker_character:
            match = FENCE_RE.fullmatch(line)
            if match is None:
                outside.append((line_number, line))
                continue
            marker = match.group(1)
            marker_character = marker[0]
            marker_length = len(marker)
            info = match.group(2).casefold()
            body = []
            continue

        stripped = line.strip()
        if len(stripped) >= marker_length and set(stripped) == {
            marker_character
        }:
            blocks.append((info, "\n".join(body)))
            marker_character = ""
            marker_length = 0
            info = ""
            body = []
        else:
            body.append(line)

    if marker_character:
        raise AssertionError("Markdown contains an unbalanced code fence")
    return outside, blocks


def markdown_tables(content: str) -> list[list[list[str]]]:
    outside, _ = split_fenced_content(content)
    lines = [line for _, line in outside]
    tables: list[list[list[str]]] = []
    index = 0
    while index < len(lines) - 1:
        header = split_markdown_table_row(lines[index])
        delimiter = split_markdown_table_row(lines[index + 1])
        if (
            not header
            or len(header) != len(delimiter)
            or not all(
                TABLE_DELIMITER_CELL_RE.fullmatch(cell) for cell in delimiter
            )
        ):
            index += 1
            continue
        rows = [header]
        index += 2
        while index < len(lines):
            row = split_markdown_table_row(lines[index])
            if len(row) != len(header):
                break
            rows.append(row)
            index += 1
        if len(rows) > 1:
            tables.append(rows)
    return tables


def has_expected_table_shape(content: str) -> bool:
    return any(
        len(table) >= 3 and len(table[0]) == 2
        for table in markdown_tables(content)
    )


def heading_levels(content: str) -> list[int]:
    outside, _ = split_fenced_content(content)
    return [
        len(match.group(1))
        for _, line in outside
        if (match := HEADING_RE.match(line)) is not None
    ]


def link_destinations(content: str) -> list[str]:
    outside, _ = split_fenced_content(content)
    return [
        match.group(1)
        for _, line in outside
        for match in MARKDOWN_LINK_RE.finditer(line)
    ]


def table_shapes(tables: list[list[list[str]]]) -> list[tuple[int, int]]:
    return [(len(table), len(table[0])) for table in tables]


def table_inline_code_layout(
    tables: list[list[list[str]]],
) -> list[list[list[list[str]]]]:
    return [
        [[INLINE_CODE_RE.findall(cell) for cell in row] for row in table]
        for table in tables
    ]


def first_untranslated_han_line(content: str) -> tuple[int, str] | None:
    outside, _ = split_fenced_content(content)
    for line_number, line in outside:
        candidate = INLINE_CODE_RE.sub("", line)
        candidate = MARKDOWN_LINK_DESTINATION_RE.sub("]()", candidate)
        candidate = URL_RE.sub("", candidate)
        candidate = PLACEHOLDER_RE.sub("", candidate)
        if HAN_CHARACTER_RE.search(candidate):
            return line_number, line
    return None


def check_markdown(path: Path) -> None:
    content = require_file(path)
    if "TODO" in content:
        raise AssertionError(f"Markdown output contains TODO marker: {path}")


def check_structured_translation(path: Path, source_path: Path) -> None:
    content = require_file(path)
    source = require_file(source_path)
    for protected in [
        "`v2.4.0`",
        "{customer_id}",
        "https://example.com/admin",
        "`region`",
        "`featureFlag`",
        '"featureFlag": "translation-preview"',
    ]:
        require_contains(content, protected, "translation.md")
    if heading_levels(content) != heading_levels(source):
        raise AssertionError(
            "translation.md must preserve the source heading hierarchy"
        )
    if link_destinations(content) != link_destinations(source):
        raise AssertionError(
            "translation.md must preserve source links and destinations"
        )
    source_tables = markdown_tables(source)
    translated_tables = markdown_tables(content)
    if table_shapes(translated_tables) != table_shapes(source_tables):
        raise AssertionError(
            "translation.md must preserve all source table dimensions"
        )
    if table_inline_code_layout(translated_tables) != table_inline_code_layout(
        source_tables
    ):
        raise AssertionError(
            "translation.md must preserve inline code in its source table cells"
        )
    _, source_blocks = split_fenced_content(source)
    _, translated_blocks = split_fenced_content(content)
    if translated_blocks != source_blocks:
        raise AssertionError(
            "translation.md must preserve fenced code blocks exactly"
        )
    untranslated = first_untranslated_han_line(content)
    if untranslated is not None:
        line_number, line = untranslated
        raise AssertionError(
            "translation.md contains untranslated Chinese prose "
            f"at line {line_number}: {line!r}"
        )


def check_terminology_content(
    path: Path, delta_path: Path, brief_path: Path
) -> dict:
    payload = check_termbase_json(path)
    brief = require_dict(
        json.loads(require_file(brief_path)), "terminology brief"
    )
    direction = require_nonempty_string(
        brief.get("language_direction"), "terminology brief.language_direction"
    )
    direction_parts = [part.strip() for part in direction.split("->")]
    if len(direction_parts) != 2 or not all(direction_parts):
        raise AssertionError(
            "terminology brief.language_direction must use '<source> -> <target>'"
        )
    expected_source_locale, expected_target_locale = direction_parts
    if payload.get("source_locale") != expected_source_locale:
        raise AssertionError(
            "termbase.job.json source_locale must match terminology brief"
        )
    if payload.get("target_locale") != expected_target_locale:
        raise AssertionError(
            "termbase.job.json target_locale must match terminology brief"
        )
    client_id = require_nonempty_string(
        brief.get("client_id"), "terminology brief.client_id"
    )
    require_nonempty_string(brief.get("domain"), "terminology brief.domain")
    project_id = require_nonempty_string(
        brief.get("project_id"), "terminology brief.project_id"
    )
    brief_terms = [
        require_dict(item, f"terminology brief.terms[{index}]")
        for index, item in enumerate(
            require_list(brief.get("terms"), "terminology brief.terms"),
            start=1,
        )
    ]
    if not brief_terms:
        raise AssertionError("terminology brief must include terms")
    entries = require_list(payload.get("entries"), "entries")
    entries_by_source: dict[str, dict] = {}
    for entry in entries:
        source = require_dict(entry.get("source"), "entry.source")
        source_term = require_nonempty_string(
            source.get("term"), "entry.source.term"
        )
        if source_term in entries_by_source:
            raise AssertionError(
                f"Duplicate canonical source term: {source_term}"
            )
        entries_by_source[source_term] = entry
    expected_sources = {
        require_nonempty_string(
            term.get("source"), "terminology brief term.source"
        )
        for term in brief_terms
    }
    if set(entries_by_source) != expected_sources:
        raise AssertionError(
            "termbase.job.json source terms must exactly match terminology brief"
        )
    conflicts_by_concept_id = conflicts_by_concept(payload)
    events = check_delta_jsonl(delta_path, payload)
    source_by_concept_id = {
        require_nonempty_string(
            entry.get("concept_id"), f"{source_term}.concept_id"
        ): source_term
        for source_term, entry in entries_by_source.items()
    }
    for event_index, event in enumerate(events, start=1):
        event_concept_id = event.get("concept_id")
        event_source_term = event.get("source_term")
        if event_concept_id is not None:
            canonical_source = source_by_concept_id.get(event_concept_id)
            if canonical_source is None:
                raise AssertionError(
                    f"Delta line {event_index} references unknown concept_id "
                    f"{event_concept_id}"
                )
            if (
                event_source_term is not None
                and event_source_term != canonical_source
            ):
                raise AssertionError(
                    f"Delta line {event_index} source_term does not match "
                    f"concept {event_concept_id}"
                )
        elif event_source_term not in expected_sources:
            raise AssertionError(
                f"Delta line {event_index} references unknown source_term "
                f"{event_source_term}"
            )
    for brief_term in brief_terms:
        source_term = require_nonempty_string(
            brief_term.get("source"), "terminology brief term.source"
        )
        preferred_target = require_nonempty_string(
            brief_term.get("preferred_target"),
            f"terminology brief {source_term}.preferred_target",
        )
        entry = entries_by_source[source_term]
        concept_id = require_nonempty_string(
            entry.get("concept_id"), f"{source_term}.concept_id"
        )
        scope = require_dict(entry.get("scope"), f"{concept_id}.scope")
        for key, expected in [
            ("client_id", client_id),
            ("project_id", project_id),
        ]:
            if scope.get(key) != expected:
                raise AssertionError(
                    f"Entry {concept_id} scope.{key} must match terminology brief"
                )
        target = require_dict(entry.get("target"), f"{concept_id}.target")
        if target.get("preferred") != preferred_target:
            raise AssertionError(
                f"Entry {concept_id} preferred target must match terminology brief"
            )
        expected_forbidden = {
            require_nonempty_string(
                value,
                f"terminology brief {source_term}.forbidden_targets[]",
            )
            for value in require_list(
                brief_term.get("forbidden_targets", []),
                f"terminology brief {source_term}.forbidden_targets",
            )
        }
        actual_forbidden: set[str] = set()
        for item in require_list(
            target.get("forbidden", []), f"{concept_id}.target.forbidden"
        ):
            forbidden_entry = require_dict(
                item, f"{concept_id}.target.forbidden[]"
            )
            if forbidden_entry.get("match_mode") != "case_insensitive":
                raise AssertionError(
                    f"Entry {concept_id} forbidden match_mode must be case_insensitive"
                )
            if forbidden_entry.get("severity") != "blocking":
                raise AssertionError(
                    f"Entry {concept_id} forbidden severity must be blocking"
                )
            actual_forbidden.add(
                require_nonempty_string(
                    forbidden_entry.get("term"), "forbidden.term"
                )
            )
        raw_conflicting_target = brief_term.get("conflicting_target")
        conflicting_target = (
            require_nonempty_string(
                raw_conflicting_target,
                f"terminology brief {source_term}.conflicting_target",
            )
            if raw_conflicting_target is not None
            else None
        )
        if actual_forbidden != expected_forbidden:
            raise AssertionError(
                f"Entry {concept_id} forbidden targets must match terminology brief"
            )
        context = require_dict(entry.get("context"), f"{concept_id}.context")
        if context.get("definition") != brief_term.get("definition"):
            raise AssertionError(
                f"Entry {concept_id} definition must match terminology brief"
            )
        positive_brief = require_dict(
            brief_term.get("positive_example"),
            f"terminology brief {source_term}.positive_example",
        )
        if not any(
            require_dict(example, f"{concept_id}.positive_examples[]").get(
                "source"
            )
            == positive_brief.get("source")
            and example.get("target") == positive_brief.get("target")
            for example in require_list(
                context.get("positive_examples"),
                f"{concept_id}.context.positive_examples",
            )
        ):
            raise AssertionError(
                f"Entry {concept_id} positive example must match terminology brief"
            )
        negative_brief = require_dict(
            brief_term.get("negative_example"),
            f"terminology brief {source_term}.negative_example",
        )
        if not any(
            require_dict(example, f"{concept_id}.negative_examples[]").get(
                "source"
            )
            == negative_brief.get("source")
            and example.get("bad_target") == negative_brief.get("bad_target")
            and negative_brief.get("reason")
            in {value for value in example.values() if isinstance(value, str)}
            for example in require_list(
                context.get("negative_examples"),
                f"{concept_id}.context.negative_examples",
            )
        ):
            raise AssertionError(
                f"Entry {concept_id} negative example must match terminology brief"
            )
        for forbidden_term in expected_forbidden:
            matching_forbidden_events = [
                event
                for event in events
                if event.get("op") == "add_forbidden"
                and event.get("concept_id") == concept_id
                and event.get("forbidden_term") == forbidden_term
            ]
            if len(matching_forbidden_events) != 1:
                raise AssertionError(
                    f"Delta events must add forbidden term {forbidden_term!r} "
                    "exactly once "
                    f"for {concept_id}"
                )
        concept_conflicts = conflicts_by_concept_id.get(concept_id, [])
        if conflicting_target is None:
            if entry.get("status") != "approved":
                raise AssertionError(
                    f"Entry {concept_id} without a conflict must be approved"
                )
            if concept_conflicts:
                raise AssertionError(
                    f"Entry {concept_id} has an unexpected canonical conflict"
                )
            continue
        if entry.get("status") not in {"conflict", "needs_confirmation"}:
            raise AssertionError(
                f"Entry {concept_id} must expose its unresolved conflict status"
            )
        expected_candidates = {preferred_target, conflicting_target}
        matching_conflicts = [
            conflict
            for conflict in concept_conflicts
            if set(
                require_list(
                    conflict.get("competing_targets"),
                    f"{concept_id}.conflict.competing_targets",
                )
            )
            == expected_candidates
            and conflict.get("status") == "open"
            and conflict.get("blocking") is True
        ]
        if len(concept_conflicts) != 1 or len(matching_conflicts) != 1:
            raise AssertionError(
                f"Entry {concept_id} must have one matching open conflict"
            )
        conflict_id = matching_conflicts[0]["conflict_id"]
        matching_raise_events = [
            event
            for event in events
            if event.get("op") == "raise_conflict"
            and event.get("conflict_id") == conflict_id
        ]
        if len(matching_raise_events) != 1:
            raise AssertionError(
                f"Delta events must raise canonical conflict {conflict_id} "
                "exactly once"
            )
    return payload


def check_mqm_content(
    path: Path, sample_path: Path, seeded_defects: list
) -> None:
    payload = json.loads(require_file(path))
    issues = require_list(payload.get("issues"), "review.json.issues")
    for issue in issues:
        if issue.get("resolution_status") != "open":
            raise AssertionError(
                "mqm-review-json issues must have resolution_status open"
            )
    sample = require_dict(
        json.loads(require_file(sample_path)), "MQM review sample"
    )
    segments = {
        require_nonempty_string(
            segment.get("segment_id"), "segment_id"
        ): segment
        for segment in require_list(sample.get("segments"), "sample.segments")
    }
    for issue in issues:
        segment_id = require_nonempty_string(
            issue.get("segment_id"), "issue.segment_id"
        )
        segment = segments.get(segment_id)
        if segment is None:
            raise AssertionError(
                f"review.json references unknown segment: {segment_id}"
            )
        for quote_field, segment_field in [
            ("source_quote", "source"),
            ("target_quote", "target"),
        ]:
            quote = require_nonempty_string(
                issue.get(quote_field), f"issue.{quote_field}"
            )
            segment_text = require_nonempty_string(
                segment.get(segment_field),
                f"segment.{segment_field}",
            )
            if quote not in segment_text:
                raise AssertionError(
                    f"review.json {quote_field} is not evidence from "
                    f"segment {segment_id}"
                )

    for defect in seeded_defects:
        if any(
            issue.get("segment_id") == defect.get("segment_id")
            and issue.get("category") == defect.get("category")
            and issue.get("severity") == defect.get("severity")
            and require_nonempty_string(
                defect.get("source_anchor"), "seeded defect.source_anchor"
            )
            in require_nonempty_string(
                issue.get("source_quote"), "issue.source_quote"
            )
            and require_nonempty_string(
                defect.get("target_anchor"), "seeded defect.target_anchor"
            ).casefold()
            in require_nonempty_string(
                issue.get("target_quote"), "issue.target_quote"
            ).casefold()
            for issue in issues
        ):
            continue
        raise AssertionError(
            "review.json is missing seeded defect: "
            f"segment={defect.get('segment_id')!r}, "
            f"category={defect.get('category')!r}, "
            f"severity={defect.get('severity')!r}"
        )


QA_FAILURE_HEADING = "## blocking failure records"
QA_FAILURE_RECORD_RE = re.compile(
    r"^-\s+\[(FAIL|PASS)\]\s+([a-z][a-z-]*)\s+\|\s*(.+?)\s*$"
)
QA_FAILURE_FIELD_COUNTS = {
    "forbidden-term": 1,
    "missing-approved-term": 2,
    "open-conflict": 1,
    "major-mqm": 4,
    "projection-mismatch": 1,
    "qa-check": 3,
}
PENDING_SIGNOFF_SENTENCE = (
    "Human or subject-matter expert sign-off still required."
)


def parse_qa_failure_records(content: str) -> set[tuple[str, ...]]:
    lines = content.splitlines()
    heading_indexes = [
        index
        for index, line in enumerate(lines)
        if line.strip().casefold() == QA_FAILURE_HEADING
    ]
    if len(heading_indexes) != 1:
        raise AssertionError(
            "qa.md must include exactly one '## Blocking failure records' "
            "section"
        )

    records: set[tuple[str, ...]] = set()
    for line in lines[heading_indexes[0] + 1 :]:
        stripped = line.strip()
        if stripped.startswith("#"):
            break
        if not stripped:
            continue
        match = QA_FAILURE_RECORD_RE.fullmatch(stripped)
        if match is None:
            raise AssertionError(
                "Blocking failure records must use the documented syntax"
            )
        status, kind, raw_fields = match.groups()
        fields = [field.strip() for field in raw_fields.split("|")]
        expected_count = QA_FAILURE_FIELD_COUNTS.get(kind)
        if (
            expected_count is None
            or len(fields) != expected_count
            or not all(fields)
        ):
            raise AssertionError(f"Invalid QA failure record: {stripped}")
        key = (kind, *(field.casefold() for field in fields))
        if key in records:
            raise AssertionError(f"Duplicate QA failure record: {stripped}")
        if status != "FAIL":
            raise AssertionError("Blocking failure records may only use [FAIL]")
        records.add(key)
    if not records:
        raise AssertionError("qa.md must include blocking failure records")
    return records


def check_qa_content(path: Path, run_dir: Path | None) -> None:
    content = require_file(path)
    lowered = content.casefold()
    for required in [
        "major",
        "blocking",
        "forbidden",
        "conflict",
        "unresolved",
        "termbase.job.json",
        "termbase.tbx",
        "terminology-review.tsv",
    ]:
        if required not in lowered:
            raise AssertionError(f"qa.md must mention {required!r}")
    for checked_file in [
        "translation.md",
        "termbase.job.json",
        "termbase.delta.jsonl",
        "termbase.tbx",
        "terminology-review.tsv",
        "review.json",
    ]:
        if checked_file not in lowered:
            raise AssertionError(
                f"qa.md must identify checked file {checked_file!r}"
            )
    if content.count(PENDING_SIGNOFF_SENTENCE) != 1:
        raise AssertionError(
            "qa.md must include exactly one exact pending human-review sentence"
        )
    if run_dir is None:
        raise AssertionError(
            "--run-dir is required for final-qa-contract package checks"
        )
    package = run_dir / "evals" / "files" / "qa-package"
    translation = require_file(package / "translation.md")
    termbase = check_termbase_json(package / "termbase.job.json")
    check_delta_jsonl(package / "termbase.delta.jsonl", termbase)
    projection_mismatches: list[str] = []
    for file_name, structure_checker, projection_checker in [
        (
            "termbase.tbx",
            lambda: check_tbx(package / "termbase.tbx"),
            lambda: check_tbx(package / "termbase.tbx", termbase),
        ),
        (
            "terminology-review.tsv",
            lambda: check_terminology_review_tsv(
                package / "terminology-review.tsv"
            ),
            lambda: check_terminology_review_tsv(
                package / "terminology-review.tsv", termbase
            ),
        ),
    ]:
        structure_checker()
        try:
            projection_checker()
        except AssertionError:
            projection_mismatches.append(file_name)
    if set(projection_mismatches) != {
        "termbase.tbx",
        "terminology-review.tsv",
    }:
        raise AssertionError(
            "final-qa-contract fixture must contain seeded TBX and TSV "
            "projection mismatches"
        )
    forbidden_hits = [
        term
        for term in termbase["_checked_forbidden_terms"]
        if term.casefold() in translation.casefold()
    ]
    if not forbidden_hits:
        raise AssertionError(
            "final-qa-contract fixture must contain a forbidden terminology hit"
        )
    missing_approved_terms: list[tuple[str, str]] = []
    for entry in require_list(termbase.get("entries"), "entries"):
        if entry.get("status") != "approved":
            continue
        concept_id = require_nonempty_string(
            entry.get("concept_id"), "entry.concept_id"
        )
        target = require_dict(entry.get("target"), f"{concept_id}.target")
        preferred = require_nonempty_string(
            target.get("preferred"), f"{concept_id}.target.preferred"
        )
        accepted_targets = {
            preferred,
            *{
                require_nonempty_string(
                    value, f"{concept_id}.target.allowed_variants[]"
                )
                for value in require_list(
                    target.get("allowed_variants", []),
                    f"{concept_id}.target.allowed_variants",
                )
            },
        }
        forbidden = {
            require_nonempty_string(item.get("term"), "forbidden.term")
            for item in require_list(
                target.get("forbidden", []), f"{concept_id}.target.forbidden"
            )
        }
        if not any(
            term.casefold() in translation.casefold()
            for term in accepted_targets
        ) and any(
            term.casefold() in translation.casefold() for term in forbidden
        ):
            missing_approved_terms.append((concept_id, preferred))
    if not missing_approved_terms:
        raise AssertionError(
            "final-qa-contract fixture must omit an applicable approved term"
        )
    open_conflicts = [
        require_dict(conflict, "conflicts[]")
        for conflict in require_list(termbase.get("conflicts"), "conflicts")
        if conflict.get("status") == "open" and conflict.get("blocking") is True
    ]
    if not open_conflicts:
        raise AssertionError(
            "final-qa-contract fixture must contain an open blocking conflict"
        )
    review_path = package / "review.json"
    check_review_json(review_path)
    review = json.loads(require_file(review_path))
    major_issues = [
        require_dict(issue, "review.json issue")
        for issue in require_list(review.get("issues"), "review.json.issues")
        if issue.get("severity") == "Major"
        and issue.get("resolution_status") == "open"
    ]
    if not major_issues:
        raise AssertionError(
            "final-qa-contract fixture must contain a Major MQM issue"
        )
    expected_records = {
        *{("forbidden-term", term.casefold()) for term in forbidden_hits},
        *{
            (
                "missing-approved-term",
                concept_id.casefold(),
                preferred.casefold(),
            )
            for concept_id, preferred in missing_approved_terms
        },
        *{
            (
                "open-conflict",
                require_nonempty_string(
                    conflict.get("conflict_id"), "conflict.conflict_id"
                ).casefold(),
            )
            for conflict in open_conflicts
        },
        *{
            (
                "major-mqm",
                require_nonempty_string(
                    issue.get("issue_id"), "issue.issue_id"
                ).casefold(),
                require_nonempty_string(
                    issue.get("segment_id"), "issue.segment_id"
                ).casefold(),
                require_nonempty_string(
                    issue.get("category"), "issue.category"
                ).casefold(),
                require_nonempty_string(
                    issue.get("target_quote"), "issue.target_quote"
                ).casefold(),
            )
            for issue in major_issues
        },
        *{
            ("projection-mismatch", file_name.casefold())
            for file_name in projection_mismatches
        },
    }
    actual_records = parse_qa_failure_records(content)
    if actual_records != expected_records:
        raise AssertionError(
            "qa.md blocking failure records do not match the package: "
            f"missing={sorted(expected_records - actual_records)}, "
            f"unexpected={sorted(actual_records - expected_records)}"
        )


def case_input_path(case: dict, run_dir: Path | None, file_name: str) -> Path:
    if run_dir is None:
        raise AssertionError(
            f"--run-dir is required to validate {case['id']} inputs"
        )
    matches = [
        run_dir / relative
        for relative in case.get("files", [])
        if Path(relative).name == file_name
    ]
    if len(matches) != 1:
        raise AssertionError(
            f"Eval {case['id']} must reference exactly one {file_name}"
        )
    return matches[0]


def check_case_specific(
    case: dict, outputs: Path, run_dir: Path | None
) -> None:
    case_id = case["id"]
    if case_id == "structured-markdown-translation":
        check_structured_translation(
            outputs / "translation.md",
            case_input_path(case, run_dir, "structured-doc.md"),
        )
    elif case_id == "terminology-glossary-conflict":
        payload = check_terminology_content(
            outputs / "termbase.job.json",
            outputs / "termbase.delta.jsonl",
            case_input_path(case, run_dir, "terminology-brief.json"),
        )
        check_tbx(outputs / "termbase.tbx", payload)
        check_terminology_review_tsv(
            outputs / "terminology-review.tsv", payload
        )
    elif case_id == "mqm-review-json":
        check_mqm_content(
            outputs / "review.json",
            case_input_path(case, run_dir, "mqm-review-sample.json"),
            require_list(case.get("seeded_defects"), "seeded_defects"),
        )
    elif case_id == "final-qa-contract":
        check_qa_content(outputs / "qa.md", run_dir)


def check_forbidden_paths(case: dict, run_dir: Path | None) -> None:
    if run_dir is None:
        if case.get("forbidden_created_paths"):
            raise AssertionError(
                "--run-dir is required for forbidden path checks"
            )
        return
    for relative in case.get("forbidden_created_paths", []):
        forbidden = run_dir / relative
        if forbidden.exists():
            raise AssertionError(f"Forbidden path was created: {forbidden}")


def check_response_patterns(case: dict, response_path: Path | None) -> None:
    required = case.get("required_response_patterns", [])
    forbidden = case.get("forbidden_response_patterns", [])
    if not required and not forbidden:
        return
    if response_path is None:
        raise AssertionError(
            "--response is required for response pattern checks"
        )
    response = response_path.read_text(encoding="utf-8").casefold()
    for pattern in required:
        if pattern.casefold() not in response:
            raise AssertionError(
                f"Required response pattern not found: {pattern}"
            )
    for pattern in forbidden:
        if pattern.casefold() in response:
            raise AssertionError(f"Forbidden response pattern found: {pattern}")


def check_workspace_changes(
    case: dict, workspace_diff_path: Path | None
) -> None:
    if workspace_diff_path is None:
        raise AssertionError(
            "--workspace-diff is required for workspace change checks"
        )
    payload = require_dict(
        json.loads(require_file(workspace_diff_path)), "workspace diff"
    )
    changes: dict[str, set[str]] = {
        key: {
            require_nonempty_string(item, f"workspace diff.{key}[]")
            for item in require_list(payload.get(key), f"workspace diff.{key}")
        }
        for key in ["added", "removed", "modified"]
    }
    allowed_added = (
        set()
        if case.get("forbid_workspace_changes")
        else {
            (Path("outputs") / expected).as_posix()
            for expected in case.get("expected_files", [])
        }
    )
    unexpected_added = changes["added"] - allowed_added
    missing_added = allowed_added - changes["added"]
    if (
        unexpected_added
        or missing_added
        or changes["removed"]
        or changes["modified"]
    ):
        raise AssertionError(
            "Eval workspace changes violate the output allowlist: "
            f"unexpected_added={sorted(unexpected_added)}, "
            f"missing_added={sorted(missing_added)}, "
            f"removed={sorted(changes['removed'])}, "
            f"modified={sorted(changes['modified'])}"
        )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Validate expected files for an enterprise translation eval case."
    )
    parser.add_argument(
        "--evals", required=True, type=Path, help="Path to evals.json"
    )
    parser.add_argument("--case", required=True, help="Eval case id")
    parser.add_argument(
        "--outputs", required=True, type=Path, help="Output directory"
    )
    parser.add_argument("--run-dir", type=Path, help="Eval run directory")
    parser.add_argument(
        "--response", type=Path, help="Agent response text file"
    )
    parser.add_argument(
        "--workspace-diff",
        type=Path,
        help="Workspace file changes captured around the Copilot run",
    )
    args = parser.parse_args(argv)

    case = load_eval_case(args.evals, args.case)
    if not (
        case.get("expected_files")
        or case.get("forbidden_created_paths")
        or case.get("forbid_workspace_changes")
        or case.get("required_response_patterns")
        or case.get("forbidden_response_patterns")
    ):
        raise AssertionError(f"Eval {case['id']} has no objective checks")
    outputs = args.outputs
    for expected in case.get("expected_files", []):
        path = outputs / expected
        if expected == "review.json":
            check_review_json(path)
        elif expected == "termbase.job.json":
            check_termbase_json(path)
        elif expected == "termbase.delta.jsonl":
            check_delta_jsonl(path)
        elif expected == "termbase.tbx":
            check_tbx(path)
        elif expected == "terminology-review.tsv":
            check_terminology_review_tsv(path)
        elif expected.endswith(".md"):
            check_markdown(path)
        else:
            require_file(path)
    check_case_specific(case, outputs, args.run_dir)
    check_forbidden_paths(case, args.run_dir)
    check_workspace_changes(case, args.workspace_diff)
    check_response_patterns(case, args.response)

    print(
        json.dumps(
            {
                "case": case["id"],
                "checked_files": case.get("expected_files", []),
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as exc:  # noqa: BLE001
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1) from None
