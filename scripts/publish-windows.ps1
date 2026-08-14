[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $outputRoot $Runtime
$archivePath = Join-Path $outputRoot "FFXIVConfigManager-$Runtime.zip"
$project = Join-Path $repositoryRoot "src/FFXIVConfigManager.Desktop/FFXIVConfigManager.Desktop.csproj"

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

New-Item $publishDirectory -ItemType Directory -Force | Out-Null

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出代码：$LASTEXITCODE"
}

Get-ChildItem $publishDirectory -Filter "*.pdb" -Recurse | Remove-Item -Force

if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "发布完成：$archivePath"
