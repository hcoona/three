"""Rights-segregated symbolic intake and fail-closed TEST exclusion."""

from __future__ import annotations

import csv
import hashlib
import json
import re
import tarfile
import unicodedata
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter
from dataclasses import dataclass
from pathlib import PurePosixPath
from typing import TYPE_CHECKING, Literal, cast

from music21 import converter

from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation.alignment import (
    TEST_GOLD_IDS,
    atomic_write_text,
    check_no_symlink_under,
)
from sheet_music_omr.harness.corpora import (
    MAX_DOCUMENT_BYTES,
    PDMX_LICENSES,
    CorpusError,
    extract_piano,
    lieder_candidates,
    mxl_root,
)
from sheet_music_omr.harness.gold import SCORE_NAME, gold_id
from sheet_music_omr.harness.musescore import sha256_file
from sheet_music_omr.harness.screen import screen
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import (
    Arpeggiate,
    ArpeggioRelation,
    Articulation,
    AssembledScore,
    Barline,
    ClefChange,
    CrossStaff,
    DashesStart,
    Dynamic,
    DynamicText,
    EndingOption,
    Fermata,
    HairpinStart,
    LeftRepeatOption,
    MeasureRest,
    Note,
    Ornament,
    OttavaStart,
    PedalAction,
    PedalLine,
    PedalSign,
    PedalSignKind,
    Rehearsal,
    Rest,
    RightOption,
    SingleTremolo,
    SlurRelation,
    Tempo,
    TremoloRelation,
    Tuplet,
    Words,
)
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.musicxml_reverse import MusicXmlError, from_musicxml_text
from sheet_music_omr.parser import parse_document
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Iterator, Mapping
    from pathlib import Path

    from sheet_music_omr.model import Event, MeasureBlock

type RightsVariant = Literal["permissive", "nc_sa", "private_traindev"]

EXPECTED_TEST_EXCLUSION_SHA256 = (
    "65a798c51095c8cd6f37374428e6e9a39cba7954d33fe047b49959f87538a80b"
)

_MIN_LETTERS = 2
_MIN_FIXTURE_MEASURES = 4
_CSV_FIELD_LIMIT = 2**31 - 1
_STOP_WORDS = frozenset(
    [
        "a",
        "an",
        "and",
        "das",
        "de",
        "der",
        "die",
        "du",
        "in",
        "la",
        "le",
        "of",
        "on",
        "the",
        "und",
    ]
)
_PLACEHOLDERS = frozenset(
    {
        "untitled",
        "piano solo",
        "unknown",
        "unknown composer",
        "anonymous",
        "traditional",
        "various",
        "various artists",
    }
)
COMPOSER_TRANSLITERATION_ALIASES: dict[str, str] = {
    "tschaikowsky": "tchaikovsky",
    "tschaikovsky": "tchaikovsky",
    "chaikovsky": "tchaikovsky",
    "tchaikowsky": "tchaikovsky",
    "rakhmaninov": "rachmaninoff",
    "rachmaninov": "rachmaninoff",
    "scriabine": "scriabin",
    "skryabin": "scriabin",
    "moussorgsky": "mussorgsky",
    "musorgsky": "mussorgsky",
    "prokofieff": "prokofiev",
    "schostakowitsch": "shostakovich",
    "haendel": "han" + "del",
}
_SINGLE_COMPOSER_CATALOGUE_AUTHORITIES: dict[str, str] = {
    "bwv": "bach",
    "buxwv": "buxtehude",
    "kv": "mozart",
    "hob": "haydn",
    "sz": "bartok",
    "rv": "vivaldi",
    "hwv": "han" + "del",
}
_SLASH_CATALOGUE_SUB = re.compile(
    r"\b(bwv|buxwv|woo|op(?:us)?|kv?|hob|hwv|rv|sz|l|h|d|s)\.?\s*"
    r"((?:[ivxlcdm]+[:.\s/-]+)?\d+[a-z]?)\s*/\s*(\d+[a-z]?)\b",
    re.IGNORECASE,
)
_CATALOGUES = re.compile(
    r"\b(bwv|buxwv|woo|op(?:us)?|kv?|hob|hwv|rv|sz|l|h|d|s)\s*"
    r"(?:([ivxlcdm]+)\s+)?(\d+[a-z]?)(?:\s+(?:no|nr)\s+(\d+[a-z]?))?\b"
)
_STANDALONE_NO = re.compile(r"\b(?:no|nr)\s+(\d+[a-z]?)\b")
_SHA256 = re.compile(r"[0-9a-f]{64}")
_STATUS_NAME = re.compile(
    r"^\s*(?:(\d+)\s*[-\u2013\u2014]\s*)?"
    r"(.+?)\s+[-\u2013\u2014]\s+(.+)\s*$"
)
_FORBIDDEN_CORPORA = frozenset(
    {
        "grandstaff",
        "cameragrandstaff",
        "fpgrandstaff",
        "olimpic",
        "polishscores",
        "smb",
    }
)
_PRIVATE_IDS = frozenset(gold_id(n) for n in range(1, 24)) - TEST_GOLD_IDS
_RIGHTS_VARIANTS = frozenset({"permissive", "nc_sa", "private_traindev"})
_PUBLIC_LICENSES = PDMX_LICENSES | {"CC0-1.0"}
_DIGEST_FIELDS = (
    "raw_sha256",
    "extracted_xml_sha256",
    "exported_xml_sha256",
    "accepted_xml_sha256",
    "facsimile_pdf_sha256",
)


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _words(text: str) -> str:
    folded = unicodedata.normalize("NFKD", text.casefold())
    return " ".join(
        re.findall(
            r"[^\W_]+",
            "".join(c for c in folded if not unicodedata.combining(c)),
        )
    )


