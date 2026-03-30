# Qidian Novel Downloader — PoC Lab

Early-stage proof-of-concept scripts for downloading novel content from
[qidian.com](https://www.qidian.com/) to local Markdown files, for later
processing with [Calibre](https://calibre-ebook.com/).

Target novel: [天理协议](https://www.qidian.com/book/1045928363/) (ID: `1045928363`)

## PoC Summary

Three approaches were prototyped and validated. The **headless + login fallback**
approach is recommended for the production C# implementation.

| # | Approach | Script | Verdict |
|---|----------|--------|---------|
| 1 | Playwright MCP Extension | `poc_mcp_extension.py` | ✅ Works — requires running Edge + MCP Bridge extension |
| 2 | Persistent Context (real profile) | `poc_persistent_context.py` | ⚠️ Works but requires ALL Edge closed |
| 3 | **Headless + headed login fallback** | `poc_headless_login.py` | ✅ **Recommended** — no lock conflicts, cookies persist |

### Detailed Comparison

| Criterion | MCP Extension | Persistent (real) | Headless + Login |
|-----------|--------------|-------------------|------------------|
| Other Edge profiles running | ✅ Yes | ❌ Crashes | ✅ Yes |
| Login state (cookies) | ✅ Real browser | ✅ Real profile | ✅ Dedicated profile |
| First-run login | Not needed | Not needed | Headed fallback |
| Headless scraping | ❌ Extension mode only | ❌ Headed only | ✅ Headless |
| VIP purchased content | ✅ Full | ✅ Full | ✅ Full |
| VIP unpurchased content | ⚠️ Preview only | ⚠️ Preview only | ⚠️ Preview only |
| Anti-bot bypass | ✅ Real browser | ✅ Real browser | ✅ Not detected |
| External dependencies | MCP Bridge extension | None | None |

## Qidian Website Technical Notes

### Anti-Bot Protection

All Qidian pages return an empty HTML shell with `probe.js` for plain HTTP
requests (curl/fetch/requests).  A real browser engine (Chromium) is required to
execute the JavaScript and render the page content.

Headless Chromium is **not** blocked — pages render fully (~187 KB HTML) with
correct content.

### URL Patterns

```
Catalog:  https://www.qidian.com/book/{bookId}/catalog/
Chapter:  https://www.qidian.com/chapter/{bookId}/{chapterId}/
```

### DOM Structure — Catalog Page

Volume headers are `<h3>` elements containing the pattern `·共XX章 免费/VIP`:

```
订阅本卷 命运之序·共80章 免费
世界之痛·共42章 VIP
```

Chapter links sit under the volume container as `<a href="/chapter/{bookId}/{chapterId}/">`.

### DOM Structure — Three Chapter Types

| Type | DOM | Extraction Strategy |
|------|-----|-------------------|
| Free chapter | `<p><span class="content-text">text</span><span class="review"><span class="review-count">31</span></span></p>` | Target `span.content-text` — avoids paragraph comment (段评) counts |
| VIP purchased | `<p>text</p>` inside `<main>` — no wrapper spans | Target `main p`, clone + strip `.review` elements |
| VIP unpurchased | Same as purchased, but only 4 preview paragraphs + paywall div | Same extraction; detect paywall via `body.textContent.includes('需要订阅后才能阅读')` |

### Login Detection

Qidian sets login state asynchronously via AJAX.  The DOM pattern:

- **Logged in:** `<div id="sign-in">` WITHOUT `hidden` class; `#user-name` shows real username
- **Not logged in:** `<div id="sign-in" class="... hidden">` with `#user-name` showing placeholder `用户名`

Must poll for up to ~10 seconds after `DOMContentLoaded` for the AJAX to resolve.

### Paragraph Comment (段评) Noise

Free chapters include inline comment counts that contaminate `textContent`:

```html
<p>
  <span class="content-text" data-count="31">相原正在办手续。</span>
  <span class="review"><span class="review-count">31</span></span>
</p>
```

`p.textContent` → `"相原正在办手续。31"` (polluted)
`span.content-text.textContent` → `"相原正在办手续。"` (clean)

Safety net: regex strips trailing digits after Chinese punctuation in Markdown generation.

### Chromium Profile Lock

`launch_persistent_context(user_data_dir)` acquires `SingletonLock` at the **User Data
directory level** — not per-profile.  If any Edge instance is using the same User Data dir,
the new instance crashes (exit code 21).

Workaround: use a **dedicated** profile directory (`.browser-profile/`) instead of the real
Edge User Data.  Cookies persist across runs; login is only needed once.

Copying the real profile to a new directory does NOT preserve login state because Edge
cookies are DPAPI-encrypted and bound to the original file path.

## Running the PoCs

### Prerequisites

```bash
pip install playwright mcp
npx playwright install chromium    # for PoC 2 & 3
```

For PoC 1 only: install the
[Playwright MCP Bridge](https://chromewebstore.google.com/detail/playwright-mcp-bridge)
extension in Edge.

### PoC 1: MCP Extension Mode

Requires a running Edge instance with the MCP Bridge extension.

```bash
python poc_mcp_extension.py --book-id 1045928363 -o novel_output.md
```

### PoC 2: Persistent Context (real profile)

Requires ALL Edge instances to be closed first.

```bash
python poc_persistent_context.py
```

### PoC 3: Headless + Login Fallback (recommended)

Works with other Edge profiles running.

```bash
python poc_headless_login.py
```

First run opens a headed browser for login.  Subsequent runs skip login if
cookies are still valid.

## Verified Results

| Chapter Type | Paragraphs | Chars | Complete |
|-------------|-----------|-------|----------|
| Free (第1章 龙女) | 136 | 4,045 | ✅ Full |
| Free (第2章 小混蛋) | 137 | 4,072 | ✅ Full |
| VIP purchased (第80章) | 133 | 4,297 | ✅ Full |
| VIP unpurchased (第121章) | 4 | 138 | ⚠️ Preview + truncation marker |

Full 80-chapter free download: 333,633 characters, ~20 min with 5-12s rate limiting.

## Design Recommendations for Production (C#)

1. **Browser approach:** Headless Playwright with dedicated profile directory
   + headed login fallback (PoC 3 pattern)
2. **Rate limiting:** Conservative 5-12s random delay; exponential backoff retry
3. **Caching:** File-based per-chapter JSON with content hash for change detection
4. **Content extraction:** Target `span.content-text` for free chapters;
   `main p` (clone + strip `.review`) for VIP chapters
5. **Login detection:** Poll `#sign-in` element's `hidden` class (up to 10s)
6. **Partial chapters:** Detect paywall text, append truncation marker
7. **Output format:** Markdown with H1 = volume title, H2 = chapter title
