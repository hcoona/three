# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2025-12-18

### Changed

- **BREAKING**: The `version` template field now contains the raw NBGV version string instead of the PEP 440 normalized version. Use `normalized_version` for the PEP 440 compliant string.
- **BREAKING**: Map unrecognized prerelease labels to `.dev` suffixes to preserve version ordering (e.g., `1.0.0-ci` becomes `1.0.0.dev0+ci`, `1.0.0-ci.1` becomes `1.0.0.dev1+ci`). Complex prerelease tags that cannot be mapped will now raise an error.
- **BREAKING**: Render boolean version-tuple components as Python boolean literals (`True`/`False`) instead of quoted strings.

### Fixed

- Avoid accidental path duplication when invoking `nbgv get-version` with a relative project directory
- Fix `GitVersion` mapping protocol behavior (missing keys now raise `KeyError`; iteration/length reflect all supported keys)
- Ensure single-element version tuples are rendered with a trailing comma
- Fix documentation links in project metadata
- Use strict SemVer 2.0 regex for version parsing
- Raise `NbgvVersionNormalizationError` when version normalization fails, instead of a generic `RuntimeError`.

## [1.1.0] - 2025-12-17

### Added

- Add project URLs metadata to pyproject.toml

### Fixed

- Fix linting issues and improve code quality

## [1.1.0b1] - 2025-11-09

### Added

- Replace VersionRevision with BuildNumber in version tuple
- Add epoch support for versioning
- Enhance version tuple configuration options
- Prefer normalized version in Python templates
- Support templated version files in Hatch plugin
- Update default version field to SemVer2

### Documentation

- Clarify epoch handling in version-tuple configuration
- Update README.md for Write Version File in Hatch Integration
- Update README.md for Pypi Preview

### Changed

- Bump Version from 1.0.0b1 to 1.1.0b1

## [1.0.0b1] - 2025-11-08

### Added

- Introduce nbgv-python Package
