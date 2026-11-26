# Builds the release package: release/VPNBypass/ and release/VPNBypass-v<version>.zip
# -SelfContained bundles the .NET runtime (bigger zip, nothing to install for users).
param([string]$Version = "2.1.0", [switch]$SelfContained)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "release\VPNBypass"
$zip = Join-Path $root "release\VPNBypass-v$Version.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish (Join-Path $root "src\VpnBypass\VpnBypass.csproj") `
    -c Release -r win-x64 --self-contained $SelfContained.IsPresent `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$($SelfContained.IsPresent) `
    -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Copy-Item (Join-Path $root "STOP_AND_CLEAR.bat") $out
Copy-Item (Join-Path $root "README.txt") $out
Copy-Item (Join-Path $root "LICENSE") $out

Compress-Archive -Path $out -DestinationPath $zip
Write-Host "Package ready: $zip"
