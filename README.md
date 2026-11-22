# Ejection Seats for KSP 1.12

Ejection seats are something you add to a cockpit, not a separate part. Any crewed command pod from any mod gets an
**Ejection Seats** section in its right-click menu in the VAB/SPH. Install the seats, pick an era and seat, set the
force and angle, and an **Eject crew** action goes into your **Abort** group. You can move it to any other
action group.

## Install

1. ModuleManager is required (you already have it).
2. Copy `GameData/EjectionSeats` into `Kerbal Space Program/GameData/`, or run
   `powershell -ExecutionPolicy Bypass -File build.ps1 -Install`, which rebuilds the DLL and copies it in.
3. BDArmory is optional. If it is installed, the seat rockets use BDA's missile exhaust (`smallExhaust`).
   Without it, a built-in flame and smoke-trail plume is used instead.

## In the editor (right-click any crewed pod)

| Option | Range | Notes |
|---|---|---|
| Install / Remove ejection seats | — | adds 0.08 t and 450 funds per crew seat (configurable) |
| Era | Modern, Cold War, World War II | limits the Seat list to that period |
| Seat | depends on era | Modern: ACES II, Martin-Baker US16E, Zvezda K-36D. Cold War: Martin-Baker Mk 4/5, Martin-Baker Mk 7, KM-1. World War II: Heinkel He 162, Heinkel He 219. Cosmetic. Seats are kerbal-sized interpretations of the real designs, and they switch to a ReStock look when ReStock is installed |
| Ejection force | 5–50 g | acceleration from the catapult and rocket |
| Rocket burn | 0.2–1.5 s | how long the force is applied |
| Pitch angle | −10° … +10° | + tilts the trajectory aft |
| Yaw angle | −10° … +10° | + tilts the trajectory to the right |
| Chute delay | 1–2 s | time from seat separation to parachute deployment |
| Blow canopy first | on/off | |
| Show interior after | on/off | the IVA becomes visible from outside through the hole |
| Show ejection preview | — | draws each seat's path (green = clear, red = blocked), ghost seats, and boxes around the meshes it thinks are the canopy |

The **Ejection** status line in the menu summarises what the analyser found, for example
`2 seat(s), canopy: canopyGlass, path clear` or `path blocked by Mk16 Parachute`.

## How it works out each cockpit

There is no per-mod config. For each pod it:

1. **Finds the seats** from the part's IVA (`InternalSeat` transforms mapped into the part with KSP's own
   IVA-to-part rotation). If there is no IVA, it spaces the seats evenly across the part.
2. **Picks the ejection axis.** Kerbals eject along their IVA seat's own "up" axis, with your pitch and yaw
   applied on top. Aircraft seats fire up through the canopy, and capsule crews fire the way they're facing,
   for example out of the front of a crew capsule. The path is ray-cast against the whole craft to find where
   it leaves the hull and to report anything in the way. Setting `preferVehicleUp = true` in Settings.cfg also
   tries the craft's "up" and a blend of both, keeping the clearest one. Pods with no IVA use the craft's "up".
3. **Finds the canopy.** Every mesh is scored on its name (canopy, glass, window, hatch…), whether its
   shader is transparent, whether it sits on the ejection path, and whether it is above the seat. Meshes that
   are most of the part are rejected, since they are the fuselage. Canopy meshes are detached whole.
4. **Cuts an escape hole.** Any hull triangles still in a cylinder above each seat are subdivided and cut
   out. They fly off together with the canopy as one piece of debris, so cockpits whose glass is baked into
   the hull mesh (most stock ones) still get a clean exit.
5. **Shows the interior.** The IVA meshes are copied into the outside world inside the cockpit, and the same
   column is cut out of them so overhead panels don't block the view.

## The sequence

1. **Canopy jettison:** the det-cord puff, flash and bang, then the canopy or hull panel tumbles up and aft.
2. **Strap in:** each kerbal appears leaning forward and settles back into the seat (seated animation).
3. **Catapult rail:** the seat rides a rail fixed to the cockpit, so it clears the hull even if the plane is
   rolling. Gas puff at the base.
4. **Rocket:** the plume lights partway up the rail, the seat stabilises through the burn, with rocket sound
   and a flickering light. The solid motor leaves a thick smoke trail that hangs in the air (`srbSmoke`).
5. **Coast:** the drogue pitches the seat back gently.
6. **Seat/man separation:** the seat falls away tumbling and the kerbal kicks free into a free-fall pose. The
   seat becomes a real **Ejection Seat** part (a debris vessel with whatever SolidFuel is left, KSP aero and crash
   handling), so it breaks up when it hits the ground. The part can't be built in the editor.
7. **Parachute:** 1–2 s later a gored, scalloped round canopy comes out as a streamer, inflates with an
   overshoot, then breathes and sways. The drag gives about 7 m/s at sea level. In vacuum it isn't deployed
   and you get a message instead.
8. **Touchdown:** the canopy collapses and falls over, and control of the kerbal is handed back to you.

Multi-seat cockpits eject one seat after another, 0.35 s apart. The camera follows the first kerbal out.
After a save and reload, the cockpit keeps its missing canopy or hole, and its interior stays visible.

## Tuning (`GameData/EjectionSeats/Settings.cfg`)

The game re-reads this file while it is running, so you can tweak values without restarting. The ones most
likely to need adjusting:

* `kerbalSeatOffset` / `ivaToEvaOffset`: if the kerbal floats above or sinks into the seat.
* `animSeated`, `animFreefall`, `animChute`: animation names. Set `debug = true` and every clip the EVA
  kerbal has is printed to `KSP.log`.
* `hatchRadius`, `hatchStartHeight`: how big the hole is and where it starts.
* `canopyMinScore` and the word lists: if a canopy isn't detected, or the wrong mesh is.
* `plumeScale`, `plumeRotation`: the size and direction of the BDA plume.
* `srbSmoke`, `srbSmokeAmount`, `srbSmokeLifetime`: the seat motor's smoke trail.
* `seatAsPart`: set `false` to go back to the seat being a simple object that disappears after `seatLifetime`.
* `seatMaxRecline`: how far from upright (degrees) the airflow or parachute may lean the kerbal back.
* `kerbalsFeelGForce`: set it to `true` if you want 50 g to actually knock kerbals out.

## Building

`build.ps1` compiles `Source/*.cs` with the .NET Framework 4 `csc.exe` that ships with Windows, against
`KSP_x64_Data/Managed`. Pass `-KspDir` if KSP lives somewhere else. The source is C# 5 so that this
compiler can build it.

## Known limitations

* A parachute that is open when you save is not saved. After a reload the kerbal is a normal EVA kerbal and
  can use the stock EVA chute.
* While seated and under the canopy, the kerbal's EVA controls are suspended. They come back on landing (or
  straight after separation in vacuum).
* Meshes the game marks as non-readable can't be cut. Those cockpits fall back to detaching the canopy only,
  or to a smoke puff.
