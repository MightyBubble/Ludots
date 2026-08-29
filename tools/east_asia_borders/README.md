# East Asia borders tooling

`rasterize_countries_to_field.py` converts Natural Earth `admin_0` GeoJSON into
Ludots Field schema v2 rects for the East Asia playable board.

Projection parameters are read from
`EastAsiaPlayableTerrainMod/assets/terrain/east_asia_terrain_profile.json`
(spherical Albers + `playableWorldWidthOverrideCm` / `sourceWorldWidthCm`).

Do not invent lon/lat→world formulas in call sites; regenerate through this tool.
