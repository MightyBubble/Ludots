# Case E · 下一任 Agent 任务条

你不是来「再修一遍黄环」的。黄环自写、Invoke 复用命中、continuous 不强制 Query，已在 PR #1444 台阶里。

## 你要做什么

1. 先读远景正本（必读）：  
   `gitbook/architecture/graph-callable-function-vision.md`
2. 再读现网对照：  
   `mods/showcases/case_e_selection/CaseESelectionMod/docs/case-e-config-structure.html`
3. 进度只认：  
   `gitbook/architecture/graph-capability-status.md`
4. **只出可评审方案**（按远景 §7 模板），未评审通过不要大改 Core。

## 方案必须钉死的选择

- 预览终态：图内副作用（S1）还是纯返回+统一物化（S2）？  
- `continuousQuery` 迁到什么字段名？  
- 宿主调用保留 `InvokeGraph` 还是收成 `InvokeFunc`？  
- 与 FuncLib「必须 pure」、以及 #1084/#1099 Query 准纯叙事怎么共存？

## 禁令

- 禁止 `InvokeQuery`  
- 禁止 continuous 再 `GraphReturnWriter` 代写  
- 禁止 commit/tap 再手抄第二份命中链  
- 禁止另写交接替代远景正本

## 验收车

Case E showcase + `CaseESelectionShowcaseAcceptanceTests`；玩家话 UAT 见远景 §6。
