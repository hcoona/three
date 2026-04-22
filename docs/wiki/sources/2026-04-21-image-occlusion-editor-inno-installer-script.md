# ImageOcclusionEditor Inno Installer Script

## Summary

`Build-InnoInstaller.ps1` turns the published WinUI app output into an installer
by running Inno Setup against the conventional publish directory.

## Key Points

- The script assumes `dotnet publish` has already run.
- It discovers the publish output using the same `out/<Configuration>/<TFM>/<RID>`
  convention as the publish script.
- It locates `ISCC.exe`, validates the published executable exists, and invokes
  `Setup.iss`.

## Important Claims

- Installer creation is modeled as a second-stage packaging step after binary
  publishing.
- This separation aligns well with a future release matrix that builds binaries
  first and packages installers only where needed.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [ImageOcclusionEditor dotnet publish Script](./2026-04-21-image-occlusion-editor-dotnet-publish-script.md)

## Open Questions

- Which other apps, if any, need installer packaging beyond raw published
  binaries?

## Source Location

- `src/public/app/ImageOcclusionEditor/script/Build-InnoInstaller.ps1`
