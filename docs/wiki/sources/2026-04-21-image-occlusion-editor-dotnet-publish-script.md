# ImageOcclusionEditor dotnet publish Script

## Summary

`Publish-ImageOcclusionEditor.ps1` publishes the WinUI app into a conventional
`out/` layout by calling `dotnet publish` directly.

## Key Points

- The output shape is `out/ImageOcclusionEditor/<Configuration>/<TFM>/<RID>/`.
- The script reads TFM and RID from the project file instead of hard-coding
  them.
- It uses locked restore mode when a lock file exists.
- It generates an SBOM after the publish step.

## Important Claims

- This is the clearest current example of the desired future C# app release
  shape.
- The app publish flow is file-system-based and does not depend on GitHub
  artifact collection primitives.

## Related Pages

- [ImageOcclusionEditor WinUI3 Project](./2026-04-21-image-occlusion-editor-winui3-csproj.md)
- [ImageOcclusionEditor Inno Installer Script](./2026-04-21-image-occlusion-editor-inno-installer-script.md)

## Open Questions

- How should multi-RID app publish orchestration be modeled repo-wide?

## Source Location

- `src/public/app/ImageOcclusionEditor/script/Publish-ImageOcclusionEditor.ps1`
