# gr-03 配置说明 · 图编程模型

> 配置写法与行为。第一性需求见 [gr-02 PRD](../prd/gr-01-model.md)；编辑器需求见 [UXD](../uxd/gr-01-model.md)；现状见 [reference](../reference/gr-01-model.md)。

## 1. 示例配置

图资产全部住在 `GAS/graphs.json`，真实最小条目（引擎默认 `assets/GAS/graphs.json`）：

```json
{
  "id": "Graph.FuncLib.Demo.ConstSeven", "kind": "Script", "entry": "c",
  "nodes": [ { "id": "c", "op": "ConstInt", "intValue": 7 }, { "id": "h", "op": "HaltReturnInt" } ],
  "controlEdges": [ { "from": "c", "fromPort": "next", "to": "h" } ],
  "valueEdges": [ { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" } ]
}
```

节点与边的完整字段表@@gr1@@；六种 kind 的差别@@gr2@@。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 图的全局名，装载期换成整数 id；同名合并键 |
| `kind` | 六值之一（gr-04），决定返回槽与节点白名单；必填、大小写敏感 |
| `entry` | 控制流起点节点 id |
| `nodes` / `controlEdges` / `valueEdges` | 节点表与控制/值两张边表；两键必须齐（gr-03） |
| `outputs` | 仅 Query 图声明输出物化（gr-09） |

## 3. 文件结构

`assets/GAS/graphs.json` 主文件 + `GAS/graphs/` 分片目录（一文件一条，文件名取 id 尾段；见事实页目录节）。引用许可序：graphs 在 func_lib、action_lib 之前（cfg-05）。

## 4. 运行时加载效果

装载链：读入合并 → 编译即校验（gr-05 诊断码）→ 符号 patch → 注册终态（gr-02 PRD 承诺 4）→ func_lib（gr-08）→ action_lib（gr-09）→ 图名注册表冻结。执行期合同@@gr4@@。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 任一编译诊断（gr-05） | 装载失败，一次报全 |
| 图名注册超上限 | 装载失败 |
| 冻结后再注册图 | 启动失败 |
| func_lib/action_lib 引用未注册图 | 装载失败，指明名字 |

## 6. 实例

- 引擎默认：`assets/GAS/graphs.json`（18 图：17 Script + 1 Effect，计数见事实页）
- mod 分片：`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/`
- 运行时常量与上限：[事实与取值表](../facts.md) 与 [gr-02 reference](../reference/gr-01-model.md)

**相关文档**：[gr-02 PRD](../prd/gr-01-model.md) · [gr-03 配置说明](gr-02-document.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
