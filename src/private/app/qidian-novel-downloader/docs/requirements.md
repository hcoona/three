# Qidian Novel Downloader — Software Requirements Specification

**Status:** Draft for review
**Phase:** Requirements Analysis
**Document type:** Normative requirements baseline
**Target:** Private C# CLI application under `src/private/app/qidian-novel-downloader/`
**Related informative document:** `technical-notes.md`

---

## 1. Purpose and Scope

This document defines the user-visible and externally verifiable requirements
for a private command-line tool that downloads novel content from
[qidian.com](https://www.qidian.com/) to local Markdown files.

The tool is intended for personal use. It shall only save content that is
legitimately visible to the current user session on Qidian at download time.
It shall not bypass payment, subscription, DRM, or other access controls.

The generated Markdown is intended to be a readable archival format that can
later be converted by external tools such as Calibre.

Detailed requirements that were explicitly confirmed during stakeholder review,
even when they constrain later design choices, are marked with
**[Stakeholder-confirmed detail]** so they are not mistaken for unreviewed
research carry-over.

---

## 2. Product Overview

### 2.1 Target Users

The primary user is the repository author or another trusted private operator.
No public distribution is currently planned.

### 2.2 Supported Platforms

- Windows (primary)
- Linux
- macOS

### 2.3 Operating Assumptions

- The user has network access to Qidian.
- The user may optionally have a logged-in Qidian account.
- Access to paid chapters depends on the entitlements of the logged-in account.
- The tool runs as a local CLI application and stores its own local state.

### 2.4 Key Terms

- **Book**: A Qidian novel identified by a numeric book ID.
- **Catalog**: The list of volumes and chapters for a book.
- **Visible content**: Content that the current Qidian session can legally view.
- **Preview chapter**: A chapter for which only a public excerpt is visible.
- **Validated session**: **[Stakeholder-confirmed detail]** A persisted session
  for which the tool has confirmed a logged-in state. The requirements
  baseline does not require separate validation by restarting the browser or by
  probing paid-content access before reporting success.

---

## 3. Functional Requirements

### 3.1 Common CLI Requirements

- **FR-CLI-001** The product shall provide the following subcommands:
  `download`, `login`, `cache-clear`, and `info`.
- **FR-CLI-002** **[Stakeholder-confirmed detail]** The product shall accept
  book references as either numeric book IDs or desktop-site canonical Qidian
  book-home URLs in the form `https://www.qidian.com/book/{bookId}` with an
  optional trailing slash. When such a URL is provided, the tool shall extract
  the corresponding numeric book ID before further processing. Other Qidian URL
  variants are out of scope for automatic normalization.
- **FR-CLI-003** **[Stakeholder-confirmed detail]** The product shall accept
  book references from either the configuration file or the command line.
- **FR-CLI-004** **[Stakeholder-confirmed detail]** When both configuration
  values and command-line arguments are provided for the same setting, the
  command-line value shall take precedence.
- **FR-CLI-005** **[Stakeholder-confirmed detail]** When multiple books are
  targeted in one invocation, the tool shall process all targeted books within
  the same command. The requirements baseline does not constrain whether the
  implementation uses sequential or parallel execution.
- **FR-CLI-006** **[Stakeholder-confirmed detail]** The tool shall print a
  concise end-of-command summary that reports completed, reused, skipped, and
  failed work.
- **FR-CLI-007** **[Stakeholder-confirmed detail]** The CLI shall allow the
  user to override the browser executable path for the current invocation.

### 3.2 Download Command (`download`)

#### 3.2.1 Input and Scope

- **FR-DL-001** The `download` command shall download one or more targeted
  books.
- **FR-DL-002** **[Stakeholder-confirmed detail]** For each targeted book, the
  downloader shall process the chapters that appear in the currently available
  catalog, regardless of VIP status.
- **FR-DL-003** The downloader shall save only content that is visible to the
  current user session:
    - Free chapters: save the full visible chapter text
    - Purchased or otherwise entitled VIP chapters: save the full visible chapter
      text
    - Unpurchased VIP chapters: save only the public preview text
- **FR-DL-004** **[Stakeholder-confirmed detail]** For preview-only chapters,
  the generated Markdown shall append the marker
  `……（本章内容未完，需订阅后阅读全文）` after the visible excerpt.

#### 3.2.2 Output

- **FR-DL-005** **[Stakeholder-confirmed detail]** The downloader shall
  generate one Markdown file per targeted book. If the target output file
  already exists, the downloader shall refuse to overwrite it by default and
  shall only overwrite it after explicit user confirmation or an explicit
  command-line option that allows overwrite behavior.
- **FR-DL-006** **[Stakeholder-confirmed detail]** The downloader shall use a
  default output directory unless overridden by configuration or command-line
  option. The requirements baseline does not constrain the exact default path.
- **FR-DL-007** **[Stakeholder-confirmed detail]** The default filename shall
  use the pattern `{bookId}_{bookTitle}_{author}.md` with values derived from
  the retrieved book metadata and sanitized for the local filesystem.
- **FR-DL-008** **[Stakeholder-confirmed detail]** The default filename rule
  shall apply uniformly to all targeted books. The requirements baseline does
  not support per-book output filename overrides.
- **FR-DL-009** **[Stakeholder-confirmed detail]** The generated Markdown shall
  group chapter content under the corresponding volume titles.
- **FR-DL-010** **[Stakeholder-confirmed detail]** The Markdown structure shall
  use volume titles as level-1 headings and chapter titles as level-2
  headings.
- **FR-DL-011** **[Stakeholder-confirmed detail]** Chapter paragraph text
  shall be separated by blank lines.
- **FR-DL-012** **[Stakeholder-confirmed detail]** Output files shall be
  encoded as UTF-8 text.

#### 3.2.3 Dry Run

- **FR-DL-013** The `download` command shall support a `--dry-run` mode.
- **FR-DL-014** In dry-run mode, the command shall not download chapter bodies
  and shall not write Markdown output files.
- **FR-DL-014A** **[Stakeholder-confirmed detail]** In dry-run mode, the
  command may fetch current catalog data and update catalog cache state when
  needed to evaluate chapter scope or cache reuse status.
- **FR-DL-015** In dry-run mode, the command shall report, for each chapter in
  scope, whether the chapter is cached, changed, or requires a fresh fetch
  because no reusable cache exists.
- **FR-DL-015A** **[Stakeholder-confirmed detail]** When dry-run evaluation
  requires authentication to inspect the relevant catalog scope or cache reuse
  status, `--dry-run` shall trigger the same interactive login flow as a normal
  download command.

#### 3.2.4 Authentication Interaction

- **FR-DL-016** **[Stakeholder-confirmed detail]** If `download` requires
  authentication and no reusable valid session is available, the tool shall
  trigger a manual interactive login flow in a visible browser window and
  obtain a reusable validated session before continuing. If no validated
  session can be established, the command shall fail rather than continue as if
  authentication had succeeded.
- **FR-DL-017** **[Stakeholder-confirmed detail]** After successful manual
  login and session validation, later runs shall be able to reuse the
  persisted session state.

#### 3.2.5 Cache-Aware Operation

- **FR-DL-018** The downloader shall cache book catalog data for reuse across
  runs.
- **FR-DL-019** The downloader shall cache downloaded chapter content and the
  metadata needed to decide whether a chapter must be re-fetched.
- **FR-DL-020** The cached chapter metadata shall include the chapter word count
  recorded at fetch time.
- **FR-DL-021** **[Stakeholder-confirmed detail]** On a later run, the
  downloader shall compare the catalog's reported chapter word count with the
  cached chapter word count. If they differ, the downloader shall fetch that
  chapter again.
- **FR-DL-022** **[Stakeholder-confirmed detail]** If the catalog's reported
  chapter word count matches the cached chapter word count, the downloader
  shall reuse the cached chapter content.

#### 3.2.6 Failure Handling

- **FR-DL-023** When an individual chapter fails after retries, the downloader
  shall continue processing the remaining chapters for the targeted book.
- **FR-DL-024** **[Stakeholder-confirmed detail]** Failed chapters shall be
  reported in the command summary, shall not be silently treated as
  successfully downloaded, and, when a Markdown output file is generated, shall
  be represented in that file by the fixed placeholder text
  `……（本章下载失败，未能获取正文）`.
- **FR-DL-025** When one targeted book fails in a multi-book invocation, the
  command shall continue processing the remaining books and report the failed
  book in the final summary.

### 3.3 Login Command (`login`)

- **FR-LOGIN-001** **[Stakeholder-confirmed detail]** The `login` command
  shall open a visible browser window to a Qidian page suitable for manual
  login.
- **FR-LOGIN-002** **[Stakeholder-confirmed detail]** The tool shall require
  the user to complete authentication manually rather than automating
  credential entry.
- **FR-LOGIN-003** **[Stakeholder-confirmed detail]** After successful login,
  the tool shall persist the session so that later commands can reuse it.
- **FR-LOGIN-004** **[Stakeholder-confirmed detail]** The `login` command shall
  detect login completion automatically by observing a logged-in state rather
  than requiring a separate user confirmation step.
- **FR-LOGIN-005** **[Stakeholder-confirmed detail]** Before reporting success,
  the command shall validate that a logged-in session has been established and
  persisted for later commands. If validation fails while the interactive
  session remains available, the command shall allow the user to continue the
  login flow until a valid session is established or the user closes the
  browser window.
- **FR-LOGIN-006** **[Stakeholder-confirmed detail]** When a valid session is
  established, the command shall print a confirmation message and exit.
- **FR-LOGIN-007** **[Stakeholder-confirmed detail]** If the browser window is
  closed before a valid session is established, the command shall report
  failure and exit.

### 3.4 Cache Management Command (`cache-clear`)

- **FR-CACHE-001** **[Stakeholder-confirmed detail]** `cache-clear` without a
  book argument shall remove all downloader cache data for catalog and chapter
  content, but shall not remove configuration files, log files, or persisted
  login session state.
- **FR-CACHE-002** **[Stakeholder-confirmed detail]** `cache-clear <book-id>`
  shall remove cached catalog and chapter data only for the specified book.
- **FR-CACHE-003** **[Stakeholder-confirmed detail]** `cache-clear
--catalog-only` shall remove catalog cache data while preserving cached
  chapter content. The command shall support both global catalog-only clearing
  and catalog-only clearing scoped to a specified book.
- **FR-CACHE-004** **[Stakeholder-confirmed detail]** If the requested cache
  target does not exist, the command shall succeed as a no-op and report that
  nothing was removed.
- **FR-CACHE-005** **[Stakeholder-confirmed detail]** `cache-clear` operations
  shall not clear or invalidate the persisted login session state.

### 3.5 Book Information Command (`info`)

- **FR-INFO-001** **[Stakeholder-confirmed detail]** `info <book-id>` shall
  display book metadata without writing a Markdown output file.
- **FR-INFO-002** The displayed metadata shall include:
    - book title
    - author
    - volume list with chapter counts and VIP/free status
    - total chapter count
    - **[Stakeholder-confirmed detail]** estimated word count derived from the
      catalog page when available
    - **[Stakeholder-confirmed detail]** a cache coverage summary for the book
      that includes, at minimum, cached chapters versus total chapters

---

## 4. Data, Configuration, and Local State Requirements

### 4.1 Configuration File

- **FR-CONFIG-001** **[Stakeholder-confirmed detail]** The tool shall support a
  configuration file at a tool-managed default path. The requirements baseline
  does not constrain the exact file format or filesystem location.
- **FR-CONFIG-002** **[Stakeholder-confirmed detail]** The absence of a
  configuration file shall not prevent the tool from running.
- **FR-CONFIG-003** The configuration file shall support, at minimum, these
  user-facing settings:
    - browser executable path override
    - browser profile directory override
    - output directory
    - reading speed
    - minimum and maximum request delay
    - retry count
    - catalog cache TTL
    - log level
    - default book list for batch downloads
- **FR-CONFIG-004** **[Stakeholder-confirmed detail]** The configuration format
  for the default book list shall not define per-book output filenames; the
  standard output filename rule shall apply uniformly.
- **FR-CONFIG-005** **[Stakeholder-confirmed detail]** When `download` is
  invoked without any book reference on the command line, the command shall use
  the configured default book list if one exists.
- **FR-CONFIG-006** **[Stakeholder-confirmed detail]** When `download` is
  invoked without any book reference on the command line and no configured
  default book list exists, the command shall fail input validation.
- **FR-CONFIG-007** **[Stakeholder-confirmed detail]** The `reading speed`
  setting shall be used to calculate the base delay between chapter fetches
  during sustained downloads.
- **FR-CONFIG-008** **[Stakeholder-confirmed detail]** The `minimum and
maximum request delay` settings shall define lower and upper bounds for the
  actual delay applied between chapter fetches.
- **FR-CONFIG-009** **[Stakeholder-confirmed detail]** The `retry count`
  setting shall define the number of additional attempts permitted after the
  initial attempt fails.
- **FR-CONFIG-010** **[Stakeholder-confirmed detail]** The `catalog cache
TTL` setting shall define the maximum age of cached catalog data. When the
  TTL has expired, the next access to that book's catalog shall fetch fresh
  catalog data and update the cache.
- **FR-CONFIG-011** **[Stakeholder-confirmed detail]** The `log level` setting
  shall suppress log events below the configured severity threshold.
- **FR-CONFIG-012** **[Stakeholder-confirmed detail]** A browser profile
  directory override may point to any user-accessible browser profile
  directory, including the user's normal browser profile.

### 4.2 Local State Layout

- **FR-STATE-001** **[Stakeholder-confirmed detail]** The tool shall keep its
  local state under a tool-managed default location. The requirements baseline
  does not constrain the exact filesystem path.
- **FR-STATE-002** The local state shall include separate areas for:
    - browser profile data
    - downloader cache
    - log files

---

## 5. Quality and Operational Requirements

### 5.1 Browser and Session Behavior

- **NFR-BROWSER-001** The implementation shall use a browser-capable rendering
  approach that can execute the JavaScript required by Qidian pages.
- **NFR-BROWSER-002** **[Stakeholder-confirmed detail]** Normal download
  operations shall run headlessly by default.
- **NFR-BROWSER-003** **[Stakeholder-confirmed detail]** Interactive
  authentication operations shall use a visible browser window.
- **NFR-BROWSER-004** **[Stakeholder-confirmed detail]** By default, browser
  session data used by the tool shall be isolated from the user's normal
  browser profile. This default isolation requirement does not prohibit an
  explicit user override of the browser profile directory.
- **NFR-BROWSER-005** **[Stakeholder-confirmed detail]** When no explicit
  browser executable path is provided, the implementation shall select a
  browser runtime in this fallback order: system-installed Microsoft Edge,
  system-installed Google Chrome, then Playwright-provided Chromium.

### 5.2 Request Pacing and Recovery

- **NFR-RATE-001** The downloader shall insert non-constant delays between
  chapter fetches during sustained downloads. The actual delay shall remain
  within the configured minimum and maximum request-delay bounds.
- **NFR-RATE-002** Transient chapter retrieval failures shall be retried before
  the chapter is reported as failed, up to the configured retry count.
- **NFR-RATE-003** **[Stakeholder-confirmed detail]** Empty extracted chapter
  content, when not explained by a visible preview or paywall state, shall
  follow the same general retry policy as other transient retrieval failures.

### 5.3 Progress, Logging, and Compatibility

- **NFR-OBS-001** **[Stakeholder-confirmed detail]** Long-running downloads
  shall present ongoing console progress that continues to update the current
  work item being processed. Plain-text progress output and rich progress
  displays are both acceptable.
- **NFR-OBS-002** **[Stakeholder-confirmed detail]** The command summary shall
  distinguish work downloaded during the current run from work reused from
  cache.
- **NFR-OBS-003** **[Stakeholder-confirmed detail]** The tool shall write log
  files to its tool-managed local log area. The requirements baseline does not
  constrain the internal log structure or schema.
- **NFR-OBS-004** **[Stakeholder-confirmed detail]** The supported log levels
  shall include at least Trace, Debug, Information, Warning, and Error.
- **NFR-COMPAT-001** **[Stakeholder-confirmed detail]** The application shall
  support Windows, Linux, and macOS.
- **NFR-COMPAT-002** **[Stakeholder-confirmed detail]** User-facing files and
  console output shall support Unicode text.

---

## 6. Exit Codes

- **FR-EXIT-001** Exit code `0` shall indicate complete success, including
  successful no-op operations such as clearing a non-existent cache target.
- **FR-EXIT-002** Exit code `1` shall indicate command-line usage or input
  validation failure.
- **FR-EXIT-003** **[Stakeholder-confirmed detail]** Exit code `2` shall
  indicate an operational failure, such as an unrecoverable login, network,
  browser, cache, or output error. Any `download` run that completes with one
  or more unrecoverable chapter or book failures shall return exit code `2`,
  even if partial output files were generated.

---

## 7. Out of Scope

- Payment or DRM bypass
- Support for non-Qidian websites
- GUI or web interface
- EPUB or MOBI generation inside this tool
- CI/CD pipeline design
- Public distribution or packaging

---

## 8. Informative References

The following material is informative and not part of the normative
requirements baseline:

- `technical-notes.md` — research findings, candidate implementation details,
  and design inputs carried forward from early investigation
