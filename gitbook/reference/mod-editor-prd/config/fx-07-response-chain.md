# fx-07 配置说明 · 响应链

> 配置写法与行为。第一性需求见 [fx-07 PRD](../prd/fx-07-response-chain.md)；编辑器需求见 [UXD](../uxd/fx-07-response-chain.md)；现状见 [reference](../reference/fx-07-response-chain.md)。

## 1. 示例配置

模板侧只有一个开关（champion 演示 mod，真实）：

```json
[
  { "id": "Effect.Champion.Garen.Judgment", "tags": ["Effect.Champion.Damage"],
    "presetType": "Search", "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery": { "kind": "BuiltinSpatial", "shape": "Circle", "radius": 260 } }
]
```

回应本体装载在目标实体的响应链监听组件上（容量与字段合同见下表与 reference；仓库暂无直接 JSON 写入样例，属扩展面装载）。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `participatesInResponse: true` | 该效果提案开响应窗口；false 一律不收回应 |
| 回应类型 Hook | 对匹配事件置取消——提案被否决 |
| 回应类型 Modify | 按操作码改写窗口数值（修正表生效于提案值） |
| 回应类型 Chain | 派生新提案（数量与深度上限见事实页） |
| 回应类型 PromptInput | 窗口转入等待交互，交互或订单请求原子发布 |
| `eventTagId` | 匹配事件；0 为通配 |
| `priority` | 大者优先；同窗裁决顺序由此决定 |

动态图路径为约定能力（回应携带图 id、约定寄存器槽位），现状未接线、只生效静态值——编辑器应标注"未生效"（todo/effect.md E5）。

## 3. 文件结构

模板开关在效果表（fx-02）；回应本体属实体运行时组件，由实体模板或代码扩展面装载，无独立 JSON 表。

## 4. 运行时加载效果

加载期无额外动作；运行期窗口按状态机推进：开窗（根提案+验证通过+声明参与）→收集→（如需）等输入→从尾向前裁决，通过者进入计算相位后内联或实体化。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 响应入队超容量 / 连锁超深 | 报错，指明根效果 |
| 步数超上限 | 熔断清空本窗队列 |
| 根预算超限 | 报扇出预算错误（上限见事实页） |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（多条 Search/伤害模板 `participatesInResponse: true`）

**相关文档**：[fx-07 PRD](../prd/fx-07-response-chain.md) · [fx-06 配置说明](fx-06-proposal-window.md) · [rt-02](rt-02-budgets.md)
