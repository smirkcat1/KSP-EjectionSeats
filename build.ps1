# Builds GameData/EjectionSeats/Plugins/EjectionSeats.dll against your KSP install.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-KspDir "C:\path\to\Kerbal Space Program"] [-Install]
param(
    [string]$KspDir = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$managed = Join-Path $KspDir "KSP_x64_Data\Managed"
if (-not (Test-Path $managed)) { throw "KSP Managed folder not found: $managed" }

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root "GameData\EjectionSeats\Plugins"
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll") +
        (Get-ChildItem $managed -Filter "UnityEngine*.dll" | ForEach-Object { $_.Name })
$refArgs = $refs | ForEach-Object { "/r:`"$(Join-Path $managed $_)`"" }
$sources = Get-ChildItem (Join-Path $root "Source") -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }

$cmd = "/nologo /noconfig /nostdlib /target:library /optimize+ /warn:4 /nowarn:1591,0618 /out:`"$(Join-Path $out 'EjectionSeats.dll')`" " + ($refArgs -join " ") + " " + ($sources -join " ")
$p = Start-Process -FilePath $csc -ArgumentList $cmd -NoNewWindow -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Build failed" }
Write-Host "Built $(Join-Path $out 'EjectionSeats.dll')"

if ($Install) {
    $dest = Join-Path $KspDir "GameData\EjectionSeats"
    Copy-Item (Join-Path $root "GameData\EjectionSeats") (Join-Path $KspDir "GameData") -Recurse -Force
    Write-Host "Installed to $dest"
}
