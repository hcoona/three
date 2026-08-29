# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "cssselect2==0.8.0",
#   "defusedxml==0.7.1",
#   "fonttools==4.60.1",
#   "html5lib==1.1",
#   "jsonschema==4.25.1",
#   "playwright==1.56.0",
#   "pymupdf==1.26.6",
#   "tinycss2==1.4.0",
# ]
# ///

from __future__ import annotations

import copy
import ctypes
import importlib
import json
import math
import re
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any
from unittest import mock

from jsonschema import Draft202012Validator

sys.dont_write_bytecode = True

PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
SKILL = PUBLICATION_ROOT / "skills" / "scholarly-render-qa"
SUPPORT_ROOT = PUBLICATION_ROOT / "tests"
sys.path.insert(0, str(SUPPORT_ROOT))

publication_test_support: Any = importlib.import_module(
    "publication_test_support"
)
MainResult = publication_test_support.MainResult
PdfPage = publication_test_support.PdfPage
apply_profile_mutation = publication_test_support.apply_profile_mutation
add_pdf_javascript = publication_test_support.add_pdf_javascript
add_pdf_type3_font = publication_test_support.add_pdf_type3_font
add_pdf_vector_mark = publication_test_support.add_pdf_vector_mark
asset_record = publication_test_support.asset_record
build_rendered_publication = publication_test_support.build_rendered_publication
clear_pdf_page_contents = publication_test_support.clear_pdf_page_contents
copy_tree = publication_test_support.copy_tree
detect_browser = publication_test_support.detect_browser
fixed_profile_ceiling = publication_test_support.fixed_profile_ceiling
import_by_path = publication_test_support.import_by_path
invoke_main = publication_test_support.invoke_main
read_json = publication_test_support.read_json
remove_pdf_font_programs = publication_test_support.remove_pdf_font_programs
resolve_stable_asset = publication_test_support.resolve_stable_asset
scale_pdf_user_unit = publication_test_support.scale_pdf_user_unit
sha256_bytes = publication_test_support.sha256_bytes
tree_snapshot = publication_test_support.tree_snapshot
write_json = publication_test_support.write_json
write_pdf = publication_test_support.write_pdf

reconstruct_pdf = import_by_path(
    "scholarly_reconstruct_pdf_for_qa_tests",
    PUBLICATION_ROOT
    / "skills"
    / "scholarly-pdf-reconstruction"
    / "scripts"
    / "reconstruct_pdf.py",
)
assemble_print = import_by_path(
    "scholarly_assemble_print_for_qa_tests",
    PUBLICATION_ROOT
    / "skills"
    / "scholarly-print-assembly"
    / "scripts"
    / "assemble_print.py",
)
validate_package = import_by_path(
    "scholarly_validate_package_for_qa_tests",
    PUBLICATION_ROOT / "scripts" / "validate_package.py",
)
audit_publication = import_by_path(
    "scholarly_audit_publication_under_test",
    SKILL / "scripts" / "audit_publication.py",
)
fitz: Any = importlib.import_module("fitz")

BROWSER = detect_browser()
CORE_CHECK_IDS = {
    "manifest.integrity",
    "html.offline-profile",
    "render.geometry-overflow",
    "pdf.fonts",
    "pdf.actions-type3-text",
    "figures.crop-bindings",
    "render.repeatability",
    "rasters.complete",
    "publication.tree-unchanged",
}
CORE_CHECK_ORDER = (
    "manifest.integrity",
    "html.offline-profile",
    "render.geometry-overflow",
    "pdf.fonts",
    "pdf.actions-type3-text",
    "figures.crop-bindings",
    "render.repeatability",
    "rasters.complete",
    "publication.tree-unchanged",
)
OVERFLOW_FRAGMENT = (
    '<h1 id="opening">Integrated publication</h1>\n'
    '<p><span class="keep-together">' + ("A" * 240) + "</span></p>\n"
    "<!-- figure: integration-figure -->\n"
)
PIPELINE_FRAGMENT_SENTINEL = "qa-fragment-body-sentinel-8f31d71a"
PIPELINE_FRAGMENT = (
    '<h1 id="opening">Integrated publication</h1>\n'
    f'<p><a href="#opening">{PIPELINE_FRAGMENT_SENTINEL}</a></p>\n'
    "<!-- figure: integration-figure -->\n"
)
NONCANONICAL_STABLE_PATHS = (
    "../outside.bin",
    "/absolute.bin",
    "C:/absolute.bin",
    "nested\\asset.bin",
    "./x",
    "a/./b",
    "a//b",
    "a/",
)


def add_pdf_external_actions(
    path: Path,
    uri_target: str,
    file_target: str,
) -> None:
    """Add long URI and file actions without rendering visible content."""
    with fitz.open(path) as document:
        page = document.load_page(0)
        page.insert_link(
            {
                "kind": fitz.LINK_URI,
                "from": fitz.Rect(24, 24, 72, 36),
                "uri": uri_target,
            }
        )
        page.insert_link(
            {
                "kind": fitz.LINK_LAUNCH,
                "from": fitz.Rect(24, 40, 72, 52),
                "file": file_target,
            }
        )
        document.saveIncr()


def add_pdf_page_link(path: Path, link: dict[str, Any]) -> None:
    """Add one page annotation link without visible content."""
    with fitz.open(path) as document:
        page = document.load_page(0)
        page.insert_link(
            {
                "from": fitz.Rect(24, 24, 72, 36),
                **link,
            }
        )
        document.saveIncr()


def add_pdf_named_destination(path: Path) -> None:
    """Add a direct named destination that resolves to a document page."""
    with fitz.open(path) as document:
        document.new_page(width=360, height=480)
        source_xref = document.page_xref(0)
        target_xref = document.page_xref(1)
        document.xref_set_key(
            document.pdf_catalog(),
            "Dests",
            f"<< /terms [{target_xref} 0 R /XYZ 36 36 0] >>",
        )
        annotation_xref = document.get_new_xref()
        document.update_object(
            annotation_xref,
            (
                "<< /Type /Annot /Subtype /Link "
                "/Rect [24 24 72 36] /Dest /terms >>"
            ),
        )
        document.xref_set_key(
            source_xref,
            "Annots",
            f"[{annotation_xref} 0 R]",
        )
        document.saveIncr()


def add_pdf_unknown_action(path: Path, encoded_subtype: str) -> None:
    """Add duplicate catalog references to one unknown PDF action subtype."""
    with fitz.open(path) as document:
        catalog = document.pdf_catalog()
        action_xref = document.get_new_xref()
        document.update_object(
            action_xref,
            f"<< /Type /Action /S /{encoded_subtype} >>",
        )
        document.xref_set_key(
            catalog,
            "OpenAction",
            f"{action_xref} 0 R",
        )
        document.xref_set_key(
            catalog,
            "AA",
            f"<< /WC {action_xref} 0 R >>",
        )
        document.saveIncr()


def pdf_action_observation(path: Path) -> dict[str, Any]:
    """Inspect only the bounded PDF action evidence."""
    with fitz.open(path) as document:
        return audit_publication.pdf_actions(document, fitz)


def windows_short_path(path: Path) -> Path | None:
    """Return the Win32 short path when the volume exposes one."""
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    get_short_path = kernel32.GetShortPathNameW
    get_short_path.argtypes = [
        ctypes.c_wchar_p,
        ctypes.c_wchar_p,
        ctypes.c_uint32,
    ]
    get_short_path.restype = ctypes.c_uint32
    buffer = ctypes.create_unicode_buffer(32768)
    length = get_short_path(str(path), buffer, len(buffer))
    if length == 0 or length >= len(buffer):
        return None
    return Path(buffer.value)


def windows_extended_alias(path: Path) -> Path:
    """Return the extended drive or UNC spelling of an absolute path."""
    value = str(path.absolute())
    if value.startswith("\\\\?\\"):
        return Path(value)
    if value.startswith("\\\\"):
        return Path("\\\\?\\UNC\\" + value[2:])
    return Path("\\\\?\\" + value)


