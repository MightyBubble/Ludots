# 评审请求：ViewMode 整套退役（PR #1730）

本文是给独立评审者（人类或 AI）的自包含审计+合并需求。你不被信任前提交者的任何自述——包括本文件里的声明，全部以仓库证据为准。前置提交者已跑过两轮内部对抗审计并修了 12 项，这些修复同样在审计范围内，不要因为"已经审过"而放过。

## 评审对象

- PR：#1730（`viewmode-retire` → `cursor/retire-stance-de53`）
- 评审提交：`305c135843`（单一提交，含全部内容）
- 基线：`caae8e2f48`（= PR #1713 的 HEAD；本 PR 堆叠其上，#1713 合入后改 base 到 main）
- 旧实现读取方式（被删文件的唯一真相）：
  - `git show 305c135843~1:mods/CoreInputMod/ViewMode/ViewModeManager.cs`
  - `git show 305c135843~1:mods/CoreInputMod/Systems/ViewModeSwitchSystem.cs`
  - 旧 viewmodes.json：`git show 305c135843~1:<mod>/assets/viewmodes.json`（8 份）

## PR 的主张

删除 ViewMode 体系（Manager/Runtime/Registrar/Loader/Config 共 526 行 + ViewModeSwitchSystem + 8 份 viewmodes.json），三根轴的去向：

1. **相机轴**：`VirtualCameraRequest` 直写或 `ActivateVirtualCamera` 图节点（op 530，#1713 已落）。共享预设包 CameraProfilesMod 回归纯资产（不携带按键、不注册系统）；模式键归各消费方 mod 自持（CameraShowcase F1-F4 / RoadNetwork F1,F3 / CameraAcceptance F1-F4 / CapStd F5-F8）。
2. **输入上下文轴**：CapStd 声明 `assets/Input/interaction_modes.json`（Control/Avatar 两模式），写 `InteractionMode` 组件，由 `InputContextProjectionSystem` 投影 IMC 栈；`Controls` 移出 game.json `startupInputContexts`。
3. **施法方式轴（自认过渡态）**：champion/interaction 直写 `ActiveInputOrderMapping.SetInteractionMode(CastModeType)`；等 #1713 后续片（这两家的下单图化）落地后应消亡。
4. 技能栏轴：保持 LocalOrderSource 配置写键路径不变。

## 必须重点攻击的面（按风险排序）

1. **复制客户端相位**：新加的模式键轮询系统全部注册在 `SystemGroup.LocalInput`，理由是 `GameEngine.UpdateReplicatedClientLocalInput`（约 :5246）只执行 LocalInput 组。请验证：(a) 这个相位事实；(b) 各轮询系统在 LocalInput 里读 `CoreServiceKeys.AuthoritativeInput`（冻结快照）不会早于快照冻结；(c) 它们写的副作用（VirtualCameraRequest / InteractionMode 组件 / SetInteractionMode）在复制客户端路径上会被正确消费。
2. **崩溃面**：集合跟随（EntityCollectionPrimary/Group）的 request 必须带 owner。修复集中在 `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceCameras.cs` 与 `mods/showcases/camera/CameraShowcaseMod/Runtime/CameraShowcaseCameras.cs`。请找漏网：还有谁发 request 而定义可能是集合跟随？（grep `VirtualCameraRequest` 全部写入点，对照 `virtual_cameras.json` 里 followTargetKind 为集合类的定义。）
3. **行为漂移**：旧 `ApplyViewMode` 的副作用序是 Pop/PushContext → 相机 request（含 FollowTargetKind/CollectionKey/Owner 覆盖）→ SetInteractionMode → 技能栏键 → ActiveModeIdKey。逐个消费方核对等价性，特别注意：跨 mod 切图后的施法模式残留（修复方案是 GetActive 返回 null 哨兵 + Ensure 复位，见 `ChampionSkillCastModes.cs` / `InteractionShowCastModes.cs`——验证这个复位链真的闭合）。
4. **键位归属纪律**：AGENTS.md「资产方不声明调用方」+ 设计文档（`artifacts/techdebt/2026-09-28-coreinputmod-retirement-design.md` §3.11.4）「没有公共默认按键」。请全仓 grep 确认：CameraProfilesMod 现在零按键零系统；不存在任何 mod 把自己的键注进别人的稳态上下文；CoreInputMod 的 default_input.json 里 ViewModeNext/ViewModePrev 已删。
5. **InteractionModeMap 首用风险**：CapStd 是全仓第一个 mod 侧 `interaction_modes.json` 消费者。已知问题（记录未修）：DeepObject 合并下数组整替，CapStd 片段重述了根的 mode.normal/mode.targeting——根演进时会静默吞。请评估：(a) 这个风险是否可接受为引擎侧债；(b) `InteractionMode` 组件是否走网络复制、CapStd 写组件在 host-with-seat 监听服上是否有复制后果。
6. **过渡态的诚实性**：施法方式轴直写 `InputOrderMappingSystem` 是不是在给一个 #720 要拆的聚合根续命？判断这个过渡是否合理，还是应该现在就图化。
7. **测试方法学**：本 PR 的验证口径是"与基线 caae8e2f48 红集逐名对齐、零新增红"。请独立复跑并判断这个口径是否成立（特别是：基线红是否真的全部预先存在、有没有本 PR 把绿跑成红又被"对齐"话术盖过去的情况）。

