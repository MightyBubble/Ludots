# fx-22 配置说明 · 生命周期原子操作

> 配置写法与行为。第一性需求见 [fx-22 PRD](../prd/fx-23-lifecycle-atomic.md)；编辑器需求见 [UXD](../uxd/fx-23-lifecycle-atomic.md)；现状见 [reference](../reference/fx-23-lifecycle-atomic.md)。

## 1. 示例配置

真实参数块（`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/effects.json` 的 `Effect.GraphOps.Lifecycle`，教学上补齐 preset 即成部署效果条目）：

```json
{
  "id": "Effect.GraphOps.DeployAlly",
  "presetType": "DeployConsumeSource",
  "lifetime": "Instant",
  "configParams": {
    "_ep.targetEntityTemplate":          { "type": "EntityTemplate", "value": "GraphOps.Ally" },
    "_ep.lifecycleAttributeValueSource": { "type": "LifecycleAttributeValueSource", "value": "Current" },
    "_ep.lifecycleAttribute0":           { "type": "Attribute", "value": "Health" }
  }
}
```

预设的默认执行图（真实文件 `assets/GAS/graphs.json`，`Graph.Lifecycle.DeployConsumeSource`）：BeginLifecycleTransaction → MaterializeTemplate → CopyIdentityComponents → CopyAttributeSlice → ClearActiveEffects → TransferStableId → ConsumeEntity。

## 2. 字段与行为

| 参数键 | 这样配会产生什么效果 |
|---|---|
| `_ep.targetEntityTemplate` | 必配（EntityTemplate）：物化出的目标实体模板 id |
| `_ep.lifecycleAttributeValueSource` | 必配：`Base` 或 `Current`——属性切片按哪个值拷贝 |
| `_ep.lifecycleAttribute0..3` | 至少 1 条（Attribute）：要拷贝的属性键，容量 4（事实页/常量） |

效果条目本身只有 preset + 参数：六步链由 DeployConsumeSource 预设的默认相位图执行，无需作者拼图。**现状提示**：预设默认图含未认证生命周期内建，挂本 preset 的模板启动计划编译即拒（治理跟踪中，见 spec E15）；原子链语义经测试直连执行器验证。

## 3. 文件结构

效果条目在 `assets/GAS/effects.json`；目标模板在 `Entities/templates.json`（见 ent-01）；默认图在 `assets/GAS/graphs.json`、预设声明在 `assets/GAS/preset_types.json`（见 fx-03）。

## 4. 运行时加载效果

loader 强制三件必配检查（模板键为正、来源合法、至少一条属性键）；运行期开始事务时捕获源快照，按配置组装后由执行器依序跑六步，失败回滚已物化目标。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 DeployConsumeSource 组合 / 非 Instant | 启动失败，指明效果 |
| 缺模板键 / 来源非法 / 无属性键 | 启动失败，报键名 |
| 源已死或已挂起销毁 | 执行期抛生命周期错误 |
| 目标点不可解析 / 模板键不可解析 / 嵌套事务 | 执行期抛错 |
| 任一原子步失败 | 回滚已物化目标后上抛 |
| 挂本 preset 的模板进入计划编译 | 启动失败（现状，Unsupported(Lifecycle)） |

## 6. 实例

- 生命周期参数三件套：`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/effects.json`（Effect.GraphOps.Lifecycle）
- 六步链与原子性测试：`src/Tests/GasTests/Integration/LifecycleArchitectureTests.cs`（直连执行器验证）

**相关文档**：[fx-22 PRD](../prd/fx-23-lifecycle-atomic.md) · [fx-13 配置说明](fx-14-config-params.md)
