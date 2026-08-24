# C# Packability Rules

## Summary

`src/Directory.Build.props` centrally decides whether a C# project is packable
based on which subtree it lives in.

## Key Points

- Projects under `src/public/lib/` and `src/private/lib/` are packable.
- Projects under `src/public/app/`, `src/private/app/`, `src/lab/`, and
  `src/sample/` are not packable.
- The file is path-driven, so project visibility and project kind are encoded by
  folder location rather than repeated per project.

## Important Claims

- C# library publishing can be inferred repo-wide from folder placement.
- C# apps are expected to publish binaries rather than NuGet packages.

## Related Pages

- [Hjg.Pngcs C# Package Metadata](./2026-04-21-hjg-pngcs-csproj.md)
- [PhiFailureDetector Console App](./2026-04-21-phi-failure-detector-console-csproj.md)

## Open Questions

- Which non-packable private apps should still receive binary release automation?

## Source Location

- `src/Directory.Build.props`
