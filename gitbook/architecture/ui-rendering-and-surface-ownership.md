# UI 渲染控制与 Surface 所有权（SSOT）

本页是 Ludots UI 渲染控制的正式总览，用“说人话”的方式讲清三件事：

1. 一帧 UI 是怎么从“数据变化”走到“屏幕上像素更新”的（**更新流程**）。
2. 谁有权改动屏幕上的 UI，谁只能旁观（**Surface 所有权**）。
3. Skia 与 WebUI 是怎么混排的，高性能大批量数据走哪条路（**渲染分层**）。

新接手的人（包括 AI agent）只要先读完本页，就能不踩坑地往里加 UI。深度实现细节见文末「相关文档」。

---

## 0 一句话心智模型

> **UI 不是“谁想画就画”，而是“向唯一的 `UiSurfaceHost` 租一块位置，提交一个构建器，由 Host 合成成唯一一棵 `UiScene`，交给唯一的 `UIRoot` 渲染/输入”。**

- 想显示 UI 的人（mod / 能力控制器 / showcase / Markup / 浏览器面板）都是**贡献者（contributor）**。
- `UiSurfaceHost` 是**唯一**能写 `UIRoot.Scene` 的生产组件。
- `UIRoot` 是**渲染 + 输入的设备端口**，不再是一个谁都能塞场景的公共槽。
- 适配器（Raylib / Web / 商业引擎）只是**消费**那一棵合成好的场景，不做任何场景堆叠，也没有 fallback。

记住这张图就够用了：

```text
贡献者（mod / 控制器 / showcase / Markup / 浏览器面板）
        │  Acquire 租约 → Publish(UiSurfaceContribution)
        ▼
UiSurfaceHost  ← 唯一写 UIRoot.Scene 的人；按 segment / 优先级 / 独占 合成成 1 棵 UiScene
        │  内部 MountSceneFromHost / ClearSceneFromHost + 置 UIRoot.IsDirty
        ▼
UIRoot         ← 渲染 + 输入端口；持有当前 Scene、IsDirty、输入帧快照
        │
        ├─ 渲染端口 IUiRenderer ── SkiaUiRenderer 画 UiScene
        └─ 输入：HandleInput(PointerEvent/KeyboardEvent) → 派发到节点 / Canvas sink
        ▼
适配器（驱动适配器）
   Raylib: RaylibOverlayCompositor 按 IsDirty 决定是否重绘 Skia UI 层
   Web:    WebUiRuntimeBridge.TryConsumeScene 按 IsDirty 决定是否序列化推给浏览器
```

---

## 1 三层（六边形）：谁是 domain，谁是 adapter

| 层 | 是谁 | 职责 | 红线 |
|----|------|------|------|
| **Domain（UI 模型层）** | `src/Libraries/Ludots.UI/`（`UiSurfaceHost`、`UIRoot`、`UiScene`、`ReactivePage`、`Compose`） | UI 的唯一真相：合成策略、所有权、布局、输入语义。零平台依赖。 | **绝不**引用 SkiaSharp / Raylib / CEF 等任何后端。 |
| **Ports（策略接口）** | `IUiRenderer`、`IUiTextMeasurer`、`IUiImageSizeProvider`、`IUiCanvasContent`、`IUiCanvasInputSink` 等 | 把“渲染 / 测量 / 自定义画布”抽象成接口，由 domain 注入使用。 | domain 只依赖接口，不依赖实现。 |
| **Adapters（驱动/被驱动适配器）** | `src/Libraries/Ludots.UI.Skia/`（渲染）、`src/Adapters/Raylib/`、`src/Adapters/Web/`、`Ludots.UI.Browser*`、商业引擎 adapter | 把平台输入翻译成 `PointerEvent` 喂给 `UIRoot`；把 `UIRoot.Scene` 渲染/序列化出去；托管浏览器内核与原生窗口生命周期。 | **不做场景堆叠、不做 fallback、不持有 gameplay 真相。** |

依赖方向（只能从下往上指）：

```text
Ludots.UI            → 零平台依赖（仅 FlexLayoutSharp）
Ludots.UI.Skia       → Ludots.UI + SkiaSharp
Ludots.UI.Browser    → Ludots.UI（浏览器表面契约，无 native 内核）
Adapter (Raylib/Web) → Ludots.UI + Ludots.UI.Skia + Core
```

