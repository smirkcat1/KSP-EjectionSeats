Seat rocket plumes
==================

Put EJECTION_PLUME config files (*.cfg) in this folder to change how a seat's rocket looks.
Make them with the plume editor (tools/plume_editor/plume_editor.html in the source repository).

Each EJECTION_PLUME node names a seat id with "seat = ..." (modern, us16e, k36, coldwar, mk7, km1, ww2,
he219), or "seat = all" for every seat that has no plume of its own. Values left out keep the default look.
Files are read in alphabetical order, so a later file replaces an earlier plume for the same seat. The plumes that
ship with the mod are in EjectionSeats_Plumes.cfg; to replace one, name your file so it sorts after it (e.g. "Z_MyPlumes.cfg").
Changes are picked up within a couple of seconds, on the next ejection, without restarting the game.
