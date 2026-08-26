# Codex 审计需求：RelationshipTemplate（#1063）与 Entity Attachment（#1064）

> 交接文档（2026-08-23 写、08-25 重发——本地 tmp 副本被清理，以此为准）。两条独立工作线已完成实现、自审、对抗性审计与修正，需要合并前终审。票面即合同；本页只讲"东西在哪、审什么、怎么验"。

## 1. 工作地点一览（分支均已推到 origin）

| 线 | Issue | 分支（origin 上） | 基点 | worktree（本地） |
|---|---|---|---|---|
| A：RelationshipTemplate | #1063 | `origin/codex/relationship-template`（3 commits） | `b5ff09490d` | `C:\001_AI\wt-ludots-reltemplate` |
| B：Entity Attachment | #1064 | `origin/codex/entity-attachment`（7 commits，顶 `0cea6c6021`） | `b5ff09490d` | `C:\001_AI\wt-ludots-attachment` |

- 不要在 `C:\001_AI\LudotsProd` 主工作树干活。
- origin/main 已大幅漂移（08-25 已到 PR #1181 之后）。对当前 main 直接 `git diff` 会显示**幻影删除**；真实 delta 用 `git diff b5ff09490d..<branch>`；终审前建议先 rebase 到新 main。

## 2. A 线（#1063）：审计 PASS，可合

3 commits：`7b3e6275ae`（主实现 8 文件 +654）→ `e908f364d2`（验证记录）→ `5ec8a0bbbc`（自审摘要对齐）。

- 对抗性审计逐条 PASS（复用 ComponentRegistry 链、幂等由构造保证、安装期预编译零 JSON、稳态零分配、示范数据只在 showcase mod）：见 #1063 评论 issuecomment-5386026673。
- 全量 GasTests 与基线逐名 diff（trx 提名）：**分支独有失败 = 0**（基线 75 / 分支 74，唯一差异是基线侧一个性能基准 flaky）。
- 非阻断遗留（评论有记录）：跨文件常量镜像无守卫测试；有/无模板组件形状分叉；零分配门偏宽松；运行时注册类型静默无模板。

复核：worktree 内 `dotnet build src/Core/Ludots.Core.csproj` + `dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~RelationshipTypeTemplate|FullyQualifiedName~Association"`。

## 3. B 线（#1064）：审计出过 1 个 HIGH + 1 个约定违反，均已修正，待终审

修正历史（细节见 #1064 三条评论与自审产物 §10）：

1. **HIGH-1（已修）**：parent-moved 门 × 求解器时序——SavePrevious 步首抹平 Previous、真实位姿写者（求解器/订单/位移）都在 sink 之后 → 门恒判"未移动"，位置依赖子永久冻结，且被测试时序掩护。修正：**门删除、sink 恒重算**（票面条目作废，issue 已记录偏离）；回归测试 `PostSinkWriterTiming_PositionDependentChild_StillFollows`；两个 harness 改引擎真实时序。
2. **约定违反（用户指出，已修）**："挂接链上只允许一个 mass nav 成员"。此前实现保留子 nav agent + 复用 displaced 求解器状态——违反约定。修正：nav 域 `SuspendedNavMembership` 快照 + `MassNavigationMembership.Suspend/Restore` 单点；attach 摘除成员身份三组件、detach/孤儿自愈回放（旧 Index 不复用，`MassNavigationAuthoredAgentBindingSystem` 按已提交位姿重播种）；事务路径同构 stage（同事务正反抵消）；桥接 Nav↔Attached displaced 复用回退为 main 形态（Attached 转移显式 no-op）；"nav agent 无 MovementParticipation fail-fast"旧合同删除。
3. **M1/M2（已修）**：StageAttach facing 走 staged 视图（`TryReadRelationFacing`）；OwnFacing 朝向回退链恢复（子→父→0，`ResolveOwnFacingRad` 四处统一）。

终审重点（建议顺序）：恒重算取舍确认 → Suspend/Restore 全调用点生命周期完备性（含事务回滚一致性）→ 绑定系统 append/rebuild 对挂起→恢复序列的行为 → Attached 写权与已挂起成员身份解耦后的边界 → 真实引擎端到端再压一次多层跟随。

复核：
```bash
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~Attachment"          # 应 23/23
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter "FullyQualifiedName~MassNavigationAttachedAuthority"  # 应 1/1
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj                                  # 4 失败为基线既有
```

## 4. 基线认知与环境坑

- 基线 `b5ff09490d` 全量 GasTests 本身 ~74 个既有失败（Shield/Durability 属性未注册、SkiaSharp 白名单等）+ ArchitectureTests 4 个——判断回归用逐名 diff，别看总数。
- `dotnet test --results-dir` 会被 MSBuild 拒（MSB1001）且退出码被管道掩盖——只用 `--logger "trx;LogFileName=x.trx"`。
- 长命令（首次 restore 的全量构建）超 10 分钟会触发 agent 600 秒看门狗——后台跑并轮询。
- B 线测试再生 `artifacts/acceptance/entity-attachment/**` 是分支自身证据；其他 acceptance 目录的再生是噪音，提交前还原。

## 5. 红线（AGENTS.md）

不新增 `docs/adr/`；AAC ADR SSOT 在 #239/#244；代码注释禁 issue/PR 编号；attachment 只管绑定与位置。合并建议：A 线可直接 rebase 开 PR；B 线终审后再开。
