# EPIC：多势力生产体系 Showcase 套件（红警 / 星际 / 帝国 / 4X）—— 基于 Browser UI Runtime + WebUI DataPlane

> 状态：Draft v2（**纠正版**，供 Codex 执行）
> **基线 commit：`d289bd3d1`（本地 `main`，"Merge branch 'codex/browser-ui-runtime'"）**
> 工作区：`C:/001_AI/LudotsProd_showcase_epic`
> **UI 基建：`codex/browser-ui-runtime` 那套 —— 嵌入式 CEF 浏览器表面（`Ludots.UI.Browser*`）+ `Ludots.WebUI` 的 WebUI DataPlane，mod 用真实 web 应用做 UI。**

---

## ⚠️ 0. 重大纠正说明（必读，相对 v1）

v1 epic 把"web UI 基建"**理解错了**，导致 codex 朝错误方向开发。本节固化正确事实：

1. **基线错了**：v1 基于 `origin/main`（`289387b4c`）。但你的真正 `main` 是 **`d289bd3d1`**，二者在 `f58bd23df` 之后**已分叉**——`289387b4c` 走了 save-followups 等线，**根本不含 browser-ui-runtime**；`d289bd3d1` 才是合入 `codex/browser-ui-runtime` 的合并点，且同时含 存档/AI/entity association/progression。**本 epic 一切以 `d289bd3d1` 为基线。**
2. **UI 基建错了**：v1 分析的是 `src/Adapters/Web` + `src/Client/Web`（服务端权威 Three.js 流式渲染）。**那不是目标。** 真正的 UI 基建是 **Browser UI Runtime**：
   - `src/Libraries/Ludots.UI.Browser`（引擎中立的浏览器表面契约）
   - `src/Libraries/Ludots.UI.Browser.Cef`（CEF 提供者）、`Ludots.UI.Browser.Skia`（Skia 帧适配）
   - `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibBrowserLayerRenderer.cs`（CEF OnPaint dirty-rect → Raylib `Texture2D` 直传）
   - `src/Libraries/Ludots.WebUI`（**WebUI DataPlane** facade，`Ludots.WebUI.DataPlane` / `Ludots.WebUI.Browser` 命名空间）
   - 参考 mod：`mods/browser/BrowserCefRuntimeMod/`、`mods/showcases/browser_ui/BrowserUiShowcaseMod/`（原生 HTML/JS）、**`mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/`（React + React Flow + DataPlane，黄金范例）**
   - 文档：`docs/architecture/browser_ui_runtime.md`、`docs/architecture/webui_dataplane_architecture.md`、`docs/adr/ADR-0003-browser-ui-runtime-contract.md`
3. **codex 已产生错误方向 WIP**：当前 worktree 里有未提交/未跟踪改动，是 codex 按 v1 错误 epic 写的，**触碰了错误的 `src/Client/Web`**（甚至删了 `FrameProtocol.ts`），并按错误结构建了 `mods/capabilities/rts_hud_web`、`Ludots.UI/Surfaces` 等。**这些 UI 相关产物方向错误，需作废或重定向**（见 §7 处置）。

> 一句话：**showcase 的 UI = 每个 mod 自带一个真实 web 应用（推荐 React，可精准还原参考游戏 HUD），跑在 Ludots 启动的 CEF 浏览器表面里，通过 WebUI DataPlane（topic/command）与游戏运行时通信；宿主用 Raylib + CEF runtime preset 运行。绝不碰 `src/Adapters/Web` / `src/Client/Web`。**

---

## 1. 目标与四个 Mod

### 1.1 目标

基于 `d289bd3d1` 已合入的 **存档 / Utility AI / Entity Association（AAC）/ Progression / Exchange / Browser UI Runtime + WebUI DataPlane**，交付四个参考流派的生产/训练 showcase。每个 Mod 的 UI 全部是真实 web 应用（CEF 表面 + DataPlane），且能切换不同势力 view，演示该流派完整生产体系 + 外交 + 贸易。

### 1.2 四个 Mod（参考流派 → 生产范式）

