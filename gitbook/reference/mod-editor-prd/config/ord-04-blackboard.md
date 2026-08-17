# ord-04 配置说明 · 订单黑板

> 配置写法与行为。第一性需求见 [ord-04 PRD](../prd/ord-04-blackboard.md)；编辑器需求见 [UXD](../uxd/ord-04-blackboard.md)；现状见 [reference](../reference/ord-04-blackboard.md)。

## 1. 示例配置

键声明（核心 mod 真实，`order_types.json` 键段）：

```json
{ "orderBlackboardKeys": { "Attack.MovePosition": true, "Attack.TargetEntity": true } }
```

存储目标五键组（教学骨架——瞬时完成类型专用）：

```json
"persistentStoredTarget": {
  "targetKindKey":    "Stash.Kind",
  "targetPositionKey": "Stash.Point",
  "targetEntityKey":   "Stash.Entity",
  "hexQKey":           "Stash.Q",
  "hexRKey":           "Stash.R"
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| 键段条目 `"My.Key": true` | 注册自定义键（编号从 10000 起）；值必须 `true` |
| 内置键 | 施法五键（槽位/目标实体/目标点/参数/朝向）与通用四键（目标实体/目标点/整参/浮参）已固定，禁重声明 |
| `targetKindKey` | 存储目标形态判别键（实体/点/六角格三态） |
| `targetPositionKey` | 点形态落点（空间缓冲） |
| `targetEntityKey` | 实体形态落点（实体缓冲） |
| `hexQKey` / `hexRKey` | 六角格坐标（整数缓冲）；缺世界点时按六角坐标推导 |

- 五键组五项全部必填，且仅 `instantComplete=true` 的类型可配（见 ord-01）。
- 四种缓冲容量为引擎常量（见 reference）；空间键一 key 承载点序列。

## 3. 文件结构

键与五键组都声明在 `GAS/order_types.json`（键在根键段、五键组在各类型内；合并路径见 ord-01）；黑板无独立配置文件。

## 4. 运行时加载效果

键段在类型注册前加载入键注册表；五键组随类型解析为键 id；运行期订单激活/瞬时完成系统按 id 读写四种缓冲。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 键段值非 `true` / 重声明内置键 | 启动失败 |
| 五键组引用未注册键或残缺 | 启动失败，指明类型与键名 |
| 实体与空间目标并存写入 | 提交失败（目标歧义） |
| 缓冲容量耗尽 | 写入被拒（容量守卫） |

## 6. 实例

- 自定义键真实例：`mods/LudotsCoreMod/assets/GAS/order_types.json` 键段（`Attack.MovePosition`/`Attack.TargetEntity`）

**相关文档**：[ord-04 PRD](../prd/ord-04-blackboard.md) · [ord-01 配置说明](ord-01-types.md)