class AuditPublicationContractTests(unittest.TestCase):
    @staticmethod
    def schema_errors(
        value: dict[str, Any],
        schema_name: str = "qa-evidence.schema.json",
    ) -> list[str]:
        schema = read_json(SKILL / "assets" / schema_name)
        return [
            error.message
            for error in Draft202012Validator(schema).iter_errors(value)
        ]

    @staticmethod
    def minimal_evidence() -> dict[str, Any]:
        digest = "0" * 64
        asset = {"path": "artifact.bin", "sha256": digest, "bytes": 1}
        publication_asset = {
            **asset,
            "path_base": "publication-root",
        }
        evidence_asset = {
            **asset,
            "path_base": "evidence-root",
        }
        tree = {
            "files": 1,
            "path_inventory_sha256": digest,
            "fingerprint_sha256": digest,
            "symlinks": [],
            "special_nodes": [],
        }
        return {
            "schema_version": "1.0",
            "publication_id": "publication-1",
            "auditor": {
                "name": "audit_publication.py",
                "version": "0.1.0",
                "publication_profile": {
                    "id": "scholarly-fragment-and-stylesheet-v1",
                    "schema_version": "1.0",
                    "sha256": digest,
                },
            },
            "inputs": {"assembly_manifest": publication_asset},
            "checks": [
                {
                    "id": identifier,
                    "severity": "blocking",
                    "passed": True,
                    "message": "passed",
                    "evidence": {},
                }
                for identifier in CORE_CHECK_ORDER
            ],
            "render_pdfs": {
                "render_1": {
                    **evidence_asset,
                    "path": "independent-renders/render-1.pdf",
                },
                "render_2": {
                    **evidence_asset,
                    "path": "independent-renders/render-2.pdf",
                },
            },
            "rasters": [
                {
                    "source": source,
                    "page": 1,
                    "asset": {
                        **evidence_asset,
                        "path": f"pages/{source}/page-0001.png",
                    },
                }
                for source in ("canonical", "render-1", "render-2")
            ],
            "publication_tree": {
                "algorithm": "sha256-tree-json-v1",
                "scope": "manifest-declared-regular-files-no-symlinks",
                "before": dict(tree),
                "after": dict(tree),
                "unchanged": True,
            },
            "human_review": {
                "status": "required",
                "required_scope": ["Inspect every raster."],
            },
            "mechanical_status": "pass",
        }

    def test_check_evidence_diagnostic_ceiling_is_recursive(self) -> None:
        long_value = "diagnostic-" + ("x" * 1_000)
        values = [f"finding-{index}" for index in range(30)]

        bounded = audit_publication.bounded_diagnostic(
            {"long": long_value, "many": values}
        )

        self.assertLessEqual(
            len(bounded["long"]),
            audit_publication.MAX_DIAGNOSTIC_STRING,
        )
        self.assertIn(sha256_bytes(long_value.encode()), bounded["long"])
        self.assertEqual(30, bounded["many"]["total"])
        self.assertEqual(5, bounded["many"]["omitted"])
        self.assertEqual(25, len(bounded["many"]["samples"]))
        self.assertEqual(
            sha256_bytes(audit_publication.canonical_json(values)),
            bounded["many"]["sha256"],
        )

    def test_candidate_layout_rejects_physical_ancestor_aliases(self) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-ancestor-identity-"
        ) as temporary:
            root = Path(temporary) / "stage"
            evidence_parent = root / "evidence-owner"
            release = root / "release-alias"
            rasters = evidence_parent / "pages"
            renders = evidence_parent / "independent-renders"
            rasters.mkdir(parents=True)
            renders.mkdir()
            release.mkdir()
            layout = audit_publication.ReviewLayout(
                root,
                evidence_parent / "qa-evidence.json",
                release,
                rasters,
                renders,
            )
            original_samefile = Path.samefile
            aliased_pair = frozenset((str(release), str(evidence_parent)))

            def samefile(left: Path, right: Path) -> bool:
                if frozenset((str(left), str(right))) == aliased_pair:
                    return True
                return original_samefile(left, right)

            with (
                mock.patch.object(Path, "samefile", new=samefile),
                self.assertRaisesRegex(  # noqa: PT027
                    audit_publication.AuditError,
                    "overlap after resolving filesystem aliases",
                ),
            ):
                audit_publication.validate_candidate_layout(layout)

    @unittest.skipUnless(
        sys.platform == "win32",
        "Extended path aliases are Windows-specific.",
    )
    def test_extended_path_aliases_reject_publication_output_overlap(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-extended-identity-"
        ) as temporary:
            root = Path(temporary)
            publication = root / "publication"
            review = publication / "prior-review"
            review.mkdir(parents=True)
            (publication / "assembly-manifest.json").write_text(
                "{}\n",
                encoding="utf-8",
            )
            (publication / "index.html").write_text(
                "prior publication\n",
                encoding="utf-8",
            )
            (review / "prior-review.bin").write_bytes(b"prior review\n")
            extended_publication = windows_extended_alias(publication)
            extended_review = windows_extended_alias(review)
            try:
                aliases_are_supported = publication.samefile(
                    extended_publication
                ) and review.samefile(extended_review)
            except OSError:
                aliases_are_supported = False
            if not aliases_are_supported:
                self.skipTest("Extended path aliases are unavailable.")

            publication_before = tree_snapshot(publication)
            review_before = tree_snapshot(review)
            missing_review = publication / "missing-review"
            scenarios = (
                (
                    "normal-publication-extended-review",
                    publication,
                    extended_review,
                    review,
                ),
                (
                    "extended-publication-normal-review",
                    extended_publication,
                    review,
                    review,
                ),
                (
                    "normal-publication-extended-missing-review",
                    publication,
                    windows_extended_alias(missing_review),
                    missing_review,
                ),
            )
            for (
                name,
                publication_path,
                review_path,
                physical_review,
            ) in scenarios:
                with self.subTest(case=name):
                    result = invoke(
                        [
                            "--html",
                            str(publication_path / "index.html"),
                            "--assembly-manifest",
                            str(publication_path / "assembly-manifest.json"),
                            "--evidence",
                            str(review_path / "qa-evidence.json"),
                            "--release-manifest",
                            str(review_path / "release-manifest.json"),
                            "--rasters",
                            str(review_path / "pages"),
                            "--page-size",
                            "letter",
                            "--render-twice",
                        ]
                    )

                    self.assertEqual(2, result.exit_code, result)
                    self.assertEqual({}, result.report)
                    self.assertIn(
                        "disjoint from the publication tree",
                        result.stderr,
                    )
                    self.assertEqual(
                        publication_before,
                        tree_snapshot(publication),
                    )
                    self.assertEqual(review_before, tree_snapshot(review))
                    if physical_review == missing_review:
                        self.assertFalse(missing_review.exists())

    def test_publish_review_revalidates_publication_identity_before_rename(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-publish-identity-"
        ) as temporary:
            root = Path(temporary)
            publication = root / "publication"
            review = root / "review"
            stage = root / "stage"
            publication.mkdir()
            review.mkdir()
            stage.mkdir()
            (publication / "publication.bin").write_bytes(b"publication\n")
            (review / "qa-evidence.json").write_bytes(b"prior evidence\n")
            (review / "prior-review.bin").write_bytes(b"prior review\n")
            (stage / "candidate.bin").write_bytes(b"candidate\n")
            layout = audit_publication.ReviewLayout(
                review,
                review / "qa-evidence.json",
                review / "release-manifest.json",
                review / "pages",
                review / "independent-renders",
            )
            publication_before = tree_snapshot(publication)
            review_before = tree_snapshot(review)
            stage_before = tree_snapshot(stage)
            original_samefile = Path.samefile
            aliased_pair = frozenset(
                (str(publication.resolve()), str(review.resolve()))
            )

            def samefile(left: Path, right: Path) -> bool:
                if frozenset((str(left), str(right))) == aliased_pair:
                    return True
                return original_samefile(left, right)

            with (
                mock.patch.object(Path, "samefile", new=samefile),
                self.assertRaisesRegex(  # noqa: PT027
                    audit_publication.AuditError,
                    "review root must be disjoint from the publication tree",
                ),
            ):
                audit_publication.publish_review(
                    stage,
                    layout,
                    publication,
                )

            self.assertEqual(
                publication_before,
                tree_snapshot(publication),
            )
            self.assertEqual(review_before, tree_snapshot(review))
            self.assertEqual(stage_before, tree_snapshot(stage))

    def test_required_core_checks_are_blocking_and_gate_pass(self) -> None:
        evidence = self.minimal_evidence()
        self.assertEqual([], self.schema_errors(evidence))

        for index, identifier in enumerate(CORE_CHECK_ORDER):
            with self.subTest(advisory=identifier):
                advisory = copy.deepcopy(evidence)
                advisory["checks"][index]["severity"] = "advisory"
                self.assertTrue(self.schema_errors(advisory))

        failed_pass = copy.deepcopy(evidence)
        failed_pass["checks"][0]["passed"] = False
        self.assertTrue(self.schema_errors(failed_pass))

        failed = copy.deepcopy(failed_pass)
        failed["mechanical_status"] = "fail"
        self.assertEqual([], self.schema_errors(failed))

    def test_stable_asset_schemas_require_named_relative_bases(self) -> None:
        evidence = self.minimal_evidence()
        for name, mutate in (
            (
                "missing-base",
                lambda value: value["inputs"]["assembly_manifest"].pop(
                    "path_base"
                ),
            ),
            (
                "wrong-assembly-base",
                lambda value: value["inputs"]["assembly_manifest"].update(
                    {"path_base": "evidence-root"}
                ),
            ),
            (
                "wrong-render-base",
                lambda value: value["render_pdfs"]["render_1"].update(
                    {"path_base": "publication-root"}
                ),
            ),
        ):
            with self.subTest(case=name):
                invalid = copy.deepcopy(evidence)
                mutate(invalid)
                self.assertTrue(self.schema_errors(invalid))

        digest = "0" * 64
        release = {
            "schema_version": "1.0",
            "publication_id": "publication-1",
            "assembly_manifest": {
                "path_base": "publication-root",
                "path": "assembly-manifest.json",
                "sha256": digest,
                "bytes": 1,
            },
            "qa_evidence": {
                "path_base": "release-root",
                "path": "qa-evidence.json",
                "sha256": digest,
                "bytes": 1,
            },
            "mechanical_status": "pass",
            "generator": {
                "name": "audit_publication.py",
                "version": "0.1.0",
            },
        }
        self.assertEqual(
            [],
            self.schema_errors(release, "release-manifest.schema.json"),
        )
        invalid_release = copy.deepcopy(release)
        invalid_release["qa_evidence"]["path_base"] = "evidence-root"
        self.assertTrue(
            self.schema_errors(
                invalid_release,
                "release-manifest.schema.json",
            )
        )
        for path in NONCANONICAL_STABLE_PATHS:
            with self.subTest(path=path):
                invalid_evidence = copy.deepcopy(evidence)
                invalid_evidence["rasters"][0]["asset"]["path"] = path
                self.assertTrue(self.schema_errors(invalid_evidence))

                invalid_release = copy.deepcopy(release)
                invalid_release["qa_evidence"]["path"] = path
                self.assertTrue(
                    self.schema_errors(
                        invalid_release,
                        "release-manifest.schema.json",
                    )
                )

    def test_stable_asset_resolver_maps_named_sibling_roots(self) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-stable-paths-"
        ) as temporary:
            root = Path(temporary)
            publication = root / "publication"
            review = root / "review"
            publication.mkdir()
            (review / "independent-renders").mkdir(parents=True)
            (review / "pages").mkdir()
            targets = {
                "publication-root": publication / "assembly-manifest.json",
                "evidence-root": review / "independent-renders" / "render.pdf",
                "release-root": review / "qa-evidence.json",
            }
            for path_base, target in targets.items():
                target.write_bytes(f"{path_base}\n".encode())
            roots = {
                "publication-root": publication,
                "evidence-root": review,
                "release-root": review,
            }
            records = [
                {
                    "path_base": path_base,
                    **asset_record(roots[path_base], target),
                }
                for path_base, target in targets.items()
            ]
            self.assertEqual(
                list(targets.values()),
                [resolve_stable_asset(record, roots) for record in records],
            )
            for path in NONCANONICAL_STABLE_PATHS:
                with (
                    self.subTest(path=path),
                    self.assertRaises(ValueError),  # noqa: PT027
                ):
                    resolve_stable_asset(
                        {
                            **records[1],
                            "path": path,
                        },
                        roots,
                    )

    def test_shared_publication_policy_conformance_corpus(self) -> None:
        profile = read_json(SKILL / "assets" / "publication-profile.json")
        corpus = read_json(SUPPORT_ROOT / "publication-policy-conformance.json")
        families = set(corpus["declared_font_families"])
        for case in corpus["fragment_cases"]:
            with self.subTest(fragment=case["id"]):
                findings, _identifiers = audit_publication.validate_fragment(
                    case["content"],
                    profile,
                    case["id"],
                )
                self.assertEqual(case["accepted"], not findings, findings)
        for case in corpus["stylesheet_cases"]:
            with self.subTest(stylesheet=case["id"]):
                findings, _records = audit_publication.validate_stylesheet(
                    case["content"],
                    profile,
                    families,
                    case["id"],
                )
                self.assertEqual(case["accepted"], not findings, findings)

    def test_fixed_publication_profile_ceilings_are_aligned(self) -> None:
        expected = read_json(SUPPORT_ROOT / "publication-profile-cases.json")[
            "fixed_ceiling"
        ]
        for name, module in (
            ("package-validator", validate_package),
            ("assembly", assemble_print),
            ("qa", audit_publication),
        ):
            with self.subTest(runtime=name):
                self.assertEqual(expected, fixed_profile_ceiling(module))

    def test_qa_loader_profile_shape_mutations(self) -> None:
        profile = read_json(SKILL / "assets" / "publication-profile.json")
        cases = read_json(SUPPORT_ROOT / "publication-profile-cases.json")[
            "mutations"
        ]
        for case in cases:
            with (
                self.subTest(case=case["id"]),
                tempfile.TemporaryDirectory(
                    prefix="scholarly-qa-profile-"
                ) as temporary,
            ):
                profile_path = Path(temporary) / "publication-profile.json"
                write_json(
                    profile_path,
                    apply_profile_mutation(profile, case),
                )
                with mock.patch.object(
                    audit_publication,
                    "PROFILE_PATH",
                    profile_path,
                ):
                    audit_publication.load_profile.cache_clear()
                    try:
                        if case["accepted"]:
                            audit_publication.load_profile()
                        else:
                            with self.assertRaises(  # noqa: PT027
                                audit_publication.AuditError
                            ):
                                audit_publication.load_profile()
                    finally:
                        audit_publication.load_profile.cache_clear()

    def test_inspect_pdf_has_no_aggregate_raster_budget(self) -> None:
        actual_page_pixels = math.ceil(
            360 * audit_publication.RASTER_SCALE
        ) * math.ceil(480 * audit_publication.RASTER_SCALE)
        nominal_page_pixels = math.ceil(
            612 * audit_publication.RASTER_SCALE
        ) * math.ceil(792 * audit_publication.RASTER_SCALE)
        page_count = 1_000_000_000 // actual_page_pixels + 1
        self.assertGreater(page_count * actual_page_pixels, 1_000_000_000)
        self.assertGreater(page_count * nominal_page_pixels, 1_000_000_000)
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-raster-count-"
        ) as temporary:
            root = Path(temporary)
            pdf = root / "many-pages.pdf"
            write_pdf(
                pdf,
                [PdfPage(vector_marks=False)] * page_count,
            )
            pixmap = mock.Mock()
            pixmap.tobytes.return_value = b"synthetic-png"
            with mock.patch.object(
                fitz.Page,
                "get_pixmap",
                return_value=pixmap,
            ) as get_pixmap:
                report = audit_publication.inspect_pdf(
                    pdf,
                    "many-pages.pdf",
                    "letter",
                    root / "review" / "pages",
                    "canonical",
                    root / "review",
                    {"fonts": [], "font_roles": {}},
                )
            self.assertEqual(page_count, report.evidence["page_count"])
            self.assertEqual(page_count, len(report.rasters))
            self.assertEqual(page_count, get_pixmap.call_count)

    def test_inspect_pdf_rejects_unsafe_per_page_geometry(self) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-raster-geometry-"
        ) as temporary:
            root = Path(temporary)
            pdf = root / "unsafe-page.pdf"
            write_pdf(pdf, [PdfPage(vector_marks=False)])
            scale_pdf_user_unit(pdf, scale=100)
            with (
                mock.patch.object(fitz.Page, "get_pixmap") as get_pixmap,
                self.assertRaises(audit_publication.PublicationError),  # noqa: PT027
            ):
                audit_publication.inspect_pdf(
                    pdf,
                    "unsafe-page.pdf",
                    "letter",
                    root / "review" / "pages",
                    "canonical",
                    root / "review",
                    {"fonts": [], "font_roles": {}},
                )
            get_pixmap.assert_not_called()

    def test_one_uri_annotation_uses_detection_not_duplicate_counts(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-uri-action-"
        ) as temporary:
            pdf = Path(temporary) / "uri.pdf"
            target = "https://example.invalid/unsafe"
            write_pdf(pdf, [PdfPage(vector_marks=False)])
            add_pdf_page_link(
                pdf,
                {"kind": fitz.LINK_URI, "uri": target},
            )

            actions = pdf_action_observation(pdf)

        self.assertTrue(actions["unsafe_detected"])
        self.assertEqual(["uri"], actions["unsafe_kinds"])
        self.assertFalse(actions["unsafe_kinds_truncated"])
        self.assertIn(
            {"kind": "uri", "source_category": "page-link"},
            actions["witnesses"],
        )
        self.assertEqual(
            [
                {
                    "kind": "uri",
                    "page": 1,
                    "target_category": "uri",
                    "scheme_category": "https",
                    "target_characters": len(target),
                    "target_bytes": len(target.encode()),
                    "target_sha256": sha256_bytes(target.encode()),
                }
            ],
            actions["target_samples"],
        )
        self.assertFalse(
            {"total", "unsafe_total", "omitted", "sha256", "samples"}
            & set(actions)
        )

    def test_two_javascript_document_actions_need_no_multiplicity(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-javascript-actions-"
        ) as temporary:
            pdf = Path(temporary) / "javascript.pdf"
            write_pdf(pdf, [PdfPage(vector_marks=False)])
            add_pdf_javascript(pdf, "open-action")
            add_pdf_javascript(pdf, "additional-action")

            actions = pdf_action_observation(pdf)

        self.assertTrue(actions["unsafe_detected"])
        self.assertEqual(["javascript"], actions["unsafe_kinds"])
        self.assertIn(
            {"kind": "javascript", "source_category": "catalog"},
            actions["witnesses"],
        )
        self.assertEqual([], actions["target_samples"])

    def test_mixed_unsafe_action_kinds_are_reported(self) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-mixed-actions-"
        ) as temporary:
            pdf = Path(temporary) / "mixed.pdf"
            write_pdf(pdf, [PdfPage(vector_marks=False)])
            add_pdf_javascript(pdf, "open-action")
            add_pdf_page_link(
                pdf,
                {
                    "kind": fitz.LINK_URI,
                    "uri": "https://example.invalid/mixed",
                },
            )

            actions = pdf_action_observation(pdf)

        self.assertTrue(actions["unsafe_detected"])
        self.assertEqual(["javascript", "uri"], actions["unsafe_kinds"])
        self.assertEqual(
            {"catalog", "annotation", "page-link"},
            {witness["source_category"] for witness in actions["witnesses"]},
        )

    def test_internal_goto_only_remains_safe(self) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-goto-action-"
        ) as temporary:
            pdf = Path(temporary) / "goto.pdf"
            write_pdf(
                pdf,
                [
                    PdfPage(vector_marks=False),
                    PdfPage(vector_marks=False),
                ],
            )
            add_pdf_page_link(
                pdf,
                {
                    "kind": fitz.LINK_GOTO,
                    "page": 1,
                    "to": fitz.Point(36, 36),
                },
            )

            actions = pdf_action_observation(pdf)

        self.assertFalse(actions["unsafe_detected"])
        self.assertEqual([], actions["unsafe_kinds"])
        self.assertEqual([], actions["witnesses"])
        self.assertEqual([], actions["target_samples"])

    def test_direct_named_destinations_are_goto_but_named_actions_are_unsafe(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-named-action-"
        ) as temporary:
            direct_destination = Path(temporary) / "direct-destination.pdf"
            write_pdf(
                direct_destination,
                [PdfPage(vector_marks=False)],
            )
            add_pdf_named_destination(direct_destination)
            with fitz.open(direct_destination) as document:
                links = document[0].get_links()
            self.assertEqual(fitz.LINK_NAMED, links[0]["kind"])
            self.assertEqual(1, links[0]["page"])

            actions = pdf_action_observation(direct_destination)
            self.assertFalse(actions["unsafe_detected"])
            self.assertEqual([], actions["unsafe_kinds"])

            named_action = Path(temporary) / "named-action.pdf"
            write_pdf(
                named_action,
                [PdfPage(vector_marks=False)],
            )
            add_pdf_unknown_action(named_action, "Named")

            actions = pdf_action_observation(named_action)
            self.assertTrue(actions["unsafe_detected"])
            self.assertIn("named", actions["unsafe_kinds"])


