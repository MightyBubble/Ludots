# gr-op-05 配置说明 · 节点：黑板

> 配置写法与行为。第一性需求见 [gr-op-05 PRD](../prd/gr-op-05-blackboard.md)；编辑器需求见 [UXD](../uxd/gr-op-05-blackboard.md)；现状见 [reference](../reference/gr-op-05-blackboard.md)。

## 1. 示例配置

节点画廊真实文件（`ReadBlackboardFloat.json`）：

```json
[
  {
    "id": "showcase.graph_op.ReadBlackboardFloat",
    "kind": "Effect",
    "entry": "src",
    "nodes": [
      { "id": "src", "op": "LoadContextSource" },
      { "id": "readF", "op": "ReadBlackboardFloat", "blackboardKey": "showcase.bb.power" }
    ],
    "controlEdges": [
      { "from": "src", "fromPort": "next", "to": "readF" }
    ],
    "valueEdges": [
      { "from": "src", "fromPort": "value", "to": "readF", "toPort": "source" }
    ]
  }
]
```

写侧同构：`WriteBlackboardFloat` 节点带 `source`/`value` 引脚与 `blackboardKey`（教学骨架：读上下文源实体的黑板并写回另一键）：

```json
{ "id": "w", "op": "WriteBlackboardFloat", "blackboardKey": "showcase.bb.power" }
```

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| ReadBlackboardFloat | L+SC | source Entity + imm 键 | Float | 读 source 黑板 Float 键 |
| ReadBlackboardInt | L+SC | source + imm 键 | Int | 读 Int 键 |
| ReadBlackboardEntity | L+SC | source + imm 键 | Entity | 读 Entity 键 |
| WriteBlackboardFloat | E | source value + imm 键 | — | 写 source 黑板 Float 键 |
| WriteBlackboardInt | E | source value + imm 键 | — | 写 Int 键 |
| WriteBlackboardEntity | E | source value + imm 键 | — | 写 Entity 键 |

互斥与陷阱：

- 键经 ConfigKeyRegistry 声明（黑板条目上限见事实页）；未注册键编译期失败。
- Read 是 L+SC：Effect/Score/Validation/Derived/Script 可用，Query 图不可用；Write 只在 Effect 图。
- 图内黑板键与订单系统内置黑板键（persistentStoredTarget 五键，ord-04）同池：撞名时读到的就是订单写的那份。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`blackboardKey` 字段写键名，见 gr-04。

## 4. 运行时加载效果

键名编译期经 ConfigKeyRegistry 解析；执行期读写实体黑板缓冲（订单/AI 与图共用同一缓冲）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 键未注册 | 编译失败，指明节点与键名 |
| 键类型与节点不符 | 编译失败 |
| Read 遇实体无黑板缓冲 | 缺省值，不报错 |
| Query 图用 Read | 编译失败（kind 掩码外） |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ReadBlackboardFloat.json`
- 同目录 `ReadBlackboardInt.json`、`ReadBlackboardEntity.json`、`WriteBlackboardFloat.json`、`WriteBlackboardInt.json`、`WriteBlackboardEntity.json`

**相关文档**：[gr-op-05 PRD](../prd/gr-op-05-blackboard.md) · [ord-04 配置说明](ord-04-blackboard.md) · [gr-op-04 配置说明](gr-op-04-attributes.md)
