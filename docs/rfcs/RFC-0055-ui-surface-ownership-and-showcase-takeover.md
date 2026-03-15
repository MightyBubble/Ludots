---
文档类型: RFC 提案
创建日期: 2026-03-15
维护人: X28技术团队
RFC编号: RFC-0055
状态: Draft
---

# RFC-0055 UI surface ownership 与 showcase takeover 契约

本文提出一套面向 retained UI、overlay、world HUD 与 adapter 叠加层的 surface ownership / lease / restore 契约，用于解决多个 mod 或 showcase 同时贡献玩家可见界面时的抢占、闪烁、残留与恢复缺失问题。它不直接修改现有 `docs/architecture/ui_runtime_architecture.md` 与 `docs/architecture/entity_command_panel_infrastructure.md` 的正式结论；RFC 被接受后，正式规则必须回写到 `docs/conventions/` 与 `docs/architecture/`。

## 1 背景

当前仓库在玩家可见界面上已经具备多套可复用基建：

*   `src/Libraries/Ludots.UI/UIRoot.cs`
*   `src/Libraries/Ludots.UI/Runtime/UiScene.cs`
*   `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
*   `src/Core/Presentation/Hud/ScreenOverlayBuffer.cs`
*   `src/Core/Presentation/Hud/PresentationOverlayScene.cs`
*   `src/Core/Engine/SystemFactoryRegistry.cs`
*   `src/Core/Scripting/TriggerManager.cs`
*   `mods/capabilities/entityinfo/EntityInfoPanelsMod/`
*   `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/`

本次实体信息面板与实体指令面板开发暴露出一个共同缺口：系统可以各自“把东西画出来”，但仓库还没有一个正式的 surface owner / takeover 契约来约束多个界面宿主之间的关系。问题表现为：

1. `UIRoot` 当前只维护单一 `UiScene`，多个 controller 若直接 `MountScene(...)`，最终效果取决于最后一次写入。
2. showcase 或 demo mod 为了让自己的界面独占显示，容易引入临时 suppression flag；一旦缺少 restore 约束，就会出现闪烁标题、残留面板或 resume 后状态错误。
3. engine-side 的“状态对了”不代表玩家实际看到的界面正确；若没有 adapter-visible 的 ownership 验收，问题只能在手工试玩时偶发暴露。
4. UI 面板接管 world-click 或选择行为时，仓库缺少统一的交互边界表达，容易出现界面显示正常但实体选不中、相机行为失效等回归。

结论：这不是单个 mod 的实现瑕疵，而是表现层基础设施与开发规范的共同缺口。

## 2 目标

本 RFC 目标如下：

1. 为 retained UI、overlay、world HUD 与 adapter overlay 定义统一的 surface 类型与 owner 语义。
2. 为 showcase / mod 临时接管已有界面提供显式 lease / restore 契约，而不是依赖临时 suppression。
3. 把 `MapLoaded`、`MapResumed`、`MapUnloaded` 上的 acquire / revalidate / release 生命周期固化为正式规则。
4. 保持与输入系统松耦合；surface ownership 只解决显示面与可见性归属，不直接决定输入采样。
5. 为 feature workflow 与 skill 提供统一验收锚点，确保玩家可见正确性成为第一等约束。

## 3 非目标

本 RFC 不做以下事情：

1. 不新增第二套 UI runtime，也不绕开 `src/Libraries/Ludots.UI/`。
2. 不把所有 UI 都强制改成同一种 presenter 写法。
3. 不把点击、hover、相机、选择等输入决策直接塞进 surface lease 服务。
4. 不把“临时 suppression flag”包装成长期正式 API；它只能作为过渡期 mitigation。

## 4 Surface 模型

建议把玩家可见 surface 固定为以下几类：

```csharp
public enum UiSurfaceKind : byte
{
    RetainedUi = 0,
    ScreenOverlay = 1,
    WorldHud = 2,
    AdapterOverlay = 3,
}

