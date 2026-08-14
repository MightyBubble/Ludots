# StarCraft Full RTS Showcase

This showcase is the first playable pass for the 100-unit StarCraft-style RTS slice.

Run it from the repo root:

```powershell
scripts\run-mod-launcher.cmd cli launch rts_starcraft_full_showcase --adapter raylib
```

What a new player should try first:

1. Watch the three armies spawn with distinct Terran, Zerg, and Protoss unit sets.
2. Select a base unit from the command panel.
3. Train a combat unit from the base.
4. Use faction skills and watch the GAS graph effects update combat state.
5. Push the Terran assault route until the Zerg base is destroyed.

This entry depends on the shared RTS browser production HUD and runs with the CEF browser runtime enabled. The browser panel is part of the play surface, not a debug-only report.

Current acceptance coverage:

- content completeness for 100 templates, presenters, map placements, items, abilities, effects, and graphs
- CEF browser DataPlane startup
- production, mining, passive upgrade effects, graph-triggered combat effects, and victory loop

