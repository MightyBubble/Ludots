# gr-06 runtime spec · 编译与校验

> 引擎实现任务书。第一性需求见 [gr-05 PRD](../prd/gr-04-compilation.md)；现状见 [reference](../reference/gr-04-compilation.md)。

## 1. 概述

编译器合同：固定检查序、诊断码封闭集、糖展开 SSOT、两段符号 patch。

## 2. 设计

- 检查序保持十四步（头 → id 唯一 → entry → 控制边 → 值边 → 寄存器 → 必需边 → 端口 → 不可达 → SSA → 预算 → 前缀 Jump → 输出 schema → 冻结）。
- 诊断码 GASG0001-0021 封闭；新增码必须扩字典再使用，禁止裸文本诊断。
- 糖名 SSOT 保持 GraphAuthoringSugar；糖展开仅 Script（Wait=Yield 别名）；Linear/Query 链尾自动补 HaltReturnInt(A=0)。
- 符号 patch 两段：注册前种类级 patch（tag/属性/模板/集合键/效果模板/派发预设/配置键/内建/关系四类）；func_lib 装载后 PatchFuncLib 换图 id 清位；幂等靠 ConditionalWeakTable。

## 3. 精确语义与不变量

- 编译纯函数：同文档同诊断同产物；一份文档诊断一次报全；符号 patch 幂等；产物不含字符串符号。

## 4. 迁移与治理

现状即基线；治理项 G3（HaltReturnInt 缺省读 I[0] 未成文）、G4（装载先清后编译、失败半初始化）见 todo/graph.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-05 PRD](../prd/gr-04-compilation.md) · [reference](../reference/gr-04-compilation.md) · [gr-08 spec](gr-06-funclib.md)
