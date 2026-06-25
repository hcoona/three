# Workflow Release Deferred PyPI Multi-Wheel Support

## Status

Deferred out of the current signed-off scope.

## Current-Scope Rule

Current-scope PyPI support is intentionally narrowed to the repo's present pure-
Python packaging reality:

- each `pypi-publish` node must own exactly one wheel;
- it may also own zero or one sdist;
- those artifacts must come from the same variant.

This keeps the current filename, remote-member matching, and immutable-registry
replay rules aligned with the repo's current pure-Python Hatchling packages.

## Deferred Issue

Support for multiple wheels under one PyPI publication intent, including cross-
variant wheel sets or platform-specific wheel matrices, is out of current
scope.

This deferral is only about broader PyPI artifact cardinality. It does not defer
first-delivery live PyPI publication for the current one-wheel-plus-optional-
sdist path.

## Why It Is Deferred

That broader support would require a fuller planner-frozen wheel
filename/tag design than the current scope needs. In particular, future design
work would need to define how planner-owned wheel identity is frozen and
matched across:

- descriptor contract compatibility rules;
- plan `projection.final-distribution-filenames-by-artifact-id` and
  `projection.final-distribution-sha256-by-artifact-id`;
- immutable-registry remote-member matching and replay classification;
- executor upload obligations for wheel basenames and wheel tags.

This document records the deferred issue only. It does not choose that future
design now.
