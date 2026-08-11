#Requires -Version 7.0

function Test-ExactObjectPropertySet {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedNames
    )

    $actualNames = @($Value.PSObject.Properties.Name)
    return $actualNames.Count -eq $ExpectedNames.Count -and
    @($actualNames | Where-Object { $_ -cnotin $ExpectedNames }).Count -eq 0
}

function Get-LegacyNuGetCleanupPlan {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApplicationRoot,

        [Parameter(Mandatory = $true)]
        [string]$NuGetPluginRoot,

        [Parameter(Mandatory = $true)]
        [string]$OwnershipManifestPath,

        [Parameter(Mandatory = $true)]
        [System.StringComparer]$PathComparer,

        [Parameter(Mandatory = $true)]
        [System.StringComparison]$PathComparison
    )

    if (-not (Test-Path -LiteralPath $ApplicationRoot -PathType Container)) {
        throw 'The legacy deployment application payload is unavailable.'
    }
    $rootExists = Test-Path -LiteralPath $NuGetPluginRoot
    if ($rootExists -and
        -not (Test-Path -LiteralPath $NuGetPluginRoot -PathType Container)) {
        throw 'The legacy NuGet plugin root is not a directory.'
    }

    $rootPrefix = $NuGetPluginRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $files = [System.Collections.Generic.List[object]]::new()
    $directories = [System.Collections.Generic.HashSet[string]]::new($PathComparer)
    $sourceFiles = if ($rootExists) {
        @(Get-ChildItem -LiteralPath $ApplicationRoot -File -Recurse -Force)
    }
    else {
        @()
    }
    foreach ($sourceFile in $sourceFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $ApplicationRoot,
            $sourceFile.FullName
        )
        $targetPath = [System.IO.Path]::GetFullPath(
            (Join-Path $NuGetPluginRoot $relativePath)
        )
        if (-not $targetPath.StartsWith($rootPrefix, $PathComparison)) {
            throw 'The legacy NuGet plugin payload inventory escaped its recorded root.'
        }
        if (Test-Path -LiteralPath $targetPath -PathType Container) {
            throw "The legacy NuGet plugin payload path '$targetPath' drifted to a directory."
        }
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            continue
        }

        $targetFile = Get-Item -LiteralPath $targetPath -Force
        $sourceHash = (
            Get-FileHash -Algorithm SHA256 -LiteralPath $sourceFile.FullName
        ).Hash
        $targetHash = (
            Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath
        ).Hash
        if ($sourceFile.Length -ne $targetFile.Length -or $sourceHash -cne $targetHash) {
            throw "The legacy NuGet plugin payload path '$targetPath' has drifted."
        }
        $files.Add(
            [pscustomobject]@{
                Path         = $targetPath
                Content      = [System.IO.File]::ReadAllBytes($targetPath)
                UnixFileMode = if ($IsWindows) {
                    $null
                }
                else {
                    [int][System.IO.File]::GetUnixFileMode($targetPath)
                }
            }
        )

        $directory = [System.IO.Path]::GetDirectoryName($targetPath)
        while (-not [string]::IsNullOrWhiteSpace($directory) -and
            -not $directory.Equals($NuGetPluginRoot, $PathComparison)) {
            $directories.Add($directory) | Out-Null
            $directory = [System.IO.Path]::GetDirectoryName($directory)
        }
    }

    $markerPath = Join-Path $NuGetPluginRoot '.azureauth-credprovider.nuget-plugin-layout'
    if (Test-Path -LiteralPath $markerPath -PathType Container) {
        throw 'The legacy NuGet plugin marker drifted to a directory.'
    }
    if (Test-Path -LiteralPath $OwnershipManifestPath -PathType Container) {
        throw 'The legacy NuGet ownership manifest drifted to a directory.'
    }
    $markerExists = Test-Path -LiteralPath $markerPath -PathType Leaf
    $manifestExists = Test-Path -LiteralPath $OwnershipManifestPath -PathType Leaf
    if ($markerExists -ne $manifestExists) {
        throw 'The legacy NuGet marker and ownership manifest are inconsistent.'
    }
    if ($markerExists) {
        $legacyMarker = (
            "azureauth-credprovider nuget-plugin-layout`n" +
            "phase=10`n" +
            "runtime=netcore`n" +
            "entrypoint=azureauth-credprovider.dll`n"
        )
        if ((Get-Content -LiteralPath $markerPath -Raw) -cne $legacyMarker) {
            throw 'The legacy NuGet plugin marker is not recognized.'
        }

        $manifestJson = Get-Content -LiteralPath $OwnershipManifestPath -Raw
        $manifest = $manifestJson | ConvertFrom-Json
        if (-not (Test-ExactObjectPropertySet `
                    -Value $manifest `
                    -ExpectedNames @(
                    'schemaVersion',
                    'manifestId',
                    'ownerProductId',
                    'scope',
                    'entrySelector',
                    'productVersion',
                    'safeMetadata',
                    'entries'
                ))) {
            throw 'The legacy NuGet ownership manifest is not recognized.'
        }
        $entries = @($manifest.entries)
        if ($manifest.schemaVersion -ne 1 -or
            $manifest.manifestId -cne 'phase10-nuget-plugin-layout' -or
            $manifest.ownerProductId -cne 'azureauth-credprovider' -or
            $manifest.scope -cne 'user' -or
            $manifest.entrySelector -cne 'nuget.plugin-layout' -or
            $manifest.productVersion -cne 'phase10' -or
            @($manifest.safeMetadata.PSObject.Properties).Count -ne 0 -or
            $entries.Count -ne 1 -or
            -not (Test-ExactObjectPropertySet `
                    -Value $entries[0] `
                    -ExpectedNames @(
                    'sequence',
                    'targetKind',
                    'targetPathOrName',
                    'key'
                )) -or
            $entries[0].sequence -ne 1 -or
            $entries[0].targetKind -cne 'nuGetPluginLayout' -or
            -not ([System.IO.Path]::GetFullPath(
                    [string]$entries[0].targetPathOrName
                )).Equals($NuGetPluginRoot, $PathComparison) -or
            $entries[0].key -cne 'physical-target') {
            throw 'The legacy NuGet ownership manifest is not recognized.'
        }

        foreach ($metadataPath in @($markerPath, $OwnershipManifestPath)) {
            $files.Add(
                [pscustomobject]@{
                    Path         = $metadataPath
                    Content      = [System.IO.File]::ReadAllBytes($metadataPath)
                    UnixFileMode = if ($IsWindows) {
                        $null
                    }
                    else {
                        [int][System.IO.File]::GetUnixFileMode($metadataPath)
                    }
                }
            )
        }
    }

    return [pscustomobject]@{
        Files       = @($files)
        Directories = @($directories)
        Root        = $NuGetPluginRoot
    }
}

