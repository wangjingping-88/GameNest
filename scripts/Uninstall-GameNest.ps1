[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [ValidateSet('Ask', 'Keep', 'Remove')]
    [string]$DataAction = 'Ask'
)

$ErrorActionPreference = 'Stop'
$portableRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$markerPath = Join-Path $portableRoot '.gamenest-portable-root'
$appPath = Join-Path $portableRoot 'GameNest.App.exe'

if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw '安全检查失败：当前目录不是完整的 GameNest 便携版目录。'
}

$runningApp = Get-Process -Name 'GameNest.App' -ErrorAction SilentlyContinue
if ($runningApp) {
    throw '请先关闭 GameNest，再运行卸载。'
}

$dataRoot = [IO.Path]::GetFullPath(
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'GameNest'))

if ($DataAction -eq 'Ask') {
    Write-Host ''
    Write-Host '卸载 GameNest 前，请选择如何处理本地数据：'
    Write-Host '  1. 保留数据库、自动备份、日志和封面缓存（默认）'
    Write-Host '  2. 删除数据库、自动备份、日志和封面缓存'
    $answer = Read-Host '请输入 1 或 2'
    $DataAction = if ($answer -eq '2') { 'Remove' } else { 'Keep' }
}

if ($DataAction -eq 'Remove') {
    Write-Host "已选择删除本地数据：$dataRoot"
    if ((Test-Path -LiteralPath $dataRoot) -and
        $PSCmdlet.ShouldProcess($dataRoot, '删除 GameNest 数据库、备份、日志和封面缓存')) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
}
else {
    Write-Host "已选择保留本地数据：$dataRoot"
}

Write-Host "准备删除便携版程序目录：$portableRoot"
if ($PSCmdlet.ShouldProcess($portableRoot, '删除 GameNest 便携版程序目录')) {
    Set-Location ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $portableRoot -Recurse -Force
}
