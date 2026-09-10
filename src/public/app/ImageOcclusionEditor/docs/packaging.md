# Publish and Installer Responsibilities

Project maintainers use this record to locate the existing packaging stages.
The scripts and project file own executable settings; these observations are
not a release-channel selection or a new support commitment.

[Publish-ImageOcclusionEditor.ps1](../script/Publish-ImageOcclusionEditor.ps1)
publishes into `out/ImageOcclusionEditor/<Configuration>/<TargetFramework>/<RuntimeIdentifier>/`.
It selects locked restore when the package lock exists and invokes CycloneDX
for an SBOM after publishing. Its parameters and error handling remain the
executable contract.

[Build-InnoInstaller.ps1](../script/Build-InnoInstaller.ps1) consumes the existing
publish output, stages the Inno Setup inputs, and invokes ISCC. It does not run
`dotnet publish`. Its output-root parameters determine the consumed paths.

[The WinUI project](../ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj)
owns the Windows target, RID, self-contained and NativeAOT settings and retained
trim-warning policy. These settings alone do not prove compatibility, successful
publishing or an accepted release matrix. The existing [README](../README.md)
and license/notice files retain their user and attribution interfaces.

This routing preserves the distinct publish, installer and project-setting
observations from the [publish digest][publish], [installer digest][installer]
and [project digest][project] at the accepted repository baseline. The scripts
were inspected for this record; no packaging or publication was run.

[publish]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/sources/2026-04-21-image-occlusion-editor-dotnet-publish-script.md
[installer]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/sources/2026-04-21-image-occlusion-editor-inno-installer-script.md
[project]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/sources/2026-04-21-image-occlusion-editor-winui3-csproj.md
