# TODO 总账 · 种子条目

> 写手册过程中沉淀的治理项。严重度：高（误导用户/数据错误）· 中（易用性/体系缺口）· 低（打磨）。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| T1 | 高 | 配置重载机制不可达：触发器已注册、实现完整、全仓零发射方——作者以为能热重载 | src/Core/Config/ReloadConfigTrigger.cs；GameEngine.cs 注册点 | 接线（工作台菜单发射）或显式移除；专文讲组语义 | issue #985 |
| T2 | 高 | 合并冲突只有 id 级溯源，字段级"谁覆盖了我"不可查 | ConfigConflictReport.cs:13-71 | 合并期记录字段级胜出（cfg-04 spec 任务） | 待立项 |
| T3 | 高 | 目录条目无消费方认领：死条目（登记了没人加载）静默存在 | ConfigCatalogLoader.cs 单向查询 | 加载器显式认领 + 启动对账（cfg-04 spec 任务） | 待立项 |
| T4 | 中 | tag 无声明面：mod 的 tag 公共 API 靠扫配置反推；撞名行为不一致（技能 last-wins、效果抛错） | TagRegistry 首现注册；#989 比较器分裂节 | `GAS/tags.json` 声明=接口面（不设闸门）+ 注册出处追踪；撞名策略统一 | 方案已定待立项 |
| T5 | 中 | tag 256 上限被 Effect.*/Cooldown.* 线性占用（正经游戏即爆） | 实测：单启动 <60；Effect.* 93 / Cooldown.* 58 全仓 | 效果身份 tag 出位图（优先）→ 512 扩容（备选）；编辑器用量预警已设计 | #989 评论 |
| T6 | 中 | 地图实体仅追加合并：难度修正无法改既有实例，只能加新的 | MapManager.cs MergeMapConfig AddRange | Entities 按 InstanceId 深合并（map-01 spec 治理项） | 待立项 |
| T7 | 中 | 触发器只能写代码：改一句剧情也要进 C# | TriggerTypes 反射装载（GameEngine.cs:2789-2800） | 声明式触发器（条件+动作组合，走效果/订单） | map-02 spec 治理项 |
| T8 | 中 | "纯读选 tag"节点空档：状态栏 curState 场景无一等节点，ADR 留了活口没人兑现 | ADR #876 决策表"可另单保留" | 重立 op：输入绑通用 tag 集/用户表，禁绑专表 | 待提案 |
| T9 | 中 | LSW 保存路径硬编码四类 GAS 常量，扩展表要改代码 | LiveEditModSaveService.cs:254-288 | 随配置根 SSOT 统一消费（cfg-04 spec 任务） | 待立项 |
| T10 | 中 | game.json 走管线特例：与目录体系并行两条合并路径 | ConfigPipeline.MergeGameConfig 专用入口 | 目录化收敛为 DeepObject 条目（cfg-06 spec 任务） | 待立项 |
| T11 | 中 | 启动计划无 dry-run：编辑器组合预览无法只算不写 | LauncherService 仅完整生成 | dry-run 入口（cfg-03 spec 任务） | 待立项 |
| T12 | 低 | priority 双语义：产品路径无效、仅调试平局——作者易误当排序用 | DependencyResolver.cs:82-136（本地回退） | 编辑器隐藏该字段或加"仅调试"锁；文档已注明 | 文档已覆盖 |
| T13 | 低 | facts 页无 CI 门禁：数字漂移要靠人跑脚本 | scripts/generate-prd-facts.py | CI 步骤：再生成 + git diff --exit-code | 待立项 |
| T14 | 低 | UXD 仅 cfg-01 为高保真样板，cfg-02…08 待升级 | uxd/ 目录 | 按样板逐篇补线框/控件数据源/交互流/状态 | 排期中 |
| T15 | 低 | graph-node-op-wiki 与手册节点族篇将双轨：删除节点时 wiki 死页（已发生一次） | 本次清理两页 | 生成 wiki 时以 GraphOps 枚举为准做孤儿检测 | 待立项 |
| T16 | 高 | 扩展属性三件套死链路：无生产者、分配的 id（10001+）进不了 64 槽 AttributeBuffer | ExtensionAttributeRegistry.cs:19,40-43；AttributeSchemaUpdateQueue.cs:16-28；AttributeBuffer.cs:72-81 | 接通（缓冲扩容或映射层）或移除；MAX_EXTENSION_ATTRS=1000 死常量一并清理（详见 todo/attribute.md A1） | 待立项 |
| T17 | 高 | 地图实体 spawn 项的 `position` 字段解析后零消费（放置实际只认 `overrides.WorldPositionCm`），作者写 `position` 得到的是模板默认位置；且官方样例 level_1.json 用的是已废弃的 `"Position": {"Value":…}` 覆盖键，样例在教错误写法 | MapConfig.cs:84（字段定义）；消费方只读 Template/InstanceId/Overrides/PresenterParamOverrides：MapLoader.cs:247-304、NavObstacleAuthoringAdapter.cs:39-60；样例：assets/Maps/level_1.json:10 | 删除 EntitySpawnData.Position 死字段；改写 level_1.json 为 `WorldPositionCm` 合同的可用样例；地图实体 schema 校验拒绝 `position` 键 | 待立项 |
| T18 | 高 | 地图片段类型错误（`teams` 写成字符串、枚举拼错）只打一条日志就静默丢弃整个片段，地图以缺失该 mod 全部内容的形态"成功"加载 | MapManager.cs:118-121（catch JsonException 后仅 Log.Error 继续循环）；片段来源 :107-122 | 地图片段反序列化失败 fail-fast（与其他 catalog 表对齐），至少累计"被丢弃片段"并使地图加载标记为失败 | 待立项 |
| T19 | 中 | mod.json 的 `main` 等字段类型错误（数字/null）被静默忽略，mod 被当作纯资产 mod 跳过代码加载；同文件 `priority`/`dependencies` 类型错误却抛异常——同一份清单两种校验严格度 | ModManifestJson.cs:69-72（main 非字符串静默跳过；64-67、101-114 同理）；后果：ModLoader.cs:385-388 | 清单字段类型不匹配一律抛 `Invalid mod.json ('main' must be string)`，与 priority 分支（:74-79）对齐 | 待立项 |
| T20 | 中 | 地图合并的 `structureAwareGrounding`/`structureAwareNavigation` 是单向粘滞布尔：基图或先加载 mod 置 true 后，子图显式写 `false` 无法关闭（合并只在 source 为 true 时赋值） | MapManager.cs:216-217 | 改为"片段出现即覆盖"（presence-based）或三态 nullable，让显式 false 可传播 | 待立项 |
| T21 | 中 | 作者可写 JSON 的严格度三套并存：game.json 与组件负载 strict（未知键抛错），DataRegistry（entities/templates 等全部表）与 MapManager 反序列化未设 Disallow——模板里拼错 `onSpawnEffect` 大小写会静默变成"无出生效果" | DataRegistry.cs:27（仅 CaseSensitive+IncludeFields）；MapManager.cs:108（仅 CaseInsensitive）；对照 strict：ConfigPipeline.cs:43、ComponentRegistry.cs:122 | DataRegistry/MapManager 统一改用 StrictJsonOptions（Disallow 未知成员），把"拼错键"从静默降级为启动期明确报错 | 待立项 |
| T22 | 中 | config_catalog 条目的 `AllowEmpty` 开关被解析、存储，但全仓库零读取：作者把某表从 true 改成 false（期望空表报错）不会有任何行为变化，是死配置旋钮 | 解析：ConfigCatalogLoader.cs:90-101；存储：ConfigCatalogEntry.cs:10；消费：全 src grep `AllowEmpty` 仅上述两文件 | 实现其语义（非 AllowEmpty 的表合并结果为空 → 抛错），或删除该字段避免假开关 | 待立项 |
| T23 | 中 | 地图继承（`parentId`）合并后返回的 MapConfig.Id 仍是父图 Id，子图实体全部以父图 id 注册进 MapLoadEntityIndex，而会话侧按请求的子图 id 查找——一旦使用继承，Teams/Players 绑定必然解析失败；当前无非空 ParentId 使用（潜伏雷） | MapManager.cs:175-177（finalConfig=parentConfig 后合并不修 Id）；消费错位：MapLoader.cs:139,212,302 vs ParticipantBindingResolver.cs:61 | 继承合并完成后强制 `finalConfig.Id = mapId`，并补一条继承链端到端测试 | 待立项 |
| T24 | 低 | 地图顶层 `dependencies` 与 `metadata` 被解析、被逐 mod 深合并，但运行时零消费（唯一"读者"是离线工具里复制的同一段合并代码）：作者写地图依赖期望影响加载顺序，实际什么都不发生 | MapConfig.cs:16,28；仅合并无消费：MapManager.cs:219-225,252-258；工具复制合并：NavObstacleAuthoringCatalog.cs:312-318 | 接入语义（按依赖驱动地图资产加载顺序），或从 MapConfig 删除这两个字段，避免"看起来有行为"的假面 | 待立项 |
