# Visible Checklist: mass-navigation-large-world

- `000_boot.png`: should show the configured 64km RTS world framing, MassNavigation unit samples, solver window, flow work area and minimap marker counts.
- `001_selection_order.png`: command actor, world HUD, screen HUD bar/text, and zero-drop counts should prove the formal order and complete projection paths.
- `002_remote_minimap_jump.png`: camera coordinates should be far from boot coordinates while agent counts remain unchanged.
- `003_return_original_area.png`: agent count and scenario spawn/reset counters should match boot, proving camera movement did not recreate the scenario.
- `screens/timeline.png` is the compact strip for side-by-side UAT review.

- `000_boot.png`: agents=10000, groups=0, minimapVisible=10000, frameMs=27.309
- `001_selection_order.png`: agents=10000, groups=0, minimapVisible=10000, frameMs=36.944
- `002_remote_minimap_jump.png`: agents=10000, groups=0, minimapVisible=10000, frameMs=16.237
- `003_return_original_area.png`: agents=10000, groups=0, minimapVisible=10000, frameMs=16.201
