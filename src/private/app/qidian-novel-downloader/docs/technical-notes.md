# Qidian Novel Downloader — Technical Notes

**Status:** Working notes
**Phase:** Requirements Analysis input
**Document type:** Informative only (non-normative)
**Related normative document:** `requirements.md`

---

## 1. Purpose

This document preserves technical observations, PoC findings, and candidate
implementation ideas gathered during early investigation. These notes are
useful inputs for later architecture and design work, but they are not binding
requirements.

---

## 2. Classification of Inputs

### 2.1 Historical Inputs Considered During Requirements Analysis

The following items were discussed during early investigation and requirements
analysis. They are preserved here for historical context, but `requirements.md`
and the current implementation remain the authoritative sources for the present
project requirements and technology choices:

- `download --dry-run` was identified early as an in-scope feature candidate
- `login`, `cache-clear`, and `info` were identified early as CLI command
  candidates
- configuration file format and user-facing settings were explored during early
  investigation; see `requirements.md` and the implementation for the current
  authoritative JSON format
- exit code classification and supported-platform scope were early requirement
  candidates
- support for canonical Qidian book URLs was identified early as an input
  requirement candidate
- `info` including VIP/free status was an early output requirement candidate
- `download` processing catalog chapters regardless of VIP status, while still
  saving only content legitimately visible to the current session, was an early
  behavioral requirement candidate
- chapter failures being skipped and reported rather than silently counted as
  success was an early error-handling requirement candidate
- empty content following the general retry policy was an early retry-policy
  candidate
- catalog word count versus cached word count as the retained chapter
  change-detection rule was an early design candidate
- normal downloads defaulting to headless operation with interactive headed
  login when needed was an early UX/runtime candidate
- browser runtime selection falling back in the order system-installed
  Microsoft Edge, system-installed Google Chrome, then Playwright-provided
  Chromium was an early runtime candidate
- users explicitly overriding the browser executable path was an early
  configurability candidate

### 2.2 Candidate Runtime and Packaging

- C# CLI application under `src/private/app/qidian-novel-downloader/`
- Candidate target framework: `net10.0` (subject to design-phase confirmation)
- Candidate publish style: single-file publish per supported RID
- Early publish examples included:
  - `dotnet publish -r win-x64 -c Release`
  - `dotnet publish -r linux-x64 -c Release`
  - `dotnet publish -r osx-arm64 -c Release`
- Repository package management is expected to follow the monorepo's central
  package management conventions, historically via
  `Directory.Packages.props`

### 2.3 Candidate Dependencies

| Package | Candidate purpose |
|---------|-------------------|
| Microsoft.Playwright | Browser automation |
| System.CommandLine | CLI argument parsing |
| YamlDotNet | YAML configuration |
| Spectre.Console | Progress bars and rich console output |
| Microsoft.Extensions.Logging | Structured logging |

### 2.4 Candidate Project Structure

```text
src/private/app/qidian-novel-downloader/
├── QidianNovelDownloader.csproj
├── Program.cs
├── Commands/
│   ├── DownloadCommand.cs
│   ├── LoginCommand.cs
│   ├── CacheClearCommand.cs
│   └── InfoCommand.cs
├── Browser/
│   ├── BrowserManager.cs
│   ├── LoginDetector.cs
│   └── PageEvaluator.cs
├── Extraction/
│   ├── CatalogExtractor.cs
│   └── ChapterExtractor.cs
├── Cache/
│   ├── CacheManager.cs
│   ├── CatalogCache.cs
│   └── ChapterCache.cs
├── RateLimiting/
│   └── AdaptiveThrottler.cs
├── Models/
│   ├── Book.cs
│   ├── Volume.cs
│   ├── Chapter.cs
│   └── AppConfig.cs
├── Output/
│   └── MarkdownGenerator.cs
└── docs/
```

### 2.5 Candidate Test Focus

- Current test framework in this repository: xUnit
- Candidate test project location:
  `tests/private/app/qidian-novel-downloader/`
