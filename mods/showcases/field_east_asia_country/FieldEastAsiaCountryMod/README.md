# Field East Asia Country

Natural Earth 110m `admin_0` national borders, Albers-projected and rasterized
into Field schema v2 (`ownership.east_asia.country`, cell 7142 cm) on the
~64 km East Asia playable board.

Regenerate:

```bash
python3 tools/east_asia_borders/rasterize_countries_to_field.py \
  --geojson tools/east_asia_borders/ne_110m_admin_0_countries.geojson \
  --profile mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/terrain/east_asia_terrain_profile.json \
  --out mods/showcases/field_east_asia_country/FieldEastAsiaCountryMod/assets/Fields/cells/ownership.east_asia.country.json
```

Launch borders alone:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:field_east_asia_country_raylib'
```
