# Ludots Inspector

人类前端：连接运行中的 Agent Bridge（`http://127.0.0.1:47921`），对 `/tools` 目录里**每一个**工具生成表单，调用与 CLI / MCP 相同的 `POST /rpc` 方法。

## 开发

```bash
cd src/Tools/Ludots.Inspector.React
npm install
npm run dev   # http://127.0.0.1:5179
```

游戏进程需已加载 `AgentBridgeMod`。桥侧已开 CORS，浏览器可直连环回端口。

## 与 AI 前端的关系

| 客户端 | 谁用 | 协议 |
|--------|------|------|
| `Ludots.AgentBridge.Cli` | AI / 脚本 | 同一 HTTP JSON-RPC |
| `Ludots.AgentBridge.Mcp` | MCP 宿主 | stdio → 同一 HTTP |
| Inspector（本应用） | 人 | 浏览器 → 同一 HTTP |

不另造命令协议。计划 SSOT：GitHub epic #1056。
