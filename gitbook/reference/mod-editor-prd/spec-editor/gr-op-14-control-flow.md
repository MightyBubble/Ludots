# gr-op-14 editor spec · 节点：Script 控制流

> 编辑器实现任务书。编辑器需求见 [gr-op-14 UXD](../uxd/gr-op-14-control-flow.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-14-control-flow.md)。

## 1. 概述

结构化糖面板与结构块画布；Halt 自动补、钉槽建议、深度投影为编辑器侧诊断与便利。

## 2. 设计

- **目录条目**：描述符表扫描八行 + 糖常量表五项；糖在 Effect 图只放行 BranchBool。
- **结构块渲染**：While/Until/BranchBool 画为包围块；内部存糖节点，保存时交编译器展开（编辑器不预展开，保往返无损）。
- **Halt 自动补**：保存管线检测显式终结缺失时插入 HaltReturnInt 并出一次性提示；插入位置为入口可达的末端。
- **钉槽建议**：静态识别糖循环内被写的未钉槽 Int 节点，建议写 `pinRegister`。
- **深度投影**：按 InvokeScript 调用链静态估深，与 vm 限额比对。

## 3. 精确语义与不变量

- 糖节点存储形态与手写展开等价：往返 JSON 不丢结构。
- Halt 自动补的判定与编译器 MissingHalt 同源（同一可达性规则）。

## 4. 依赖接口与验收

- 消费：描述符表、糖常量表、FuncLib 注册表、寄存器文件、vm 限额。
- 验收：While 糖保存展开后重开仍是 While 块；缺 Halt 图无法带错保存；子图内 Wait 拒绝落点。

**相关文档**：[gr-op-14 UXD](../uxd/gr-op-14-control-flow.md) · [gr-op-14 runtime spec](../spec-runtime/gr-op-14-control-flow.md)
