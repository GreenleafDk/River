# River in a Box – ideas, todos, measurements

## TODO

- Outlet: the last ~10 % of the length gets messy (wide, braided, streaky) – try a fixed base level or a longer
  ghost reach at the outlet.
- Sand table preset (1 % fall, no plants) incises a canyon and braids instead of meandering; the throttle keeps it
  stable but it needs its own defaults (lower landscape speed, maybe a little cohesion).
- Grid streaks along x in shallow water over loose sand; check the bedload flux splitting / add a little bed diffusion.
- Brush picking uses the flat reference plane, not the drawn heights (off by vscale·z).
- Tool icons, portrait layout, Android export ("Android Debug" preset with `.dbg` app id), splash, app icon.
- Save/load landscapes; time-lapse recording.
- View: bed change since start (needs z0), bedload/shear view, oxbow lakes highlighted.

- Bank erosion still leaves axis-aligned scarps (rectangles) on the floodplain in places.
- Oxbow lakes: no infill by sediment or water plants yet (only evaporation dries them, then land plants).

## Ideas

- Floods: discharge that varies over time (hydrograph) – overbank deposits build levees and floodplains.
- Sea/lake at the outlet as a fixed water level → deltas (the user asked why deltas form only at the sea:
  a fixed base level and the flow losing its channel).
- Several inlets / tributaries.

## Measurements

- 2026-10-09: the meandering came from making bank erosion asymmetric (outer bend ×20, base 2e-5) plus random
  inlet wander; with symmetric bank erosion the channel only widened and filled. Inner bend deposition 3 gives
  sharper bends and clear point bars (sinuosity 1.50 at 115 d vs 1.38 with 1).
