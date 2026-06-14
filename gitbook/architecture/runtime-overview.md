# 运行时总览

本页描述 Ludots 当前运行时的正式大图景。

## 1 主要目录

- `src/Core/`：ECS、GAS、配置、脚本、地图、相机、运行时核心
- `src/Apps/`：产品和 adapter 入口
- `src/Tools/`：launcher、editor bridge 等开发工具
- `mods/`：内置与示例 Mod
- `assets/`：基础资源和配置

## 2 主要管线

- Launcher Runtime Pipeline：selector → launcher graph artifact → `launcher.runtime.json` → adapter app → shared `ModLoadContext`
- Launcher graph contract：`src/Core/Hosting/LauncherGraphDocument.cs` owns the runtime-readable DTO; launcher writes it and `GameBootstrapper` reads it, with no parallel launcher/runtime graph DTO.
- ConfigPipeline：合并运行时配置
- Mod Loading：解析 `mod.json`、排序依赖、挂载 VFS、调用 `IMod.OnLoad`
- GAS Effect Pipeline：从 Ability 激活到 Effect 处理、属性计算与延迟触发
- Presentation Pipeline：通过 Performer 和 ResponseChain 驱动表现
- Trigger Pipeline：通过 `TriggerManager.OnEvent` 组织脚本触发
- UI Runtime：通过 `UiScene` 与 `IUiRenderer` 驱动 UI

## 3 当前主线能力域

- TimeFlow：统一时间域、token 与时钟推进
- Items：物品、背包、装备、布局与 showcase 套件
- Narrative：quest、dialogue、cinematic 与 frontend kit
- Relationships：关系图谱、指标、回调、协同处理与 showcase
- Selection / Insight：选择容器、控制组、实体信息面板
- Order Navigation Movement：move order、nav runtime、多策略路径与路网 showcase

## 4 SystemGroup Phase

```text
SchemaUpdate → InputCollection → PostMovement → AbilityActivation →
EffectProcessing → AttributeCalculation → DeferredTriggerCollection →
Cleanup → EventDispatch → ClearPresentationFlags
```

新增 System 必须明确归属其中一个 phase。

## 5 产品入口

- web launcher：`.\scripts\run-mod-launcher.cmd`
- CLI launcher：`.\scripts\run-mod-launcher.cmd cli ...`
- canonical browser URL：`http://localhost:5299/launcher/index.html`
- adapter 直跑仅用于调试

## 6 深度材料

- `docs/architecture/launcher_ssot_user_first.md`
- `docs/architecture/startup_entrypoints.md`
- `docs/architecture/mod_architecture.md`
- `docs/architecture/gas_layered_architecture.md`
- `docs/architecture/order_navigation_movement.md`
- `docs/architecture/item_inventory_equipment_architecture.md`
- `docs/architecture/narrative_quest_dialogue_cinematic.md`
- `docs/architecture/narrative_frontend_kit.md`
- `docs/architecture/time_flow.md`
- `docs/architecture/ui_runtime_architecture.md`