> 为什么这样分：合成“屏幕上该显示什么”是**业务策略**，必须留在 domain；具体用 Skia 画、还是序列化给浏览器画、还是 UE5 BLUI 画，是**适配器细节**。换适配器不该改动一行合成逻辑。

---

## 2 Surface 所有权：黄金规则

历史问题：以前 `UIRoot.Scene` 是个公共可变槽，N 个控制器按表现系统注册顺序各写各的 → **后写者赢**，谁也说不清屏幕上到底该是谁的 UI。Epic #398 把这事收口了。

**黄金规则（务必背下来）：**

1. **唯一写者**：生产代码里只有 `UiSurfaceHost` 能挂/清 `UIRoot.Scene`。`UIRoot.MountSceneFromHost / ClearSceneFromHost` 是 `internal`，编译期就挡住外部调用。
2. **贡献者用租约**：任何要显示 retained UI 的组件，先 `Acquire` 一张租约，再 `Publish` 一个**构建器**（不是已挂好的场景）。
3. **失败即炸**：句柄过期 / 未注册 / 越权，直接抛异常或返回 false，**没有 fallback、不静默降级**。
4. **脏标记也归 Host**：贡献者改了内容只调 `Invalidate(handle)`，**不要**自己写 `UIRoot.IsDirty`。
5. **CI 护栏**：`mods/**`、`src/Adapters`、`Ludots.UI.HtmlEngine` 里出现 `.MountScene(` / `.ClearScene(` 会被架构测试判失败。

### 2.1 Host 接口与租约模型

源码：`src/Libraries/Ludots.UI/Surface/`

```csharp
public interface IUiSurfaceHost
{
    UiScene? Scene { get; }
    UiSurfaceLeaseHandle Acquire(UiSurfaceLeaseRequest request);
    bool Revalidate(UiSurfaceLeaseHandle handle);
    void Publish(UiSurfaceLeaseHandle handle, UiSurfaceContribution contribution);
    void Invalidate(UiSurfaceLeaseHandle handle);
    bool Release(UiSurfaceLeaseHandle handle);
}
```

- `UiSurfaceLeaseRequest(OwnerId, Segment, Priority, Exclusive)`：
  - `OwnerId` 必须**稳定**（用 mod / 系统 / showcase 的稳定 id，**不要**用控制器实例名）。
  - `Segment`：`Background(0) / Main(100) / Overlay(200) / Modal(300) / Debug(400)`，决定 z 序大区间。
  - `Priority`：同区间内的细排序。
  - `Exclusive=true`：独占接管——优先级最高的独占租约会**隐藏所有非独占贡献**（用于 showcase 全屏接管、浏览器 demo 等）。
- `UiSurfaceContribution`：携带一个 `UiElementBuilder` 工厂（`FromBuilder`）或一个 `ReactivePage`（`FromReactivePage`），外加 theme / stylesheets / reactive 钩子。**Host 负责把它们合成成最终场景**，贡献者永远不碰 `UiScene` 本体。

### 2.2 Host 内部做了什么（你不用改，但要知道）

`UiSurfaceHost`（`Surface/UiSurfaceHost.cs`）持有**一棵** `UiScene`：

- `GetVisibleEntries()`：先看有没有独占租约（有则只显示优先级最高那张）；否则按 `Segment → Priority → LeaseId` 排序全部显示。
- `BuildHostRoot()`：把每个贡献者包进一个全屏 `<section>`，z 序 = `Segment + Priority + i`，并且 **Host 外壳和每个 section 都设 `PointerEvents.None`**——这样命中测试能穿透到真正可交互的叶子节点和浏览器画布，外壳不会“吞”点击。
- `RebuildNow()`：结构变更（Publish/Release）走全量重建 + 增量 patch（`ApplyReactiveRoot`）；运行时变更（`Invalidate` → reactive 刷新）走增量。**带 `_isRebuilding` 重入护栏**，防止重建过程中被回调再次触发而错乱。
- 重建后调用 `UIRoot.MountSceneFromHost / ClearSceneFromHost` 并置 `UIRoot.IsDirty = true`。

---

## 3 一帧 UI 的更新流程（数据 → 像素）

下面是一次“状态变化导致 UI 更新”的完整链路：

