# gr-04 配置说明 · 图文档格式

> 配置写法与行为。第一性需求见 [gr-03 PRD](../prd/gr-02-document.md)；编辑器需求见 [UXD](../uxd/gr-02-document.md)；现状见 [reference](../reference/gr-02-document.md)。

## 1. 示例配置

引擎默认真实条目（`assets/GAS/graphs.json`），控制边 + 值边各一条：

```json
{
  "id": "Graph.FuncLib.Demo.ConstSeven", "kind": "Script", "entry": "c",
  "nodes": [ { "id": "c", "op": "ConstInt", "intValue": 7 }, { "id": "h", "op": "HaltReturnInt" } ],
  "controlEdges": [ { "from": "c", "fromPort": "next", "to": "h" } ],
  "valueEdges": [ { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" } ]
}
```

带符号字段与分支端口的真实节点（`Graph.Script.DrinkUntilFull` 摘录）：`{ "id": "branchNeedDrink", "op": "BranchBool" }`，其出边 fromPort 为 `true` / `false`；常量可加 `"pinRegister": 0` 钉寄存器。

## 2. 字段与行为

顶层七字段：`id`/`kind`/`entry`/`nodes`/`controlEdges`/`valueEdges`/`outputs`（仅 Query，gr-09）。边四段：`from`/`fromPort`/`to`/`toPort`（控制边 toPort 缺省）。

节点字段按族：

| 字段族 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| 身份 | `id` / `op` | 节点名（可省略自动补）与操作名 |
| 常量 | `intValue` / `floatValue` / `boolValue` | Const 节点的字面值 |
| 图符号 | `graphId`（调用图）/ `functionName`（FuncLib） | 两者互斥，同现即拒；装载期换成整数 id |
| 数据符号 | `attribute` `tag` `template` `collectionKey` `effectTemplate` `payloadPreset` `builtinHandler` `blackboardKey` `configKey` | 按节点语义取其一，装载期解析（gr-05 符号 patch） |
| 关系 | `relationshipType` / `relationshipMode` / `metric` / `flag` | 关系族节点引用 rel-01 目录条目 |
| 查询 | `slot` `queryCapacityPolicy` `droppedOutput` `validOutput` | 事件槽位与查询容量/落点策略 |
| 形状 | `radiusCm`…`hexRadius` `layerMask` `teamId` `descending` | 空间查询节点的形状与过滤参数 |
| 寄存器 | `pinRegister` | 钉死写目标寄存器；缺省 -1 由分配器决定；仅 int 类节点 |

## 3. 文件结构

条目即文档：主文件 `assets/GAS/graphs.json` 数组元素，或 `GAS/graphs/` 分片一文件一条（gr-02 第 3 节）。

## 4. 运行时加载效果

装载时先过创作门（FrontDoor）：kind 必填且该 kind 允许控制流创作；节点带 next 一律拒；两键边表强制；id 补全大小写不敏感。通过后进编译（gr-05）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 节点带 `next` 字段 | 装载失败（显式拒绝，非忽略） |
| 缺 `controlEdges` 或 `valueEdges` | 装载失败 |
| 缺 `kind` | 装载失败 |
| `graphId` 与 `functionName` 同现 | 装载失败 |
| 未知端口名 | 编译诊断（gr-05） |

## 6. 实例

- `assets/GAS/graphs.json`（多张真实文档）；分片单条示例 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AbsFloat.json`
- 上限与目录计数见 [事实与取值表](../facts.md)

**相关文档**：[gr-03 PRD](../prd/gr-02-document.md) · [gr-02 配置说明](gr-01-model.md) · [gr-05 配置说明](gr-04-compilation.md)
