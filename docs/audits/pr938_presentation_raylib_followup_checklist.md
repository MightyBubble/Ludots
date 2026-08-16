# PR938 Presentation/Raylib 跟进清单（分层 checklist，含触发情景）

- 审计对象：PR938 合并提交 `3e405fd42c`（Presentation SSOT + Raylib 客户端升级，120 commits / 911 files）。
- 触发情景列均为实际验证结论（grep 生产调用点 / 现有内容使用面），不是推测；验证日期 2026-08-15。
- ✅ = 已在本地工作区修复并验证（构建 0 错误、RaylibAdapterTests 相关 15/15 通过），待随修复 PR 一起提交；❌ = 待决策/待修。
- 优先级判据：触发情景是否是"现有内容的日常行为"。分三档：**日常**（现有内容就会踩）、**误用**（内容作者写错才会踩）、**未来**（需要尚不存在的能力/内容才可能踩）。

## 一、Raylib 层（画面最终画成什么样）

| # | 问题 | 状态 | 代码位置 | 不修的用户影响 | 触发情景（已验证） |
|---|---|---|---|---|---|
| R1 | 海岛地图进图必崩（天空配色文件与代码要的字段不匹配） | ✅ 已修 | `RaylibSkyEnvironment.cs:329-330`；新文件 `src/Platforms/Desktop/sky_daynight.vs/.fs` | 进图即闪退 | 【日常】装载 visual atmosphere showcase 并进图即触发——修复前 100% 复现 |
| R2 | 有水的地图整帧不进调色 | ✅ 已修 | `RaylibHostLoop.cs:578-580, 783` | 水地图画面发灰 | 【日常】任何声明了水环境的地图（海岛 showcase） |
| R3 | 群体动画分组缓存只进不出 | ✅ 已修 | `RaylibPrimitiveRenderer.cs:689, 692-712, 95` | 长局越玩越卡 | 【日常】群体动画场景长时间游玩（颜色/动作帧组合持续变化时累积） |
| R4 | 截图验收工具引用旧目录 | ✅ 已修 | `tools/raylib_client_parity_acceptance/Program.cs:54` | 无法出验收截图 | 【日常】开发者运行该工具即触发 |
| R5 | 水面 4 项输入缺报错检查 | ✅ 已修 | `RaylibTerrainRenderer.cs:353-368` | 错误晚暴露 | 【误用】未来改 water 着色器时写漏 uniform |
| R6 | 超密地形直接闪退、不降画质 | ✅ 已修 | `RaylibVisualHeightmapRenderer.cs:855, 882-893, 1119-1136`；测试：`RaylibVisualHeightmapRendererTests.cs:112-130` | 那张地图谁都玩不了 | 【日常/编辑器】visual terrain editor 默认 257×257 数据块会超过 Raylib 16 位索引上限；现在 Raylib 只对渲染网格按 stride 降采样，底层高度数据不变 |
| R7 | 一套没人引用的贴花着色器进安装包 | ✅ 已清 | 删除 `src/Platforms/Desktop/decal_unlit.vs/.fs`；移除两个 csproj 复制条目 | 零影响，占体积 | 【无触发】全仓无加载方；按清理项删除，现役投影贴花仍使用 `decal_project.vs/.fs` |

## 二、Presentation runtime 层（Core：什么时候画、画哪个）

