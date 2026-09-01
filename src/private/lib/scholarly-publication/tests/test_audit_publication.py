# /// script
# requires-python = "==3.12.11"
# dependencies = [
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

import asyncio
import copy
import importlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from collections import Counter
from pathlib import Path
from typing import Any
from unittest import mock

from jsonschema import Draft202012Validator

sys.dont_write_bytecode = True
PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
SKILL = PUBLICATION_ROOT / "skills" / "scholarly-render-qa"
SUPPORT_ROOT = PUBLICATION_ROOT / "tests"
sys.path.insert(0, str(SUPPORT_ROOT))
support: Any = importlib.import_module("publication_test_support")
MainResult = support.MainResult
PdfPage = support.PdfPage
add_pdf_vector_mark = support.add_pdf_vector_mark
asset_record = support.asset_record
build_rendered_publication = support.build_rendered_publication
clear_pdf_page_contents = support.clear_pdf_page_contents
copy_tree = support.copy_tree
detect_browser = support.detect_browser
import_by_path = support.import_by_path
invoke_main = support.invoke_main
read_json = support.read_json
remove_pdf_font_programs = support.remove_pdf_font_programs
resolve_stable_asset = support.resolve_stable_asset
scale_pdf_user_unit = support.scale_pdf_user_unit
sha256_bytes = support.sha256_bytes
tree_snapshot = support.tree_snapshot
write_json = support.write_json
write_pdf = support.write_pdf
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
audit_publication = import_by_path(
    "scholarly_audit_publication_under_test",
    SKILL / "scripts" / "audit_publication.py",
)
fitz: Any = importlib.import_module("fitz")
BROWSER = detect_browser()
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
PIPELINE_SENTINEL = "qa-outcome-observer-sentinel"
PIPELINE_FRAGMENT = (
    '<h1 id="opening">Integrated publication</h1>\n'
    f'<p><a href="#opening">{PIPELINE_SENTINEL}</a></p>\n'
    "<!-- figure: integration-figure -->\n"
)


def _add_pdf_page_link(path: Path, link: dict[str, Any]) -> None:
    with fitz.open(path) as document:
        document.load_page(0).insert_link(
            {"from": fitz.Rect(24, 24, 72, 36), **link}
        )
        document.saveIncr()


def _add_pdf_named_destination(path: Path) -> None:
    with fitz.open(path) as document:
        document.new_page(width=360, height=480)
        source_xref, target_xref = document.page_xref(0), document.page_xref(1)
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
        document.xref_set_key(source_xref, "Annots", f"[{annotation_xref} 0 R]")
        document.saveIncr()


def _add_pdf_action(
    path: Path,
    subtype: str | None,
    target: str | None = None,
) -> None:
    with fitz.open(path) as document:
        action_xref = document.get_new_xref()
        suffix = "" if subtype is None else f" /S {subtype}"
        target_entry = (
            "" if target is None else f" /T <{target.encode('utf-8').hex()}>"
        )
        document.update_object(
            action_xref,
            f"<< /Type /Action{suffix}{target_entry} >>",
        )
        document.xref_set_key(
            document.pdf_catalog(), "OpenAction", f"{action_xref} 0 R"
        )
        document.saveIncr()


def _set_pdf_local_destination(
    path: Path,
    kind: str,
    raw: str | None,
    named_destination: tuple[str, str] | None = None,
    next_subtype: str | None = None,
) -> None:
    with fitz.open(path) as document:
        page_xref = document.page_xref(0)
        if named_destination is not None:
            name, destination = named_destination
            document.xref_set_key(
                document.pdf_catalog(),
                "Dests",
                f"<< /{name} {destination.format(page=page_xref)} >>",
            )
        value = None if raw is None else raw.format(page=page_xref)
        next_entry = ""
        if next_subtype is not None:
            next_xref = document.get_new_xref()
            document.update_object(
                next_xref,
                f"<< /Type /Action /S {next_subtype} >>",
            )
            next_entry = f" /Next {next_xref} 0 R"
        if kind == "goto":
            destination_entry = "" if value is None else f" /D {value}"
            action_xref = document.get_new_xref()
            document.update_object(
                action_xref,
                f"<< /Type /Action /S /GoTo{destination_entry}{next_entry} >>",
            )
            value = f"{action_xref} 0 R"
        else:
            assert value is not None
        document.xref_set_key(
            document.pdf_catalog(),
            "OpenAction",
            value,
        )
        document.saveIncr()


def _set_pdf_direct_destination(
    path: Path,
    source: str,
    raw: str,
    named_destination: tuple[str, str] | None = None,
) -> None:
    with fitz.open(path) as document:
        page_xref = document.page_xref(0)
        if named_destination is not None:
            name, destination = named_destination
            document.xref_set_key(
                document.pdf_catalog(),
                "Dests",
                f"<< /{name} {destination.format(page=page_xref)} >>",
            )
        value = raw.format(page=page_xref)
        if source == "annotation":
            annotation_xref = document.get_new_xref()
            document.update_object(
                annotation_xref,
                (
                    "<< /Type /Annot /Subtype /Link "
                    f"/Rect [24 24 72 36] /Dest {value} >>"
                ),
            )
            document.xref_set_key(
                page_xref,
                "Annots",
                f"[{annotation_xref} 0 R]",
            )
        else:
            assert source == "outline"
            outline_xref = document.get_new_xref()
            item_xref = document.get_new_xref()
            document.update_object(
                item_xref,
                (
                    f"<< /Title (QA destination) /Parent {outline_xref} 0 R "
                    f"/Dest {value} >>"
                ),
            )
            document.update_object(
                outline_xref,
                (
                    f"<< /Type /Outlines /First {item_xref} 0 R "
                    f"/Last {item_xref} 0 R /Count 1 >>"
                ),
            )
            document.xref_set_key(
                document.pdf_catalog(),
                "Outlines",
                f"{outline_xref} 0 R",
            )
        document.saveIncr()


def _pdf_action_observation(path: Path) -> dict[str, Any]:
    with fitz.open(path) as document:
        return audit_publication.pdf_actions(document, fitz)


def _invoke(arguments: list[str]) -> MainResult:
    return invoke_main(audit_publication, arguments, require_report=False)


class AuditPublicationUnitTests(unittest.TestCase):
    def test_crop_aspect_ratio_uses_relative_cross_products(self) -> None:
        self.assertFalse(
            audit_publication.same_aspect_ratio(1.0, 180.0, 1.0, 100.0)
        )
        self.assertTrue(
            audit_publication.same_aspect_ratio(18.0, 1800.0, 1.0, 100.0)
        )


