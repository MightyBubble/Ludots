# map-02 · 地图触发器

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/map-02-triggers.md)；编辑器需求见 [UXD](../uxd/map-02-triggers.md)；引擎实现见 [runtime spec](../spec-runtime/map-02-triggers.md)；编辑器实现见 [editor spec](../spec-editor/map-02-triggers.md)；现状见 [reference](../reference/map-02-triggers.md)。

## 1. 定位

触发器是响应游戏事件的代码单元：开局加载、条件满足、定时到点——战役剧情与胜负判定的载体。地图不定义触发器逻辑，只声明**启用哪些**。

## 2. 产品承诺

- **启用地声明**：地图的触发器类型清单是启用开关；逻辑本体在 mod 代码里，同码多图复用。
- **多源并集**：各 mod 对同一地图启用的触发器取并集——难度修正可以额外启用一个"更狠的"触发器而不动原地图。
- **解析 fail-fast**：类型名解析不到触发器类即加载失败。
- **组合表达剧情**：剧情 = 触发器（代码）读地图数据（布阵/元数据）+ 施放效果/下达订单；触发器本身不承载数值。

## 3. 运行行为

进地图时按清单反射实例化并注册触发器；游戏事件到达即触发。

## 4. 异常承诺

类型名不存在或不继承触发器基类——加载失败并指明类型名与地图。

**相关文档**：[配置说明](../config/map-02-triggers.md) · [UXD](../uxd/map-02-triggers.md) · [map-01](map-01-definition.md) · [cfg-08](../prd/cfg-08-mod-extensions.md)
