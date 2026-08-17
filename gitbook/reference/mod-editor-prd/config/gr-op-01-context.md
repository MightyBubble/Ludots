# gr-op-01 配置说明 · 节点：常量与上下文

> 配置写法与行为。第一性需求见 [gr-op-01 PRD](../prd/gr-op-01-context.md)；编辑器需求见 [UXD](../uxd/gr-op-01-context.md)；现状见 [reference](../reference/gr-op-01-context.md)。

## 1. 示例配置

节点画廊真实文件（`ConstFloat.json`、`LoadCaster.json`，教学用 vignette 图）：

```json
[
  {
    "id": "showcase.graph_op.ConstFloat",
    "kind": "Effect",
    "entry": "fixed",
    "nodes": [ { "id": "fixed", "op": "ConstFloat", "floatValue": 42 } ],
    "controlEdges": [],
    "valueEdges": []
  }
]
```

```json
[
  {
    "id": "showcase.graph_op.LoadCaster",
    "kind": "Effect",
    "entry": "loadSelf",
    "nodes": [
      { "id": "loadSelf", "op": "LoadCaster" },
      { "id": "alsoSelf", "op": "LoadCaster" },
      { "id": "same", "op": "CompareEqEntity" }
    ],
    "controlEdges": [
      { "from": "loadSelf", "fromPort": "next", "to": "alsoSelf" },
      { "from": "alsoSelf", "fromPort": "next", "to": "same" }
    ],
    "valueEdges": [
      { "from": "loadSelf", "fromPort": "value", "to": "same", "toPort": "a" },
      { "from": "alsoSelf", "fromPort": "value", "to": "same", "toPort": "b" }
    ]
  }
]
```

## 2. 逐 op 表

kind 缩写：E=Effect、S=Score、V=Validation、D=Derived、Q=Query、SC=Script；L=E+S+V+D（线性四类）。引脚名列即 `toPort` 名。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| ConstBool | L | — | Bool | 字面布尔 |
| ConstInt | L+SC | — | Int | 字面整数；可写 `pinRegister` 钉寄存器槽 |
| ConstFloat | L+Q+SC | — | Float | 字面小数 |
| LoadCaster | L+Q+SC | — | Entity 固定 E0 | 施法者 |
| LoadExplicitTarget | L+SC | — | Entity 固定 E1 | 显式目标 |
| LoadContextSource | L | — | Entity | 上下文源实体 |
| LoadContextTarget | L | — | Entity | 上下文目标实体 |
| LoadContextTargetContext | L | — | Entity | 目标的上下文实体 |
| LoadViewer | L | — | Entity 固定 E2 | 观众实体（镜头/知识视角） |
| LoadEventPayloadInt | L | — | Int | 事件载荷整数，`imm` 槽位 0..1 |
| LoadEventPayloadFloat | L | — | Float | 事件载荷小数，`imm` 槽位 0..3 |
| LoadTargetPosX | L | — | Int | 击落点 X 坐标，厘米整数 |
| LoadTargetPosY | L | — | Int | 击落点 Y 坐标，厘米整数 |

互斥与陷阱：

- E0/E1/E2 三个实体槽在任何 kind 下都被编译期保留，scratch 分配自动避让；不要指望"多出来的实体槽"存在。
- Script 图里省略 `value` 的 HaltReturnInt 读环境槽 I[0]，与本族载荷同属宿主注入环境（见 gr-op-14）。
- 上下文三件是"线性四类"专属：Query/Script 图里没有上下文实体注入，不可使用。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录 `assets/GAS/graphs/<名>.json`；节点字段（`op`、字面量、`pinRegister`）写在 nodes 数组里。文档格式见 gr-02。

## 4. 运行时加载效果

图在配置链 `graphs` 环节加载；每个节点 op 查描述符表取 kind 掩码与引脚合同，常量字面量折进指令立即数。挂接点（效果相位、订单校验、AI 打分）在执行前注入上下文实体与事件载荷。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 载荷槽位越界 | 编译失败，指明节点与槽位 |
| `pinRegister` 撞保留槽或超容量 | 编译失败 |
| 上下文实体缺位 | 产出无效实体句柄，消费方判空接管 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ConstFloat.json`
- 同目录 `LoadCaster.json`、`LoadViewer.json`、`LoadEventPayloadInt.json`、`LoadTargetPosX.json`

**相关文档**：[gr-op-01 PRD](../prd/gr-op-01-context.md) · [gr-op-02 配置说明](gr-op-02-math.md) · [gr-op-04 配置说明](gr-op-04-attributes.md)
