# attr-01 配置说明 · 属性定义与约束

> 配置写法与行为。第一性需求见 [attr-01 PRD](../prd/attr-01-definition.md)；编辑器需求见 [UXD](../uxd/attr-01-definition.md)；现状见 [reference](../reference/attr-01-definition.md)。

## 1. 示例配置

引擎默认约束表（`GAS/attribute_constraints.json`，DeepObject 合并）现状全量：

```json
{
  "Health":   { "clampToBase": true, "min": 0 },
  "Minerals": { "clampToBase": true, "min": 0 },
  "Lumber":   { "clampToBase": true, "min": 0 },
  "Credits":  { "clampToBase": true, "min": 0 },
  "Gas":      { "clampToBase": true, "min": 0 }
}
```

效果修改器里声明属性（教学骨架）：

```json
[ { "id": "Effect.Example.Cost", "modifiers": [ { "attribute": "Credits", "op": "Add", "value": -150 } ] } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| 键（属性名） | 首现即注册；全局命名空间 |
| `clampToBase` | 血条型：Current 上限 = 当前 Base（聚合管线动态扩缩 Cap）；缺省为普通型 |
| `min` / `max` | 数值钳制边界；可只写其一 |

属性初值在实体模板的 `AttributeBuffer` 组件（`base`/`current` 键值表，见 ent-01）。

## 3. 文件结构

`assets/GAS/attribute_constraints.json`（目录登记、DeepObject 合并；引擎默认现有 5 属性）。约束行不是必须的：属性可以只在修改器/模板里出现。

## 4. 运行时加载效果

约束表加载时逐键注册属性并挂约束；全部加载器跑完后注册表冻结。热通道：工作台可替换**既有**属性的约束数值（带回滚）；加/删约束与新增属性名是重启级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 属性总数超上限（事实页） | 启动失败 |
| 冻结后出现新属性名 | 启动失败 |
| 实体模板引用未注册属性 | 启动失败，指明模板与属性名 |

## 6. 实例

- 引擎默认：`assets/GAS/attribute_constraints.json`
- mod 使用：效果表的 Credits 修改器；Ore/Power 见实体模板

**相关文档**：[attr-01 PRD](../prd/attr-01-definition.md) · [attr-02 配置说明](attr-02-modifiers.md)
