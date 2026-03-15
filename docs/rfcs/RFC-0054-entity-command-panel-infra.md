# RFC-0054 通用实体指令面板基础设施与演示 Mod 设计

本文提出一个通用实体指令面板（Entity Command Panel）基础设施，用于在统一 UI Runtime 上显示实体技能组，并通过 trigger 与句柄驱动开关、切组、重绑定与多实例并存。它不定义正式规范；正式结论在实现后必须回写到 `docs/architecture/` 或 `docs/reference/`。

## 1 问题与结论

当前仓库已经具备三块可复用基础：

* `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs` 与 `src/Libraries/Ludots.UI/Runtime/UiScene.cs` 已提供 retained diff、虚拟窗口与运行时窗口刷新能力。
* `src/Core/Gameplay/GAS/Components/AbilityStateBuffer.cs`、`GrantedSlotBuffer`、`AbilityFormSlotBuffer` 与 `AbilitySlotResolver` 已提供按 slot 解析基础技能、形态覆盖与临时授予覆盖的能力。
* `src/Core/Engine/SystemFactoryRegistry.cs` 与 `src/Core/Scripting/TriggerManager.cs` 已提供按 Mod 注册系统、由 trigger/Event 激活的生命周期入口。

但当前仍缺三项基础设施缺口：

1. `src/Libraries/Ludots.UI/UIRoot.cs` 一次只能挂一棵 `UiScene`，现有交互面板都是各自直接 `MountScene(...)`，没有可复用的多实例共享宿主。
2. `mods/CoreInputMod/Systems/SkillBarOverlaySystem.cs` 只能绘制单条 Native HUD skill bar，无法表达技能组切换、多实例、锚点尺寸配置，也没有 handle 生命周期。
3. `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs` 只有执行与 indicator 元数据，没有“面板展示元数据”与“面板数据提供契约”。

结论：这不是一个可以直接塞进某个 demo mod 的普通 feature，而是一个需要先补 Core 扩展点的基础设施任务。

## 2 复用清单

复用基建：

* Registry: `SystemFactoryRegistry` — 注册可选 presentation system，由 map trigger 或 event handler 激活实体指令面板宿主。
* Registry: `AbilityDefinitionRegistry` — 提供 abilityId 到执行定义的查找入口，面板运行时据此解析已装备 slot。
* Pipeline: `ReactivePage<TState>` → `UiScene.ApplyReactiveRoot(...)` → `UIRoot.Render()` — 单一 UI runtime，用于 retained diff、多实例组合与虚拟窗口。
* Pipeline: `AbilityStateBuffer` + `AbilityFormRoutingSystem` + `AbilitySlotResolver.Resolve(...)` — 把实体当前 slot 解析成最终可显示技能。
* Pipeline: `TriggerManager.FireEvent(...)` / `FireMapEvent(...)` — 允许 trigger 在不绑定输入系统的前提下控制面板开关与切组。
* System: `AbilityIndicatorOverlayBridge`、`ContextScoredOrderResolver` — 已证明 slot 解析、form/granted 层合并与 context group 查询可以在 Core 中集中完成。
* Mod: `InteractionShowcaseMod` — 可复用其多交互模式、form routing 与 showcase 表达方式。
* Mod: `CameraAcceptanceMod` — 可复用其 reactive panel、多区块 UI、虚拟窗口与性能观测方法。

## 3 设计目标与非目标

目标：

* 一个宿主系统内支持多个实体指令面板实例同时显示。
* 每个实例都能独立配置锚点、尺寸、目标实体、当前技能组与显示状态。
* trigger 和 C# 代码都通过 handle 控制实例，不直接耦合任何输入系统。
* 面板按 slot 显示，并能切换技能组。
* 支持基础 slot、形态覆盖 slot、授予覆盖 slot 的统一展示。
* 大量实体信息显示时保持 SoA、零分配、共享缓存与 retained diff，不引入整页重建抖动。

非目标：

* 不在第一版直接定义权威输入映射或技能点击施法链路。
* 不新增第二套 UI runtime，不绕开 `UiScene` / `ReactivePage`。
* 不把“技能展示名称、图标、组信息”硬塞进无关输入系统或 host adapter。

## 4 拟新增 Core API

本节 API 全部为拟新增契约；现有 API 路径已在上一节列出。

