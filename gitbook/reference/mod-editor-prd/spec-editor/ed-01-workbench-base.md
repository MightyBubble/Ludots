# ed-01 editor spec · 实时技能工作台

> 编辑器实现任务书。编辑器需求见 [ed-01 UXD](../uxd/ed-01-workbench-base.md)；引擎侧见 [runtime spec](../spec-runtime/ed-01-workbench-base.md)。

## 1. 概述

工作台前端实现：DataPlane 命令消费、快照渲染、Inspector/效果链/暂存条三视图。

## 2. 设计

- **命令层**：11 个 lsw.* 命令经主题发送；本地乐观更新仅限展示层，真值以快照回包为准（LatestWins 对齐）。
- **快照渲染**：修订号驱动重绘；会话脏标记、预检结论四级、不可用动作清单（ed-03 同源）全部来自快照字段，前端不自算。
- **Inspector**：字段描述符驱动表单（只读/min/max/枚举同源）；编辑即 stageEdit 命令、诊断码就地回显。
- **效果链时间线**：消费 refreshEffectChain 结果，按 trace 聚合七相位；丢弃计数常显。
- **保存流**：previewSave → 计划 diff 视图（含排除项）→ 确认 saveToMod。

## 3. 精确语义与不变量

- 徽章/结论/不可用清单与引擎分类结果逐字段同源，前端不重新分级。
- 修订号回退即丢弃本地乐观态并全量重拉。

## 4. 依赖接口与验收

- 消费：DataPlane 主题与 11 命令、快照 DTO、LSW 诊断码表、效果链刷新结果。
- 验收：改热字段→预检→应用→游戏内验证四步闭环；提交失败红条且暂存保留；保存计划正确列出排除项；断线重连后快照一致。

**相关文档**：[ed-01 UXD](../uxd/ed-01-workbench-base.md) · [ed-01 runtime spec](../spec-runtime/ed-01-workbench-base.md)
