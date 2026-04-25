# hcoona-release-smoke

`hcoona-release-smoke` is a tiny public package used to validate the Three
monorepo's Python release workflow.

It exists so the repository has one intentionally minimal Python package that
can exercise:

- build-system-integrated NBGV versioning through `nbgv-python`;
- pure-Python wheel and source distribution publication;
- end-to-end workflow-release smoke checks for PyPI-oriented paths.

This package is intentionally small and stable. It is not positioned as a
general-purpose end-user library.
