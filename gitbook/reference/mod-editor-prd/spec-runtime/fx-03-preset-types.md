# fx-03 runtime spec · Preset 类型系统

> 引擎实现任务书。第一性需求见 [fx-03 PRD](../prd/fx-03-preset-types.md)；现状见 [reference](../reference/fx-03-preset-types.md)。

## 1. 概述

preset 原型的注册、id 段、组件元数据定位与默认处理器回落合同。

## 2. 设计

- 注册语义保持：全字段必填，handler 仅 builtin|graph；内建枚举名占固定 id，mod 原型从 FirstModPresetTypeId 起、上限 2048；Freeze 后拒注册。
- 组件元数据定位保持：components 是纯声明性元数据，不驱动块校验；块校验唯一正本在模板 loader 的 preset 联动规则，两处不得再分叉。
- 回落链保持：模板 Main 图权威；无 Main 且未 SkipMain 才回落 preset 默认处理器（fx-05）。

## 3. 精确语义与不变量

- 同一 presetType 解析结果唯一：注册表优先于内建枚举，无静默覆盖；components 与模板块校验不存在隐式耦合。

## 4. 迁移与治理

治理项 E1（todo/effect.md）：内建 16 种 preset 绝大多数无核心资产消费者——为每原型补至少一条核心/底座示范条目，或在手册标注示例出处。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-03 PRD](../prd/fx-03-preset-types.md) · [reference](../reference/fx-03-preset-types.md)