def _informative(value: str) -> bool:
    text = _words(value)
    return (
        text not in _PLACEHOLDERS
        and sum(c.isalpha() for c in text) >= _MIN_LETTERS
    )


def _ordinal_sets(
    tokens: frozenset[str],
) -> tuple[set[str], set[str], set[str]]:
    standalone = {
        tok.removeprefix("no:") for tok in tokens if tok.startswith("no:")
    }
    subs = {tok.partition("#")[2] for tok in tokens if "#" in tok}
    systems = {
        tok.split(":", 1)[0] for tok in tokens if not tok.startswith("no:")
    }
    return standalone, standalone | subs, systems


def _bases_and_subs(
    tokens: frozenset[str],
) -> dict[str, set[str]]:
    by_base: dict[str, set[str]] = {}
    for tok in tokens:
        if tok.startswith("no:"):
            continue
        base, _, sub = tok.partition("#")
        by_base.setdefault(base, set())
        if sub:
            by_base[base].add(sub)
    return by_base


def _compatible_catalogue(left: frozenset[str], right: frozenset[str]) -> bool:
    l_bases = _bases_and_subs(left)
    r_bases = _bases_and_subs(right)
    for base in l_bases.keys() & r_bases.keys():
        l_subs, r_subs = l_bases[base], r_bases[base]
        if not l_subs or not r_subs or (l_subs & r_subs):
            return True
    return False


@dataclass(frozen=True, slots=True)
class WorkIdentity:
    """Normalized composer surname, distinctive title and catalogue tokens."""

    composer_surname: str
    title_tokens: frozenset[str]
    catalogue_tokens: frozenset[str]

    def conflicts_with(
        self, other: WorkIdentity, *, conservative_exclusion: bool = False
    ) -> bool:
        """Return whether work numbers or same-system catalogues clash."""
        if self.composer_surname != other.composer_surname:
            return True
        if not (self.catalogue_tokens and other.catalogue_tokens):
            return False
        l_no, l_all, l_sys = _ordinal_sets(self.catalogue_tokens)
        r_no, r_all, r_sys = _ordinal_sets(other.catalogue_tokens)
        compatible_cat = _compatible_catalogue(
            self.catalogue_tokens, other.catalogue_tokens
        )
        if (
            l_no
            and r_no
            and not (l_no & r_no)
            and not (conservative_exclusion and compatible_cat)
        ):
            return True
        if (
            not conservative_exclusion
            and l_all
            and r_all
            and not (l_all & r_all)
        ):
            return True
        if compatible_cat:
            return False
        return bool(l_sys & r_sys)

    def matches(
        self, other: WorkIdentity, *, conservative_exclusion: bool = False
    ) -> bool:
        """Find the same composer and work by title or catalogue."""
        if self.composer_surname != other.composer_surname:
            return False
        if self.conflicts_with(
            other, conservative_exclusion=conservative_exclusion
        ):
            return False
        if _compatible_catalogue(self.catalogue_tokens, other.catalogue_tokens):
            return True
        return bool(
            self.title_tokens
            and other.title_tokens
            and (
                self.title_tokens <= other.title_tokens
                or other.title_tokens <= self.title_tokens
            )
        )


def canonicalize_composer_surname(raw_surname: str) -> str:
    """Map known classical transliteration variants to one canonical surname."""
    norm = _words(raw_surname)
    return COMPOSER_TRANSLITERATION_ALIASES.get(norm, norm)


def normalize_work_identity(composer: str, title: str) -> WorkIdentity | None:
    """Return a work identity, or None for invalid/conflicting metadata."""
    if not (_informative(composer) and _informative(title)):
        return None
    cleaned_composer = re.sub(r"\([^)]*\)|\[[^\]]*\]", " ", composer)
    surname_parts = [
        tok
        for tok in _words(cleaned_composer.split(",", 1)[0]).split()
        if tok.isalpha() and tok not in {"jr", "sr"}
    ]
    if not surname_parts or len(surname_parts[-1]) < 2:  # noqa: PLR2004
        return None
    surname = canonicalize_composer_surname(surname_parts[-1])
    if surname in _PLACEHOLDERS:
        return None
    title_text = _words(_SLASH_CATALOGUE_SUB.sub(r"\1 \2 no \3", title))
    cat_matches = _CATALOGUES.findall(title_text)
    for raw_prefix, _, _, _ in cat_matches:
        authority = _SINGLE_COMPOSER_CATALOGUE_AUTHORITIES.get(raw_prefix)
        if authority is not None and surname != authority:
            return None
    stripped_title = _CATALOGUES.sub("", title_text)
    standalone_numbers = _STANDALONE_NO.findall(stripped_title)
    catalogues = frozenset(
        [
            *(
                f"{ {'kv': 'k', 'opus': 'op'}.get(prefix, prefix) }:"
                + (f"{series}/" if series else "")
                + number
                + (f"#{sub}" if sub else "")
                for prefix, series, number, sub in cat_matches
            ),
            *(f"no:{num}" for num in standalone_numbers),
        ]
    )
    tokens = frozenset(
        token
        for token in _STANDALONE_NO.sub("", stripped_title).split()
        if token not in _STOP_WORDS
        and token.isalpha()
        and len(token) >= _MIN_LETTERS
    )
    return (
        WorkIdentity(surname, tokens, catalogues)
        if tokens or catalogues
        else None
    )