| Mod | 参考流派 | 势力（可切换 view） | 主打生产范式 | 必须演示 |
|-----|----------|---------------------|---------------|----------|
| **M1 RedAlertLike** | C&C / Red Alert | Allied / Soviet | **直接建造** + MCV **部署** | 直接/部署建造、训练、科技、外交、贸易 |
| **M2 StarCraftLike** | StarCraft II | Terran / Protoss / Zerg | **工人建造**(SCV) + **Warp/部署**(Protoss) + **产卵孵化**(Zerg) | 三族三范式、训练、WarpGate 科技、外交、贸易 |
| **M3 EmpireLike** | Age of Empires | 两个文明 | **村民(工人)建造** + 建筑训练 + **时代/科技树** | 工人建造、训练、多层科技树、外交、贸易 |
| **M4 FourXLike** | Civ / Stellaris | 3 个帝国 | **城市排产** + 深科技树 | 城市排产、科技树、**重外交协议 + 贸易互换** |

合起来覆盖全部范式：直接 / 部署 / 工人 / 产卵 / 城市排产 + 训练 + 科技树 + 外交 + 贸易。UI 风格对齐各自参考项目（在 web 应用内自由实现）。

---

## 2. 真正的 UI 基建：Browser UI Runtime + WebUI DataPlane（事实契约）

> 全部以 `BrowserReactFlowShowcaseMod` 为黄金范例。开发前精读：`mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/`（`BrowserReactFlowShowcaseModEntry.cs`、`BrowserReactFlowShowcaseDataPlane.cs`、`WebApp/src/dataplane/client.js`、`WebApp/src/dataplane/DataPlanePanel.jsx`、`WebApp/src/main.jsx`）+ `docs/architecture/browser_ui_runtime.md` + `docs/architecture/webui_dataplane_architecture.md`。

### 2.1 浏览器表面层（`Ludots.UI.Browser` / `.Cef` / `.Skia`）

- `IBrowserRuntime`（CEF 提供者，service key `"BrowserRuntime"`，由 `BrowserCefRuntimeMod` 注册）：`CreateSurfaceAsync(BrowserViewport, IBrowserResourceResolver) -> IBrowserSurface`。
- `BrowserAppResourceResolver(assetRoot)`：从 VFS 解析的本地 web app 包目录加载 `index.html`/JS/CSS/WASM。
- `IBrowserSurface`：`NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-app://app/")))`、`Messages`（`IBrowserMessageBridge`）、帧/输入/生命周期。
- `BrowserSurfaceCanvasContent(surface, hitTestOptions: BrowserSurfaceHitTestOptions.Alpha())`：把浏览器表面包成 `Ui.Canvas(...)` payload，挂进 `UiScene`/`UIRoot`；**alpha 命中测试**让透明 web 像素穿透到原生层（指针/键盘/焦点仍走 `UIRoot`/`UiScene`）。
- Raylib 宿主：`RaylibBrowserLayerRenderer`（CEF OnPaint dirty-rect → `Texture2D`，绕过 Skia framebuffer 走低开销路径）。

### 2.2 WebUI DataPlane（`Ludots.WebUI.DataPlane` / `Ludots.WebUI.Browser`）

宿主侧（C#）：
- `IWebUiTopicProducer`：`string Topic`；`bool TryCreateSnapshot(in WebUiTopicContext, out WebUiOutboundPacket)`；`WebUiOutboundPacket CreateDeltaPacket(sessionId)`；可选 `CreateBinarySnapshotPacket`（用 `WebUiEntityColumnarPacket.EncodeSnapshot` + `WebUiEntityColumnarRow` SoA，二进制 magic `LWDP`，schema id）。
- `WebUiOutboundPacket(sessionId, topic, WebUiPacketKind{Snapshot|Delta}, WebUiDeliverySemantics{LatestWins|ReliableOrdered}, payloadBytes, contentType, requestId?)`。
- `WebUiCommandRouter(IWebUiEntityGenerationResolver, IWebUiCommandPermissionValidator)` + `Register(name, IWebUiCommandHandler)`；`IWebUiCommandHandler.HandleAsync(WebUiCommandRequest) -> WebUiCommandResult`；`WebUiCommandRequest{Name, Payload(JsonElement), EntityRefs, ClientSeq}`。
- `WebUiDataPlaneRuntime(router)`：`RegisterTopic(producer)`；`AttachSession(id, IWebUiDataTransport) -> WebUiDataPlaneSession`；`PublishAsync(packet, ct)`。
- 传输：`BrowserMessageBridgeDataTransport(surface.Messages)`（DataPlane 走浏览器消息桥）。
- **生产 topic 必须复用 Core 既有 store，不得另起炉灶**（`webui_dataplane_architecture.md` §2）：
  - 实体列表/检视/命令源 → **`EntityCollectionStore`** 投影（owner + collectionKey + window(start,count) + `EntityCollectionView.Revision` 作为缓存失效令牌；保留 `EntityCollectionSourceKind`/`RoleKind`）。
  - 高频 marker/entity topic → 仿 **`MinimapMarkerBuffer`/`MinimapScreenMarkerBuffer`**（SoA、显式容量、bucket key、稳定 id、drop 诊断）。
  - 未知 topic/缺失服务 → **加载/消费边界 fail-fast**，禁止静默回退到 selection/缓存/浏览器侧状态。

