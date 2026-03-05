# Config ID 格式规范

本篇定义 Ludots 配置系统中所有 ID 的格式约定，确保 config_catalog、数据文件、ModId、MapId 等处的命名与类型一致，避免混用导致的合并错误与维护困惑。

## 1 硬性约束

*   **IdField 统一**：ArrayById 配置的主键字段名一律为 `id`（camelCase），禁止 `Id` 或其它变体。
*   **ID 值类型统一**：所有 config 的 `id` 字段值必须为 JSON 字符串类型，禁止整数。
*   **唯一链路**：无 fallback、无向后兼容；字段名或类型不匹配时，合并逻辑直接跳过或报错。

## 2 IdField 命名

### 2.1 config_catalog.json

`config_catalog.json` 中所有 ArrayById 条目的 `IdField` 必须为 `"id"`：

```json
{ "Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id" }
```

### 2.2 数据文件

对应 JSON 数据中主键字段必须为 `"id"`（小写）：

```json
{ "id": "Effect.Moba.Damage.Q", "tags": ["Effect.ApplyForce"] }
```

## 3 ID 值格式（按配置类型）

| 配置类型 | 格式 | 示例 |
|---------|------|------|
| ModId (mod.json name) | `[A-Za-z][A-Za-z0-9]*`，不含 `:`, `/` | `LudotsCoreMod` |
| MapId / StartupMapId | 非空字符串，建议 snake_case | `entry`, `ui_test` |
| Entity template id | 非空字符串 | `orc_grunt`, `Ability.Moba.SkillQ` |
| Effect/Ability id | 点分隔，`Category.Mod.Name` | `Effect.Moba.Damage.Q` |
| Performer id | 字符串（原数字改为字符串） | `"9010"`, `"5001"` |
| Camera PresetId | PascalCase 字符串 | `Moba`, `Rts` |

## 4 ModId 约束

ModId 即 `mod.json` 的 `name` 字段，用于 VFS 路径前缀 `ModId:Path`：

*   禁止包含 `:` 或 `/`（与 VFS 解析冲突）。
*   建议字母开头，仅含字母与数字。
*   校验在 `ModManifestJson.ParseStrict` 中执行，非法则抛出异常。

## 5 VFS 路径

*   格式：`ModId:Path/To/Resource`
*   示例：`Core:Configs/game.json`、`MobaDemoMod:assets/Configs/GAS/effects.json`
*   ModId 必须为已挂载 Mod 的 `name`，大小写敏感。

## 6 新增配置检查清单

在新增 ArrayById 配置前，确认：

1.  config_catalog 中 `IdField` 为 `"id"`。
2.  数据文件使用 `"id"` 字段，值为 JSON 字符串。
3.  若为 Mod 扩展，ModId 符合格式约束。
4.  文档与目录已更新（如适用）。

## 7 相关文档

*   [数据配置类与通用合并策略最佳实践](12_config_data_merge_best_practices.md)
*   [Mod 架构与配置系统](02_mod_architecture.md)
*   [ConfigPipeline 合并管线](07_config_pipeline.md)
