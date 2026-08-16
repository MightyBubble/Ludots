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
