# RFC-0057 英雄技能 Sandbox、全局施法模式与技能面板呈现

本 RFC 定义一个最小可玩的英雄技能测试场景方案，并明确本任务需要先补的交互与 UI 基建边界。目标是在不新建平行输入、选择、GAS 或 UI runtime 的前提下，交付一个可选择多个 EZ / 盖伦 / 杰斯实例、可切换全局施法模式、可显示技能图标与指示器、且全部技能走 Ludots GAS 的 playable mod。

## 1 目标

本任务要同时满足以下目标：

1. 提供一个最小英雄技能测试场景 mod，包含多个 EZ、盖伦、杰斯实例。
2. 不同实例、不同当前状态、不同形态下，技能面板应实时不同。
3. 支持三种全局施法模式，并可通过工具面板切换：
4. `SmartCast`：按下即放。
5. `SmartCastWithIndicator`：按下显示指示器，抬起施法。
6. `PressReleaseAimCast`：按下并抬起技能键后进入瞄准，再由鼠标确认施法。
7. 技能指示器、技能图标、技能状态展示都必须走通用基建。
8. 技能逻辑通过现有 GAS `AbilityExec + EffectTemplate + Graph + Tag` 链路实现，不在 mod 内写专用技能执行器。

## 2 非目标

本任务不做以下内容：

1. 不新建第二套 selection runtime、order pipeline、aim presentation pipeline 或 UI runtime。
2. 不把英雄战斗逻辑写成专用 `EzSystem`、`GarenSystem`、`JayceSystem`。
3. 不在 feature 代码里静默混入未声明的 Core 大改。
4. 不在本轮强行补齐完整的冷却百分比、充能层数或资源蓝耗产品化 HUD；如基建缺失，以真实状态位和可验证交互优先。

## 3 复用清单

复用基建：

- Registry: `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs` — 技能定义与元数据注册。
- Registry: `src/Core/Gameplay/GAS/Registry/AbilityIdRegistry.cs` — 技能名到 ID 的稳定映射。
- Registry: `src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs` — 形态驱动的槽位覆写。
- Registry: `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` — 效果模板注册。
- Pipeline: `src/Core/Input/Selection/*` — formal selection SSOT、selection view bridge、click / box select。
- Pipeline: `src/Core/Input/Orders/InputOrderMappingSystem.cs` — 输入到 order 的交互模式主线。
- Pipeline: `mods/CoreInputMod/Systems/AbilityAimPresentationProjectionSystem.cs` + `src/Core/Input/Orders/AbilityAimPresentationRuntime.cs` + `src/Core/Presentation/Systems/PerformerRuleSystem.cs` — 通用技能瞄准表现链路，运行时只发布 `ability.aim.*` 事件，视觉由 performer rule 消费。
- Pipeline: `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` — 能力激活、失败原因、timeline 执行。
- Mod: `mods/EntityCommandPanelMod/` — 通用实体技能面板宿主。
- Mod: `mods/CoreInputMod/` — selection、aim presentation、skill bar、view mode。
- Mod: `mods/fixtures/camera/CameraAcceptanceMod/` — selection callback、retained UI、WorldHud overlay 的可复用交互实现样例。
- Mod: `mods/showcases/interaction/InteractionShowcaseMod/` — view mode、interaction mode、GAS 交互 showcase 样例。

## 4 已确认的缺口

### 4.1 技能面板展示缺口

当前 `mods/EntityCommandPanelMod/Runtime/GasEntityCommandPanelSource.cs` 只暴露：

- `AbilityId`
- `TemplateEntityId`
- `Base/FormOverride/GrantedOverride/TemplateBacked/Empty`

当前 `mods/EntityCommandPanelMod/UI/EntityCommandPanelController.cs` 只渲染文本行，不支持：

- 通用技能图标
- 按交互模式切换的图标 / 提示
- toggle active / blocked 等运行时状态位

### 4.2 全局施法模式缺口

当前 `InputOrderMappingSystem` 已支持：

- `TargetFirst`
- `SmartCast`
- `AimCast`
- `SmartCastWithIndicator`
- `ContextScored`

但尚不支持：

- `PressReleaseAimCast`

即“按下并抬起技能键后进入瞄准，再由鼠标确认”的传统 RTS 施法变体。

### 4.3 面板工具切换缺口

