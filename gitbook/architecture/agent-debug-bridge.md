# Agent 调试桥（Agent Debug Bridge）

设计 SSOT：[`RFC-0066`](https://github.com/mightybubble/Ludots/blob/main/docs/rfcs/RFC-0066-agent-debug-bridge.md) · 计划 [epic #1056](https://github.com/MightyBubble/Ludots/issues/1056)。任务实操见 [Agent Bridge](../agent-bridge.md)。

语义层是 `IAgentTool` 注册表；客户端走同一环回 HTTP JSON-RPC（`method` = 工具名）。

## 架构一句话

```
CLI / MCP / Inspector / curl
        │ HTTP JSON-RPC
        ▼
127.0.0.1:47921（AgentBridgeHttpServer；CORS 给浏览器）
        │ ConcurrentQueue
        ▼
游戏线程 Pump（AgentBridgeSystem）
        │ BuiltinAgentTools → AgentToolRegistry
        ▼
TaskCompletionSource 回包
```

| 客户端 | 路径 |
|--------|------|
| CLI | `src/Tools/Ludots.AgentBridge.Cli` |
| MCP | `src/Tools/Ludots.AgentBridge.Mcp`（stdio → HTTP） |
| Inspector | `src/Tools/Ludots.Inspector.React`（紧凑面板；每工具独立 debug） |
| 地址解析 | `AgentBridgeEndpoint` |

## 启用

启动配置的 Mod 集合加入 `AgentBridgeMod` 即启用（参考 `src/Apps/Raylib/Ludots.App.Raylib/raylib.agent-demo.launch.graph.json`）。

| 配置 | 说明 |
|------|------|
| `LUDOTS_AGENT_BRIDGE=0` | 强制关闭（即使 Mod 已加载） |
| `LUDOTS_AGENT_BRIDGE_PORT=<port>` | 覆盖端口（默认 47921，占用时自动 +1 重试最多 16 次） |
| 发现文件 | `artifacts/agent-bridge/sessions/<pid>.json`（`{ port, pid, tools, … }`），进程退出时删除 |

安全边界：仅绑定 `127.0.0.1`，无鉴权——与 `dotnet-dump` 同信任模型，属调试接口。

## 端点

- `GET /health` → `{ ok, instance, pendingRequests, pumpCount, lastPumpUtc }`（`pumpCount` 不涨说明游戏主循环停了）
- `GET /tools` → 工具目录（以本端点 / `BuiltinAgentTools` 为准）
- `POST /rpc` → `{"jsonrpc":"2.0","id":1,"method":"ludots.session.info","params":{}}`
- CORS：浏览器可连环回端口

## 内置工具

注册 SSOT：`BuiltinAgentTools.RegisterAll`（`AgentBridgeMod` 启动时调用）。新增工具必须进注册表并让 `AgentBridgeToolCatalogContractTests` 变绿，禁止只改文档。

| 域 | 工具 | 能力 |
|----|------|------|
| 会话 | `ludots.session.info` | tick / 地图 / Mod 清单 / 相机 / 分辨率 |
| 时间 | `ludots.time.get` · `ludots.time.control` | pause / step N / resume |
| 相机 | `ludots.camera.control` | get / set / follow / unfollow |
| 日志 | `ludots.logs.tail` | 进程内日志环形缓冲（桥激活时挂入） |
| 事件 | `ludots.events.fire` | 经正式 Trigger 路径发事件 |
| 空间 | `ludots.entities.pick` · `ludots.spatial.query` | 点选 + radius/aabb/cone/rect/line |
| 导航 | `ludots.nav.project` · `ludots.nav.findPath` | 投影 / 寻路 |
| 实体 | `ludots.entities.query` | 屏占比、过滤、分页 |
| UI | `ludots.ui.tree` · `ludots.ui.query` · `ludots.ui.click` | 树 / 选择器 / 点击 |
| GAS | `ludots.gas.entity` · `ludots.gas.diagnostics` | 属性槽 / 诊断缓冲 |
| 订单 | `ludots.orders.inspect` · `ludots.orders.issue` | 观测 / 正式 intake 下发 |
| 输入 | `ludots.input.state` · `ludots.input.inject` · `ludots.input.raw` | 状态 / 语义注入 / 窗口层注入 |
| 图调试 | `ludots.graph.debug` | GraphDebugTrace list / configure / drain |
| 帧捕获 | `ludots.screenshot` · `ludots.recording.start/stop` | PNG / 序列录制 |
| Presenter | `ludots.presenters.query` · `.desync` · `.screen` | 视觉代理全链 / 分歧 / 席位投影 |

### 输入两层模型

- **语义动作层**（`input.inject`）：直接写 `PlayerInputHandler` 的注入表，绕过硬件与 UI——用来驱动游戏行为（放技能、下命令）。
- **窗口原始层**（`input.raw`）：事件从宿主输入轮询点进入，UI 命中测试、指针捕获、键位绑定全部生效——用来验证"用户真的点这个按钮会怎样"。

### 六边形端口

截图与窗口层输入是宿主能力端口，不绑定具体引擎：`IHostFrameCapture`（`Ludots.Platform.Abstractions`）与 `SyntheticInputDevice`（`Ludots.Core.Input.Runtime`）由宿主适配器实现/接线（Raylib 已接：`RaylibFrameCaptureService` + `RaylibInputBackend`/`RaylibHostLoop` 咨询虚拟设备）。未来 Unity / Unreal / Godot 宿主实现同两端口即可，桥工具零改动；未实现时工具显式报 `service.unavailable`。

错误协议：`-32601` 未知工具，`-32602` 参数错，`-32000` 域错误（`data.code` 如 `entity.not_found`、`ui.node_not_found`、`bridge.timeout`）。错误信息自带下一步指引（如发现工具、合法键来源）。

## 客户端接入

地址：`AgentBridgeEndpoint`（显式 URL > `LUDOTS_AGENT_BRIDGE_URL` > discovery > `47921`）。

```bash
dotnet build src/Tools/Ludots.AgentBridge.Cli/Ludots.AgentBridge.Cli.csproj -c Release
dotnet exec src/Tools/Ludots.AgentBridge.Cli/bin/Release/net8.0/Ludots.AgentBridge.Cli.dll tools --names
dotnet exec src/Tools/Ludots.AgentBridge.Cli/bin/Release/net8.0/Ludots.AgentBridge.Cli.dll call ludots.session.info

dotnet build src/Tools/Ludots.AgentBridge.Mcp/Ludots.AgentBridge.Mcp.csproj -c Release
dotnet exec src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net8.0/Ludots.AgentBridge.Mcp.dll http://127.0.0.1:47921

cd src/Tools/Ludots.Inspector.React && npm install && npm run dev
# → http://127.0.0.1:5179 ；每工具独立 debug（req/res），非全屏壳
```

## 扩展

其他 Mod 取 `AgentBridgeModEntry.ToolRegistryKey`（`ServiceKey<AgentToolRegistry>`）注册 `IAgentTool` 后，即出现在 `/tools` 与各客户端。

## 已验证闭环

无多模态 agent 仅凭 `GET /tools` 完成：列工具 → 会话 → 镜头内实体 → UI 树 → GAS → pause/step/resume → 订单；并用 `ui.click` 转镜头。详见 RFC §8。
