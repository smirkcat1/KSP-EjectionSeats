# Builds a release folder and a SpaceDock-ready zip:  release\EjectionSeats-<version>\ and EjectionSeats-<version>.zip
# Usage:  powershell -ExecutionPolicy Bypass -File tools\make_release.ps1 [-KspDir "C:\path\to\Kerbal Space Program"]
param([string]$KspDir = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program")
$ErrorActionPreference = "Stop"
$VERSION = "1.2.0"             # keep in step with Source\AssemblyInfo.cs
$KSP_VERSION = "1.12.5"; $KSP_MIN = "1.12.0"; $KSP_MAX = "1.12.99"

$root = Split-Path $PSScriptRoot -Parent
$name = "EjectionSeats-$VERSION"
$rel = Join-Path $root "release"
$stage = Join-Path $rel $name

# fresh build
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1") -KspDir $KspDir
if ($LASTEXITCODE -ne 0) { throw "build failed" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "GameData") | Out-Null
$gd = Join-Path $stage "GameData\EjectionSeats"
Copy-Item (Join-Path $root "GameData\EjectionSeats") $gd -Recurse
# nothing from testing goes out
Remove-Item (Join-Path $gd "PluginData") -Recurse -Force -ErrorAction SilentlyContinue

# release defaults: no debug logging
$cfg = Join-Path $gd "Settings.cfg"
$c = [IO.File]::ReadAllText($cfg)
$c = [Regex]::Replace($c, '(?m)^(\s*debug\s*=\s*)true', '${1}false')
[IO.File]::WriteAllText($cfg, $c)

# KSP-AVC version file
function V($s) { $p = $s.Split('.'); return '{ "MAJOR": ' + $p[0] + ', "MINOR": ' + $p[1] + ', "PATCH": ' + $p[2] + ' }' }
$vp = $VERSION.Split('.')
$avc = "{`r`n" +
       '  "NAME": "Ejection Seats",' + "`r`n" +
       '  "VERSION": { "MAJOR": ' + $vp[0] + ', "MINOR": ' + $vp[1] + ', "PATCH": ' + $vp[2] + ', "BUILD": 0 },' + "`r`n" +
       '  "KSP_VERSION": ' + (V $KSP_VERSION) + ",`r`n" +
       '  "KSP_VERSION_MIN": ' + (V $KSP_MIN) + ",`r`n" +
       '  "KSP_VERSION_MAX": ' + (V $KSP_MAX) + "`r`n}`r`n"
[IO.File]::WriteAllText((Join-Path $gd "EjectionSeats.version"), $avc)

Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $gd "CHANGELOG.md")
Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $stage "CHANGELOG.md")
Copy-Item (Join-Path $root "tools\spacedock_listing.md") (Join-Path $stage "SPACEDOCK_LISTING.md")
foreach ($lic in "LICENSE", "LICENSE.txt", "LICENSE.md") {
    $lp = Join-Path $root $lic
    if (Test-Path $lp) { Copy-Item $lp (Join-Path $gd $lic); Copy-Item $lp (Join-Path $stage $lic) }
}

# zip with forward-slash paths (Compress-Archive on Windows PowerShell writes backslashes, which breaks
# extraction on Linux/macOS and CKAN). Contents: GameData/EjectionSeats/..., plus CHANGELOG and LICENSE.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = Join-Path $rel "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$z = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @(Get-ChildItem (Join-Path $stage "GameData") -Recurse -File) +
             @(Get-ChildItem $stage -File | Where-Object { $_.Name -in "CHANGELOG.md", "LICENSE" })
    foreach ($f in $files) {
        $entry = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $z.Dispose() }
Copy-Item $zip (Join-Path $stage "$name.zip")
"Release folder: $stage"
"Zip:            $zip (" + [Math]::Round((Get-Item $zip).Length / 1KB) + " KB)"
