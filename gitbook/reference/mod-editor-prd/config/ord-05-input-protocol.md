# ord-05 配置说明 · 输入协议

> 配置写法与行为。第一性需求见 [ord-05 PRD](../prd/ord-05-input-protocol.md)；编辑器需求见 [UXD](../uxd/ord-05-input-protocol.md)；现状见 [reference](../reference/ord-05-input-protocol.md)。

## 1. 示例配置

没有单独的配置文件，门写在能力执行时间轴里（真实例，`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`）：

```json
{ "id": "Ability.Champion.SpellEngineer.CataclysmRing",
  "exec": { "items": [
    { "kind": "TagClip", "tick": 0, "duration": 90, "tag": "Cooldown.Champion.SpellEngineer.E" },
    { "kind": "EffectSignal", "tick": 0, "template": "Effect.Champion.SpellEngineer.CataclysmRing" },
    { "kind": "EventGate", "tick": 0, "payloadA": 180 },
    { "kind": "End", "tick": 0 } ] } }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `kind: "EventGate"` | 走到这一步就停下，等事件标记出现或超时 |
| `tag`（EventGate） | 要等的事件标记 |
| `payloadA`（EventGate） | 超时 tick 数；0 表示一直等 |
| `tick` | 门在时间轴上的开启点 |

玩家选目标、确认这类输入不写在时间轴里，写在交互上下文的图里（见 input-03），图算完再下施法命令，目标随命令进来。

## 3. 文件结构

门是能力表 `GAS/abilities.json`（分片目录 `GAS/abilities/`）exec 时间轴的一步（能力卷见 ab-02）。

## 4. 运行时加载效果

加载时逐步检查步骤类型，不认识的类型直接报错；运行期走到事件门进入等待，放行后继续后面的步骤。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 写了 `InputGate` / `TargetCollectionGate` 或其他未知类型 | 启动失败，指明能力与步骤序号 |
| 等待期间一直没有事件、也没配超时 | 一直等，直到订单被打断或替换 |

## 6. 实例

- 事件门真实例：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`

**相关文档**：[ord-05 PRD](../prd/ord-05-input-protocol.md) · [input-03 配置说明](input-03-interaction-context.md)
