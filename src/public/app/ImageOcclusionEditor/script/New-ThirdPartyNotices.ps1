<#
.SYNOPSIS
    Generate the project's third-party license list and write it to THIRD-PARTY-NOTICES.TXT.

.DESCRIPTION
    - Use CycloneDX to generate the SBOM (manifest\bom.json)
    - Parse component and license information
    - Treat official .NET components such as Microsoft.* / System.* / runtime.* as "MIT AND .NET Library License"
    - Group by license and write to THIRD-PARTY-NOTICES.TXT at the repository root

.PARAMETER Project
    Project file path for SBOM generation (default: ImageOcclusionEditorWinUI3\ImageOcclusionEditorWinUI3.csproj).

.PARAMETER ManifestDir
    SBOM output directory (default: manifest).

.PARAMETER Output
    Output file path (default: THIRD-PARTY-NOTICES.TXT at the repository root).

.NOTES
    Requires the CycloneDX .NET tool to be installed locally.
    Prefer the command "dotnet-CycloneDX"; if unavailable, fall back to "dotnet CycloneDX".
#>
param(
    [string]$Project = "ImageOcclusionEditorWinUI3\ImageOcclusionEditorWinUI3.csproj",
    [string]$ManifestDir = "out\_manifest",
    [string]$Output = "THIRD-PARTY-NOTICES.TXT"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $true
$PSStyle.OutputRendering = 'Ansi'

# Dot-source shared helpers
. "$PSScriptRoot/Helpers.ps1"

# Known repository URL overrides for packages missing metadata
$RepoUrlOverrides = @{
    'FileSignatures'         = 'https://github.com/neilharvey/FileSignatures';
    'IO.Github.Hcoona.Pngcs' = 'https://github.com/hcoona/Pngcs';
}

function Resolve-RepoRoot {
    # Script located under repo/script
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    return Resolve-Path (Join-Path $scriptDir "..")
}

function New-DirectoryIfMissing {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, 'Create directory')) {
            [void](New-Item -ItemType Directory -Path $Path)
        }
    }
}


# --- Web/metadata helpers ---
function Invoke-WebRequestSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [int]$TimeoutSec = 20
    )
    try {
        Write-Information "[HTTP] GET $Uri"
        return Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec $TimeoutSec
    }
    catch {
        Write-Warning "[HTTP] Failed GET ${Uri}: $($_.Exception.Message)"
        return $null
    }
}

function Test-UrlExistsHead {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [int]$TimeoutSec = 15
    )
    try {
        $resp = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Head -TimeoutSec $TimeoutSec -MaximumRedirection 5
        if ($null -ne $resp.StatusCode) {
            return (($resp.StatusCode -ge 200) -and ($resp.StatusCode -lt 300))
        }
        # If StatusCode not available, consider success due to no exception
        return $true
    }
    catch {
        return $false
    }
}

function Get-FinalUrl {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [int]$TimeoutSec = 20
    )
    try {
        $resp = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Head -TimeoutSec $TimeoutSec -MaximumRedirection 10
        # Try to get final effective URL
        if ($resp.BaseResponse -and $resp.BaseResponse.ResponseUri) { return $resp.BaseResponse.ResponseUri.AbsoluteUri }
        if ($resp.Headers -and $resp.Headers.Location) { return [string]$resp.Headers.Location }
    }
    catch {
        Write-Verbose "[HTTP] HEAD failed for $($Uri): $($_.Exception.Message)"
    }
    return $Uri
}

function Convert-GitHubBlobToRaw {
    param([Parameter(Mandatory = $true)][string]$Url)
    if ($Url -match 'https?://github.com/.+/blob/.+') {
        return ($Url -replace '/blob/', '/raw/')
    }
    return $Url
}

