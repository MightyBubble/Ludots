# Scenario: champion-skill-panel-showcase

## Header
- build: GasTests / ChampionSkillPanelShowcase_WritesThemeArtifacts
- map: champion_skill_sandbox
- clock: FixedFrame @ 60 Hz
- execution_timestamp_utc: 2026-08-23T06:31:51.1904539Z
- screenshots: `screens/001_lol_ezreal.png`, `screens/002_dota2_geomancer.png`, `screens/003_sc2_spell_engineer.png`

## Timeline
[T+001] Theme=LoL | Ezreal Alpha selected | bottom bar restyled into Summoner-Rift-style release panel
[T+002] Theme=Dota2 | Geomancer Alpha selected | ornate six-slot console showcase captured under indicator cast mode
[T+003] Theme=SC2 | Spell Engineer Alpha selected | command-card showcase captured under press-release cast mode

## Outcome
- result: success
- failure_branch: theme buttons or scene capture path failed to update the mounted shared command panel
- final_theme: EntityCommandPanel.Showcase.SC2
- final_selected: Spell Engineer Alpha
- final_mode: ChampionSkillSandbox.Mode.PressReleaseAim
- final_screenshot: screens/003_sc2_spell_engineer.png

## Summary Stats
- total_actions: 3
- themed_showcases: 3
- shared_panel_runtime_reused: true
- median_tick_ms: 1.708
- max_tick_ms: 29.237
