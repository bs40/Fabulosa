<#
.SYNOPSIS
  Builds Fabulosa as a single self-contained Windows x64 executable.

.DESCRIPTION
  Output: publish\Fabulosa.exe (no .NET runtime install required on the target machine).
  Run from anywhere:  powershell -ExecutionPolicy Bypass -File build\publish.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish"

dotnet publish (Join-Path $root "src\Fabulosa\Fabulosa.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out

dotnet publish (Join-Path $root "tools\TestMusic\TestMusic.csproj") `
    -c $Configuration -r $Runtime --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o (Join-Path $out "tools")

Write-Host ""
Write-Host "Published:" (Join-Path $out "Fabulosa.exe")
Get-ChildItem $out -File | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