def _status_identity(value: object, number: int) -> WorkIdentity | None:
    if not isinstance(value, str):
        return None
    name = value.replace("\\", "/").rsplit("/", 1)[-1]
    parsed = _STATUS_NAME.fullmatch(
        re.sub(r"\.pdf$", "", name, flags=re.IGNORECASE)
    )
    if parsed is None or (parsed[1] is not None and int(parsed[1]) != number):
        return None
    return normalize_work_identity(parsed[2], parsed[3])


def _verified_exclusion(data: bytes) -> dict[str, tuple[str, str]]:
    if _sha(data) != EXPECTED_TEST_EXCLUSION_SHA256:
        message = "TEST exclusion manifest SHA-256 does not match the freeze"
        raise StopConditionError(message)
    manifest = json.loads(data)
    if (
        not isinstance(manifest, dict)
        or manifest.get("schema") != "omr-test-exclusion-v1"
        or manifest.get("test_piece_count") != len(TEST_GOLD_IDS)
        or not isinstance(manifest.get("pieces"), list)
    ):
        message = "invalid TEST exclusion manifest"
        raise StopConditionError(message)
    digests: dict[str, tuple[str, str]] = {}
    for row in manifest["pieces"]:
        gid = row["gold_id"]
        xml, pdf = row["accepted_xml_sha256"], row["facsimile_pdf_sha256"]
        if gid in digests or not (
            _SHA256.fullmatch(xml) and _SHA256.fullmatch(pdf)
        ):
            message = "invalid or repeated TEST exclusion entry"
            raise StopConditionError(message)
        digests[gid] = (xml, pdf)
    if digests.keys() != TEST_GOLD_IDS:
        message = "TEST exclusion manifest does not contain the frozen TEST set"
        raise StopConditionError(message)
    return digests


def _status_rows(path: Path) -> list[object]:
    data = json.loads(path.read_bytes())
    rows = (
        data.get("batches", data.get("pieces"))
        if isinstance(data, dict)
        else data
    )
    if not isinstance(rows, list):
        message = "invalid TEST batch-status records"
        raise StopConditionError(message)
    return rows


def _test_status(path: Path) -> dict[str, dict[str, object]]:
    found: dict[str, dict[str, object]] = {}
    for row in _status_rows(path):
        if not isinstance(row, dict):
            continue
        number = row.get("number")
        if type(number) is not int or gold_id(number) not in TEST_GOLD_IDS:
            continue
        gid = gold_id(number)
        if gid in found or row.get("accepted") is not True:
            message = "repeated or unaccepted TEST batch-status record"
            raise StopConditionError(message)
        found[gid] = row
    if found.keys() != TEST_GOLD_IDS:
        message = "missing TEST batch-status records"
        raise StopConditionError(message)
    return found


def _cache_component(value: object) -> str:
    if (
        not isinstance(value, str)
        or value in {"", ".", ".."}
        or any(c in value for c in "/\\:")
    ):
        message = "invalid TEST gold-cache path component"
        raise StopConditionError(message)
    return value


