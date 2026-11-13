#requires -version 5
<#
  DiscImageStudio 预设分享工具 —— 一键编译
  用 Windows 自带的 .NET Framework 4.x 编译器，无需安装任何 SDK。
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe，请确认系统里装了 .NET Framework 4.x"
}

$outDir = Join-Path $root 'dist'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$exe  = Join-Path $outDir 'DiscImageStudio预设工具.exe'
$src  = Join-Path $root 'src\PresetTool.cs'
$icon = Join-Path $root 'assets\DiscImageStudio导出工具.ico'

if (Test-Path $exe) {
    try { Remove-Item $exe -Force } catch { throw "请先关闭正在运行的工具再编译" }
}

Write-Host "编译中..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /codepage:65001 "/win32icon:$icon" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll `
    "/out:$exe" $src

if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host "完成：$exe（$size KB）" -ForegroundColor Green
