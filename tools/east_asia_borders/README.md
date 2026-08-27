# East Asia borders tooling

`rasterize_countries_to_field.py` converts Natural Earth `admin_0` GeoJSON into
Ludots Field schema v2 rects for the East Asia playable board.

`export_country_decal_png.py` paints those rects into `country_borders.png`, a
board-aligned Decal stamp (one pixel per Field cell). Palette is
`FieldEastAsiaCountryMod/assets/Textures/country_palette.json`.

Projection parameters are read from
`EastAsiaPlayableTerrainMod/assets/terrain/east_asia_terrain_profile.json`
(spherical Albers + `playableWorldWidthOverrideCm` / `sourceWorldWidthCm`).

Do not invent lon/lat→world formulas in call sites; regenerate through this tool.

**Alignment contract:** country ↔ province (or any multi-scale ownership join)
belongs here as a one-shot offline bake. Do not push alignment types, ChildOf
trees, or stacked admin layers into Core. Runtime only loads and point-queries
the baked `Fields/cells/*.json`. See
`gitbook/architecture/mapfield-discreteid-ssot.md`.
