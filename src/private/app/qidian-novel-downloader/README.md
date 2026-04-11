# Qidian Novel Downloader

This directory contains the formal C# implementation of the private Qidian downloader described in `docs/requirements.md` and informed by the lab Playwright PoCs under `src/lab/qidian-novel-downloader/`.

## Implementation choices

The implementation follows the repository's standard .NET private-app pattern and uses:

- `.NET Generic Host` for startup, DI, configuration, and logging.
- `System.CommandLine` for command parsing.
- `Microsoft.Playwright` for browser automation.
- A dedicated Playwright persistent profile directory for reusable session state.
- File-based cache and log storage under a tool-managed local state root.

The design intentionally follows the production direction validated by the lab:

- headless browser by default for `download` and `info`
- visible browser for `login`
- browser fallback order of Microsoft Edge, Google Chrome, then Playwright Chromium

## Local state layout

By default, the app stores local state under:

- Windows: `%LOCALAPPDATA%\Hcoona\QidianNovelDownloader`
- Linux/macOS: `~/.local/share/hcoona/qidian-novel-downloader`

The state root contains:

- `config.json`
- `browser-profile/`
- `cache/`
- `logs/`
- `output/`

## Configuration

The tool-managed config file is JSON so it aligns with current .NET Generic Host and configuration-provider guidance from Microsoft Learn.

Example `config.json`:

```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Information"
        }
    },
    "Qidian": {
        "browserPath": null,
        "browserProfileDir": null,
        "outputDir": null,
        "readingSpeed": 5000,
        "minimumRequestDelaySeconds": 5,
        "maximumRequestDelaySeconds": 12,
        "retryCount": 3,
        "catalogCacheTtlHours": 24,
        "defaultBooks": ["1045928363"]
    }
}
```

If `config.json` is absent, the app still runs.

## Commands

```text
download [books...] [--dry-run] [--overwrite] [--browser-path <path>] [--browser-profile-dir <dir>] [--output-dir <dir>]
login [--browser-path <path>] [--browser-profile-dir <dir>]
cache-clear [book] [--catalog-only]
info <book> [--browser-path <path>] [--browser-profile-dir <dir>]
```

Book arguments accept either:

- a numeric Qidian book id
- `https://www.qidian.com/book/{bookId}`

## Playwright browser installation

If the host machine doesn't have Microsoft Edge or Google Chrome available, install the Playwright-managed Chromium browser before first use.

From the repository root:

```powershell
dotnet build .\src\private\app\qidian-novel-downloader\QidianNovelDownloader.csproj
pwsh .\src\private\app\qidian-novel-downloader\bin\Debug\net10.0\playwright.ps1 install chromium
```

## Validation

Recommended commands:

```powershell
dotnet build .\src\private\app\qidian-novel-downloader\QidianNovelDownloader.csproj
dotnet test .\tests\private\app\qidian-novel-downloader\Hcoona.QidianNovelDownloader.Tests.csproj
```