1. **贡献者改状态**：例如能力控制器在表现系统 tick 里检测到选中变化，调用 `host.Invalidate(handle)`（或重新 `Publish` 一个新构建器）。
2. **Host 标脏**：`UiSurfaceHost` 置 `pendingRebuild`，并把 `UIRoot.IsDirty = true`。
3. **Host 合成**：在重建时机把所有可见贡献合成进那棵唯一 `UiScene`，做布局（`UiScene.Layout`）与增量 patch。
4. **适配器按 `IsDirty` 出帧**：
   - **Raylib（桌面/原生）**：`RaylibOverlayCompositor.Render` 看 `!_uiHadContent || uiRoot.IsDirty` 决定是否重画 Skia UI 层；要画就 `uiRoot.Render()`（用 `SkiaUiRenderer` 把 `Scene` 画进 UI 层），画完 `IsDirty=false`。
   - **Web（远程串流原生 UI）**：`WebUiRuntimeBridge.TryConsumeScene` 看 `IsDirty` 决定是否把 `Scene` 序列化成 JSON 推给瘦客户端；推完 `IsDirty=false`（consume-once）。
5. **下一帧**：没有人标脏 → `IsDirty=false` → 不重画 / 不重新序列化，省成本。

> 关键：`UIRoot.IsDirty` 是**唯一**的“要不要重新出帧”的门，且**只由 Host 置位**。这保证 Skia 重绘门和 Web 序列化门共享同一个真相源。所以你绝对不能在 mod 里自己写 `root.IsDirty = true` 去“强制刷新”。

---

## 4 输入流程与输入帧安全

`UIRoot.HandleInput`（`src/Libraries/Ludots.UI/UIRoot.cs`）是所有平台输入的统一入口：

1. 适配器把平台输入（鼠标/键盘/触摸）翻译成 `PointerEvent` / `KeyboardEvent` 喂进来。
2. `HandleInput` **先把当前 `Scene` 快照到局部变量**，整帧用这个快照做命中测试与派发。
3. 派发到命中的节点；如果命中的是一个 `Ui.Canvas(...)` 且其内容实现了 `IUiCanvasInputSink` / `IUiCanvasKeyboardInputSink` / `IUiCanvasFocusSink`（例如浏览器表面），就把输入交给该 sink，并支持 alpha 命中穿透。
4. 派发可能触发回调（按钮点击等），回调**可能在派发过程中**让 Host 重挂/清场景。`HandleInput` 在尾部用 `ReferenceEquals(Scene, snapshot)` 对账：若场景已被换/清，则只置 `IsDirty=true` 并安全收尾，**绝不**去读已失效的旧场景（这就是 issue #394 修复的崩溃）。

> 也就是说，“点击 → 切地图 / 关面板 / 换 showcase”这类在输入帧内改变 UI 的操作是**一等公民、保证安全**的。新写按钮回调时不用担心“点完把自己清掉会不会崩”。

---

## 5 Skia 与 WebUI 混排：什么走哪条路

Ludots 同屏混排两类 UI，**职责不重叠**：

| 用途 | 走哪条路 | 在哪 |
|------|---------|------|
| **高性能大批量原生数据**（如 30k 单位的 HUD 血条 / 文字、minimap marker） | Core Presentation 批渲染（SoA 批量，零每实体分配），**不进 UiScene** | `src/Core/Presentation/Hud/`（`ScreenHudBatchBuffer`、`PresentationOverlayScene`、`WorldHudToScreenSystem`），由 Skia 直接批量绘制 |
| **retained 交互 UI**（面板、按钮、表单、表格、节点图编辑器…） | `UiSurfaceHost` → `UiScene` → `SkiaUiRenderer` | `Ludots.UI` + `Ludots.UI.Skia` |
| **真·Web 应用**（React / React Flow 等） | 作为一个 `Ui.Canvas(BrowserSurfaceCanvasContent)` 节点**嵌进 Host 的 UiScene**；数据用 **WebUI DataPlane** 喂 | `Ludots.UI.Browser*` + `Ludots.WebUI.DataPlane` |

要点：

