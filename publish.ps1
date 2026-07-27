# Builds the self-contained, single-file WiiFitToVRC.exe and copies it to the repo root.
# Run this after making changes, before committing an updated exe.
#
# Usage: powershell -File publish.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$outDir = Join-Path $root "publish_temp"

dotnet publish (Join-Path $root "src\WiiFitToVRC.App\WiiFitToVRC.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outDir

Copy-Item (Join-Path $outDir "WiiFitToVRC.App.exe") (Join-Path $root "WiiFitToVRC.exe") -Force
Remove-Item $outDir -Recurse -Force

Write-Host "Published WiiFitToVRC.exe to $root"
