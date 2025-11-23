# PNG test suite download and extraction script
$url = "http://www.schaik.com/pngsuite/PngSuite-2017jul19.zip"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetDir = Join-Path $scriptDir "testsuite1"
$tempZipFile = Join-Path $env:TEMP "PngSuite-2017jul19.zip"

Write-Host "Starting PNG test suite download..."
Write-Host "URL: $url"
Write-Host "Target directory: $targetDir"

# Create target directory
if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Host "Created directory: $targetDir"
} else {
    Write-Host "Directory already exists: $targetDir"
}

try {
    # Download ZIP file
    Write-Host "Downloading file..."
    Invoke-WebRequest -Uri $url -OutFile $tempZipFile -UseBasicParsing
    Write-Host "Download completed: $tempZipFile"

    # Extract to target directory
    Write-Host "Extracting to: $targetDir"
    Expand-Archive -Path $tempZipFile -DestinationPath $targetDir -Force
    Write-Host "Extraction completed"

    # Display extracted file count
    $fileCount = (Get-ChildItem -Path $targetDir -Recurse -File | Measure-Object).Count
    Write-Host "Extracted $fileCount files to $targetDir"

} catch {
    Write-Error "Operation failed: $($_.Exception.Message)"
    exit 1
} finally {
    # Clean up temporary file
    if (Test-Path $tempZipFile) {
        Remove-Item $tempZipFile -Force
        Write-Host "Cleaned up temporary file: $tempZipFile"
    }
}

Write-Host "PNG test suite download and extraction completed!"
