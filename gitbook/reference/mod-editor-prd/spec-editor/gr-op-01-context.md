# gr-op-01 editor spec · 节点：常量与上下文

> 编辑器实现任务书。编辑器需求见 [gr-op-01 UXD](../uxd/gr-op-01-context.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-01-context.md)。

## 1. 概述

节点面板"常量与上下文"分组从描述符表生成，引脚与字面量控件随 op 行自动派生。

## 2. 设计

- **目录条目**：扫描描述符表本族 op 行；条目可用性 = 掩码 ∩ 当前图 kind 非空。
- **节点卡**：无输入引脚（linearPorts/queryPorts/scriptPorts 为空）+ 单输出引脚，输出类型取自描述符输出列。
- **字面量控件**：按 imm 角色派生——Immediate→Int 框、ImmediateFloat→Float 框；ConstBool 走布尔开关。
- **`pinRegister` 控件**：候选槽 = 寄存器容量内全部 Int 槽减去保留槽与已占槽。

## 3. 精确语义与不变量

- 面板条目集合与描述符表逐行一致；不手写目录。
- 补全菜单候选的类型判定与编译器值类型表同源。

## 4. 依赖接口与验收

- 消费：描述符表枚举、寄存器文件保留槽查询、imm 角色枚举。
- 验收：Effect 图中 13 条全可用；Query 图中上下文三件与 LoadExplicitTarget 置灰；ConstFloat 字面量改动往返 JSON 无损。

**相关文档**：[gr-op-01 UXD](../uxd/gr-op-01-context.md) · [gr-op-01 runtime spec](../spec-runtime/gr-op-01-context.md)
