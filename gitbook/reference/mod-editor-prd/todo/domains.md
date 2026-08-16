# TODO 域分册 · 表现/基建/其余域

> 卷 12-14（pres / infra / misc）写作中沉淀的治理项，编号 D 起，与 [backlog.md](backlog.md) 的 T 系列并行。条目模型同总账：`编号 | 严重度 | 问题（第一性）| 现状证据 | 方案建议 | 状态`。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| D1 | 中 | instanced_batches 是纯 latent 通道：表结构、加载器、事件键解析俱全，但全仓库无任何 JSON 行数据——作者无从抄样例，字段语义只剩代码可考 | InstancedBatchAssetConfigLoader.cs:16,58-84；assets/Presentation/instanced_batches.json 与全部 mods 均无行数据 | 补一个可启动的合批 showcase（大树/草丛批量摆放即可）；此前配置说明标注"教学骨架·无真实实例" | 待立项 |
| D2 | 中 | 时钟缺省值与实配脱节：Engine 时钟代码默认 50、实配 20；Physics2D 时钟代码默认 15、实配 60——排障者按缺省推演必错 | EngineClockConfig.cs:10；assets/Engine/clock.json；Physics2DClockConfig.cs:23；assets/Physics2D/clock.json | 缺省值收敛为与事实页同源的单一出处，或日志同时打印实配与缺省；编辑器设置页已设计"缺省 vs 实配"提示（infra-01 UXD） | 待立项 |
| D3 | 中 | 根表空占位族：UI 两张（command_deck/production_overview）与 Items 三张、Exchange、Narrative、Quests 根表全空 []——"根表仅占位、内容下沉 mod"是隐性约定，新作者会以为引擎默认不存在该域 | assets/UI/command_deck_profiles.json 等（`{"profiles":[]}` / `[]`）；真实内容全部在 showcase mods | 目录条目加"属主/占位"标注；启动对账与 T3 联动确认消费方；文档已在本手册各 config 层标注 | 待立项 |
| D4 | 低 | 手册总篇卷 12 的 pres-01 范围列曾写着 `presentation_behaviors`、`prefabs` 两表——两表不存在（前者内联于 presenters 的 behaviors 数组与 instanced_batches 的 behaviors 字段；后者被 MeshAssetConfigLoader 显式拒绝并指路 Presenter AssetBinding），目录与实际表脱节 | MeshAssetConfigLoader.cs:59-63；config_catalog.json 无此两表 | 总篇目录勘误：卷 12 范围列已更新为实际表；pres-01 六件按实际表书写 | 已修订 |
| D5 | 中 | insight_profiles 域归属分裂：目录条目在引擎 config_catalog 声明，loader 却是 mod 内实现（EntityInfoPanelsMod）——不装能力 mod 该表即无人认领，且加载时序依赖 mod 装载窗口 | assets/config_catalog.json:437；mods/capabilities/entityinfo/EntityInfoPanelsMod/Insight/EntityInsightProfileLoader.cs:13,31 | loader 收编引擎侧（域归引擎、mod 只供数据），或目录条目标注属主为能力 mod；文档已标注"随能力 mod 加载" | 待立项 |
