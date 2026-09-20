# infra-04 配置说明 · 界面档案

> 配置写法与行为。第一性需求见 [infra-04 PRD](../prd/infra-04-ui-profiles.md)；编辑器需求见 [UXD](../uxd/infra-04-ui-profiles.md)；现状见 [reference](../reference/infra-04-ui-profiles.md)。

## 1. 示例配置

引擎真实聚合档案（`assets/UI/ability_aggregation_profiles.json` 全量）与两张空根表：

```json
[
  { "id": "aggregation.by_template", "groupBy": "template.id" },
  { "id": "aggregation.by_ability_id", "groupBy": "ability.id" }
]
```

```json
{ "profiles": [] }
```

命令甲板档案（教学骨架，合成；字段合同见 loader，全仓库无真实行）：

```json
{
  "profiles": [
    { "id": "deck.rts.bottom", "slots": [ { "abilityId": "Ability.Rally", "hotkey": "H" } ] }
  ]
}
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| ability_aggregation_profiles | `id` | 聚合档案名 |
| ability_aggregation_profiles | `groupBy` | 内建表达式：`template.id`（同模板归一组）/ `ability.id`（逐技能一组）；安装期编译，未知即抛 |
| command_deck_profiles | `profiles[]` | 命令甲板布局条目；现状空表 |
| production_overview_profiles | `profiles[]` | 生产总览布局条目；现状空表 |

## 3. 文件结构

`assets/UI/` 三件：`ability_aggregation_profiles.json`（ArrayById）、`command_deck_profiles.json`（DeepObject，根键 profiles）、`production_overview_profiles.json`（同构）。**注意**：两张 profiles 根表为空占位（D3），真实内容一律写在 mod 的同名文件里深合并。

## 4. 运行时加载效果

聚合档案加载后结构校验，groupBy 前缀解析推迟到注册表安装期；面板档案加载后供对应 UI 系统消费。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| groupBy 表达式未知 | 安装期抛错，指明档案 |
| 档案结构非法（字段缺失/类型错） | 加载失败 |
| 档案缺失 | 非错误：面板回退内建布局 |

## 6. 实例

- `assets/UI/ability_aggregation_profiles.json`（两条内建聚合）
- `assets/UI/command_deck_profiles.json`、`production_overview_profiles.json`（空根表占位）

**相关文档**：[infra-04 PRD](../prd/infra-04-ui-profiles.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
