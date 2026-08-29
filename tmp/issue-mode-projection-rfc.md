# RFC/设计：交互模式为实体状态，input context 为本机投影（输入侧状态分层修正）

## 判据（第一性原理）

状态放哪只看一件事：**谁需要读它**。

- 模拟要读的状态（AI 决策、UI 渲染、回放重现、存档恢复、其他实体反应）→ entity
- 只有本机 I/O 要读的状态（设备绑定、键位档案、seat 表）→ 本机服务，永不进存档/网络

这与禁则④是同一条原理的两半：模拟状态上 entity，I/O 状态留本机。

## 现状三个放错层的状态

| 状态 | 谁要读 | 应住哪 | 现状 |
|---|---|---|---|
| 操作模式（瞄准中/驾驶投石车/菜单中） | 模拟侧全员 | entity 组件 | 藏在 `PlayerInputHandler` context 栈 + `InteractionContextStack` 服务（引擎单例，GameEngine.cs:1155），模拟不可见 |
| 用哪张键位表 | 只有本机解释器 | 本机 | scheme，基本正确 |
| 下单偏好（智能施法、默认 intent） | 订单派发在模拟里跑 | entity（CommandPref） | 挂在 scheme 上——本机 I/O 档案携带玩法数据 |

scheme 一个概念横跨 I/O 层与玩法层，是「scheme 与 context 关系绕晕」的最大单一来源；context 栈是多来源命令式写入的缓存（scheme 切换 push/pop、交互桥接 push/pop、无 graph op），没有单一真相可查。

## 目标模型

```
模拟层（全 entity、graph 可写、存档/回放/网络天然携带）
  InteractionMode 组件（挂 representative / 被交互实体）
  CommandPref · Control Plane（已是 query graph）
        ▼ 投影层（一个 system，纯派生，明文映射表 mode → contexts + priority）
  (seat × 实体模式 × UI 状态) → 各 seat 应激活的 context 集合，每 tick diff 派生 push/pop
        ▼ 本机层（不进存档/网络）
  device→seat 绑定 · per-seat handler · scheme（收窄为纯键位档案）· per-seat 权威快照
```

核心一句：**操作模式是模拟状态，input context 是它在每台机器上的本机投影。** 模式切换的唯一写入口是 trigger graph；投影是唯一把模式翻译成 context 的地方，代码不得绕过投影直接 push/pop。context 栈从「真相」降级为「缓存」。

## 三条产品原则的映射

1. **一切明文 + graph**：模式切换 = graph op 写组件（可视化、可断点、可回放）；mode→context 映射表是明文配置；调试看两样——graph 里的模式切换边、投影 system 的 diff 日志。
2. **模式随时切换**：4X 施法 → effect graph 写 `mode.targeting` → 投影换 context；全战上投石车 → possession 变化（graph）→ 控制平面重算 → 被控实体 profile 决定 `mode.siege` → 投影换表；退出/打断由触发器回收。全程没有代码偷偷换 context。
3. **状态在 entity**：模式、偏好、控制关系全在实体上；键位、设备、seat 留本机。

远端推论：换机 = 只改 possession/PresentBinding，模式挂在实体上不搬家（咬合 #902「换机不搬家」UAT）；旁观 = 盯他人 LogicView + 只读投影——#1119 两个预留场景从「没想好」变成模型的自然推论。

## 与现状的冲突（逐项盘点）

1. **InteractionContextStack / bridge**：bridge 已是投影的正确雏形（把交互帧翻译成 handler context），错在源头——栈是服务不是实体。演进：帧语义升级为模式组件，栈退役，bridge 变投影输入源。
2. **scheme 的 defaults**（commandIntent / castDispatchProfile）归宿是模式定义 / CommandPref（玩法侧），scheme 收窄为纯键位档案。动 RFC-0065 DEC-14/15 定案，需正式裁决（路线第③步），不得顺手改。
3. **seat.controlSchemeId 入档**（#1118 接受现状 + 显式例外）：新模型提供更清晰的重评依据，在「首个面向玩家发布前复审」既定点重开。
4. **#1058 在途切片不受影响**：per-seat 输入通道、设备扇出在本模型里依然是必须的本机基建——投影本来就是 per-seat 的。

## 落地路线（四步增量，不推倒）

- [ ] ① `InteractionMode` entity 组件 + 明文映射表（mode → contexts + priority，未知名 fail-fast）+ `InputContextProjectionSystem`（输出 `(seatId, contextId, op)` 命令流接现有 Push/PopContext；**不动 bridge**，行为兼容过渡）+ 测试
- [ ] ② `SetInteractionMode` 类 graph op（照既有「graph 写实体组件」op 先例，禁止新造平行 op 体系）
- [ ] ③ scheme 职责收窄 RFC：DEC-14/15 修订，defaults 迁移归宿（模式定义 / CommandPref）
- [ ] ④ `InteractionContextStack` 退役：交互帧消费方全部迁到模式组件 + 投影，bridge 退役

## UAT（节选）

```gherkin
场景: graph 施法进入瞄准
  假如 representative 处于 mode.normal
  当 effect graph 写 mode.targeting
  那么 投影在下一 tick 激活 targeting 对应 context 集合
  并且 存档/回放携带该模式，恢复后投影结果一致

场景: 模式随时切换不残留
  假如 representative 处于 mode.targeting 且对应 context 已激活
  当 触发器写回 mode.normal
  那么 投影弹掉 targeting context，normal 的 context 集合恢复

场景: 换机不搬家
  假如 甲的代表实体持有 mode.targeting
  当 possession 转到另一台机器
  那么 模式组件仍在甲的代表实体上，新机器重新投影
```

## 边界

- 本单是输入侧分层修正的设计与路线 SSOT；实现按步骤拆 PR 挂本单
- 不推翻 #1058 在途切片；不动 #1118 裁决（复审走既定点）
- 不新建 Machine/Client/Device/UISurface 类型（术语禁则③）
