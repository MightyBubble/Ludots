# attr-01 · 属性定义与约束

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/attr-01-definition.md)；编辑器需求见 [UXD](../uxd/attr-01-definition.md)；引擎实现见 [runtime spec](../spec-runtime/attr-01-definition.md)；editor spec 见 [editor spec](../spec-editor/attr-01-definition.md)；现状见 [reference](../reference/attr-01-definition.md)。

## 1. 定位

属性是实体上的数值状态（生命、金币、移速）：每个属性一组 Base/Cap/Current 三值与可选约束。一切数值效果的落点。

## 2. 产品承诺

- **名字即声明**：属性名在约束表、绑定表、效果修改器、实体模板中首次出现即注册（全局命名空间，上限见事实页）。
- **血条型与普通型**：`clampToBase` 约束让属性表现为"上限随基线伸缩的池"（掉血减 Current、扩容写 Base）；普通属性只有显式 min/max。
- **约束可热改不可增删**：既有属性的约束数值可经工作台热替换；给无约束属性加约束、或删约束，是重启级。
- **启动后注册表冻结**：新属性名只属于启动期；冻结后再注册即失败。

## 3. 运行行为

约束表在配置链早期加载并注册属性；注册表在全部加载器跑完后冻结；属性 id 从 0 连续分配（上限见事实页）。

## 4. 异常承诺

超过属性上限、冻结后注册新名、引用未注册属性——启动失败并指明名字与位置。

**相关文档**：[配置说明](../config/attr-01-definition.md) · [attr-02](attr-02-modifiers.md) · [ent-01](ent-01-templates.md)
