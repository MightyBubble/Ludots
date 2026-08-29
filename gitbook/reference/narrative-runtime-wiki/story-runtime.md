# Story · Dialogue · Sequencer：剧情与演出

多页对话、脚本时间轴、剧情线——这三件套负责"讲给玩家听"。完整架构文档在 [Story Runtime：Dialogue / Sequencer](../../architecture/story-runtime-dialogue-sequencer.md)，本页只钉它们和 Activity / Task 的分工，以及从哪跑起来。

## 分工

| 件 | 管什么 | 不该塞什么 |
|---|---|---|
| Story | 剧情线编排（哪段剧情何时开启） | 拍板逻辑（那是 Activity） |
| Dialogue | 多页对话（说话人、分支、选项文案） | 长期进度（那是 Task） |
| Sequencer | 演出时间轴（镜头、节奏、音画调度） | 玩法判定（各玩法域自己的事） |

对话选项和活动选项形似实异：对话选项推进对话流，活动选项当场结算世界状态。写配置前先问"选完要不要改世界"——要，就写成 Activity。

## 配置路径与存档

- 声明：`Story/lines.json`、`Dialogue/dialogues.json`、`Sequencer/sequences.json`（ArrayById）；
- 加载期有 `LegacyNarrativeConfigGuard` 拒绝旧版叙事配置混入；
- 存档 domain：`dialogue` / `sequencer`（见[通用存档系统](../../architecture/save-system.md)）。

## 入口与验收

| 项 | 值 |
|---|---|
| 总装 showcase | `narrative` / `narrative_frontend`（registry） |
| 主题换肤 | `narrative_theme_sanguo` / `narrative_theme_fantasy` / `narrative_theme_acnh` |
| 验收 | `NarrativeShowcasePlayableAcceptanceTests` |