Web 侧（JS，复用范例 `client.js`）：
- `createLudotsDataPlaneClient({transport})`：`handshake()` / `subscribe(topic, handler)` / `command(name, payload, {entityRefs})` / `unsubscribe()`。
- 传输优先 `window.ludotsDataplane` → `CefSharp.PostMessage` 桥；浏览器开发态有 `createFakeLudotsDataPlaneTransport`（fake）便于不开游戏调 UI。
- 二进制帧 `decodeEntityColumnarPacket(bytes)`（magic `0x5044574c`）。snapshot/delta/binaryChunk 三类入站。

### 2.3 Mod 接线模式（照搬范例）

`OnLoad` 注册 `GameEvents.GameStart` → 在 handler 中：
1. 取 `IBrowserRuntime`（key `"BrowserRuntime"`，缺失则挂"missing runtime"提示场景并提示用 CEF preset）。
2. `BrowserAppResourceResolver(ResolveAssetRoot)`（经 `engine.VFS.TryResolveFullPath("<Mod>:Assets/<app>/index.html")`）。
3. `runtime.CreateSurfaceAsync(viewport, resolver)`。
4. `SetupDataPlane`：建 topic producer(s)、`WebUiCommandRouter`(+权限校验+代际解析)、`Register` 各命令、`BrowserMessageBridgeDataTransport(surface.Messages)`、`WebUiDataPlaneRuntime.RegisterTopic` + `AttachSession`、起发布循环 `PublishAsync(CreateDeltaPacket/CreateBinarySnapshotPacket)`。
5. `root.MountScene(Ui.Panel(原生穿透层, Ui.Canvas(browserContent).WidthPercent(100).HeightPercent(100)))`；`surface.NavigateAsync(ludots-app://app/)`。
6. `OnUnload` 停发布、Dispose runtime/surface/content。

### 2.4 关键纠正：RFC-0055 单 `UIRoot` 问题在此架构下基本消解

浏览器表面只是**一个** `Ui.Canvas`，所有生产/训练/科技/外交/贸易/存档面板都在**web 应用内部**（React DOM）自行组合布局。因此 v1 的"多面板组合/surface 租约"阻塞项**不再是核心阻塞**；`src/Libraries/Ludots.UI/Surfaces`（codex WIP）方向不需要。

---

## 3. 其余基建（在 `d289bd3d1` 已存在，沿用 v1 分析，路径已复核）

> 这些 core 系统在 `d289bd3d1` 同样存在（已核对 `src/Core/Gameplay/Exchange`、`src/Core/Persistence`、`src/Core/Gameplay/Progression`、`src/Core/Association`）。它们的语义与 v1 分析一致，唯一不同是 UI 出口改为 DataPlane topic/command。

