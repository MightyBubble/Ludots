## GAS Composition Gate — Self Review

- **Task / Issue**: 控制方案切换后的默认施法方式（graph-capability-status §3.3.2 第 6 条）
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 切方案只发已有的地图事件，默认施法由声明该方案的 mod 用现成的 `IsPref` / `SetPref` 图写入 `InteractionPref`；没有新 opcode，也没有新的 JSON schema。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 座位方案变化 | 1 | 内建地图事件 `ControlSchemeChanged`，走 `TriggerManager.FireMapEvent` |
| 玩家是否亲手选过 | 3 | `pref.aim.cast_mode_origin`（`scheme` / `player`），种在 `InteractionPref` |
| 方案到默认施法 | 3 | 声明该方案的 mod 的 TriggerGraph，按 `MapTrigger.NewSchemeId` 过滤后 `SetPref` |
| 未知方案 id | 1 | 装载时扫已编译过滤器，名字必须是已安装的控制方案 |

### 3. Reuse list

- Handlers: `IsPref`（540，槽位 -1 只读玩家级）、`SetPref`（541）
- Queues / Systems: `TriggerManager` 地图事件表；上下文触发器门闩挂上 `interaction.context.aim.cast` 时补发还没人听的那一次
- Resolvers / Registries: `ConfigKeyRegistry`（方案名的载荷 int）、`ControlSchemeRuntime.SchemeIdRegistry`、`InteractionPrefCatalog`
- Existing presets / graphs: `graph.aim.set_cast_mode` 在写完施法方式后把 origin 写成 `player`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。`SetPref` 先写 `pref.aim.cast_mode`，再写 origin。origin 写成 `player` 之后，后续切方案不再覆盖施法方式。

### 6. Config SSOT

行为配置落在: graph / catalog

- `mods/capabilities/input/AimInteractionMod/assets/Input/interaction_prefs.json`
- `mods/showcases/aim_control_schemes/AimControlSchemesShowcaseMod/assets/GAS/graphs/graph.aim_schemes.apply_scheme_cast_default.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/graphs/graph.champion.apply_scheme_cast_default.json`
- `mods/showcases/moba_demo/MobaDemoMod/assets/GAS/graphs/graph.moba.apply_scheme_cast_default.json`

是否新增 JSON schema: NO — 方案到默认值就是图入口上的载荷过滤器和 `SetPref` 的字面量。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

再加一套方案的默认施法，是声明该方案的 mod 加一条带 `MapTrigger.NewSchemeId` 过滤的图入口。
