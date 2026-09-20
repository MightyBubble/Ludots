# 审计需求 · 三进程 RTS 多人前线验收（issue-709 收尾链）

- **Subject**: `rts-multiplayer-frontline-three-process-audit`
- **分支**: `codex/issue-709-rts-multiplayer` @ `ecaaa8e31a`
- **Worktree**: `C:\001_AI\LudotsProd_issue709`（worktree 干净，全部改动已提交）
- **交接时间**: 2026-08-24（final8 全绿后）
- **交接方**: Kimi（接续 zcode 会话 sess_3b022e18 完成收尾）

## 一句话现状

三进程（DedicatedServer + 双 Raylib 客户端）RTS 多人前线验收跑到 **final8 全绿**：四里程碑（ready / advancing / engaging / completed）× 双客户端世界证据 + 截图 + 8 组帧缓冲像素证据全部通过；GasTests / PresentationTests 与 main 分叉点基线对比均无新增失败。

## 审计范围与具体问题（按优先级）

### A. 核心运行时修复——`842dae1577`（唯一 src/ 改动）

文件：`src/Core/Presentation/Systems/PresenterEntityTransformSyncSystem.cs`，新增 `PropagateInheritedChildTransforms(parent)`，在两个变换同步调用点的 `SyncFastAttachedChildren(...)` 之后调用。

根因主张：子 presenter 默认 `TransformSource.InheritParent`（PresenterEntityRuntime.cs:264-265），此前只被行为系统 tick 的 `PropagateParentDrivenTransforms` 覆盖，无行为根 presenter 永不被 tick → 子级稳定可视层冻结在创建位置。

请审计：

1. **完备性**：两个调用点（EntityAnchoredQuery 循环、SyncSingleRootOwnerPayloads）是否覆盖了全部根变换同步路径？是否存在第三条路径漏挂？
2. **正确性**：方法内「扫描子级 → 凡无 PerfOwnerPayloadAttachedTransformSync / PerfHasAttachmentTick 标记且 TransformSource 为 InheritParent/AttachedToParent → 调一次完整传播后 return」的语义——return-after-first 在多子级/多根场景下是否漏传播？
3. **性能**：每帧全量子级扫描的开销是否可接受？是否需要增量标记？
4. **回归面**：对带 Attachment 行为/快速标记子级的既有路径是否零影响（设计上应短路跳过）？
5. **测试真实性**：`InheritParent_OwnerPayloadDescendantsFollowMovedRootAndRetainStableIdentity`（PresentationTests，由红转绿）是否真实覆盖该合同，还是恰好通过？

### B. 验收管线规则调整（6 个提交，全部在 scripts/acceptance/）

请独立判断每次规则放松是否有依据，还是「下调阈值凑绿」：

1. `1ecc6ab2f6` **engaging 世界间距可选化**——不声明 `minimumWorldSeparationSource` 即跳过。依据：final6 实测实体 88/140 间距 91.3cm，围攻阶段单位合法贴近（DirectAttackProfile RangeCm=650 不约束死亡/出场瞬态）。**请复核这个 91.3cm 是否真是合法玩法位置，还是掩盖了聚集/寻路缺陷。**
2. `b106004c50` **completed forbidden defeatedCore 改 anchor 范围**——依据：`completedLosingCoreCount=0` 证明败方核心已销毁，screen 范围误伤 4000cm 外存活的胜方核心。请确认 anchor + positionToleranceCm=2200 没有削弱「核心死后消失」合同。
3. `1d1c85683f` / `b89ffe9dfb` **minimumObservedMoveCm 1000→500**（截图规则 + 验收计划两处）——依据：一屏化后基地间距 4000cm，旧阈值按宽图标定几何不可达。请确认 500cm 仍保有证明力。
4. `b106004c50` 之前的 `f21e8703ec`（显式 100cm）已被 `1ecc6ab2f6` 取代，审计时以最终态为准。
5. `c658fb6f28` 严格模式属性探测修复——纯脚本健壮性，低风险。

### C. 前段 WIP 链净效果（zcode 会话段，`1919401b73` 至 `1d1c85683f`）

1. 多个 `wip(diag)` 提交声称探针已撤销（`b282b465c9`「撤销全部临时探针」）——**请验证最终树中无残留探针代码**（搜 FrontlineSystems.cs / LocalOrderSourceHelper.cs 等被探针触碰过的文件）。
2. 关键玩法修复各一个提交，均有逐里程碑验证证据：
   - `e133e14706` 开图就绪改判直接证据（revision 增量闸门死锁）
   - `64afe2bdb9` 开图相机播种后 return 删除
   - `9556fedf1e` 命令 intent collection owner 唯一座位回退
   - `d0842fcca6` 步兵模板 Team/PlayerOwner 哨兵 0

### D. GasTests 两个真回归的归属确认

分支 vs main 分叉点（`6daa88a45d`）基线对比：分支 57 失败 / 2924 通过，基线 74 失败 / 2712 通过；分支净修复 36 个基线失败，19 个"新失败"均为分支新增测试。仅 2 个真回归，均不在本收尾链触动的文件范围，请独立确认归属并决定是否阻塞合并：

1. `InputOrderMapping_PositionMoveCommand_WithGroupTargetLayout_AssignsOffsetTargetsAcrossExplicitActorCollection`——期望 `<380,0,640>` 实测 `<381,0,640>`（1cm 布局标定差）
2. `ProdModSmoke_ChampionSkillSandboxMod`——EntityCommandPanelMod 缺 `UiTextMeasurer` 服务注册（`EntityCommandPanelController.cs:39`）

### E. 证据自洽性与可复现性

1. final8 证据目录内三方 gameplay-evidence、run-manifest、截图、帧缓冲 request/result 是否互相自洽（tick、坐标、血量交叉一致）。
2. **重跑验证**（约 6-7 分钟）：worktree 干净状态下执行
   `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/acceptance/run-rts-multiplayer-frontline-three-process.ps1 artifacts/acceptance/rts-multiplayer-frontline-three-process/<audit-tag>`
   注意：bash 环境缺 Windows 变量，直接 cmd/powershell 下 dotnet 在 PATH 即可；若 testhost 报 SkiaSharp DllNotFound，把 `src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/libSkiaSharp.dll` 拷到对应测试输出目录。

## 已完成工作（精确路径）

- 终验证据：`artifacts/acceptance/rts-multiplayer-frontline-three-process/20260824T_kimi_final8/`（run-manifest.json status=passed，三方 gameplay-evidence.json，8 组截图 evidence + framebuffer-pixel-evidence）
- skill 工件：`artifacts/acceptance/rts-multiplayer-frontline-three-process/battle-report.md`、`trace.jsonl`、`path.mmd`
- 逐轮失败证据：同目录 `20260824T_kimi_final4` 至 `final7`（每轮唯一失败原因见对应提交 message）

## 未决事项（Owner: 仓库 owner，非审计方）

1. 是否推送分支并整理 wip/diag 提交链
2. 是否关 issue #1074 / #1075
3. GasTests 两个真回归在本分支处理还是另开 issue

## 审计方 Next Actions（执行顺序）

1. 读 `842dae1577` diff + 本节 A 的五个问题，给出运行时修复的正确性裁决
2. 按 E.2 重跑一轮验收，确认可复现全绿
3. 按 B/C/D 逐项给出「规则放松有依据 / 凑绿」判定与回归归属确认
