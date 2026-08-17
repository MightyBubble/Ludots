# gr-op-11 配置说明 · 节点：生命周期与内建

> 配置写法与行为。第一性需求见 [gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md)；编辑器需求见 [UXD](../uxd/gr-op-11-lifecycle-builtin.md)；现状见 [reference](../reference/gr-op-11-lifecycle-builtin.md)。

## 1. 示例配置

引擎默认真实图（`assets/GAS/graphs.json` 的 `Graph.Lifecycle.DeployConsumeSource`，部署吞噬链七节点）：

```json
{
  "id": "Graph.Lifecycle.DeployConsumeSource",
  "kind": "Effect",
  "entry": "begin",
  "nodes": [
    { "id": "begin", "op": "BeginLifecycleTransaction" },
    { "id": "materialize", "op": "InvokeBuiltin", "builtinHandler": "MaterializeTemplate" },
    { "id": "copyIdentity", "op": "InvokeBuiltin", "builtinHandler": "CopyIdentityComponents" },
    { "id": "copyAttrs", "op": "InvokeBuiltin", "builtinHandler": "CopyAttributeSlice" },
    { "id": "clearFx", "op": "InvokeBuiltin", "builtinHandler": "ClearActiveEffects" },
    { "id": "transferId", "op": "InvokeBuiltin", "builtinHandler": "TransferStableId" },
    { "id": "consume", "op": "InvokeBuiltin", "builtinHandler": "ConsumeEntity" }
  ],
  "controlEdges": [
    { "from": "begin", "fromPort": "next", "to": "materialize" },
    { "from": "materialize", "fromPort": "next", "to": "copyIdentity" }
  ],
  "valueEdges": []
}
```

（控制边依序串联至 consume，此处省略后五条。）

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| BeginLifecycleTransaction | E | — | — | 开生命周期事务 |
| InvokeBuiltin | E | imm=handler 符号 | — | 调用内建处理器（委托内建） |

内建 handler 全表二十个（`builtinHandler` 字段取值）：

| 分组 | handler | 干什么 |
|---|---|---|
| 通用 | ApplyModifiers | 读合并参数对目标上修改器 |
| 空间派发 | SpatialQuery / DispatchPayload / ReResolveAndDispatch | 空间检索、按列表派发载荷、周期重检索再派发 |
| 物理 | ApplyForce | 按 ForceParams 加 2D 力 |
| 造物 | CreateProjectile / CreateUnit | 造弹道实体 / 排队造单位 |
| 位移 | ApplyDisplacement | 建位移状态实体 |
| 关系视野 | ApplyRelation / RevealArea / DecayRevealArea | 改父子关系 / 揭示区域 / 揭示衰减 |
| 兑换进度 | ExecuteExchange / CompleteProgression / SubmitOrderFromBlackboard | 结兑换 / 完成进度 / 按黑板键下单 |
| 生命周期原子 | MaterializeTemplate / CopyIdentityComponents / CopyAttributeSlice / ClearActiveEffects / TransferStableId / ConsumeEntity | 物化/复制身份/复制属性切片/清效果/移交稳定 id/吞噬 |

互斥与陷阱：

- **组合门**：效果组合编译对 Lifecycle 域 fail-closed——本族不能折进效果模板相位图，只能显式 Effect 图。
- 事务纪律：InvokeBuiltin 要落在 BeginLifecycleTransaction 之后的生命周期图里；把内建散进普通相位图会被管线校验拒。
- handler 名是符号不是数字 id；mod 代码新内建走扩展注册窗口（cfg-08），别改内置表。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`builtinHandler` 写 handler 名，见 gr-02。生命周期内建语义正本见 fx-23。

## 4. 运行时加载效果

handler 符号编译期对内建注册表解析；执行期事务内逐个委托执行，参数从效果上下文合并读取。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| handler 符号未注册 | 编译失败，指明节点与符号 |
| 效果组合折叠遇本族 | 编译拒绝（Lifecycle 域 fail-closed） |
| 事务外调用内建 | 生命周期管线校验拒绝 |

## 6. 实例

- 引擎默认：`assets/GAS/graphs.json`（`Graph.Lifecycle.DeployConsumeSource`）
- 节点画廊：`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeBuiltin.json`、`BeginLifecycleTransaction.json`

**相关文档**：[gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md) · [fx-23 配置说明](fx-23-lifecycle-atomic.md) · [cfg-08 配置说明](cfg-08-mod-extensions.md)
