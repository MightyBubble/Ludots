# gr-op-11 editor spec · 节点：生命周期与内建

> 编辑器实现任务书。编辑器需求见 [gr-op-11 UXD](../uxd/gr-op-11-lifecycle-builtin.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-11-lifecycle-builtin.md)。

## 1. 概述

内建菜单与默认链模板从内建注册表与引擎默认图生成；组合门与事务前驱检查为编辑器侧诊断。

## 2. 设计

- **目录条目**：描述符表扫描两行；非 Effect 图整组隐藏。
- **内建菜单**：数据源 = 内建注册表枚举（含 mod 扩展注册项），分组映射编辑器侧维护；写回 `builtinHandler` 字符串。
- **读参说明**：handler→参数块静态映射表（ApplyForce→ForceParams 等），随注册表版本同步。
- **模板链**：内置引擎默认图副本作为插入模板；插入是纯图操作，不引用运行对象。
- **诊断**：事务前驱可达性与组合门投影复用 runtime spec 定义的检查结论。

## 3. 精确语义与不变量

- 菜单候选与内建注册表一致（含 mod 扩展，随会话刷新）。
- 模板链插入的图与引擎默认图同构。

## 4. 依赖接口与验收

- 消费：描述符表、内建注册表、引擎默认图文档、效果组合域元数据。
- 验收：菜单 20+扩展项分组正确；一键插链编译通过；折叠视图拦截红条可见。

**相关文档**：[gr-op-11 UXD](../uxd/gr-op-11-lifecycle-builtin.md) · [gr-op-11 runtime spec](../spec-runtime/gr-op-11-lifecycle-builtin.md)
