# Entity Command Panel Showcase Acceptance

## Scenario
- Showcase: `EntityCommandPanelShowcaseMod` over `interaction_showcase_hub`.
- Launcher binding: `entity_command_panel_showcase` (`.\scripts\run-mod-launcher.cmd cli launch entity_command_panel_showcase --adapter raylib`).
- Registry: `EntityCommandPanelSourceRegistry` resolves `gas.collection-ability-slots` to `CollectionGasEntityCommandPanelSource`.
- Profile registry: Core `aggregation.by_template`/`aggregation.by_ability_id` plus EntityCommandPanelMod `aggregation.by_family` fragment are installed.
- Runtime path: `IEntityCommandPanelToolbarProvider.Activate` -> `CollectionGasEntityCommandPanelSource.SetAggregationProfile` -> `EntityCommandPanelSourceDispatch.CopySlots`.
- WebUI path: `EntityCommandPanelShowcaseDataPlane` publishes `ludots.showcase.entity_command_panel.state`; CEF app assets are packaged under `assets/entity-command-panel-app`.
- Collection owner: local player entity, `collection.command.source` containing Arcweaver, Vanguard, and Commander.

## Results
| Profile | Profile id | Slot count | Revision | Labels |
|---------|------------|------------|----------|--------|
| Family | aggregation.by_family | 8 | 1784404673 | Context, Dash, Mobility, Projectile, Defense, Toggle, Area, Advanced |
| Template | aggregation.by_template | 24 | 1458872427 | Fireball, Blink Step, Arc Shield, NovaPulse, ArcDash, GuardToggle, ActionContext, RuneBurst, Fireball, Leap, Guard, Shockwave, ChargeDash, IronWall, ActionContext, GroundSlam, Stone Throw, Blink Step, Guard, Overclock, ThrustJump, ShieldNet, ActionContext, OrbitalStrike |
| Ability | aggregation.by_ability_id | 21 | 2266766512 | ActionContext, ArcDash, GuardToggle, NovaPulse, RuneBurst, ActionContext, OrbitalStrike, Overclock, ShieldNet, ThrustJump, ActionContext, ChargeDash, GroundSlam, IronWall, Shockwave, Arc Shield, Blink Step, Fireball, Guard, Leap, Stone Throw |

## Slot Detail
### Family
| Slot | Label | Detail | Ability id | Template id | Flags | Action |
|------|-------|--------|------------|-------------|-------|--------|
| 0 | Context | 3 owners | ActionAttack · Smart Cast | 1 | 0 | Base | ActionAttack |
| 1 | Dash | 3 owners | Z · Smart Cast | 2 | 0 | Base | SkillZ |
| 2 | Mobility | 3 owners | Mobility command shared by Arcweaver and Commander. | 27 | 0 | Base | SkillW |
| 3 | Projectile | 3 owners | Projectile attack owned by Commander. | 28 | 0 | Base | SkillQ |
| 4 | Defense | 3 owners | Defensive command shared by Vanguard and Commander. | 26 | 0 | Base | SkillE |
| 5 | Toggle | 3 owners | F · Smart Cast | 6 | 0 | Base | SkillF |
| 6 | Area | 3 owners | R · Smart Cast | 7 | 0 | Base | SkillR |
| 7 | Advanced | 3 owners | RuneBurst · Smart Cast | 8 | 0 | Base | RuneBurst |

