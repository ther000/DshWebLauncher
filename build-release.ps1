param(
    [ValidateSet('Lightweight', 'Standalone', 'All')]
    [string]$Mode = 'All'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\DshWebLauncher\DshWebLauncher.csproj'
$outputRoot = Join-Path $PSScriptRoot 'artifacts\release'

Remove-Item $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $outputRoot -ItemType Directory -Force | Out-Null

dotnet test (Join-Path $PSScriptRoot 'tests\DshWebLauncher.Tests\DshWebLauncher.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw '测试失败，已停止发布。' }

function Publish-Package([string]$name, [bool]$selfContained) {
    $directory = Join-Path $outputRoot $name
    dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $directory
    if ($LASTEXITCODE -ne 0) { throw "$name 发布失败。" }

    $zip = Join-Path $outputRoot "$name.zip"
    Compress-Archive -Path (Join-Path $directory '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = Get-FileHash $zip -Algorithm SHA256
    "$($hash.Hash)  $($hash.Path | Split-Path -Leaf)" | Set-Content (Join-Path $outputRoot "$name.sha256") -Encoding ascii
}

if ($Mode -in @('Lightweight', 'All')) { Publish-Package 'DshWebLauncher-win-x64-lightweight' $false }
if ($Mode -in @('Standalone', 'All')) { Publish-Package 'DshWebLauncher-win-x64-standalone' $true }

Get-ChildItem $outputRoot | Select-Object Name, Length, LastWriteTime
