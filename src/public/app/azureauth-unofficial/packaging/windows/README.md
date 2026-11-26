# azureauth-unofficial

**azureauth-unofficial** is the Windows x64 command of **AzureAuth Unofficial V2**, an
independent, unofficial, pre-release fork of
[Microsoft Authentication CLI](https://github.com/AzureAD/microsoft-authentication-cli).
It is not a Microsoft product, is not supported by Microsoft, and makes no support,
production-readiness, or compatibility commitment. Source and license:
<https://github.com/hcoona/microsoft-authentication-cli> (branch `main-v2`).

This package is an alpha prerelease. It requires Windows 11 x64 in an interactive user
session with Web Account Manager (WAM) available.

## Contents

| File | Purpose |
| --- | --- |
| `azureauth-unofficial.exe` | The command, a self-contained Native AOT executable. |
| `msalruntime.dll` | The Microsoft authentication broker runtime. Keep it next to the executable. |
| `profiles/visual-studio-legacy-wam.json` | An optional Client Profile; see below. |
| `LICENSE.txt` | The license of this project. |
| `THIRD-PARTY-NOTICES.txt` | Notices for the bundled .NET runtime and Microsoft libraries. |
| `README.md` | This file. |

## Install

Extract the archive into a directory you choose, for example
`%LOCALAPPDATA%\Programs\azureauth-unofficial`. Do not extract it into an AzureAuth
installation directory such as `%LOCALAPPDATA%\Programs\AzureAuth`. Keep every file in
the same directory. Nothing is added to `PATH` or the registry, and no other file is
written.

Each release is published with a SHA-256 checksum. Before extracting, compare it:

```powershell
(Get-FileHash .\azureauth-unofficial-<version>-win-x64.zip -Algorithm SHA256).Hash
```

The files are not code-signed. Windows marks files downloaded from the internet with the
Mark of the Web, and the extracted files may carry the mark too. Microsoft Defender
SmartScreen may then warn before the first run. If you trust the archive whose checksum
you verified, you can clear the mark on the archive before extracting it, as described
in Microsoft's
[Unblock-File documentation](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/unblock-file):

```powershell
Unblock-File .\azureauth-unofficial-<version>-win-x64.zip
```

Clearing the mark does not help with Smart App Control: where it is on in enforcement
mode, it blocks unsigned code that Microsoft's app intelligence service does not
recognize, whether or not the file has the mark.

See [Microsoft Defender SmartScreen](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
and [Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview)
for how Windows evaluates downloaded and unsigned applications.

## Use

```powershell
azureauth-unofficial.exe --help
azureauth-unofficial.exe --version
azureauth-unofficial.exe authenticate --protocol 1 `
  --profile "$PWD\profiles\visual-studio-legacy-wam.json" `
  --account-email you@example.com `
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default `
  --interaction interactive-if-needed
```

An `authenticate` call writes one JSON result to stdout and exits 0 on success or 1 on a
typed failure. The result contains an access token; treat stdout as a secret.

From WSL, call the Windows executable directly and pass the Profile as a Windows path:

```sh
/mnt/c/Users/<you>/AppData/Local/Programs/azureauth-unofficial/azureauth-unofficial.exe \
  authenticate --protocol 1 \
  --profile 'C:\Users\<you>\AppData\Local\Programs\azureauth-unofficial\profiles\visual-studio-legacy-wam.json' \
  --account-email you@example.com \
  --scope 499b84ac-1321-427f-aa17-267ca6975798/.default \
  --interaction non-interactive-only
```

The Profile path must be an absolute path on a local fixed Windows drive. The call runs
in the Windows session; any sign-in window appears on the Windows desktop.

## The Bundled Profile

`profiles/visual-studio-legacy-wam.json` selects the Microsoft-owned Visual Studio public
client application. It is used only when you pass it with `--profile`; there is no
default Profile. You may instead use a Profile for an application registration you own.

- Microsoft owns this registration. This project has no endorsement from Microsoft, no
  support commitment for it, and no control over its availability; Microsoft may change
  or restrict it at any time.
- Sign-in logs, consent prompts, and audit records show **Visual Studio** as the
  application, not this project.
- If the registration stops working, the call fails. There is no fallback to another
  application, a personal access token, or a browser.

## Uninstall

Delete the directory you extracted the archive into. Sign-in state belongs to Windows and
WAM and is not removed by deleting the directory.