def invoke(arguments: list[str]) -> MainResult:
    """Invoke the public QA CLI and capture its optional JSON report."""
    return invoke_main(
        audit_publication,
        arguments,
        require_report=False,
    )


class BrowserSelectionTests(unittest.TestCase):
    def test_default_browser_candidate_precedence(self) -> None:
        scenarios: tuple[dict[str, Any], ...] = (
            {
                "name": "environment-override",
                "platform": "posix",
                "configured": True,
                "windows_edge": False,
                "windows_chrome": False,
                "path_name": "chromium",
                "bundled": True,
                "expected": "configured",
            },
            {
                "name": "windows-chrome-without-edge",
                "platform": "nt",
                "configured": False,
                "windows_edge": False,
                "windows_chrome": True,
                "path_name": None,
                "bundled": True,
                "expected": "windows-chrome",
            },
            {
                "name": "non-windows-path-chromium",
                "platform": "posix",
                "configured": False,
                "windows_edge": False,
                "windows_chrome": False,
                "path_name": "chromium",
                "bundled": False,
                "expected": "path",
            },
            {
                "name": "system-before-playwright",
                "platform": "posix",
                "configured": False,
                "windows_edge": False,
                "windows_chrome": False,
                "path_name": "google-chrome",
                "bundled": True,
                "expected": "path",
            },
        )
        with tempfile.TemporaryDirectory(
            prefix="scholarly-qa-browser-selection-"
        ) as temporary:
            root = Path(temporary)

            def executable(
                paths: dict[str, Path],
                name: str,
                path: Path,
            ) -> Path:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(name.encode("ascii"))
                paths[name] = path
                return path

            for scenario in scenarios:
                with self.subTest(scenario=scenario["name"]):
                    case_root = root / str(scenario["name"])
                    paths: dict[str, Path] = {}

                    environment: dict[str, str] = {}
                    if scenario["configured"]:
                        configured = executable(
                            paths,
                            "configured",
                            case_root / "configured" / "browser",
                        )
                        environment["SCHOLARLY_PUBLICATION_BROWSER"] = str(
                            configured
                        )

                    program_files = case_root / "Program Files"
                    if scenario["platform"] == "nt":
                        environment["PROGRAMFILES"] = str(program_files)
                    if scenario["windows_edge"]:
                        executable(
                            paths,
                            "windows-edge",
                            program_files
                            / "Microsoft"
                            / "Edge"
                            / "Application"
                            / "msedge.exe",
                        )
                    if scenario["windows_chrome"]:
                        executable(
                            paths,
                            "windows-chrome",
                            program_files
                            / "Google"
                            / "Chrome"
                            / "Application"
                            / "chrome.exe",
                        )

                    which_results: dict[str, str] = {}
                    path_name = scenario["path_name"]
                    if path_name is not None:
                        path_browser = executable(
                            paths,
                            "path",
                            case_root / "path" / str(path_name),
                        )
                        which_results[str(path_name)] = str(path_browser)

                    bundled_path = case_root / "playwright" / "chromium"
                    if scenario["bundled"]:
                        executable(paths, "bundled", bundled_path)

                    fake_os = mock.Mock()
                    fake_os.name = scenario["platform"]
                    fake_os.environ = environment
                    chromium = mock.Mock()
                    chromium.executable_path = str(bundled_path)
                    launched = object()
                    chromium.launch.return_value = launched
                    playwright = mock.Mock()
                    playwright.chromium = chromium

                    with (
                        mock.patch.object(audit_publication, "os", fake_os),
                        mock.patch.object(
                            audit_publication.shutil,
                            "which",
                            side_effect=which_results.get,
                        ),
                    ):
                        selected = audit_publication.launch_browser(
                            playwright,
                            None,
                        )

                    self.assertIs(launched, selected)
                    chromium.launch.assert_called_once_with(
                        headless=True,
                        executable_path=str(
                            paths[str(scenario["expected"])].resolve()
                        ),
                    )


