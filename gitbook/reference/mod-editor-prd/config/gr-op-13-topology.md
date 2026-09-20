# gr-op-13 配置说明 · 节点：拓扑谓词

> 配置写法与行为。第一性需求见 [gr-op-13 PRD](../prd/gr-op-13-topology.md)；编辑器需求见 [UXD](../uxd/gr-op-13-topology.md)；现状见 [reference](../reference/gr-op-13-topology.md)。

## 1. 示例配置

节点画廊真实文件（`ControlDomainResolve.json`，Validation 图）：

```json
[
  {
    "id": "showcase.graph_op.ControlDomainResolve",
    "kind": "Validation",
    "entry": "member",
    "nodes": [
      { "id": "member", "op": "LoadExplicitTarget" },
      { "id": "resolve", "op": "ControlDomainResolve" }
    ],
    "controlEdges": [
      { "from": "member", "fromPort": "next", "to": "resolve" }
    ],
    "valueEdges": [
      { "from": "member", "fromPort": "value", "to": "resolve", "toPort": "source" }
    ]
  }
]
```

## 2. 逐 op 表

kind 缩写同 gr-op-01；三件均 L（线性四类）。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| ControlDomainResolve | L | source | Entity | source 所属控制域的代表实体 |
| ControlDomainControls | L | a b | Bool | a 能否指挥 b |
| KnowledgeHasProjection | L | a b | Bool | 观众 a 对 b 有知识投影 |

互斥与陷阱：

- KnowledgeHasProjection 的 a 惯例接 LoadViewer（E2）——"观众知不知道"是这个节点的本意场景；接别的实体则是"任意观察者视角"。
- ControlDomainResolve 出的代表可能就是 source 自己（自成域）——别假设一定换人。
- 三件不进 Query/Script 图；Query 图要按控制域筛实体目前无对应管线件（观察项，见 spec）。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；本族无符号字段，见 gr-02。

## 4. 运行时加载效果

编译期校验引脚；执行期各查一次控制域/知识投影结构，零分配。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 实体不在控制域 | 解析出无效句柄，不报错 |
| 判定遇无效实体 | 返回假 |
| 引脚类型不符 | 编译失败 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ControlDomainResolve.json`
- 同目录 `ControlDomainControls.json`、`KnowledgeHasProjection.json`

**相关文档**：[gr-op-13 PRD](../prd/gr-op-13-topology.md) · [gr-op-01 配置说明](gr-op-01-context.md) · [fx-19 配置说明](fx-19-vision.md)
