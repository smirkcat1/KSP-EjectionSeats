# Writes GameData/EjectionSeats/Parts/EjectionSeat/seat.mu: a KSP model file holding only a root transform
# and a box collider (the seat itself is built in game). Run again only if the collider needs to change.
param([string]$Out = (Join-Path $PSScriptRoot "..\GameData\EjectionSeats\Parts\EjectionSeat\seat.mu"))
$scale = 0.68   # default seatScale; the part module resizes the collider to the configured value
$fs = [IO.File]::Create($Out)
$w = New-Object IO.BinaryWriter($fs)
try {
    $w.Write([int]76543)          # .mu magic
    $w.Write([int]5)              # file version
    $w.Write("seat")              # model name
    # root transform: name, local position, rotation (x,y,z,w), scale
    $w.Write("seat")
    foreach ($f in 0, 0, 0, 0, 0, 0, 1, 1, 1, 1) { $w.Write([single]$f) }
    # entry 6 = box collider: size, centre
    $w.Write([int]6)
    foreach ($f in (0.5 * $scale), (1.0 * $scale), (0.45 * $scale), 0, (0.3 * $scale), (-0.08 * $scale)) { $w.Write([single]$f) }
    # entry 1 = end of this transform
    $w.Write([int]1)
}
finally { $w.Close(); $fs.Close() }
"wrote $Out (" + (Get-Item $Out).Length + " bytes)"