function ConvertTo-GitHubRepoUrl {
    param([Parameter(Mandatory = $true)][string]$Url)
    $u = $Url.Trim()
    # Strip common prefixes
    $u = $u -replace '^(git\+|scm:git:)', ''
    # Try to extract https://github.com/owner/repo
    $m = [regex]::Match($u, 'https?://github\.com/([^/]+)/([^/?#]+)')
    if ($m.Success) {
        $owner = $m.Groups[1].Value
        $repo = ($m.Groups[2].Value -replace '\.git$', '')
        return "https://github.com/$owner/$repo"
    }
    return $null
}

function Convert-HtmlToText {
    param([Parameter(Mandatory = $true)][string]$Html)
    try {
        $text = ($Html -replace '(?s)<script.*?</script>', '') -replace '(?s)<style.*?</style>', ''
        $text = $text -replace '(?<=[\p{L}\p{N}])(?:<[^>]+>)+(?=[\p{L}\p{N}])', ''
        $text = ($text -replace '<[^>]+>', ' ')
        $text = [System.Net.WebUtility]::HtmlDecode($text)
        # Normalize whitespace
        $text = ($text -replace "\r\n|\r|\n", "`n") -replace ' +', ' '
        $lines = $text -split "`n" | ForEach-Object { $_.TrimEnd() }
        return (($lines -join "`n").Trim())
    }
    catch { return $Html }
}

function Format-NoticeContent {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Text)

    $normalized = ($Text.Trim() -replace "\r\n|\r|\n", "`n") -replace "`t", '    '
    $lines = $normalized -split "`n" | ForEach-Object { $_.TrimEnd() }
    return ($lines -join [Environment]::NewLine)
}

function Get-NuGetLocalPackageDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $roots = @()
    if ($env:NUGET_PACKAGES) { $roots += $env:NUGET_PACKAGES }
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ($userProfile) { $roots += (Join-Path $userProfile '.nuget/packages') }

    $lowerId = $Id.ToLowerInvariant()
    $lowerVer = $Version.ToLowerInvariant()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $path = Join-Path (Join-Path $root $lowerId) $lowerVer
        if (Test-Path -LiteralPath $path -PathType Container) {
            return (Resolve-Path -LiteralPath $path).Path
        }
    }
    return $null
}

function ConvertFrom-NuGetNuspecMetadatum {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [string]$PackageDirectory
    )

    [xml]$xml = $Content
    $meta = $xml.package.metadata
    $repoUrl = $null
    $repoCommit = $null
    $repoNode = $meta.repository
    if ($repoNode) {
        $repoUrl = $repoNode.GetAttribute('url')
        $repoCommit = $repoNode.GetAttribute('commit')
        if ([string]::IsNullOrWhiteSpace($repoUrl)) { $repoUrl = $null }
        if ([string]::IsNullOrWhiteSpace($repoCommit)) { $repoCommit = $null }
    }

    $licenseNode = $meta.license
    $licenseText = if ($licenseNode) { [string]$licenseNode.InnerText } else { $null }
    $licenseType = if ($licenseNode) { $licenseNode.GetAttribute('type') } else { $null }
    if ([string]::IsNullOrWhiteSpace($licenseType)) { $licenseType = $null }

    return [pscustomobject]@{
        ProjectUrl        = [string]$meta.projectUrl
        RepositoryUrl     = $repoUrl
        RepositoryCommit  = $repoCommit
        LicenseUrl        = [string]$meta.licenseUrl
        LicenseExpression = if ($licenseType -eq 'file') { $null } else { $licenseText }
        LicenseFile       = if ($licenseType -eq 'file') { $licenseText } else { $null }
        LicenseType       = $licenseType
        PackageDirectory  = $PackageDirectory
    }
}