public readonly record struct UiSurfaceLeaseHandle(int Slot, uint Generation)
{
    public bool IsValid => Slot >= 0 && Generation != 0;
}
```

设计原则：

*   同一时刻，一个 surface segment 必须有明确 owner。
*   owner 是稳定身份，例如 mod / system / showcase id，而不是某个瞬时 controller 实例名。
*   ownership 是显示面契约，不是输入层契约；输入仍通过各自正式服务处理。

## 5 Lease 服务提议

建议新增统一服务，例如：

```csharp
public readonly record struct UiSurfaceLeaseRequest(
    UiSurfaceKind Surface,
    string SegmentId,
    string OwnerId,
    bool Exclusive);

public interface IUiSurfaceLeaseService
{
    UiSurfaceLeaseHandle Acquire(in UiSurfaceLeaseRequest request);
    bool TryRevalidate(UiSurfaceLeaseHandle handle);
    bool Release(UiSurfaceLeaseHandle handle);
    bool TryGetOwner(UiSurfaceKind surface, string segmentId, out string ownerId);
}
```

语义要求：

*   `Surface + SegmentId` 共同定位一个可被接管的显示面。
*   `OwnerId` 必须稳定，便于日志、调试与 restore。
*   `Exclusive = true` 表示独占接管；共享观察型 surface 可在后续扩展，但第一版先保证独占语义可验证。
*   handle 采用 slot + generation，避免旧 lease 误释放新 owner。

## 6 生命周期规则

对于 map / showcase 生命周期，建议固定以下规则：

1. `MapLoaded`
   需要接管 surface 的 mod 在此阶段 acquire lease，并记录被接管前的 owner 或状态。
2. `MapResumed`
   必须重新校验 lease 仍然有效；如果 surface 已被其他流程重建，当前 owner 要么 restore，要么显式重新 acquire。
3. `MapUnloaded`
   当前 owner 必须 release lease，并执行 restore；不得把 suppression、隐藏状态或空 scene 残留到下一张图。

补充规则：

*   “能显示出来”不等于生命周期正确，必须验证 resume / unload 后的 restore。
*   world-click、entity selection、camera 等交互边界必须与 surface lease 一起验收，避免出现显示面恢复了但交互仍被锁死。

## 7 与现有方案的关系

本 RFC 不否定当前可工作的临时修复，但要求明确它们的地位：

*   `InteractionShowcaseIds.SuppressUiPanelKey` 之类 suppression flag 只能作为短期 mitigation。
*   当两个以上 feature 反复争用同一 surface 时，必须上升为正式 lease 基建，而不是继续叠加“隐藏这个、再恢复那个”的条件分支。
*   `docs/architecture/entity_command_panel_infrastructure.md` 与 `docs/rfcs/RFC-0053-entity-info-panels-for-ui-and-overlay.md` 中提到的多实例 host、UI/overlay 双前端，都应受同一套 ownership 规则约束。

## 8 验收要求

若未来实现本 RFC，最低验收应包括：

*   engine-side：lease acquire / revalidate / release 的测试或日志证据。
*   adapter-visible：玩家可见截图、录屏或可复现实机观察，证明首帧可读且无闪烁残留。
*   interaction safety：面板显示与关闭前后，实体选择、世界点击、相机行为维持设计预期。
*   takeover / restore：`MapLoaded`、`MapResumed`、`MapUnloaded` 三个阶段均有可验证证据。

## 9 相关文档

*   Feature 开发工作流：见 [../conventions/01_feature_development_workflow.md](../conventions/01_feature_development_workflow.md)
*   文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
*   统一 UI Runtime 与三前端写法：见 [../architecture/ui_runtime_architecture.md](../architecture/ui_runtime_architecture.md)
*   Entity Command Panel 基础设施：见 [../architecture/entity_command_panel_infrastructure.md](../architecture/entity_command_panel_infrastructure.md)
*   RFC-0053 正式游戏可复用实体信息面板：见 [RFC-0053-entity-info-panels-for-ui-and-overlay.md](RFC-0053-entity-info-panels-for-ui-and-overlay.md)
*   RFC-0054 通用实体指令面板基础设施：见 [RFC-0054-entity-command-panel-infra.md](RFC-0054-entity-command-panel-infra.md)
