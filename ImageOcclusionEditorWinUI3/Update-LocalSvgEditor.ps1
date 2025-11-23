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
    Download and update local SVG Editor files

.DESCRIPTION
    This script downloads the svgedit package from the NPM registry, extracts it, and replaces the svg-edit folder in the current directory.
    The script performs the following steps:
    1. Download svgedit-7.3.8.tgz file
    2. Use 7-Zip to extract the tgz file (requires two extractions)
    3. Extract /package/dist/editor directory
    4. Replace the svg-edit folder in the current directory

.PARAMETER Version
    The svgedit version to download. Default is "7.3.8"
.PARAMETER Force
    Force the operation, overwrite even if the target directory exists
.PARAMETER KeepDownload
    Keep downloaded files, do not delete temporary files

.EXAMPLE
    .\Update-LocalSvgEditor.ps1
    Update SVG Editor with default version 7.3.8
.EXAMPLE
    .\Update-LocalSvgEditor.ps1 -Version "7.4.0" -Force
    Force update SVG Editor with version 7.4.0
.EXAMPLE
    .\Update-LocalSvgEditor.ps1 -KeepDownload -Verbose
    Update SVG Editor and keep downloaded files, show verbose output

.NOTES
    Author: ImageOcclusionEditor
    Version: 1.0
    Created: 2025-08-01
    Requirements:
    - 7-Zip must be installed and available in PATH
    - Internet connection required to download files
    This script is written for PowerShell 5.0+

.LINK
    https://registry.npmjs.org/svgedit/
    https://7-zip.opensource.jp/chm/cmdline/commands/extract_full.htm
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(HelpMessage = "svgedit version to download")]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = "7.3.8",

    [Parameter(HelpMessage = "Force operation, overwrite existing files")]
    [switch]$Force,

    [Parameter(HelpMessage = "Keep downloaded temporary files")]
    [switch]$KeepDownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Get script directory
$ScriptRoot = $PSScriptRoot
if (-not $ScriptRoot) {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

Write-Verbose "Script running directory: $ScriptRoot"

# Define variables
$DownloadUrl = "https://registry.npmjs.org/svgedit/-/svgedit-$Version.tgz"
$TempDir = Join-Path $env:TEMP "svgedit-update-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$TgzFile = Join-Path $TempDir "svgedit-$Version.tgz"
$TarFile = Join-Path $TempDir "svgedit-$Version.tar"
$ExtractDir = Join-Path $TempDir "extracted"
$TargetDir = Join-Path $ScriptRoot "svg-edit"

function Test-SevenZip {
    <#
    .SYNOPSIS
    Check if 7-Zip is available
    #>
    try {
        $null = Get-Command "7z" -ErrorAction Stop
        Write-Verbose "7-Zip found and available"
        return $true
    }
    catch {
        Write-Error "7-Zip not found. Please make sure 7-Zip is installed and in the PATH environment variable."
        return $false
    }
}

function New-TempDirectory {
    <#
    .SYNOPSIS
    Create temporary directory
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    try {
        if (Test-Path $Path) {
            if ($PSCmdlet.ShouldProcess($Path, "Remove existing directory")) {
                Write-Verbose "Temporary directory exists, cleaning: $Path"
                Remove-Item $Path -Recurse -Force
            }
        }

        if ($PSCmdlet.ShouldProcess($Path, "Create temporary directory")) {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
            Write-Verbose "Temporary directory created: $Path"
        }
    }
    catch {
        throw "Failed to create temporary directory '$Path': $($_.Exception.Message)"
    }
}

function Get-SvgEditPackage {
    <#
    .SYNOPSIS
    Download svgedit package file
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Url,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$OutputPath
    )

    try {
        Write-Information "Downloading svgedit-$Version.tgz..." -InformationAction Continue
        Write-Verbose "Download URL: $Url"
        Write-Verbose "Save to: $OutputPath"

        $webRequest = @{
            Uri = $Url
            OutFile = $OutputPath
            UseBasicParsing = $true
            TimeoutSec = 300
        }
        Invoke-WebRequest @webRequest

        if (-not (Test-Path $OutputPath)) {
            throw "Downloaded file not found at expected location: $OutputPath"
        }

        $fileInfo = Get-Item $OutputPath
        if ($fileInfo.Length -eq 0) {
            throw "Downloaded file is empty: $OutputPath"
        }

        Write-Verbose "Download complete, file size: $($fileInfo.Length) bytes"
    }
    catch {
        throw "Download failed: $($_.Exception.Message)"
    }
}