class AuditPublicationReplacementTests(unittest.TestCase):
    suite_temporary: tempfile.TemporaryDirectory[str]
    suite_root: Path
    baseline_publication: Path

    @classmethod
    def setUpClass(cls) -> None:
        if BROWSER is None:
            message = "No local Chromium-family browser is available."
            raise unittest.SkipTest(message)
        cls.suite_temporary = tempfile.TemporaryDirectory(
            prefix="scholarly-qa-suite-"
        )
        cls.suite_root = Path(cls.suite_temporary.name)
        cls.baseline_publication = build_rendered_publication(
            cls.suite_root / "pipeline",
            reconstruct_pdf,
            assemble_print,
            browser=BROWSER,
            fragment_html=PIPELINE_FRAGMENT,
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.suite_temporary.cleanup()

    def setUp(self) -> None:
        self.case_root = self.suite_root / self._testMethodName
        self.case_root.mkdir()

    def fresh_publication(self, name: str = "publication") -> Path:
        publication = self.case_root / name
        copy_tree(self.baseline_publication, publication)
        return publication

    @staticmethod
    def arguments(
        publication: Path,
        review: Path,
        *,
        render_twice: bool = True,
        evidence_name: str = "qa-evidence.json",
        release_name: str = "release-manifest.json",
    ) -> list[str]:
        evidence = review / evidence_name
        arguments = [
            "--html",
            str(publication / "index.html"),
            "--assembly-manifest",
            str(publication / "assembly-manifest.json"),
            "--evidence",
            str(evidence),
            "--release-manifest",
            str(review / release_name),
            "--rasters",
            str(evidence.parent / "pages"),
            "--page-size",
            "letter",
            "--browser",
            str(BROWSER),
        ]
        if render_twice:
            arguments.append("--render-twice")
        return arguments

    @staticmethod
    def semantic_asset_records(
        manifest: dict[str, Any],
    ) -> list[dict[str, Any]]:
        """Return documented manifest asset records without runtime helpers."""
        records = list(manifest["inputs"].values())
        records.extend(fragment["asset"] for fragment in manifest["fragments"])
        records.extend(
            part["source_svg"]
            for figure in manifest["figures"]
            for part in figure["parts"]
        )
        records.extend(font["asset"] for font in manifest["fonts"])
        records.extend(manifest["stylesheets"])
        records.extend(
            record
            for record in manifest["outputs"].values()
            if isinstance(record, dict)
        )
        return records

    @classmethod
    def refresh_asset(cls, publication: Path, logical: str) -> None:
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        replacement = asset_record(publication, publication / logical)
        semantic_matches = 0
        for record in cls.semantic_asset_records(manifest):
            if record["path"] == logical:
                record.update(replacement)
                semantic_matches += 1
        tracked_matches = 0
        for record in manifest["tracked_files"]:
            if record["path"] == logical:
                record.update(replacement)
                tracked_matches += 1
        if semantic_matches < 1 or tracked_matches != 1:
            message = (
                f"manifest does not uniquely bind tracked asset {logical!r}"
            )
            raise AssertionError(message)
        write_json(manifest_path, manifest)

    @staticmethod
    def check_by_id(
        evidence: dict[str, Any],
        identifier: str,
    ) -> dict[str, Any]:
        return next(
            check for check in evidence["checks"] if check["id"] == identifier
        )

    @staticmethod
    def nested_records(value: Any) -> list[dict[str, Any]]:
        records: list[dict[str, Any]] = []
        if isinstance(value, dict):
            records.append(value)
            for item in value.values():
                records.extend(
                    AuditPublicationReplacementTests.nested_records(item)
                )
        elif isinstance(value, list):
            for item in value:
                records.extend(
                    AuditPublicationReplacementTests.nested_records(item)
                )
        return records

    def assert_check_evidence_bounded(
        self,
        evidence: dict[str, Any],
    ) -> None:
        def inspect(value: Any) -> None:
            if isinstance(value, str):
                self.assertLessEqual(
                    len(value),
                    audit_publication.MAX_DIAGNOSTIC_STRING,
                )
            elif isinstance(value, list):
                self.assertLessEqual(
                    len(value),
                    audit_publication.MAX_DIAGNOSTIC_ITEMS,
                )
                for item in value:
                    inspect(item)
            elif isinstance(value, dict):
                for item in value.values():
                    inspect(item)

        for check in evidence["checks"]:
            inspect(check["evidence"])

    def assert_schema_valid(
        self,
        value: dict[str, Any],
        schema_name: str,
    ) -> None:
        schema = read_json(SKILL / "assets" / schema_name)
        errors = sorted(
            Draft202012Validator(schema).iter_errors(value),
            key=lambda error: tuple(map(str, error.absolute_path)),
        )
        self.assertEqual(
            [],
            [f"{error.json_path}: {error.message}" for error in errors],
        )

    def assert_blocked(
        self,
        publication: Path,
        review: Path,
        expected_failed: set[str],
    ) -> dict[str, Any]:
        before = tree_snapshot(publication)
        result = invoke(self.arguments(publication, review))
        self.assertEqual(1, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertFalse((review / "release-manifest.json").exists())
        self.assertEqual(before, tree_snapshot(publication))
        evidence_path = review / "qa-evidence.json"
        evidence = read_json(evidence_path)
        self.assertEqual("fail", evidence["mechanical_status"])
        failed = {
            check["id"] for check in evidence["checks"] if not check["passed"]
        }
        self.assertLessEqual(expected_failed, failed)
        return evidence

    @staticmethod
    def refresh_render_asset(rendered: Any, output: Path) -> None:
        content = output.read_bytes()
        rendered.pdf = type(rendered.pdf)(
            path=rendered.pdf.path,
            file=output,
            sha256=sha256_bytes(content),
            bytes=len(content),
        )

    def test_actual_pipeline_emits_nine_checks_and_release_contract(
        self,
    ) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        before = tree_snapshot(publication)

        result = invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        self.assertEqual("pass", result.report["mechanical_status"])
        evidence_path = review / "qa-evidence.json"
        release_path = review / "release-manifest.json"
        self.assertEqual(
            str(evidence_path.resolve()),
            result.report["evidence"],
        )
        self.assertEqual(
            str(release_path.resolve()),
            result.report["release_manifest"],
        )
        self.assertNotIn(".staging-", result.stdout)
        evidence = read_json(evidence_path)
        release = read_json(release_path)
        serialized_evidence = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn("render_1_dom", serialized_evidence)
        self.assertNotIn("render_2_dom", serialized_evidence)
        self.assertNotIn(PIPELINE_FRAGMENT_SENTINEL, serialized_evidence)
        self.assertNotIn("file:", serialized_evidence.casefold())
        self.assertNotIn(
            json.dumps(
                str(publication.resolve()),
                ensure_ascii=False,
            )[1:-1],
            serialized_evidence,
        )
        self.assertNotIn(
            publication.resolve().as_posix(),
            serialized_evidence,
        )
        self.assertEqual(
            CORE_CHECK_IDS,
            {check["id"] for check in evidence["checks"]},
        )
        self.assertEqual(9, len(evidence["checks"]))
        self.assertTrue(
            all(
                check["severity"] == "blocking" and check["passed"]
                for check in evidence["checks"]
            )
        )
        self.assertEqual("pass", evidence["mechanical_status"])
        self.assertEqual("required", evidence["human_review"]["status"])
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_schema_valid(release, "release-manifest.schema.json")
        self.assertEqual(
            {
                "schema_version",
                "publication_id",
                "auditor",
                "inputs",
                "checks",
                "render_pdfs",
                "rasters",
                "publication_tree",
                "human_review",
                "mechanical_status",
            },
            set(evidence),
        )
        self.assertEqual(
            {"name", "version", "publication_profile"},
            set(evidence["auditor"]),
        )
        self.assertEqual(
            {"render_1", "render_2"},
            set(evidence["render_pdfs"]),
        )
        self.assertEqual(
            {
                "schema_version",
                "publication_id",
                "assembly_manifest",
                "qa_evidence",
                "mechanical_status",
                "generator",
            },
            set(release),
        )
        self.assertEqual({"name", "version"}, set(release["generator"]))
        self.assertEqual(
            {
                "path_base": "publication-root",
                **asset_record(
                    publication,
                    publication / "assembly-manifest.json",
                ),
            },
            release["assembly_manifest"],
        )
        self.assertEqual(
            release["assembly_manifest"],
            evidence["inputs"]["assembly_manifest"],
        )
        self.assertEqual(
            {
                "path_base": "release-root",
                **asset_record(review, evidence_path),
            },
            release["qa_evidence"],
        )
        roots = {
            "publication-root": publication,
            "evidence-root": evidence_path.parent,
            "release-root": release_path.parent,
        }
        stable_records = [
            evidence["inputs"]["assembly_manifest"],
            *evidence["render_pdfs"].values(),
            *(item["asset"] for item in evidence["rasters"]),
            release["assembly_manifest"],
            release["qa_evidence"],
        ]
        resolved = [
            resolve_stable_asset(record, roots) for record in stable_records
        ]
        self.assertEqual(
            (publication / "assembly-manifest.json").resolve(),
            resolved[0],
        )
        self.assertEqual(evidence_path.resolve(), resolved[-1])
        self.assertTrue(
            all(
                path.is_relative_to(publication.resolve())
                or path.is_relative_to(review.resolve())
                for path in resolved
            )
        )
        self.assertEqual(
            self.check_by_id(
                evidence,
                "rasters.complete",
            )["evidence"]["expected"],
            len(evidence["rasters"]),
        )
        self.assertEqual(before, tree_snapshot(publication))
        self.assertTrue(evidence["publication_tree"]["unchanged"])

    def test_manifest_load_failure_is_operational_and_integrity_failures_block(
        self,
    ) -> None:
        invalid_publication = self.fresh_publication("manifest-schema")
        invalid_manifest_path = invalid_publication / "assembly-manifest.json"
        invalid_manifest = read_json(invalid_manifest_path)
        invalid_manifest.pop("publication_id")
        write_json(invalid_manifest_path, invalid_manifest)
        invalid_review = self.case_root / "review-manifest-schema"

        invalid = invoke(
            self.arguments(
                invalid_publication,
                invalid_review,
            )
        )

        self.assertEqual(2, invalid.exit_code, invalid)
        self.assertEqual({}, invalid.report)
        self.assertFalse(invalid_review.exists())

        overflow = self.fresh_publication("overflow-bbox")
        overflow_manifest_path = overflow / "assembly-manifest.json"
        overflow_manifest = read_json(overflow_manifest_path)
        overflow_manifest["figures"][0]["parts"][0]["bbox"][0] = 10**400
        write_json(overflow_manifest_path, overflow_manifest)
        overflow_review = self.case_root / "review-overflow-bbox"

        overflow_result = invoke(
            self.arguments(
                overflow,
                overflow_review,
            )
        )

        self.assertEqual(2, overflow_result.exit_code, overflow_result)
        self.assertEqual({}, overflow_result.report)
        self.assertNotIn("Traceback", overflow_result.stderr)
        self.assertFalse(overflow_review.exists())

        noncanonical = self.fresh_publication("noncanonical-bbox")
        noncanonical_manifest_path = noncanonical / "assembly-manifest.json"
        noncanonical_manifest = read_json(noncanonical_manifest_path)
        noncanonical_manifest["figures"][0]["parts"][0]["bbox"] = [
            54.0001,
            220.0001,
            306.0001,
            340.0001,
        ]
        write_json(noncanonical_manifest_path, noncanonical_manifest)
        self.assert_blocked(
            noncanonical,
            self.case_root / "review-noncanonical-bbox",
            {"manifest.integrity"},
        )

        nonpositive = self.fresh_publication("nonpositive-bbox")
        nonpositive_manifest_path = nonpositive / "assembly-manifest.json"
        nonpositive_manifest = read_json(nonpositive_manifest_path)
        nonpositive_manifest["figures"][0]["parts"][0]["bbox"] = [
            54.0,
            220.0,
            306.0,
            220.0,
        ]
        write_json(nonpositive_manifest_path, nonpositive_manifest)
        evidence = self.assert_blocked(
            nonpositive,
            self.case_root / "review-nonpositive-bbox",
            {"manifest.integrity"},
        )
        assert evidence is not None
        self.assertIn(
            "bbox must have positive width and height",
            json.dumps(evidence["checks"], ensure_ascii=False),
        )

        for name in ("tracked-hash", "undeclared-tree"):
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                if name == "tracked-hash":
                    with (publication / "inputs" / "assembly-spec.json").open(
                        "ab"
                    ) as stream:
                        stream.write(b" ")
                    expected = {"manifest.integrity"}
                else:
                    (publication / "undeclared.bin").write_bytes(b"rogue")
                    expected = {"manifest.integrity"}
                self.assert_blocked(
                    publication,
                    self.case_root / f"review-{name}",
                    expected,
                )

    def test_profile_link_and_figure_artifacts_block_release(self) -> None:
        cases = (
            ("fragment-profile", "html.offline-profile"),
            ("stylesheet-link", "html.offline-profile"),
            ("figure-crop", "figures.crop-bindings"),
        )
        for name, expected_check in cases:
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                if name == "fragment-profile":
                    logical = "fragments/section-one.html"
                    path = publication / logical
                    path.write_text(
                        path.read_text(encoding="utf-8").replace(
                            "<p>",
                            '<p onclick="alert(1)">',
                            1,
                        ),
                        encoding="utf-8",
                    )
                else:
                    logical = "index.html"
                    path = publication / logical
                    content = path.read_text(encoding="utf-8")
                    if name == "stylesheet-link":
                        content = content.replace(
                            'href="assets/print.css"',
                            ('href="assets/stylesheets/stylesheet-001.css"'),
                            1,
                        )
                    else:
                        content, replacements = re.subn(
                            r'viewBox="[^"]+"',
                            'viewBox="0 0 1 1"',
                            content,
                            count=1,
                        )
                        self.assertEqual(1, replacements)
                    path.write_text(content, encoding="utf-8")
                self.refresh_asset(publication, logical)
                evidence = self.assert_blocked(
                    publication,
                    self.case_root / f"review-{name}",
                    {expected_check},
                )
                assert evidence is not None
                self.assertTrue(
                    self.check_by_id(
                        evidence,
                        "manifest.integrity",
                    )["passed"]
                )

    def test_text_read_failures_emit_path_neutral_diagnostics(self) -> None:
        publication = self.fresh_publication("text-read-failures")
        manifest = read_json(publication / "assembly-manifest.json")
        fragment = manifest["fragments"][0]
        cases = (
            (
                "generated-html",
                manifest["outputs"]["html"]["path"],
                None,
                "os",
            ),
            (
                "generated-css",
                manifest["outputs"]["css"]["path"],
                None,
                "unicode",
            ),
            (
                "retained-stylesheet",
                manifest["stylesheets"][0]["path"],
                None,
                "os",
            ),
            (
                "fragment",
                fragment["asset"]["path"],
                fragment["id"],
                "unicode",
            ),
        )
        original_read_text = Path.read_text
        for category, logical, fragment_id, failure_category in cases:
            with self.subTest(category=category):
                target = (publication / logical).resolve()
                raw_message = f"raw-{failure_category}-{category}-message"
                absolute_filename = (
                    rf"C:\qa-private\{category}-absolute-secret.txt"
                )

                def injected_read_text(
                    path: Path,
                    *args: Any,
                    expected_target: Path = target,
                    injected_failure: str = failure_category,
                    injected_message: str = raw_message,
                    injected_filename: str = absolute_filename,
                    **kwargs: Any,
                ) -> str:
                    if path.resolve() == expected_target:
                        if injected_failure == "os":
                            raise FileNotFoundError(
                                2,
                                injected_message,
                                injected_filename,
                            )
                        encoding = "utf-8"
                        raise UnicodeDecodeError(
                            encoding,
                            b"\xffprivate",
                            0,
                            1,
                            injected_message,
                        )
                    return original_read_text(path, *args, **kwargs)

                review = self.case_root / f"review-{category}"
                with mock.patch.object(
                    Path,
                    "read_text",
                    new=injected_read_text,
                ):
                    evidence = self.assert_blocked(
                        publication,
                        review,
                        {"html.offline-profile"},
                    )

                assert evidence is not None
                self.assert_schema_valid(
                    evidence,
                    "qa-evidence.schema.json",
                )
                self.assert_check_evidence_bounded(evidence)
                diagnostics = [
                    record
                    for record in self.nested_records(evidence["checks"])
                    if record.get("kind") == "text-read"
                    and record.get("category") == category
                ]
                self.assertTrue(diagnostics)
                for diagnostic in diagnostics:
                    self.assertEqual(logical, diagnostic["path"])
                    self.assertEqual(
                        failure_category,
                        diagnostic["failure_category"],
                    )
                    if fragment_id is None:
                        self.assertNotIn("fragment_id", diagnostic)
                    else:
                        self.assertEqual(
                            fragment_id,
                            diagnostic["fragment_id"],
                        )
                    if failure_category == "os":
                        self.assertEqual(2, diagnostic["errno"])
                    else:
                        self.assertNotIn("errno", diagnostic)
                serialized = json.dumps(evidence, ensure_ascii=False)
                self.assertNotIn(raw_message, serialized)
                self.assertNotIn(absolute_filename, serialized)
                self.assertNotIn(
                    absolute_filename.replace("\\", "/"),
                    serialized,
                )
                self.assertNotIn("file:", serialized.casefold())

    def test_long_resource_urls_emit_path_neutral_bounded_evidence(
        self,
    ) -> None:
        for scheme in ("file", "https"):
            with self.subTest(scheme=scheme):
                publication = self.fresh_publication(f"resource-{scheme}")
                sentinel = f"{scheme}-credential-" + ("x" * 700)
                if scheme == "file":
                    raw_url = (
                        publication.resolve().as_uri()
                        + f"/private/{sentinel}?token=secret#fragment"
                    )
                else:
                    raw_url = (
                        "https://user:password@example.invalid/"
                        f"{sentinel}?token=secret#fragment"
                    )
                index = publication / "index.html"
                encoded_url = raw_url.replace("&", "&amp;").replace(
                    '"',
                    "&quot;",
                )
                content, replacements = re.subn(
                    r'(<image\b[^>]*\bhref=")[^"]*(")',
                    lambda match, replacement=encoded_url: (
                        f"{match.group(1)}{replacement}{match.group(2)}"
                    ),
                    index.read_text(encoding="utf-8"),
                    count=1,
                )
                self.assertEqual(1, replacements)
                index.write_text(content, encoding="utf-8")
                self.refresh_asset(publication, "index.html")

                evidence = self.assert_blocked(
                    publication,
                    self.case_root / f"review-resource-{scheme}",
                    {"html.offline-profile", "figures.crop-bindings"},
                )

                assert evidence is not None
                self.assert_schema_valid(evidence, "qa-evidence.schema.json")
                self.assert_check_evidence_bounded(evidence)
                diagnostics = [
                    record
                    for record in self.nested_records(evidence["checks"])
                    if record.get("kind") == "resource-url"
                    and record.get("sha256") == sha256_bytes(raw_url.encode())
                ]
                self.assertTrue(diagnostics)
                self.assertTrue(
                    all(
                        record["category"] == "nonlocal"
                        and record["scheme_category"] == scheme
                        and record["input_characters"] == len(raw_url)
                        and record["input_bytes"] == len(raw_url.encode())
                        for record in diagnostics
                    )
                )
                serialized = json.dumps(evidence, ensure_ascii=False)
                self.assertNotIn(raw_url, serialized)
                self.assertNotIn(sentinel, serialized)
                self.assertNotIn("example.invalid", serialized)
                self.assertNotIn("user:password", serialized)
                self.assertNotIn("file:", serialized.casefold())
                self.assertNotIn(str(publication.resolve()), serialized)
                self.assertNotIn(
                    publication.resolve().as_posix(),
                    serialized,
                )

    def test_malformed_source_svg_emits_relative_stable_diagnostic(
        self,
    ) -> None:
        publication = self.fresh_publication("malformed-source-svg")
        manifest = read_json(publication / "assembly-manifest.json")
        logical = manifest["figures"][0]["parts"][0]["source_svg"]["path"]
        source_svg = publication / logical
        source_svg.write_bytes(b'<svg xmlns="http://www.w3.org/2000/svg"><g>')
        self.refresh_asset(publication, logical)

        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-malformed-source-svg",
            {"figures.crop-bindings"},
        )

        assert evidence is not None
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_check_evidence_bounded(evidence)
        diagnostics = [
            record
            for record in self.nested_records(evidence["checks"])
            if record.get("kind") == "source-svg"
        ]
        self.assertTrue(
            any(
                record["category"] == "xml-parse-error"
                and record["path"] == logical
                and record["path_sha256"] == sha256_bytes(logical.encode())
                for record in diagnostics
            )
        )
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn(str(source_svg.resolve()), serialized)
        self.assertNotIn(source_svg.resolve().as_posix(), serialized)
        self.assertNotIn("unclosed token", serialized.casefold())
        self.assertNotIn("file:", serialized.casefold())

    def test_long_pdf_action_targets_emit_bounded_metadata_only(self) -> None:
        publication = self.fresh_publication("long-pdf-actions")
        uri_sentinel = "uri-target-secret-" + ("u" * 700)
        file_sentinel = "file-target-secret-" + ("f" * 700)
        uri_target = (
            "https://user:password@example.invalid/"
            f"{uri_sentinel}?token=secret#fragment"
        )
        file_target = str(
            publication.resolve() / "private" / f"{file_sentinel}.pdf"
        )
        pdf = publication / "publication.pdf"
        add_pdf_external_actions(pdf, uri_target, file_target)
        self.refresh_asset(publication, "publication.pdf")

        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-long-pdf-actions",
            {"pdf.actions-type3-text"},
        )

        assert evidence is not None
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_check_evidence_bounded(evidence)
        check = self.check_by_id(evidence, "pdf.actions-type3-text")
        canonical = check["evidence"]["observations"]["canonical"]
        actions = canonical["actions"]
        self.assertTrue(actions["unsafe_detected"])
        self.assertIn("uri", actions["unsafe_kinds"])
        self.assertTrue(
            {"launch", "goto-remote"} & set(actions["unsafe_kinds"])
        )
        self.assertFalse(
            {"total", "unsafe_total", "omitted", "sha256", "samples"}
            & set(actions)
        )
        targeted = [
            action
            for action in actions["target_samples"]
            if "target_sha256" in action
        ]
        self.assertTrue(
            any(
                action["kind"] == "uri"
                and action["page"] == 1
                and action["target_category"] == "uri"
                and action["scheme_category"] == "https"
                and action["target_characters"] == len(uri_target)
                and action["target_bytes"] == len(uri_target.encode())
                and action["target_sha256"] == sha256_bytes(uri_target.encode())
                for action in targeted
            )
        )
        self.assertTrue(
            any(
                action["kind"] in {"launch", "goto-remote"}
                and action["page"] == 1
                and action["target_category"] == "file"
                and action["scheme_category"] in {"file", "path"}
                and action["target_characters"] > 256
                and re.fullmatch(r"[a-f0-9]{64}", action["target_sha256"])
                for action in targeted
            ),
            targeted,
        )
        finding = check["evidence"]["findings"]["canonical"]
        self.assertTrue(finding["actions"]["unsafe_detected"])
        self.assertEqual(
            actions["unsafe_kinds"],
            finding["actions"]["unsafe_kinds"],
        )
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn(uri_target, serialized)
        self.assertNotIn(file_target, serialized)
        self.assertNotIn(uri_sentinel, serialized)
        self.assertNotIn(file_sentinel, serialized)
        self.assertNotIn("example.invalid", serialized)
        self.assertNotIn("user:password", serialized)
        self.assertNotIn("file:", serialized.casefold())
        self.assertNotIn(str(publication.resolve()), serialized)
        self.assertNotIn(publication.resolve().as_posix(), serialized)

    def test_unknown_pdf_action_subtype_is_fixed_and_path_neutral(
        self,
    ) -> None:
        publication = self.fresh_publication("unknown-pdf-action")
        decoded_subtype = "../../private/unknown-action-decoded-sentinel-" + (
            "x" * 64
        )
        encoded_subtype = "".join(
            f"#{byte:02X}" for byte in decoded_subtype.encode()
        )
        self.assertGreater(
            len(encoded_subtype),
            audit_publication.MAX_DIAGNOSTIC_STRING,
        )
        pdf = publication / "publication.pdf"
        add_pdf_unknown_action(pdf, encoded_subtype)
        self.refresh_asset(publication, "publication.pdf")

        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-unknown-pdf-action",
            {"pdf.actions-type3-text"},
        )

        assert evidence is not None
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_check_evidence_bounded(evidence)
        check = self.check_by_id(evidence, "pdf.actions-type3-text")
        actions = check["evidence"]["observations"]["canonical"]["actions"]
        decoded_bytes = decoded_subtype.encode()
        self.assertTrue(actions["unsafe_detected"])
        self.assertEqual(["unknown-action"], actions["unsafe_kinds"])
        self.assertEqual(
            [
                {
                    "kind": "unknown-action",
                    "source_category": "catalog",
                    "subtype_characters": len(decoded_subtype),
                    "subtype_bytes": len(decoded_bytes),
                    "subtype_sha256": sha256_bytes(decoded_bytes),
                }
            ],
            actions["witnesses"],
        )
        self.assertFalse(actions["witnesses_truncated"])
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn(decoded_subtype, serialized)
        self.assertNotIn("unknown-action-decoded-sentinel", serialized)
        self.assertNotIn(encoded_subtype, serialized)
        self.assertNotIn("#75#6E#6B#6E#6F#77#6E", serialized)

    def test_actual_pdf_artifacts_drive_pdf_and_geometry_failures(self) -> None:
        cases = (
            ("font", remove_pdf_font_programs, "pdf.fonts"),
            (
                "action",
                lambda path: add_pdf_javascript(path, "open-action"),
                "pdf.actions-type3-text",
            ),
            ("type3", add_pdf_type3_font, "pdf.actions-type3-text"),
            (
                "text",
                clear_pdf_page_contents,
                "pdf.actions-type3-text",
            ),
            (
                "geometry",
                scale_pdf_user_unit,
                "render.geometry-overflow",
            ),
        )
        for name, mutation, expected_check in cases:
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                pdf_path = publication / "publication.pdf"
                mutation(pdf_path)
                self.refresh_asset(publication, "publication.pdf")

                evidence = self.assert_blocked(
                    publication,
                    self.case_root / f"review-{name}",
                    {expected_check},
                )
                assert evidence is not None
                assert evidence is not None
                self.assertTrue(
                    self.check_by_id(
                        evidence,
                        "manifest.integrity",
                    )["passed"]
                )
                if name == "font":
                    canonical = self.check_by_id(
                        evidence,
                        "pdf.fonts",
                    )["evidence"]["fonts"]["canonical"]
                    self.assertTrue(
                        any(not font["embedded"] for font in canonical)
                    )
                elif name == "action":
                    canonical = self.check_by_id(
                        evidence,
                        "pdf.actions-type3-text",
                    )["evidence"]["observations"]["canonical"]
                    self.assertTrue(canonical["actions"]["unsafe_detected"])
                    self.assertIn(
                        "javascript",
                        canonical["actions"]["unsafe_kinds"],
                    )
                elif name == "type3":
                    canonical = self.check_by_id(
                        evidence,
                        "pdf.actions-type3-text",
                    )["evidence"]["observations"]["canonical"]
                    self.assertTrue(canonical["type3_fonts"])
                elif name == "text":
                    canonical = self.check_by_id(
                        evidence,
                        "pdf.actions-type3-text",
                    )["evidence"]["observations"]["canonical"]
                    self.assertEqual(0, canonical["text_characters"])
                else:
                    canonical = self.check_by_id(
                        evidence,
                        "render.geometry-overflow",
                    )["evidence"]["pdf_pages"]["canonical"]
                    self.assertFalse(canonical[0]["size_matches"])

    def test_actual_browser_overflow_blocks_release(self) -> None:
        publication = build_rendered_publication(
            self.case_root / "overflow-pipeline",
            reconstruct_pdf,
            assemble_print,
            browser=BROWSER,
            fragment_html=OVERFLOW_FRAGMENT,
        )
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review",
            {"render.geometry-overflow"},
        )

        assert evidence is not None
        self.assertTrue(
            self.check_by_id(evidence, "manifest.integrity")["passed"]
        )
        self.assertTrue(
            self.check_by_id(evidence, "html.offline-profile")["passed"]
        )
        self.assertTrue(
            self.check_by_id(
                evidence,
                "render.geometry-overflow",
            )["evidence"]["overflow"]
        )

    def test_fractional_bbox_pipeline_remains_qa_compatible(self) -> None:
        publication = build_rendered_publication(
            self.case_root / "fractional-bbox-pipeline",
            reconstruct_pdf,
            assemble_print,
            browser=BROWSER,
            fragment_html=PIPELINE_FRAGMENT,
            figure_bbox=(
                10.0004,
                20.0004,
                110.0006,
                120.0006,
            ),
        )
        manifest = read_json(publication / "assembly-manifest.json")
        self.assertEqual(
            [10.0, 20.0, 110.001, 120.001],
            manifest["figures"][0]["parts"][0]["bbox"],
        )
        review = self.case_root / "review"

        result = invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        self.assertEqual("pass", evidence["mechanical_status"])
        self.assertTrue(
            self.check_by_id(
                evidence,
                "figures.crop-bindings",
            )["passed"]
        )

    def test_unsafe_manifest_font_names_publish_blocking_evidence(self) -> None:
        cases = (
            ("control", "\u0001"),
            ("format", "\u200b"),
            ("surrogate", "\ud800"),
        )
        for name, character in cases:
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                review = self.case_root / f"review-{name}"
                success = invoke(self.arguments(publication, review))
                self.assertEqual(0, success.exit_code, success)
                previous = tree_snapshot(review)

                manifest_path = publication / "assembly-manifest.json"
                manifest = read_json(manifest_path)
                family = f"Fixture{character}Serif"
                manifest["fonts"][0]["family"] = family
                manifest["font_roles"] = {
                    "body-cjk": family,
                    "body-latin": family,
                }
                manifest_path.write_text(
                    json.dumps(
                        manifest,
                        ensure_ascii=True,
                        indent=2,
                        sort_keys=True,
                    )
                    + "\n",
                    encoding="utf-8",
                )

                evidence = self.assert_blocked(
                    publication,
                    review,
                    {"manifest.integrity"},
                )

                assert evidence is not None
                self.assertEqual(9, len(evidence["checks"]))
                self.assertNotEqual(previous, tree_snapshot(review))
                self.assertFalse((review / "release-manifest.json").exists())

        unsafe_role = "private-token=qa-font-role-sentinel\u200b"
        publication = self.fresh_publication("unsafe-undeclared-role")
        review = self.case_root / "review-unsafe-undeclared-role"
        success = invoke(self.arguments(publication, review))
        self.assertEqual(0, success.exit_code, success)
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["font_roles"]["body-latin"] = unsafe_role
        write_json(manifest_path, manifest)

        evidence = self.assert_blocked(
            publication,
            review,
            {"manifest.integrity"},
        )

        assert evidence is not None
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn("qa-font-role-sentinel", serialized)
        self.assertIn(
            sha256_bytes(unsafe_role.encode("utf-8")),
            serialized,
        )

        unsafe_family = "private-token=qa-family-sentinel\u200b"
        publication = self.fresh_publication("unsafe-family-leak")
        review = self.case_root / "review-unsafe-family-leak"
        success = invoke(self.arguments(publication, review))
        self.assertEqual(0, success.exit_code, success)
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["fonts"][0]["family"] = unsafe_family
        manifest["fonts"][0]["postscript_name"] = None
        manifest["fonts"][0]["full_name"] = None
        manifest["font_roles"] = {
            "body-cjk": unsafe_family,
            "body-latin": unsafe_family,
        }
        write_json(manifest_path, manifest)

        evidence = self.assert_blocked(
            publication,
            review,
            {"manifest.integrity", "pdf.fonts"},
        )

        assert evidence is not None
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn("qa-family-sentinel", serialized)
        self.assertIn(
            sha256_bytes(unsafe_family.encode("utf-8")),
            serialized,
        )

    def test_figure_and_crop_ids_may_share_a_value(self) -> None:
        publication = build_rendered_publication(
            self.case_root / "overlapping-figure-crop-id",
            reconstruct_pdf,
            assemble_print,
            browser=BROWSER,
            fragment_html=PIPELINE_FRAGMENT,
            figure_part_id="integration-figure",
        )
        manifest = read_json(publication / "assembly-manifest.json")
        self.assertEqual(
            manifest["figures"][0]["dom_id"],
            manifest["figures"][0]["parts"][0]["id"],
        )
        review = self.case_root / "review-overlapping-id"

        result = invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        self.assertTrue(
            self.check_by_id(evidence, "manifest.integrity")["passed"]
        )
        self.assertTrue(
            self.check_by_id(
                evidence,
                "figures.crop-bindings",
            )["passed"]
        )

    def test_repeatability_and_raster_contract_seams_block_release(
        self,
    ) -> None:
        with self.subTest(check="render.repeatability"):
            publication = self.fresh_publication("repeatability")
            review = self.case_root / "review-repeatability"
            original_render_once = audit_publication.render_once
            render_number = 0

            def divergent_render(
                browser: Any,
                context: Any,
                output: Path,
                evidence_root: Path,
            ) -> Any:
                nonlocal render_number
                rendered = original_render_once(
                    browser,
                    context,
                    output,
                    evidence_root,
                )
                render_number += 1
                if render_number == 2:
                    add_pdf_vector_mark(output)
                    self.refresh_render_asset(rendered, output)
                return rendered

            with mock.patch.object(
                audit_publication,
                "render_once",
                side_effect=divergent_render,
            ):
                evidence = self.assert_blocked(
                    publication,
                    review,
                    {"render.repeatability"},
                )
            assert evidence is not None
            self.assertFalse(
                self.check_by_id(
                    evidence,
                    "render.repeatability",
                )["evidence"]["render_rasters_equal"]
            )

        with self.subTest(check="rasters.complete"):
            publication = self.fresh_publication("rasters")
            review = self.case_root / "review-rasters"
            original_inspect_pdf = audit_publication.inspect_pdf

            def duplicate_raster_binding(  # noqa: PLR0913, PLR0917
                path: Path,
                logical: str,
                page_size: str,
                raster_dir: Path,
                raster_source: str,
                evidence_root: Path,
                manifest: dict[str, Any],
            ) -> Any:
                report = original_inspect_pdf(
                    path,
                    logical,
                    page_size,
                    raster_dir,
                    raster_source,
                    evidence_root,
                    manifest,
                )
                if raster_source == "render-2":
                    report.rasters.append(dict(report.rasters[0]))
                return report

            with mock.patch.object(
                audit_publication,
                "inspect_pdf",
                side_effect=duplicate_raster_binding,
            ):
                evidence = self.assert_blocked(
                    publication,
                    review,
                    {"rasters.complete"},
                )
            assert evidence is not None
            raster_check = self.check_by_id(evidence, "rasters.complete")
            self.assertGreater(
                raster_check["evidence"]["observed"],
                raster_check["evidence"]["expected"],
            )

    def test_publication_change_during_real_render_blocks_release(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        before = tree_snapshot(publication)
        original_render_once = audit_publication.render_once
        mutated = False

        def mutate_after_render(
            browser: Any,
            context: Any,
            output: Path,
            evidence_root: Path,
        ) -> Any:
            nonlocal mutated
            rendered = original_render_once(
                browser,
                context,
                output,
                evidence_root,
            )
            if not mutated:
                (context.root / "late-change.bin").write_bytes(b"late")
                mutated = True
            return rendered

        with mock.patch.object(
            audit_publication,
            "render_once",
            side_effect=mutate_after_render,
        ):
            result = invoke(self.arguments(publication, review))

        self.assertEqual(1, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        self.assertFalse(
            self.check_by_id(
                evidence,
                "publication.tree-unchanged",
            )["passed"]
        )
        self.assertFalse(evidence["publication_tree"]["unchanged"])
        self.assertFalse((review / "release-manifest.json").exists())
        self.assertNotEqual(before, tree_snapshot(publication))

    def test_prior_review_survives_rerun_operational_failure(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        success = invoke(self.arguments(publication, review))
        self.assertEqual(0, success.exit_code, success)
        before = tree_snapshot(review)

        with mock.patch.object(
            audit_publication,
            "launch_browser",
            side_effect=audit_publication.AuditError(
                "synthetic browser failure"
            ),
        ):
            failure = invoke(self.arguments(publication, review))

        self.assertEqual(2, failure.exit_code, failure)
        self.assertEqual({}, failure.report)
        self.assertEqual(before, tree_snapshot(review))
        self.assertFalse(
            any(
                path.name.startswith(
                    (f".{review.name}.staging-", f".{review.name}.backup-")
                )
                for path in review.parent.iterdir()
            )
        )

    def test_prior_review_survives_publication_load_or_inspection_failure(
        self,
    ) -> None:
        for name in ("manifest-load", "canonical-pdf"):
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                review = self.case_root / f"review-{name}"
                success = invoke(self.arguments(publication, review))
                self.assertEqual(0, success.exit_code, success)
                before = tree_snapshot(review)

                if name == "manifest-load":
                    manifest_path = publication / "assembly-manifest.json"
                    manifest = read_json(manifest_path)
                    manifest.pop("publication_id")
                    write_json(manifest_path, manifest)
                    patcher = mock.patch.object(
                        audit_publication,
                        "render_pair",
                        return_value=[],
                    )
                else:
                    pdf = publication / "publication.pdf"
                    pdf.write_bytes(b"%PDF-1.7\nbroken\n%%EOF\n")
                    self.refresh_asset(publication, "publication.pdf")
                    patcher = mock.patch.object(
                        audit_publication,
                        "render_pair",
                        return_value=[],
                    )

                with patcher:
                    failure = invoke(
                        self.arguments(
                            publication,
                            review,
                        )
                    )

                self.assertEqual(2, failure.exit_code, failure)
                self.assertEqual({}, failure.report)
                self.assertEqual(before, tree_snapshot(review))
                self.assertFalse(
                    any(
                        path.name.startswith(
                            (
                                f".{review.name}.staging-",
                                f".{review.name}.backup-",
                            )
                        )
                        for path in review.parent.iterdir()
                    )
                )

    @unittest.skipUnless(
        sys.platform == "win32",
        "Windows short-name aliases are platform-specific.",
    )
    def test_short_name_output_alias_preserves_prior_review(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        evidence_name = "qa-evidence-manifest-long-name.json"
        success = invoke(
            self.arguments(
                publication,
                review,
                evidence_name=evidence_name,
            )
        )
        self.assertEqual(0, success.exit_code, success)
        before = tree_snapshot(review)

        short_path = windows_short_path(review / evidence_name)
        if short_path is None:
            self.skipTest("GetShortPathNameW is unavailable for this path.")
        short_name = short_path.name
        if short_name.casefold() == evidence_name.casefold():
            self.skipTest("8.3 short-name generation is disabled.")

        result = invoke(
            self.arguments(
                publication,
                review,
                evidence_name=evidence_name,
                release_name=short_name,
            )
        )

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual(before, tree_snapshot(review))
        self.assertIn("alias the same filesystem object", result.stderr)
        self.assertFalse(
            any(
                path.name.startswith(
                    (f".{review.name}.staging-", f".{review.name}.backup-")
                )
                for path in review.parent.iterdir()
            )
        )

    @unittest.skipUnless(
        sys.platform == "win32",
        "Windows short-name aliases are platform-specific.",
    )
    def test_short_name_ancestor_alias_preserves_prior_review(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        evidence_directory = "QA evidence output directory"
        evidence_name = f"{evidence_directory}/qa-evidence.json"
        success = invoke(
            self.arguments(
                publication,
                review,
                evidence_name=evidence_name,
            )
        )
        self.assertEqual(0, success.exit_code, success)
        before = tree_snapshot(review)

        short_directory = windows_short_path(review / evidence_directory)
        if short_directory is None:
            self.skipTest("GetShortPathNameW is unavailable for this path.")
        if short_directory.name.casefold() == evidence_directory.casefold():
            self.skipTest("8.3 short-name generation is disabled.")

        pdf = publication / "publication.pdf"
        add_pdf_javascript(pdf, "open-action")
        self.refresh_asset(publication, "publication.pdf")
        result = invoke(
            self.arguments(
                publication,
                review,
                evidence_name=evidence_name,
                release_name=short_directory.name,
            )
        )

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual(before, tree_snapshot(review))
        self.assertIn(
            "overlap after resolving filesystem aliases",
            result.stderr,
        )
        self.assertFalse(
            any(
                path.name.startswith(
                    (f".{review.name}.staging-", f".{review.name}.backup-")
                )
                for path in review.parent.iterdir()
            )
        )

    def test_completed_blocking_rerun_replaces_prior_success(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        success = invoke(self.arguments(publication, review))
        self.assertEqual(0, success.exit_code, success)
        previous = tree_snapshot(review)
        self.assertIn("release-manifest.json", previous)

        pdf = publication / "publication.pdf"
        add_pdf_javascript(pdf, "open-action")
        self.refresh_asset(publication, "publication.pdf")
        blocked = invoke(self.arguments(publication, review))

        self.assertEqual(1, blocked.exit_code, blocked)
        self.assertEqual({}, blocked.report)
        evidence = read_json(review / "qa-evidence.json")
        self.assertEqual("fail", evidence["mechanical_status"])
        self.assertFalse((review / "release-manifest.json").exists())
        self.assertNotEqual(previous, tree_snapshot(review))
        self.assertEqual(
            ["javascript"],
            self.check_by_id(
                evidence,
                "pdf.actions-type3-text",
            )["evidence"]["observations"]["canonical"]["actions"][
                "unsafe_kinds"
            ],
        )
        self.assertTrue(
            (review / "independent-renders" / "render-1.pdf").is_file()
        )
        self.assertTrue(
            (review / "pages" / "canonical" / "page-0001.png").is_file()
        )

    def test_unrelated_nonempty_review_root_is_rejected_unchanged(
        self,
    ) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        review.mkdir()
        (review / "unrelated.bin").write_bytes(b"unrelated review owner\n")
        before = tree_snapshot(review)

        result = invoke(self.arguments(publication, review))

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual(before, tree_snapshot(review))
        self.assertIn("ownership marker", result.stderr)

    def test_publication_rename_failure_restores_prior_review(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        success = invoke(self.arguments(publication, review))
        self.assertEqual(0, success.exit_code, success)
        before = tree_snapshot(review)
        final_root = review.resolve()
        original_replace = Path.replace

        def fail_stage_publish(source: Path, target: Path) -> Path:
            destination = Path(target)
            if (
                source.parent == final_root.parent
                and source.name.startswith(f".{final_root.name}.staging-")
                and destination == final_root
            ):
                message = "synthetic final-root rename failure"
                raise OSError(message)
            return original_replace(source, target)

        with mock.patch.object(Path, "replace", new=fail_stage_publish):
            result = invoke(self.arguments(publication, review))

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual(before, tree_snapshot(review))
        self.assertIn("synthetic final-root rename failure", result.stderr)
        self.assertFalse(
            any(
                path.name.startswith(
                    (f".{review.name}.staging-", f".{review.name}.backup-")
                )
                for path in review.parent.iterdir()
            )
        )

    def test_operational_errors_exit_two_without_publication_changes(
        self,
    ) -> None:
        publication = self.fresh_publication()
        before = tree_snapshot(publication)

        missing_flag_review = self.case_root / "review-missing-flag"
        missing_flag = invoke(
            self.arguments(
                publication,
                missing_flag_review,
                render_twice=False,
            )
        )
        self.assertEqual(2, missing_flag.exit_code, missing_flag)
        self.assertEqual({}, missing_flag.report)
        self.assertFalse((missing_flag_review / "qa-evidence.json").exists())
        self.assertEqual(before, tree_snapshot(publication))

        browser_failure_review = self.case_root / "review-browser-failure"
        with mock.patch.object(
            audit_publication,
            "launch_browser",
            side_effect=audit_publication.AuditError(
                "synthetic browser failure"
            ),
        ):
            browser_failure = invoke(
                self.arguments(publication, browser_failure_review)
            )
        self.assertEqual(2, browser_failure.exit_code, browser_failure)
        self.assertEqual({}, browser_failure.report)
        self.assertFalse((browser_failure_review / "qa-evidence.json").exists())
        self.assertFalse(
            (browser_failure_review / "release-manifest.json").exists()
        )
        self.assertEqual(before, tree_snapshot(publication))


if __name__ == "__main__":
    unittest.main(verbosity=2)
