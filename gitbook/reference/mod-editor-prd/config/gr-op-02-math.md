# gr-op-02 配置说明 · 节点：数学与比较

> 配置写法与行为。第一性需求见 [gr-op-02 PRD](../prd/gr-op-02-math.md)；编辑器需求见 [UXD](../uxd/gr-op-02-math.md)；现状见 [reference](../reference/gr-op-02-math.md)。

## 1. 示例配置

节点画廊真实文件（`AddFloat.json`，两常量求和）：

```json
[
  {
    "id": "showcase.graph_op.AddFloat",
    "kind": "Effect",
    "entry": "base",
    "nodes": [
      { "id": "base", "op": "ConstFloat", "floatValue": 30 },
      { "id": "bonus", "op": "ConstFloat", "floatValue": 12 },
      { "id": "sum", "op": "AddFloat" }
    ],
    "controlEdges": [
      { "from": "base", "fromPort": "next", "to": "bonus" },
      { "from": "bonus", "fromPort": "next", "to": "sum" }
    ],
    "valueEdges": [
      { "from": "base", "fromPort": "value", "to": "sum", "toPort": "a" },
      { "from": "bonus", "fromPort": "value", "to": "sum", "toPort": "b" }
    ]
  }
]
```

`SelectEntity.json` 同目录：CompareEqInt 产 Bool，喂 SelectEntity 的 `condition`，`a`/`b` 分别接 LoadExplicitTarget 与 LoadCaster 的实体值线。

## 2. 逐 op 表

kind 缩写同 gr-op-01（L=E+S+V+D、Q、SC）。Float 读属性用 LoadAttribute，见 [gr-op-04](gr-op-04-attributes.md)。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| AddFloat / MulFloat / SubFloat / DivFloat | L | a b Float | Float | 四则 |
| MinFloat / MaxFloat | L | a b | Float | 取小 / 取大 |
| ClampFloat | L | value min max | Float | 钳制到闭区间 |
| AbsFloat / NegFloat | L | value | Float | 绝对值 / 取负 |
| RandomFloat01 | L | — | Float | 0 到 1 区间随机数，每次执行取新值 |
| CompareGtFloat | L+SC | a b | Bool | a > b |
| AddInt | L+SC | a b Int | Int | 整数加法 |
| CompareLtInt | L+SC | a b | Bool | a < b |
| CompareEqInt | L | a b | Bool | a == b |
| CompareEqEntity | L+Q | a b Entity | Bool | 两句柄是否同一实体 |
| SelectEntity | L | condition a b | Entity | condition 真→a，假→b |

互斥与陷阱：

- Int 与 Float 无隐式转换：Int 值线接不进 Float 引脚，反之亦然；跨类型换算目前没有专用节点，公式要在同一类型内闭合。
- Query 图里本族只有 CompareEqEntity（L+Q）可用；AddFloat 系纯 Float 节点在 Query 图不可用。
- RandomFloat01 打破纯度：需要可复现结果的图（如校验）慎用。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；格式见 gr-02。

## 4. 运行时加载效果

编译期做引脚类型检查（a/b/value/min/max/condition 的值类型来自描述符表）；执行期双目节点各读一次值线，结果写目的寄存器，零分配。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引脚悬空或类型不符 | 编译失败，指明节点与引脚 |
| Int/Float 混接 | 编译失败 |
| 除零等数值域问题 | 不算图错误，按引擎浮点语义产出 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AddFloat.json`
- 同目录 `ClampFloat.json`、`RandomFloat01.json`、`CompareGtFloat.json`、`AddInt.json`、`SelectEntity.json`

**相关文档**：[gr-op-02 PRD](../prd/gr-op-02-math.md) · [gr-op-01 配置说明](gr-op-01-context.md) · [gr-op-14 配置说明](gr-op-14-control-flow.md)
