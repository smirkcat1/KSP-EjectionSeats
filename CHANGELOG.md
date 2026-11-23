# Changelog

## 1.2.0

* Fixed the ejection seat (and the kerbal in it) starting about 20 cm below the cockpit's own seat. The kerbal now
  starts exactly where they were sitting in the IVA, in any cockpit.
* The jettisoned canopy no longer has its cabin lights on.
* Ejection seats can now be installed in crew cabins too (Mk1 Crew Cabin, Mk3 Passenger Module and the like),
  not just cockpits and command pods.
* Ejecting kerbals no longer knock the craft into a spin.
* Kerbals no longer collide with ejection seats (their own or a crewmate's) while riding one and for 2 seconds after
  separating, which sometimes killed them or flung them off at seat separation. The spent seat has no collisions
  at all until it is clear of the kerbal.
* You can cut the seat parachute away: right-click the kerbal and press "Cut parachute" (no keybind, so it never
  clashes with other keys or mods). The kerbal can still open their own EVA parachute afterwards.
* The seat rocket now looks like a solid rocket motor: a thick, bright yellow-white flame with churning fire and
  sparks instead of a liquid-engine flame with shock diamonds.
* Seat plumes can be tuned per seat with EJECTION_PLUME configs in `GameData/EjectionSeats/Plumes` (flame size and
  colour, fire, sparks, smoke, light, and the nozzle's position and angle). They are re-read without restarting.
* Each seat now has its own plume, modelled on real seat-test footage: a short white-hot jet with a big glow, sparks
  and a thick smoke trail, tan on modern seats, grey-brown on Cold War seats and dark grey on the WWII cartridges.

## 1.1.0

* Kerbals now ride the seat in a braced ejecting pose, with the arms solved so the hands stay clear of the body.
  Under the canopy they use KSP's own parachute animations: braking while it opens, then hanging and looking
  round.
* The jetpack, parachute pack and backpacks are hidden while the kerbal is in the seat, and the seat is fitted to
  the suit's back.
* Crew leave the cockpit the moment Eject is pressed and wait strapped into their seats. A cockpit destroyed during
  the canopy jettison (for example ejecting just before hitting the ground) no longer takes them with it; the
  seats fire straight away instead.
* Kerbals are seated from the first moment of the ejection instead of briefly standing up in the seat.
* New setting `kerbalPoses` to turn the poses off.

## 1.0.0 (first release)

* Ejection seats for any crewed command part from any mod, added from the part's right-click menu in the
  VAB/SPH. No per-part configs.
* Eight seats in three eras: Modern (ACES II, Martin-Baker US16E, Zvezda K-36D), Cold War (Martin-Baker Mk 4/5,
  Martin-Baker Mk 7, KM-1) and World War II (Heinkel He 162, Heinkel He 219). Stock look by default, ReStock look
  when ReStock is installed.
* Adjustable ejection force (5–50 g), rocket burn time, pitch and yaw (±10°), parachute delay and opening time.
* **Eject crew** action, in the Abort group by default.
* Automatic canopy detection and jettison, with an escape hole cut through hulls whose glass is part of the
  hull. The cockpit interior stays visible afterwards, including after a save and reload.
* Animated strap-in, catapult rail, rocket burn, seat/man separation and parachute deployment.
* Layered rocket flames and a thick solid-rocket smoke trail.
* The separated seat becomes a real "Ejection Seat" debris part, with SolidFuel, KSP aerodynamics and crash
  handling, so it breaks up when it hits the ground. It can't be built in the editor.
* Parachutes use the stock canopy models, so mods that retexture stock parachutes change them too.
* Optional support for BDArmory (missile exhaust), ReStock, RealWindows, FreeIVA, TweakScale, StretchScale,
  BetterTimeWarp slow motion and C.A.N.O.P.I.E.S. (cloth canopies).