function Get-NuGetPackageInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Version
    )
    # Prefer the locally restored package because it is the exact versioned
    # package content used by this project, including embedded license files.
    $packageDir = Get-NuGetLocalPackageDirectory -Id $Id -Version $Version
    if ($packageDir) {
        $nuspec = Get-ChildItem -LiteralPath $packageDir -Filter '*.nuspec' -File | Select-Object -First 1
        if ($nuspec) {
            try {
                return ConvertFrom-NuGetNuspecMetadatum -Content ([System.IO.File]::ReadAllText($nuspec.FullName)) -PackageDirectory $packageDir
            }
            catch {
                Write-Verbose "[NuGet] Failed to parse local nuspec for $($Id)@$($Version): $($_.Exception.Message)"
            }
        }
    }

    # Try the NuGet registration API first.
    $lowerId = $Id.ToLowerInvariant()
    $lowerVer = $Version.ToLowerInvariant()
    $regUrl = "https://api.nuget.org/v3/registration5-gz-semver2/$lowerId/$lowerVer.json"
    $resp = Invoke-WebRequestSafe -Uri $regUrl
    if ($resp -and $resp.Content) {
        try {
            $json = $resp.Content | ConvertFrom-Json
            if ($json -and $json.catalogEntry) {
                $entry = $json.catalogEntry
                $repositoryUrl = $null
                $repositoryCommit = $null
                if ($entry.PSObject.Properties.Match('repositoryUrl').Count -gt 0 -and $entry.repositoryUrl) {
                    $repositoryUrl = $entry.repositoryUrl
                }
                elseif ($entry.PSObject.Properties.Match('repository').Count -gt 0 -and $entry.repository) {
                    if ($entry.repository.PSObject.Properties.Match('url').Count -gt 0 -and $entry.repository.url) { $repositoryUrl = $entry.repository.url }
                    if ($entry.repository.PSObject.Properties.Match('commit').Count -gt 0 -and $entry.repository.commit) { $repositoryCommit = $entry.repository.commit }
                }
                $projectUrl = if ($entry.PSObject.Properties.Match('projectUrl').Count -gt 0) { $entry.projectUrl } else { $null }
                $licenseUrl = if ($entry.PSObject.Properties.Match('licenseUrl').Count -gt 0) { $entry.licenseUrl } else { $null }
                $licenseExpression = if ($entry.PSObject.Properties.Match('licenseExpression').Count -gt 0) { $entry.licenseExpression } else { $null }
                return [pscustomobject]@{
                    ProjectUrl        = $projectUrl
                    RepositoryUrl     = $repositoryUrl
                    RepositoryCommit  = $repositoryCommit
                    LicenseUrl        = $licenseUrl
                    LicenseExpression = $licenseExpression
                    LicenseFile       = $null
                    LicenseType       = if ($licenseExpression) { 'expression' } else { $null }
                    PackageDirectory  = $packageDir
                }
            }
        }
        catch {
            Write-Verbose "[NuGet] Failed to parse registration JSON for $($Id)@$($Version): $($_.Exception.Message)"
        }
    }

    # Fallback: flat container nuspec
    $nuspecUrl = "https://api.nuget.org/v3-flatcontainer/$lowerId/$lowerVer/$lowerId.nuspec"
    $resp2 = Invoke-WebRequestSafe -Uri $nuspecUrl
    if ($resp2 -and $resp2.Content) {
        try {
            return ConvertFrom-NuGetNuspecMetadatum -Content $resp2.Content -PackageDirectory $packageDir
        }
        catch {
            Write-Verbose "[NuGet] Failed to parse nuspec XML for $($Id)@$($Version): $($_.Exception.Message)"
        }
    }
    return $null
}

