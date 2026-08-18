# RFC-0066 Agent Debug Bridge（Ludots Harness for AI Agents）

Status: Draft (implementation in progress on branch `feat/agent-debug-bridge`)

## 1 背景与目标

Ludots 目前没有面向 AI Agent 的运行时调试/操控接口。现有相关设施都是"给人看"或"给测试用"的：

- `DiagnosticsOverlayMod`：F5/F6/F7 屏幕叠加层，面向人类开发者。
- `AIInspectorMod`：事件触发的配置打印。
- `LiveSkillWorkbench`：进程内 Live 编辑管线，无外部传输层。
- WebUI DataPlane：面向 Web 面板的主题投影，不面向 Agent。

结果是 Agent 想调试一个运行中的 Ludots 进程，只能走 computer-use（截屏 + 模拟键鼠），既不可靠也无法触达 GAS / Order / Input 的内部状态。

本 RFC 定义 **Agent Debug Bridge**：一套内置于运行时、面向任意 AI Agent 的调试与操控框架，目标是让没有多模态能力的 Agent 也能完整理解并操作游戏运行时。

设计目标：

1. **任意 Agent 可用**：传输层是 localhost HTTP + JSON-RPC 2.0（任何 Agent 都会发 HTTP），外加一个零依赖 MCP stdio 适配器（MCP 兼容 Agent 直接挂载）。Bridge 语义与传输解耦，传输是适配器。
2. **结构化优先**：对无多模态能力的 Agent，输出镜头内实体（屏幕坐标、屏幕占比）、统一 UI 树（含矩形与可见性）、GAS/Order/Input 内部状态。截屏只是可选补充（host 能力，非必备路径）。
3. **一切皆 Mod 原则**：Bridge 本体是一个可选 Mod（`AgentBridgeMod`），通过 `ModPaths` 启用；工具注册表开放给其他 Mod 扩展自定义工具。
4. **零 fallback**：缺服务、缺能力、未知工具一律显式报错，错误带具体原因码。

## 2 架构

```text
AI Agent (任意)
   ├── HTTP 直连:  POST http://127.0.0.1:<port>/rpc   (任何 Agent / curl / 脚本)
   └── MCP 挂载:   Ludots.AgentBridge.Mcp (stdio) ──► HTTP ──┐
                                                             ▼
Ludots 进程:  AgentBridgeHttpServer (后台线程, 仅绑 127.0.0.1)
                  │  enqueue (请求入队, 后台线程绝不触碰 ECS)
                  ▼
             AgentBridgeSystem (游戏线程, 每帧 pump)
                  │  dispatch
                  ▼
             AgentToolRegistry ──► IAgentTool 实现 (读 World / UiScene / GAS / Input)
                  │
                  ▼
             实例注册表 artifacts/agent-bridge/sessions/<pid>.json (port/pid/host/label/capabilities/mods/mapId)
```

关键约束：

- **线程模型**：HTTP 监听线程只负责收发字节；所有工具执行都经由 `ConcurrentQueue` + 每帧 pump 在游戏线程完成。请求-响应用 `TaskCompletionSource` 桥接，超时显式报错。
- **为什么不是纯进程内 MCP**：MCP 是 stdio/SSE 每客户端进程模型，把 MCP server 嵌入游戏进程会把游戏生命周期和单个客户端绑死，且引入 MCP SDK 依赖。HTTP 是通用底座，MCP 是一层薄适配（与 WebUI DataPlane 的 transport-neutral 哲学一致：`IWebUiDataTransport` 之于 CEF/BLUI，等同 `AgentBridgeRuntime` 之于 HTTP/MCP）。
- **为什么不是 computer-use**：所有状态都有结构化出口；截屏退化为可选 host 能力。

## 3 复用清单（防重复造轮子）

