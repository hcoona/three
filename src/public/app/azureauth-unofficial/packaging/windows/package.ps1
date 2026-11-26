#Requires -Version 7.4
<#
.SYNOPSIS
Builds the azureauth-unofficial Windows x64 portable zip from a clean Git checkout.

.DESCRIPTION
Run on Windows x64 from a full Git clone (Nerdbank.GitVersioning needs the history)
with the .NET SDK that global.json selects and the Visual Studio C++ tools that Native
AOT requires. The script refuses a dirty worktree, restores in locked mode from the
repository's nuget.org-only configuration, publishes Native AOT, enforces the archive
content allowlist, checks that native imports resolve only to inbox System32 DLLs and
API sets, checks the executable's version resource, runs the packaged --version, and
writes the zip and its SHA-256. It also writes the publish output's symbol files, with the
same license and notices, into a separate symbols zip with its own SHA-256, beside the
package and never inside it. Packaged text files use LF line endings.

The script runs no authentication. It writes only under -OutputDirectory, the
repository's ignored obj and bin directories, and the NuGet package folder.

.PARAMETER OutputDirectory
Directory for the zips and checksums; created if missing. Default: artifacts\package.

.PARAMETER DisableNuGetAudit
Turns off NuGet audit for the restore, for hosts that cannot reach the audit source.
Locked mode still requires every package to match the committed lock files.

.PARAMETER AllowNonPublicRelease
Packages a build from a ref other than main-v2, whose version carries the commit-ID
prerelease suffix. Use it only for development validation; a packaged release or a
real-host test subject is built with main-v2 checked out.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $DisableNuGetAudit,
    [switch] $AllowNonPublicRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$CommandName = 'azureauth-unofficial'
$Repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Project = Join-Path $Repository 'src\Authentication.Cli\Authentication.Cli.csproj'

function Fail([string] $Message) { throw "package.ps1: $Message" }

if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    Fail 'run on Windows x64.'
}

