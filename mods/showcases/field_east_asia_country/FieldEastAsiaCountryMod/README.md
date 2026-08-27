# Field East Asia Country

Natural Earth 110m `admin_0` national borders, Albers-projected and rasterized
into Field schema v2 (`ownership.east_asia.country`, cell 7142 cm) on the
~64 km East Asia playable board.

玩家看到的国界色是整图 **Decal 投影贴花**：`country_borders.png` 按棋盘尺寸印在高度图上。
填色接近不透明，避免草地从贴花底下透出来。Field 格子只负责过境判定；地图标签 `Raylib.FieldOverlays:Off` 关掉调试用 DiscreteOwnership 铺盖。

Regenerate cells:

```bash
python3 tools/east_asia_borders/rasterize_countries_to_field.py \
  --geojson tools/east_asia_borders/ne_110m_admin_0_countries.geojson \
  --profile mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/terrain/east_asia_terrain_profile.json \
  --out mods/showcases/field_east_asia_country/FieldEastAsiaCountryMod/assets/Fields/cells/ownership.east_asia.country.json
```

Regenerate the decal stamp after cells or palette change:

```bash
python3 tools/east_asia_borders/export_country_decal_png.py \
  --field mods/showcases/field_east_asia_country/FieldEastAsiaCountryMod/assets/Fields/cells/ownership.east_asia.country.json \
  --palette mods/showcases/field_east_asia_country/FieldEastAsiaCountryMod/assets/Textures/country_palette.json \
  --profile mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/terrain/east_asia_terrain_profile.json \
  --out mods/showcases/field_east_asia_country/FieldEastAsiaCountryMod/assets/Textures/country_borders.png
```

Launch borders alone:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:field_east_asia_country_raylib'
```