- **Entity Association（AAC）**：`OwnershipResolver`（`Owns` 边，被生产单位归属真相）、`ChildOf`/`RelationOps`（驻军/建造附着/morph）、`EntityCollectionStore`+`EntitySetQueryRuntime`+`ScopeResolver`+`KnowledgeProjection*`、`ParticipantViewCapabilityMod`（势力视角切换）。详见 `docs/architecture/entity_collection_query_infrastructure.md`。
- **Progression / 科技树**：`src/Core/Gameplay/Progression`（`scopes/progressions/requirements` + `useRequirement`/`showRequirement` + `CompleteProgression`）；参考 `mods/showcases/progression_scope`、`team_research`。**M2 WarpGate 须从遗留 tag 迁移到 progression。**
- **Exchange / Relationship（外交+贸易）**：`ExchangeRuntime.TryExecute` + `ExchangeScopedOperationStore`（动态报价）+ `RelationshipRuntime`（metric/flag/band）；ADR-0003-exchange（禁造 Merchant/Trade core）；offer/accept 握手 = mod 层状态机。参考 `mods/showcases/diplomacy_trade_gate`、`gold_market`、`item_system`、`fourx_association`、`mods/FourXDemoMod`。
- **Save**：`src/Core/Persistence`（`WorldSnapshotService`/`WorldRestoreService`/`SaveSlotStore`/`ISaveStorage`）；缺玩家文件系统 `ISaveStorage` 实现 + 存档 UI（本架构里存档 UI = web 应用面板 + DataPlane command）。
- **AI**：Utility AI 已接主循环（仅 combat 出货）；macro（build/train/research/diplomacy）需 mod 出货 `AI/*.json` + order types + 执行层。参考 `mods/AIInspectorMod`、`gitbook/architecture/ai-utility-autocast-contract.md`。

---

## 4. 架构决策（SSOT，开发前遵守）

1. **运行宿主**：Raylib adapter + **CEF runtime preset**（依赖 `BrowserCefRuntimeMod`）。**禁止**使用/修改 `src/Adapters/Web`、`src/Client/Web`、`src/Platforms/Web`。
2. **UI = web app + DataPlane**：每个 mod 一个 web 应用（推荐 React，vite 构建，产物打包进 `Assets/<app>/`，源在 `WebApp/`）。所有显示走 DataPlane **topic**（snapshot/delta，复用 `EntityCollectionStore`/marker buffer），所有操作走 **command**（router → handler → 领域服务）。复用 `client.js` SDK（抽成共享包，见 B1）。
3. **所有权 SSOT**：被生产单位/建筑 `OwnershipResolver.EnsureOwnership(playerRep, e)`；`PlayerOwner` 仅输入路由。
4. **势力视角 SSOT**：command `switchParticipantView` → `ParticipantViewCapabilityRuntime.SelectPlayer/SelectTeam`；topic 按当前势力 collection key 重投影。
5. **科技树 SSOT**：`Progression/{scopes,progressions,requirements}.json`，禁 `technologies.json`/tag 解锁；多层（时代）用 `ProgressionLevelAtLeast`/链式 `ProgressionCompleted`。
6. **外交/贸易 SSOT**：`RelationshipRuntime` + `ExchangeRuntime`(+`ExchangeScopedOperationStore`)；offer/accept = mod 状态机；禁造 Merchant/Trade/Treaty core。
7. **DataPlane 复用律**：topic 是 `EntityCollectionStore`/marker buffer 的投影；未知 topic/缺服务 fail-fast；浏览器侧缓存是派生视图，按 host revision/sequence 失效。
8. **launcher SSOT**：每 Mod + CEF preset 登记 `launcher.config.json`/`launcher.presets.json`（参考 browser-ui-runtime 已加的 `BrowserCefRuntimeMod`/react-flow 条目）。
9. **加载期 fail-fast**：坏配置/缺资源加载期暴露。

---

## 5. 工作分解（Phases / Sub-Issues）

### Phase 0 — 基线与运行环境
- **0.1** worktree 切到 `d289bd3d1` 基线（见 §7 处置 codex WIP）。
- **0.2** 跑通现有 CEF 范例：`BrowserCefRuntimeMod` + `BrowserReactFlowShowcaseMod` 经 launcher CEF preset 在 Raylib 宿主启动，确认浏览器层 + DataPlane + alpha 穿透可用（作为四个 Mod 的工程模板）。

