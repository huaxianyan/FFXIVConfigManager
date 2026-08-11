[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceCharacterPath,

    [Parameter(Mandatory = $true)]
    [string]$TargetCharacterPath,

    [string]$SandboxRoot = (Join-Path $env:TEMP "FFXIVConfigManager-ManualTest")
)

$ErrorActionPreference = "Stop"

foreach ($path in @($SourceCharacterPath, $TargetCharacterPath)) {
    if (-not (Test-Path $path -PathType Container)) {
        throw "Character directory does not exist: $path"
    }
}

$sourceSandbox = Join-Path $SandboxRoot "FFXIV_CHR1111111111111111"
$targetSandbox = Join-Path $SandboxRoot "FFXIV_CHR2222222222222222"

if (Test-Path $SandboxRoot) {
    Remove-Item $SandboxRoot -Recurse -Force
}

New-Item $sourceSandbox -ItemType Directory -Force | Out-Null
New-Item $targetSandbox -ItemType Directory -Force | Out-Null

Get-ChildItem $SourceCharacterPath -File -Filter "*.DAT" |
    Copy-Item -Destination $sourceSandbox
Get-ChildItem $TargetCharacterPath -File -Filter "*.DAT" |
    Copy-Item -Destination $targetSandbox

Write-Host "Sandbox created: $SandboxRoot"
Write-Host "Add this directory as a custom profile in FFXIVConfigManager."
Write-Host "Source: FFXIV_CHR1111111111111111"
Write-Host "Target: FFXIV_CHR2222222222222222"