function Get-NuGetPackageLicenseNotice {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Version,
        $PackageInfo
    )

    if (-not $PackageInfo) { return $null }

    $availableAt = New-Object System.Collections.Generic.List[string]
    $content = $null

    if ($PackageInfo.PackageDirectory) {
        $licenseFileNames = New-Object System.Collections.Generic.List[string]
        if ($PackageInfo.LicenseFile) { $licenseFileNames.Add([string]$PackageInfo.LicenseFile) | Out-Null }
        @(
            'LICENSE', 'LICENSE.txt', 'LICENSE.md', 'LICENSE.TXT', 'License.txt',
            'license', 'license.txt', 'license.md',
            'LICENCE', 'LICENCE.txt', 'LICENCE.md', 'Licence', 'Licence.txt', 'Licence.md',
            'licence', 'licence.txt', 'licence.md',
            'NOTICE', 'NOTICE.txt', 'NOTICE.md'
        ) | ForEach-Object { $licenseFileNames.Add($_) | Out-Null }

        foreach ($licenseFileName in ($licenseFileNames | Select-Object -Unique)) {
            $candidate = Join-Path ([string]$PackageInfo.PackageDirectory) $licenseFileName
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $relativeName = $licenseFileName -replace '\\', '/'
                $availableAt.Add("NuGet package $Id@$Version`: $relativeName") | Out-Null
                $content = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $candidate))
                break
            }
        }
    }

    if ($PackageInfo.LicenseUrl) {
        $licenseUrl = [string]$PackageInfo.LicenseUrl
        if ($licenseUrl -like 'https://go.microsoft.com/fwlink*') { $licenseUrl = Get-FinalUrl -Uri $licenseUrl }
        if ($licenseUrl -match 'https?://github.com/.+') { $licenseUrl = Convert-GitHubBlobToRaw -Url $licenseUrl }
        $availableAt.Add($licenseUrl) | Out-Null
    }
    elseif ($PackageInfo.LicenseExpression) {
        $availableAt.Add("https://licenses.nuget.org/$($PackageInfo.LicenseExpression)") | Out-Null
    }

    if ($content -or $availableAt.Count -gt 0) {
        return [pscustomobject]@{
            AvailableAt = @($availableAt)
            Content     = $content
        }
    }
    return $null
}

function Test-LicenseContentMatchesExpression {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Expression
    )

    $normalizedExpression = $Expression.Trim()
    $normalizedContent = ($Content -replace "\r\n|\r|\n", "`n").Trim()

    switch -Regex ($normalizedExpression) {
        '^MIT$' {
            return ($normalizedContent -match '(?im)^\s*(The\s+)?MIT License(\s*\(MIT\))?\s*$' -and
                $normalizedContent -match 'Permission is hereby granted, free of charge' -and
                $normalizedContent -notmatch 'Microsoft Public License')
        }
        '^MS-PL$' {
            return ($normalizedContent -match 'Microsoft Public License' -and
                $normalizedContent -match 'This license governs use of the accompanying software')
        }
        '^MPL-2\.0$' {
            return ($normalizedContent -match 'Mozilla Public License Version 2\.0')
        }
        '^GPL-3\.0-or-later$' {
            return ($normalizedContent -match 'GNU GENERAL PUBLIC LICENSE' -and
                $normalizedContent -match 'Version 3')
        }
        default {
            return $false
        }
    }
}

function Test-StandardizedLicenseTextNeedsPackageCopyright {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Expression
    )

    if ($Expression -ne 'MIT') { return $false }
    return ($Content -match '<year>\s+<copyright holders>')
}

function Get-GitHubLicenseContent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoUrl,
        [string]$Ref
    )
    # Normalize repo URL to https://github.com/owner/name
    if ($RepoUrl -notmatch '^https?://github.com/[^/]+/[^/]+') { return $null }
    $prefix = ($RepoUrl -replace '/+$', '')
    $branches = if ($Ref) { @($Ref) } else { @('main', 'master') }
    $files = @(
        # Common US spelling
        'LICENSE', 'LICENSE.txt', 'LICENSE.md', 'LICENSE.TXT', 'License.txt',
        'license', 'license.txt', 'license.md',
        # British spelling variants
        'LICENCE', 'LICENCE.txt', 'LICENCE.md', 'Licence', 'Licence.txt', 'Licence.md',
        'licence', 'licence.txt', 'licence.md'
    )
    foreach ($b in $branches) {
        foreach ($f in $files) {
            $raw = "$prefix/raw/$b/$f"
            if (Test-UrlExistsHead -Uri $raw) {
                $resp = Invoke-WebRequestSafe -Uri $raw
                if ($resp -and $resp.Content) {
                    return [pscustomobject]@{ Url = $raw; Content = $resp.Content }
                }
            }
        }
    }
    return $null
}