- Markdown generation from parsed book data
- Cache management and invalidation behavior
- Adaptive throttling calculations
- Configuration parsing and CLI/config merging

---

## 3. Website Behavior Observed During Research

### 3.1 JavaScript Rendering Requirement

Plain HTTP clients received an empty HTML shell plus JavaScript bootstrap
resources, including `probe.js`. A full browser engine appears necessary to
render the actual page content. In one observed scenario, the fully rendered
page source was approximately 187 KB of HTML.

### 3.2 Headless Browser Observation

Research indicated that headless Chromium was still able to render the target
pages in the observed scenarios. This observation should be revalidated during
design and test.

### 3.3 Browser Profile Locking

Chromium-family browsers use a user-data-directory-level singleton lock. The
lock file observed during research was `SingletonLock`. Reusing a live personal
profile may conflict with an already running browser instance.

### 3.4 Cookie Portability Limitation

On Windows, copied browser profiles may not preserve usable cookies because the
cookies are DPAPI-encrypted and tied to the original environment and storage
location. Copying profile files to a new path can break cookie decryption.

### 3.5 Login State Timing

Login state on Qidian appears to resolve asynchronously after page load rather
than being immediately reliable at `DOMContentLoaded`. During research, the
`#sign-in` element was observed to start with the `hidden` class and then
change state after an AJAX call, usually within about 1 to 5 seconds. A
polling window of up to 10 seconds was therefore treated as a reasonable
implementation input.

---

## 4. PoC Findings and Candidate Implementation Notes

### 4.1 Observed PoC Approaches

The lab folder currently contains three relevant PoC directions:

1. **Playwright MCP extension bridge**
   - Works with a running Edge instance plus the bridge extension
   - Uses the real browser session and extensions
   - Demonstrated catalog extraction, chapter download, caching, pacing, retry,
     and Markdown generation in Python
2. **Persistent context against a real Edge user-data directory**
   - Works only when all Edge instances are closed first
   - Conflicts with Chromium's `SingletonLock` behavior
3. **Headless download with headed login fallback**
   - Uses a dedicated browser profile directory
   - Avoids lock conflicts with the user's live browser session
   - Persists cookies across runs
   - Was marked as the recommended production direction in the lab README

### 4.2 Browser Selection and Overrides

The browser fallback order retained in `requirements.md` is:

1. System-installed Microsoft Edge
2. System-installed Google Chrome
3. Playwright-provided Chromium

An explicit browser executable override is also retained as a user-facing
requirement. Early drafting explored a `--browser-path` CLI argument paired
with a `browser_path` configuration key. A dedicated browser profile override
was likewise considered as a candidate configuration input.

### 4.3 Browser Mode Strategy

- Default to headless for downloads
- Switch to headed mode for explicit login
- Allow automatic headed fallback when authentication is required

### 4.4 Profile Strategy

- Use a tool-managed dedicated browser profile directory
- Avoid reusing the user's normal browser profile
- Persist cookies and local session state across runs

### 4.5 Anti-Detection Measures

- Prefer a real system browser when available
- Disable the `AutomationControlled` Blink feature when the chosen runtime
  makes that possible

### 4.6 Output and Partial-Content Notes

Observed PoC behavior and retained drafting notes included:

- Markdown output grouped by volume, then chapter
- Preview-only chapters append the marker
  `……（本章内容未完，需订阅后阅读全文）`
- Free-chapter extraction and VIP-chapter extraction use different DOM cues

---

## 5. Request Pacing and Failure Handling Notes

### 5.1 Observed PoC Pacing and Retry Behavior

The current lab PoCs do not share a single pacing formula:

- `poc_mcp_extension.py` used a random delay between `5s` and `12s`
- `poc_mcp_extension.py` retried chapter fetch failures up to `3` times with
  an exponential wait based on a `10s` retry base delay
- `poc_headless_login.py` used a smaller demo delay window of roughly `3s` to
  `6s` between sampled chapters

These values reflect PoC validation choices rather than a locked product
requirement.