## 复现命令

```bash
git fetch origin
git worktree add D:/audit-pr1730 305c135843        # 评审树
git worktree add D:/audit-base caae8e2f48          # 基线树

# 各自构建后跑：
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj -c Debug -v minimal
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj -c Debug -v minimal
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --filter "FullyQualifiedName~CapabilityStandardVirtualCameraShowcaseAcceptance|FullyQualifiedName~ChampionSkillSandboxPlayableAcceptance|FullyQualifiedName~UxPrototype|FullyQualifiedName~InteractionShowcasePlayableAcceptance" -v minimal
```

预期（两树对照）：
- ThreeCTests：12 红 / 116 绿，两边红名单相同（一个测试改名：`...AndViewModeActions` → `...Actions`）。
- ArchitectureTests：5 红 / 388 绿，同名。
- GasTests 过滤集：基线与评审树各有 1-17 个红（InteractionShowcase_PlayableFlow、road/massnav 线），名单相同。
- CapStd 验收 7/7、champion 4/4、ux 5/5 绿。

## 已知事实与遗留（不替提交者遮掩）

- 基线自带的红（12+5+17）是 #1713 draft 分支的既有债：相机 showcase/acceptance 在该分支上因座位装配环境红（与 #1139 家族同源），road/massnav 线是 #1127 订单链断裂未修。**不属于本 PR，但意味着"默认状态镜头"在本线上没有可跑的绿验收**——机器级规则 5（验证必须包含默认状态）在这条线上无法满足，请评审时把这个缺口记在 #1713 头上而不是放行本 PR 时假装它不存在。
- 记录未修的两条：(1) interaction_modes.json 的 DeepObject 数组整替（上文第 5 点）；(2) 旧 ClearActiveMode 的技能栏标签离图清理无消费方接盘（champion/interaction 离图后标签残留，暴露面小）。
- mod.json 层面的 CoreInputMod 依赖（约 80 个 mod）按设计文档 §3.11.4 留给片 8 统一删，本 PR 只断了 csproj 死引用。

## 请回答的问题（合并判定）

1. 本 PR 是否可以合入（在 #1713 合入之后）？给出：可合 / 需补 X 后可合 / 不可合。
2. 第 1-6 攻击面里有没有真问题？逐条给结论和证据（file:line）。
3. 过渡态（施法方式轴直写 mapping）是否可接受为 #1713 后续片的前置，还是应当在本 PR 内图化？
4. 两条已知遗留的归属是否正确（引擎债 / 后续片），有没有应当本 PR 内修的？

## 索引

- 设计文档：`artifacts/techdebt/2026-09-28-coreinputmod-retirement-design.md`（§3.8 视角模式、§3.9 引擎节点、§3.11.4 按键归属）
- GAS 组合自审：`artifacts/gas-composition-gate-viewmode-retire.md`（含两轮审计修正记录）
- 相关：#1713（执行 PR，draft）、#1691（退役第一批）、#720（InputOrderMapping 拆解）、#1398（图化债务清单）、#1607（输入→下令图化，已合）
