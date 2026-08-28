# Story Runtime：Dialogue / Sequencer

对应 Epic [#1083](https://github.com/MightyBubble/Ludots/issues/1083)。

**实现 SSOT**：[Story Runtime Dialogue/Sequencer 拆分合同](../../docs/architecture/story_runtime_dialogue_sequencer.md)

本页只做导航入口，不另写平行合同。

## 读前判断

- 玩家侧三种表达：可分支对话、角色头顶气泡、时间轴驱动的镜头+字幕
- 引擎侧拆成 Dialogue / Sequencer；台词与表现走 Story Line + Presentation Profile
- 条件 → Query Graph；副作用 → TriggerGraph；事实 → MapVariable / Blackboard；文案 → TextToken
- 旧 `Narrative/*` 配置与 `NarrativeDirector` 已退役；加载期 fail-closed

## 表现路由（世界 vs 屏幕）

| profileId | 锚点 |
|-----------|------|
| `story.dialogue_overlay` | 屏幕 Overlay |
| `story.world_bubble` | 说话者世界坐标 → `IScreenProjector` |
| `story.immersive_subtitle` | 屏幕字幕轨 |
| `story.standing_portrait` | 半屏立绘 |

## 表现前后端

- 运行时产出 `DialogueView` / `SequenceView`（已解析文案 + `imageId`）
- `StoryPresentationProjector` 投影为带 `StoryPresentationStreamHandle` 的字符串袋帧
- NarrativeFrontend 解析 `imageId`、套布局与眉题；不读 Graph / 下一节点

## 相关页面

- [关口口令：对话作者入门](dialogue-author-kit.md)
- [运行时总览](runtime-overview.md)
- [通用存档系统](save-system.md)（domain：`dialogue` / `sequencer`）
- 历史文档（已废止作 SSOT）：`docs/architecture/narrative_dialogue_cinematic.md`、`docs/architecture/narrative_frontend_kit.md`