function Get-NuGetLicenseExpressionContent {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Expression)

    $licenseUrl = "https://licenses.nuget.org/$Expression"
    $resp = Invoke-WebRequestSafe -Uri $licenseUrl
    if ($resp -and $resp.Content) {
        return [pscustomobject]@{
            Url     = $licenseUrl
            Content = (Convert-HtmlToText -Html $resp.Content)
        }
    }
    return $null
}

function Get-LicenseFromComponent($component) {
    # Treat official .NET components as dual-licensed: MIT AND .NET Library License
    $name = [string]$component.name
    if ($name -match '^(Microsoft\.|System\.|runtime\.)') {
        return 'MIT AND .NET Library License'
    }

    $licenses = $component.licenses
    if ($null -eq $licenses -or $licenses.Count -eq 0) { return 'Unknown' }

    # Try 'expression' first
    $expr = $licenses | Where-Object { $_.PSObject.Properties.Match('expression').Count -gt 0 -and $_.expression }
    if ($expr) { return [string]$expr[0].expression }

    # Then try license.id or license.name
    $first = $licenses[0]
    if ($first -and $first.license) {
        $id = $first.license.id
        if ($id) { return [string]$id }
        $nm = $first.license.name
        if ($nm) { return [string]$nm }
    }
    return 'Unknown'
}

function Read-Component([string]$BomPath) {
    $jsonText = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $BomPath))
    $json = $jsonText | ConvertFrom-Json
    if (-not $json.components) { return @() }
    # Filter out the application itself
    $json.components | Where-Object { $_.type -ne 'application' }
}

function Format-Header([string]$RepoRoot) {
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
    @(
        'THIRD-PARTY NOTICES',
        '====================',
        "Generated on: $ts",
        '',
        'This file lists third-party components used by this project with their license names.',
        '',
        'In the event that we accidentally failed to list a required notice, please bring it',
        'to our attention. Please an issue at https://github.com/hcoona/ImageOcclusionEditor/issues.',
        '',
        'Microsoft official .NET libraries are treated as dual-licensed under MIT and the .NET Library License:',
        'https://dotnet.microsoft.com/dotnet_library_license.htm',
        '',
        'The attached notices are provided for information only.',
        '',
        'Do not edit this file manually. Regenerate via script/New-ThirdPartyNotices.ps1.',
        ''
    ) -join [Environment]::NewLine
}