### 4.1 面板句柄与控制服务

```csharp
public readonly record struct EntityCommandPanelHandle(int Slot, uint Generation)
{
    public bool IsValid => Slot >= 0 && Generation != 0;
}

public enum EntityCommandPanelAnchorPreset : byte
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    BottomCenter,
    Center
}

public readonly struct EntityCommandPanelAnchor
{
    public EntityCommandPanelAnchorPreset Preset { get; init; }
    public Vector2 OffsetPx { get; init; }
}

public readonly struct EntityCommandPanelSize
{
    public float WidthPx { get; init; }
    public float HeightPx { get; init; }
}

public readonly struct EntityCommandPanelOpenRequest
{
    public Entity TargetEntity { get; init; }
    public string SourceId { get; init; }
    public string InstanceKey { get; init; }
    public EntityCommandPanelAnchor Anchor { get; init; }
    public EntityCommandPanelSize Size { get; init; }
    public int InitialGroupIndex { get; init; }
    public bool StartVisible { get; init; }
}

public interface IEntityCommandPanelService
{
    EntityCommandPanelHandle Open(in EntityCommandPanelOpenRequest request);
    bool Close(EntityCommandPanelHandle handle);
    bool SetVisible(EntityCommandPanelHandle handle, bool visible);
    bool RebindTarget(EntityCommandPanelHandle handle, Entity targetEntity);
    bool SetGroupIndex(EntityCommandPanelHandle handle, int groupIndex);
    bool CycleGroup(EntityCommandPanelHandle handle, int delta);
    bool SetAnchor(EntityCommandPanelHandle handle, in EntityCommandPanelAnchor anchor);
    bool SetSize(EntityCommandPanelHandle handle, in EntityCommandPanelSize size);
    bool TryGetState(EntityCommandPanelHandle handle, out EntityCommandPanelInstanceState state);
}
```

设计说明：

* `Handle = Slot + Generation`，对齐仓库里 `PathHandle` 与 persistent slot/generation 的防悬挂模式。
* `InstanceKey` 只用于打开阶段的幂等定位与脚本侧别名绑定，不参与热路径遍历。
* `IEntityCommandPanelService` 只暴露显隐、切组、重绑定与布局控制，不读取 `PlayerInputHandler`。

### 4.2 脚本侧 handle 门面

trigger/JSON 无法直接保存运行时句柄，因此需要一层脚本门面：

```csharp
public interface IEntityCommandPanelHandleStore
{
    bool TryBind(string alias, EntityCommandPanelHandle handle);
    bool TryGet(string alias, out EntityCommandPanelHandle handle);
    bool Remove(string alias);
}
```

配套拟新增命令：

* `OpenEntityCommandPanelCommand`
* `CloseEntityCommandPanelCommand`
* `SetEntityCommandPanelVisibilityCommand`
* `SetEntityCommandPanelGroupCommand`
* `RebindEntityCommandPanelTargetCommand`

这些命令只依赖 `CoreServiceKeys.EntityCommandPanelService` 与 `CoreServiceKeys.EntityCommandPanelHandleStore`，不依赖输入系统。

### 4.3 面板数据源契约

实体指令面板不直接耦合某个具体玩法 Mod，而是通过 source 查询数据：

```csharp
public readonly struct EntityCommandPanelGroupView
{
    public int GroupId { get; init; }
    public string GroupLabel { get; init; }
    public byte SlotCount { get; init; }
}

public readonly struct EntityCommandPanelSlotView
{
    public int SlotIndex { get; init; }
    public int AbilityId { get; init; }
    public EntityCommandSlotStateFlags StateFlags { get; init; }
    public short CooldownPermille { get; init; }
    public short ChargesCurrent { get; init; }
    public short ChargesMax { get; init; }
}

public interface IEntityCommandPanelSource
{
    bool TryGetRevision(Entity target, out uint revision);
    int GetGroupCount(Entity target);
    bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group);
    int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination);
}
```

设计说明：

* `CopySlots(..., Span<T>)` 避免 `IEnumerable<T>`、`List<T>` 与闭包分配。
* `TryGetRevision(...)` 用于 panel host 做增量刷新与多实例共享缓存。
* 第一版默认 source 由 GAS 提供，实现“按 slot 显示当前技能组”；后续允许召唤物、建筑、卡牌单位注册自己的 source。

