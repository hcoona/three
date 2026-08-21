# PhiFailureDetector Console App

## Summary

`PhiFailureDetector.ConsoleApp.csproj` is a public C# app that still references
`Microsoft.Build.Artifacts`.

## Key Points

- The project is a public app under `src/public/app/`.
- It has `OutputType=Exe`.
- It includes `<Sdk Name="Microsoft.Build.Artifacts" />`.
- It references the public `PhiFailureDetector` library project.

## Important Claims

- The current public app story is mixed: one app already uses explicit
  `dotnet publish`, while another still uses the Artifacts SDK pattern.
- This project is a concrete migration candidate for the future app-release
  model.

## Related Pages

- [C# Packability Rules](./2026-04-21-src-directory-build-props-packability.md)

## Open Questions

- Should this app publish a single RID-specific console binary or a broader
  matrix of targets?

## Source Location

- `src/public/app/PhiFailureDetector.Console/PhiFailureDetector.ConsoleApp.csproj`
