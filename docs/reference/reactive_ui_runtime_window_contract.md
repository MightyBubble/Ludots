# Reactive UI Runtime Window Contract

本文定义 Ludots 当前 `ReactivePage` runtime window virtualization 的正式调用契约，覆盖 `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`、`src/Libraries/Ludots.UI/Reactive/ReactiveContext.cs`、`src/Libraries/Ludots.UI/Runtime/UiScene.cs` 和 `src/Libraries/Ludots.UI/UIRoot.cs`。本文只描述已经实现的 `Ludots.UI` 行为，不定义未来 closure 方案。

## 1 入口与边界

当前 runtime window 相关入口如下：

- virtual window 声明：`src/Libraries/Ludots.UI/Reactive/ReactiveContext.cs`
- runtime refresh：`src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
- retained diff / metrics / window 缓存：`src/Libraries/Ludots.UI/Runtime/UiScene.cs`
- 宿主生命周期：`src/Libraries/Ludots.UI/UIRoot.cs`
- 当前已验证 fixture：`mods/fixtures/camera/CameraAcceptanceMod/UI/CameraAcceptancePanelController.cs`

本文不覆盖 adapter 侧 dirty upload、persistent static lane 同步或 Web / Raylib host 的上传语义；这些不属于 `Ludots.UI` 的 runtime window contract。

## 2 当前所有权模型

| 关注点 | 当前 owner | 已实现行为 | 证据路径 |
|------|------|------|------|
| 状态驱动重组 | `ReactivePage<TState>` | `SetState(...)` / `Mutate(...)` 立即触发 `Recompose(...)` | `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs` |
| virtual window 声明 | reactive render code | `GetVerticalVirtualWindow(...)` 在 render 期间记录 host id、item extent、viewport extent、overscan 与当前 window | `src/Libraries/Ludots.UI/Reactive/ReactiveContext.cs`、`src/Libraries/Ludots.UI/Reactive/ReactivePage.cs` |
| runtime window 刷新 | mounted page caller | 只有 caller 调用 `RefreshRuntimeDependencies()` 时，才会重新计算 scroll / viewport 驱动的 window 并触发 `UiReactiveUpdateReason.RuntimeWindowChange` | `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs` |
| generic lifecycle | `UIRoot` / `UiShowcaseMounting` | `UIRoot.Update(...)` 只推进 time，`UIRoot.HandleInput(...)` 只分发输入并设置 dirty，`MountReactivePage(...)` 只挂 scene，不驱动 runtime refresh | `src/Libraries/Ludots.UI/UIRoot.cs`、`mods/UiShowcaseCoreMod/Showcase/UiShowcaseMounting.cs` |
| 观测缓存 | `UiScene` | 每次 reactive recompose 后更新 `LastReactiveUpdateMetrics` 与 `TryGetVirtualWindow(...)` 可见窗口快照 | `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |

结论只有一句：当前 runtime window refresh 是 caller-owned contract，不是 `UIRoot` 内建生命周期。

## 3 使用 virtualization 的调用方责任

当 reactive page 同时满足以下条件时，挂载方必须显式驱动 runtime refresh：

- render 代码调用 `GetVerticalVirtualWindow(...)`
- host 节点拥有稳定 `id`
- visible slice 依赖滚动偏移、宿主测量高度或其他 runtime-only 布局数据

当前调用方责任如下：

1. 在 mounted page 自己的 mount / sync / update 循环中调用 `RefreshRuntimeDependencies()`。
2. 当返回值为 `true` 时，把宿主 `UIRoot.IsDirty` 标成 `true`，让场景重新布局并渲染。
3. 不要假设 `UIRoot.Update(...)`、`UIRoot.HandleInput(...)`、`UiShowcaseMounting.MountReactivePage(...)` 会替你完成这一步。
4. virtualized scroll host 的 `id` 必须稳定；否则 `Scene.FindByElementId(...)` 无法把新 window 绑定回正确宿主。

当前可复用的 owner 模式见 `mods/fixtures/camera/CameraAcceptanceMod/UI/CameraAcceptancePanelController.cs`：`MountOrSync(...)` 在挂 scene、同步 state snapshot 之后调用 `_page.RefreshRuntimeDependencies()`，若 window 变化则标记 `root.IsDirty = true`。

## 4 可观测性契约

不允许把“没有整页重组”写成无法验证的结论。当前正式可观测信号如下：

| 观测目标 | API / 指标 | 含义 |
|------|------|------|
| runtime window 是否触发刷新 | `UiReactiveUpdateMetrics.Reason == UiReactiveUpdateReason.RuntimeWindowChange` | 当前 patch 的直接触发原因是 window 改变，而不是 state change |
| 是否保持 retained diff | `UiReactiveUpdateMetrics.FullRemount == false` | 当前 patch 没有 remount 整棵 scene |
| patch / reuse 规模 | `UiReactiveUpdateMetrics.PatchedNodes`、`UiReactiveUpdateMetrics.ReusedNodes` | 可以验证只 patch 变化节点，而不是整页重建 |
| 当前可见窗口 | `UiScene.TryGetVirtualWindow(...)` | 可以直接读取 host 的 `StartIndex`、`EndIndexExclusive`、`VisibleCount` |
| virtualization budget | `UiReactiveUpdateMetrics.VirtualizedWindowCount`、`VirtualizedTotalItems`、`VirtualizedComposedItems` | 可以验证只 compose visible slice |
| 页面级累计计数 | `ReactivePage.FullRecomposeCount`、`ReactivePage.IncrementalPatchCount` | 可以在 fixture HUD 或测试里跟踪 remount / incremental patch 次数 |

## 5 已验证路径

当前仓库里，runtime window contract 的正式验证路径是 camera acceptance panel：

- 场景实现：`mods/fixtures/camera/CameraAcceptanceMod/UI/CameraAcceptancePanelController.cs`
- 诊断状态：`mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceDiagnosticsState.cs`
- 自动化测试：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`

现有测试已经覆盖以下事实：

- retained diff 仍保留既有节点复用与 incremental patch：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`
- 大 selection list 只 compose visible slice：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
- scroll 后 update reason 会切到 `RuntimeWindowChange`，并继续走 incremental patch 而不是 full remount：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`

## 6 非目标

当前 contract 不包含以下语义：

- `UIRoot` 自动拥有所有 mounted reactive page 的 runtime refresh 生命周期
- adapter 侧 diff/upload 节流或 dirty 同步
- 没有 runtime owner 的场景里自动推断 virtual window 刷新时机

## 7 相关文档

- UI 架构总览：见 [../architecture/ui_runtime_architecture.md](../architecture/ui_runtime_architecture.md)
- Reactive UI closure 提案与 playable mod 设计：见 [../rfcs/RFC-0002-reactive-ui-runtime-window-closure-and-playable-mods.md](../rfcs/RFC-0002-reactive-ui-runtime-window-closure-and-playable-mods.md)