function Expand-TgzFile {
    <#
    .SYNOPSIS
    Extract tgz file (requires two extractions)
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateScript({Test-Path $_ -PathType Leaf})]
        [string]$TgzPath,

        [Parameter(Mandatory)]
        [string]$TarPath,

        [Parameter(Mandatory)]
        [string]$ExtractPath
    )

    try {
        Write-Information "Extracting tgz file to tar..." -InformationAction Continue
        Write-Verbose "Command: 7z x `"$TgzPath`" -o`"$(Split-Path $TarPath)`" -y"

        $result = & 7z x "$TgzPath" -o"$(Split-Path $TarPath)" -y 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "7z extraction output: $($result -join "`n")"
            throw "7z extraction of tgz failed, exit code: $LASTEXITCODE"
        }

        Write-Verbose "tgz extraction complete"

        if (-not (Test-Path $TarPath)) {
            throw "Expected tar file not found after extraction: $TarPath"
        }

        Write-Information "Extracting tar file..." -InformationAction Continue
        Write-Verbose "Command: 7z x `"$TarPath`" -o`"$ExtractPath`" -y"

        $result = & 7z x "$TarPath" -o"$ExtractPath" -y 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "7z extraction output: $($result -join "`n")"
            throw "7z extraction of tar failed, exit code: $LASTEXITCODE"
        }

        Write-Verbose "tar extraction complete"
    }
    catch {
        throw "Extraction process failed: $($_.Exception.Message)"
    }
}
function Copy-EditorFile {
    <#
    .SYNOPSIS
    Copy editor files to target directory
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateScript({Test-Path $_ -PathType Container})]
        [string]$SourcePath,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$DestinationPath,

        [Parameter(Mandatory)]
        [bool]$ForceOverwrite
    )

    try {
        $EditorSourcePath = Join-Path $SourcePath "package\dist\editor"

        if (-not (Test-Path $EditorSourcePath)) {
            throw "package\dist\editor directory not found in extracted files: $EditorSourcePath"
        }

        Write-Verbose "Editor source directory: $EditorSourcePath"
        Write-Verbose "Target directory: $DestinationPath"

        $parentPath = Split-Path $DestinationPath -Parent
        if (-not (Test-Path $parentPath)) {
            throw "Parent directory does not exist: $parentPath"
        }

        if (Test-Path $DestinationPath) {
            if ($ForceOverwrite) {
                Write-Information "Deleting existing svg-edit directory..." -InformationAction Continue
                Remove-Item $DestinationPath -Recurse -Force -ErrorAction Stop
            }
            else {
                throw "Target directory already exists: $DestinationPath. Use -Force parameter to overwrite."
            }
        }

        Write-Information "Copying editor files..." -InformationAction Continue
        Copy-Item $EditorSourcePath $DestinationPath -Recurse -Force -ErrorAction Stop

        Write-Verbose "File copy complete"

        $copiedFiles = Get-ChildItem $DestinationPath -Recurse -File | Measure-Object
        Write-Information "$($copiedFiles.Count) files copied to svg-edit directory" -InformationAction Continue
    }
    catch {
        throw "File copy failed: $($_.Exception.Message)"
    }
}

function Remove-TempFile {
    <#
    .SYNOPSIS
    Clean up temporary files
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string]$TempPath
    )

    try {
        if (Test-Path $TempPath) {
            if ($PSCmdlet.ShouldProcess($TempPath, "Remove temporary files")) {
                Write-Verbose "Cleaning up temporary files: $TempPath"
                Remove-Item $TempPath -Recurse -Force
                Write-Verbose "Temporary files cleanup complete"
            }
        }
    }
    catch {
        Write-Warning "Warning during temporary files cleanup: $($_.Exception.Message)"
    }
}

# Main execution logic
try {
    Write-Information "Starting SVG Editor update (version: $Version)" -InformationAction Continue

    Write-Verbose "Validating prerequisites..."

    if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "User")) {
        Write-Warning "Script is running with elevated privileges. Consider running as regular user if possible."
    }

    if (-not (Test-SevenZip)) {
        return
    }

    $testFile = Join-Path $ScriptRoot "test-write-permission.tmp"
    try {
        "test" | Out-File $testFile -ErrorAction Stop
        Remove-Item $testFile -ErrorAction SilentlyContinue
        Write-Verbose "Write permissions verified for target directory"
    }
    catch {
        throw "Insufficient permissions to write to target directory: $ScriptRoot"
    }

    if ($PSCmdlet.ShouldProcess("svg-edit directory", "Update to version $Version")) {
        New-TempDirectory -Path $TempDir

        try {
            Get-SvgEditPackage -Url $DownloadUrl -OutputPath $TgzFile

            Expand-TgzFile -TgzPath $TgzFile -TarPath $TarFile -ExtractPath $ExtractDir

            Copy-EditorFile -SourcePath $ExtractDir -DestinationPath $TargetDir -ForceOverwrite $Force.IsPresent

            Write-Information "SVG Editor update complete!" -InformationAction Continue -Tags "Success"
        }
        finally {
            if (-not $KeepDownload.IsPresent) {
                Remove-TempFile -TempPath $TempDir
            }
            else {
                Write-Information "Temporary files kept at: $TempDir" -InformationAction Continue
            }
        }
    }
    else {
        Write-Information "Operation cancelled" -InformationAction Continue
    }
}
catch {
    Write-Error "Update process failed: $($_.Exception.Message)"

    if (-not $KeepDownload.IsPresent) {
        Remove-TempFile -TempPath $TempDir
    }

    exit 1
}
