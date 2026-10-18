# Battlefield landscape appearance

The map preview follows the selected landscape and its native EGA palette.
Tropical ground is green, desert ground is brown and Arctic ground is white.
The Arctic ground plane is solid; it is not universally covered with invented
hatching. Placed terrain geometry can have its own patterned polygon fills.

**Soften white** is a preview choice for a slightly grey display. It does not
change the stored palette or game data. Disable it to inspect native EGA white.
CLI map exports keep native white unless the option is explicitly requested.

The right-hand map view rotates and zooms a finite ground plane together with
its placed objects. Its neutral backdrop is not additional terrain. The
top-down view stays fixed for selecting stored map cells.

GRD heights remain meaningful for inspection/editing and collision research;
they are not displayed as an invented green height-band mesh. The workshop
preview is not a pixel-exact recreation of the mission briefing or the game's
perspective combat renderer.