### 5.2 Candidate Request Pacing Model

Proposed formula from early research:

```text
base_delay = word_count / reading_speed
actual_delay = base_delay * jitter_factor * backoff_multiplier
```

Candidate defaults considered during research:

- `reading_speed = 5000` characters per minute
- roughly 48 seconds for a typical 4000-character chapter
- `jitter_factor` in `[0.7, 1.3]`
- `backoff_multiplier` starts at `1.0`
- backoff cap at `16`
- extra retry penalty delay of `30s * backoff_multiplier`
- minimum delay `3s`
- maximum delay `300s`
- `recovery_window = 5` successful requests

### 5.3 Failure Signals Explored

Signals explored as anti-bot or throttling indicators included:

- HTTP 403 responses
- captcha pages
- empty content pages
- CloudFlare challenges

These values should be treated as design inputs, not fixed requirements unless
they are explicitly adopted later.

---

## 6. Cache and Extraction Notes

### 6.1 Candidate Cache Layout

```text
~/.qidian-downloader/
├── browser-profile/
├── cache/
│   └── {book_id}/
│       ├── catalog.json
│       └── chapters/
│           ├── {chapter_id}.json
│           └── ...
├── config.yaml
└── logs/
    └── qidian-dl-{date}.log
```

During early investigation, the chapter cache payload was expected to include
the extracted paragraphs, the fetched word count, and a fetch timestamp.

### 6.2 Cache Behavior Notes

`poc_mcp_extension.py` currently demonstrates:

- per-book catalog cache files
- per-chapter content cache files
- cache reuse across runs
- a `24` hour catalog freshness check

The specific change-detection rule retained in `requirements.md` compares the
catalog-reported chapter word count against the cached chapter word count.
That rule was not fully validated by the lab implementation, but it was kept as
a confirmed product rule during prior requirements review.

### 6.3 Candidate Catalog Extraction Cues

- Catalog URL pattern:
  `https://www.qidian.com/book/{bookId}/catalog/`
- Volume headings observed in `<h3>` elements with text patterns such as
  `·共XX章 免费` and `·共XX章 VIP`
- Chapter links observed under `/chapter/{bookId}/{chapterId}/`
- Per-chapter word count appeared in link tooltip text such as
  `章节字数：XXXX`

### 6.4 Candidate Chapter Extraction Cues

Observed DOM variants from early investigation:

| Scenario | Candidate selector | Note |
|---------|--------------------|------|
| Free chapter | `span.content-text` | Avoids paragraph-comment noise |
| Purchased VIP chapter | `main p` | Plain paragraph extraction |
| Unpurchased VIP chapter | `main p` + paywall detection | Save preview only |

Explored paywall checks included:

- `body.textContent.includes('需要订阅后才能阅读')`
- `body.textContent.includes('本章为付费章节')`

### 6.5 Paragraph Comment Cleanup

For free chapters, inline paragraph-comment counts appeared near content text.
Targeting the content span directly seemed more reliable than extracting the
entire paragraph node. A regex-based cleanup fallback was also explored, using
trailing-digit cleanup after Chinese punctuation such as `[。！？」…]`.

One observed DOM example was:

```html
<p>
  <span class="content-text" data-count="31">text</span>
  <span class="review"><span class="review-count">31</span></span>
</p>
```

### 6.6 Login Detection Cues

An explored login-detection strategy was:

- inspect the `#sign-in` element
- if the element exists and does not have the `hidden` class, treat the session
  as logged in
- if the element has the `hidden` class or is absent, treat the session as not
  yet confirmed
- continue polling briefly because the page may update login state

---

## 7. Historical Notes

- The automatic login fallback direction was intended to match the earlier
  "PoC 3" behavior explored during investigation.

---

## 8. Traceability Note

When design work starts, the chosen technical solution should trace back to the
normative requirements in `requirements.md`. If a research note becomes a
binding decision, it should be copied into the appropriate design or
architecture document rather than treated as a requirement automatically.