| # | 问题 | 状态 | 代码位置 | 不修的用户影响 | 触发情景（已验证） |
|---|---|---|---|---|---|
| P1 | 画面留鬼影：缓存清退只覆盖"正常删除"，"定义注销"路径不清缓存 | ❌ 观察项（降级） | 死分支：`StableDrawCache.cs:175-194`；正常清理点：`PresenterRuntimeSystem.cs:576`、`PresenterEmitSystem.cs:895-898, 1042-1045`；缺口：`PresenterEntityRuntime.cs:2733+`（Reconcile 的定义注销分支） | 删掉的建筑还画在屏幕上，重启才消失 | 【未来】需要"presenter 实体先存活、定义后注销"的时序。已验证：`ReloadConfigs` 只重载 AI/Narrative/Quests 不含 Presentation（`GameEngine.cs:581+`）；LiveSkillWorkbench 热改管线 0 处碰 presenter；`Unregister` 唯一生产调用点是加载期 `__delete`（`PresenterDefinitionConfigLoader.cs:79`）——**现有流程构造不出触发时序**，属未来编辑器/mod 热重载时代的潜伏债。考古：缺口由 `8f7570a4ae`（2026-04-20，T19 持久缓存改造）引入，原始设计（`942d077cd0`）投影即全量淘汰、无此问题 |
| P2 | 动画槽按实体编号索引、不校验"是否同一实体" | ❌ 防御缺口（降级） | `PresenterAnimatorStateBuffer.cs:10, 29-35, 67-80`；清退点：`PresenterRuntimeSystem.cs:535` | 新实体继承旧实体动画状态 | 【未来】已验证：**同图内"销毁→刷新"是安全的**——所有 runtime.Destroy 路径都带回调清槽（`PresenterRuntimeSystem.cs:535`）。剩余窗口 = 绕过 presenter 运行时的实体销毁（如未来切图直接清 world）+ 动画槽是引擎级服务跨场景存活；当前未发现绕行点，但也无测试锁住该约束 |
| P3 | 合批提交状态字典只增不减 | ❌ 待修 | `InstancedBatchSubmissionRuntime.cs:9` 附近；唯一回收点 `InstancedBatchEmissionSystem.cs:129-144` | 长局越玩越卡 | 【未来】已验证：现有全部 mod 的 presenters 配置**无一处使用合批绑定**（仅 LudotsCoreMod 的 game.json 有容量配置键）。触发需要未来出现"用合批 + 单局长玩"的内容 |
| P4 | 一帧内全局事件超额被无声丢弃（容量 256） | ❌ 择机 | `GlobalPresentationEventBuffer.cs:42-45` | 昼夜/天气/区域变化某件"没发生" | 【未来】全局事件仅 3 种（昼夜/天气/区域），常规每帧个位数；同帧 256+ 条需要极端玩法设计（如一 tick 内切换 256 个区域）。现有内容无此规模 |
| P5 | 挂骨骼部件的缩放设置被忽略 | ❌ 定案 | `PresenterGroundingUtility.cs:196-202` | 武器挂手上大小调不了 | 【未来】已验证：现有内容（含 schema 示例 mod）**零处使用骨骼挂点**。触发 = 未来内容用"挂点 + 自定义大小"组合时发现不生效 |
| P6 | 死代码/误导项打包 | ❌ 随手清 | 见下方明细 | 玩家零影响 | 【无触发】空方法 `PresenterEntityRuntime.cs:3483-3486`；冗余三元 `:4664`；恒 0 变量 `WorldHudToScreenSystem.cs:119,178,182`；死属性 `TerrainHeightSyncSystem.cs:51`；不可达计数 `PrimitiveDrawBuffer.cs:61-67`；慢合并 `InstancedBatchOperationBuffer.cs:204-214` + `InstancedBatchEmissionSystem.cs:183-195` |

## 三、配置结构层（内容与 mod 怎么声明）

