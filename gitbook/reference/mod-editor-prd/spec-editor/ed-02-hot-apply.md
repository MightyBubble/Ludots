# ed-02 editor spec · 热应用白名单与边界

> 编辑器实现任务书。编辑器需求见 [ed-02 UXD](../uxd/ed-02-hot-apply.md)；引擎侧见 [runtime spec](../spec-runtime/ed-02-hot-apply.md)。

## 1. 概述

应用级别标注实现：字段徽章、预检聚合、白名单速查——全部与引擎分类器同源。

## 2. 设计

- **徽章**：静态预估（编辑器侧字段→通道映射表）+ 预检报告实值双层；预检结果覆盖预估。
- **映射表**：由 spec 白名单四通道生成（与 LiveApplyMode 四级同词表）；表是生成物，不手维护。
- **混合级别**：预检报告按级别分组，"只应用可热项"提交 NextCastLiveApply 项并生成重载/重启待办清单。
- **回滚呈现**：CommitRolledBack（LSW0020）逐项展开原因；CommitRollbackFailed（LSW0021）置顶红色并锁定应用按钮。

## 3. 精确语义与不变量

- 徽章判定与引擎 Classify 同源同结果；预估与实值不一致时以实值为准并高亮差异。
- 身份字段清单来自字段描述符只读标记，前端不自判。

## 4. 依赖接口与验收

- 消费：预检分类报告（操作×字段×级别）、四级枚举、LSW0020/0021 诊断。
- 验收：热字段三处修改徽章全"下次施放"；改 preset 身份被锁且说明重启级；混合改动只应用可热项且待办正确。

**相关文档**：[ed-02 UXD](../uxd/ed-02-hot-apply.md) · [ed-02 runtime spec](../spec-runtime/ed-02-hot-apply.md)
