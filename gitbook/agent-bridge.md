# Agent Bridge

> 面向 AI coding agent 的 Ludots 运行时操控与取证通道：环回 HTTP JSON-RPC，24 个自描述工具，零多模态依赖——看画面、点 UI、放技能、查状态、抓证据，全部用结构化 JSON 完成，取代 screenshot + computer-use 的脆弱路径。引擎侧资产验收工具（拖入商店模型验 PBR/OBJ 转换）见[教程：Raylib 资产验收台](raylib-asset-acceptance.md)。
>
> 设计 SSOT：[RFC-0066](https://github.com/mightyBubble/Ludots/blob/main/docs/rfcs/RFC-0066-agent-debug-bridge.md)；工具域参考手册：[架构页 · Agent 调试桥](architecture/agent-debug-bridge.md)。**本页是任务视角的实操指南**，所有请求/响应示例来自真实验证会话（`raylib.agent-demo` · ChampionSkillSandbox）。

## 60 秒上手

```bash
# 1. 构建（app + 图中各 mod）
dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Debug
dotnet build mods/AgentBridgeMod/AgentBridgeMod.csproj -c Debug   # 其余 mod 同理

# 2. 启动（launch graph 的 orderedModIds 含 AgentBridgeMod 即启用）
cd src/Apps/Raylib/Ludots.App.Raylib/bin/Debug/net8.0
dotnet Ludots.App.Raylib.dll launcher.agent-demo.runtime.json

# 3. 判活 + 发现工具
curl -s http://127.0.0.1:47921/health   # {"ok":true,"pid":...,"pumpCount":41,...}
curl -s http://127.0.0.1:47921/tools    # 20 个工具的 name/description/inputSchema

# 4. 第一个调用（JSON-RPC 2.0）
curl -s -X POST http://127.0.0.1:47921/rpc \
  -d '{"jsonrpc":"2.0","id":1,"method":"ludots.session.info","params":{}}'
```

环境变量：`LUDOTS_AGENT_BRIDGE=0` 强制关闭；`LUDOTS_AGENT_BRIDGE_PORT` 换端口（默认 47921，占用自动 +1）；发现文件是 artifacts/agent-bridge/session.json（运行时生成、不入库，含 port/pid/完整工具目录），进程退出即删。仅绑定 127.0.0.1，无鉴权调试接口。

## Agent 标准工作循环：观察 → 驱动 → 验证

每次调试会话都走这个闭环，工具选择自然浮现：

```text
/health 判活 ──▶ session.info（tick/mods）──▶ entities.query（找目标，看 screenCoverage 是否在镜头内）
      │                                                    │
      ▼                                                    ▼
  驱动：orders.issue / input.inject / camera.follow / events.fire / ui.click
      │
      ▼
  验证：gas.entity（属性变了？）/ ui.tree（面板亮了？）/ screenshot（画面对了？）/ logs.tail（日志说了什么？）
```

**判活要领**：`/health` 的 `pumpCount` 必须在涨——不涨说明游戏主循环卡死/暂停，后续一切调用都不可信。

## 按任务查工具（不按域）

| 你想做什么 | 用什么 | 关键参数/要领 |
|-----------|--------|---------------|
| 确认游戏活着、-loaded 了哪些 mod | `ludots.session.info` | 无参；tick 在涨=循环活着 |
| 找一个实体（敌人/英雄/召唤物） | `ludots.entities.query` | `nameFilter`（大小写不敏感子串）、`onScreenOnly:true` 只看镜头内的；返回 `screenCoverage`（占屏比）判断"看得清吗" |
| 屏幕某点下面是哪个实体 | `ludots.entities.pick` | `{x, y, radiusPixels?=24}`；与点击选中同一条生产解析链（selectable + 知识门控），未命中返回 `hit:false` |
| 一片区域里有谁（半径/扇形/直线） | `ludots.spatial.query` | `shape: radius/aabb/cone/rect/line` + 中心与尺寸参数；直走生产空间查询服务（技能/自动索敌同层），带 `distanceCmToCenter` 与 `dropped` 诊断 |
| 某点可不可走 / A→B 怎么走 | `ludots.nav.project` / `ludots.nav.findPath` | project 命中可行走三角形；findPath 返回 `status`（NotReady=瓦片未就绪）+ 路径点 + `travelCostCm` |
| 看某实体的血量/属性/技能槽 | `ludots.gas.entity` | `{entityId}`；tags 名称已解析、attributes 只列非零 |
| 看面板/按钮长什么样 | `ludots.ui.tree` / `ludots.ui.query` | tree 全量；query 用 CSS 选择器——**实测注意**：选择器按 tag/`#id`/`.class` 匹配，本仓 UI 多用 tag（`selector:"button"` 命中 10 个，`.button` 命中 0 个，因为节点没有 class） |
| 点一个按钮 | `ludots.ui.click` | `{elementId}` 或裸坐标 `{x,y}`；返回 `handled:true/false` + 命中节点的 rect/pseudoState——点到容器会 `handled:false`，换 elementId |
| 让实体做一件事（移动/施法/攻击） | `ludots.orders.issue` | `orderType` 是**字符串键或数字 id**；合法键在 `mods/LudotsCoreMod/assets/GAS/order_types.json`：实测 `castAbility` / `moveTo` / `attackTarget` / `stop` / `chainPass` 等；`targetEntityId` 或 `worldXCm/worldYCm` 按订单类型二选一 |
| 模拟按键（放技能） | `ludots.input.inject` | `{actionId, mode:"press"|"release"|"set"}`——语义层，走游戏输入绑定表；**press 后记得 release** |
| 模拟真实鼠标键盘（验证 UI 交互） | `ludots.input.raw` | `{op:"pointerMove"|...,"x","y"}`——窗口层，UI 命中/指针捕获全生效；下一帧才应用 |
| 查输入管线状态 | `ludots.input.state` | 看 `uiCaptured`（UI 是否吃掉了输入）再决定用哪层注入 |
| 冻结世界逐步看 | `ludots.time.control` | `pause` → `step {steps:N}`（响应带 `targetTick`）→ `resume`；pause 后 screenshot 依然可用 |
| 把镜头对准目标 | `ludots.camera.control` | `follow {entityId}` 实体跟随；`set {yaw,pitch,distanceCm}` 部分姿态（持久）；`unfollow` 解除 |
| 截一张图做证据 | `ludots.screenshot` | `{name?}`；PNG 落 `artifacts/agent-bridge/shots/`；帧末履行，pause 时也能截 |
| 录一段过程 | `ludots.recording.start` / `.stop` | PNG 序列 + manifest.json 落 `artifacts/agent-bridge/recordings/<时间戳>/`，agent 可抽帧阅读 |
| 看引擎日志 | `ludots.logs.tail` | `count/minLevel/channel/contains` 过滤；**只捕获桥激活之后的日志**；`totalWritten/capacity` 看旋转 |
| 触发一个游戏事件 | `ludots.events.fire` | `{event:"GameStart"}` 等；与引擎生命周期同分发路径；返回 `triggerErrors` 计数——**配 logs.tail 组成"发事件→看反应"闭环** |
| 看订单管线吞吐 | `ludots.orders.inspect` | 准入/终态缓冲、单实体 OrderBuffer；响应附 `orderTypes` 合法键清单（id/key/label） |
| 看 GAS 诊断事件 | `ludots.gas.diagnostics` | 当帧 system/metric/count 转储 |

## 实测会话摘录（agent-demo · 可直接复制的形状）

```jsonc
// 找实体 → 拿到 entityId
{"method":"ludots.entities.query","params":{"limit":20}}
// → {"entities":[{"entityId":9,"name":"Ezreal Alpha","worldCm":{"x":1180,"y":720},"onScreen":false},...],"totalMatched":16}

// 用键名下单（字符串键）
{"method":"ludots.orders.issue","params":{"entityId":9,"orderType":"castAbility","targetEntityId":9}}
// → {"entityId":9,"orderTypeId":100,"result":"Queued","accepted":true}

// 语义按键：按下并释放
{"method":"ludots.input.inject","params":{"actionId":"SkillQ","mode":"press"}}   // → {"injected":true}
{"method":"ludots.input.inject","params":{"actionId":"SkillQ","mode":"release"}}

// 镜头跟随 → 摘证据 → 看日志
{"method":"ludots.camera.control","params":{"action":"follow","entityId":9}}    // → 响应含 followingEntityId:9
{"method":"ludots.screenshot","params":{"name":"after-fix.png"}}                // → {"path":"…shots/after-fix.png","bytes":82000}
{"method":"ludots.logs.tail","params":{"count":5,"minLevel":"Info"}}            // → entries 按时间正序
```

## 踩坑清单（全部实测踩过）

1. **`orderType` 键源**：合法键在 `mods/LudotsCoreMod/assets/GAS/order_types.json`（`castAbility`/`moveTo`/`attackTarget`/`stop`…）；`ludots.orders.inspect` 响应的 `orderTypes` 字段也带键清单（id/key/label），`orders.issue` 键名报错时会指向这两个来源。
2. **CSS 选择器按属性匹配**：`.button` 匹配的是 `class="button"`，本仓多数按钮只有 tag——用 `selector:"button"` 或 `#elementId`。
3. **`ui.click` 的 `handled:false` 不是故障**：命中了容器节点但无点击处理器；用 `ui.query` 拿真实按钮的 `elementId` 再点。
4. **`input.inject` 的 press 是"按住"语义**：press 之后必须 release，否则按键悬挂。
5. **`logs.tail` 只有激活后的日志**：想看启动期日志要靠 `game.log` 文件重定向，环里没有。
6. **`pumpCount` 停涨** = 主循环停了（真死或被 pause 卡住），先 `/health` 再谈别的。
7. **截图/录像路径**固定在仓库根 `artifacts/agent-bridge/`（从 AppBase 向上找 `global.json` 定位仓库根）。

## MCP 接入（把桥挂进你的 agent 客户端）

```bash
# 零依赖 stdio 适配器；桥地址解析：argv > LUDOTS_AGENT_BRIDGE_URL > 发现文件 > 47921
dotnet build src/Tools/Ludots.AgentBridge.Mcp/Ludots.AgentBridge.Mcp.csproj -c Release
dotnet exec src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net8.0/Ludots.AgentBridge.Mcp.dll http://127.0.0.1:47921
```

MCP 客户端配置片段（Claude Code / pi 等 stdio server 通用）：

```json
{
  "mcpServers": {
    "ludots": {
      "command": "dotnet",
      "args": ["exec", "<仓库>/src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net8.0/Ludots.AgentBridge.Mcp.dll"]
    }
  }
}
```

不配 MCP 也完全够用：`POST /rpc` 的 method 就是工具名，任何能发 HTTP 的 agent 都能驱动。

## 错误协议

| code | 含义 | 典型场景 |
|------|------|----------|
| `-32601` | 未知工具 | method 拼错 |
| `-32602` | 参数错 | 缺必填/键名拼错/`orderType` 键不存在；`data.code=invalid.params` |
| `-32000` | 域错误 | `entity.not_found`、`ui.node_not_found`、`bridge.timeout`、`capability.unavailable`（宿主未实现端口） |

错误信息自带下一步指引；改参数前先读 `data.code` 与 message。
