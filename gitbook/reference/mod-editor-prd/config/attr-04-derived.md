# attr-04 配置说明 · 派生属性图

> 配置写法与行为。第一性需求见 [attr-04 PRD](../prd/attr-04-derived.md)；编辑器需求见 [UXD](../uxd/attr-04-derived.md)；现状见 [reference](../reference/attr-04-derived.md)。

## 1. 示例配置

**状态标注：实验特性，生产零实例**——assets 全域无绑定、`GAS/graphs.json` 无 Derived kind 图；以下为合法写法推演（教学骨架），裁判样例见 §6 测试。

实体模板组件按图名绑定：

```json
{
  "components": {
    "AttributeDerivedGraphBinding": { "graphs": ["Derived.Example.MoveSpeedFromStacks"] }
  }
}
```

对应图须在 `assets/GAS/graphs.json`（或分片）声明且 kind 为 Derived（教学骨架，节点从略，写法见 gr-04）：

```json
{ "id": "Derived.Example.MoveSpeedFromStacks", "kind": "Derived", "nodes": [ ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `graphs` | 图名数组，逐个经注册表解析；绑定数上限 8（源码常量，见 reference） |
| 数字 id 写法 | `graphProgramIds`/`graphProgramId` 被显式拒绝——"internal only; author graphs by name" |

## 3. 文件结构

绑定写在实体模板（`Entities/templates.json`，见 ent-01）；图写在 GAS 图表（见 gr-04）。图先于绑定加载（引用许可序）。

## 4. 运行时加载效果

模板加载时图名经注册表解析，未知即抛；运行期聚合重算后逐绑定执行，写回宿主属性并使该属性当帧不恢复持久值（见 attr-03）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知图名 | 启动失败 |
| 数字 id 写法 | 拒绝，提示只可写图名 |
| 非 Derived kind 图 | 执行闸拒绝 |
| 绑定数超上限 | 模板加载失败 |
| 写作用域内副作用 | 执行失败（GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden） |

## 6. 实例

- 生产实例：无（实验特性，A7）
- 合法写法参照：`src/Tests/GasTests/GasCore/AttributeDerivedGraphTests.cs`

**相关文档**：[attr-04 PRD](../prd/attr-04-derived.md) · [attr-03 配置说明](attr-03-aggregation.md)
