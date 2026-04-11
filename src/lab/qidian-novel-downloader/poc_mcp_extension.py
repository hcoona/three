# ruff: noqa: ANN001, ANN204, BLE001, C901, D101, D102, D103, D107, E501, EM102, PLR0912, PLR0915, RUF001, S311, SIM117, TRY003, TRY300, TRY301, TRY400
"""PoC 1: Qidian Novel Downloader — Playwright MCP Extension Mode.

Connects to a running Edge browser via the Playwright MCP Bridge extension
to download novel content.  This leverages the real browser session (cookies,
extensions, fingerprint) and naturally bypasses anti-bot detection.

Prerequisites:
    1. Install "Playwright MCP Bridge" extension in Edge from Chrome Web Store.
    2. npm install -g @playwright/mcp  (or use npx)
    3. pip install mcp

Usage:
    python poc_mcp_extension.py [--book-id BOOK_ID] [--output OUTPUT]
                                [--cache-dir CACHE_DIR] [--free-only]

Features:
    - Connects to running Edge via Playwright MCP Bridge extension
    - File-based caching (catalog + per-chapter JSON)
    - Conservative rate limiting (random 5-12s delays between chapters)
    - Exponential backoff retry (up to 3 retries per chapter)
    - Handles free, VIP purchased, and VIP unpurchased chapters
    - Strips paragraph comment counts (段评) from extracted text
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import logging
import random
import re
import sys
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
from mcp.types import TextContent

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("qidian-dl")

QIDIAN_BASE = "https://www.qidian.com"
DEFAULT_BOOK_ID = "1045928363"
DEFAULT_OUTPUT = "novel_output.md"
DEFAULT_CACHE_DIR = ".cache"

MIN_DELAY = 5.0
MAX_DELAY = 12.0
MAX_RETRIES = 3
RETRY_BASE_DELAY = 10.0


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------


@dataclass
class Chapter:
    title: str
    url: str
    chapter_id: str
    is_vip: bool = False


@dataclass
class Volume:
    title: str
    is_vip: bool = False
    chapters: list[Chapter] = field(default_factory=list)


@dataclass
class CatalogCache:
    book_id: str
    fetched_at: float
    volumes: list[dict]


@dataclass
class ChapterCache:
    chapter_id: str
    title: str
    content: list[str]
    fetched_at: float
    content_hash: str


# ---------------------------------------------------------------------------
# Cache manager
# ---------------------------------------------------------------------------


class CacheManager:
    """File-based cache for catalog and chapter content."""

    def __init__(self, cache_dir: Path, book_id: str):
        self.cache_dir = cache_dir / book_id
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.catalog_file = self.cache_dir / "catalog.json"
        self.chapters_dir = self.cache_dir / "chapters"
        self.chapters_dir.mkdir(exist_ok=True)

    def get_catalog(self, max_age_hours: float = 24.0) -> CatalogCache | None:
        if not self.catalog_file.exists():
            return None
        try:
            data = json.loads(self.catalog_file.read_text(encoding="utf-8"))
            age_hours = (time.time() - data["fetched_at"]) / 3600
            if age_hours > max_age_hours:
                log.info("Catalog cache expired (%.1f hours old)", age_hours)
                return None
            log.info("Using cached catalog (%.1f hours old)", age_hours)
            return CatalogCache(**data)
        except (json.JSONDecodeError, KeyError, TypeError) as e:
            log.warning("Corrupt catalog cache: %s", e)
            return None

    def save_catalog(self, book_id: str, volumes: list[Volume]) -> None:
        data = CatalogCache(
            book_id=book_id,
            fetched_at=time.time(),
            volumes=[
                {
                    "title": v.title,
                    "is_vip": v.is_vip,
                    "chapters": [asdict(c) for c in v.chapters],
                }
                for v in volumes
            ],
        )
        self.catalog_file.write_text(
            json.dumps(asdict(data), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        log.info("Catalog cached to %s", self.catalog_file)

    def get_chapter(self, chapter_id: str) -> ChapterCache | None:
        path = self.chapters_dir / f"{chapter_id}.json"
        if not path.exists():
            return None
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            return ChapterCache(**data)
        except (json.JSONDecodeError, KeyError, TypeError):
            return None

    def save_chapter(
        self, chapter_id: str, title: str, paragraphs: list[str]
    ) -> None:
        content_hash = hashlib.sha256(
            "\n".join(paragraphs).encode("utf-8")
        ).hexdigest()
        data = ChapterCache(
            chapter_id=chapter_id,
            title=title,
            content=paragraphs,
            fetched_at=time.time(),
            content_hash=content_hash,
        )
        path = self.chapters_dir / f"{chapter_id}.json"
        path.write_text(
            json.dumps(asdict(data), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )


# ---------------------------------------------------------------------------
# MCP browser helpers
# ---------------------------------------------------------------------------


async def mcp_navigate(session: ClientSession, url: str) -> str:
    result = await session.call_tool("browser_navigate", {"url": url})
    return _extract_text(result)


async def mcp_evaluate(session: ClientSession, js: str) -> str:
    result = await session.call_tool("browser_evaluate", {"function": js})
    return _extract_text(result)


async def mcp_wait(session: ClientSession, seconds: float) -> None:
    await session.call_tool("browser_wait_for", {"time": seconds})


def _extract_text(result) -> str:
    for block in result.content:
        if isinstance(block, TextContent):
            return block.text
    return ""


def _extract_evaluate_value(raw: str) -> str:
    """Extract the actual return value from browser_evaluate's Markdown output.

    The MCP tool returns text like:
        ### Result
        "<escaped JSON>"
        ### Ran Playwright code
        ...
    """
    m = re.search(r"### Result\s*\n(.+?)(?:\n### |$)", raw, re.DOTALL)
    if not m:
        return raw
    value_str = m.group(1).strip()
    try:
        return json.loads(value_str)
    except (json.JSONDecodeError, TypeError):
        return value_str


# ---------------------------------------------------------------------------
# Page-level JavaScript
# ---------------------------------------------------------------------------

# Catalog extraction: returns JSON array of volumes with chapters.
CATALOG_JS = """
() => {
    const volumes = [];
    const allH3 = document.querySelectorAll('h3');

    for (const h3 of allH3) {
        const text = h3.textContent.trim();
        if (!text.includes('·共') || !text.includes('章')) continue;

        const isVip = text.includes('VIP');
        const isFree = text.includes('免费');
        const titleMatch = text.match(/^(?:订阅本卷\\s*)?(.+?)·/);
        const title = titleMatch ? titleMatch[1].trim() : text;

        const vol = { title, is_vip: isVip && !isFree, chapters: [] };
        volumes.push(vol);

        const container = h3.closest('[class*="volume"]') || h3.parentElement;
        if (!container) continue;

        const links = container.querySelectorAll('a[href*="/chapter/"]');
        for (const link of links) {
            const href = link.getAttribute('href');
            const chTitle = link.textContent.trim();
            if (!chTitle || !href) continue;

            const m = href.match(/\\/chapter\\/\\d+\\/(\\d+)/);
            const chId = m ? m[1] : '';
            vol.chapters.push({
                title: chTitle,
                url: href.startsWith('//') ? 'https:' + href : href,
                chapter_id: chId,
                is_vip: isVip && !isFree
            });
        }
    }
    return JSON.stringify(volumes);
}
"""

# Chapter content extraction: returns {paragraphs: string[], is_partial: bool}.
CHAPTER_CONTENT_JS = """
() => {
    const results = [];
    let isPartial = false;

    const bodyText = document.body.textContent;
    if ((bodyText.includes('需要订阅后才能阅读') || bodyText.includes('本章为付费章节'))
        && bodyText.includes('VIP')) {
        isPartial = true;
    }

    // Free chapters: target span.content-text directly (avoids 段评 noise)
    const contentSpans = document.querySelectorAll('span.content-text');
    if (contentSpans.length > 0) {
        for (const span of contentSpans) {
            const t = span.textContent.trim();
            if (t) results.push(t);
        }
        return JSON.stringify({paragraphs: results, is_partial: isPartial});
    }

    // VIP chapters: plain <p> inside <main>
    const mainPs = document.querySelectorAll('main p');
    if (mainPs.length > 0) {
        for (const p of mainPs) {
            const clone = p.cloneNode(true);
            clone
                .querySelectorAll('.review, .review-count, .review-icon')
                .forEach(el => el.remove());
            const t = clone.textContent.trim();
            if (t && !/^\\d+$/.test(t)) results.push(t);
        }
        return JSON.stringify({paragraphs: results, is_partial: isPartial});
    }

    // Extended fallback selectors
    const selectors = [
        '.read-content p', '.chapter-content p',
        '[class*="content"] p', '.text-wrap p', '#j_chapterContent p',
    ];
    for (const sel of selectors) {
        const ps = document.querySelectorAll(sel);
        if (ps.length > 0) {
            for (const p of ps) {
                const clone = p.cloneNode(true);
                clone
                    .querySelectorAll('.review, .review-count, .review-icon')
                    .forEach(el => el.remove());
                const t = clone.textContent.trim();
                if (t && !/^\\d+$/.test(t)) results.push(t);
            }
            return JSON.stringify({paragraphs: results, is_partial: isPartial});
        }
    }

    return JSON.stringify({paragraphs: results, is_partial: isPartial});
}
"""

CHAPTER_VIP_CHECK_JS = """
() => {
    const body = document.body.textContent;
    return String(
        body.includes('需要订阅后才能阅读')
        || body.includes('本章为付费章节')
    );
}
"""


# ---------------------------------------------------------------------------
# Core logic
# ---------------------------------------------------------------------------


async def fetch_catalog(session: ClientSession, book_id: str) -> list[Volume]:
    catalog_url = f"{QIDIAN_BASE}/book/{book_id}/catalog/"
    log.info("Fetching catalog: %s", catalog_url)

    await mcp_navigate(session, catalog_url)
    await mcp_wait(session, 3)

    raw = await mcp_evaluate(session, CATALOG_JS)
    value = _extract_evaluate_value(raw)

    try:
        volumes_data = json.loads(value) if isinstance(value, str) else value
    except (json.JSONDecodeError, TypeError):
        log.error("Failed to parse catalog JSON. Raw:\n%s", raw[:500])
        return []

    volumes: list[Volume] = []
    for vd in volumes_data:
        vol = Volume(
            title=vd["title"],
            is_vip=vd.get("is_vip", False),
            chapters=[
                Chapter(
                    title=c["title"],
                    url=c["url"],
                    chapter_id=c["chapter_id"],
                    is_vip=c.get("is_vip", False),
                )
                for c in vd.get("chapters", [])
            ],
        )
        volumes.append(vol)

    log.info(
        "Found %d volumes, %d total chapters",
        len(volumes),
        sum(len(v.chapters) for v in volumes),
    )
    for v in volumes:
        log.info(
            "  %s: %d chapters (%s)",
            v.title,
            len(v.chapters),
            "VIP" if v.is_vip else "Free",
        )
    return volumes


async def rate_limit_delay() -> None:
    delay = random.uniform(MIN_DELAY, MAX_DELAY)
    log.debug("Rate limit delay: %.1fs", delay)
    await asyncio.sleep(delay)


async def fetch_chapter_content(
    session: ClientSession, chapter: Chapter, attempt: int = 0
) -> list[str]:
    """Navigate to a chapter page and extract paragraph text."""
    url = chapter.url
    if not url.startswith("http"):
        url = QIDIAN_BASE + url

    log.info(
        "Fetching chapter: %s (%s) [attempt %d]",
        chapter.title,
        chapter.chapter_id,
        attempt + 1,
    )

    try:
        await mcp_navigate(session, url)
        await mcp_wait(session, 3)

        raw = await mcp_evaluate(session, CHAPTER_CONTENT_JS)
        value = _extract_evaluate_value(raw)

        try:
            data = json.loads(value) if isinstance(value, str) else value
        except (json.JSONDecodeError, TypeError):
            data = {}

        if isinstance(data, list):
            paragraphs = data
            is_partial = False
        else:
            paragraphs = data.get("paragraphs", [])
            is_partial = data.get("is_partial", False)

        if not paragraphs:
            vip_raw = await mcp_evaluate(session, CHAPTER_VIP_CHECK_JS)
            vip_val = _extract_evaluate_value(vip_raw)
            if "true" in str(vip_val).lower():
                log.warning(
                    "Chapter %s is VIP-locked with no preview, skipping",
                    chapter.title,
                )
                return []
            raise RuntimeError(f"No content found for {chapter.title}")

        if is_partial:
            paragraphs.append("……（本章内容未完，需订阅后阅读全文）")
            log.info(
                "  Got %d preview paragraphs (%d chars) [PARTIAL]",
                len(paragraphs) - 1,
                sum(len(p) for p in paragraphs),
            )
        else:
            log.info(
                "  Got %d paragraphs (%d chars)",
                len(paragraphs),
                sum(len(p) for p in paragraphs),
            )
        return paragraphs

    except Exception as e:
        if attempt < MAX_RETRIES - 1:
            delay = RETRY_BASE_DELAY * (2**attempt)
            log.warning(
                "Failed to fetch %s: %s. Retrying in %.0fs...",
                chapter.title,
                e,
                delay,
            )
            await asyncio.sleep(delay)
            return await fetch_chapter_content(session, chapter, attempt + 1)
        log.error(
            "Failed to fetch %s after %d attempts: %s",
            chapter.title,
            MAX_RETRIES,
            e,
        )
        return []


# ---------------------------------------------------------------------------
# Markdown generation
# ---------------------------------------------------------------------------


def generate_markdown(
    volumes: list[Volume], chapter_contents: dict[str, list[str]]
) -> str:
    lines: list[str] = []
    number_only = re.compile(r"^\d+$")
    trailing_count = re.compile(r"(?<=[。！？」…\u3000])(\d+)$")

    for vol in volumes:
        has_content = any(
            ch.chapter_id in chapter_contents for ch in vol.chapters
        )
        if not has_content:
            continue
        lines.append(f"# {vol.title}")
        lines.append("")
        for ch in vol.chapters:
            content = chapter_contents.get(ch.chapter_id)
            if content is None:
                continue
            lines.append(f"## {ch.title}")
            lines.append("")
            for para in content:
                if number_only.match(para):
                    continue
                cleaned = trailing_count.sub("", para)
                lines.append(cleaned)
                lines.append("")

    return "\n".join(lines)


def load_catalog_from_cache(cache: CacheManager) -> list[Volume] | None:
    cached = cache.get_catalog()
    if cached is None:
        return None
    volumes: list[Volume] = []
    for vd in cached.volumes:
        vol = Volume(
            title=vd["title"],
            is_vip=vd.get("is_vip", False),
            chapters=[
                Chapter(
                    title=c["title"],
                    url=c["url"],
                    chapter_id=c["chapter_id"],
                    is_vip=c.get("is_vip", False),
                )
                for c in vd.get("chapters", [])
            ],
        )
        volumes.append(vol)
    return volumes


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


async def main() -> None:
    parser = argparse.ArgumentParser(
        description="Qidian Novel Downloader PoC (MCP Extension)"
    )
    parser.add_argument("--book-id", default=DEFAULT_BOOK_ID)
    parser.add_argument("--output", "-o", default=DEFAULT_OUTPUT)
    parser.add_argument("--cache-dir", default=DEFAULT_CACHE_DIR)
    parser.add_argument("--force-refresh", action="store_true")
    parser.add_argument("--free-only", action="store_true", default=True)
    parser.add_argument("--verbose", "-v", action="store_true")
    args = parser.parse_args()

    if args.verbose:
        log.setLevel(logging.DEBUG)

    cache = CacheManager(Path(args.cache_dir), args.book_id)

    volumes: list[Volume] | None = None
    if not args.force_refresh:
        volumes = load_catalog_from_cache(cache)

    server_params = StdioServerParameters(
        command="npx",
        args=["@playwright/mcp@latest", "--extension", "--browser", "msedge"],
    )

    log.info("Connecting to Edge via Playwright MCP extension bridge...")

    async with stdio_client(server_params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = await session.list_tools()
            log.info("MCP connected. %d tools available.", len(tools.tools))

            if volumes is None:
                volumes = await fetch_catalog(session, args.book_id)
                if not volumes:
                    log.error("Failed to extract catalog. Exiting.")
                    sys.exit(1)
                cache.save_catalog(args.book_id, volumes)

            tasks: list[tuple[Volume, Chapter]] = []
            cached_count = 0
            skipped_vip = 0
            for vol in volumes:
                for ch in vol.chapters:
                    if args.free_only and (vol.is_vip or ch.is_vip):
                        skipped_vip += 1
                        continue
                    if cache.get_chapter(ch.chapter_id) is not None:
                        cached_count += 1
                        continue
                    tasks.append((vol, ch))

            log.info(
                "Download plan: %d to download, %d cached, %d VIP skipped",
                len(tasks),
                cached_count,
                skipped_vip,
            )

            for i, (vol, ch) in enumerate(tasks):
                log.info(
                    "[%d/%d] Downloading: %s > %s",
                    i + 1,
                    len(tasks),
                    vol.title,
                    ch.title,
                )
                paragraphs = await fetch_chapter_content(session, ch)
                if paragraphs:
                    cache.save_chapter(ch.chapter_id, ch.title, paragraphs)
                else:
                    log.warning("  No content for %s", ch.title)
                if i < len(tasks) - 1:
                    await rate_limit_delay()

    chapter_contents: dict[str, list[str]] = {}
    for vol in volumes:
        for ch in vol.chapters:
            cached_ch = cache.get_chapter(ch.chapter_id)
            if cached_ch is not None:
                chapter_contents[ch.chapter_id] = cached_ch.content

    markdown = generate_markdown(volumes, chapter_contents)
    output_path = Path(args.output)
    output_path.write_text(markdown, encoding="utf-8")
    log.info(
        "Written %d characters to %s (%d chapters)",
        len(markdown),
        output_path,
        len(chapter_contents),
    )


if __name__ == "__main__":
    asyncio.run(main())