### Template
| Slot | Label | Detail | Ability id | Template id | Flags | Action |
|------|-------|--------|------------|-------------|-------|--------|
| 0 | Fireball | Projectile spell shared by Arcweaver and Vanguard. | 28 | 0 | Base | SkillQ |
| 1 | Blink Step | Mobility command shared by Arcweaver and Commander. | 27 | 0 | Base | SkillW |
| 2 | Arc Shield | Defensive command owned by Arcweaver. | 26 | 0 | Base | SkillE |
| 3 | NovaPulse | R · Smart Cast | 7 | 0 | Base | SkillR |
| 4 | ArcDash | Z · Smart Cast | 2 | 0 | Base | SkillZ |
| 5 | GuardToggle | F · Smart Cast | 6 | 0 | Base | SkillF |
| 6 | ActionContext | ActionAttack · Smart Cast | 1 | 0 | Base | ActionAttack |
| 7 | RuneBurst | RuneBurst · Smart Cast | 8 | 0 | Base | RuneBurst |
| 8 | Fireball | Projectile spell shared by Arcweaver and Vanguard. | 28 | 0 | Base | SkillQ |
| 9 | Leap | Mobility command owned by Vanguard. | 30 | 0 | Base | SkillW |
| 10 | Guard | Defensive command shared by Vanguard and Commander. | 29 | 0 | Base | SkillE |
| 11 | Shockwave | R · Smart Cast | 25 | 0 | Base | SkillR |
| 12 | ChargeDash | Z · Smart Cast | 21 | 0 | Base | SkillZ |
| 13 | IronWall | F · Smart Cast | 24 | 0 | Base | SkillF |
| 14 | ActionContext | ActionAttack · Smart Cast | 18 | 0 | Base | ActionAttack |
| 15 | GroundSlam | RuneBurst · Smart Cast | 23 | 0 | Base | RuneBurst |
| 16 | Stone Throw | Projectile attack owned by Commander. | 31 | 0 | Base | SkillQ |
| 17 | Blink Step | Mobility command shared by Arcweaver and Commander. | 27 | 0 | Base | SkillW |
| 18 | Guard | Defensive command shared by Vanguard and Commander. | 29 | 0 | Base | SkillE |
| 19 | Overclock | R · Smart Cast | 11 | 0 | Base | SkillR |
| 20 | ThrustJump | Z · Smart Cast | 15 | 0 | Base | SkillZ |
| 21 | ShieldNet | F · Smart Cast | 12 | 0 | Base | SkillF |
| 22 | ActionContext | ActionAttack · Smart Cast | 9 | 0 | Base | ActionAttack |
| 23 | OrbitalStrike | RuneBurst · Smart Cast | 10 | 0 | Base | RuneBurst |

### Ability
| Slot | Label | Detail | Ability id | Template id | Flags | Action |
|------|-------|--------|------------|-------------|-------|--------|
| 0 | ActionContext | ActionAttack · Smart Cast | 1 | 0 | Base | ActionAttack |
| 1 | ArcDash | Z · Smart Cast | 2 | 0 | Base | SkillZ |
| 2 | GuardToggle | F · Smart Cast | 6 | 0 | Base | SkillF |
| 3 | NovaPulse | R · Smart Cast | 7 | 0 | Base | SkillR |
| 4 | RuneBurst | RuneBurst · Smart Cast | 8 | 0 | Base | RuneBurst |
| 5 | ActionContext | ActionAttack · Smart Cast | 9 | 0 | Base | ActionAttack |
| 6 | OrbitalStrike | RuneBurst · Smart Cast | 10 | 0 | Base | RuneBurst |
| 7 | Overclock | R · Smart Cast | 11 | 0 | Base | SkillR |
| 8 | ShieldNet | F · Smart Cast | 12 | 0 | Base | SkillF |
| 9 | ThrustJump | Z · Smart Cast | 15 | 0 | Base | SkillZ |
| 10 | ActionContext | ActionAttack · Smart Cast | 18 | 0 | Base | ActionAttack |
| 11 | ChargeDash | Z · Smart Cast | 21 | 0 | Base | SkillZ |
| 12 | GroundSlam | RuneBurst · Smart Cast | 23 | 0 | Base | RuneBurst |
| 13 | IronWall | F · Smart Cast | 24 | 0 | Base | SkillF |
| 14 | Shockwave | R · Smart Cast | 25 | 0 | Base | SkillR |
| 15 | Arc Shield | Defensive command owned by Arcweaver. | 26 | 0 | Base | SkillE |
| 16 | Blink Step | 2 owners | Mobility command shared by Arcweaver and Commander. | 27 | 0 | Base | SkillW |
| 17 | Fireball | 2 owners | Projectile spell shared by Arcweaver and Vanguard. | 28 | 0 | Base | SkillQ |
| 18 | Guard | 2 owners | Defensive command shared by Vanguard and Commander. | 29 | 0 | Base | SkillE |
| 19 | Leap | Mobility command owned by Vanguard. | 30 | 0 | Base | SkillW |
| 20 | Stone Throw | Projectile attack owned by Commander. | 31 | 0 | Base | SkillQ |


## Verdict
- success: yes
- evidence: the showcase exposes all three profile buttons, uses the registered collection source, and regroups the live M6 collection through installed aggregation profiles without rebuilding Core pipeline infrastructure.