class TestExclusionGate:
    """Verify frozen TEST inputs without parsing their notation."""

    __test__ = False

    def __init__(
        self, test_exclusion: Path, batch_status: Path, gold_cache: Path
    ) -> None:
        """Load the frozen manifest, byte hashes and work identities."""
        identities: set[WorkIdentity] = set()
        try:
            pairs = _verified_exclusion(test_exclusion.read_bytes())
            for gid, row in _test_status(batch_status).items():
                workspace = _cache_component(row.get("workspace"))
                build = _cache_component(row.get("latest_build"))
                path = gold_cache / workspace / build / SCORE_NAME
                try:
                    check_no_symlink_under(path, gold_cache)
                except StopConditionError as error:
                    message = f"invalid TEST gold-cache path for {gid}"
                    raise StopConditionError(message) from error
                file_sha = sha256_file(path)
                allowed_xml_shas = {pairs[gid][0]}
                if gid == "gold-01":
                    # Include PR #1705's one-time staged tie-repaired gold-01
                    # build alongside the pre-repair W3 manifest digest so both
                    # variants are verified and excluded.
                    allowed_xml_shas.add(
                        "b7eb0fee18d12582d838a359d3eb648b42c0a02382971634cc50eec5558bc886"
                    )
                if file_sha not in allowed_xml_shas:
                    message = f"TEST gold-cache digest mismatch for {gid}"
                    raise StopConditionError(message)
                pairs[gid] = (*pairs[gid], file_sha)  # type: ignore[assignment]
                number = int(gid.removeprefix("gold-"))
                aliases = {
                    identity
                    for field in ("source", "workspace")
                    if (identity := _status_identity(row.get(field), number))
                    is not None
                }
                if not aliases:
                    message = f"missing non-notation TEST identity for {gid}"
                    raise StopConditionError(message)
                identities.update(aliases)
        except (OSError, ValueError, KeyError, TypeError) as error:
            message = "cannot verify frozen TEST inputs"
            raise StopConditionError(message) from error
        self._digests = frozenset(
            digest for pair in pairs.values() for digest in pair
        )
        self._identities = frozenset(identities)
        self._batch_status = batch_status
        self._gold_cache = gold_cache

    def verifies_train_dev(self, gid: str, raw_digest: str) -> bool:
        """Check that claimed TRAIN_DEV bytes match the accepted local cache."""
        if gid not in _PRIVATE_IDS:
            return False
        try:
            number = int(gid.removeprefix("gold-"))
            matches = [
                row
                for row in _status_rows(self._batch_status)
                if isinstance(row, dict) and row.get("number") == number
            ]
            if len(matches) != 1 or matches[0].get("accepted") is not True:
                return False
            workspace = _cache_component(matches[0].get("workspace"))
            build = _cache_component(matches[0].get("latest_build"))
            path = self._gold_cache / workspace / build / SCORE_NAME
            check_no_symlink_under(path, self._gold_cache)
            return sha256_file(path) == raw_digest
        except OSError, ValueError, StopConditionError:
            return False

    def matches_digest(
        self, *, gold_id: str | None = None, digests: Iterable[str] = ()
    ) -> bool:
        """Check identifiers or hashes before a candidate payload is parsed."""
        return gold_id in TEST_GOLD_IDS or any(
            digest.lower() in self._digests for digest in digests
        )

    def exclusion_reason(
        self,
        composer: str,
        title: str,
        *,
        gold_id: str | None = None,
        digests: Iterable[str] = (),
    ) -> str | None:
        """Return the exclusion counter key, or None for a distinct work."""
        if self.matches_digest(gold_id=gold_id, digests=digests):
            return "excluded_test_match"
        identity = normalize_work_identity(composer, title)
        if identity is None:
            return "excluded_missing_metadata"
        if any(
            identity.matches(test, conservative_exclusion=True)
            for test in self._identities
        ):
            return "excluded_test_match"
        return None

    def audit_admitted_manifest(
        self, rows: Iterable[Mapping[str, object]]
    ) -> None:
        """Stop if an admitted row matches TEST or cannot be audited."""
        for row in rows:
            composer, title = row.get("composer"), row.get("title")
            gid = row.get("source_id")
            if not isinstance(gid, str) or gid not in TEST_GOLD_IDS:
                gid = row.get("gold_id")
            digests = tuple(
                digest
                for field in _DIGEST_FIELDS
                if isinstance(digest := row.get(field), str)
            )
            reason = self.exclusion_reason(
                composer if isinstance(composer, str) else "",
                title if isinstance(title, str) else "",
                gold_id=gid if isinstance(gid, str) else None,
                digests=digests,
            )
            if reason is not None:
                message = (
                    f"admitted source manifest failed TEST audit: {reason}"
                )
                raise StopConditionError(message)


def verify_nc_sa_repo_license(repo_dir: Path) -> None:
    """Require a readable repository-level CC BY-NC-SA 4.0 reuse license."""
    marker = re.compile(
        r"\bcc\s+by-nc-sa\s+4\.0\b|creativecommons\.org/licenses/by-nc-sa/4\.0\b",
        re.IGNORECASE,
    )
    try:
        for path in sorted(repo_dir.glob("LICENSE*")):
            if (
                path.is_file()
                and not path.is_symlink()
                and marker.search(path.read_text(encoding="utf-8"))
            ):
                return
    except (OSError, UnicodeError) as error:
        message = "rights_unverified: cannot read the repository license"
        raise StopConditionError(message) from error
    message = (
        "rights_unverified: repository has no verified CC BY-NC-SA 4.0 license"
    )
    raise StopConditionError(message)


@dataclass(frozen=True, slots=True)
class SourceCandidate:
    """A local payload and its source-specific rights and identity evidence."""

    source_id: str
    corpus: str
    rights_variant: RightsVariant
    license_url: str
    raw: bytes
    composer: str | None = None
    title: str | None = None
    gold_id: str | None = None
    repo_dir: Path | None = None
    decode: Callable[[bytes], bytes] | None = None
    piano_extract: bool = False


@dataclass(frozen=True, slots=True)
class ProfiledSource:
    """An admitted source and its canonical notation, ready for W4b."""

    manifest_row: dict[str, object]
    score: AssembledScore
    canonical_v2: str
    musicxml: bytes


@dataclass(frozen=True, slots=True)
class IntakeResult:
    """Aggregate outcome and manifest locations for one rights partition."""

    counts: dict[str, int]
    manifest_path: Path
    summary_path: Path


class _CandidateExcludedError(Exception):
    pass


