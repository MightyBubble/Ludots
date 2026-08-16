# ord-05 reference · 输入协议

> 现状参考。第一性需求见 [ord-05 PRD](../prd/ord-05-input-protocol.md)；配置说明见 [ord-05 配置说明](../config/ord-05-input-protocol.md)。

## 1. 现状快照

- 协议结构：`InputRequest{RequestId, RequestTagId, Source, Target, Context, PayloadA, PayloadB}` 与 `InputResponse{RequestId, ResponseTagId, Source, Target, TargetContext, PayloadA, PayloadB}`。
- 队列：请求队列 RingBuffer 默认 1024、下限 16；响应缓冲 SwapRemove、`TryConsume` 按请求号配对。
- 三种门位：InputGate=20、EventGate=21、TargetCollectionGate=22。
- 门流程（AbilityExecSystem）：进门构造请求（请求号 = payloadA≠0 ? payloadA : OrderId）入队置 GateWaiting；EventGate 设等待标记 + 可选截止；处理期按等待号取响应，命中且 `resp.Target` 存活 → 回填 `inst.Target` 与 TargetContext；EventGate 超时放行或事件命中放行。
- 加载器要求 InputGate 显式 `payloadA`；响应生产者为 `GasInputResponseSystem`（`Confirm.PressedThisFrame` 回填）。
- 仓库尚无 InputGate 的 JSON 用例；EventGate 真实例见 champion_skill_sandbox。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 请求/响应结构 | src/Core/Gameplay/GAS/Input/InputProtocol.cs:15-24,26-35 |
| 队列容量 / 配对消费 | src/Core/Gameplay/GAS/Input/InputQueues.cs:113-116,92-105,118-121 |
| 三门位常量 | src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs:36-41 |
| 进门构造（两种请求号来源） | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:1308,1339 |
| EventGate 等待/截止 | AbilityExecSystem.cs:1368-1378 |
| 处理门（回填/超时） | AbilityExecSystem.cs:1397-1453,1455-1490 |
| InputGate payloadA 校验 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:407-411 |
| 响应生产者 | src/Core/Input/Interaction/GasInputResponseSystem.cs |
| EventGate 真实例 | mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json |

**相关文档**：[ord-05 PRD](../prd/ord-05-input-protocol.md) · [ord-06 reference](ord-06-input-mappings.md)
