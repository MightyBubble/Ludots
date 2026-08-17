# gr-05 配置说明 · 六种 Kind

> 配置写法与行为。第一性需求见 [gr-04 PRD](../prd/gr-03-kinds.md)；编辑器需求见 [UXD](../uxd/gr-03-kinds.md)；现状见 [reference](../reference/gr-03-kinds.md)。

## 1. 示例配置

kind 写在 graphs.json 条目头。真实实例五种：

```json
{ "id": "Graph.FuncLib.Demo.ConstSeven", "kind": "Script", "entry": "c", "nodes": [ … ], "controlEdges": [ … ], "valueEdges": [ … ] }
{ "id": "showcase.graph_op.AbsFloat", "kind": "Effect", "entry": "modifier", "nodes": [ … ], "controlEdges": [ … ], "valueEdges": [ … ] }
{ "id": "Graph.GraphScore.MissingHealth", "kind": "Score", "entry": "target", "nodes": [ … ] }
{ "id": "showcase.graph_op.LoadEventPayloadFloat", "kind": "Validation", "entry": "payloadFloat", "nodes": [ { "id": "payloadFloat", "op": "LoadEventPayloadFloat", "slot": 0 } ], "controlEdges": [], "valueEdges": [] }
{ "id": "Tests.DerivedAttributeGraph.EngineOwned", "kind": "Derived", "entry": "source", "nodes": [ … LoadSelfAttribute → MulFloat → WriteSelfAttribute … ] }
```

Query 现状无主线实例，写法@@gr8@@。

## 2. 字段与行为

| kind | 返回约定 | 典型挂点（gr-09） |
|---|---|---|
| `Effect` | 无返回；做事节点全放行 | 效果相位图、相位监听 |
| `Query` | 产出 TargetList，按 outputs 物化（gr-09） | 瞄准预览、查询物化 |
| `Score` | 浮点分值写 F[0] | AI 打分 |
| `Validation` | 布尔判定写 B[0]，执行前先清零 | 能力前置、订单校验、OnPropose |
| `Derived` | 不经返回槽：WriteSelfAttribute 直写自身属性 | 派生属性 |
| `Script` | HaltReturnInt 把 I[A] 写入返回整数（I[0] 为宿主 ABI 槽） | BT 叶、HFSM、关卡脚本、FuncLib/ActionLib |

节点白名单要点：Script 专属控制流（Call/Return/Yield/InvokeScript 与全部作者糖）只进 Script 图；Effect 图节点全放行；Derived 图唯一的写属性节点是 WriteSelfAttribute；其余 kind 只收纯读节点。监听图另受相位相容约束（InvokeBuiltin 拒、需监听上下文的 LoadConfig 拒；纯相位须 Pure，非纯相位须 Pure+事务）。

## 3. 文件结构

无独立文件——kind 是 graphs.json 条目字段（gr-03）。

## 4. 运行时加载效果

装载期按 kind 做白名单校验（RequireAllowed）、寄存器边界、分支目标与收尾检查（必含 HaltReturnInt；Effect/Score/Validation/Derived 链尾自动补）；E0/E1/E2 与宿主返回槽在编译期保留，scratch 保护。注册后 kind 不可改。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 越权节点进该 kind | 装载失败（策略错误码八值之一） |
| 挂接点 kind 不符 | 挂接失败，指明图与挂点 |
| Derived 图写非自身属性 | 装载失败 |
| pinRegister 落入保留槽 | 装载失败（寄存器越界/别名冲突诊断） |

## 6. 实例

- Script：`assets/GAS/graphs.json` 17 张；Effect：同文件 1 张（计数见事实页）
- Score：`mods/showcases/capability_standard/CapabilityStandardGraphScoreShowcaseMod/assets/GAS/graphs.json`
- Validation：同目录 NodeGallery 分片；Derived：`mods/fixtures/gas/DerivedAttributeGraphAcceptanceMod/assets/GAS/graphs.json`

**相关文档**：[gr-04 PRD](../prd/gr-03-kinds.md) · [gr-03 配置说明](gr-02-document.md) · [gr-09 配置说明](gr-08-mount-points.md)
