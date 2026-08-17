# Superweapon Context Showcase Acceptance

## Header
- scenario name: RFC-0065 SHOW-1 / M2/P6 superweapon context confirmation
- build/version: test runtime
- seed/map/clock: deterministic headless, `interaction_showcase_hub`, 60Hz tick

## Scenario
- Showcase: `SuperweaponContextShowcaseMod` over `interaction_showcase_hub`.
- Launcher binding: `superweapon_context_showcase` (`.\scripts\run-mod-launcher.cmd cli launch superweapon_context_showcase --adapter raylib`).
- Runtime path: `castAbility.Start` -> `AbilityExecSystem` -> `AbilityExecInteractionContextSystem` -> `InteractionContextInputContextBridge` -> `CoreServiceKeys.AuthoritativeInput` -> `GameplayEventBus`.
- Context profile: `ctx.ability.superweapon.confirm_targets`.
- Player action: press `<Keyboard>/enter` through `imc.ability.confirm`; the test does not publish the completion event directly.

## Timeline
- [T+000] Launcher binding `superweapon_context_showcase` -> `mods/showcases/superweapon_context/SuperweaponContextShowcaseMod` verified; Commander#8.Cast(Superweapon Context) -> GateWaiting(`Event.Showcase.Superweapon.Confirmed`).
- [T+001] AbilityFrame.Push(`ctx.ability.superweapon.confirm_targets`) -> IMC `imc.ability.confirm` active.
- [T+002] ContextBoundCollectionWriter.CommitCast -> ability targets `6, 7`.
- [T+003] PlayerInput(`<Keyboard>/enter`) -> Authoritative `SuperweaponConfirm` -> GameplayEvent published.
- [T+004] AbilityExecSystem consumes event -> End -> frame restored to `interaction.context.default`.

## Outcome
| Field | Value |
|-------|-------|
| Ability id | 26 |
| Local player | Entity = { Id = 8, WorldId = 13, Version = 1 } |
| Commander context entity | Entity = { Id = 8, WorldId = 13, Version = 1 } |
| Ability targets | Entity = { Id = 6, WorldId = 13, Version = 1 }, Entity = { Id = 7, WorldId = 13, Version = 1 } |
| Raw local targets | Entity = { Id = 6, WorldId = 13, Version = 1 }, Entity = { Id = 7, WorldId = 13, Version = 1 } |
| Confirm input observed | True |
| Confirm events published | 1 |
| Command source after frame restore | Entity = { Id = 8, WorldId = 13, Version = 1 } |

## Summary Stats
- total actions: 1 physical confirm press
- routed targets: 2
- dropped/budget/fuse counters: 0 observed in this headless path

## Verdict
- success: yes
- evidence: ability-owned frame captured raw targets on the local anchor, domain-routed target acquisition to `collection.ability.superweapon.targets`, left `collection.command.source` untouched, confirmed through IMC/authoritative input, and restored default routing after the event gate completed.