| # | 问题 | 状态 | 代码位置 | 不修的用户影响 | 触发情景（已验证） |
|---|---|---|---|---|---|
| C1 | 同名资产静默融合/覆盖，撞名无警告 | ❌ 待修 | `ParticleVfxRegistry.cs:22-38`；`MeshAssetRegistry.cs:32`；`InstancedBatchAssetRegistry.cs:22` | 玩家看到非预期的贴图/模型且无人知晓 | 【误用，修正表述】已验证：资产加载是**按 id 深合并后单遍**（`MeshAssetConfigLoader.cs:38` 走 `MergeArrayByIdFromCatalog`），所以跨 mod 撞名不是"后者整体覆盖"，而是**字段级静默融合**——两个 mod 的同 id 条目各出一部分字段拼成一个，结果更隐蔽；同文件内重复 id 同理。触发 = 两个 mod 撞 id 或单文件重复 id |
| C2 | 第 17 个子部件静默消失（上限 16，加载期不校验、塞不进不报错） | ❌ 待修 | 上限：`PresenterChildren.cs:6, 13-26`；不查返回值：`PresenterEntityRuntime.cs:298`；修法落点：`PresenterDefinitionConfigLoader.cs` 加载期校验 | 部件挂多了直接不显示、无提示 | 【误用】现有 showcase 均未超 16 子件；触发 = 未来内容作者在一个定义上挂 ≥17 个子部件 |
| C3 | 地表自定义被锁死（档名/阈值/参数编号硬编码） | ❌ 待修 | `ChunkSurfaceBakeSystem.cs:326-349` | mod 作者只能用默认地表配置 | 【误用】触发 = mod 作者声明非 `"default_surface_lod"` 的档名，直接抛错。现有内容全用默认档 |
| C4 | 旧声音通道硬编码"循环+全音量"，配置格式无音量/循环字段 | ❌ 待修 | 硬编码：`PresenterAssetEmitRuntime.cs:388-405`；格式缺口：`BehaviorSlot.cs` 的 AssetBindingConfig | 用错通道的声音调不了音量 | 【未来】已验证：唯一使用者是 schema 示例 mod（`mods/fixtures/presenter_schema_reference`），正式 showcase 全走新通道。触发 = 未来内容照抄示例误用旧通道 |
| C5 | 参数编号反查名字时同号互相覆盖，诊断名字不确定 | ❌ 择机 | 编号重载：`WellKnownPresenterParamKeys.cs:13-123`；覆盖处：`PresenterParamKeyRegistry.cs:166-175` | 玩家零影响 | 【无触发，仅诊断误导】排查问题看日志/诊断输出时名字可能标错。补注释或让反查表带上下文即可 |

## 四、测试/验收基础设施（不属于任何一层，但影响所有层）

| # | 问题 | 状态 | 代码位置 | 不修的用户影响 | 触发情景（已验证） |
|---|---|---|---|---|---|
| T1 | 性能门禁实质失效（15ms→100ms，警告零阻断）+ PR938 新测试大多不进 CI | ❌ 立项 | 放宽点：`BehaviorTreeRuntimeTests.cs:74-97`、`FsmRuntimeTests.cs:104-150`、`LevelDirectorRuntimeTests.cs:49-53`、`GraphBehaviorPressureMatrixTests.cs:54-90`、`GraphBehaviorArenaAcceptanceTests.cs:98-101`、`GraphBehaviorShowcaseAcceptanceTestNames.cs:31,50`；CI 现状自述 `solution-verify.yml:163` | 游戏在后续更新里悄悄变卡，等玩家骂了才知道 | 【日常】任何未来 PR 的性能回归都不会红 CI——这是清单里唯一"不需要任何人做错任何事"就会触发的项 |
| T2 | "代码要什么 vs 着色器里有什么"无守卫测试 | ❌ 待补 | 合同：`RaylibSkyEnvironment.cs:362-408` | 同类进图必崩换个写法复发 | 【误用】未来任何人改着色器或改合同字段的 PR——R1 事故本可被它拦截 |

## 修正记录（相对初版清单）

1. **P1 降级为观察项**：初版按"热重载会触发"表述，经查运行时配置重载不含 Presentation、无任何绕行注销路径——现有产品流程构造不出触发时序。保留在清单里是因为修复窗口成本低（对账补一行），且编辑器 PRD（`docs/mod-editor-prd.html`）在路线图上。
2. **P2 降级为防御缺口**：初版暗示"高频刷怪可能撞上"，经查同图销毁路径全带清退回调，剩余风险仅在"绕过 presenter 运行时的销毁"这一尚不存在的路径。
3. **P3/P4/C4/P5 标注"未来"**：经 grep 现有内容使用面，合批绑定、骨骼挂点、旧声音通道均为零使用或仅示例 mod。
4. **C1 表述修正**：跨 mod 撞名不是"后写者胜"而是字段级静默融合（合并管线按 id 深合并），结果比覆盖更隐蔽。

## 建议处理顺序（按触发档位重排）

1. **日常档（现有内容就踩，必须管）**：T1（唯一的被动触发项）。
2. **误用档（内容作者会踩，防呆）**：C2、C1、R6、C3——全是"加载期校验/报警"类小改动。
3. **未来档（出现对应能力/内容时再修也来得及，先挂观察）**：P1、P2、P3、P4、C4、P5。建议各补一行注释或守卫测试锁住已知行为，等触发条件进入开发计划再实修。
4. **随手清**：P6、C5、R7。