function Write-Notice([string]$OutputPath, $components) {
    # Group and sort
    $grouped = $components | Group-Object { Get-LicenseFromComponent $_ } | Sort-Object Name

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((Format-Header -RepoRoot (Split-Path -Parent $OutputPath))) | Out-Null

    foreach ($g in $grouped) {
        $licenseName = [string]$g.Name
        $lines.Add("License: $licenseName") | Out-Null
        $lines.Add(('-' * (9 + $licenseName.Length))) | Out-Null

        $pkgs = $g.Group | Sort-Object name, version | ForEach-Object {
            $n = [string]$_.name
            $v = [string]$_.version
            "$n@$v"
        } | Select-Object -Unique

        foreach ($p in $pkgs) { $lines.Add("- $p") | Out-Null }
        if ($licenseName -eq 'MIT AND .NET Library License') {
            $lines.Add('  Note: Microsoft official .NET libraries are treated as dual-licensed under MIT and the .NET Library License.') | Out-Null
            $lines.Add('        See: https://dotnet.microsoft.com/dotnet_library_license.htm') | Out-Null
        }

        $lines.Add('') | Out-Null
    }

    $dir = Split-Path -Parent $OutputPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-DirectoryIfMissing -Path $dir }
    Set-Content -LiteralPath $OutputPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8

    # Append detailed license notices per user request
    $nonMs = $components | Where-Object { ([string]$_.name) -notmatch '^(Microsoft\.|System\.|runtime\.)' }
    # choose a representative version (highest) per package name
    $byName = $nonMs | Group-Object name
    $pkgs = @()
    foreach ($g in $byName) {
        $selected = $g.Group |
            Sort-Object -Descending -Property @{ Expression = { try { [version]$_.version } catch { [version]'0.0.0.0' } } } |
            Select-Object -First 1
        $pkgs += $selected
    }
    $pkgs = $pkgs | Sort-Object name

    Add-Content -LiteralPath $OutputPath -Value ([Environment]::NewLine + "" + [Environment]::NewLine) -Encoding UTF8
    foreach ($p in $pkgs) {
        $id = [string]$p.name
        $ver = [string]$p.version
        $meta = Get-NuGetPackageInfo -Id $id -Version $ver
        $projectUrl = if ($meta) { $meta.ProjectUrl } else { $null }
        if ($projectUrl -and $projectUrl -like 'https://go.microsoft.com/fwlink*') { $projectUrl = Get-FinalUrl -Uri $projectUrl }
        $repoUrl = if ($meta) { if ($meta.RepositoryUrl) { $meta.RepositoryUrl } elseif ($projectUrl -and $projectUrl -match 'github.com') { $projectUrl } }
        if (-not $repoUrl -and $RepoUrlOverrides.ContainsKey($id)) { $repoUrl = $RepoUrlOverrides[$id] }
        # Normalize GitHub URLs from either repositoryUrl or projectUrl
        $ghFromRepo = if ($repoUrl) { ConvertTo-GitHubRepoUrl -Url $repoUrl }    else { $null }
        $ghFromProject = if ($projectUrl) { ConvertTo-GitHubRepoUrl -Url $projectUrl } else { $null }
        $githubRepoUrl = if ($ghFromRepo) { $ghFromRepo } elseif ($ghFromProject) { $ghFromProject } else { $null }
        $isGitHubRepo = [string]::IsNullOrEmpty($githubRepoUrl) -eq $false
        $packageLicense = Get-NuGetPackageLicenseNotice -Id $id -Version $ver -PackageInfo $meta
        $licenseUrls = @()
        if ($packageLicense) { $licenseUrls += @($packageLicense.AvailableAt) }
        $licenseContent = if ($packageLicense -and $packageLicense.Content) { [string]$packageLicense.Content } else { $null }
        $repositoryCommit = if ($meta -and $meta.RepositoryCommit) { [string]$meta.RepositoryCommit } else { $null }
        $licenseGuess = $null
        $licenseExpression = if ($meta -and $meta.LicenseExpression) { [string]$meta.LicenseExpression } else { $null }
        if (-not $licenseContent -and $meta -and $meta.LicenseExpression) {
            $licenseGuess = Get-NuGetLicenseExpressionContent -Expression $licenseExpression
            if ($licenseGuess) {
                $licenseUrls += $licenseGuess.Url
                $licenseContent = [string]$licenseGuess.Content
            }
        }
        if ($isGitHubRepo -and $repositoryCommit -and
            (-not $licenseExpression -or -not $licenseContent -or
            (Test-StandardizedLicenseTextNeedsPackageCopyright -Content $licenseContent -Expression $licenseExpression))) {
            $licenseGuess = Get-GitHubLicenseContent -RepoUrl $githubRepoUrl -Ref $repositoryCommit
            if ($licenseGuess) {
                $githubLicenseContent = [string]$licenseGuess.Content
                if (-not $licenseExpression -or (Test-LicenseContentMatchesExpression -Content $githubLicenseContent -Expression $licenseExpression)) {
                    $licenseUrls += $licenseGuess.Url
                    $licenseContent = $githubLicenseContent
                }
            }
        }
        if (-not $licenseContent -and $isGitHubRepo -and -not $repositoryCommit -and -not $licenseExpression) {
            $licenseGuess = Get-GitHubLicenseContent -RepoUrl $githubRepoUrl
            if ($licenseGuess) {
                $licenseUrls += $licenseGuess.Url
                $licenseContent = [string]$licenseGuess.Content
            }
        }

        $section = @()
        $section += ""
        $section += "License notice for $id"
        $section += "-------------------------------"
        if ($projectUrl) { $section += ""; $section += $projectUrl }
        elseif ($repoUrl) { $section += ""; $section += $repoUrl }
        if (@($licenseUrls).Count -gt 0) {
            $section += ""
            $section += "Available at"
            $section += ($licenseUrls | Select-Object -Unique)
        }
        if ($licenseContent) {
            $section += ""
            $section += (Format-NoticeContent -Text $licenseContent)
        }
        $sectionText = ($section -join [Environment]::NewLine)
        Add-Content -LiteralPath $OutputPath -Value $sectionText -Encoding UTF8
        Add-Content -LiteralPath $OutputPath -Value ([Environment]::NewLine) -Encoding UTF8
    }

    # Append unified Microsoft section once at the end
    $msSection = @()
    $msSection += ""
    $msSection += "Microsoft .NET Libraries"
    $msSection += "-------------------------------"
    $msSection += ""
    $msSection += "License notices for Microsoft official .NET libraries"
    $msSection += "Available at"
    $msSection += "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"
    $msSection += "https://dotnet.microsoft.com/dotnet_library_license.htm"
    # Append original texts
    $msSection += ""
    $runtimeRaw = "https://github.com/dotnet/runtime/raw/main/LICENSE.TXT"
    try {
        if (Test-UrlExistsHead -Uri $runtimeRaw) {
            $resp = Invoke-WebRequestSafe -Uri $runtimeRaw
            if ($resp -and $resp.Content) {
                $msSection += ""
                $msSection += (Format-NoticeContent -Text $resp.Content)
            }
        }
    }
    catch {
        Write-Verbose "[HTTP] Failed to fetch runtime license text: $($_.Exception.Message)"
    }
    $dotnetLibPage = "https://dotnet.microsoft.com/dotnet_library_license.htm"
    $finalDotnetLib = Get-FinalUrl -Uri $dotnetLibPage
    # Try to fetch page content; may be HTML
    try {
        $pageResp = Invoke-WebRequestSafe -Uri $finalDotnetLib
        if ($pageResp -and $pageResp.Content) {
            $msSection += ""
            $msSection += (Convert-HtmlToText -Html $pageResp.Content)
        }
    }
    catch {
        Write-Verbose "[HTTP] Failed to fetch .NET Library License page: $($_.Exception.Message)"
    }
    Add-Content -LiteralPath $OutputPath -Value ($msSection -join [Environment]::NewLine) -Encoding UTF8
}

