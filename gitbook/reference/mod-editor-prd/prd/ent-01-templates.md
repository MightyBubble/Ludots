# ent-01 · 实体模板

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ent-01-templates.md)；编辑器需求见 [UXD](../uxd/ent-01-templates.md)；引擎实现见 [runtime spec](../spec-runtime/ent-01-templates.md)；editor spec 见 [editor spec](../spec-editor/ent-01-templates.md)；现状见 [reference](../reference/ent-01-templates.md)。

## 1. 定位

实体模板是组件初值的命名快照：一个模板 = 一类单位/建筑/标记物。地图布阵引用模板并逐实例覆盖；效果造单位也引用模板。

## 2. 产品承诺

- **组件开放映射**：模板的组件是"引擎组件名 → 初始值 JSON"的开放映射——引擎有什么组件，模板就能配什么，不需要每加组件改 schema。
- **三处消费同一份**：地图布阵、效果造单位、出生效果钩子引用同一模板表。
- **出生效果钩子**：模板可声明生成时施加的效果（如经济建筑的产钱 buff）。
- **跨 mod 合并**：模板表是配置目录内的一张表，同 id 深合并——皮肤与强化 mod 可只改数值不动结构。

## 3. 运行行为

模板在启动期随表加载注册；实例化发生在地图加载（布阵）或效果执行（造单位）时：组件按名装配，实例覆盖值最后写入。

## 4. 异常承诺

组件名不存在、组件初值不合组件 schema、引用未注册模板——加载/实例化失败并指明。

**相关文档**：[配置说明](../config/ent-01-templates.md) · [UXD](../uxd/ent-01-templates.md) · [map-01](map-01-definition.md) · [cfg-04](../prd/cfg-04-config-tables.md)