### 4.4 技能展示元数据注册表

`AbilityDefinitionRegistry` 当前没有展示字段，因此建议补一套展示注册表，而不是把 UI 文案塞进执行定义：

```csharp
public readonly struct AbilityPresentationDefinition
{
    public string DisplayName { get; init; }
    public string ShortName { get; init; }
    public string IconToken { get; init; }
    public int DefaultGroupId { get; init; }
    public byte PreferredSlotIndex { get; init; }
    public EntityCommandAbilityKind AbilityKind { get; init; }
}

public sealed class AbilityPresentationRegistry
{
    public void Register(int abilityId, in AbilityPresentationDefinition definition);
    public bool TryGet(int abilityId, out AbilityPresentationDefinition definition);
}
```

建议配置入口：

* `GAS/ability_presentations.json`：展示名、短名、图标 token、默认组、展示顺序。
* `GAS/command_panel_groups.json`：当一个实体需要显式多组编排时，定义 group id、label、包含哪些 slot 或 abilityId。

这样 `AbilityDefinitionRegistry` 继续只负责执行定义，展示契约单独放在 GAS/Presentation 边界。

## 5 运行时结构

### 5.1 宿主模式

第一版不允许每个面板各自 `MountScene(...)`。应新增一个统一宿主 presentation system，例如：

* `EntityCommandPanelHostSystem`
* `EntityCommandPanelRuntime`
* `EntityCommandPanelController`

宿主职责：

* 独占一个 `ReactivePage<EntityCommandPanelHostState>`。
* 在一棵 root scene 里组合所有活动实例。
* 对每个实例输出一个 card；多个实例共享同一 `UiScene`、`UiDispatcher` 与 retained diff。

这与当前 `CameraAcceptancePanelController`、`InteractionShowcasePanelController` 的“单页单宿主”模式一致，但升级成“多实例单宿主”。

### 5.2 SoA 存储

实例热路径采用 SoA：

* `Entity[] Targets`
* `uint[] Generations`
* `byte[] VisibleFlags`
* `short[] GroupIndices`
* `EntityCommandPanelAnchor[] Anchors`
* `EntityCommandPanelSize[] Sizes`
* `uint[] SourceRevisions`
* `int[] SourceIds`

脚本别名、字符串 label、图标 token 只保留在冷路径表或 registry 中；热路径只处理整数 id、索引与 blittable struct。

### 5.3 零分配与共享缓存

必须满足：

* panel host 每帧不创建 `List<>`、闭包或 LINQ 枚举器。
* slot 视图复制到复用的 `Span<EntityCommandPanelSlotView>` 或固定 scratch buffer。
* 同一实体、同一组被多个实例同时展示时，按 `(Entity, SourceId, GroupIndex, Revision)` 共享一次解析结果。
* 组切换只刷新目标实例，不触发全量 remount。
* slot 数过多时使用 `ReactiveContext.GetVerticalVirtualWindow(...)` 做局部组合。

### 5.4 显示与动作边界

面板默认是“可观测 HUD”，不是输入权威源：

* 显示状态来自 ECS + source + registry。
* trigger、脚本或 UI click 只向外发出意图事件，不直接执行施法逻辑。
* 如果后续要支持“点 slot 发出施法请求”，也必须经由独立 callback sink 或 command bus，而不是在面板内部读取 `PlayerInputHandler`。

## 6 GAS 侧分组语义

通用实体指令面板至少需要支持三类组来源：

1. 基础技能组：来自 `AbilityStateBuffer` 当前 `Count`。
2. 形态技能组：来自 `AbilityFormRoutingSystem` 解析后的 `AbilityFormSlotBuffer`。
3. 上下文/授予技能组：来自 `GrantedSlotBuffer`、`ContextGroupRegistry` 或专用 source。

推荐显示层规则：

* panel 永远按“当前生效 slot”显示，而不是同时暴露基础值与覆盖值。
* slot 上额外标记覆盖来源：
  `Base`、`FormOverride`、`GrantedOverride`、`ContextVariant`。
* 组切换不修改 GAS 真相，只切换展示 source 或 source 内部视图。

## 7 演示 Mod 设计

建议新增独立 playable mod：

* `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod`