def _check_rights(
    candidate: SourceCandidate, gate: TestExclusionGate, raw_digest: str
) -> None:
    corpus = _words(candidate.corpus)
    if "smb" in corpus.split() or any(
        name in corpus.replace(" ", "") for name in _FORBIDDEN_CORPORA - {"smb"}
    ):
        msg = "forbidden_label_source"
        raise _CandidateExcludedError(msg)
    if candidate.rights_variant == "private_traindev":
        if (
            candidate.decode is None
            and not candidate.piano_extract
            and candidate.gold_id is not None
            and gate.verifies_train_dev(candidate.gold_id, raw_digest)
        ):
            return
    elif candidate.rights_variant == "nc_sa":
        if candidate.repo_dir is not None and candidate.gold_id is None:
            try:
                verify_nc_sa_repo_license(candidate.repo_dir)
            except StopConditionError as error:
                msg = "rights_unverified"
                raise _CandidateExcludedError(msg) from error
            return
    elif (
        candidate.rights_variant == "permissive"
        and candidate.license_url in _PUBLIC_LICENSES
        and candidate.gold_id is None
    ):
        return
    msg = "rights_unverified"
    raise _CandidateExcludedError(msg)


def _metadata(root: ET.Element) -> tuple[str, str]:
    composer = root.findtext("./identification/creator[@type='composer']", "")
    title = root.findtext("./work/work-title") or root.findtext(
        "./movement-title", ""
    )
    return composer, title


def _check_piano(root: ET.Element) -> None:
    instruments = root.findall("./part-list/score-part/score-instrument")
    sounds = [
        item.findtext("instrument-sound", "").casefold() for item in instruments
    ]
    if any(sounds) and all(
        sound.startswith("keyboard.piano") for sound in sounds
    ):
        return
    names = [item.findtext("instrument-name", "") for item in instruments]
    if not any(sounds) and any(
        "piano" in _words(name).split()
        for name in (
            *names,
            root.findtext("./part-list/score-part/part-name", ""),
        )
    ):
        return
    msg = "not_piano"
    raise _CandidateExcludedError(msg)


_DIRECTION_CATEGORIES: dict[type, str] = {
    ClefChange: "clef_changes",
    DashesStart: "words",
    Dynamic: "dynamics",
    DynamicText: "dynamics",
    HairpinStart: "hairpins",
    OttavaStart: "ottavas",
    Rehearsal: "rehearsal",
    Tempo: "tempo",
    Words: "words",
}
_FLAG_CATEGORIES: dict[type, str] = {
    Arpeggiate: "arpeggios",
    ArpeggioRelation: "arpeggios",
    Articulation: "articulations",
    CrossStaff: "cross_staff",
    Fermata: "fermatas",
    Ornament: "ornaments",
    SingleTremolo: "tremolos",
}
_CATEGORY_FIELDS = {
    "N1": ("dynamics",),
    "N2": ("hairpins",),
    "N3": ("words",),
    "N4": ("tempo",),
    "N5": ("pedal",),
    "N6": ("fingering",),
    "N7": ("rehearsal",),
    "N8": ("repeats_voltas",),
    "L1": ("ties",),
    "L2": ("slurs",),
    "L3": ("articulations", "fermatas"),
    "L4": ("arpeggios",),
    "L5": ("ornaments",),
    "D2": ("tuplets",),
    "D3": ("multi_voice_share",),
    "D4": ("cross_staff",),
    "D7": ("tremolos",),
    "P2": ("clef_changes",),
    "P3": ("ottavas",),
    "X1": ("accidentals",),
}
_RARE_CATEGORIES = ("D2", "D4", "P2", "N8")


def _tuplet_count(events: Iterable[Event]) -> int:
    return sum(
        1 + _tuplet_count(event.items)
        for event in events
        if isinstance(event, Tuplet)
    )


def _measure_counts(block: MeasureBlock) -> Counter[str]:
    counts: Counter[str] = Counter()
    counts["tuplets"] = sum(
        _tuplet_count(voice.events) for voice in block.voices
    )
    counts["barlines_repeats_voltas"] = sum(
        isinstance(option, LeftRepeatOption | EndingOption | RightOption)
        for option in block.options
    )
    counts["repeats_voltas"] = sum(
        isinstance(option, LeftRepeatOption | EndingOption)
        or (
            isinstance(option, RightOption) and option.barline is Barline.REPEAT
        )
        for option in block.options
    )
    voices = Counter(voice.staff for voice in block.voices)
    counts["multi_voice_share"] = int(
        any(count > 1 for count in voices.values())
    )
    return counts


def _event_counts(event: object, counts: Counter[str]) -> None:
    if category := _DIRECTION_CATEGORIES.get(type(event)):
        counts[category] += 1
    if isinstance(event, PedalLine):
        counts["pedal"] += event.action is PedalAction.LINE_START
    if isinstance(event, PedalSign):
        counts["pedal"] += event.kind is PedalSignKind.DOWN
    if isinstance(event, MeasureRest):
        counts["fermatas"] += event.fermata is not None
    if isinstance(event, Note | Rest):
        for flag in event.flags:
            _count_flag(flag, counts)
    if isinstance(event, Note):
        counts["fingering"] += sum(
            len(pitch.fingering) for pitch in event.pitches
        )
        counts["ties"] += sum(
            pitch.tie_start is not None for pitch in event.pitches
        )
        counts["accidentals"] += sum(
            pitch.accidental is not None for pitch in event.pitches
        )


