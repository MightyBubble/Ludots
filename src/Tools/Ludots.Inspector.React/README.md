# Ludots Inspector

连接 Agent Bridge（默认 `http://127.0.0.1:47921`）的可视化工具页：左侧按域列出 `/tools`，中间按 schema 填参调用，右侧是该工具自己的 debug。

与 CLI / MCP / curl 共用同一 `BuiltinAgentTools` 目录，不维护第二份名单。门户说明：[Agent 调试桥 → 可视化调试面板](https://mightybubble.github.io/Ludots/agent-bridge.html#doc/inspector)。

```bash
cd src/Tools/Ludots.Inspector.React
npm install
npm run dev   # http://127.0.0.1:5179
```

宿主需已加载 `AgentBridgeMod`。顶栏出现 `ok · N tools` 后再选工具。
