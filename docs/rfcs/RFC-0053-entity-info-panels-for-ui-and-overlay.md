---
文档类型: RFC 提案
创建日期: 2026-03-15
维护人: X28技术团队
RFC编号: RFC-0053
状态: Draft
---

# RFC-0053 正式游戏可复用实体信息面板（UI + Overlay 双前端）

本提案定义一套面向正式游戏与诊断场景共用的实体信息面板能力。它不把“实体信息显示”锁死在某个 demo mod、某套输入绑定或某个渲染前端，而是把同一份实体信息数据面同时服务给 `UI` 与 `overlay` 两个前端：正式 HUD / inspector 走 UI，极限诊断与低成本透视走 overlay，二者共享同一套 handle、trigger、实体目标绑定与数据采样逻辑。

本提案只定义架构、API 和 playable mod 设计，不直接替代 `docs/architecture/ui_runtime_architecture.md`、`docs/architecture/gas_layered_architecture.md` 与 `docs/architecture/trigger_guide.md` 的当前实现说明。

## 1 背景

当前仓库已经具备下列可复用基础：

* `src/Libraries/Ludots.UI/UIRoot.cs`
* `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
* `src/Core/Presentation/Hud/ScreenOverlayBuffer.cs`
* `src/Core/Presentation/Hud/PresentationOverlayScene.cs`
* `src/Core/Presentation/Hud/PresentationOverlaySceneBuilder.cs`
* `src/Core/Scripting/TriggerManager.cs`
* `src/Core/Engine/SystemFactoryRegistry.cs`
* `src/Core/Gameplay/GAS/Components/AttributeBuffer.cs`
* `src/Core/Gameplay/GAS/Components/ActiveEffectContainer.cs`
* `src/Core/Gameplay/GAS/Components/EffectModifiers.cs`
* `src/Core/Gameplay/GAS/Components/EffectGrantedTags.cs`
* `src/Core/Gameplay/GAS/Components/TagCountContainer.cs`
* `src/Core/Gameplay/GAS/Components/GameplayTagEffectiveCache.cs`
* `src/Core/Gameplay/GAS/Components/GameplayEffect.cs`
* `src/Core/Gameplay/GAS/Components/EffectContext.cs`
* `src/Core/Gameplay/GAS/Components/EffectStack.cs`
* `src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs`
* `src/Core/Gameplay/GAS/Registry/TagRegistry.cs`
* `docs/architecture/ui_runtime_architecture.md`
* `docs/architecture/gas_layered_architecture.md`
* `docs/architecture/trigger_guide.md`
* `docs/architecture/ecs_soa.md`

当前缺口也很明确：

1. `UIRoot` 当前只有单一 `Scene` 挂载点，`MountScene` / `ClearScene` 直接切换整棵场景树，适合单面板 controller，但不适合正式游戏里多个系统并存贡献 UI 子树。
2. overlay 侧已经有共享的 `ScreenOverlayBuffer` 与 retained `PresentationOverlayScene`，但 `ScreenOverlayBuffer` 目前只暴露简单 `AddText` / `AddRect`，没有面向多实例信息面板的 handle、stable-id、dirty-serial 与容量治理。
3. GAS 数据本身已经存在于 ECS 组件中，但“面向展示的实体信息采样面”尚未抽象出来；不同 mod 现在各自读 `AttributeBuffer`、`ActiveEffectContainer`、`TagCountContainer` 等组件，缺少统一 API。

因此，这不是一个“把现有 debug panel 改大一点”的 feature，而是一个必须 infrastructure-first 处理的正式能力。

## 2 目标

本提案的目标是：

1. 提供一个通用实体信息面板能力，支持 `UI` 与 `overlay` 双前端。
2. 支持两类面板：
   * `ComponentInspector`：显示指定实体的全部 component 与值，并支持按 component 开关显示。
   * `GasInspector`：只显示指定实体的 tag 与 attribute，并支持开关显示 attribute aggregate 来源与 modifier 状态。
3. 面板实例通过 handle 控制生命周期，允许同屏多个实例并存。
4. 面板显示/关闭可由 trigger 驱动，但不绑定任何无关输入系统。
5. 面板布局可配置锚点、偏移、尺寸与前端目标。
6. 数据采样遵循 `docs/architecture/ecs_soa.md` 的 SoA / Zero-GC 原则，保证大量实体信息同时显示时不会把成本扩散到 ECS 热路径。
7. 提供一个从玩家角度可理解的 demo mod 设计，使能力既能做诊断，也能表达正式游戏使用方式。

## 3 非目标

本提案不做以下事情：

1. 不新增第二套 UI runtime，不绕开 `src/Libraries/Ludots.UI/`。
2. 不把实体信息显示逻辑写进某个具体 demo mod 的专属 system。
3. 不要求 overlay 与 UI 使用两套不同的 handle、target 绑定或采样实现。
4. 不把全局键鼠输入硬编码进面板生命周期；UI 点击只是 UI 前端的可选交互，不是能力前提。
5. 不在 ECS 聚合热路径里常驻写调试字符串或临时对象。

## 4 复用基建

复用基建：

* Registry: `SystemFactoryRegistry`，用于注册正式 presentation system 或 schema host。
* Pipeline: `TriggerManager` / `context.OnEvent(...)`，用于通过事件驱动打开、关闭、更新面板。
* Pipeline: `UiScene` / `ReactivePage<TState>`，用于 UI 前端的 retained diff、滚动与虚拟窗口。
* Pipeline: `ScreenOverlayBuffer` → `PresentationOverlaySceneBuilder` → `PresentationOverlayScene`，用于 overlay 前端的共享叠加层。
* System: `UIRoot`，作为 UI 渲染入口；但其单 `Scene` 约束要求补一个共享 host，而不是让每个面板直接抢 `MountScene(...)`。
* GAS Data: `AttributeBuffer`、`ActiveEffectContainer`、`EffectModifiers`、`EffectGrantedTags`、`GameplayEffect`、`EffectContext`、`EffectStack`、`TagCountContainer`、`GameplayTagEffectiveCache`。
* GAS Registry: `AttributeRegistry`、`TagRegistry`，用于把 ID 还原成稳定显示名。
* ECS Meta: `World.GetSignature(entity)` 与 `ComponentType.Type`，用于枚举实体 component 结构。

复用结论：

* overlay host 已存在，但需要为 retained 面板补 stable-id / dirty-serial / capacity 约束。
* UI host 不存在；正式游戏若要让多个系统共同贡献 UI，必须先抽一个统一的 UI layer host。
* 面板本体不应直接依赖输入层；trigger、handle 与 host service 才是正式入口。

## 5 关键设计结论

### 5.1 一份数据面，两个前端

实体信息显示必须拆成三层：

1. `EntityInfoPanelService`
   负责 handle、实例生命周期、目标实体绑定、配置、开关状态与数据采样调度。
2. `EntityInfoSnapshotStore`
   负责把 ECS / GAS 数据采样为面向展示的 SoA 行缓冲。
3. Presenter
   * `EntityInfoUiPresenter`
   * `EntityInfoOverlayPresenter`

两个 presenter 只读 snapshot，不自己直接扫描 ECS。

### 5.2 UI 是正式交互前端，overlay 是正式诊断前端

本能力必须同时支持：

* `UI`：适合正式游戏内 inspector、可点击折叠、滚动、可视化 section、多人共屏停靠。
* `overlay`：适合极限诊断、战斗内只读卡片、性能紧张路径、无输入依赖的快速观测。

单实例可以选择其前端：

* 只走 `UI`
* 只走 `overlay`
* 同时镜像到 `UI + overlay`

### 5.3 正式游戏 UI 侧必须先补 Host，不允许继续直抢 `UIRoot.Scene`

因为 `src/Libraries/Ludots.UI/UIRoot.cs` 当前只有单一 `Scene`，正式游戏如果同时有主 HUD、系统菜单、实体信息面板、剧情提示页，就不能继续采用“谁最后 `MountScene(...)` 谁生效”的 controller 模式。

因此，本提案要求新增共享 host：

* `IUiHudLayerHost`
* `UiHudLayerHandle`
* `IUiHudLayerContributor`

它们的职责是让多个系统贡献 UI 子树，而由 host 统一产出一个 `UiScene` 给 `UIRoot`。

overlay 侧不新增平行 host，继续复用共享 `ScreenOverlayBuffer` / `PresentationOverlayScene` 链，但需要补 retained 面板需要的元数据。

## 6 提议 API

以下 API 均为新提议，不是当前仓库已存在实现。

### 6.1 Handle 与实例

```csharp
public readonly record struct EntityInfoPanelHandle(int Slot, int Generation)
{
    public static EntityInfoPanelHandle Invalid => new(-1, 0);
    public bool IsValid => Slot >= 0 && Generation > 0;
}

