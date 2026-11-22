# SpaceDock listing

Fill in the SpaceDock "Create a new mod" form with the text below, then upload the zip from the release folder.

**Name:** Ejection Seats

**Game version:** 1.12.5 (works on 1.12.x)

**License:** MIT (choose "MIT" in the form; the LICENSE file is in the zip).

**Short description** (shown in mod lists):

> Ejection seats for any crewed cockpit, from any mod. Canopy jettison, rocket seats, real parachutes and seats that crash.

**Long description:**

Ejection seats are something you add to a cockpit, not a part you attach. Any crewed command part, stock or
modded, gets an **Ejection Seats** section in its right-click menu in the VAB/SPH. Install the seats and an
**Eject crew** action goes into your Abort group.

There are no per-part configs. For every cockpit the mod finds the crew seats from the IVA, works out which way
each kerbal should leave, finds the canopy and plans a clear path out of the craft.

**Features**

* Eight seats in three eras:
  * Modern: ACES II, Martin-Baker US16E, Zvezda K-36D.
  * Cold War: Martin-Baker Mk 4/5, Martin-Baker Mk 7, KM-1.
  * World War II: Heinkel He 162, Heinkel He 219.
* Each seat has a stock look, and a ReStock look when ReStock is installed.
* Adjustable ejection force (5–50 g), burn time, pitch and yaw angle, parachute delay and opening time.
* The canopy blows off, or a hole is cut through the hull, and the cockpit interior stays visible afterwards.
* The kerbal straps in, rides the catapult rail and rocket, separates from the seat and opens a parachute.
* Layered rocket flames and a thick solid-rocket smoke trail.
* The spent seat becomes a real part with leftover SolidFuel, so it breaks up when it hits the ground.
* Parachutes use the stock canopy models, so parachute retextures apply to them.

**Requirements:** ModuleManager.

**Optional, supported:** BDArmory (missile exhaust plumes), ReStock, RealWindows, FreeIVA, TweakScale,
StretchScale, BetterTimeWarp (watch ejections in slow motion), C.A.N.O.P.I.E.S. (cloth canopies).

**Installation:** copy the `GameData/EjectionSeats` folder from the zip into your KSP `GameData` folder.
Install ModuleManager as well.
