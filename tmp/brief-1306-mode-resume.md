# 任务书：#1306 路线①②（InteractionMode 组件 + 投影 + graph op）——恢复运行版

工作树：C:\001_AI\LudotsProd\.worktrees\pi-mode-projection（分支 codex/mode-entity-projection，内容等价 main：a4fa091027 + #1302 + #1303）

## 现场状态（2026-08-28 16:55 快照提交）
进行中改动已快照为 wip commit（含：GameEngine.cs、GASGraph op 表多文件、GraphProgramSymbolPatcher、assets/config_catalog.json、src/Tests/GasTests/InteractionInput/InteractionModeProjectionTests.cs）。勘察+部分实现完成，续做不要推倒。

## 任务（issue #1306 路线①②，gh issue view 1306 读全文）
1. InteractionMode 组件：**稀疏可选**——无组件 = mode.normal 默认态；SetInteractionMode 写 mode.normal = remove 组件，非默认 = add/set；禁止 bind/spawn 全量预挂；测试含「未挂组件的实体不产生模式 context」。
2. 明文映射表（mode → contexts + priority，配置走 ConfigPipeline 模式）：未知 mode id / 引用未定义 context → fail-fast 点名。
3. InputContextProjectionSystem：从实体模式派生应激活 context 集，diff 后走 PushContext/PopContext；输出语义 (seatId, contextId, op) 命令流（不改 handler 拓扑，per-seat 是另一切片）；InteractionContextInputContextBridge 保持原样并存（过渡期双输入源）；scheme 激活路径不动。
4. SetInteractionMode graph op：先找「graph 写实体组件」op 先例照做，禁止平行 op 体系。
5. 持久化：模式组件进存档 round-trip（勘察 CoreSaveParticipants 既有组件登记模式）；严禁 seatId / controlSchemeId / 设备标识入档（禁则④守卫会红）。
6. UAT：graph 写 mode.targeting → 投影下一 tick 激活；写回 normal → 恢复无残留；存档恢复后投影一致；sole seat 零回归。

## 边界
不动 scheme 激活链（ControlSchemeRuntime/ParticipantBindingResolver）、InteractionContextStack 本体、呈现管线、per-seat handler 重构。
注意：另一切片可能已改 ControlSchemeRuntime/ParticipantBindingResolver——若 rebase 到最新 main 有冲突，输入侧改动优先保留对方。

## 工程注意
全量测试污染 artifacts/（留底/恢复/只提交源文件）；ArchitectureTests 5 个存量失败不追；trx logger 拿失败名单；引用存在性自检；push 失败重试；开工前读 gitbook/contributing/ai-assisted-development.md。
PR（base main）：概要 / 对照 #1306 目标模型的落地说明 / 复用清单 / 验证证据，Refs #1306（路线③④仍开放，不写 Closes）。禁止合并。