[Flags]
public enum EntityInfoPanelSurface
{
    None = 0,
    Ui = 1 << 0,
    Overlay = 1 << 1,
}

public enum EntityInfoPanelKind : byte
{
    ComponentInspector = 0,
    GasInspector = 1,
}

public enum EntityInfoPanelAnchor : byte
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3,
    Center = 4,
}

public readonly record struct EntityInfoPanelLayout(
    EntityInfoPanelAnchor Anchor,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height);
```

### 6.2 目标实体绑定

```csharp
public enum EntityInfoTargetKind : byte
{
    FixedEntity = 0,
    ServiceKeyEntity = 1,
    ScriptContextEntity = 2,
}

public readonly record struct EntityInfoPanelTarget(
    EntityInfoTargetKind Kind,
    Entity FixedEntity,
    string Key);
```

说明：

* `FixedEntity` 用于直接绑定某个实体。
* `ServiceKeyEntity` 用于绑定 `CoreServiceKeys.SelectedEntity`、`CoreServiceKeys.HoveredEntity` 等正式游戏状态。
* `ScriptContextEntity` 用于 trigger / command 在上下文里传目标实体。

### 6.3 明细开关

```csharp
[Flags]
public enum EntityInfoGasDetailFlags
{
    None = 0,
    ShowAttributeAggregateSources = 1 << 0,
    ShowModifierState = 1 << 1,
}

