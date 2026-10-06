# 评审结果：ViewMode 整套退役（PR #1730）

- **评审者**：DeepSeek（独立 AI 评审，未参与前置开发与内部审计）
- **评审对象**：提交 `305c135843`（评审树 `D:/audit-pr1730`），基线 `caae8e2f48`（`D:/audit-base`），按 `artifacts/review-request-viewmode-retire.md` 的攻击面与复现命令独立执行
- **归档说明**：评审者以对话回复交付结论，本文由提交者按回复忠实转录归档；两处后续处置见文末「转录后处置」。

## 判定

**可合（#1713 合入后）。**

## 复现验证（face 7）

| 套件 | 基线 | 评审树 | 比对 |
|---|---|---|---|
| ThreeCTests | 12 红 / 116 绿 | 12 红 / 116 绿 | 仅 1 改名（`...AndViewModeActions`→`...Actions`，两树均红）✓ |
| ArchitectureTests | 5 红 / 388 绿 | 5 红 / **387 绿** | 红名 5/5 相同；PR 少 1 个绿守卫测试 `CoreInput_ViewModeSwitchSystem_DoesNotRenderPersistentHud`（守护已删文件，合法成对删除；提交信息的"388 绿"应披露为 387） |
| GasTests 过滤集 | 1 红 / 28 绿 | 1 红 / 28 绿 | 同名同集 ✓ |

红集逐名对齐、零新增红成立。

## 七攻击面结论

1. **真问题 1 个（face 3，潜伏）**：champion 的 `ChampionSkillSandbox.Controls`（prio 90）经 game.json startup 常驻且永不被弹出（runtime 零 Push/Pop），轮询系统无地图守卫 → 在 Interaction(80)/Road(50) 等低优先地图上 F1-F4 跨 mod 触发、可劫持相机。旧实现 per-mode push/pop + 卸载弹栈，无此泄漏。
2. face 1 全绿：旧 ViewModeSwitchSystem 本来就注册在 LocalInput（基线 :66），新相位是继承非发明；复制客户端上相机半轴当帧消费，InteractionMode/SetInteractionMode 半轴静默失效但与基线同构。
3. face 2 闭合：全仓 4 个集合跟随定义逐写点核对，三条路径全走 owner-aware 助手，无漏网。
4. face 5：DeepObject 数组整替确认为引擎债（今日逐字重述无功能故障，根演进前修即可）；InteractionMode 无网络复制、投影只读本座席 → host-with-seat 无后果。
5. face 6 过渡态诚实：直写 `mapping.SetInteractionMode` 是旧 sink 原位搬迁，非新增层；图 op 463 是另一根轴，不必本 PR 图化。
6. face 4 通过，仅 1 处死引用 `using CoreInputMod.ViewMode;`（PresentationTests，无害）。
7. 审计资产：`D:/audit-pr1730`、`D:/audit-base` 两个干净目录保留。

评审者附加建议：把 champion 上下文常驻泄漏登记为第 3 条遗留，不要让它作为潜伏漂移消失。

## 转录后处置（提交者，commit 见 PR）

1. champion 真问题已修：`PollCastModeSwitch` 增加 `IsSandboxMap` 守卫（上下文常驻但消费者离图不响应；上下文弹栈治理随 champion 自身后续片）。已登记为 gate 产物遗留第 3 条。
2. 死 `using CoreInputMod.ViewMode;`（PresentationTests）已删除。
3. 披露更正：评审树 ArchitectureTests 绿数为 387（原提交信息与评审请求文档写 388，少算的 1 为合法成对删除的守卫测试）；评审请求文档已加勘误注。