def _count_flag(flag: object, counts: Counter[str]) -> None:
    if category := _FLAG_CATEGORIES.get(type(flag)):
        counts[category] += 1
    elif isinstance(flag, SlurRelation):
        counts["slurs"] += flag.role.value == "start"
    elif isinstance(flag, TremoloRelation):
        counts["tremolos"] += flag.role.value == "two-start"


def _coverage(
    score: AssembledScore, targets: Mapping[str, float] | None
) -> tuple[dict[str, float], dict[str, float], float]:
    timeline = build_timeline(score)
    counts: Counter[str] = Counter()
    for frame in timeline.frames:
        counts.update(_measure_counts(frame.block))
    for placed in timeline.events():
        _event_counts(placed.event, counts)
    measures = len(timeline.frames)
    densities = {
        field: counts[field] / measures
        for fields in _CATEGORY_FIELDS.values()
        for field in fields
    }
    densities["barlines_repeats_voltas"] = (
        counts["barlines_repeats_voltas"] / measures
    )
    rates = {
        category: sum(densities[field] for field in fields)
        for category, fields in _CATEGORY_FIELDS.items()
    }
    gaps = _shortfalls(rates, targets)
    weight = 1.0 + sum(
        1.0 + rates[category]
        for category in _RARE_CATEGORIES
        if rates[category]
    )
    return densities | rates, gaps, weight


def _shortfalls(
    rates: Mapping[str, float], targets: Mapping[str, float] | None
) -> dict[str, float]:
    return {
        category: max(0.0, target - rates.get(category, 0.0))
        for category, target in (targets or {}).items()
        if category in _CATEGORY_FIELDS
    }


def _candidate_xml(
    candidate: SourceCandidate, gate: TestExclusionGate
) -> tuple[bytes, dict[str, str]]:
    digests = {"raw_sha256": _sha(candidate.raw)}
    if candidate.source_id in TEST_GOLD_IDS or gate.matches_digest(
        gold_id=candidate.gold_id, digests=digests.values()
    ):
        msg = "excluded_test_match"
        raise _CandidateExcludedError(msg)
    _check_rights(candidate, gate, digests["raw_sha256"])
    if (
        candidate.composer is not None
        and candidate.title is not None
        and (
            reason := gate.exclusion_reason(candidate.composer, candidate.title)
        )
    ):
        raise _CandidateExcludedError(reason)
    try:
        xml = (
            candidate.decode(candidate.raw)
            if candidate.decode
            else candidate.raw
        )
        extracted_digest = _sha(xml)
        if gate.matches_digest(digests=(extracted_digest,)):
            msg = "excluded_test_match"
            raise _CandidateExcludedError(msg)
        if candidate.piano_extract:
            digests["exported_xml_sha256"] = extracted_digest
            xml = extract_piano(xml)
            extracted_digest = _sha(xml)
            if gate.matches_digest(digests=(extracted_digest,)):
                msg = "excluded_test_match"
                raise _CandidateExcludedError(msg)
    except (CorpusError, OSError) as error:
        msg = "source_extract_failed"
        raise _CandidateExcludedError(msg) from error
    digests["extracted_xml_sha256"] = extracted_digest
    return xml, digests


def _candidate_metadata(
    candidate: SourceCandidate, xml: bytes, gate: TestExclusionGate
) -> tuple[str, str]:
    try:
        root = ET.fromstring(xml)  # noqa: S314 - stdlib does not load external entities
    except (ET.ParseError, ValueError, LookupError) as error:
        msg = "unreadable_xml"
        raise _CandidateExcludedError(msg) from error
    xml_composer, xml_title = _metadata(root)
    composer = (
        xml_composer if candidate.composer is None else candidate.composer
    )
    title = xml_title if candidate.title is None else candidate.title
    if any(
        gate.exclusion_reason(c, t) == "excluded_test_match"
        for c, t in (
            (xml_composer, xml_title),
            (composer, title),
            (xml_composer, title),
            (composer, xml_title),
        )
    ):
        msg = "excluded_test_match"
        raise _CandidateExcludedError(msg)
    if gate.exclusion_reason(composer, title):
        msg = "excluded_missing_metadata"
        raise _CandidateExcludedError(msg)
    _check_piano(root)
    return composer, title


def _preflight_candidate(
    candidate: SourceCandidate, gate: TestExclusionGate
) -> tuple[bytes, dict[str, str], str, str]:
    try:
        xml, digests = _candidate_xml(candidate, gate)
        composer, title = _candidate_metadata(candidate, xml, gate)
    except (MusicXmlError, TextScoreError, ReviewRequiredError) as error:
        msg = "normalization_failed"
        raise _CandidateExcludedError(msg) from error
    return xml, digests, composer, title