public readonly record struct EntityInfoPanelRequest(
    EntityInfoPanelKind Kind,
    EntityInfoPanelSurface Surface,
    EntityInfoPanelTarget Target,
    EntityInfoPanelLayout Layout,
    EntityInfoGasDetailFlags GasDetailFlags,
    bool Visible);
```

### 6.4 统一服务

```csharp
public interface IEntityInfoPanelService
{
    EntityInfoPanelHandle Open(in EntityInfoPanelRequest request);
    bool Close(EntityInfoPanelHandle handle);
    bool SetVisible(EntityInfoPanelHandle handle, bool visible);
    bool UpdateLayout(EntityInfoPanelHandle handle, in EntityInfoPanelLayout layout);
    bool UpdateTarget(EntityInfoPanelHandle handle, in EntityInfoPanelTarget target);
    bool UpdateGasDetailFlags(EntityInfoPanelHandle handle, EntityInfoGasDetailFlags flags);

    bool SetComponentEnabled(EntityInfoPanelHandle handle, ComponentType componentType, bool enabled);
    bool SetAllComponentsEnabled(EntityInfoPanelHandle handle, bool enabled);
}
```

设计要求：

* service 是唯一 handle 生成者。
* handle 采用 slot + generation，防止关闭后旧引用误操作新实例。
* component 开关是实例级状态，不污染实体本身，也不污染其他面板实例。

### 6.5 Trigger / Command 入口

```csharp
public sealed class OpenEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;
    public EntityInfoPanelRequest Request { get; init; }
}

public sealed class CloseEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;
}

