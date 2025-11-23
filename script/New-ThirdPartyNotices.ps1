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
  'FileSignatures' = 'https://github.com/neilharvey/FileSignatures';
  'IO.Github.Hcoona.Pngcs' = 'https://github.com/hcoona/Pngcs';
}

function Resolve-RepoRoot {
  # Script located under repo/script
  $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
  return Resolve-Path (Join-Path $scriptDir "..")
}

function New-DirectoryIfMissing {
  [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
  param([Parameter(Mandatory=$true)][string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    if ($PSCmdlet.ShouldProcess($Path, 'Create directory')) {
      [void](New-Item -ItemType Directory -Path $Path)
    }
  }
}


# --- Web/metadata helpers ---
function Invoke-WebRequestSafe {
  param(
    [Parameter(Mandatory=$true)][string]$Uri,
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
    [Parameter(Mandatory=$true)][string]$Uri,
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
    [Parameter(Mandatory=$true)][string]$Uri,
    [int]$TimeoutSec = 20
  )
  try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method Head -TimeoutSec $TimeoutSec -MaximumRedirection 10
    # Try to get final effective URL
    if ($resp.BaseResponse -and $resp.BaseResponse.ResponseUri) { return $resp.BaseResponse.ResponseUri.AbsoluteUri }
    if ($resp.Headers -and $resp.Headers.Location) { return [string]$resp.Headers.Location }
  } catch {
    Write-Verbose "[HTTP] HEAD failed for $($Uri): $($_.Exception.Message)"
  }
  return $Uri
}

function Convert-GitHubBlobToRaw {
  param([Parameter(Mandatory=$true)][string]$Url)
  if ($Url -match 'https?://github.com/.+/blob/.+') {
    return ($Url -replace '/blob/', '/raw/')
  }
  return $Url
}

function Normalize-GitHubRepoUrl {
  param([Parameter(Mandatory=$true)][string]$Url)
  $u = $Url.Trim()
  # Strip common prefixes
  $u = $u -replace '^(git\+|scm:git:)', ''
  # Try to extract https://github.com/owner/repo
  $m = [regex]::Match($u, 'https?://github\.com/([^/]+)/([^/?#]+)')
  if ($m.Success) {
    $owner = $m.Groups[1].Value
    $repo  = ($m.Groups[2].Value -replace '\.git$', '')
    return "https://github.com/$owner/$repo"
  }
  return $null
}

function Convert-HtmlToText {
  param([Parameter(Mandatory=$true)][string]$Html)
  try {
    $text = ($Html -replace '(?s)<script.*?</script>','') -replace '(?s)<style.*?</style>',''
    $text = ($text -replace '<[^>]+>',' ')
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    # Normalize whitespace
    $text = ($text -replace "\r\n|\r|\n","`n") -replace ' +',' '
    return $text.Trim()
  } catch { return $Html }
}

function Get-NuGetPackageInfo {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$Id,
    [Parameter(Mandatory=$true)][string]$Version
  )
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
        return [pscustomobject]@{
          ProjectUrl       = $entry.projectUrl
          RepositoryUrl    = if ($entry.repositoryUrl) { $entry.repositoryUrl } elseif ($entry.repository -and $entry.repository.url) { $entry.repository.url } else { $null }
          LicenseUrl       = $entry.licenseUrl
          LicenseExpression= $entry.licenseExpression
        }
      }
    } catch {
      Write-Verbose "[NuGet] Failed to parse registration JSON for $($Id)@$($Version): $($_.Exception.Message)"
    }
  }

  # Fallback: flat container nuspec
  $nuspecUrl = "https://api.nuget.org/v3-flatcontainer/$lowerId/$lowerVer/$lowerId.nuspec"
  $resp2 = Invoke-WebRequestSafe -Uri $nuspecUrl
  if ($resp2 -and $resp2.Content) {
    try {
      [xml]$xml = $resp2.Content
      $meta = $xml.package.metadata
      $repoUrl = $null
      if ($meta.repository -and $meta.repository.url) { $repoUrl = [string]$meta.repository.url }
      return [pscustomobject]@{
        ProjectUrl       = [string]$meta.projectUrl
        RepositoryUrl    = $repoUrl
        LicenseUrl       = [string]$meta.licenseUrl
        LicenseExpression= [string]$meta.license
      }
    } catch {
      Write-Verbose "[NuGet] Failed to parse nuspec XML for $($Id)@$($Version): $($_.Exception.Message)"
    }
  }
  return $null
}

