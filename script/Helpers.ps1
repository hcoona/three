<#
.COPYRIGHT
    Copyright (C) 2025 Shuai Zhang

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

.SYNOPSIS
  Shared helper functions for ImageOcclusionEditor build and packaging scripts.

.NOTES
  This file is intended to be dot-sourced by other scripts in the same folder:
    . "$PSScriptRoot/Build-Helpers.ps1"
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $global:PSNativeCommandUseErrorActionPreference = $true
}

function Write-Status {
  param(
    [Parameter(Mandatory)][string]$Message,
    [ValidateSet('Info','Warn','Error','Success')]
    [string]$Level = 'Info'
  )
  switch ($Level) {
    'Info'    { Write-Information "[>] $Message" -InformationAction Continue }
    'Warn'    { Write-Warning     "[!] $Message" }
    'Error'   { Write-Error       "[x] $Message" }
    'Success' { Write-Information "[OK] $Message" -InformationAction Continue }
  }
}

function Get-RepoRoot {
  <# Returns repository root directory assuming this file is under repoRoot/script #>
  return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

function Get-ProjectInfo {
  <#
  .SYNOPSIS
    Reads basic project info from a csproj: TargetFramework, AssemblyName, RuntimeIdentifier(s).
  .PARAMETER CsprojPath
    Path to the csproj file.
  .PARAMETER DefaultAssemblyName
    Fallback if AssemblyName is not present. Default: ImageOcclusionEditor
  .PARAMETER DefaultRid
    Optional fallback RID if not declared in project. If not provided, returns $null when not found.
  .OUTPUTS
    PSCustomObject @{ TargetFramework; AssemblyName; RuntimeIdentifier }
  #>
  param(
    [Parameter(Mandatory)][string]$CsprojPath,
    [string]$DefaultAssemblyName = 'ImageOcclusionEditor',
    [string]$DefaultRid
  )
  if (-not (Test-Path -LiteralPath $CsprojPath -PathType Leaf)) {
    throw "Project file not found: $CsprojPath"
  }
  [xml]$projXml = Get-Content -LiteralPath $CsprojPath -Raw
  $tfmNode = $projXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
  $tfm = if ($tfmNode) { $tfmNode.InnerText } else { $null }
  if ([string]::IsNullOrWhiteSpace($tfm)) { throw 'TargetFramework not found in csproj.' }

  $asmNode = $projXml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
  $asm = if ($asmNode) { $asmNode.InnerText } else { $null }
  if ([string]::IsNullOrWhiteSpace($asm)) { $asm = $DefaultAssemblyName }

  $ridNode = $projXml.SelectSingleNode('/Project/PropertyGroup/RuntimeIdentifier')
  $rid = if ($ridNode) { $ridNode.InnerText } else { $null }
  if ([string]::IsNullOrWhiteSpace($rid)) {
    $ridsNode = $projXml.SelectSingleNode('/Project/PropertyGroup/RuntimeIdentifiers')
    if ($ridsNode) {
      $rids = @($ridsNode.InnerText -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
      if ($rids.Count -gt 0) { $rid = $rids[0] }
    }
  }
  if ([string]::IsNullOrWhiteSpace($rid) -and $DefaultRid) { $rid = $DefaultRid }

  [pscustomobject]@{
    TargetFramework    = $tfm
    AssemblyName       = $asm
    RuntimeIdentifier  = $rid
  }
}

function Get-PublishOutputPath {
  <#
  .SYNOPSIS
    Builds the publish output path layout used by Publish-ImageOcclusionEditor.ps1
  .DESCRIPTION
    out/ImageOcclusionEditor/<Configuration>/<TFM>/<RID>/
  #>
  param(
    [Parameter(Mandatory)][string]$PublishOutputRoot,
    [Parameter(Mandatory)][string]$Configuration,
    [Parameter(Mandatory)][string]$TargetFramework,
    [Parameter(Mandatory)][string]$RuntimeIdentifier
  )
  $root = Join-Path $PublishOutputRoot 'ImageOcclusionEditor'
  $path = Join-Path $root $Configuration
  $path = Join-Path $path $TargetFramework
  $path = Join-Path $path $RuntimeIdentifier
  return $path
}

function Get-ISCCPath {
  <# Locates Inno Setup compiler (ISCC.exe) from hint, PATH or common install paths. #>
  param([string]$Hint)
  if ($Hint) {
    if (Test-Path -LiteralPath $Hint) { return (Resolve-Path -LiteralPath $Hint).Path }
    throw "Inno Setup compiler not found at: $Hint"
  }
  $cmd = Get-Command -Name 'iscc','iscc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cmd) { return $cmd.Source }
  $candidates = @(
    "$($env:LOCALAPPDATA)\Programs\Inno Setup 6\ISCC.exe",
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
  )
  foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
  throw 'Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -InnoSetupCompiler.'
}

function Invoke-CycloneDX {
  <#
  .SYNOPSIS
    Run CycloneDX to generate SBOM for a given project and return bom.json path.
  .PARAMETER ProjectPath
    Path to the .csproj or solution to analyze.
  .PARAMETER OutDir
    Output directory where bom.json will be written.
  .OUTPUTS
    Full path to bom.json
  #>
  param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$OutDir
  )
  # Ensure output directory exists
  if (-not (Test-Path -LiteralPath $OutDir)) {
    [void](New-Item -ItemType Directory -Path $OutDir)
  }

  $cdxArguments = @(
    $ProjectPath,
    '-o', $OutDir,
    '--exclude-dev',
    '--exclude-test-projects',
    '--output-format', 'Json'
  )

  $cmds = @(
    @{ File = 'dotnet-CycloneDX'; Arguments = $cdxArguments },
    @{ File = 'dotnet'; Arguments = @('CycloneDX') + $cdxArguments }
  )

  $succeeded = $false
  foreach ($c in $cmds) {
    try {
      Write-Information "[CycloneDX] Running: $($c.File) $($c.Arguments -join ' ')" -InformationAction Continue
      & $c.File @($c.Arguments) | Out-Null
      $exit = $LASTEXITCODE
      if ($exit -eq 0) { $succeeded = $true; break }
      Write-Warning "Command exited with code $exit. Trying fallback if available..."
    }
    catch {
      Write-Warning "Failed to run $($c.File): $($_.Exception.Message)"
    }
  }

  if (-not $succeeded) {
    throw "Failed to run CycloneDX CLI. Ensure 'dotnet tool install --global CycloneDX' has been executed."
  }

  $bomPath = Join-Path $OutDir 'bom.json'
  if (-not (Test-Path -LiteralPath $bomPath)) {
    throw "SBOM not found: $bomPath"
  }
  return (Resolve-Path -LiteralPath $bomPath).Path
}