def normalize_and_profile_candidate(  # noqa: PLR0913
    candidate: SourceCandidate,
    *,
    gate: TestExclusionGate,
    seen_canonical: set[str],
    counts: Counter[str],
    min_measures: int = 8,
    reference_rates: Mapping[str, float] | None = None,
) -> ProfiledSource | None:
    """Screen, normalize and profile a distinct, rights-verified piano piece."""
    if min_measures < _MIN_FIXTURE_MEASURES:
        message = "synthetic intake requires at least four measures"
        raise ValueError(message)
    try:
        checked = _preflight_candidate(candidate, gate)
    except _CandidateExcludedError as error:
        counts[str(error)] += 1
        return None
    _, profile = _profile_candidate(
        candidate,
        checked,
        seen_canonical=seen_canonical,
        min_measures=min_measures,
        reference_rates=reference_rates,
    )
    if isinstance(profile, str):
        counts[profile] += 1
        return None
    gate.audit_admitted_manifest((profile.manifest_row,))
    seen_canonical.add(str(profile.manifest_row["canonical_v2_sha256"]))
    counts["admitted"] += 1
    return profile


def _profile_candidate(
    candidate: SourceCandidate,
    checked: tuple[bytes, dict[str, str], str, str],
    *,
    seen_canonical: set[str] | None = None,
    min_measures: int = 8,
    reference_rates: Mapping[str, float] | None = None,
) -> tuple[str | None, ProfiledSource | str]:
    xml, digests, composer, title = checked
    digest = None
    try:
        score = _screen_score(xml, min_measures)
        canonical = write_document(score)
        digest = _sha(canonical.encode("utf-8"))
        if seen_canonical is not None and digest in seen_canonical:
            return digest, "duplicate_canonical"
        musicxml = to_musicxml(score)
    except _CandidateExcludedError as error:
        return digest, str(error)
    except MusicXmlError, TextScoreError, ReviewRequiredError:
        return digest, "normalization_failed"
    try:
        converter.parse(
            musicxml.decode("utf-8"), format="musicxml", forceSource=True
        )
    except Exception:  # noqa: BLE001 - music21 exposes varied exception types
        return digest, "music21_parse_failed"

    densities, gaps, weight = _coverage(score, reference_rates)
    row: dict[str, object] = {
        "schema": "omr-synth-manifest-v1",
        "source_id": candidate.source_id,
        "corpus": candidate.corpus,
        "rights_variant": candidate.rights_variant,
        "license_url": candidate.license_url,
        "gold_id": candidate.gold_id,
        "composer": composer,
        "title": title,
        **digests,
        "canonical_v2_sha256": digest,
        "measure_count": len(score.blocks),
        "category_densities": densities,
        "coverage_shortfalls": gaps,
        "sampling_weight": weight,
    }
    return digest, ProfiledSource(row, score, canonical, musicxml)


def _screen_score(xml: bytes, min_measures: int) -> AssembledScore:
    outcome = screen(xml)
    if outcome.reason is not None:
        msg = f"screen_{outcome.reason}"
        raise _CandidateExcludedError(msg)
    score = parse_document(from_musicxml_text(xml))
    if (
        not isinstance(score, AssembledScore)
        or len(score.blocks) < min_measures
    ):
        msg = "too_few_measures"
        raise _CandidateExcludedError(msg)
    if not any(
        isinstance(p.event, Note) for p in build_timeline(score).events()
    ):
        msg = "all_rest"
        raise _CandidateExcludedError(msg)
    return score


def intake_sources(  # noqa: PLR0913
    candidates: Iterable[SourceCandidate],
    *,
    run_dir: Path,
    rights_variant: RightsVariant,
    gate: TestExclusionGate,
    max_admitted: int | None = None,
    max_per_corpus: Mapping[str, int] | None = None,
    admitted_tracker: Counter[str] | None = None,
    min_measures: int = 8,
    reference_rates: Mapping[str, float] | None = None,
) -> IntakeResult:
    """Write canonical sources and an audited rights-specific manifest."""
    if rights_variant not in _RIGHTS_VARIANTS or (
        max_admitted is not None and max_admitted < 0
    ):
        message = "invalid synthetic intake partition or admission limit"
        raise ValueError(message)
    root = run_dir / "synthetic" / rights_variant
    counts: Counter[str] = Counter(
        admitted=0, excluded_missing_metadata=0, excluded_test_match=0
    )
    admitted_by_corpus = (
        admitted_tracker if admitted_tracker is not None else Counter()
    )
    rows: list[dict[str, object]] = []
    seen: set[str] = set()
    measure_count = 0
    coverage_totals: dict[str, float] = {}
    if max_admitted != 0:
        for candidate in candidates:
            if (
                max_per_corpus is not None
                and candidate.corpus in max_per_corpus
                and admitted_by_corpus[candidate.corpus]
                >= max_per_corpus[candidate.corpus]
            ):
                continue
            if candidate.rights_variant != rights_variant:
                message = (
                    "source rights variant does not match its output partition"
                )
                raise StopConditionError(message)
            admitted = normalize_and_profile_candidate(
                candidate,
                gate=gate,
                seen_canonical=seen,
                counts=counts,
                min_measures=min_measures,
                reference_rates=reference_rates,
            )
            if admitted is not None:
                admitted_by_corpus[candidate.corpus] += 1
                measures = len(admitted.score.blocks)
                measure_count += measures
                rates = cast(
                    "dict[str, float]",
                    admitted.manifest_row["category_densities"],
                )
                for category, rate in rates.items():
                    coverage_totals[category] = (
                        coverage_totals.get(category, 0.0) + rate * measures
                    )
                rows.append(_write_source(run_dir, root, admitted))
            if max_admitted is not None and counts["admitted"] >= max_admitted:
                break
    gate.audit_admitted_manifest(rows)
    manifest, summary = (
        root / "sources-manifest.jsonl",
        root / "intake-summary.json",
    )
    for path in (manifest, summary):
        check_no_symlink_under(path, run_dir)
    atomic_write_text(
        manifest,
        "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
    )
    rates = {
        category: count / measure_count
        for category, count in coverage_totals.items()
    }
    atomic_write_text(
        summary,
        json.dumps(
            {
                "rights_variant": rights_variant,
                "counts": dict(counts),
                "measure_count": measure_count,
                "category_densities": rates,
                "coverage_shortfalls": _shortfalls(rates, reference_rates),
            },
            indent=2,
        )
        + "\n",
    )
    return IntakeResult(dict(counts), manifest, summary)


