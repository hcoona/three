# PNG test suite download and extraction script
$url = "http://www.schaik.com/pngsuite/PngSuite-2017jul19.zip"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetDir = Join-Path $scriptDir "testsuite1"
$tempZipFile = Join-Path $env:TEMP "PngSuite-2017jul19.zip"

Write-Output "Starting PNG test suite download..."
Write-Output "URL: $url"
Write-Output "Target directory: $targetDir"

# Create target directory
if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Output "Created directory: $targetDir"
}
else {
    Write-Output "Directory already exists: $targetDir"
}

try {
    # Download ZIP file
    Write-Output "Downloading file..."
    Invoke-WebRequest -Uri $url -OutFile $tempZipFile -UseBasicParsing
    Write-Output "Download completed: $tempZipFile"

    # Extract to target directory
    Write-Output "Extracting to: $targetDir"
    Expand-Archive -Path $tempZipFile -DestinationPath $targetDir -Force
    Write-Output "Extraction completed"

    # Display extracted file count
    $fileCount = (Get-ChildItem -Path $targetDir -Recurse -File | Measure-Object).Count
    Write-Output "Extracted $fileCount files to $targetDir"

}
catch {
    Write-Error "Operation failed: $($_.Exception.Message)"
    exit 1
}
finally {
    # Clean up temporary file
    if (Test-Path $tempZipFile) {
        Remove-Item $tempZipFile -Force
        Write-Output "Cleaned up temporary file: $tempZipFile"
    }
}

Write-Output "PNG test suite download and extraction completed!"
