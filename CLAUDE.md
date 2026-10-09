# River in a Box – notes for Claude

Also read `../CLAUDE.md` (rules shared by all the "in a Box" projects: reply in Danish, one git worktree per task,
merge into `main` when the user says commit, hidden test runs via `../_Tools/godot-run.ps1`). Ideas, todos and
measurements go in `IDEAS.md`. Started 2026-10-09. Godot 4.7.2 mono, C#, Forward+ on Vulkan.

## What it is

The classroom sand table (a tilted table of sand with water let in at one end, where a river forms and starts to meander),
at full size: a landscape of width × length in metres with a general fall in %. **The fall is only a driving force**
(g·S in the momentum equation, and in the bed slope the sediment sees); heights are stored relative to it and the view
draws the land flat. Seen in oblique 3D with a free orbit camera. Success = a meandering river forms over time.

## Code map

- `shaders/river.glsl` – all compute kernels (RDShaderFile versions): `momentum` (staggered u/v, semi-Lagrangian
  advection, eddy viscosity, g·S, implicit Manning, upwind face depth = wet/dry safe), `continuity` (h and suspended
  sediment, inlet source, pick-up/settling, evaporation), `sedcell` (bedload vector per cell: Meyer-Peter Müller,
  helical-flow turn `atan(A h κ)`, downhill pull, **inner bend deposition** = extra flux towards the bend centre;
  dry bank cells get a downslope flux (Sobel normal, isotropic) set by the fastest neighbouring water ×
  (1 + **outer bend erosion** × curvature × outer-side) — this asymmetry is what makes meanders), `sedface` (face
  fluxes by flux splitting, inlet feed, outlet, avalanching past the angle of repose), `bed` (Exner with
  mdt = morfac·dt, loose layer, vegetation grows on dry ground), `export` (textures), `brush`, `thalweg`.
- `scripts/RiverSim.cs` – buffers, ping-pong uniform sets (set s reads buffer s, writes 1−s), params P[] (indices
  as the `#define`s), frame loop, async stats/thalweg, inlet wander (Ornstein-Uhlenbeck random walk of the inflow
  angle and position: the randomness that starts the meanders), automatic `Throttle` of the landscape speed when the
  bed update clamps.
- `scripts/Landscape.cs` – size, fall, resolution, inlet, starting channel, bumps, soil patchiness (random
  erodibility), presets; `SimParams` – the physics knobs (all live via `UploadParams`).
- `scripts/LandView.cs` + `shaders/terrain.gdshader`, `water.gdshader` – one vertex per cell, heights from the sim
  textures (Texture2Drd), vertical exaggeration `VScale`.
- `scripts/Main.cs` (scene, camera, brushes, test flags, thalweg analysis), `Main.Ui.cs` (top bar, tools, settings
  drawer, status), `BoxTheme.cs` (family theme, teal accent).

## Running / testing

`tools/test.ps1 -Name x -Seconds 90 -Extra "--steps 200 --top --noui --view 2 --set OuterBank=20"` builds, imports
and runs hidden; screenshot `out/x.png`, log lines each second with sinuosity, channels, width, amplitude.
Flags after `--`: `--selftest s`, `--steps n` (per frame), `--shot path s`, `--shotevery prefix s`, `--top`,
`--noui`, `--view 0..3`, `--cam yaw pitch dist/length`, `--preset i`, `--res cells`, `--seed n`,
`--set Field=value` (any `SimParams` or `Landscape` field). After editing `river.glsl` run `--headless --import`.
Keys: Space pause, F fit, T top view. Mouse: left orbit (Look tool) or paint, right orbit, shift+right/middle pan,
wheel zoom; touch: one finger, two-finger pinch/pan.

Measured 2026-10-09 (RX 9070 XT, 600×150 cells): ~8000 steps/s; defaults give sinuosity ~1.4–1.5 after ~115
landscape days (90 s real at 200 steps/frame).

## Options added 2026-10-09 (user's requests)

- Periodic sides (`Landscape.PeriodicSides`, default on, shader `PERIODIC` = P[35]: `wj()` wraps rows, `vid` face NY =
  face 0, noise sampled on a cylinder so there is no seam). With walls the river got stuck along a side.
- Sea at the outlet (`Landscape.Sea`, preset "River delta", P[36]/P[37]): basin over the last `SeaFraction`, filled
  to a flat true level (relative eta = level − S(L − x)); the outlet ghost cell holds the sea level and backflow is
  allowed. Off by default.
- Fall and water (discharge) are in the top bar. Brush strength goes to 20 (10× the old max).
- Tooltips for every control in `scripts/Tooltips.cs` (keyed by label/name); new controls need an entry.
- Sediment feed default 0.75: 1.0 aggraded at the inlet into a braided fan; 0.5 incised a straight stable channel.
- `tools/test.ps1` always re-imports the shaders (also with -NoBuild): stale SPIR-V cost a lot of confusion once.
- Walls (periodic off): `sideSlope()` = fictive slope towards the middle in the wall zone (P[39] `SidePush` %,
  zone P[46]), added to the v momentum and the bedload slope; `K_BED` relaxes the zone towards its target profile
  (P[47] `SideRise` m at the wall, P[48] `SideRecoverDays`), not mass conserving. "Hard ground at walls" = rock band
  at build time. A real raised edge alone was eroded away (user, 2026-10-09).
- The fall is drawn as a tilt by default (`LandView.Fall`, shader `fall`: y = (z − S x)·vscale, View → "Show the fall";
  camera target and brush picking use the tilted plane `GroundY`). The user found the flat view misleading on
  steep land (water seemed to run uphill), 2026-10-09; the sim itself is unchanged.
- View smoothing (`LandView.Smooth`, shader `smooth_amt`, default 1): 5x5 binomial heights for ground, wet-only
  average for the water surface. Bed smoothing under water (P[38]) damps grid-sized pits.
