# cfg-08 editor spec · 代码扩展面

> 编辑器实现任务书。编辑器需求见 [cfg-08 UXD](../uxd/cfg-08-mod-extensions.md)；引擎侧见 [runtime spec](../spec-runtime/cfg-08-mod-extensions.md)；第一性需求见 [cfg-08 PRD](../prd/cfg-08-mod-extensions.md)。

## 1. 概述

扩展键投影与消费控件的实现：编辑器从运行时取"进程内已注册键清单"，驱动面板与选择器动态生成。

## 2. 设计

- **扩展键投影**：消费引擎的注册表枚举（四注册面分组、含来源 mod 与元数据）；连接运行中实例时实时，离线时明示不可用。
- **节点面板生成**：图 op 投影 + 输入输出类型 → 节点库条目；类型信息供连线校验。
- **选择器与校验**：builtin id / performer 扩展键字段统一用键选择控件；存在性校验消费投影。

## 3. 精确语义与不变量

- 投影与引擎注册表同源；不维护编辑器侧副本。
- 类型约束的编辑器判定与图编译器一致。

## 4. 依赖接口与验收

- 消费：四注册表枚举、图 op 类型信息、图编译校验。
- 验收：注册新 op 后面板即时出现且可连线；引用未注册键在编辑期报出且与启动报错同源。

**相关文档**：[cfg-08 UXD](../uxd/cfg-08-mod-extensions.md) · [cfg-08 runtime spec](../spec-runtime/cfg-08-mod-extensions.md)