| 需求 | 复用的既有基建 |
|------|----------------|
| 服务定位 | `GameEngine.TryGetService` / `CoreServiceKeys`（World、GameSession、ScreenProjector、UIRoot、TagOps、GasDiagnosticEventBuffer、InputHandler、OrderAdmissionResultBuffer 等全部可达） |
| 世界→屏幕投影 | `IScreenProjector.WorldToScreen`（`CoreScreenProjector`，含投影缓存与 revision） |
| 实体枚举 | Arch `World.Query` + `WorldPositionCm` / `Name` / `AttributeBuffer` 组件（模式见 `DiagnosticsOverlaySystem`） |
| UI 树 | `UiScene.EnumerateVisualNodes()` / `QuerySelectorAll` / `HitTest`；Compose/Reactive/Markup 三写法本来就归一到同一 `UiScene`（ADR-0002），一份树遍历天然覆盖三条路径 |
| UI 表面所有权 | `UiSurfaceHost`（lease/segment 模型，`RFC-0055`）；Browser surface 经 `IBrowserRuntime` 枚举，`UiNode.CanvasContent` 标记浏览器嵌入点 |
| 时间控制 | `IPacemaker` / `TurnBasedPacemaker` / `engine.Pacemaker`（`DiagnosticsOverlaySystem` 的 F8 模式） |
| 输入注入 | `PlayerInputHandler.InjectAction` / `InjectButtonPress` / `InjectButtonRelease`（已存在）；UI 键盘注入 `RaylibSyntheticKeyboardInput` |
| 订单下发 | `OrderSubmitter.Submit` + `OrderTypeRegistry`（正式订单准入管线，含规则校验与 terminal result） |
| GAS 诊断 | `GasDiagnosticEventBuffer`（已注册为 CoreServiceKeys 服务） |
| 系统挂载 | `SystemFactoryRegistry.Register` + `TryActivate`（`DiagnosticsOverlayMod` 模式） |
| Mod 扩展 | `IModContext.Extensions`（`ModExtensionHub`）供其他 Mod 注册自定义工具 |

## 4 组件划分

| 组件 | 位置 | 职责 |
|------|------|------|
| `Ludots.AgentBridge` | `src/Libraries/Ludots.AgentBridge/` | 平台无关语义层：工具注册表、工具上下文、内置工具、请求泵、HTTP server |
| `AgentBridgeMod` | `mods/AgentBridgeMod/` | 把一切接进运行时：注册系统、注册内置工具、按配置启停 |
| `Ludots.AgentBridge.Mcp` | `src/Tools/Ludots.AgentBridge.Mcp/` | 零依赖 MCP stdio → HTTP 适配器（initialize / tools/list / tools/call / ping） |

`Ludots.AgentBridge` 引用 `Ludots.Core` 与 `Ludots.UI`（平台无关模型层）；不引用 Skia/Raylib/CEF。截屏等 host 能力通过 `CoreServiceKeys` 扩展键由宿主适配器注入，缺失时工具显式报 `capability.unavailable`。

## 5 工具协议

所有工具经 JSON-RPC 2.0 调用：`POST /rpc {"jsonrpc":"2.0","id":1,"method":"<tool>","params":{...}}`。

自描述端点：

- `GET /health` → `{ ok, pid, tick, uptimeMs }`
- `GET /tools` → 工具目录（name / description / inputSchema JSON Schema），供 Agent 发现
- `GET /` → 人读说明 + 发现文件路径

内置工具（v1）：

