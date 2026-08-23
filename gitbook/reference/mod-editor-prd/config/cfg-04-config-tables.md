# cfg-04 配置说明 · 配置表体系

> 配置写法与行为。第一性需求见 [cfg-04 PRD](../prd/cfg-04-config-tables.md)；编辑器需求见 [UXD](../uxd/cfg-04-config-tables.md)；现状见 [reference](../reference/cfg-04-config-tables.md)。

## 1. 示例配置

一张表由"目录登记 + 表文件"构成。

登记（目录里一行）：

```json
{ "Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id" }
```

表文件（mod 内 `{mod名}:assets/GAS/effects.json`，与登记的 Path 拼接；以下为教学骨架，非仓库文件）：

```json
[ { "id": "Effect.MyMod.Poison", "presetType": "DoT", "lifetime": "After",
    "participatesInResponse": false,
    "duration": { "durationTicks": 60, "periodTicks": 10, "clockId": "FixedFrame" } } ]
```

分片形态（登记 `"ShardDirectories": ["GAS/abilities"]` 后，主文件同根的 `GAS/abilities/` 目录下一条一个文件；教学骨架）：

```json
[ { "id": "Ability.MyMod.EmberBolt", "exec": { "clockId": "FixedFrame", "items": [ ] } } ]
```

### JSON 书写通则

- UTF-8；不允许注释与尾逗号。
- 内容表条目的字段名小驼峰且**大小写敏感**；目录登记条目本身是 PascalCase 白名单（`Path`/`Policy`/…）。**未知字段即错**；枚举值**精确匹配**；语义字符串禁首尾空白；布尔写规范 `true`/`false`。

## 2. 字段与行为

| 字段 | 类型 | 必填 | 这样配会产生什么效果 |
|---|---|---|---|
| `Path` | string | 是 | 表的相对路径；加载器以此查询 |
| `Policy` | string | 是 | 合并策略五选一：`Replace` / `DeepObject` / `ArrayReplace` / `ArrayAppend` / `ArrayById`（主力） |
| `IdField` | string | 否 | 条目去重字段，默认 `id`；函数库两表用 `name` |
| `ArrayAppendFields` | string[] | 否 | 条目内按追加合并的数组字段；当前无使用 |
| `ShardDirectories` | string[] | 否 | 分片目录：每来源先主文件后分片、稳定顺序汇入同一合并 |
| `AllowEmpty` | bool | 否 | 允许整表零片段（有意留空的扩展点）；不声明时零片段即失败 |

## 3. 文件结构

目录正本：`Core:config_catalog.json`（磁盘上即仓库 assets/ 根），自身跨 mod 合并，mod 可追加条目（治理审批）。表文件：引擎默认在 assets/ 根的各域目录；mod 内唯一位置 `{mod名}:assets/{Path}`；分片目录与主文件同根。

## 4. 加载原理

五步链（启动期一次）：**登记 → 收集（含分片）→ 合并 → 编译（加载器校验、解析引用）→ 注册（名字换 id）**。编译在扩展枢纽冻结之后（扩展枢纽见 cfg-08）——代码注册的扩展键可供配置引用。当前启用条目数与按域计数见 [事实与取值表](../facts.md)（脚本生成，随目录演进），逐表字段见对应卷。

## 5. 新增一张表与加载器

先过治理门禁（多数需求用已有表组合即可表达）。确认后四步：目录登记 → 写加载器（校验/引用/注册，fail-closed、错误带条目定位）→ 挂进加载链（排在被引用表之后）→ 验收（未登记失败、未知字段失败、空表合法）。加载器属引擎侧；图节点等扩展走 cfg-08 注册面，不走此路。

## 6. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 条目缺 `Path`/`Policy`、策略拼错、未知字段 | 启动失败 |
| 加载器查询未登记路径 | 启动失败（没登记的 JSON 不是配置） |
| 表文件语法错、未知字段、枚举拼错 | 启动失败，指出文件与条目 |
| 条目缺 id / 引用未注册 id | 启动失败，指明引用方与目标 |

## 7. 实例

- 目录正本：`Core:config_catalog.json`（assets/ 根）
- 同表两来源对照：`Core:GAS/graphs.json` 与 `MobaDemoMod:assets/GAS/graphs.json`

**相关文档**：[cfg-04 PRD](../prd/cfg-04-config-tables.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md) · [cfg-06 配置说明](cfg-06-game-config.md)