def _write_source(
    run_dir: Path, root: Path, source: ProfiledSource
) -> dict[str, object]:
    digest = str(source.manifest_row["canonical_v2_sha256"])
    v2_path, xml_path = (
        root / "sources" / f"{digest}.{suffix}" for suffix in ("v2", "musicxml")
    )
    for path in (v2_path, xml_path):
        check_no_symlink_under(path, run_dir)
    atomic_write_text(v2_path, source.canonical_v2)
    atomic_write_text(xml_path, source.musicxml.decode("utf-8"))
    return source.manifest_row | {
        "canonical_v2_path": v2_path.relative_to(root).as_posix(),
        "canonical_xml_path": xml_path.relative_to(root).as_posix(),
    }


_PDMX_FILTERS = {
    "subset:no_license_conflict": "True",
    "subset:all_valid": "True",
    "n_tracks": "1",
}


def _pdmx_key(name: str) -> str | None:
    key = PurePosixPath(name)
    if (
        key.is_absolute()
        or ".." in key.parts
        or "\\" in name
        or key.suffix != ".mxl"
    ):
        return None
    return key.as_posix()


def _pdmx_metadata(
    csv_path: Path,
) -> dict[str, tuple[str, str | None, str | None]]:
    previous_limit = csv.field_size_limit(_CSV_FIELD_LIMIT)
    try:
        return _read_pdmx_metadata(csv_path)
    finally:
        csv.field_size_limit(previous_limit)


def _read_pdmx_metadata(
    csv_path: Path,
) -> dict[str, tuple[str, str | None, str | None]]:
    metadata: dict[str, tuple[str, str | None, str | None]] = {}
    with csv_path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = reader.fieldnames or []
        if len(set(fields)) != len(fields) or not {
            *_PDMX_FILTERS,
            "mxl",
            "license_url",
        } <= set(fields):
            message = "PDMX metadata lacks unique required intake columns"
            raise StopConditionError(message)
        for row in reader:
            license_url = row.get("license_url", "")
            if (
                any(
                    row.get(field) != value
                    for field, value in _PDMX_FILTERS.items()
                )
                or row.get("subset:deduplicated", "True") != "True"
                or license_url not in PDMX_LICENSES
                or (key := _pdmx_key(row.get("mxl") or "")) is None
            ):
                continue
            if key in metadata:
                message = "staged PDMX metadata repeats a candidate archive key"
                raise StopConditionError(message)
            metadata[key] = (
                license_url,
                row.get("composer")
                or row.get("composer_name")
                or row.get("artist_name"),
                row.get("title") or row.get("song_name"),
            )
    return metadata


def iter_pdmx_candidates(downloads_dir: Path) -> Iterator[SourceCandidate]:
    """Stream eligible regular PDMX members without unpacking the archive."""
    metadata = _pdmx_metadata(downloads_dir / "PDMX.csv")
    with tarfile.open(downloads_dir / "mxl.tar.gz", mode="r|gz") as archive:
        for member in archive:
            key = _pdmx_key(member.name)
            if (
                key is None
                or key not in metadata
                or not member.isfile()
                or not 0 < member.size <= MAX_DOCUMENT_BYTES
            ):
                continue
            license_url, composer, title = metadata.pop(key)
            stream = archive.extractfile(member)
            if stream is not None:
                with stream:
                    raw = stream.read()
                yield SourceCandidate(
                    key,
                    "PDMX",
                    "permissive",
                    license_url,
                    raw,
                    composer=composer,
                    title=title,
                    decode=mxl_root,
                )


def iter_lieder_candidates(
    downloads_dir: Path, export_musicxml: Callable[[bytes], bytes]
) -> Iterator[SourceCandidate]:
    """Yield staged Lieder scores for lazy local export and piano extraction."""
    path = downloads_dir / "Lieder-v3.0.0.zip"
    staged = lieder_candidates(path)
    with zipfile.ZipFile(path) as archive:
        for entry in staged.candidates:
            member = archive.getinfo(f"{staged.top}/{entry.key}")
            if not 0 < member.file_size <= MAX_DOCUMENT_BYTES:
                continue
            yield SourceCandidate(
                entry.key,
                "OpenScore Lieder",
                "permissive",
                "CC0-1.0",
                archive.read(member),
                decode=export_musicxml,
                piano_extract=True,
            )
