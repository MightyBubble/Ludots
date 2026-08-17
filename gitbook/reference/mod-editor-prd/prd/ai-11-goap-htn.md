# ai-11 · GOAP 与 HTN 规划

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-11-goap-htn.md)；编辑器需求见 [UXD](../uxd/ai-11-goap-htn.md)；引擎实现见 [runtime spec](../spec-runtime/ai-11-goap-htn.md)；editor spec 见 [editor spec](../spec-editor/ai-11-goap-htn.md)；现状见 [reference](../reference/ai-11-goap-htn.md)。

## 1. 定位

世界状态族六表让单位"为目标做计划"：atoms 声明 256 位世界观的位，projection 把 Order 黑板投影进世界状态，utility goals 选择要满足的目标，goap_actions/goap_goals/htn_domain 分别喂三个规划引擎（GOAP A*、HTN 分解、DirectTask 直发）。

## 2. 产品承诺

- **atom 首现即注册**：atoms 表仅 id；他表引用未声明 atom 启动失败。
- **投影声明式**：五种 op（Int 比较/Entity 判空）读 Order 黑板键（键须 order_types 声明或内建）。
- **目标双轨计分**：utility goals 带 Bool 考量（True/FalseScore）与规划策略（None/Goap/Htn/DirectTask）。
- **三引擎一出口**：GOAP 加权 A*（世界状态版本变更才重规划）、HTN 栈式 DFS 分解、计划执行统一走 PlanExecutor.TrySubmitOrder。

## 3. 运行行为

每步：AIGoalSelectionSystem 按 utility goals 计分选目标；GoapPlanningSystem 检查世界状态版本，变更才重规划（A* 在 256 位世界状态上搜，动作来自 ActionLibraryCompiled256 的 SoA 位掩码）；HtnPlanningSystem 栈式分解方法回退；AIPlanExecutionSystem 逐步 TrySubmitOrder。

## 4. 异常承诺

引用未声明 atom、projection 键未声明且非内建、IntKey/EntityKey 与 op 不匹配、IntValue/EntityKey 互斥破坏、goap_action 缺 Order、htn_domain 引用越界——启动失败并带表:id.字段。

**相关文档**：[配置说明](../config/ai-11-goap-htn.md) · [ai-02](ai-01-utility-overview.md) · [cfg-04](cfg-04-config-tables.md)