### Phase A — DataPlane 生产化插件（共享，阻塞项）
- **A1 共享 web DataPlane SDK 包**：把 `client.js`（+ `decodeEntityColumnarPacket`、fake transport）抽成可被四个 web 应用复用的本地包（如 `src/Client/WebUiKit/` 或各 `WebApp` 通过 workspace 依赖）。**注意：这是 DataPlane 客户端 SDK，不是 `src/Client/Web` Three.js。**
- **A2 EntityCollection topic 适配器**：实现可复用的 `IWebUiTopicProducer`，把 `EntityCollectionStore`（owner+key+window+revision）投影成 snapshot/delta（保留 source/role 描述、window、revision 失效）。这是把范例里的 mock producer 换成真实 Core 数据的核心件。
- **A3 高频 marker topic 适配器**：仿 `MinimapMarkerBuffer`/`MinimapScreenMarkerBuffer` 的 SoA/bucket/drop 模型实现单位/建筑 marker topic（供 minimap/世界标记）。
- **A4 command 路由助手**：标准化权限校验 + 代际解析 + 错误回传（参考范例 `WebUiCommandRouter` 用法），供四个 mod 复用。
- 验收：A2/A3 各有 DataPlane topic 单测（revision、window payload、容量/drop 诊断）；与 `UiBrowserTests` 风格对齐。

### Phase B — 共享 RTS 生产/外交能力（gameplay，无 UI 栈假设）
- **B1 `RtsProductionCapabilityMod`**：从 `RtsDemoMod` 抽可复用生产/建造/训练/驻军 runtime，接 AAC（生产完成 `EnsureOwnership`；发布 `faction.<id>.{units,buildings,production_queue}` 到 `EntityCollectionStore` 供 A2 投影）。数据契约表达 5 种范式。
- **B2 科技树查询投影**：把 scope host `ProgressionStateBuffer` + 各 tech `requirements` 评估（已解锁/可研究/锁定/前置）暴露为可被 DataPlane topic 消费的结构化只读视图。
- **B3 外交/贸易 offer-accept 状态机**：提议→pending→接受/拒绝；接受用 `ExchangeScopedOperationStore.Set` + `TryExecute`；协定用 `RelationshipRuntime.SetMetric/SetFlag`。暴露为 topic（关系矩阵、pending offers）+ command（propose/accept/reject/signPact/embargo）。
- **B4 势力切换服务**：包一层 command → `ParticipantViewCapabilityRuntime`，并驱动 topic 重投影。
- 验收：每项 NUnit gameplay 测试（不依赖浏览器）。

### Phase C — 四个 web 应用 + 四个 root mod
> 每个：`mod.json` + `<Mod>.csproj` + `<Mod>ModEntry.cs`（照搬范例接线）+ `Browser... DataPlane.cs`（topic/command，复用 A2/A3/A4 + B1–B4）+ `WebApp/`（React 源）+ `Assets/<app>/`（vite 产物）+ `assets/{game.json,Maps,GAS,Entities,Progression,Exchange,Relationships,Presentation}`。依赖 `BrowserCefRuntimeMod`/`RtsProductionCapabilityMod`/`ParticipantViewCapabilityMod`。放 `mods/showcases/production_<flavor>/`。
- **C1 M1 RedAlertLike**：Allied/Soviet；直接建造 + MCV 部署；兵营/战车工厂训练（含分步扣费）；雷达解锁进阶兵种；停火/结盟 + 矿石贸易；web UI 还原 C&C 底部指挥栏 + 右侧建造列表 + 顶部资源。
- **C2 M2 StarCraftLike**：Terran/Protoss/Zerg；SCV 工人建造 + Protoss Warp/部署 + Zerg 产卵孵化；**WarpGate 研究迁移到 progression**；三族资源贸易 + 停战；web UI 还原 SC2 命令卡 3×5 + 生产/孵化队列 + 补给/资源。
- **C3 M3 EmpireLike**：两文明；村民建造 + 建筑训练 + **时代升级科技树（≥2 级链）**；多资源（食/木/金/石，`AttributeBuffer`）；贡品/贸易 + 结盟；web UI 还原 AoE 顶部资源条 + 村民/建筑面板 + 时代树。
- **C4 M4 FourXLike**：3 帝国；城市排产 + 深科技树；**多边外交（Trust/AtWar/TradePact/Embargo）+ offer/accept 协定 + 贸易路线（`EffectRequest` 周期 buff）**；复用 `fourx_association` 链路；web UI 还原 4X 控制台：城市/排产、科技树、**外交面板（关系矩阵+提议/接受/拒绝）**、**贸易面板（报价构造+互换）**。
- 每个 Mod 验收见 §6。