当前 `viewmodes.json -> ViewModeManager -> InputOrderMappingSystem.SetInteractionMode(...)` 已经是正式全局配置链路，但 `EntityCommandPanel` 还没有内建的全局交互模式切换工具面板。

## 5 设计结论

### 5.1 施法模式作为 map / 3C 全局配置项

本任务不新建单独的 cast-mode config 体系，而是在现有 `viewmodes.json` 合同上扩展：

- `ViewModeConfig.InteractionMode` 继续作为全局施法模式入口。
- sandbox map 通过自己的 `assets/viewmodes.json` 注册三种施法模式。
- UI 工具面板通过 `ViewModeManager.SwitchTo(...)` 切换当前模式。

### 5.2 技能图标挂在技能表现元数据上，而不是挂在底层 order type 上

一个技能可以在不同全局施法模式下呈现不同图标或提示，但其底层仍是同一能力定义或同一槽位解析结果。

因此新增通用元数据：

- `AbilityPresentation.DisplayName`
- `AbilityPresentation.Icon`
- `AbilityPresentation.AccentColor`
- `AbilityPresentation.ModeIconOverrides`
- `AbilityPresentation.ModeHintOverrides`

图标与提示由面板控制器根据“当前 effective ability + 当前全局施法模式”选择，不把 UI 身份直接挂在 `OrderType` 上。

### 5.3 技能槽位继续以 effective slot 为 SSOT

面板数据仍然以 `AbilitySlotResolver.Resolve(...)` 为准：

- granted override > form override > base

这样杰斯类形态英雄天然能通过 `AbilityFormRoutingSystem` 切换面板内容，而无需专用 hero panel 逻辑。

### 5.4 工具面板复用现有 retained host

由于 `UIRoot` 当前仍只挂载一个 `UiScene`，sandbox 不新建第二个 scene owner。

改为：

- 继续由 `EntityCommandPanelController` 持有 retained scene。
- 在该 host scene 内增加一个可选的全局 interaction toolbar。
- toolbar 是否显示由全局开关控制，sandbox 打开，其他 mod 不受影响。

## 6 分提交实施顺序

### 提交一：设计文档与架构边界

内容：

- 新增本 RFC。
- 更新 `docs/rfcs/README.md`。

### 提交二：通用交互与面板基建

内容：

- 为 ability definition 增加 presentation 元数据。
- 为 `InputOrderMappingSystem` 增加 `PressReleaseAimCast`。
- 为 `EntityCommandPanel` 增加：
- 图标渲染
- 基于施法模式的图标 / hint 选择
- blocked / active 状态位
- sandbox 可启用的全局施法模式工具条

### 提交三：ChampionSkillSandboxMod

内容：

- 新建最小 sandbox mod。
- 提供 EZ / 盖伦 / 杰斯多实例与不同状态样本。
- 通过 formal selection 驱动 focus command panel。
- 通过 `viewmodes.json` 暴露三种施法模式。
- 通过现有 `AbilityAimPresentationRuntime -> PresentationEventStream -> PerformerRuleSystem` 链路渲染所有瞄准预览。

### 提交四：验收

内容：

- 增加最小 acceptance tests。
- 生成 `artifacts/acceptance/champion-skill-sandbox/` 产物。
- 回写正式架构文档。

## 7 验收标准

### 7.1 交互

- 三种施法模式都可由工具面板切换。
- 模式切换是 map / 3C 全局状态，而不是单英雄私有状态。
- 按当前模式施法时，输入行为与预期一致。

### 7.2 技能面板

- 选中不同英雄实例时，技能面板内容实时变化。
- 同一英雄在不同形态或 tag 状态下，技能面板内容实时变化。
- 面板展示技能图标、名字、热键、状态。

### 7.3 GAS 与指示器

- 所有技能通过现有 GAS 主线执行。
- 所有瞄准图形通过 `AbilityAimPresentationRuntime` 发布事件，并由 performer event-condition-action rules 输出。
- 不新增第二套 aim presentation / input stack。

## 8 相关文档

*   [RFC-0054-entity-command-panel-infra.md](RFC-0054-entity-command-panel-infra.md)
*   [../architecture/entity_command_panel_infrastructure.md](../architecture/entity_command_panel_infrastructure.md)
*   [../architecture/entity_selection_architecture.md](../architecture/entity_selection_architecture.md)
*   [../architecture/gas_layered_architecture.md](../architecture/gas_layered_architecture.md)
*   [../architecture/interaction/README.md](../architecture/interaction/README.md)