function Get-GitHubLicenseContent {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$RepoUrl
  )
  # Normalize repo URL to https://github.com/owner/name
  if ($RepoUrl -notmatch '^https?://github.com/[^/]+/[^/]+') { return $null }
  $prefix = ($RepoUrl -replace '/+$','')
  $branches = @('main','master')
  $files = @(
    # Common US spelling
    'LICENSE','LICENSE.txt','LICENSE.md','LICENSE.TXT','License.txt',
    'license','license.txt','license.md',
    # British spelling variants
    'LICENCE','LICENCE.txt','LICENCE.md','Licence','Licence.txt','Licence.md',
    'licence','licence.txt','licence.md'
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
    $ghFromRepo    = if ($repoUrl)    { Normalize-GitHubRepoUrl -Url $repoUrl }    else { $null }
    $ghFromProject = if ($projectUrl) { Normalize-GitHubRepoUrl -Url $projectUrl } else { $null }
    $githubRepoUrl = if ($ghFromRepo) { $ghFromRepo } elseif ($ghFromProject) { $ghFromProject } else { $null }
    $isGitHubRepo = [string]::IsNullOrEmpty($githubRepoUrl) -eq $false
    # Only consider NuGet's licenseUrl when repo is not GitHub; for GitHub we want raw LICENSE content.
    $licenseUrl = if (-not $isGitHubRepo -and $meta -and $meta.LicenseUrl) { $meta.LicenseUrl } else { $null }
    if ($licenseUrl -and $licenseUrl -like 'https://go.microsoft.com/fwlink*') { $licenseUrl = Get-FinalUrl -Uri $licenseUrl }
    if ($licenseUrl -and $licenseUrl -match 'https?://github.com/.+') { $licenseUrl = Convert-GitHubBlobToRaw -Url $licenseUrl }
    if (-not $isGitHubRepo -and -not $licenseUrl -and $meta -and $meta.LicenseExpression) {
      $licenseUrl = "https://licenses.nuget.org/" + ([string]$meta.LicenseExpression)
    }
    $licenseGuess = $null
    if ($isGitHubRepo) {
      $licenseGuess = Get-GitHubLicenseContent -RepoUrl $githubRepoUrl
      if (-not $licenseUrl -and $licenseGuess) { $licenseUrl = $licenseGuess.Url }
    }

    $section = @()
    $section += ""
    $section += "License notice for $id"
    $section += "-------------------------------"
    if ($projectUrl) { $section += ""; $section += $projectUrl }
    elseif ($repoUrl) { $section += ""; $section += $repoUrl }
    if ($licenseUrl) {
      $section += ""
      $section += "Available at"
      $section += $licenseUrl
    }
    if ($licenseGuess -and $licenseGuess.Content) {
      $section += ""
      $section += $licenseGuess.Content.Trim()
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
        $msSection += $resp.Content.Trim()
      }
    }
  } catch {
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
  } catch {
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

  $bomPath = Invoke-CycloneDX -ProjectPath $projectPath -OutDir $manifestPath
  Write-Information "[CycloneDX] SBOM generated at: $bomPath"

  $components = Read-Component -BomPath $bomPath
  Write-Information ("[Info] Components loaded: {0}" -f $components.Count)

  $outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }
  Write-Notice -OutputPath $outputPath -components $components
  Write-Information "[Done] THIRD-PARTY-NOTICES written to: $outputPath"
}
finally {
  Pop-Location
}
