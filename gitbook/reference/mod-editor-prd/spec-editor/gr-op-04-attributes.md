# gr-op-04 editor spec · 节点：属性与配置

> 编辑器实现任务书。编辑器需求见 [gr-op-04 UXD](../uxd/gr-op-04-attributes.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-04-attributes.md)。

## 1. 概述

属性/配置选择器与直写警示从描述符标记自动派生。

## 2. 设计

- **目录条目**：描述符表扫描；WriteSelfAttribute 的警示与 LoadConfig 的禁用条件分别取 `derivedWrite`、`listenerOwner` 标记。
- **属性选择器**：数据源 = 属性注册表投影（含约束徽标）；写回节点 `attribute` 字符串。
- **配置键选择器**：数据源 = ConfigKeyRegistry 投影；按值类型过滤（Float/Int/EffectId 各自匹配）。
- **监听图判定**：图宿主类型为监听宿主时，LoadConfig 条目置灰原因文案固定。

## 3. 精确语义与不变量

- 选择器候选与注册表投影一致；禁用判定与编译器同源（标记 + 宿主类型）。
- 选择器只写字符串符号，不内联 id。

## 4. 依赖接口与验收

- 消费：描述符表（derivedWrite/listenerOwner）、属性注册表投影、ConfigKeyRegistry。
- 验收：监听图内 LoadConfig 置灰；WriteSelfAttribute 警示常驻；Derived 图可用 WriteSelfAttribute 而其他写节点置灰。

**相关文档**：[gr-op-04 UXD](../uxd/gr-op-04-attributes.md) · [gr-op-04 runtime spec](../spec-runtime/gr-op-04-attributes.md)
