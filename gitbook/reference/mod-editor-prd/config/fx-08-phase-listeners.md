# fx-11 配置说明 · 相位监听器

> 配置写法与行为。第一性需求见 [fx-10 PRD](../prd/fx-08-phase-listeners.md)；编辑器需求见 [UXD](../uxd/fx-08-phase-listeners.md)；现状见 [reference](../reference/fx-08-phase-listeners.md)。

## 1. 示例配置

champion 演示 mod 的连招标记（真实）：W 命中挂标记，任意技能命中时消耗标记——

```json
[
  { "id": "Effect.Champion.Ezreal.EssenceFluxHit", "tags": ["Effect.Champion.Buff"],
    "presetType": "Buff", "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 240, "periodTicks": 0, "clockId": "FixedFrame" },
    "phaseListeners": [
      { "phase": "OnApply", "scope": "Target", "action": "Graph",
        "listenEffectId": "Effect.Champion.Ezreal.MysticShotHit",
        "graphProgram": "Graph.Champion.Ezreal.PopEssenceFlux", "priority": 100 } ] }
]
```

（原条目还有两条同形监听，分别听 ArcaneShiftBoltHit 与 TrueshotBarrageHit。）

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `phase` | 监听的相位（八相位之一）；监听器在该相位三槽后执行 |
| `scope: Target / Source` | 视角：登记在目标实体或施法者实体的监听缓冲 |
| `action: Graph / Event / Both` | 执行图、发布事件或双动作 |
| `listenEffectId` | 听指定效果 id；缺省为通配（标签监听通道语义相同，运行时字段见 reference） |
| `graphProgram` | action 含 Graph 时必填；监听图有纯度限制（fx-08） |
| 事件标签声明 | action 含 Event 时必填；纯相位禁用 |
| `priority` | 同相位多监听器的执行序，大者优先 |
| 块容量 | 每模板监听器数上限见事实页 |

## 3. 文件结构

`phaseListeners` 是效果模板顶层组件块（fx-04）；引用的图在 `GAS/graphs.json`。即时寿命模板禁带本块。

## 4. 运行时加载效果

loader 与执行计划双重校验契约（动作组合、id 对应、纯相位限制）；应用时以宿主效果实体 id 注册并延迟回放；宿主过期或移除时按宿主清理，缓冲压缩不留洞。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| action 非三种合法组合 / 声明与动作不对应 | 启动失败 |
| 纯相位监听器带发布事件 | 启动失败 |
| Instant 模板携带 phaseListeners | 启动失败；运行期再遇持久监听需求同样抛错 |
| 收集超容量 | 运行期报错（不丢弃） |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（EssenceFluxHit 三连监听；moba_demo 亦有用法）

**相关文档**：[fx-10 PRD](../prd/fx-08-phase-listeners.md) · [fx-07 配置说明](fx-05-phases.md)
