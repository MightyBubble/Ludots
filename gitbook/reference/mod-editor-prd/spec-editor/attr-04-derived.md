# attr-04 editor spec · 派生属性图

> 编辑器实现任务书。编辑器需求见 [attr-04 UXD](../uxd/attr-04-derived.md)；引擎侧见 [runtime spec](../spec-runtime/attr-04-derived.md)。

## 1. 概述

实体模板的派生绑定区：图选择器+绑定列表，实验特性徽标。

## 2. 设计

- **投影**：模板组件 graphs 数组 ↔ 绑定行列表；序列化只产图名，永不产出数字 id。
- **数据源**：图注册表投影过滤 kind=Derived，与执行闸同源；容量上限取源码常量（同 reference 锚点）。
- **同源不变量**：选择器可见集=可绑定集；用途属性列由图定义静态扫描。

## 3. 精确语义与不变量

- 图被删后绑定悬空在保存前检出（注册表查名失败即红条）。
- 绑定增删属模板结构变更，统一标注重启生效。

## 4. 依赖接口与验收

- 消费：图注册表枚举（kind 过滤）、图定义解析（写属性扫描）、实体模板模型。
- 验收：非 Derived 图不可选；悬空引用保存被拒并定位模板；序列化产物只含图名。

**相关文档**：[attr-04 UXD](../uxd/attr-04-derived.md) · [attr-04 runtime spec](../spec-runtime/attr-04-derived.md)
