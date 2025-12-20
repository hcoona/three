# Changelog

All notable changes to **steam-account-history-to-csv** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- WXT-based build/dev/packaging pipeline (Chrome MV3, Firefox, Edge) with generated manifests.
- Automatic icon handling via WXT and `@wxt-dev/auto-icons`.
- `PRIVACY.md` privacy policy.
- `README.user.md` end-user guide.

### Fixed

- CSV output reliability: quote header cells to prevent commas/quotes/newlines in header labels from breaking the CSV.
- CSV compatibility: use CRLF (`\r\n`) line endings for better Excel/Windows handling.
- Download hygiene: revoke the generated `blob:` object URL after triggering the download.
- Encoding clarity: set download MIME type to `text/csv;charset=utf-8` (BOM is still included).
- Accessibility: keep the injected control as a `<span>` but add `role="button"`, `tabIndex=0`, and Enter/Space keyboard activation.
- Idempotency: avoid injecting multiple "Export CSV" controls on repeated content script execution.
- Logging: standardize export failure logging to structured `{ error }` payload.

## [1.0.1] - 2025-11-29

### Added

- Core functionality: inject an "Export CSV" button into Steam Account History and export the visible table as CSV.
- CSV output safeguards: quoted values, multi-line cell values joined with `|`, and a UTF-8 BOM for better Excel compatibility.
- Initial build tooling and assets (TypeScript project scaffolding, manifest and icons).