public sealed class UpdateEntityInfoPanelCommand : GameCommand
{
    public string HandleSlotKey { get; init; } = string.Empty;
    public EntityInfoPanelLayout? Layout { get; init; }
    public EntityInfoPanelTarget? Target { get; init; }
    public EntityInfoGasDetailFlags? GasDetailFlags { get; init; }
}
```

说明：

* trigger 不直接持有 runtime handle，而是通过 `HandleSlotKey` 解析到 mod runtime 存储的 handle。
* 这样 trigger 配置、脚本命令与运行时 handle 生命周期解耦。

## 7 数据面设计

### 7.1 SoA 实例存储

`EntityInfoPanelService` 内部实例表采用 SoA：

* `Kinds[]`
* `Surfaces[]`
* `Targets[]`
* `Layouts[]`
* `Visible[]`
* `GasDetailFlags[]`
* `UiDirtySerial[]`
* `OverlayDirtySerial[]`
* `Generation[]`

不为每个实例 new controller / page / overlay object。

### 7.2 Snapshot Store

`EntityInfoSnapshotStore` 为每个实例维护定长行缓冲与 section 索引，不在采样时构造 `List<T>`：

* `PanelHeaderRow[]`
* `ComponentRow[]`
* `AttributeRow[]`
* `TagRow[]`
* `ModifierRow[]`
* `AggregateSourceRow[]`
* `SectionRange[]`

每个实例通过 `StartIndex + Count` 指向自己的行窗口。

### 7.3 ComponentInspector 采样

对 `ComponentInspector`：

1. 通过 `World.GetSignature(entity)` 枚举 `ComponentType`。
2. 组件顺序按 `ComponentType.Id` 稳定排序。
3. 每个 component 先写 header row，再按 formatter 输出 value rows。
4. component 开关只影响 value rows 是否展开，不移除 header row。

为满足“显示所有 component 与值”，需要补一个统一的 component value formatter registry：

```csharp
public interface IEntityComponentValueFormatter
{
    ComponentType ComponentType { get; }
    int WriteRows(Entity entity, World world, ref EntityInfoRowWriter writer);
}
```

要求：

* formatter registry 在 schema / startup 阶段注册，运行时只查表。
* 对高频热组件使用手写 formatter。
* 对未手写组件，允许用一次性缓存后的字段元数据 walker 输出值，但不得在每帧重新反射扫描类型结构。

### 7.4 GasInspector 采样

对 `GasInspector`：

* tag 区：
  * `TagCountContainer`
  * `GameplayTagEffectiveCache`
  * `TagRegistry`
* attribute 区：
  * `AttributeBuffer`
  * `AttributeRegistry`
* modifier / source 区：
  * `ActiveEffectContainer`
  * `EffectModifiers`
  * `EffectGrantedTags`
  * `GameplayEffect`
  * `EffectContext`
  * `EffectStack`
  * `EffectTemplateRef`

语义定义：

1. `ShowAttributeAggregateSources`
   显示“哪些 effect / source entity / template 正在为该 attribute 或 tag 提供贡献”。
2. `ShowModifierState`
   显示 effect 当前状态，例如：
   * `GameplayEffect.State`
   * `GameplayEffect.RemainingTicks`
   * `EffectStack.Count`
   * `EffectContext.Source`

约束：

* 对普通 modifier 与 tag contribution，可通过 `ActiveEffectContainer` 回查 effect entity 直接得出。
* 对 derived graph，目前仓库只有 `AttributeDerivedGraphBinding` 与 graph program 绑定，没有现成的逐 attribute 贡献 trace；因此本提案只要求展示“由哪个 graph program 参与最终聚合”，不在第一阶段承诺节点级贡献明细。

## 8 前端设计

### 8.1 UI Presenter

`EntityInfoUiPresenter` 通过 `IUiHudLayerHost` 提供一个 retained layer。

UI 前端职责：

* 大面板
* 可滚动 section
* component 展开/折叠
* attribute / tag / source 分区
* 多实例栈式停靠
* 高密度信息的虚拟窗口

UI 前端不直接读取 ECS，只消费 snapshot。

### 8.2 Overlay Presenter

overlay 继续走：

* `ScreenOverlayBuffer`
* `PresentationOverlaySceneBuilder`
* `PresentationOverlayScene`

但需要补两点：

1. `ScreenOverlayItem` 增加 `StableId` / `DirtySerial`
2. `ScreenOverlayBuffer` 提升为面板可用容量，避免大面板或多实例时过早打满

overlay 前端职责：

* 小面板 / 低成本卡片
* 战斗内只读观察
* 大量实例同时显示
* 无输入依赖

overlay 不承担复杂折叠交互；复杂交互留给 UI。

## 9 性能与零分配要求

### 9.1 不在 ECS 热路径写展示对象

以下行为禁止进入 `AttributeAggregatorSystem`、`EffectLifetimeSystem` 等热路径：

* `new string`
* `new List<T>`
* `JsonSerializer`
* 每帧反射扫描 component 类型

实体信息采样必须作为独立 presentation/diagnostics 阶段执行，只对“当前被面板观察的实体”生效。

### 9.2 观察集是显式的

service 维护显式观察集：

* 只有可见实例绑定到的实体才进入采样。
* 同一实体被多个实例观察时，共享一次实体快照，再由不同 presenter 做视图裁剪。

### 9.3 脏序列分离

每个实例维护：

* `DataDirtySerial`
* `UiDirtySerial`
* `OverlayDirtySerial`

规则：

* 实体数据变化只脏化绑定到该实体的实例。
* layout 变化只脏化对应前端。
* UI 折叠状态变化不强迫 overlay 重新构建。

### 9.4 文本生成延迟到展示边界

snapshot 层优先保存结构化行数据，不直接保存最终字符串。

* UI 前端在 retained 节点 patch 时生成少量可视文本。
* overlay 前端在最终写 `ScreenOverlayBuffer` 时生成文本。

这样可以把字符串分配限制在“当前可见行”，而不是“所有被观察实体的所有字段”。

## 10 Demo Mod 设计

建议新增：

* `mods/showcases/entity-info/EntityInfoShowcaseMod/`

### 10.1 用户视角目标

玩家进入 showcase 后，不是看一块技术面板，而是做三件直观事情：

1. 选中一个英雄，看到正式 UI 版 `ComponentInspector`。
2. 让同一英雄叠加 buff / debuff，看到 `GasInspector` 在 UI 与 overlay 两个前端上同步表达。
3. 切到 stress 场景，同时打开多个 overlay 小卡片，确认大量实例也不会把主循环打崩。

### 10.2 建议场景

#### 场景 A：单英雄正式 HUD

* 地图加载后 trigger 打开一个 `UI` `GasInspector`
* 目标绑定 `CoreServiceKeys.SelectedEntity`
* 右上角显示 attribute / tag
* 用户点击 UI 内 section 开关查看 aggregate source 与 modifier state

#### 场景 B：组件透视台

* 地图加载后 trigger 打开一个 `UI` `ComponentInspector`
* 左侧显示当前选中实体所有 component
* 用户在 UI 内按 component 展开/折叠

#### 场景 C：战斗内 overlay 压力墙

* trigger 批量打开 16 到 64 个 `overlay` `GasInspector`
* 每个实例绑定不同实体，锚点按网格铺开
* 用于验证多实例、稳定 handle、低成本文本输出与 dirty 刷新

### 10.3 Trigger 表达方式

建议 demo mod 提供以下 trigger 入口：

* `GameStart`：注册 service、UI host contributor、presentation system
* `MapLoaded(showcase_hud)`：打开正式 UI inspector
* `MapLoaded(showcase_components)`：打开 component inspector
* `MapLoaded(showcase_stress)`：批量打开 overlay inspectors
* `MapUnloaded(*)`：按 handle slot 关闭对应实例

### 10.4 用户可见成功标准

* 同一个实体可以同时被 UI 与 overlay 两个前端观测，且内容语义一致。
* UI inspector 不依赖全局热键才能开关 section。
* overlay 压力场景下，多实例面板不会把其他 HUD 或主战斗流程拖慢到不可用。
* trigger 可以打开、关闭、换目标、换锚点，而不需要重新绑输入系统。

## 11 建议落地顺序

1. 先补 `IUiHudLayerHost`
   因为正式游戏 UI 侧没有这个 host，就无法把实体信息面板安全接入主 HUD。
2. 升级 overlay 元数据
   在 `ScreenOverlayBuffer` / `ScreenOverlayItem` 上补 stable-id、dirty-serial 与容量策略。
3. 落 `EntityInfoPanelService` + `EntityInfoSnapshotStore`
   先把数据面与 handle 做稳，再接前端。
4. 先交付 `GasInspector`
   因为它复用现有 GAS 组件最直接。
5. 再交付 `ComponentInspector`
   它需要完整的 component formatter registry。
6. 最后补 `EntityInfoShowcaseMod`
   用来产出面向用户的表达与 acceptance evidence。

## 12 风险与开放问题

1. `UIRoot` 单场景约束是正式接入的硬阻塞，不先补 host，会继续形成“谁最后挂 scene 谁赢”的隐式耦合。
2. derived graph 目前没有节点级 attribute 贡献 trace；第一阶段只能展示 graph program 参与事实，不能展示更细的聚合来源。
3. component 全量值显示若完全依赖冷路径反射，正式场景下仍可能抖动；必须把 formatter registry 作为正式基建，而不是 demo 代码。
4. overlay 面板若继续沿用当前 `ScreenOverlayBuffer.MaxItems` / `MaxStrings` 规模，stress 场景会过早溢出。

## 13 相关文档

* UI 运行时 SSOT：见 [../architecture/ui_runtime_architecture.md](../architecture/ui_runtime_architecture.md)
* GAS 分层与 sink：见 [../architecture/gas_layered_architecture.md](../architecture/gas_layered_architecture.md)
* Trigger 开发指南：见 [../architecture/trigger_guide.md](../architecture/trigger_guide.md)
* ECS SoA 原则：见 [../architecture/ecs_soa.md](../architecture/ecs_soa.md)
* 文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
