param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $Directory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$Repository = [IO.Path]::GetFullPath($Repository)
$Directory = [IO.Path]::GetFullPath($Directory)
if (Test-Path $Directory) { throw 'Control bootstrap state already exists.' }
$transfer = Join-Path $Directory 'transfer'
New-Item -ItemType Directory -Path (Join-Path $transfer 'control') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $transfer 'ruby') | Out-Null
Push-Location $Repository
try {
    $project = 'src/private/app/workflow-delivery/WorkflowDelivery.csproj'
    $properties = @('-property:Configuration=Debug', '-property:ContinuousIntegrationBuild=true',
        '-property:RestoreLockedMode=true', '-property:NuGetInteractive=false')
    dotnet restore $project --locked-mode --configfile (Join-Path $Repository 'nuget.config') @properties `
        "-binaryLogger:$Directory/candidate-control-restore.binlog"
    $target = dotnet msbuild $project -nologo -noAutoResponse -target:Build -getProperty:TargetPath `
        @properties "-binaryLogger:$Directory/candidate-control.binlog"
    $target | Set-Content (Join-Path $Directory 'candidate-control.stdout') -Encoding utf8
    $target = ($target -join "`n").Trim()
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Missing native TargetPath output.' }
    Copy-Item (Join-Path (Split-Path $target) '*') (Join-Path $transfer 'control') -Recurse
    git archive --format=zip "--output=$transfer/python-control.zip" HEAD `
        src/private/app/workflow-delivery/Native/Python
    $helper = git show HEAD:src/private/app/workflow-delivery/Native/Ruby/facts.rb
    [IO.File]::WriteAllText((Join-Path $transfer 'ruby/facts.rb'), ($helper -join "`n") + "`n",
        [Text.UTF8Encoding]::new($false))
    dotnet (Join-Path $transfer 'control/WorkflowDelivery.dll') ci caller-dotnet bootstrap `
        $Repository $Directory $transfer
}
finally { Pop-Location }