### Phase D — 跨切面
- **D1 AI 势力**：每 Mod ≥1 AI 势力出货 `AI/{profiles,decisions,tasks,target_filters,...}.json` + `GAS/order_types.json`（build/train/research/executeExchange/attack/move），执行层落到 B1/GAS；可经 `AIInspectorMod` trace；人类可切 view 观战。
- **D2 存档（web 面板 + 平台存储）**：实现玩家文件系统 `ISaveStorage`；存档面板做成 web 应用一部分，经 DataPlane topic（`SaveSlotStore.ListSlots`）+ command（save/load）。确保生产队列等状态在 ECS 或经 `ISaveParticipant`。
- **D3 launcher**：四个 Mod + CEF preset 登记 `launcher.config.json`/`launcher.presets.json`（参考已有 `BrowserCefRuntimeMod`/react-flow preset）；`scripts/run-mod-launcher.cmd cli launch '$rts_starcraft_like' --adapter raylib` 起 CEF 可玩。

### Phase E — 文档 + 验收
- **E1** capability-standard 登记（`gitbook/architecture/capability-standard-showcases.md`）。
- **E2** 更新 `gitbook/architecture/uat-playable-showcase-matrix.md`。
- **E3** 验收测试：DataPlane topic 测试 + `UiBrowserTests` 风格表面测试 + 每 Mod gameplay 验收（headless + 模拟 command + 断言 + `artifacts/acceptance/` 证据）。
- **E4** 若新增共享件，回写架构文档（DataPlane 复用律以 `webui_dataplane_architecture.md` 为 SSOT）。

---

## 6. 每个 Mod 验收标准

1. **势力切换**：≥2 势力 view 切换，UI（命令卡/队列/科技/外交）随当前势力 topic 重投影；可见性/选择按 participant view 正确。
2. **生产范式**：主打范式可玩且全套覆盖；生产单位归属 `Owns` + collection 正确。
3. **训练**：建筑训练单位，队列/进度/扣费在 web UI 正确（含 C&C 分步扣费）。
4. **科技树**：数据驱动 progression，web UI 显示依赖+状态，研究后解锁生产/单位（`useRequirement`/`showRequirement` 生效）。
5. **外交协议**：势力间签订/撕毁协定（metric/flag 变化），web 按钮驱动；M4 含 offer/accept 握手。
6. **贸易互换**：经 `ExchangeRuntime` 互换，外交门控生效（`RelationshipDenied`），web 按钮驱动。
7. **全 web UI + DataPlane**：所有交互经 CEF 表面 web 应用 + DataPlane topic/command；**不依赖 `src/Adapters/Web`/`src/Client/Web`**；缺 CEF runtime 时有明确提示场景。
8. **存档**：可存读档确定性续跑（≥1 Mod 端到端）。
9. **AI 势力**：≥1 Mod 有自动运转的 AI 势力。
10. **launcher**：CEF preset 可启动。
11. **数据驱动 / fail-fast**：无隐式硬编码；坏配置/缺资源加载期报错。

---

## 7. codex WIP 处置（当前 worktree 有错误方向产物）

当前 `C:/001_AI/LudotsProd_showcase_epic`（基于错误基线 `289387b4c`）含 codex 按 v1 写的 WIP：
- **作废/重定向（UI 方向错误）**：`src/Client/Web/*` 改动（含删 `FrameProtocol.ts`）、`mods/capabilities/rts_hud_web/`、`src/Libraries/Ludots.UI/Surfaces/`（RFC-0055 租约在本架构非必需）、`src/Tools/Ludots.Launcher.React/` 中若是针对旧 web adapter 的部分。
- **可能可重定向/复用（gameplay/配置/平台）**：`mods/capabilities/rts_production/`、`mods/showcases/production_*_like/` 的**非 UI**部分（地图/GAS/Entities/Progression/Exchange/Relationships 配置 + AAC 接线）、`FileSystemSaveStorage.cs`（D2 平台存储）、`RtsProductionShowcaseSuiteAcceptanceTests.cs`、参与视图 command service。这些需**rebase 到 `d289bd3d1`** 后逐个评估能否保留。
- 处置建议：在 `d289bd3d1` 上开干净分支重做；gameplay/配置类 WIP 挑拣 cherry-pick/移植，UI 类 WIP 全部按本 epic 用 CEF + DataPlane 重写。**由人类确认**是否丢弃旧 v2 分支与这些 WIP。

---

## 8. 风险与取舍

