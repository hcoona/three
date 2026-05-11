# hcoona-release-smoke-inno

Minimal console application used to smoke-test real Inno Setup installer artifacts published to GitHub Release.

The release build publishes the .NET app, then runs `script/Build-InnoInstaller.ps1` against `script/Setup.iss` with ISCC.
