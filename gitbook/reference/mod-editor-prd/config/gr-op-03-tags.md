# gr-op-03 配置说明 · 节点：标签

> 配置写法与行为。第一性需求见 [gr-op-03 PRD](../prd/gr-op-03-tags.md)；编辑器需求见 [UXD](../uxd/gr-op-03-tags.md)；现状见 [reference](../reference/gr-op-03-tags.md)。

## 1. 示例配置

节点画廊真实文件（`HasTag.json`）：

```json
[
  {
    "id": "showcase.graph_op.HasTag",
    "kind": "Effect",
    "entry": "scout",
    "nodes": [
      { "id": "scout", "op": "LoadExplicitTarget" },
      { "id": "hasEnemy", "op": "HasTag", "tag": "State.Sandbox.Marked" }
    ],
    "controlEdges": [
      { "from": "scout", "fromPort": "next", "to": "hasEnemy" }
    ],
    "valueEdges": [
      { "from": "scout", "fromPort": "value", "to": "hasEnemy", "toPort": "source" }
    ]
  }
]
```

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| HasTag | L+Q+SC | source Entity | Bool | source 有效挂有 imm 指定的 tag（规则推导计入） |

互斥与陷阱：

- **本族现存仅此一颗节点**。SelectTagInMask 与 LookupTagDisplayToken 已随 TagDisplay 专线删除（ADR #876）；全库无此二 op，表现层仅剩 TagDisplayTable 残名——配置里不要再写这两个名字。
- "纯读选 tag id"（把 tag 名变成 Int 供查表）按 ADR 活口可重立：输入必须绑通用 tag 集/用户表，禁绑专表；见 spec 治理项 G8。
- 判定是"有效标签"：规则推导出的 tag 也算命中；裸层数判定不是本节点的语义（层数读取走 tag 表现/规则面）。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`tag` 字段写 tag 名（符号），见 gr-04。

## 4. 运行时加载效果

tag 名在编译期经 tag 注册表解析为位 id（首现注册、惰性，见 tag-01）；执行期读有效缓存出 Bool。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未注册 tag 名 | 编译失败，指明节点与 tag 名 |
| 实体无 tag 组件 | 按"没有该 tag"返回假，不报错 |
| 写了已删除的 op 名 | 编译失败，未知 op |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/HasTag.json`

**相关文档**：[gr-op-03 PRD](../prd/gr-op-03-tags.md) · [tag-01 配置说明](tag-01-basics.md) · [gr-op-14 配置说明](gr-op-14-control-flow.md)
