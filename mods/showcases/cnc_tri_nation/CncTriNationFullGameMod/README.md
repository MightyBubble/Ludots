# C&C Tri-Nation Full Game Showcase

This showcase is the first player-facing C&C-style tri-nation command slice. It opens on a live command HUD: factions and resources at the top, field roster on the left, battlefield in the center, selection and production on the right, and direct commands on the bottom bar.

Run it from the repo root:

```powershell
scripts\run-mod-launcher.cmd cli launch cnc_tri_nation_showcase --adapter raylib
```

What a new player should try first:

1. Pick a faction from the faction strip.
2. Select a unit or structure from the field roster or battlefield dots.
3. Use the bottom command bar to train, build, or activate the selected unit's exposed ability slots.
4. Watch production and graph signal panels update from the live DataPlane snapshot.

The browser HUD uses the `ludots.cnc.triNation.world` topic and only sends these commands back to the game:

- `selectEntity`
- `activateAbilitySlot`
- `switchParticipantView`

If the CEF DataPlane transport is missing, the HUD shows an explicit error instead of silently displaying fake data.

