# Agent 调试桥（Agent Debug Bridge）

> 设计 SSOT：[`docs/rfcs/RFC-0066-agent-debug-bridge.md`](https://github.com/mightybubble/Ludots/blob/main/docs/rfcs/RFC-0066-agent-debug-bridge.md)。本页是面向使用者的运行手册。

Ludots Agent 调试桥让**任意 AI coding agent**（有无多模态皆可）对运行中的游戏进程做结构化调试与操控，取代 screenshot + computer-use 的脆弱路径。

## 架构一句话

```
AI Agent ──HTTP JSON-RPC──▶ 127.0.0.1:47921（AgentBridgeHttpServer，传输线程只收发字节）
                                │ ConcurrentQueue 入队
                                ▼
                    游戏线程每帧 Pump（AgentBridgeSystem，presentation 组）
                                │ 执行工具（直读 Arch World / UiScene / GAS 缓冲）
                                ▼
                          TaskCompletionSource 回包
```

MCP 客户端（Claude Code、pi 等）另配零依赖 stdio 适配器 `src/Tools/Ludots.AgentBridge.Mcp`，它把 MCP `tools/list` / `tools/call` 转发到 HTTP 桥——游戏生命周期不被任何单个 MCP 客户端绑死。

## 启用

启动配置的 Mod 集合加入 `AgentBridgeMod` 即启用（参考 `src/Apps/Raylib/Ludots.App.Raylib/raylib.agent-demo.launch.graph.json`）。

| 配置 | 说明 |
|------|------|
| `LUDOTS_AGENT_BRIDGE=0` | 强制关闭（即使 Mod 已加载） |
| `LUDOTS_AGENT_BRIDGE_PORT=<port>` | 覆盖端口（默认 47921，占用时自动 +1 重试最多 16 次） |
| 发现文件 | `artifacts/agent-bridge/session.json`（`{ port, pid, tools }`），进程退出时删除 |

安全边界：仅绑定 `127.0.0.1`，无鉴权——与 `dotnet-dump` 同信任模型，属调试接口。

## 端点

- `GET /health` → `{ ok, pid, port, pendingRequests, pumpCount, lastPumpUtc }`（`pumpCount` 不涨说明游戏主循环停了）
- `GET /tools` → 自描述工具目录（name / description / inputSchema）
- `POST /rpc` → JSON-RPC 2.0：`{"jsonrpc":"2.0","id":1,"method":"ludots.session.info","params":{}}`

## 内置工具（13）

| 域 | 工具 | 能力 |
|----|------|------|
| 会话 | `ludots.session.info` | tick / 地图 / Mod 清单 / 相机 / 分辨率 |
| 时间 | `ludots.time.get` · `ludots.time.control` | pause（换绑 TurnBasedPacemaker）/ step N（响应带 `targetTick`）/ resume |
| 实体 | `ludots.entities.query` | 世界坐标→屏幕投影 rect、**屏幕占比**、可见性；`offset/limit/nameFilter/onScreenOnly` |
| UI | `ludots.ui.tree` · `ludots.ui.query` · `ludots.ui.click` | 统一 UiScene 遍历（markup / composite / reactive 三写法归一，browser canvas 节点有标注）；CSS 选择器；elementId 或坐标点击 |
| GAS | `ludots.gas.entity` · `ludots.gas.diagnostics` | tags（名称解析）/ attributes / active effects / ability 槽位；诊断事件缓冲转储 |
| 订单 | `ludots.orders.inspect` · `ludots.orders.issue` | 准入/终态缓冲明细；经正式 intake 路径下发订单，全生命周期可观测 |
| 输入 | `ludots.input.state` · `ludots.input.inject` | 输入上下文与 UI 捕获状态；合成 press / release / set（走 `PlayerInputHandler.Inject*`，与真实输入同路径） |

错误协议：`-32601` 未知工具，`-32602` 参数错，`-32000` 域错误（`data.code` 如 `entity.not_found`、`ui.node_not_found`、`bridge.timeout`）。错误信息自带下一步指引（如发现工具、合法键来源）。

## MCP 接入

```bash
# stdio 适配器（零依赖）；桥地址解析顺序：argv > LUDOTS_AGENT_BRIDGE_URL > 发现文件 > 默认端口
dotnet exec src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net8.0/Ludots.AgentBridge.Mcp.dll http://127.0.0.1:47921
```

在 MCP 客户端配置中把上述命令注册为 stdio server 即可，`tools/list` 与 HTTP 目录一致。

## 扩展

其他 Mod 可通过 `AgentBridgeModEntry.ToolRegistryKey`（`ServiceKey<AgentToolRegistry>`）从 `GlobalContext` 取注册表并注册自己的 `IAgentTool`——工具即刻出现在 `/tools` 目录与 MCP 客户端中。

## 已验证的验收闭环

pi coding agent + deepseek-v4-flash（无多模态）仅凭 `GET /tools` 自描述完成：列工具 → 会话快照 → 镜头内实体 → UI 树 → 单实体 GAS → pause/step 3/resume → stop 订单闭环；并自发用 `ui.click` 操作沙盒工具栏把镜头转回实体群。详见 RFC §8。