function Remove-LegacyNuGetPayload {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Plan
    )

    try {
        foreach ($file in $Plan.Files) {
            if ((Test-Path -LiteralPath $file.Path -PathType Leaf) -and
                $PSCmdlet.ShouldProcess($file.Path, 'Remove legacy NuGet payload file')) {
                Remove-Item -LiteralPath $file.Path -Force
            }
        }
        foreach ($directory in @($Plan.Directories | Sort-Object Length -Descending)) {
            if ((Test-Path -LiteralPath $directory -PathType Container) -and
                @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0 -and
                $PSCmdlet.ShouldProcess($directory, 'Remove empty legacy NuGet directory')) {
                Remove-Item -LiteralPath $directory -Force
            }
        }
        if ((Test-Path -LiteralPath $Plan.Root -PathType Container) -and
            @(Get-ChildItem -LiteralPath $Plan.Root -Force).Count -eq 0 -and
            $PSCmdlet.ShouldProcess($Plan.Root, 'Remove empty legacy NuGet root')) {
            Remove-Item -LiteralPath $Plan.Root -Force
        }
    }
    catch {
        $cleanupFailure = $_
        $rollbackFailures = [System.Collections.Generic.List[System.Exception]]::new()
        try {
            New-Item -ItemType Directory -Path $Plan.Root -Force | Out-Null
            foreach ($directory in @($Plan.Directories | Sort-Object Length)) {
                New-Item -ItemType Directory -Path $directory -Force | Out-Null
            }
            foreach ($file in $Plan.Files) {
                $parentPath = Split-Path -Parent $file.Path
                if (-not [string]::IsNullOrWhiteSpace($parentPath)) {
                    New-Item -ItemType Directory -Path $parentPath -Force | Out-Null
                }
                [System.IO.File]::WriteAllBytes($file.Path, $file.Content)
                if ($null -ne $file.UnixFileMode) {
                    [System.IO.File]::SetUnixFileMode(
                        $file.Path,
                        [System.IO.UnixFileMode][int]$file.UnixFileMode
                    )
                }
            }
        }
        catch {
            $rollbackFailures.Add($_.Exception)
        }

        if ($rollbackFailures.Count -gt 0) {
            $failures = [System.Collections.Generic.List[System.Exception]]::new()
            $failures.Add($cleanupFailure.Exception)
            foreach ($failure in $rollbackFailures) {
                $failures.Add($failure)
            }
            throw [System.AggregateException]::new(
                'Legacy NuGet cleanup failed and its validated state could not be restored.',
                $failures
            )
        }

        throw $cleanupFailure
    }
}
