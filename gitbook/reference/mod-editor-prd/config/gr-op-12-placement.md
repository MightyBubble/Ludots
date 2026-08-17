# gr-op-12 配置说明 · 节点：放置校验

> 配置写法与行为。第一性需求见 [gr-op-12 PRD](../prd/gr-op-12-placement.md)；编辑器需求见 [UXD](../uxd/gr-op-12-placement.md)；现状见 [reference](../reference/gr-op-12-placement.md)。

## 1. 示例配置

节点画廊真实文件（`ClampTargetToRange.json`，Validation 图；`SnapToNearestInCollection.json`，带有效输出口）：

```json
[
  {
    "id": "showcase.graph_op.ClampTargetToRange",
    "kind": "Validation",
    "entry": "caster",
    "nodes": [
      { "id": "caster", "op": "LoadCaster" },
      { "id": "range", "op": "ConstFloat", "floatValue": 500 },
      { "id": "clamp", "op": "ClampTargetToRange" }
    ],
    "controlEdges": [
      { "from": "caster", "fromPort": "next", "to": "range" },
      { "from": "range", "fromPort": "next", "to": "clamp" }
    ],
    "valueEdges": [
      { "from": "caster", "fromPort": "value", "to": "clamp", "toPort": "a" },
      { "from": "range", "fromPort": "value", "to": "clamp", "toPort": "b" }
    ]
  }
]
```

```json
{ "id": "snapCol", "op": "SnapToNearestInCollection", "collectionKey": "showcase.graph_op.snap", "validOutput": "snapValid" }
```

## 2. 逐 op 表

kind 缩写同 gr-op-01；本族四件均 L（线性四类）。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| ClampTargetToRange | L | a（施法者）b（射程） | Bool | 落点拉回射程内；真=发生了拉回 |
| IsPointInCircle | L | a b | Bool | 判定点在圈内的纯谓词 |
| SnapToNearestInCollection | L | source value + imm 集合键 | Entity + 有效口 | 落点吸到集合最近实体；`validOutput` 可指名有效输出口 |
| SnapToNearestGraphEdge | L | value | Bool | 落点吸到路网最近边；假=没吸上 |

互斥与陷阱：

- **副作用在校验图里**：Clamp 与两件 Snap 会改击落点——校验图不是纯读图，写订单校验图时要把"改落点"当业务决策对待。
- SnapToNearestInCollection 的 `value` 是吸附半径（Gallery 例中 200），不是序号。
- 四件不进 Query/Script 图；Script 图的落点校验无对应件（观察项，见 spec）。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`collectionKey`/`validOutput` 写节点字段，见 gr-02。集合键经 ConfigKeyRegistry 同池（gr-op-05）。

## 4. 运行时加载效果

集合键编译期解析；执行期 Clamp 原地改 TargetPos，吸附分别查集合登记与图边投影查询（GraphEdgeProjectionQuery）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 集合键未注册 | 编译失败，指明节点与键名 |
| 吸附无候选 | 集合吸附出无效句柄（有效口假）；边吸附返回假 |
| 引脚类型不符 | 编译失败 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ClampTargetToRange.json`
- 同目录 `IsPointInCircle.json`、`SnapToNearestInCollection.json`、`SnapToNearestGraphEdge.json`

**相关文档**：[gr-op-12 PRD](../prd/gr-op-12-placement.md) · [gr-op-01 配置说明](gr-op-01-context.md) · [ord-05 配置说明](ord-05-input-protocol.md)
