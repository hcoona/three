#!/usr/bin/env python3
# ruff: noqa: ANN001, BLE001, D103, E501, S110, S311, SIM105, T201
"""PoC 3: Qidian Novel Downloader — Headless-first with headed login fallback.

Uses a dedicated browser profile directory (.browser-profile/) so there are
no lock conflicts with running Edge instances.  Cookies persist across runs,
so manual login is only required once.

Flow:
    1. Launch headless → navigate to Qidian → check login status.
    2. If not logged in, close headless → open headed → let user log in.
    3. Launch headless again → scrape test chapters.

This is the RECOMMENDED approach for the production C# implementation.

Usage:
    python poc_headless_login.py
"""

from __future__ import annotations

import asyncio
import json
import random
import sys
from pathlib import Path

from playwright.async_api import BrowserContext, Page, async_playwright

EDGE_EXE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
BROWSER_PROFILE_DIR = str(Path(__file__).parent / ".browser-profile")

BOOK_ID = "1045928363"
TEST_CHAPTERS = [
    ("Free #1  (第1章 龙女)", "853782267"),
    ("Free #2  (第2章 小混蛋)", "856055438"),
    ("VIP purchased (第80章)", "864173613"),
    ("VIP unpurchased (第121章)", "866955906"),
]

# ── JS: detect login status ─────────────────────────────────────────────
# Qidian uses `#sign-in` with a `hidden` class when not logged in.
# The login state is set asynchronously via AJAX after page load.
CHECK_LOGIN_JS = """
() => {
    const bodyLen = (document.body ? document.body.innerHTML : '').length;
    const title = document.title;
    const signInEl = document.getElementById('sign-in');
    const signInHidden = signInEl ? signInEl.classList.contains('hidden') : true;
    const signOutEl = document.querySelector('.sign-out');
    const logged_in = !!signInEl && !signInHidden;
    const userName = document.getElementById('user-name');
    const userText = userName ? userName.textContent.trim() : null;
    return {
        logged_in,
        sign_in_hidden: signInHidden,
        has_sign_out: !!signOutEl,
        user_name: userText,
        body_len: bodyLen,
        title,
    };
}
"""

# ── JS: extract chapter content ─────────────────────────────────────────
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

LAUNCH_ARGS = [
    "--disable-blink-features=AutomationControlled",
    "--disable-features=AutomationControlled",
]


async def launch_context(pw, *, headless: bool) -> BrowserContext:
    """Launch a persistent Edge context (headless or headed)."""
    return await pw.chromium.launch_persistent_context(
        BROWSER_PROFILE_DIR,
        executable_path=EDGE_EXE,
        headless=headless,
        viewport={"width": 1280, "height": 900},
        args=LAUNCH_ARGS,
    )


async def check_login(page: Page) -> bool:
    """Navigate to Qidian book page and return True if logged in.

    Polls for up to 10 seconds because Qidian sets login state asynchronously.
    """
    url = f"https://www.qidian.com/book/{BOOK_ID}/"
    await page.goto(url, wait_until="domcontentloaded", timeout=60_000)
    result = None
    for _ in range(10):
        await page.wait_for_timeout(1000)
        result = await page.evaluate(CHECK_LOGIN_JS)
        if result.get("logged_in"):
            print(f"  Login check: {result}")
            return True
    print(f"  Login check: {result}")
    return False


async def interactive_login(pw) -> bool:
    """Open headed browser for user to log in manually.

    After the user presses Enter (or closes the window), verifies login
    via a fresh headless context using the persisted cookies.
    """
    print("\n┌─────────────────────────────────────────────────────┐")
    print("│  Opening browser — please log in to Qidian.        │")
    print("│  After login, come back here and press Enter.       │")
    print("│  (You may also close the browser window first.)     │")
    print("└─────────────────────────────────────────────────────┘\n")

    ctx = await launch_context(pw, headless=False)
    page = ctx.pages[0] if ctx.pages else await ctx.new_page()
    await page.goto(
        "https://www.qidian.com/", wait_until="domcontentloaded", timeout=60_000
    )

    await asyncio.get_event_loop().run_in_executor(
        None, input, "Press Enter after logging in... "
    )

    try:
        await ctx.close()
    except Exception:
        pass

    print("  Verifying login with headless browser...")
    ctx2 = await launch_context(pw, headless=True)
    page2 = ctx2.pages[0] if ctx2.pages else await ctx2.new_page()
    logged_in = await check_login(page2)
    await ctx2.close()
    return logged_in


async def scrape_chapters(pw) -> None:
    """Launch headless and scrape all test chapters."""
    print("\nLaunching headless browser for scraping...")
    ctx = await launch_context(pw, headless=True)
    page = ctx.pages[0] if ctx.pages else await ctx.new_page()

    for label, chapter_id in TEST_CHAPTERS:
        url = f"https://www.qidian.com/chapter/{BOOK_ID}/{chapter_id}/"
        print(f"\n{'=' * 60}")
        print(f"  {label}")
        print(f"  {url}")
        print(f"{'=' * 60}")

        await page.goto(url, wait_until="networkidle", timeout=60_000)
        await page.wait_for_timeout(2000)

        title = await page.title()
        print(f"  Page title : {title}")

        data = None
        for attempt in range(3):
            try:
                data = await page.evaluate(CHAPTER_CONTENT_JS)
                break
            except Exception as e:
                print(f"  Evaluate attempt {attempt + 1} failed: {e}")
                await page.wait_for_timeout(2000)
        if data is None:
            print("  [EVALUATE FAILED]")
            continue
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

        await asyncio.sleep(random.uniform(3, 6))

    await ctx.close()
    print(f"\n{'=' * 60}")
    print("Done — all 4 test chapters processed.")
    print(f"{'=' * 60}")


async def main() -> None:
    if not Path(EDGE_EXE).exists():
        print(f"ERROR: Edge not found at {EDGE_EXE}")
        sys.exit(1)

    Path(BROWSER_PROFILE_DIR).mkdir(parents=True, exist_ok=True)

    async with async_playwright() as pw:
        # Step 1: headless login check
        print("Step 1: Checking login status (headless)...")
        ctx = await launch_context(pw, headless=True)
        page = ctx.pages[0] if ctx.pages else await ctx.new_page()
        logged_in = await check_login(page)
        await ctx.close()

        # Step 2: headed login if needed
        if not logged_in:
            print("Not logged in — switching to headed browser for login.")
            logged_in = await interactive_login(pw)
            if not logged_in:
                print("ERROR: Login was not detected. Aborting.")
                sys.exit(1)
            print("Login confirmed!")
        else:
            print("Already logged in (cookies valid).")

        # Step 3: headless scraping
        await scrape_chapters(pw)


if __name__ == "__main__":
    asyncio.run(main())