| 工具 | 说明 |
|------|------|
| `ludots.session.info` | 引擎状态：tick、已加载 Mod、地图、相机、分辨率、bridge 版本、实例身份块（pid/port/host/label/capabilities） |
| `ludots.instances.list` | 枚举实例注册表内全部实例并做 `/health` 活探测（多实例定位的游戏内出口） |
| `ludots.time.get` / `ludots.time.control` | 查询时间状态；`pause` / `resume` / `step {steps}`（基于 Pacemaker 换绑） |
| `ludots.entities.query` | 镜头内实体：`entityId`、`name`、世界坐标、屏幕坐标、屏幕包围盒、屏幕占比、可见性；支持 `offset/limit/nameFilter/onScreenOnly`，超限带 dropped 诊断（DataPlane 风格） |
| `ludots.ui.tree` | 统一 UI 树：节点 id/tag/elementId/class/text/布局矩形/屏幕占比/pseudo-state/滚动状态/是否 browser canvas；`maxDepth/maxNodes/rootElementId` 分页 |
| `ludots.ui.query` | CSS 选择器查询（复用 `UiScene.QuerySelectorAll`），返回同树节点形状 |
| `ludots.ui.click` | 按 elementId 或屏幕坐标 HitTest 并派发指针事件 |
| `ludots.gas.entity` | 实体 GAS 状态：tags（名称解析）、attributes（base/current）、active effects、当前/排队订单 |
| `ludots.gas.diagnostics` | `GasDiagnosticEventBuffer` 转储（系统/指标/容量/计数） |
| `ludots.orders.inspect` | 指定实体 OrderBuffer 明细 + 全局 OrderAdmission/Terminal 结果缓冲 |
| `ludots.orders.issue` | 经 `OrderSubmitter.Submit` 下发订单（走正式准入规则，返回 `OrderSubmitResult`） |
| `ludots.input.state` | 输入上下文栈、动作清单、指针快照、UI 捕获状态、窗口层虚拟设备状态 |
| `ludots.input.inject` | **语义动作层**：`press` / `release` / `action {value}`（复用 `PlayerInputHandler.Inject*`） |
| `ludots.input.raw` | **窗口原始层**：`pointerMove/pointerDown/pointerUp/click/scroll/keyDown/keyUp/press/type/releaseAll`（经 `SyntheticInputDevice` 进入宿主轮询点，与物理输入同管线——UI 命中、捕获、绑定全部生效） |
| `ludots.screenshot` | 经 `IHostFrameCapture` 端口抓下一呈现帧 PNG 到 `artifacts/agent-bridge/shots/`；暂停下可用 |
| `ludots.recording.start` / `ludots.recording.stop` | 录屏为 PNG 序列（`intervalMs/maxFrames`），stop 写 `manifest.json`；agent 可按需抽帧阅读 |

错误协议：JSON-RPC error，`-32602` 参数错，`-32601` 未知工具，`-32000` 域错误并带 `data.code`（如 `entity.not_found`、`service.unavailable:<key>`、`capability.unavailable:<name>`）。

### 六边形端口（host 能力）

截图/录屏与窗口层输入不焊死 Raylib，端口定义在引擎无关层，宿主适配器实现：

| 端口 | 位置 | 语义 | Raylib 实现 |
|------|------|------|-------------|
| `IHostFrameCapture` | `Ludots.Platform.Abstractions` | `CapturePngAsync()` 请求-兑现模型，下一呈现帧完成时返回 PNG 字节 | `RaylibFrameCaptureService`：host loop 在 `EndDrawing` 后 `OnFramePresented()` 回读帧缓冲（复用 evidence 截图同一机制） |
| `SyntheticInputDevice` | `Ludots.Core.Input.Runtime` | 引擎无关虚拟设备：指针位置覆写、按钮/键盘边沿、滚轮、字符；宿主每帧 `AdvanceFrame()` 后在轮询点叠加虚拟状态 | `RaylibInputBackend`（玩法路径）+ `RaylibHostLoop.UpdateInput/ForwardKeyboardInput`（UI 路径）咨询同一实例 |

未来 Unity / Unreal / Godot 宿主只需各自实现这两个端口（Unity: `ScreenCapture` + InputSystem `QueueEvent`；Unreal: screenshot request + `FSlateApplication` 事件；Godot: viewport 回读 + `Input.parse_input_event`），桥工具零改动。宿主未实现时工具显式报 `service.unavailable`，无 fallback。

录屏不在端口上单独开口：桥侧以 `intervalMs` 节奏重复调用一次性截图原语实现，端口保持单方法。

## 6 启用方式

两种启用路径（`LaunchModInjection`，`GameBootstrapper.ResolveGraphPlan` 在 authored graph 校验之后应用）：

1. **一键注入**：`LUDOTS_AGENT_BRIDGE=1/true/on` 启动任意 Ludots 进程即自动把 `AgentBridgeMod` 注入 mods 列表（图里已含则跳过）；`LUDOTS_EXTRA_MODS=a,b` 可同法注入任意 mod（解析 `<repoRoot>/mods/<id>`，找不到显式报错）。
2. **图内声明**：`game.*.json` 的 `ModPaths` 加入 `mods/AgentBridgeMod`。

配置经环境变量/桥接配置节：

