# ruff: noqa: D103, E501, T201
"""PoC 2: Qidian Novel Downloader — Playwright Persistent Context (real profile).

Uses `launch_persistent_context` with the system Edge + real user profile
directory.  This keeps the user's login session (cookies are DPAPI-encrypted
and cannot be copied to a different location).

Limitation:
    - Requires ALL Edge instances to be closed first (Chromium `SingletonLock`
        is at the User Data directory level, not per-profile).
    - If any Edge profile is running, the launch will crash (exit code 21).

Usage:
    # Close ALL Edge windows first, then:
    python poc_persistent_context.py
"""

from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path

from playwright.async_api import async_playwright

EDGE_EXE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
USER_DATA_DIR = str(
    Path(
        os.environ.get(
            "LOCALAPPDATA",
            r"C:\Users\<USER>\AppData\Local",
        ),
    )
    / "Microsoft"
    / "Edge"
    / "User Data",
)
PROFILE_NAME = "Profile 5"

BOOK_ID = "1045928363"
TEST_CHAPTERS = [
    ("Free #1  (第1章 龙女)", "853782267"),
    ("Free #2  (第2章 小混蛋)", "856055438"),
    ("VIP purchased (第80章)", "864173613"),
    ("VIP unpurchased (第121章)", "866955906"),
]

CHAPTER_CONTENT_JS = """
() => {
    const results = [];
    let isPartial = false;
    const bodyText = document.body.textContent;
    if ((bodyText.includes('需要订阅后才能阅读') || bodyText.includes('本章为付费章节'))
        && bodyText.includes('VIP')) {
        isPartial = true;
    }

    const contentSpans = document.querySelectorAll('span.content-text');
    if (contentSpans.length > 0) {
        for (const span of contentSpans) {
            const t = span.textContent.trim();
            if (t) results.push(t);
        }
        return {paragraphs: results, is_partial: isPartial};
    }

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
        return {paragraphs: results, is_partial: isPartial};
    }

    return {paragraphs: results, is_partial: isPartial};
}
"""


async def main() -> None:
    if not Path(EDGE_EXE).exists():
        print(f"ERROR: Edge not found at {EDGE_EXE}")
        sys.exit(1)
    if not Path(USER_DATA_DIR).exists():
        print(f"ERROR: User Data dir not found at {USER_DATA_DIR}")
        sys.exit(1)

    async with async_playwright() as pw:
        print("Launching Edge with persistent context (real profile)...")
        print(f"  User Data : {USER_DATA_DIR}")
        print(f"  Profile   : {PROFILE_NAME}")
        context = await pw.chromium.launch_persistent_context(
            USER_DATA_DIR,
            executable_path=EDGE_EXE,
            headless=False,
            viewport={"width": 1280, "height": 900},
            args=[
                "--disable-blink-features=AutomationControlled",
                f"--profile-directory={PROFILE_NAME}",
            ],
        )

        page = context.pages[0] if context.pages else await context.new_page()

        for label, chapter_id in TEST_CHAPTERS:
            url = f"https://www.qidian.com/chapter/{BOOK_ID}/{chapter_id}/"
            print(f"\n{'=' * 60}")
            print(f"  {label}")
            print(f"  {url}")
            print(f"{'=' * 60}")

            await page.goto(url, wait_until="domcontentloaded", timeout=60_000)
            await page.wait_for_timeout(3000)

            title = await page.title()
            print(f"  Page title : {title}")

            data = await page.evaluate(CHAPTER_CONTENT_JS)
            if isinstance(data, str):
                data = json.loads(data)
            paragraphs = data.get("paragraphs", [])
            is_partial = data.get("is_partial", False)

            print(f"  Paragraphs : {len(paragraphs)}")
            print(f"  Partial    : {is_partial}")
            print(f"  Chars      : {sum(len(p) for p in paragraphs)}")
            if paragraphs:
                print(f"  First      : {paragraphs[0][:70]}...")
                print(f"  Last       : {paragraphs[-1][:70]}...")
            else:
                print("  [NO CONTENT]")

            await asyncio.sleep(3)

        await context.close()

    print(f"\n{'=' * 60}")
    print("All 4 test chapters processed.")
    print(f"{'=' * 60}")


if __name__ == "__main__":
    asyncio.run(main())
