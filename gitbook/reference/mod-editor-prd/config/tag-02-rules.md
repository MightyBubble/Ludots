# tag-02 配置说明 · Tag 规则

> 配置写法与行为。第一性需求见 [tag-02 PRD](../prd/tag-02-rules.md)；编辑器需求见 [UXD](../uxd/tag-02-rules.md)；现状见 [reference](../reference/tag-02-rules.md)。

## 1. 示例配置

仓库真实规则（`GAS/tag_rules.json`）：

```json
[ { "id": "Status.Stunned", "attached": [ "Status.CannotMove" ] } ]
```

教学骨架（六类齐用）：

```json
[ {
  "id": "Status.Stealth",
  "requiredAll": [ "State.CanStealth" ],
  "blockedAny":  [ "Status.Revealed" ],
  "attached":    [ "Status.Evasive" ],
  "removed":     [ "Status.Noise" ],
  "disabledIfAny": [ "Status.Marked" ],
  "removeIfAny":   [ "Event.Dispel" ]
} ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 规则主体 tag 名（首现即注册） |
| `requiredAll` | 添加前必须全部在场，否则拒绝 |
| `blockedAny` | 添加前任一在场即拒绝 |
| `attached` | 添加时连带授予 |
| `removed` | 添加时连带移除 |
| `disabledIfAny` | 任一在场时主体 tag"存在但无效"（有效视角不可见） |
| `removeIfAny` | 任一在场时自动移除主体 |

每类 ≤8 条；级联在事务内执行（步数预算见事实页）。

## 3. 文件结构

`GAS/tag_rules.json`（目录登记的表，数组按 id 深合并；引擎默认根当前为空，规则由各 mod 贡献）。

## 4. 运行时加载效果

启动期规则表加载并编译为掩码快查；运行期添加命中主体即触发事务。热通道：整表替换走工作台（NextCast 安全帧）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 前置缺失 / 互斥命中 | 拒绝添加（带原因） |
| 事务步数超预算 | 失败回滚并报错 |
| 规则引用未注册 tag 名 | 热替换路径拒绝；冷路径首现即注册 |

## 6. 实例

- 真实规则：`mods/showcases/arpg_demo/ArpgDemoMod/assets/GAS/tag_rules.json`
- 事件声明样例：`mods/CombatStanceBehaviorMod/assets/GAS/tag_rules.json`

**相关文档**：[tag-02 PRD](../prd/tag-02-rules.md) · [tag-01 配置说明](tag-01-basics.md) · [cfg-07 配置说明](../config/cfg-07-merge-rules.md)
