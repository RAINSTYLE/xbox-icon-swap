# Build: publishes the app to .\XboxIconSwap and makes a release zip in .\release
# No game files are included: the app draws the icons from the player's own game on first run.
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "XboxIconSwap"
$publish = Join-Path $root "obj\publish"

# Framework-dependent single file (~0.3 MB); needs the .NET 8 Desktop Runtime.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\src\XboxIconSwap" -c Release -r win-x64 -p:SelfContained=false `
    -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version -o $publish -v q -nologo
if ($LASTEXITCODE) { throw "publish failed" }

New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $publish "XboxIconSwap.exe") $out -Force
Copy-Item (Join-Path $root "README.txt") $out -Force
foreach ($json in Get-ChildItem "$root\src\XboxIconSwap\Icons" -Recurse -Filter profile.json) {
    $rel = $json.FullName.Substring("$root\src\XboxIconSwap\".Length)
    $dst = Join-Path $out $rel
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Copy-Item $json.FullName $dst -Force
}

# Release zip: exe + profiles + readme only (generated PNGs and logs are left out).
$stage = Join-Path $root "obj\stage\XboxIconSwap"
if (Test-Path (Split-Path $stage)) { Remove-Item (Split-Path $stage) -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $out "XboxIconSwap.exe"), (Join-Path $out "README.txt") $stage
Copy-Item "$root\src\XboxIconSwap\Icons" $stage -Recurse
New-Item -ItemType Directory -Force (Join-Path $root "release") | Out-Null
$zip = Join-Path $root "release\XboxIconSwap-v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Done: $out ; $zip"
