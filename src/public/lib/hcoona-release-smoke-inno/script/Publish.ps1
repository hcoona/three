param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..' 'hcoona-release-smoke-inno.csproj'
dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $OutputRoot /nologo
