# Ejection Seats

Ejection seats for Kerbal Space Program 1.12.x. Any crewed part, stock or modded (cockpits, command pods and crew
cabins), can be fitted with ejection seats in the VAB/SPH. When things go wrong, the canopy blows off or a hole is cut
through the hull, each kerbal rides a rocket seat clear of the craft, separates from it and comes down under a
parachute.

## Requirements

* **Kerbal Space Program 1.12.x** (made for 1.12.5).
* **[ModuleManager](https://github.com/sarbian/ModuleManager)**: required. The mod uses it to add ejection seats to
  every crewed part. Without it no part gets the seats.

Optional, and supported when installed: BDArmory, ReStock, RealWindows, FreeIVA, TweakScale, StretchScale,
BetterTimeWarp and C.A.N.O.P.I.E.S.

## Installing

1. Install ModuleManager if you don't have it yet (many other mods already include it): download it from its
   [GitHub page](https://github.com/sarbian/ModuleManager) or its thread on the KSP forum, and put the
   `ModuleManager.x.x.x.dll` file straight into your KSP `GameData` folder. Only keep one copy of it.
2. Download the Ejection Seats zip from SpaceDock or from the GitHub releases page.
3. Open the zip and copy its `GameData/EjectionSeats` folder into your KSP `GameData` folder.
4. Check the result. Your `GameData` folder should now contain:

   ```
   GameData/
       EjectionSeats/
       ModuleManager.x.x.x.dll
       Squad/
       ...
   ```

   If you see `GameData/GameData/EjectionSeats`, the folder went one level too deep: move `EjectionSeats` up into
   the first `GameData`.

To update, delete the old `GameData/EjectionSeats` folder first, then install the new one. To uninstall, delete
`GameData/EjectionSeats`. Seats already installed on craft are removed with it.

## Using it

1. In the VAB or SPH, right-click a cockpit, pod or crew cabin and open the **Ejection Seats** section.
2. Press **Install ejection seats**, then pick an **Era** (Modern, Cold War, World War II) and a **Seat**. You can
   adjust the ejection force, rocket burn, angles and parachute timing there too, and **Show ejection preview**
   shows the path each seat will take.
3. An **Eject crew** action is added to your Abort action group. In flight, press Abort (Backspace by default), or
   use **EJECT** in the part's right-click menu.

Once a kerbal's parachute is open, you can right-click the kerbal and press **Cut parachute** to drop away from it.
They can still open their own EVA parachute afterwards.

## Seat plumes

Each seat has its own rocket plume, set in `GameData/EjectionSeats/Plumes/EjectionSeats_Plumes.cfg`. See the
README in that folder to make your own.

## License

MIT. See [LICENSE](LICENSE).
