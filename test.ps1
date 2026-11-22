# Rebuilds the mod, copies it into the lightweight test install (stock + ModuleManager + BDArmory) and launches KSP.
param([string]$TestDir = "C:\KSP_EjectionTest")
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build.ps1") -KspDir $TestDir -Install
Start-Process -FilePath (Join-Path $TestDir "KSP_x64.exe") -WorkingDirectory $TestDir
Write-Host "KSP launched from $TestDir - log: $TestDir\KSP.log"