Push-Location $Repository
try {
    # A dirty worktree would build bytes that its version's commit does not identify.
    $status = git status --porcelain --untracked-files=normal
    if ($status) { Fail "the worktree is not clean:`n$($status -join "`n")" }
    if ((git rev-parse --is-shallow-repository) -eq 'true') { Fail 'the clone is shallow; version height needs full history.' }

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'

    $restore = @('restore', $Project, '--locked-mode', '--configfile', (Join-Path $Repository 'nuget.config'))
    if ($DisableNuGetAudit) { $restore += '-p:NuGetAudit=false' }
    dotnet @restore

    $versionJson = dotnet msbuild $Project -nologo -target:GetBuildVersion `
        -getProperty:NuGetPackageVersion -getProperty:GitCommitId -getProperty:PublicRelease `
        -getProperty:AssemblyInformationalVersion -getProperty:AssemblyFileVersion
    $build = ($versionJson -join "`n" | ConvertFrom-Json).Properties
    $publicRelease = $build.PublicRelease -eq 'True'
    if (-not $publicRelease -and -not $AllowNonPublicRelease) {
        Fail 'not a public-release build; check out main-v2, or pass -AllowNonPublicRelease for development validation.'
    }
    $version = $build.NuGetPackageVersion
    if ($version -notmatch '^0\.1\.0-alpha\.(0|[1-9][0-9]*)(\.g[0-9a-f]{10})?$') { Fail "unexpected version '$version'." }
    $full = "$version+$($build.GitCommitId.Substring(0, 10))"
    Write-Host "Packaging $CommandName $full (public release: $publicRelease)"

    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $Repository 'artifacts\package' }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $packageName = "$CommandName-$version-win-x64"
    $zip = Join-Path $OutputDirectory "$packageName.zip"
    $symbolsZip = Join-Path $OutputDirectory "$packageName-symbols.zip"
    $work = Join-Path $OutputDirectory $packageName
    foreach ($path in @($zip, "$zip.sha256", $symbolsZip, "$symbolsZip.sha256", $work)) {
        if (Test-Path -LiteralPath $path) { Fail "output already exists: $path" }
    }
    $publish = Join-Path $work 'publish'
    $stage = Join-Path $work 'stage'
    New-Item -ItemType Directory -Path $publish, $stage | Out-Null

    dotnet publish $Project -c Release -r win-x64 --no-restore -o $publish

    # Archive content allowlist. The publish output may add only symbol files: the
    # native one and the managed ones of the project's own assemblies. They go into the
    # separate symbols zip, never into the package archive.
    $published = @(Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath($publish, $_.FullName) })
    $expected = @("$CommandName.exe", 'msalruntime.dll')
    $symbols = @("$CommandName.pdb", 'Authentication.Core.pdb', 'Authentication.Windows.pdb')
    $unexpected = @($published | Where-Object { $_ -notin $expected -and $_ -notin $symbols })
    if ($unexpected) { Fail "unexpected publish output: $($unexpected -join ', ')" }
    foreach ($name in $expected) {
        if ($name -notin $published) { Fail "missing publish output: $name" }
        Copy-Item -LiteralPath (Join-Path $publish $name) -Destination $stage
    }
    if ("$CommandName.pdb" -notin $published) { Fail "missing publish output: $CommandName.pdb" }
    $symbolFiles = @($published | Where-Object { $_ -in $symbols })

    # Native imports must resolve to System32 or API-set contracts, never to a DLL
    # found through the application directory, PATH or the working directory. The
    # Visual C++ redistributable also installs into System32 on development hosts but is
    # not part of Windows, so its DLLs are rejected by name.
    $redistributable = '^(vcruntime|msvcp|concrt|vccorlib|vcomp|ucrtbased)[0-9_a-z]*\.dll$'
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { Fail 'Visual Studio C++ tools were not found.' }
    $msvc = Get-Content -LiteralPath (Join-Path $vs 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt')
    $dumpbin = Join-Path $vs "VC\Tools\MSVC\$($msvc.Trim())\bin\Hostx64\x64\dumpbin.exe"
    $system32 = [Environment]::GetFolderPath('System')
    foreach ($name in $expected) {
        $lines = & $dumpbin /nologo /dependents (Join-Path $stage $name)
        $imports = @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[A-Za-z0-9_.-]+\.dll$' })
        if (-not $imports) { Fail "no imports were read from $name." }
        foreach ($import in $imports) {
            $apiSet = $import -match '^(api|ext)-ms-'
            if ($import -match $redistributable) {
                Fail "$name imports $import, a Visual C++ redistributable DLL that Windows does not include."
            }
            if (-not $apiSet -and -not (Test-Path -LiteralPath (Join-Path $system32 $import))) {
                Fail "$name imports $import, which is not a System32 DLL or an API set."
            }
        }
        Write-Host "$name imports $($imports.Count) System32 DLLs and API sets."
    }

    # The Win32 version resource carries the numeric file version and Nerdbank.GitVersioning's
    # informational version, which omits the commit-ID prerelease suffix of non-public builds.
    $resource = (Get-Item -LiteralPath (Join-Path $stage "$CommandName.exe")).VersionInfo
    $numeric = $build.AssemblyFileVersion
    $informational = $build.AssemblyInformationalVersion
    if ($resource.FileVersion -ne $numeric -or $resource.ProductVersion -ne $informational) {
        Fail "version resource is '$($resource.FileVersion)' / '$($resource.ProductVersion)', expected '$numeric' / '$informational'."
    }

    # The packaged executable reports the computed version from its own directory, with
    # PATH limited to System32. This runs no authentication.
    $savedPath = $env:PATH
    try {
        $env:PATH = $system32
        Push-Location $stage
        try { $reported = & (Join-Path $stage "$CommandName.exe") --version } finally { Pop-Location }
    } finally { $env:PATH = $savedPath }
    if ($reported -ne "$CommandName $full") { Fail "packaged --version reported '$reported', expected '$CommandName $full'." }

    # Packaged text files use LF line endings whatever the checkout's Git configuration.
    function Write-LfText([string] $Text, [string] $Destination) {
        $lf = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
        [IO.File]::WriteAllText($Destination, $lf, [Text.UTF8Encoding]::new($false))
    }
    function Copy-LfText([string] $Source, [string] $Destination) {
        Write-LfText ([IO.File]::ReadAllText($Source)) $Destination
    }

    New-Item -ItemType Directory -Path (Join-Path $stage 'profiles') | Out-Null
    Copy-LfText (Join-Path $PSScriptRoot 'profiles\visual-studio-legacy-wam.json') (Join-Path $stage 'profiles\visual-studio-legacy-wam.json')
    Copy-LfText (Join-Path $PSScriptRoot 'README.md') (Join-Path $stage 'README.md')
    Copy-LfText (Join-Path $Repository 'LICENSE.txt') (Join-Path $stage 'LICENSE.txt')

    # Third-party notices: the fixed header, then the .NET runtime notices from the
    # ILCompiler runtime package that the lock file pins.
    $lock = Get-Content -Raw -LiteralPath (Join-Path $Repository 'src\Authentication.Cli\packages.lock.json') | ConvertFrom-Json
    $ilc = $lock.dependencies.'net10.0-windows7.0/win-x64'.'runtime.win-x64.Microsoft.DotNet.ILCompiler'.resolved
    $packages = $env:NUGET_PACKAGES
    if (-not $packages) {
        $packages = ((dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
    }
    $runtimeNotices = Join-Path $packages "runtime.win-x64.microsoft.dotnet.ilcompiler\$ilc\THIRD-PARTY-NOTICES.TXT"
    if (-not (Test-Path -LiteralPath $runtimeNotices)) { Fail "runtime notices not found: $runtimeNotices" }
    $notices = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.header.txt')) +
        (Get-Content -Raw -LiteralPath $runtimeNotices)
    Write-LfText $notices (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')

    $archive = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath($stage, $_.FullName) } | Sort-Object)
    $allowed = @("$CommandName.exe", 'LICENSE.txt', 'README.md', 'THIRD-PARTY-NOTICES.txt', 'msalruntime.dll',
        'profiles\visual-studio-legacy-wam.json') | Sort-Object
    if (Compare-Object $archive $allowed) { Fail "archive content differs from the allowlist: $($archive -join ', ')" }
    $textFiles = @('LICENSE.txt', 'README.md', 'THIRD-PARTY-NOTICES.txt', 'profiles\visual-studio-legacy-wam.json')
    foreach ($name in $textFiles) {
        if ([IO.File]::ReadAllBytes((Join-Path $stage $name)) -contains 13) { Fail "$name contains a carriage return." }
    }

    # The symbols zip holds the symbol files and the same license and notices.
    $symbolsStage = Join-Path $work 'symbols'
    New-Item -ItemType Directory -Path $symbolsStage | Out-Null
    foreach ($name in $symbolFiles) { Copy-Item -LiteralPath (Join-Path $publish $name) -Destination $symbolsStage }
    foreach ($name in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
        Copy-Item -LiteralPath (Join-Path $stage $name) -Destination $symbolsStage
    }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))

    Compress-Archive -Path (Join-Path $symbolsStage '*') -DestinationPath $symbolsZip -CompressionLevel Optimal
    $symbolsHash = (Get-FileHash -LiteralPath $symbolsZip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$symbolsZip.sha256", "$symbolsHash  $packageName-symbols.zip`n",
        [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath $work -Recurse -Force

    Write-Host "Wrote $zip"
    Write-Host "SHA-256 $hash"
    Write-Host "Wrote $symbolsZip ($(($symbolFiles + 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt') -join ', '))"
    Write-Host "SHA-256 $symbolsHash"
}
finally {
    Pop-Location
}