- **30k HUD 那条不是 UiScene**。它是“高性能大批量”专用通道，不要试图用面板系统去画上万条血条。
- **浏览器面板是 Host 的一个租约**（通常 `Segment.Main` + `Exclusive`），和原生面板在同一棵场景里合成；输入/焦点/命中穿透全部经 `UIRoot` 路由。
- **WebUI DataPlane 只管“喂数据/命令/事件给 Web 应用”**（传输中立，CEF 与 UE5 BLUI 同一套 `window.ludotsDataplane` facade），它**不**序列化 UiScene、不拥有任何 gameplay 真相。详见 `docs/architecture/webui_dataplane_architecture.md`。
- **UE5 若调用 Ludots-owned CEF bootstrap**，必须按 launcher/bootstrap 中的 `browserRuntime.providerAssemblyPath` 把 provider 包当作宿主依赖根，优先复用 `Ludots.UI.Browser.BrowserRuntimeProviderLoader` 进行 hash shadow-copy、provider ALC 加载与 provider `.deps.json` 解析；默认 managed provider 使用 collectible ALC，CEF 因 CefSharp mixed/native runtime assemblies 使用 non-collectible provider ALC，并将 `CefSharp` 声明为 process-shared assembly prefix。不要硬编码 CefSharp DLL 名，也不要从 Mod 加载链路寻找或兜底 CEF。

---

## 6 我要加 UI，怎么做（速查）

**通用步骤（所有写法都一样）：**

1. 从 `ScriptContext` / engine 取 `CoreServiceKeys.UiSurfaceHost`（拿到 `IUiSurfaceHost`）。
2. 持有一个 `UiSurfaceLeaseHandle _lease` 字段。
3. 显示：`host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("YourMod.Panel", segment, priority), page)`（`UiSurfaceHostExtensions` 提供 `EnsureLease` / `PublishReactivePage` / `ReleaseLease` 便捷封装）。
4. 内容变化：`host.InvalidateLease(_lease)`。
5. 退出/卸载：`host.ReleaseLease(ref _lease)`。

**按写法选构建器：**

- **Compose / Reactive**：构造 `UiElementBuilder` 或 `ReactivePage`，用 `UiSurfaceContribution.FromBuilder(...)` / `FromReactivePage(page)`。
- **Markup（HTML/CSS authoring）**：走 `IUiSystem`（`MarkupUiSystem`），它已经改成通过 Host 发布文档 surface，你只管 `SetHtml`。
- **Browser（真 Web 应用）**：`Acquire` 一张 `Exclusive` 租约，`Publish` 一个含 `Ui.Canvas(BrowserSurfaceCanvasContent)` 的构建器；数据走 DataPlane topic。参考 `mods/showcases/browser_react_flow/`。

**绝对不要做：**

- ❌ `root.MountScene(...)` / `root.ClearScene(...)`（编译不过，CI 也会拦）。
- ❌ `root.IsDirty = true` 强制刷新（脏标记归 Host）。
- ❌ 自己 new 第二个 `UIRoot` 或第二套 UI runtime。
- ❌ 在适配器里堆叠多张场景，或给“找不到 Host”加 fallback。
- ❌ 把上万条 HUD 当成面板节点画（用 Core Presentation 批渲染那条）。

---

## 7 正式验收入口与护栏

- Host 单测：`src/Tests/UiShowcaseTests/UiSurfaceHostTests.cs`（按 segment/z-order 合成、过期句柄不能发布、独占接管与恢复）。
- 所有权护栏：`src/Tests/ArchitectureTests/UiSurfaceOwnershipGuardTests.cs`（生产代码禁止直写 `MountScene/ClearScene`；`UIRoot` 不得暴露 public 写场景方法）。
- 输入帧安全回归：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs` 的 `UIRoot_ClickThatClearsScene_CompletesInputFrame`。
- 统一 UI 三写法验收：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`。

---

## 8 相关文档（深度材料）

正式口径以本页为准；下列文档提供更深的实现细节与决策记录，**不重新定义**本页规则：

- 统一 UI Runtime 与三前端写法：`docs/architecture/ui_runtime_architecture.md`
- WebUI DataPlane 边界：`docs/architecture/webui_dataplane_architecture.md`
- WebUI Panel Kit Manifest（WPK-1）：`docs/architecture/webui_panel_kit_manifest.md`
- 浏览器 UI Runtime：`docs/architecture/browser_ui_runtime.md`
- 适配器模式与平台抽象：`docs/architecture/adapter_pattern.md`
- 决策记录：`docs/adr/ADR-0002-unified-ui-runtime-and-authoring-models.md`、`docs/adr/ADR-0003-browser-ui-runtime-contract.md`
- 关键源码：`src/Libraries/Ludots.UI/Surface/`、`src/Libraries/Ludots.UI/UIRoot.cs`、`src/Libraries/Ludots.UI.Skia/`、`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibOverlayCompositor.cs`、`src/Adapters/Web/Ludots.Adapter.Web/Services/WebUiRuntimeBridge.cs`
