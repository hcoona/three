# ImageOcclusionEditor WinUI3 Project

## Summary

`ImageOcclusionEditorWinUI3.csproj` is a public C# desktop app configured for
RID-specific, self-contained WinUI publishing with AOT enabled.

## Key Points

- `OutputType` is `WinExe`.
- The project targets `net10.0-windows10.0.22000.0`.
- It is configured with `RuntimeIdentifiers=win-x64`.
- `PublishAot`, `WindowsAppSDKSelfContained`, and `SelfContained` are enabled.

## Important Claims

- This app already matches the future direction of using `dotnet publish` to
  produce installable binaries.
- Windows-specific packaging remains a separate concern from the publish step.

## Related Pages

- [ImageOcclusionEditor dotnet publish Script](./2026-04-21-image-occlusion-editor-dotnet-publish-script.md)
- [ImageOcclusionEditor Inno Installer Script](./2026-04-21-image-occlusion-editor-inno-installer-script.md)

## Open Questions

- Which additional RIDs or host targets should be published in buddy and
  official release matrices?

## Source Location

- `src/public/app/ImageOcclusionEditor/ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj`
