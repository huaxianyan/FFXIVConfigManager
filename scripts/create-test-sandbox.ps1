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
        throw "角色目录不存在：$path"
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

Write-Host "测试沙盒已创建：$SandboxRoot"
Write-Host "请将该目录作为自定义配置源添加至 FFXIVConfigManager。"
Write-Host "源角色：FFXIV_CHR1111111111111111"
Write-Host "目标角色：FFXIV_CHR2222222222222222"
