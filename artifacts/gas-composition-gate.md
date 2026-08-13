## GAS Composition Gate — Self Review

- **Task / Issue**: Epic #915 / PR #919 — GraphNodeOp 画廊必须可读、可见、可玩；每个 covered op 真实跑通并留下截图/录屏
- **Date**: 2026-08-13
- **Agent / Author**: Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** — 既有 graph 节点连线 + Showcase 舞台/字幕，不是新 enum/preset 开关

结论: **PASS**

一句话理由: 补的是作者图、符号补丁、玩家字幕和真实结算；不新增 Core opcode / profile DSL。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 既有 GraphNodeOp 执行 | 0 | GasGraphOpHandlerTable |
| 生命周期事务演示 | 1 | BeginLifecycleTransaction + InvokeBuiltin |
| 各家族剧本连线 | 2 | 各 GraphOps*Mod graphs.json / FrontDoor JSON |
| 舞台与字幕 | 2 | GraphShowcaseStagePresenter + ScreenOverlayBuffer |

### 3. Reuse list

- Handlers: 既有 GraphNodeOp，禁止新 opcode
- Queues / Systems: 各 Mod Simulation/Presentation；DebugDrawCommandBuffer；ScreenOverlayBuffer
- Resolvers / Registries: GraphProgramSymbolPatcher、GraphProgramAuthoringFrontDoor、GasGraphRuntimeApi、launcher screenshot env
- Existing presets / graphs: 八个 GraphOps 画廊 + Ability Graph Sandbox

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

仅黑板家族演示 `BeginLifecycleTransaction`；其余效果图单帧 halt，失败关闭。

### 6. Config SSOT

行为配置落在: 各 Mod `assets/GAS/graphs.json` / FrontDoor JSON + `assets/Configs/GAS/graph_node_op_coverage.registry.json`

是否新增 JSON schema: **NO**

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**

### 9. 可玩画廊合同（全家族共用）

每个 `showcaseId` 必须同时满足：

1. **可读**：`Metrics.Detail` 与屏幕字幕是玩家中文场景句；禁止 opcode 名、`FuncLib`/`Validation`/`tally`/`True`/`False`/`耗时…ms` 作为主文案。
2. **可见**：舞台上能看出角色/血条/圈人/水位等变化；字幕走 `GraphShowcaseStagePresenter.DrawPlayerCaption(ScreenOverlayBuffer, title, detail)`。
3. **可玩**：有 launcher binding + raylib preset；新玩家能启动看完整一局，不是只编译图。
4. **真实**：每个 covered op 必须出现在该 Showcase 的图里并被执行；禁止 C# 演戏填 Detail。
5. **证据**：`artifacts/evidence/<showcaseId>/` 含截图序列 + `play.mp4`；registry `screenshot` 指向一张能看懂剧情的 PNG。

禁止静默失败：图编译失败、符号未补丁、吸附永远失败、读配置全 0 → 测试必须红。

### 10. Attr / Float 家族

Attr 把比较/选择接入轻击/全力；Float 把出手许可与负面修正翻正写进伤害句。属性名走 `attribute_constraints.json` 合并，启动器才能打图。

### 11. Blackboard / Event 家族

空洞演示来自未 Patch 的符号下标、目标名单为空、吸附起点过远与英文 True/False。黑板 FrontDoor 后 `GraphProgramSymbolPatcher.Patch`；事件用既有 tag_rules 登记 `Event.DamageDealt`，扇出写目标名单，吸附改到够得着的落点。

### 12. Query / Rel 家族

Query 从编译-only 改为实跑沙盘：全图筛人排出最强最弱+花名册模板。
Rel 查好友链/Trusted/好感区间/拆最弱并标记失和。FuncLib 放 Gallery 目录，禁止并进引擎 GAS/func_lib.json。
