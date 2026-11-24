# Development flight test in the separate test install (C:\KSP_EjectionTest). Builds and installs the mod, adds
# the zDevFlight harness and a one-seat test craft, launches, ejects with the given settings, takes screenshots,
# then removes the harness again. Usage example:
#   powershell -ExecutionPolicy Bypass -File tools\devflight\run.ps1 -Out C:\temp\poses -Shots "0.2,0.6,1.0" -CamDist 3
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [float]$Alt = 800, [float]$Speed = 200, [float]$Duration = 6,
    [string]$Shots = "", [float]$CamDist = 1.4, [float]$CamHdg = 3.4, [float]$CamPitch = 0.25,   # front view of the kerbal
    [float]$Dump = 0, [string]$Seat = "", [float]$Kill = -1, [string]$Part = "Mark1Cockpit", [float]$PlumeExport = 0, [switch]$NoDebug,
    [string]$TestDir = "C:\KSP_EjectionTest"
)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$t = $TestDir
if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { throw "KSP is running - close it first" }
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1") -KspDir $t -Install | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$installedCfg = Join-Path $t "GameData\EjectionSeats\Settings.cfg"
if ($NoDebug) { (Get-Content $installedCfg) -replace '^(\s*debug\s*=\s*)true', '${1}false' | Set-Content $installedCfg -Encoding ascii }

# one-seat craft: the saved plane's root part swapped for a Mk1 cockpit, top-level values only
$craft = Join-Path $t "saves\test\Ships\SPH\Auto-Saved Ship.craft"
$craftLines = New-Object System.Collections.Generic.List[string]
$depth = 0; $inPart = $false; $partsSeen = 0
foreach ($l in Get-Content $craft) {
    $tr = $l.Trim()
    if (-not $inPart -and $tr -eq 'PART') { $inPart = $true; $depth = 0; $partsSeen++; if ($partsSeen -eq 1) { $craftLines.Add($l) }; continue }
    if ($inPart) {
        if ($tr -eq '{') { $depth++; if ($depth -eq 1 -and $partsSeen -eq 1) { $craftLines.Add($l) }; continue }
        if ($tr -eq '}') { if ($depth -eq 1) { if ($partsSeen -eq 1) { $craftLines.Add($l) }; $inPart = $false }; $depth--; continue }
        if ($partsSeen -eq 1 -and $depth -eq 1 -and $tr -notmatch '^(link|attN|srfN|sym) =') {
            if ($tr -match '^part = ') { $craftLines.Add("`tpart = " + $Part + "_4292993932") } else { $craftLines.Add($l) }
        }
        continue
    }
    $craftLines.Add($l)
}
Set-Content (Join-Path $t "saves\test\Ships\SPH\DevOne.craft") $craftLines -Encoding ascii

# harness
$m = Join-Path $t "KSP_x64_Data\Managed"
$refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll") + (Get-ChildItem $m -Filter "UnityEngine*.dll" | ForEach-Object { $_.Name })
$ra = ($refs | ForEach-Object { "/r:`"$m\$_`"" }) -join " "
$hd = Join-Path $t "GameData\zDevFlight"
New-Item -ItemType Directory -Force $hd | Out-Null
$cmd = "/nologo /noconfig /nostdlib /target:library /optimize+ /nowarn:0618,0219 /out:`"$hd\zDevFlight.dll`" $ra /r:`"$root\GameData\EjectionSeats\Plugins\EjectionSeats.dll`" `"$PSScriptRoot\zDevFlight.cs`""
$p = Start-Process "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -ArgumentList $cmd -NoNewWindow -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "harness build failed" }

$pd = Join-Path $t "GameData\EjectionSeats\PluginData"
New-Item -ItemType Directory -Force $pd | Out-Null
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
$inv = [Globalization.CultureInfo]::InvariantCulture
$flag = @($Out, ("alt = " + $Alt.ToString($inv)), ("speed = " + $Speed.ToString($inv)), ("duration = " + $Duration.ToString($inv)), ("shots = " + $Shots),
          ("camDist = " + $CamDist.ToString($inv)), ("camHdg = " + $CamHdg.ToString($inv)), ("camPitch = " + $CamPitch.ToString($inv)),
          ("dump = " + $Dump.ToString($inv)), ("seat = " + $Seat), ("kill = " + $Kill.ToString($inv)), ("plumeExport = " + $PlumeExport.ToString($inv)))
Set-Content (Join-Path $pd "flight.flag") $flag -Encoding ascii

$g = Start-Process (Join-Path $t "KSP_x64.exe") -WorkingDirectory $t -PassThru
$end = (Get-Date).AddMinutes(6); $hung = 0; $state = "timeout"
try {
    while ((Get-Date) -lt $end) {
        Start-Sleep 1
        if ($g.HasExited) { $state = "exited"; break }
        if (Test-Path (Join-Path $Out "done.txt")) { $state = "done"; break }
        $g.Refresh(); if ((Test-Path (Join-Path $Out "log.txt")) -and -not $g.Responding) { $hung++ } else { $hung = 0 }
        if ($hung -ge 6) { $state = "HUNG"; break }
    }
}
finally {
    Start-Sleep 2; if (-not $g.HasExited) { Stop-Process -Id $g.Id -Force }; Start-Sleep 1
    Remove-Item (Join-Path $pd "flight.flag") -Force -ErrorAction SilentlyContinue
    Remove-Item $hd -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $t "saves\test\Ships\SPH\DevOne.*") -Force -ErrorAction SilentlyContinue
}
"result=$state"
if (Test-Path (Join-Path $Out "log.txt")) { Get-Content (Join-Path $Out "log.txt") }