| 风险 | 说明 | 处理 |
|------|------|------|
| **基线分叉** | `origin/main`(289387b4c) 无 browser-runtime；本地 main d289bd3d1 有 | 一切基于 d289bd3d1；推送为独立 feature 分支 |
| **CEF 体积/分发** | CEF runtime 依赖大 | 经 `BrowserCefRuntimeMod` preset；perf-baseline 模式参考范例 env 开关 |
| **mock → 真实数据** | 范例 producer 是 mock | A2/A3 必须接 `EntityCollectionStore`/marker buffer |
| **WarpGate 遗留 tag** | 与 progression SSOT 冲突 | M2 迁移到 progression |
| **Exchange 无握手** | 4X 需 accept/reject | B3 mod 层状态机 |
| **存档无玩家存储/UI** | 不可产品化 | D2 平台 `ISaveStorage` + web 面板 |
| **AI 仅 combat** | macro 无出货 | D1 出货 build/train/research AI |
| **codex 错误 WIP** | UI 触错栈 | §7 处置，人类确认 |
| **资源 SSOT 混用** | credits 既 item 又 attribute | 每资源每 Mod 选一种 |

---

## 9. 关键文件速查（基线 `d289bd3d1`）

**Browser UI Runtime / WebUI DataPlane（核心，必读）**
- `src/Libraries/Ludots.UI.Browser/`、`Ludots.UI.Browser.Cef/`、`Ludots.UI.Browser.Skia/`
- `src/Libraries/Ludots.WebUI/`（`Ludots.WebUI.DataPlane` / `Ludots.WebUI.Browser`）
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibBrowserLayerRenderer.cs`、`RaylibHostLoop.cs`
- `mods/browser/BrowserCefRuntimeMod/`
- `mods/showcases/browser_ui/BrowserUiShowcaseMod/`（原生 HTML/JS 范例）
- **`mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/`（React + DataPlane 黄金范例）**
- `docs/architecture/browser_ui_runtime.md`、`docs/architecture/webui_dataplane_architecture.md`、`docs/adr/ADR-0003-browser-ui-runtime-contract.md`
- `src/Tests/UiBrowserTests/`

**生产 gameplay 基线**
- `mods/RtsDemoMod/assets/GAS/{abilities,effects,ability_form_sets,tag_rules}.json`、`Entities/templates.json`、`Maps/rts_entry.json`
- `mods/RtsDemoMod/Systems/RtsRelationRuntimeSystem.cs`

**AAC / 势力 / Progression / Exchange / Save / AI**
- `src/Core/Association/{OwnershipResolver,ScopeKey}.cs`、`src/Core/EntityCollections/`、`src/Core/EntityQueries/`、`src/Core/Knowledge/`、`src/Core/Presentation/Minimap/`
- `mods/capabilities/participant_view/ParticipantViewCapabilityMod/`
- `src/Core/Gameplay/Progression/`；`mods/showcases/{progression_scope,team_research}/`
- `src/Core/Gameplay/Exchange/`、`src/Core/Gameplay/Relationships/`；`mods/showcases/{diplomacy_trade_gate,gold_market,item_system,fourx_association}/`、`mods/FourXDemoMod/`
- `src/Core/Persistence/`、`src/Platform/Ludots.Platform.Abstractions/ISaveStorage.cs`
- `src/Core/Gameplay/AI/`、`mods/AIInspectorMod/`

**Launcher**
- `launcher.config.json`、`launcher.presets.json`、`scripts/run-mod-launcher.cmd`

---

## 10. 建议 PR 切分

1. Phase 0（基线 + 跑通 CEF 范例）
2. A1（共享 DataPlane web SDK）
3. A2（EntityCollection topic 适配器）+ A3（marker topic）
4. A4（command 路由助手）
5. B1（`RtsProductionCapabilityMod` + AAC）
6. B2（科技树投影）+ B4（势力切换服务）
7. B3（外交/贸易 offer-accept 状态机）
8. C1 RedAlertLike（mod + web app）
9. C2 StarCraftLike（含 WarpGate→progression）
10. C3 EmpireLike（多层时代树）
11. C4 FourXLike（外交/贸易核心）
12. D1 AI 势力
13. D2 存档（web 面板 + 平台存储）
14. D3 launcher
15. E1–E4 文档 + 验收
