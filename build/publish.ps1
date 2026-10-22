# Builds the release package: release/VPNBypass/ and release/VPNBypass-v<version>.zip
param([string]$Version = "2.0.0")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "release\VPNBypass"
$zip = Join-Path $root "release\VPNBypass-v$Version.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish (Join-Path $root "src\VpnBypass\VpnBypass.csproj") `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none `
    -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# win-x64 build only needs the 64-bit driver set.
Remove-Item (Join-Path $out "engine\x86") -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $root "STOP_AND_CLEAR.bat") $out
Copy-Item (Join-Path $root "README.txt") $out
Copy-Item (Join-Path $root "LICENSE") $out

Compress-Archive -Path $out -DestinationPath $zip
Write-Host "Package ready: $zip"
