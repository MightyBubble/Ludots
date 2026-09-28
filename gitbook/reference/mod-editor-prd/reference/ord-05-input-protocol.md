# ord-05 reference · 输入协议

> 现状参考。第一性需求见 [ord-05 PRD](../prd/ord-05-input-protocol.md)；配置说明见 [ord-05 配置说明](../config/ord-05-input-protocol.md)。

## 1. 现状快照

- 时间轴只有一种门：`EventGate=21`。`InputGate=20`、`TargetCollectionGate=22` 已删除，编号不复用。
- 门流程（AbilityExecSystem）：进门记下等待标记（item 的 `tag`）和可选截止（`payloadA` 个 tick 后）；每帧扫本帧事件，标记命中或到截止即放行。
- 加载器遇到未知步骤类型抛错，报错带能力 id、文件路径和 `exec.items[序号].kind`。
- `InputRequest` 与 `InputRequestQueue` 还在，只给响应链的 PromptInput 用，和技能时间轴无关。
- EventGate 真实例见 champion_skill_sandbox。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 门位常量 | src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs |
| 进门 / 处理门 | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs（`EnterGate`、`ProcessGate`） |
| 步骤类型解析 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs（`ParseItemKind`） |
| 响应链输入请求 | src/Core/Gameplay/GAS/Input/InputProtocol.cs、InputQueues.cs |
| EventGate 真实例 | mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json |

**相关文档**：[ord-05 PRD](../prd/ord-05-input-protocol.md) · [ord-06 reference](ord-06-input-mappings.md)