class AuditPublicationScenarioTests(unittest.TestCase):
    suite_temporary: tempfile.TemporaryDirectory[str]
    suite_root: Path
    baseline_publication: Path

    @classmethod
    def setUpClass(cls) -> None:
        if BROWSER is None:
            message = "No local Chromium-family browser is available."
            if os.environ.get("CI"):
                raise RuntimeError(message)
            raise unittest.SkipTest(message)
        cls.suite_temporary = tempfile.TemporaryDirectory(
            prefix="scholarly-qa-contracted-suite-"
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
    def candidates(review: Path) -> list[Path]:
        return list(review.parent.glob(f".{review.name}.candidate-*"))

    @staticmethod
    def arguments(publication: Path, review: Path) -> list[str]:
        return [
            "--html",
            str(publication / "index.html"),
            "--assembly-manifest",
            str(publication / "assembly-manifest.json"),
            "--evidence",
            str(review / "qa-evidence.json"),
            "--release-manifest",
            str(review / "release-manifest.json"),
            "--rasters",
            str(review / "pages"),
            "--page-size",
            "letter",
            "--browser",
            str(BROWSER),
            "--render-twice",
        ]

    @staticmethod
    def browser_stack(
        _context: Any,
        pdf_side_effect: Any,
    ) -> tuple[Any, Any, Any, Any]:
        page = mock.Mock()
        page.goto = mock.AsyncMock()
        page.emulate_media = mock.AsyncMock()
        page.evaluate = mock.AsyncMock(
            side_effect=[
                None,
                {
                    "geometry": {
                        "client": 1,
                        "scroll": 1,
                        "overflow": False,
                        "elements": [],
                    }
                },
            ]
        )
        page.pdf = mock.AsyncMock(side_effect=pdf_side_effect)
        browser_context = mock.Mock()
        browser_context.route = mock.AsyncMock()
        browser_context.new_page = mock.AsyncMock(return_value=page)
        browser_context.close = mock.AsyncMock()
        browser = mock.Mock()
        browser.new_context = mock.AsyncMock(return_value=browser_context)
        browser.close = mock.AsyncMock()
        chromium = mock.Mock()
        chromium.launch = mock.AsyncMock(return_value=browser)
        playwright = mock.Mock(chromium=chromium)
        manager = mock.MagicMock()
        manager.__aenter__ = mock.AsyncMock(return_value=playwright)
        manager.__aexit__ = mock.AsyncMock(return_value=None)
        return page, browser_context, browser, manager

    @staticmethod
    def semantic_asset_records(
        manifest: dict[str, Any],
    ) -> list[dict[str, Any]]:
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
        matches = 0
        for record in cls.semantic_asset_records(manifest):
            if record["path"] == logical:
                record.update(replacement)
                matches += 1
        if matches < 1:
            message = f"manifest does not bind {logical!r}"
            raise AssertionError(message)
        write_json(manifest_path, manifest)

    def rerender_publication(self, publication: Path) -> None:
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        pdf_record = manifest["outputs"]["draft_pdf"]
        self.assertIsInstance(pdf_record, dict)
        pdf = publication / pdf_record["path"]
        manifest["outputs"]["draft_pdf"] = None
        write_json(manifest_path, manifest)
        pdf.unlink()
        result = invoke_main(
            assemble_print,
            [
                "render",
                "--html",
                str(publication / "index.html"),
                "--pdf",
                str(pdf),
                "--browser",
                str(BROWSER),
            ],
            require_report=False,
        )
        self.assertEqual(0, result.exit_code, result)

    @staticmethod
    def check_by_id(
        evidence: dict[str, Any], identifier: str
    ) -> dict[str, Any]:
        return next(
            check for check in evidence["checks"] if check["id"] == identifier
        )

    def assert_schema_valid(
        self, value: dict[str, Any], schema_name: str
    ) -> None:
        schema = read_json(SKILL / "assets" / schema_name)
        errors = sorted(
            Draft202012Validator(schema).iter_errors(value),
            key=lambda error: tuple(map(str, error.absolute_path)),
        )
        self.assertEqual(
            [], [f"{error.json_path}: {error.message}" for error in errors]
        )

    def assert_evidence_assets(
        self,
        evidence: dict[str, Any],
        publication: Path,
        review: Path,
    ) -> None:
        roots = {
            "publication-root": publication,
            "evidence-root": review,
        }
        records = [
            evidence["inputs"]["assembly_manifest"],
            *evidence["render_pdfs"].values(),
            *(raster["asset"] for raster in evidence["rasters"]),
        ]
        for record in records:
            resolve_stable_asset(record, roots)

    def assert_path_neutral_artifact(
        self,
        artifact: dict[str, Any],
        publication: Path,
    ) -> None:
        def strings(value: Any) -> list[str]:
            if isinstance(value, str):
                return [value]
            if isinstance(value, dict):
                return [
                    item
                    for nested in value.values()
                    for item in strings(nested)
                ]
            if isinstance(value, list):
                return [item for nested in value for item in strings(nested)]
            return []

        root = publication.resolve()
        serialized = json.dumps(artifact, ensure_ascii=False)
        values = strings(artifact)
        for canary in dict.fromkeys(
            (
                PIPELINE_SENTINEL,
                str(root),
                root.as_posix(),
                root.as_uri(),
            )
        ):
            self.assertFalse(any(canary in value for value in values))
            self.assertNotIn(
                json.dumps(canary, ensure_ascii=False)[1:-1],
                serialized,
            )

    def assert_blocked(
        self,
        publication: Path,
        review: Path,
        required_failed: set[str],
        *,
        exact: bool = True,
    ) -> dict[str, Any]:
        result = _invoke(self.arguments(publication, review))
        self.assertEqual(1, result.exit_code, result)
        self.assertTrue(review.is_dir())
        self.assertFalse((review / "release-manifest.json").exists())
        evidence = read_json(review / "qa-evidence.json")
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_evidence_assets(evidence, publication, review)
        self.assert_path_neutral_artifact(evidence, publication)
        failed = {
            check["id"]
            for check in evidence["checks"]
            if check["severity"] == "blocking" and not check["passed"]
        }
        if exact:
            self.assertEqual(required_failed, failed)
        else:
            self.assertTrue(
                required_failed <= failed, (required_failed, failed)
            )
        return evidence

    def test_clean_publication_publishes_complete_review_contract(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        before = tree_snapshot(publication)
        result = _invoke(self.arguments(publication, review))
        self.assertEqual(0, result.exit_code, result)
        evidence_path, release_path = (
            review / "qa-evidence.json",
            review / "release-manifest.json",
        )
        evidence, release = read_json(evidence_path), read_json(release_path)
        self.assert_schema_valid(evidence, "qa-evidence.schema.json")
        self.assert_schema_valid(release, "release-manifest.schema.json")
        self.assertEqual("pass", evidence["mechanical_status"])
        core_checks = evidence["checks"][: len(CORE_CHECK_ORDER)]
        self.assertEqual(
            list(CORE_CHECK_ORDER),
            [check["id"] for check in core_checks],
        )
        self.assertTrue(
            all(check["severity"] == "blocking" for check in core_checks)
        )
        self.assertFalse(
            any(
                check["severity"] == "blocking" and not check["passed"]
                for check in evidence["checks"]
            )
        )
        self.assert_path_neutral_artifact(evidence, publication)
        extended_evidence = copy.deepcopy(evidence)
        extended_evidence["checks"].append(
            {
                "id": "fixture.failed-advisory",
                "severity": "advisory",
                "passed": False,
                "message": "Fixture advisory does not block a pass.",
                "evidence": {},
            }
        )
        self.assert_schema_valid(extended_evidence, "qa-evidence.schema.json")
        self.assertEqual({"render_1", "render_2"}, set(evidence["render_pdfs"]))
        self.assertTrue(
            all(
                item["path_base"] == "evidence-root"
                for item in evidence["render_pdfs"].values()
            )
        )
        sources = Counter(item["source"] for item in evidence["rasters"])
        self.assertEqual({"canonical", "render-1", "render-2"}, set(sources))
        for source, count in sources.items():
            pages = [
                item["page"]
                for item in evidence["rasters"]
                if item["source"] == source
            ]
            self.assertEqual(list(range(1, count + 1)), pages)
        self.assertEqual(
            "publication-root",
            evidence["inputs"]["assembly_manifest"]["path_base"],
        )
        self.assertEqual(
            "publication-root", release["assembly_manifest"]["path_base"]
        )
        self.assertEqual("release-root", release["qa_evidence"]["path_base"])
        roots = {
            "publication-root": publication,
            "evidence-root": review,
            "release-root": review,
        }
        records = [
            evidence["inputs"]["assembly_manifest"],
            *evidence["render_pdfs"].values(),
            *(raster["asset"] for raster in evidence["rasters"]),
            release["assembly_manifest"],
            release["qa_evidence"],
        ]
        for record in records:
            resolve_stable_asset(record, roots)
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
        self.assertEqual("required", evidence["human_review"]["status"])
        self.assertTrue(evidence["human_review"]["required_scope"])
        self.assertTrue(evidence["publication_tree"]["unchanged"])
        self.assertEqual(before, tree_snapshot(publication))
        self.assertEqual("required", result.report["human_review"])

    def test_stable_asset_schemas_reject_invalid_path_bindings(self) -> None:
        publication = self.fresh_publication("stable-schema")
        review = self.case_root / "review-stable-schema"
        result = _invoke(self.arguments(publication, review))
        self.assertEqual(0, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        release = read_json(review / "release-manifest.json")
        cases = (
            (
                "evidence-missing-base",
                "qa-evidence.schema.json",
                evidence,
                ("inputs", "assembly_manifest", "path_base"),
                None,
            ),
            (
                "evidence-wrong-base",
                "qa-evidence.schema.json",
                evidence,
                ("inputs", "assembly_manifest", "path_base"),
                "evidence-root",
            ),
            (
                "evidence-unsafe-path",
                "qa-evidence.schema.json",
                evidence,
                ("render_pdfs", "render_1", "path"),
                "../render.pdf",
            ),
            (
                "release-missing-base",
                "release-manifest.schema.json",
                release,
                ("qa_evidence", "path_base"),
                None,
            ),
            (
                "release-wrong-base",
                "release-manifest.schema.json",
                release,
                ("qa_evidence", "path_base"),
                "publication-root",
            ),
            (
                "release-unsafe-path",
                "release-manifest.schema.json",
                release,
                ("assembly_manifest", "path"),
                "C:/outside/assembly-manifest.json",
            ),
        )
        for name, schema_name, source, path, replacement in cases:
            with self.subTest(name=name):
                candidate = copy.deepcopy(source)
                parent = candidate
                for key in path[:-1]:
                    parent = parent[key]
                if replacement is None:
                    parent.pop(path[-1])
                else:
                    parent[path[-1]] = replacement
                validator = Draft202012Validator(
                    read_json(SKILL / "assets" / schema_name)
                )
                self.assertTrue(list(validator.iter_errors(candidate)))

    def test_seeded_pdf_observations_publish_failure_without_release(
        self,
    ) -> None:
        publication = self.fresh_publication()
        pdf = publication / "publication.pdf"
        clear_pdf_page_contents(pdf)
        remove_pdf_font_programs(pdf)
        scale_pdf_user_unit(pdf)
        target = "https://example.invalid/unsafe-action"
        _add_pdf_page_link(pdf, {"kind": fitz.LINK_URI, "uri": target})
        self.refresh_asset(publication, "publication.pdf")
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review",
            {
                "render.geometry-overflow",
                "pdf.fonts",
                "pdf.actions-type3-text",
            },
            exact=False,
        )
        geometry = self.check_by_id(evidence, "render.geometry-overflow")[
            "evidence"
        ]
        self.assertFalse(geometry["pdf_pages"]["canonical"][0]["size_matches"])
        self.assertIn(
            "canonical",
            self.check_by_id(evidence, "pdf.fonts")["evidence"]["findings"],
        )
        behavior = self.check_by_id(evidence, "pdf.actions-type3-text")[
            "evidence"
        ]["observations"]["canonical"]
        self.assertTrue(behavior["actions"]["unsafe_detected"])
        self.assertIn("uri", behavior["actions"]["unsafe_kinds"])
        self.assertEqual(0, behavior["text_characters"])
        serialized = json.dumps(evidence, ensure_ascii=False)
        self.assertNotIn(target, serialized)
        self.assertNotIn("xref", json.dumps(behavior["actions"]).casefold())
        self.assertTrue(evidence["publication_tree"]["unchanged"])

    def test_browser_offline_and_passive_security_matrix(self) -> None:
        publication = self.fresh_publication("active-and-dormant")
        html, css = (
            publication / "index.html",
            publication / "assets" / "print.css",
        )
        manifest = read_json(publication / "assembly-manifest.json")
        source_svg = (
            publication
            / manifest["figures"][0]["parts"][0]["source_svg"]["path"]
        )
        remote = "https://example.invalid/" + ("resource" * 80)
        html.write_text(
            html.read_text(encoding="utf-8")
            .replace(
                "</svg>",
                (
                    '<animate attributeName="opacity" values="1;1"></animate>'
                    "</svg>"
                ),
                1,
            )
            .replace(
                "</section>",
                (
                    f'<script src="{remote}"></script>'
                    f'<img src="{remote}" alt="">'
                    f'<a href="{remote}">remote</a>'
                    "</section>"
                ),
                1,
            ),
            encoding="utf-8",
        )
        css.write_text(
            css.read_text(encoding="utf-8") + f'\n@import url("{remote}");\n'
            f'body {{ background: url("{remote}"); }}\n',
            encoding="utf-8",
        )
        source_svg.write_text(
            source_svg.read_text(encoding="utf-8").replace(
                "</svg>",
                (
                    f'<rect fill="url({remote})" '
                    f'style="stroke:url({remote})"/>'
                    f'<script></script><image href="{remote}"/></svg>'
                ),
                1,
            ),
            encoding="utf-8",
        )
        for logical in (
            "index.html",
            "assets/print.css",
            source_svg.relative_to(publication).as_posix(),
        ):
            self.refresh_asset(publication, logical)
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-active-and-dormant",
            {"html.offline-profile"},
            exact=False,
        )
        offline = self.check_by_id(evidence, "html.offline-profile")["evidence"]
        serialized = json.dumps(offline, ensure_ascii=False)
        for expected in (
            "blocked_requests",
            "dormant-dom-url",
            "active-elements",
            "css-resource",
            "source-svg",
        ):
            self.assertIn(expected, serialized)
        self.assertNotIn(remote, serialized)
        active = next(
            finding
            for finding in offline["findings"]
            if isinstance(finding, dict)
            and finding.get("kind") == "active-elements"
        )
        self.assertIn("animate", active["elements"])

        missing_font = self.fresh_publication("failed-local-font")
        missing_manifest = read_json(missing_font / "assembly-manifest.json")
        (missing_font / missing_manifest["fonts"][0]["asset"]["path"]).unlink()
        missing_evidence = self.assert_blocked(
            missing_font,
            self.case_root / "review-failed-local-font",
            {"manifest.integrity", "html.offline-profile", "pdf.fonts"},
        )
        requests = self.check_by_id(missing_evidence, "html.offline-profile")[
            "evidence"
        ]["request_findings"]
        self.assertIn("failed_requests", json.dumps(requests))

    def test_effective_metadata_security_matrix(self) -> None:
        author = self.fresh_publication("author-metadata")
        author_html = author / "index.html"
        author_html.write_text(
            author_html.read_text(encoding="utf-8").replace(
                "<title",
                '<meta name="author" content="QA Author">\n<title',
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(author, "index.html")
        author_result = _invoke(
            self.arguments(
                author,
                self.case_root / "review-author-metadata",
            )
        )
        self.assertEqual(0, author_result.exit_code, author_result)

        http_equiv = self.fresh_publication("http-equiv-metadata")
        http_equiv_html = http_equiv / "index.html"
        declarations = "".join(
            f'<meta http-equiv="x-qa-{index}" content="passive">\n'
            for index in range(audit_publication.MAX_DIAGNOSTIC_ITEMS + 3)
        )
        http_equiv_html.write_text(
            http_equiv_html.read_text(encoding="utf-8").replace(
                "<title",
                declarations + "<title",
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(http_equiv, "index.html")
        http_equiv_evidence = self.assert_blocked(
            http_equiv,
            self.case_root / "review-http-equiv-metadata",
            {"html.offline-profile"},
        )
        http_equiv_findings = self.check_by_id(
            http_equiv_evidence,
            "html.offline-profile",
        )["evidence"]["findings"]
        http_equiv_finding = next(
            finding
            for finding in http_equiv_findings
            if isinstance(finding, dict)
            and finding.get("kind") == "http-equiv-metadata"
        )
        self.assertEqual(
            audit_publication.MAX_DIAGNOSTIC_ITEMS + 3,
            http_equiv_finding["count"],
        )
        self.assertEqual(
            audit_publication.MAX_DIAGNOSTIC_ITEMS,
            len(http_equiv_finding["samples"]),
        )
        self.assertTrue(http_equiv_finding["truncated"])

        non_utf = self.fresh_publication("non-utf-metadata")
        non_utf_html = non_utf / "index.html"
        non_utf_content = non_utf_html.read_text(encoding="utf-8")
        self.assertIn('<meta charset="utf-8">', non_utf_content)
        non_utf_html.write_text(
            non_utf_content.replace(
                '<meta charset="utf-8">',
                '<meta charset="windows-1252">',
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(non_utf, "index.html")
        non_utf_evidence = self.assert_blocked(
            non_utf,
            self.case_root / "review-non-utf-metadata",
            {"html.offline-profile"},
        )
        non_utf_findings = self.check_by_id(
            non_utf_evidence,
            "html.offline-profile",
        )["evidence"]["findings"]
        character_set_finding = next(
            finding
            for finding in non_utf_findings
            if isinstance(finding, dict)
            and finding.get("kind") == "effective-character-set"
        )
        self.assertEqual("UTF-8", character_set_finding["expected"])
        self.assertNotEqual(
            "utf-8",
            str(character_set_finding["observed"]).casefold(),
        )

    def test_title_language_inherits_from_head(self) -> None:
        publication = self.fresh_publication("head-title-language")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["document"]["title_language"] = "fr"
        write_json(manifest_path, manifest)
        html = publication / "index.html"
        content = html.read_text(encoding="utf-8")
        self.assertIn("<head>", content)
        self.assertIn('<title lang="en">', content)
        html.write_text(
            content.replace("<head>", '<head lang="fr">', 1).replace(
                '<title lang="en">',
                "<title>",
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(publication, "index.html")

        result = _invoke(
            self.arguments(
                publication,
                self.case_root / "review-head-title-language",
            )
        )

        self.assertEqual(0, result.exit_code, result)

    def test_empty_title_language_stops_inheritance(self) -> None:
        publication = self.fresh_publication("empty-title-language")
        html = publication / "index.html"
        content = html.read_text(encoding="utf-8")
        self.assertIn('<title lang="en">', content)
        html.write_text(
            content.replace(
                '<title lang="en">',
                '<title lang="">',
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(publication, "index.html")

        self.assert_blocked(
            publication,
            self.case_root / "review-empty-title-language",
            {"html.offline-profile"},
        )

    def test_source_svg_metadata_text_is_not_parsed_as_css(self) -> None:
        publication = self.fresh_publication("svg-metadata")
        manifest = read_json(publication / "assembly-manifest.json")
        logical = manifest["figures"][0]["parts"][0]["source_svg"]["path"]
        source_svg = publication / logical
        source_svg.write_text(
            source_svg.read_text(encoding="utf-8").replace(
                "</svg>",
                (
                    '<metadata data-text="url(https://example.invalid/text) '
                    'unmatched(((">'
                    "url(https://example.invalid/body) {</metadata>"
                    "</svg>"
                ),
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(publication, logical)

        result = _invoke(
            self.arguments(
                publication,
                self.case_root / "review-svg-metadata",
            )
        )

        self.assertEqual(0, result.exit_code, result)

    def test_source_svg_css_resource_attributes_reject_remote_urls(
        self,
    ) -> None:
        publication = self.fresh_publication("svg-css-resource")
        manifest = read_json(publication / "assembly-manifest.json")
        logical = manifest["figures"][0]["parts"][0]["source_svg"]["path"]
        original = (publication / logical).read_text(encoding="utf-8")
        remote = "https://example.invalid/svg-resource"
        cases = {
            "presentation": f'fill="url({remote})"',
            "style": f'style="stroke:url({remote})"',
        }
        for name, attribute in cases.items():
            with self.subTest(name=name):
                candidate = self.case_root / f"source-{name}.svg"
                candidate.write_text(
                    original.replace(
                        "</svg>",
                        f"<rect {attribute}/></svg>",
                        1,
                    ),
                    encoding="utf-8",
                )
                _width, _height, findings = (
                    audit_publication.inspect_source_svg(
                        candidate,
                        candidate.name,
                    )
                )
                self.assertIn(
                    "nonlocal-resource",
                    {finding["category"] for finding in findings},
                )
                self.assertNotIn(remote, json.dumps(findings))

    def test_role_aware_browser_routing_blocks_wrong_resource_use(self) -> None:
        publication = self.fresh_publication("wrong-resource-role")
        manifest = read_json(publication / "assembly-manifest.json")
        css = publication / manifest["outputs"]["css"]["path"]
        font = publication / manifest["fonts"][0]["asset"]["path"]
        relative_font = Path(os.path.relpath(font, css.parent)).as_posix()
        css.write_text(
            css.read_text(encoding="utf-8")
            + f'\nbody {{ background-image: url("{relative_font}"); }}\n',
            encoding="utf-8",
        )
        self.refresh_asset(publication, manifest["outputs"]["css"]["path"])

        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-wrong-resource-role",
            {"html.offline-profile"},
        )

        offline = self.check_by_id(evidence, "html.offline-profile")["evidence"]
        self.assertIn(
            "blocked_requests",
            offline["request_findings"]["render_1"],
        )
        self.assertTrue(self.check_by_id(evidence, "pdf.fonts")["passed"])

    def test_page_rule_observation_respects_active_conditions(self) -> None:
        inactive = self.fresh_publication("inactive-screen-page")
        inactive_manifest = read_json(inactive / "assembly-manifest.json")
        inactive_css = inactive / inactive_manifest["outputs"]["css"]["path"]
        inactive_css.write_text(
            inactive_css.read_text(encoding="utf-8")
            + "\n@media screen { @page { size: A4; margin: 0; } }\n",
            encoding="utf-8",
        )
        self.refresh_asset(
            inactive,
            inactive_manifest["outputs"]["css"]["path"],
        )
        inactive_result = _invoke(
            self.arguments(
                inactive,
                self.case_root / "review-inactive-screen-page",
            )
        )
        self.assertEqual(0, inactive_result.exit_code, inactive_result)

        duplicate = self.fresh_publication("duplicate-active-page")
        duplicate_manifest = read_json(duplicate / "assembly-manifest.json")
        duplicate_css = duplicate / duplicate_manifest["outputs"]["css"]["path"]
        duplicate_css.write_text(
            duplicate_css.read_text(encoding="utf-8")
            + "\n@page { size: Letter; margin: 0.75in; }\n",
            encoding="utf-8",
        )
        self.refresh_asset(
            duplicate,
            duplicate_manifest["outputs"]["css"]["path"],
        )
        duplicate_evidence = self.assert_blocked(
            duplicate,
            self.case_root / "review-duplicate-active-page",
            {"html.offline-profile"},
        )
        self.assertIn(
            "exactly one active unqualified",
            json.dumps(
                self.check_by_id(
                    duplicate_evidence,
                    "html.offline-profile",
                )["evidence"]
            ),
        )

    def test_semantic_fragment_content_order_and_overflow_matrix(self) -> None:
        publication = self.fresh_publication("visible-and-overflow")
        html, css = (
            publication / "index.html",
            publication / "assets" / "print.css",
        )
        html.write_text(
            html.read_text(encoding="utf-8")
            .replace(
                "</section>", '<div class="qa-wide">wide</div></section>', 1
            )
            .replace("</main>", "</main><p id=outside>outside</p>", 1),
            encoding="utf-8",
        )
        css.write_text(
            css.read_text(encoding="utf-8")
            + "\n#opening { opacity: 0; }\n.qa-wide { width: 2000px; }\n",
            encoding="utf-8",
        )
        self.refresh_asset(publication, "index.html")
        self.refresh_asset(publication, "assets/print.css")
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-visible-and-overflow",
            {
                "html.offline-profile",
                "render.geometry-overflow",
            },
            exact=False,
        )
        offline = json.dumps(
            self.check_by_id(evidence, "html.offline-profile")["evidence"]
        )
        self.assertIn("visible-text hash mismatch", offline)
        self.assertIn("visible-content-outside-fragments", offline)
        self.assertTrue(
            self.check_by_id(evidence, "render.geometry-overflow")["evidence"][
                "overflow"
            ]
        )

        cardinality = self.fresh_publication("fragment-cardinality")
        cardinality_html = cardinality / "index.html"
        content = cardinality_html.read_text(encoding="utf-8")
        matched = re.search(
            r'(<section\b[^>]*data-fragment-id="[^"]+".*?</section>)',
            content,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(matched)
        section = matched.group(1)
        cardinality_html.write_text(
            content.replace(section, section + section, 1), encoding="utf-8"
        )
        self.refresh_asset(cardinality, "index.html")
        cardinality_evidence = self.assert_blocked(
            cardinality,
            self.case_root / "review-fragment-cardinality",
            {"html.offline-profile"},
            exact=False,
        )
        self.assertIn(
            "not exactly once",
            json.dumps(
                self.check_by_id(cardinality_evidence, "html.offline-profile")[
                    "evidence"
                ]
            ),
        )

        ordered = self.fresh_publication("fragment-order")
        manifest_path = ordered / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        original = manifest["fragments"][0]
        original_asset = ordered / original["asset"]["path"]
        second_asset = original_asset.with_name("section-two.html")
        second_asset.write_bytes(original_asset.read_bytes())
        second = copy.deepcopy(original)
        second.update(
            {
                "id": "section-two",
                "asset": asset_record(ordered, second_asset),
                "dom_selector": '[data-fragment-id="section-two"]',
            }
        )
        manifest["fragments"].append(second)
        write_json(manifest_path, manifest)
        ordered_html = ordered / "index.html"
        content = ordered_html.read_text(encoding="utf-8")
        matched = re.search(
            rf'(<section\b[^>]*data-fragment-id="{original["id"]}".*?</section>)',
            content,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(matched)
        original_section = matched.group(1)
        second_section = original_section.replace(
            f'data-fragment-id="{original["id"]}"',
            'data-fragment-id="section-two"',
            1,
        )
        ordered_html.write_text(
            content.replace(
                original_section, second_section + original_section, 1
            ),
            encoding="utf-8",
        )
        self.refresh_asset(ordered, "index.html")
        ordered_evidence = self.assert_blocked(
            ordered,
            self.case_root / "review-fragment-order",
            {"html.offline-profile"},
            exact=False,
        )
        self.assertIn(
            "manifest order",
            json.dumps(
                self.check_by_id(ordered_evidence, "html.offline-profile")[
                    "evidence"
                ]
            ),
        )

    def test_figure_crop_caption_and_ownership_matrix(self) -> None:
        publication = self.fresh_publication()
        manifest = read_json(publication / "assembly-manifest.json")
        figure, part = (
            manifest["figures"][0],
            manifest["figures"][0]["parts"][0],
        )
        html = publication / "index.html"
        content = html.read_text(encoding="utf-8")
        content = content.replace(
            f'aria-label="{figure["alt"]}"',
            'aria-label="wrong figure label"',
            1,
        )
        content = re.sub(
            r'viewBox="[^"]+"', 'viewBox="0 0 1 1"', content, count=1
        )
        content = re.sub(
            r"<figcaption>.*?</figcaption>",
            "<figcaption>Wrong caption.</figcaption>",
            content,
            count=1,
            flags=re.DOTALL,
        )
        content = re.sub(
            r'(<image\b[^>]*\bhref=")[^"]+("[^>]*>)',
            r"\1inputs/source-package.json\2",
            content,
            count=1,
        )
        crop_match = re.search(
            rf'(<svg\b[^>]*data-crop-id="{re.escape(part["id"])}".*?</svg>)',
            content,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(crop_match)
        crop_markup = crop_match.group(1)
        content = content.replace(crop_markup, "", 1)
        content = content.replace("</figure>", f"</figure>{crop_markup}", 1)
        extra = (
            '<figure data-figure-id="unbound"><figcaption>extra</figcaption>'
            '<svg data-crop-id="unbound-crop" role="img" '
            'aria-label="extra" viewBox="0 0 1 1">'
            '<image href="inputs/source-package.json"></image></svg></figure>'
        )
        html.write_text(
            content.replace("</section>", extra + "</section>", 1),
            encoding="utf-8",
        )
        self.refresh_asset(publication, "index.html")
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review",
            {"figures.crop-bindings"},
            exact=False,
        )
        figure_check = self.check_by_id(evidence, "figures.crop-bindings")[
            "evidence"
        ]
        findings = json.dumps(figure_check, ensure_ascii=False)
        for expected in (
            "cardinality",
            "aria-label",
            "caption mismatch",
            "ownership mismatch",
            "viewBox mismatch",
            "source mismatch",
            "unbound",
        ):
            self.assertIn(expected, findings)
        crop = next(
            item for item in figure_check["crops"] if item["id"] == part["id"]
        )
        self.assertFalse(crop["source_matches"])
        self.assertFalse(crop["viewbox_matches"])

    def test_manifest_integrity_and_no_review_operational_matrix(self) -> None:
        invalid = self.fresh_publication("schema-invalid")
        invalid_manifest_path = invalid / "assembly-manifest.json"
        invalid_manifest = read_json(invalid_manifest_path)
        invalid_manifest.pop("publication_id")
        write_json(invalid_manifest_path, invalid_manifest)
        invalid_review = self.case_root / "review-schema-invalid"
        invalid_result = _invoke(self.arguments(invalid, invalid_review))
        self.assertEqual(2, invalid_result.exit_code, invalid_result)
        self.assertFalse(invalid_review.exists())

        malformed = self.fresh_publication("load-invalid")
        (malformed / "assembly-manifest.json").write_text(
            "{",
            encoding="utf-8",
        )
        malformed_review = self.case_root / "review-load-invalid"
        malformed_result = _invoke(self.arguments(malformed, malformed_review))
        self.assertEqual(2, malformed_result.exit_code, malformed_result)
        self.assertFalse(malformed_review.exists())

        publication = self.fresh_publication("integrity-findings")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["policies"]["publication_profile"]["sha256"] = "0" * 64
        manifest["fragments"][0]["asset"]["sha256"] = "1" * 64
        manifest["fragments"].append(copy.deepcopy(manifest["fragments"][0]))
        manifest["figures"][0]["caption_sha256"] = "2" * 64
        bbox = manifest["figures"][0]["parts"][0]["bbox"]
        bbox[2] = bbox[0]
        manifest["font_roles"]["body-latin"] = "Undeclared Fixture Font"
        conflicting = copy.deepcopy(manifest["stylesheets"][0])
        conflicting["sha256"] = "3" * 64
        manifest["stylesheets"].append(conflicting)
        write_json(manifest_path, manifest)
        (publication / "undeclared.bin").write_bytes(b"extra")
        evidence = self.assert_blocked(
            publication,
            self.case_root / "review-integrity-findings",
            {"manifest.integrity"},
            exact=False,
        )
        findings = json.dumps(
            self.check_by_id(evidence, "manifest.integrity")["evidence"]
        )
        for expected in (
            "publication-profile identity",
            "binding mismatch",
            "duplicate fragment IDs",
            "caption hash",
            "positive width",
            "font role",
            "conflicting semantic asset declarations",
            "undeclared regular files",
        ):
            self.assertIn(expected, findings)

    def test_shared_source_svg_completes_full_qa_review(self) -> None:
        publication = self.fresh_publication("shared-svg")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        figure = manifest["figures"][0]
        original_part = figure["parts"][0]
        shared_part = copy.deepcopy(original_part)
        shared_part.update(
            {
                "id": "integration-figure-part-shared",
                "order": 2,
                "dom_selector": (
                    '[data-crop-id="integration-figure-part-shared"]'
                ),
            }
        )
        figure["parts"].append(shared_part)
        write_json(manifest_path, manifest)

        html = publication / "index.html"
        content = html.read_text(encoding="utf-8")
        matched = re.search(
            (
                rf'(<svg\b[^>]*data-crop-id="{re.escape(original_part["id"])}"'
                r".*?</svg>)"
            ),
            content,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(matched)
        original_crop = matched.group(1)
        shared_crop = original_crop.replace(
            f'data-crop-id="{original_part["id"]}"',
            'data-crop-id="integration-figure-part-shared"',
            1,
        ).replace(
            f"{figure['alt']} - part 1",
            f"{figure['alt']} - part 2",
        )
        html.write_text(
            content.replace(
                original_crop,
                original_crop + shared_crop,
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_asset(publication, "index.html")
        self.rerender_publication(publication)

        review = self.case_root / "review-shared-svg"
        result = _invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        crop_records = self.check_by_id(
            evidence,
            "figures.crop-bindings",
        )["evidence"]["crops"]
        self.assertEqual(
            {
                original_part["id"],
                "integration-figure-part-shared",
            },
            {record["id"] for record in crop_records},
        )
        self.assertEqual(
            {original_part["source_svg"]["sha256"]},
            {record["source_svg_sha256"] for record in crop_records},
        )

    def test_repeatability_raster_completeness_and_page_bound(self) -> None:
        publication = self.fresh_publication("repeatability")
        original_render_once = audit_publication.render_once
        render_number = 0

        async def divergent_render(
            browser: Any, context: Any, output: Path, evidence_root: Path
        ) -> Any:
            nonlocal render_number
            rendered = await original_render_once(
                browser, context, output, evidence_root
            )
            render_number += 1
            if render_number == 2:
                add_pdf_vector_mark(output)
                content = output.read_bytes()
                rendered.pdf = type(rendered.pdf)(
                    rendered.pdf.path,
                    output,
                    sha256_bytes(content),
                    len(content),
                )
            return rendered

        with mock.patch.object(
            audit_publication, "render_once", side_effect=divergent_render
        ):
            evidence = self.assert_blocked(
                publication,
                self.case_root / "review-repeatability",
                {"render.repeatability"},
            )
        self.assertFalse(
            self.check_by_id(evidence, "render.repeatability")["evidence"][
                "render_rasters_equal"
            ]
        )

        rasters = self.fresh_publication("raster-completeness")
        original_inspect_pdf = audit_publication.inspect_pdf

        def duplicate_raster(  # noqa: PLR0913, PLR0917
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
            audit_publication, "inspect_pdf", side_effect=duplicate_raster
        ):
            raster_evidence = self.assert_blocked(
                rasters,
                self.case_root / "review-raster-completeness",
                {"rasters.complete"},
            )
        raster_check = self.check_by_id(raster_evidence, "rasters.complete")[
            "evidence"
        ]
        self.assertGreater(raster_check["observed"], raster_check["expected"])

    def test_render_timeout_bounds_pdf_and_does_not_publish_output(
        self,
    ) -> None:
        publication = self.fresh_publication("render-timeout")
        context = audit_publication.load_context(
            publication / "assembly-manifest.json",
            publication / "index.html",
            "letter",
        )
        pdf_bytes = (publication / "publication.pdf").read_bytes()

        async def delayed_pdf(**_kwargs: Any) -> bytes:
            await asyncio.sleep(0.15)
            return pdf_bytes

        page, browser_context, browser, manager = self.browser_stack(
            context,
            delayed_pdf,
        )

        async def delayed_context(**_kwargs: Any) -> Any:
            await asyncio.sleep(0.1)
            return browser_context

        browser.new_context.side_effect = delayed_context

        with (
            mock.patch(
                "playwright.async_api.async_playwright",
                return_value=manager,
            ),
            mock.patch.object(audit_publication, "RENDER_TIMEOUT_MS", 200),
            self.assertRaisesRegex(  # noqa: PT027
                audit_publication.AuditError,
                "fixed 200 ms deadline",
            ),
        ):
            audit_publication.render_pair(
                context,
                self.case_root / "timeout-renders",
                self.case_root,
                BROWSER,
            )

        page.pdf.assert_awaited_once()
        self.assertNotIn("path", page.pdf.await_args.kwargs)
        self.assertEqual(
            f"1-{audit_publication.MAX_PDF_PAGES + 1}",
            page.pdf.await_args.kwargs["page_ranges"],
        )
        browser_context.close.assert_awaited_once_with()
        browser.close.assert_awaited_once_with()
        manager.__aexit__.assert_awaited_once()
        self.assertFalse(
            (self.case_root / "timeout-renders" / "render-1.pdf").exists()
        )

    def test_pdf_page_ceiling_precedes_render_and_rasterization(  # noqa: PLR0915
        self,
    ) -> None:
        oversized = self.fresh_publication("oversized-canonical")
        oversized_pdf = oversized / "publication.pdf"
        write_pdf(
            oversized_pdf,
            (
                PdfPage(vector_marks=False)
                for _page in range(audit_publication.MAX_PDF_PAGES + 1)
            ),
        )
        self.refresh_asset(oversized, "publication.pdf")
        raster_dir = self.case_root / "oversized-pages"

        with (
            mock.patch.object(fitz.Page, "get_pixmap") as get_pixmap,
            self.assertRaisesRegex(  # noqa: PT027
                audit_publication.PublicationError,
                "fixed 500-page ceiling",
            ),
        ):
            audit_publication.inspect_pdf(
                oversized_pdf,
                "publication.pdf",
                "letter",
                raster_dir,
                "canonical",
                self.case_root,
                {"fonts": [], "font_roles": {}},
            )
        get_pixmap.assert_not_called()
        self.assertFalse(raster_dir.exists())

        maximum_pages = self.case_root / "maximum-pages.pdf"
        write_pdf(
            maximum_pages,
            (
                PdfPage(vector_marks=False)
                for _page in range(audit_publication.MAX_PDF_PAGES)
            ),
        )
        self.assertEqual(
            audit_publication.MAX_PDF_PAGES,
            audit_publication.pdf_page_count(
                maximum_pages,
                "maximum-page fixture",
            ),
        )

        oversized_geometry = self.case_root / "oversized-geometry.pdf"
        write_pdf(oversized_geometry, [PdfPage(vector_marks=False)])
        scale_pdf_user_unit(oversized_geometry, scale=100)
        geometry_rasters = self.case_root / "oversized-geometry-pages"
        with (
            mock.patch.object(fitz.Page, "get_pixmap") as geometry_pixmap,
            self.assertRaisesRegex(  # noqa: PT027
                audit_publication.PublicationError,
                "too large to rasterize safely",
            ),
        ):
            audit_publication.inspect_pdf(
                oversized_geometry,
                "oversized-geometry.pdf",
                "letter",
                geometry_rasters,
                "canonical",
                self.case_root,
                {"fonts": [], "font_roles": {}},
            )
        geometry_pixmap.assert_not_called()

        boundary_geometry = self.case_root / "boundary-geometry.pdf"
        with fitz.open() as boundary_document:
            boundary_document.new_page(width=612, height=792)
            boundary_document.save(boundary_geometry)
        scale_pdf_user_unit(boundary_geometry, scale=2)
        boundary_rasters = self.case_root / "boundary-geometry-pages"
        with fitz.open(boundary_geometry) as boundary_document:
            page = boundary_document.load_page(0)
            observed_area = math.ceil(
                page.rect.width * audit_publication.RASTER_SCALE
            ) * math.ceil(page.rect.height * audit_publication.RASTER_SCALE)
        width, height = audit_publication.PAGE_POINTS["letter"]
        self.assertEqual(
            width
            * height
            * audit_publication.RASTER_SCALE**2
            * audit_publication.MAX_RASTER_AREA_FACTOR,
            observed_area,
        )
        pixmap = mock.Mock()
        pixmap.tobytes.return_value = b"bounded-raster"
        with mock.patch.object(
            fitz.Page,
            "get_pixmap",
            return_value=pixmap,
        ) as boundary_pixmap:
            boundary_report = audit_publication.inspect_pdf(
                boundary_geometry,
                "boundary-geometry.pdf",
                "letter",
                boundary_rasters,
                "canonical",
                self.case_root,
                {"fonts": [], "font_roles": {}},
            )
        boundary_pixmap.assert_called_once()
        self.assertEqual(1, len(boundary_report.rasters))

        review = self.case_root / "review-oversized-canonical"
        with mock.patch.object(audit_publication, "render_pair") as render_pair:
            result = _invoke(self.arguments(oversized, review))
        self.assertEqual(2, result.exit_code, result)
        self.assertIn("fixed 500-page ceiling", result.stderr)
        render_pair.assert_not_called()
        self.assertFalse(review.exists())

        generated = self.fresh_publication("oversized-generated")
        generated_context = audit_publication.load_context(
            generated / "assembly-manifest.json",
            generated / "index.html",
            "letter",
        )

        async def return_oversized(**_kwargs: Any) -> bytes:
            return oversized_pdf.read_bytes()

        _page, browser_context, browser, _manager = self.browser_stack(
            generated_context, return_oversized
        )
        output = self.case_root / "generated-ceiling" / "render.pdf"

        with (
            mock.patch.object(fitz.Page, "get_pixmap") as generated_pixmap,
            self.assertRaisesRegex(  # noqa: PT027
                audit_publication.AuditError,
                "fixed 500-page ceiling",
            ),
        ):
            asyncio.run(
                audit_publication.render_once(
                    browser,
                    generated_context,
                    output,
                    self.case_root,
                )
            )
        generated_pixmap.assert_not_called()
        self.assertNotIn("path", _page.pdf.await_args.kwargs)
        browser_context.close.assert_awaited_once_with()
        self.assertFalse(output.exists())

    def test_publication_mutation_is_completed_mechanical_failure(self) -> None:
        publication = self.fresh_publication()
        review = self.case_root / "review"
        before = tree_snapshot(publication)
        original_render_once = audit_publication.render_once
        mutated = False

        async def mutate_after_render(
            browser: Any, context: Any, output: Path, evidence_root: Path
        ) -> Any:
            nonlocal mutated
            rendered = await original_render_once(
                browser, context, output, evidence_root
            )
            if not mutated:
                (context.root / "late-change.bin").write_bytes(b"late")
                mutated = True
            return rendered

        with mock.patch.object(
            audit_publication, "render_once", side_effect=mutate_after_render
        ):
            result = _invoke(self.arguments(publication, review))
        self.assertEqual(1, result.exit_code, result)
        evidence = read_json(review / "qa-evidence.json")
        self.assert_evidence_assets(evidence, publication, review)
        self.assertFalse(evidence["publication_tree"]["unchanged"])
        self.assertFalse(
            self.check_by_id(evidence, "publication.tree-unchanged")["passed"]
        )
        self.assertFalse((review / "release-manifest.json").exists())
        self.assertNotEqual(before, tree_snapshot(publication))

    def test_existing_review_roots_are_rejected_unchanged(self) -> None:
        factories = {
            "file": lambda path: path.write_text("occupied", encoding="utf-8"),
            "empty-directory": lambda path: path.mkdir(),
            "nonempty-directory": lambda path: (
                path.mkdir(),
                (path / "prior.txt").write_text("prior", encoding="utf-8"),
            ),
        }
        for name, create in factories.items():
            with self.subTest(name=name):
                publication = self.fresh_publication(f"existing-{name}")
                review = self.case_root / f"review-existing-{name}"
                create(review)
                before = tree_snapshot(review) if review.is_dir() else None
                result = _invoke(self.arguments(publication, review))
                self.assertEqual(2, result.exit_code, result)
                self.assertTrue(review.exists())
                if name == "file":
                    self.assertEqual(
                        "occupied",
                        review.read_text(encoding="utf-8"),
                    )
                elif name == "empty-directory":
                    self.assertEqual([], list(review.iterdir()))
                elif name == "nonempty-directory":
                    self.assertEqual(
                        "prior",
                        (review / "prior.txt").read_text(encoding="utf-8"),
                    )
                if before is not None:
                    self.assertEqual(before, tree_snapshot(review))

    def test_posix_special_review_root_is_rejected_or_skipped(self) -> None:
        mkfifo = getattr(os, "mkfifo", None)
        if os.name == "nt":
            self.skipTest(
                "POSIX FIFO creation is not supported on this platform."
            )
        if not callable(mkfifo):
            self.skipTest(
                "POSIX FIFO creation is not supported on this platform."
            )
        publication = self.fresh_publication("existing-special")
        review = self.case_root / "review-existing-special"
        try:
            mkfifo(review)
        except OSError as error:
            self.skipTest(f"POSIX FIFO creation is unavailable: {error}")

        result = _invoke(self.arguments(publication, review))

        self.assertEqual(2, result.exit_code, result)
        self.assertTrue(review.exists())

    def test_symlinked_review_path_component_uses_resolved_root(self) -> None:
        publication = self.fresh_publication("symlink-component")
        physical_parent = self.case_root / "physical-parent"
        physical_parent.mkdir()
        linked_parent = self.case_root / "linked-parent"
        try:
            linked_parent.symlink_to(
                physical_parent,
                target_is_directory=True,
            )
        except (NotImplementedError, OSError) as error:
            self.skipTest(f"directory symlinks are unavailable: {error}")
        review = linked_parent / "review"

        result = _invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        self.assertTrue(
            (physical_parent / "review" / "qa-evidence.json").is_file()
        )
        self.assertTrue(
            (physical_parent / "review" / "release-manifest.json").is_file()
        )

        publication_alias = self.case_root / "publication-alias"
        publication_alias.symlink_to(
            publication,
            target_is_directory=True,
        )
        aliased_review = publication_alias / "review-via-alias"
        before = tree_snapshot(publication)

        aliased_result = _invoke(self.arguments(publication, aliased_review))

        self.assertEqual(2, aliased_result.exit_code, aliased_result)
        self.assertFalse(aliased_review.exists())
        self.assertEqual(before, tree_snapshot(publication))

        manifest_alias = self.case_root / "manifest-alias"
        manifest_alias.mkdir()
        manifest_link = manifest_alias / "assembly-manifest.json"
        manifest_link.symlink_to(publication / "assembly-manifest.json")
        nested_review = publication / "review-via-manifest-link"
        before = tree_snapshot(publication)
        arguments = self.arguments(publication, nested_review)
        arguments[arguments.index("--assembly-manifest") + 1] = str(
            manifest_link
        )

        nested_result = _invoke(arguments)

        self.assertEqual(2, nested_result.exit_code, nested_result)
        self.assertFalse(nested_review.exists())
        self.assertEqual(before, tree_snapshot(publication))

    def test_windows_junction_review_path_component_uses_resolved_root(
        self,
    ) -> None:
        if os.name != "nt":
            self.skipTest("Windows directory junctions are Windows-specific.")
        publication = self.fresh_publication("junction-component")
        physical_parent = self.case_root / "junction-target"
        physical_parent.mkdir()
        junction = self.case_root / "junction-parent"
        command = [
            os.environ.get("COMSPEC", "cmd.exe"),
            "/d",
            "/c",
            "mklink",
            "/J",
            str(junction),
            str(physical_parent),
        ]
        completed = subprocess.run(  # noqa: S603
            command,
            check=False,
            capture_output=True,
            text=True,
        )
        if completed.returncode != 0:
            self.skipTest(
                "Windows directory junction creation is unavailable: "
                f"{completed.stderr or completed.stdout}"
            )
        review = junction / "review"

        result = _invoke(self.arguments(publication, review))

        self.assertEqual(0, result.exit_code, result)
        self.assertTrue(
            (physical_parent / "review" / "qa-evidence.json").is_file()
        )
        self.assertTrue(
            (physical_parent / "review" / "release-manifest.json").is_file()
        )

    def test_completed_review_uses_one_final_rename(self) -> None:
        publication = self.fresh_publication("single-rename")
        review = self.case_root / "review-single-rename"
        original_rename = Path.rename
        rename_calls: list[tuple[Path, Path]] = []

        def tracked_rename(source: Path, target: Path) -> Path:
            rename_calls.append((source, target))
            return original_rename(source, target)

        with mock.patch.object(Path, "rename", tracked_rename):
            result = _invoke(self.arguments(publication, review))
        self.assertEqual(0, result.exit_code, result)
        self.assertEqual(1, len(rename_calls))
        self.assertEqual(review, rename_calls[0][1])
        self.assertEqual(review.parent, rename_calls[0][0].parent)

    def test_failed_review_operations_clean_candidates(self) -> None:
        rename_publication = self.fresh_publication("rename-failure")
        rename_review = self.case_root / "review-rename-failure"
        with mock.patch.object(
            Path, "rename", side_effect=OSError("rename blocked")
        ):
            result = _invoke(self.arguments(rename_publication, rename_review))
        self.assertEqual(2, result.exit_code, result)
        self.assertFalse(rename_review.exists())
        self.assertEqual([], self.candidates(rename_review))

        operational_publication = self.fresh_publication("operational-failure")
        operational_review = self.case_root / "review-operational-failure"
        with mock.patch.object(
            audit_publication,
            "audit_candidate",
            side_effect=audit_publication.PublicationError(
                "forced audit failure"
            ),
        ):
            result = _invoke(
                self.arguments(operational_publication, operational_review)
            )
        self.assertEqual(2, result.exit_code, result)
        self.assertFalse(operational_review.exists())
        self.assertEqual([], self.candidates(operational_review))

    def test_cleanup_failure_reports_the_orphan_candidate(self) -> None:
        orphan_publication = self.fresh_publication("cleanup-failure")
        orphan_review = self.case_root / "review-cleanup-failure"
        original_rmtree = shutil.rmtree

        def reject_candidate_cleanup(
            path: Path | str, *args: Any, **kwargs: Any
        ) -> None:
            candidate = Path(path)
            if candidate.name.startswith(f".{orphan_review.name}.candidate-"):
                message = "cleanup blocked"
                raise OSError(message)
            original_rmtree(path, *args, **kwargs)

        with (
            mock.patch.object(
                audit_publication,
                "audit_candidate",
                side_effect=audit_publication.PublicationError(
                    "forced audit failure"
                ),
            ),
            mock.patch.object(
                audit_publication.shutil,
                "rmtree",
                side_effect=reject_candidate_cleanup,
            ),
        ):
            result = _invoke(self.arguments(orphan_publication, orphan_review))
        self.assertEqual(2, result.exit_code, result)
        self.assertFalse(orphan_review.exists())
        candidates = self.candidates(orphan_review)
        self.assertEqual(1, len(candidates))
        self.assertIn("candidate cleanup failed", result.stderr)
        self.assertIn("orphan candidate", result.stderr)
        self.assertIn(str(candidates[0]), result.stderr)

    def test_review_path_validation_and_bounded_path_neutral_diagnostics(
        self,
    ) -> None:
        publication = self.fresh_publication("path-rules")
        for name in ("bad. ", "bad."):
            review = self.case_root / "review-parent" / name / "review"
            result = _invoke(self.arguments(publication, review))
            self.assertEqual(2, result.exit_code, result)
            self.assertFalse(review.exists())
        overlap_review = publication / "review"
        overlap_result = _invoke(self.arguments(publication, overlap_review))
        self.assertEqual(2, overlap_result.exit_code, overlap_result)
        self.assertFalse(overlap_review.exists())

        case_alias_review = self.case_root / "review-case-alias"
        case_alias_arguments = self.arguments(publication, case_alias_review)
        case_alias_arguments[
            case_alias_arguments.index("--release-manifest") + 1
        ] = str(case_alias_review / "QA-EVIDENCE.JSON")
        case_alias_result = _invoke(case_alias_arguments)
        self.assertEqual(2, case_alias_result.exit_code, case_alias_result)
        self.assertFalse(case_alias_review.exists())

        raw = "https://example.invalid/" + ("sensitive" * 100)
        resource = audit_publication.resource_url_diagnostic(
            raw,
            "dormant-dom-url",
        )
        self.assertNotIn(raw, json.dumps(resource))
        self.assertEqual("resource-url", resource["kind"])
        self.assertIn("sha256", resource)
        bounded = audit_publication.bounded_diagnostic(
            [
                audit_publication.resource_url_diagnostic(
                    raw,
                    "blocked-request",
                )
                for _ in range(audit_publication.MAX_DIAGNOSTIC_ITEMS + 8)
            ]
        )
        self.assertIsInstance(bounded, dict)
        self.assertLessEqual(
            len(bounded["samples"]),
            audit_publication.MAX_DIAGNOSTIC_ITEMS,
        )
        self.assertNotIn(raw, json.dumps(bounded))
        nested_raw = "nested-sensitive-" + ("value" * 300)
        checks: list[dict[str, Any]] = []
        audit_publication.add_check(
            checks,
            "fixture.bounded-diagnostic",
            passed=False,
            message="fixture",
            evidence={
                "nested": [
                    {"payload": nested_raw}
                    for _ in range(audit_publication.MAX_DIAGNOSTIC_ITEMS + 7)
                ]
            },
        )
        nested = checks[0]["evidence"]["nested"]
        self.assertEqual(
            audit_publication.MAX_DIAGNOSTIC_ITEMS + 7,
            nested["total"],
        )
        self.assertEqual(7, nested["omitted"])
        self.assertIn("sha256", nested)
        self.assertNotIn(nested_raw, json.dumps(checks))
        self.assertIn(
            sha256_bytes(nested_raw.encode()),
            nested["samples"][0]["payload"],
        )
        read_failure = audit_publication.text_read_diagnostic(
            "inputs/fragment.html",
            "read-error",
            OSError("C:\\private\\absolute\\secret.txt"),
        )
        self.assertNotIn("absolute", json.dumps(read_failure))

    def assert_local_pdf_destination_contract(self) -> None:
        valid_name = "qa-valid-local-destination"
        invalid_name = "qa-invalid-local-destination-canary"
        non_page_name = "qa-non-page-local-destination-canary"
        valid = (valid_name, "[{page} 0 R /Fit]")
        out_of_range = (invalid_name, "[-1 /Fit]")
        non_page = (non_page_name, "[0 /Fit]")
        reports: list[dict[str, Any]] = []
        cases = (
            ("open", "array-valid", "[{page} 0 R /Fit]", None, False),
            ("open", "array-invalid", "[]", None, True),
            ("open", "name-valid", f"/{valid_name}", valid, False),
            (
                "open",
                "name-out-of-range",
                f"/{invalid_name}",
                out_of_range,
                True,
            ),
            (
                "open",
                "name-non-page",
                f"/{non_page_name}",
                non_page,
                True,
            ),
            ("open", "string-valid", f"({valid_name})", valid, False),
            ("open", "string-unresolved", f"({invalid_name})", None, True),
            ("open", "wrong-type", "42", None, True),
            ("goto", "array-valid", "[{page} 0 R /Fit]", None, False),
            ("goto", "array-non-page", "[0 /Fit]", None, True),
            ("goto", "name-valid", f"/{valid_name}", valid, False),
            (
                "goto",
                "name-out-of-range",
                f"/{invalid_name}",
                out_of_range,
                True,
            ),
            (
                "goto",
                "string-non-page",
                f"({non_page_name})",
                non_page,
                True,
            ),
            ("goto", "string-valid", f"({valid_name})", valid, False),
            ("goto", "string-unresolved", f"({invalid_name})", None, True),
            ("goto", "missing", None, None, True),
            ("goto", "wrong-type", "42", None, True),
        )
        for kind, name, raw, named, invalid in cases:
            with self.subTest(destination=f"{kind}-{name}"):
                path = self.case_root / f"{kind}-{name}.pdf"
                write_pdf(path, [PdfPage()])
                _set_pdf_local_destination(
                    path,
                    kind,
                    raw,
                    named,
                )
                report = _pdf_action_observation(path)
                reports.append(report)
                self.assertEqual(
                    ["invalid-action"] if invalid else [],
                    report["unsafe_kinds"],
                )

        path = self.case_root / "goto-invalid-with-next.pdf"
        write_pdf(path, [PdfPage()])
        _set_pdf_local_destination(path, "goto", None, None, "/URI")
        next_report = _pdf_action_observation(path)
        reports.append(next_report)
        self.assertEqual(
            ["invalid-action", "uri"],
            next_report["unsafe_kinds"],
        )

        direct_cases = (
            (
                "annotation",
                "array-valid",
                "[{page} 0 R /Fit]",
                None,
                False,
            ),
            ("annotation", "array-invalid", "[]", None, True),
            (
                "annotation",
                "name-valid",
                f"/{valid_name}",
                valid,
                False,
            ),
            (
                "annotation",
                "name-unresolved",
                f"/{invalid_name}",
                None,
                True,
            ),
            (
                "outline",
                "array-valid",
                "[{page} 0 R /Fit]",
                None,
                False,
            ),
            ("outline", "array-invalid", "[]", None, True),
            (
                "outline",
                "name-unresolved",
                f"/{invalid_name}",
                None,
                True,
            ),
        )
        for source, name, raw, named, invalid in direct_cases:
            with self.subTest(destination=f"{source}-{name}"):
                path = self.case_root / f"{source}-{name}.pdf"
                write_pdf(path, [PdfPage()])
                _set_pdf_direct_destination(path, source, raw, named)
                report = _pdf_action_observation(path)
                reports.append(report)
                self.assertEqual(
                    ["invalid-action"] if invalid else [],
                    report["unsafe_kinds"],
                )
                self.assertEqual(
                    (
                        [
                            {
                                "kind": "invalid-action",
                                "source_category": source,
                            }
                        ]
                        if invalid
                        else []
                    ),
                    report["witnesses"],
                )

        serialized = json.dumps(reports, ensure_ascii=False)
        self.assertNotIn(valid_name, serialized)
        self.assertNotIn(invalid_name, serialized)
        self.assertNotIn(non_page_name, serialized)
        self.assertTrue(
            all(
                len(report["witnesses"])
                <= audit_publication.MAX_DIAGNOSTIC_ITEMS
                for report in reports
            )
        )

    def test_pdf_action_classification_is_fixed_bounded_and_non_cardinal(
        self,
    ) -> None:
        safe = self.case_root / "actions-safe.pdf"
        write_pdf(safe, [PdfPage()])
        _add_pdf_page_link(
            safe, {"kind": fitz.LINK_GOTO, "page": 0, "to": fitz.Point(24, 24)}
        )
        safe_report = _pdf_action_observation(safe)
        self.assertFalse(safe_report["unsafe_detected"])
        self.assertEqual([], safe_report["witnesses"])

        named_destination = self.case_root / "actions-direct-destination.pdf"
        write_pdf(named_destination, [PdfPage()])
        _add_pdf_named_destination(named_destination)
        self.assertFalse(
            _pdf_action_observation(named_destination)["unsafe_detected"]
        )

        self.assert_local_pdf_destination_contract()

        action_cases = (
            ("goto-remote", "/GoToR"),
            ("import-data", "/ImportData"),
            ("javascript", "/JavaScript"),
            ("launch", "/Launch"),
            ("named", "/Named"),
            ("submit-form", "/SubmitForm"),
            ("uri", "/URI"),
        )
        for expected_kind, subtype in action_cases:
            with self.subTest(action=expected_kind):
                path = self.case_root / f"actions-{expected_kind}.pdf"
                raw_target = f"https://example.invalid/{expected_kind}/" + (
                    "target" * 100
                )
                write_pdf(path, [PdfPage()])
                _add_pdf_action(path, subtype, raw_target)
                report = _pdf_action_observation(path)
                self.assertEqual([expected_kind], report["unsafe_kinds"])
                self.assertEqual(
                    [
                        {
                            "kind": expected_kind,
                            "source_category": "catalog",
                        }
                    ],
                    report["witnesses"],
                )
                self.assertNotIn(
                    raw_target,
                    json.dumps(report, ensure_ascii=False),
                )

        diagnostic_reports: dict[str, dict[str, Any]] = {}
        for name, subtype in (
            ("unknown-action", "/UnexpectedSubtype"),
            ("invalid-action", None),
        ):
            path = self.case_root / f"actions-{name}.pdf"
            write_pdf(path, [PdfPage()])
            _add_pdf_action(path, subtype)
            diagnostic_reports[name] = _pdf_action_observation(path)
        self.assertEqual(
            ["unknown-action"],
            diagnostic_reports["unknown-action"]["unsafe_kinds"],
        )
        self.assertEqual(
            "catalog",
            diagnostic_reports["unknown-action"]["witnesses"][0][
                "source_category"
            ],
        )
        self.assertIn(
            "subtype_sha256",
            diagnostic_reports["unknown-action"]["witnesses"][0],
        )
        self.assertEqual(
            [
                {
                    "kind": "invalid-action",
                    "source_category": "catalog",
                }
            ],
            diagnostic_reports["invalid-action"]["witnesses"],
        )
        serialized = json.dumps(diagnostic_reports, ensure_ascii=False)
        self.assertNotIn("UnexpectedSubtype", serialized)
        self.assertNotIn("xref", serialized.casefold())
        self.assertNotIn("count", serialized.casefold())
        self.assertNotIn("total", serialized.casefold())
        self.assertNotIn("omitted", serialized.casefold())
        self.assertTrue(
            all(
                len(report["witnesses"])
                <= audit_publication.MAX_DIAGNOSTIC_ITEMS
                for report in diagnostic_reports.values()
            )
        )
        fixed_kinds = {
            "goto",
            "goto-remote",
            "import-data",
            "uri",
            "launch",
            "javascript",
            "named",
            "submit-form",
            "unknown-action",
            "invalid-action",
        }
        observed = {
            kind
            for report in diagnostic_reports.values()
            for kind in report["unsafe_kinds"]
        } | {kind for kind, _subtype in action_cases}
        self.assertTrue(observed <= fixed_kinds)


if __name__ == "__main__":
    unittest.main()