它不应把基础设施写死在 Mod 内；Mod 只消费 `IEntityCommandPanelService`、`AbilityPresentationRegistry` 与一个默认 GAS source。

### 7.1 玩家流

1. 进入 Showcase Hub，默认打开主角面板。
2. 通过场景 trigger 再打开 2 到 3 个额外实例，对比不同实体、不同锚点、不同尺寸。
3. 切换主角 stance / form，观察 slot 仍保持相同位置，但技能内容替换。
4. 获得临时 buff 或装备，观察某些 slot 被 granted override 替换。
5. 通过场景按钮或 trigger 命令关闭、重新打开、重绑定同一实例。
6. 进入 stress map，同时显示多实体摘要与 1 到 4 个大面板，验证 retained diff 与 frame budget。

### 7.2 建议地图拆分

* `entity_command_panel_hub`
  单实体、多组、基础开关。
* `entity_command_panel_multi_instance`
  多实例、不同锚点尺寸、同实体/不同实体对比。
* `entity_command_panel_trigger_flow`
  通过 trigger 打开、关闭、切组、重绑定、延迟关闭。
* `entity_command_panel_stress`
  大量实体更新、技能覆盖频繁切换、虚拟窗口与 diff telemetry。

### 7.3 演示重点

* Arcweaver 一类 form 切换实体。
* Commander 一类 context-scored / group route 实体。
* 召唤物或建筑实体，用于证明“不是只有英雄能用”。
* 观测项：`UiReactiveUpdateMetrics`、组切换次数、slot patch 数、可见窗口范围、共享缓存命中率。

## 8 从用户视角出发的功能清单

基础展示：

* 显示实体名称、阵营/类型、当前技能组标签。
* 按 slot 稳定显示技能，不因 form/grant 变化打乱位置。
* 显示空 slot、锁定 slot、冷却中 slot、充能 slot、被授予覆盖 slot。
* 显示技能名、短名、图标 token、冷却进度、层数/充能数、状态标签。

面板生命周期：

* 打开、关闭、隐藏、显示。
* 通过 handle 精确控制某一个实例。
* 同时打开多个实例而互不覆盖。
* 同一实例重绑定到另一实体。

技能组能力：

* 直接切到指定组。
* 上一组/下一组循环切换。
* 自动跟随实体 form 变化刷新当前组。
* 支持“展示组”和“逻辑 form”解耦。

布局表达：

* 每个实例独立配置锚点。
* 每个实例独立配置宽高。
* 小尺寸紧凑条、标准卡片、大尺寸说明卡并存。
* 同一实体可同时显示一个精简条和一个详细卡。

触发控制：

* `MapLoaded` 自动打开。
* 进入区域自动打开或切组。
* 获得某 tag 时弹出临时技能组。
* boss 战开始时打开指令面板，结束后关闭。
* 通过脚本把同一 handle 重新绑定到剧情实体或队友实体。

性能与诊断：

* 大量实体状态变化时只 patch 受影响 slot。
* 多实例观察同一实体时复用解析缓存。
* slot 列表长时只组合可见窗口。
* 暴露 diff、虚拟窗口、缓存命中率、每帧刷新面板数。

可选增强：

* slot hover 说明卡。
* 点击 slot 发出“意图事件”而非直接施法。
* 可切换紧凑模式、战术模式、教学模式。
* 支持 pinned compare，对比自己与队友/敌人的技能组。

## 9 建议实施顺序

1. 先补 Core：`IEntityCommandPanelService`、SoA runtime store、统一 host system、handle store。
2. 再补 GAS 展示层：`AbilityPresentationRegistry` 与默认 GAS source。
3. 然后做 showcase mod：hub、多实例、trigger、stress 四张图。
4. 最后把 accepted 结论回写到 `docs/architecture/ui_runtime_architecture.md` 与相关 GAS/trigger 文档。

## 10 相关文档

* 当前 UI runtime：见 [../architecture/ui_runtime_architecture.md](../architecture/ui_runtime_architecture.md)
* 当前 GAS 分层：见 [../architecture/gas_layered_architecture.md](../architecture/gas_layered_architecture.md)
* Trigger 体系：见 [../architecture/trigger_guide.md](../architecture/trigger_guide.md)
* RFC 索引：见 [README.md](README.md)
