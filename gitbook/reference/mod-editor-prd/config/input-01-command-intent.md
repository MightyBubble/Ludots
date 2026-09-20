# input-01 配置说明 · 命令意图档案

> 配置写法与行为。第一性需求见 [input-01 PRD](../prd/input-01-command-intent.md)；编辑器需求见 [UXD](../uxd/input-01-command-intent.md)；现状见 [reference](../reference/input-01-command-intent.md)。

## 1. 示例配置

引擎根资产真实档案（`assets/Input/command_intent_profiles.json` 全量）：

```json
{ "profiles": [
  { "id": "intent.command.default",
    "groupPolicy": { "kind": "independent" },
    "rules": [
      { "priority": 20, "target": { "hasEntity": true },  "route": { "orderTypeKey": "moveTo" } },
      { "priority": 10, "target": { "hasEntity": false }, "route": { "orderTypeKey": "moveTo" } } ] } ] }
```

教学骨架（演员条件 + 槽位路由）：

```json
{ "id": "intent.command.combat",
  "groupPolicy": { "kind": "independent" },
  "rules": [
    { "priority": 30, "actor": { "hasAbilityWithCategory": "Ability.Attack" },
      "target": { "stance": "Aggressive" },
      "route": { "orderTypeKey": "attackTarget" } },
    { "priority": 20,
      "actor": { "hasAbilityWithCategory": "Ability.Train" },
      "route": { "slot": "contextGroup:group.production" } } ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 档案标识；控制方案默认与交互帧按它引用 |
| `groupPolicy.kind` | 编组策略；内置仅 `independent`（逐演员独立），新 kind 由 mod 代码注册 |
| `rules[].priority` | 数值大者先裁决，命中即止 |
| `actor` | 演员侧条件：`hasAbilityWithCategory` / `allTags` / `anyTags` |
| `target` | 目标侧条件：`allTags` / `anyTags` / `stance` / `hasEntity`（true/false/unset 三态） |
| `route.orderTypeKey` | 路由终点一：直接落订单类型 |
| `route.slot` | 路由终点二：`byAbilityCategory:<category>` 或 `contextGroup:<id>` 取技能槽 |

## 3. 文件结构

`assets/Input/command_intent_profiles.json`（引擎根资产持有默认档案；mod 可同 id 深合并扩充规则）。

## 4. 运行时加载效果

装配期加载并校验规则引用（订单类型、上下文组）；运行期每帧由仲裁器按"交互帧显式 > 控制方案默认 > 0 不路由"解析生效意图，逐演员过规则表。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未注册订单类型 / 未知槽位来源格式 | 启动失败 |
| `groupPolicy.kind` 非 independent | 启动失败 |
| 生效意图为 0（无显式无默认） | 本帧命令不路由（静默） |

## 6. 实例

- 根默认档案：`assets/Input/command_intent_profiles.json`（intent.command.default）

**相关文档**：[input-01 PRD](../prd/input-01-command-intent.md) · [ord-06 配置说明](ord-06-input-mappings.md) · [input-05 配置说明](input-05-filters-and-schemes.md)