- `LUDOTS_AGENT_BRIDGE=0` 强制关闭（即使 Mod 已加载/已注入）
- `LUDOTS_AGENT_BRIDGE_PORT=<port>` 覆盖端口（默认 47921；占用时自动 +1 重试最多 16 次并写入注册表）
- `LUDOTS_AGENT_BRIDGE_LABEL=<label>` 实例标签；`LUDOTS_AGENT_BRIDGE_HOST=<host>` 覆盖宿主类型推断
- 实例注册表：`artifacts/agent-bridge/sessions/<pid>.json`（`{pid, port, version, host, label, capabilities, mods, mapId, startedAtUtc, processPath}`）；启动时清扫死 pid 文件，进程退出时删除自身

**多实例定位**：MCP 适配器 `--instance label:X|host:X|map:X|pid:N|latest` + `--registry <dir>`（缺省：`LUDOTS_AGENT_BRIDGE_REGISTRY` > 从 CWD 向上找 global.json 定位仓库）。活探测 = GET `/health` 比对 pid；歧义显式报错并列候选，零命中显式报错。未来 raylib / unity 宿主并存时按 `host:` 或 `label:` 区分。

安全边界：仅绑定 `127.0.0.1`；这是调试接口，不做鉴权——与 `dotnet-dump` / JVM debugging agent 同级别信任模型。

## 7 与其他子系统的边界

- 不复制定义 `EntityCollectionStore`、不建平行 entity store；实体查询直读 Arch World。
- 不把 Browser DOM 拉进 Core：CEF/Web 应用的 DOM 树属 provider 能力，v1 在 `ui.tree` 中标注 browser canvas 节点并列出 surface 元数据；provider 侧 DOM 求值是后续切片（经 `IBrowserMessageBridge`，与 DataPlane 同边界）。
- Agent 工具不改写游戏真相：除 `orders.issue` / `input.inject` / `time.control` 这些显式操控工具外，一切只读。

## 8 验收

1. `Ludots.App.Raylib` 以含 `AgentBridgeMod` 的配置启动，`GET /health` 返回 ok。
2. 无多模态 Agent（本机 pi coding agent + deepseek-v4-flash）仅凭 `GET /tools` 自描述完成：列工具 → 会话信息 → 镜头内实体 → UI 树 → 单实体 GAS → pause/step/resume → 注入输入/下发订单。全程不截图即完成调试闭环。
3. MCP 适配器 `tools/list` 与 `tools/call` 与 HTTP 目录一致。

### 验收结果（2026-08-17，worktree `feat/agent-debug-bridge`）

- 全部工具经 curl 人工自测通过（v1 13 个 + v2 帧捕获与窗口层输入 4 个）；MCP 适配器 initialize / tools/list / tools/call 冒烟通过（目录与 HTTP 一致）。
- 截图目验为真实帧缓冲（UI + 3D 场景）；录屏 20/20 帧 + manifest 落盘；`input.raw` 点击工具栏按钮实测驱动相机跟随。
- pi + deepseek-v4-flash 验收通过：自主完成全部 6 步 + bonus（stop 订单闭环），并在镜头停留在场外时**自发用 `ui.click` 点击工具栏"Follow selection"按钮把镜头转回实体群**——这正是结构化 UI 树 + 操控工具替代 computer-use 的目标行为。
- pi 反馈并已修复：`time.control step` 响应增加 `targetTick`（步进在后续帧执行）；`ui.click` elementId 未命中时错误信息指向 `ui.tree`/`ui.query` 发现路径。

### 验收结果（2026-08-18，v3 健壮性切片）

- **一键启动对照实验**：无 env 时桥不激活；`LUDOTS_AGENT_BRIDGE=1` 时 mods 列表自动出现 `AgentBridgeMod`，桥监听自动端口。
- **双实例并存**：label=`injected`（47921）与 label=`demo`（47922）同时运行，注册表两文件身份正确；`--instance label:demo` 精确路由，`--instance host:raylib` 显式报歧义列候选，杀死实例后选择器报"no alive match"。
- **18 工具逐一实证**：证据 JSON 存 `artifacts/agent-bridge/showcase/`，逐项结论见 `gitbook/acceptance/agent-debug-bridge-uat.md`；pause→step 3→tick 精确 +3→resume；`orders.issue` 在暂停下抓到 admission `Queued`，恢复后指令进入实体 OrderBuffer `active` 态且目标坐标正确；截图目验 `ui.click` 点 RTS 按钮后相机切换生效。
