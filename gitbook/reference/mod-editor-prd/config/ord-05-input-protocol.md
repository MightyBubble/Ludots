# ord-05 配置说明 · 输入协议

> 配置写法与行为。第一性需求见 [ord-05 PRD](../prd/ord-05-input-protocol.md)；编辑器需求见 [UXD](../uxd/ord-05-input-protocol.md)；现状见 [reference](../reference/ord-05-input-protocol.md)。

## 1. 示例配置

协议无独立配置文件，门声明在能力执行时间轴（真实例，`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`）：

```json
{ "id": "Ability.Champion.SpellEngineer.CataclysmRing",
  "exec": { "items": [
    { "kind": "TagClip", "tick": 0, "duration": 90, "tag": "Cooldown.Champion.SpellEngineer.E" },
    { "kind": "EffectSignal", "tick": 0, "template": "Effect.Champion.SpellEngineer.CataclysmRing" },
    { "kind": "EventGate", "tick": 0, "payloadA": 180 },
    { "kind": "End", "tick": 0 } ] } }
```

输入门教学骨架（仓库尚无真实用例）：

```json
{ "kind": "InputGate", "tick": 0, "payloadA": 7 }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `kind: "InputGate"` | 等待输入响应；命中且目标存活则回填实例目标与目标上下文 |
| `kind: "EventGate"` | 等待事件标记命中或到期放行 |
| `kind: "TargetCollectionGate"` | 第三种门位，与上述共用等待状态机 |
| `payloadA`（InputGate） | 请求号来源，**必填**；缺省即启动失败 |
| `payloadA`（EventGate） | 等待的事件标记 id |
| `tick` | 门在时间轴上的开启点 |

响应生产者为确认类输入动作（`Confirm` 按下帧回填），由输入系统自动完成。

## 3. 文件结构

门是能力表 `GAS/abilities.json`（分片目录 `GAS/abilities/`）exec 时间轴的 item（能力卷见 ab-02）；请求/响应队列容量为引擎常量（见 reference）。

## 4. 运行时加载效果

能力加载时校验 InputGate 的 `payloadA` 显式存在；运行期进门构造请求（请求号 = payloadA，非零优先于订单号）入队置等待。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| InputGate 缺 `payloadA` | 启动失败，指明能力与 item 序号 |
| 应答目标已消亡 | 不回填，等待继续 |
| 响应请求号与等待号不符 | 忽略该响应，不误配 |

## 6. 实例

- 事件门真实例：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`

**相关文档**：[ord-05 PRD](../prd/ord-05-input-protocol.md) · [ord-06 配置说明](ord-06-input-mappings.md)
