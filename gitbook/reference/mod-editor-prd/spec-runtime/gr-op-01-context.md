# gr-op-01 runtime spec · 节点：常量与上下文

> 引擎实现任务书。第一性需求见 [gr-op-01 PRD](../prd/gr-op-01-context.md)；现状见 [reference](../reference/gr-op-01-context.md)。

## 1. 概述

值入口族的描述符与固定槽合同：常量折叠、E0/E1/E2 保留、宿主环境注入。

## 2. 设计

- E0/E1/E2 由寄存器文件按 kind 统一 Reserve（EntityPreset 理由），编译器 scratch 分配必须避让——保留是 kind 无关的硬保留。
- 常量节点编译为"立即数进目的寄存器"单指令；ConstInt 的 `pinRegister` 走钉槽分配，校验保留槽与容量。
- 事件载荷与落点坐标是宿主预注入的环境读取：节点不解析、不缓存，执行期一次搬运。
- **治理项**：载荷槽位范围（Int 0..1、Float 0..3）目前只在描述符 imm 上体现，缺编译期显式边界诊断——补一条带槽位号的错误信息。

## 3. 精确语义与不变量

- E0=LoadCaster、E1=LoadExplicitTarget、E2=LoadViewer，任何 kind 下恒成立。
- 保留槽上的 Reserve 标记使 scratch 永不落入 E0..E2。
- HaltReturnInt 缺省 `value` 读 I[0]，与 Script Host ABI 同槽（属 gr-op-14，此处只约束：本族不得写 I[0]）。

## 4. 迁移与治理

现状即基线；载荷槽位诊断入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-01 PRD](../prd/gr-op-01-context.md) · [reference](../reference/gr-op-01-context.md)