# --- Main flow ---
$repoRoot = Resolve-RepoRoot
Push-Location $repoRoot
try {
    $projectPath = Resolve-Path -LiteralPath $Project
    $manifestPath = Join-Path $repoRoot $ManifestDir
    New-DirectoryIfMissing -Path $manifestPath

    $projectInfo = Get-ProjectInfo -CsprojPath $projectPath
    if ($projectInfo.TargetFramework -match '-windows' -and -not $IsWindows -and -not $env:EnableWindowsTargeting) {
        $env:EnableWindowsTargeting = 'true'
    }

    $restoreArgs = @('restore', $projectPath, '--locked-mode')
    if ($projectInfo.TargetFramework -match '-windows') { $restoreArgs += '-p:EnableWindowsTargeting=true' }
    if ($projectInfo.RuntimeIdentifier) { $restoreArgs += "-p:RuntimeIdentifier=$($projectInfo.RuntimeIdentifier)" }
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    $bomPath = Invoke-CycloneDX -ProjectPath $projectPath -OutDir $manifestPath -TargetFramework $projectInfo.TargetFramework -RuntimeIdentifier $projectInfo.RuntimeIdentifier -DisablePackageRestore
    Write-Information "[CycloneDX] SBOM generated at: $bomPath"

    $components = @(Read-Component -BomPath $bomPath)
    Write-Information ("[Info] Components loaded: {0}" -f $components.Count)

    $outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }
    Write-Notice -OutputPath $outputPath -components $components
    Write-Information "[Done] THIRD-PARTY-NOTICES written to: $outputPath"
}
finally {
    Pop-Location
}
