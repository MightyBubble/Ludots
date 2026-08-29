# P社四家 SDK 实况 vs 我们 v5——一手 wiki 对照报告

来源：四份调研简报（CK3/EU4/Vic3/HOI4 官方 wiki，经 Wayback/镜像取证，原文级字段名与出处 URL 见各简报存档于本对话）。本文只做裁决级对照，不复述细节。

## 一、四家共识（"PDX 模式"的骨架）

1. **事件永不自触发**：四家一致——全部经 on_action/pulse/effect/`trigger_event` 发射。≡ 我们的 TriggerGraph 发射轨。✓
2. **时刻/旅程两域分离，四家全验证**：CK3=事件 vs story_cycles（"事件链管理器+存值"）/decisions；EU4=事件 vs missions/disasters（进度表，且明言 disaster "replaced the previous MTTH event-based system"）；Vic3=事件 vs journal entries；HOI4=事件 vs focus（进度树）。≡ 我们的 Activity（时刻）/Task（旅程）重划。域界裁决第四次验证，结案。
3. **事件块结构三家共识**：`trigger / immediate（呈现前执行）/ option* / after（选完清理）`。我们有 when/option/settle，**缺 immediate 与 after**。
4. **选项三态**：option 级 `trigger`（隐藏）+ 灰显（CK3 `show_as_unavailable`；EU4/HOI4 无灰显只有隐藏）+ 正常；兜底：CK3 `fallback`、Vic3 `default_option`、EU4/HOI4 靠位置（第一项）——EU4 wiki 记载位置约定有已知 bug。≡ 我们 show_when/enable_when/is_baseline（显式标记优于位置约定）。✓
5. **超时**：HOI4 `timeout_days`（默认 13，到期**自动选第一项**）；EU4 固定 4 个月自动选第一项；CK3 事件无超时；**Vic3 JE `timeout`（天）+ `on_timeout` 效果块**（可静默收尾/转完成/转失败/发事件）。
6. **AI 拍板**：`ai_chance` 家族，形状=基准权重+条件修正（CK3 base+add；EU4/HOI4 factor+modifier；Vic3 value+add）。HOI4 全 0→选第一项兜底。
7. **MTTH**：仅 EU4/HOI4 有（中位数语义、每 20 天掷骰）；**CK3/Vic3 事件已无 MTTH**——现代化方向=pulse + 加权抽取 + 机会门（CK3 `random_events` 的 `chance_to_happen`/`100 = 0` 空项）。
8. **上下文传递**：三家同构——**命名作用域快照随链携带**：CK3 `save_scope_as`（"saved scopes carry throughout an unbroken effect chain"）、EU4 `save_event_target_as`（链断即清，global 变体跨链）、Vic3 JE `immediate` 里的 `save_scope` 可被后续事件/本地化复用。
9. **发射与答题时限正交**：延迟（fire delay，在触发效果参数上）≠ timeout（在事件/条目定义上）。HOI4：延迟事件收件人已死 → backlog 冻结计时。
10. **表现层换皮**：HOI4 `news_event`="purely a graphical reskin"；Vic3 事件默认**不弹**（outliner 图标点击打开），`popup = yes` 强弹且不暂停，`duration` 控制留存。

## 二、对我们的裁决修正

| # | 修正 | 依据 |
|---|---|---|
| C1 | **活动 timeout 简化**：单人决策活动的到期行为定为"自动结算 baseline"（对齐 HOI4/EU4 的第一项语义，但用显式 baseline 避位置 bug）；pi 四态 `on_timeout` **收窄给 vote 块**。"到期静默消失"在事件层无先例（pi 的 HOI4 案例是记忆错误），静默收尾是**任务侧**语义 → | HOI4/EU4 timeout 原文；Vic3 JE timeout 静默转换 |
| C2 | **任务侧 timeout 按 Vic3 JE 抄**：`timeout`（天）+ `on_timeout`（可静默/转完成/转失败/发事件）→ 直接进 T1 的字段表 | Vic3 Journal_modding 原文 |
| C3 | **不建 MTTH 调度器**（codex 的 blocker 以设计决策关闭）：现代 PDX 已弃用；表达=pulse 节拍 + 机会门 Validation + WeightedPick 加权 | CK3/Vic3 无 MTTH；EU4 disaster 自述替代 MTTH |
| C4 | **补两个事件块**：活动加 `immediate`（呈现前执行——动态文案/立绘选择，context_bindings 的呈现半张）与 `after`（选完执行的跨选项清理，与所选选项无关） | 三家结构共识 |
| C5 | **context_bindings 的设计答案**：实例携带**命名作用域快照表**（发射/结算时 `save_scope` 写入，when/settle 图按名读取，实例 resolved 即清）——CK3/EU4/Vic3 三家同构 | 三家 saved scope 语义 |
| C6 | **G3 audience 字段表对齐四家**：`major`（全员各一份、各自独立拍板/超时）+ `show_major`（条件可见）+ `fire_for_sender = no` + 选项级 `original_recipient_only`；呈现策略对齐 Vic3（默认图标入列表、`popup` 显式强弹） | EU4/HOI4/Vic3 原文 |
| C7 | **T1 任务字段表获得完整参照**（Vic3 JE）：`possible`/`is_shown_when_inactive`/`immediate`/`complete+on_complete`/`fail+on_fail`/`invalid+on_invalid`（静默清理）/`timeout+on_timeout`/`on_weekly|monthly|yearly_pulse`（内可发事件）/`current_value+goal_add_value`（**goal 激活时固定**）/`modifiers_while_active`（持续修正→GAS 效果模板）/`weight`（列表排序）/`scripted_button`（条目内决策按钮） | Vic3 Journal_modding |
| C8 | **T2 桥确认**：focus/JE 完成发事件都走 `completion_reward`/`on_complete` 显式发，无引擎级完成事件——我们的任务完成→OfferActivity 同构（效果里显式发） | HOI4/Vic3 |
| C9 | fan-out 节拍参照：EU4 pulse 按 tag order **错峰**（公式分摊负载）；HOI4 延迟+backlog 冻结 | EU4 On_Actions/HOI4 |

## 三、净效果（v5 增量清单变动）

- **删除**：MTTH 调度器（C3，blocker 以设计关闭）；
- **简化**：活动 timeout（C1，单人=自动选 baseline；四态仅 vote 用）；
- **新增小件**：`immediate`/`after` 两个活动块（C4）；命名作用域快照（C5，替代"定义级 context_bindings 参数"的原思路，更贴业界）；
- **T1 饱满**：Vic3 JE 字段表直接抄改（C7），任务域不再缺形状；
- G2（撤销/结算 op）不变——四家的 timeout 都不覆盖"对方了结时我这边撤销"（multiplayer shared 场景是我们的增量需求）；
- G4（vote）维持 pi 设计，`on_timeout` 四态留在 vote 内。

## 四、四家速查（一行一事实）

| 问题 | CK3 | EU4 | Vic3 | HOI4 |
|---|---|---|---|---|
| 事件自触发？ | 无 | MTTH/轮询 | 无（必外触） | MTTH/轮询 |
| 调度 | on_action pulse 家族（无 monthly） | pulse+MTTH | on_action | on_action |
| 随机 | random_events 加权+空项 | random_events 权重 | random_events | random_events |
| 选项灰显 | show_as_unavailable | 无（只隐藏） | 无 | 无 |
| 兜底 | fallback=yes | 第一项 | default_option=yes | 第一项 |
| AI | base+add | factor+modifier | value+add | MTTH 块+全0选第一 |
| 事件超时 | 无 | 4个月自动选第一 | 无（duration 留存） | timeout_days 默认13自动选第一 |
| 旅程超时 | — | — | JE timeout+on_timeout | — |
| 链 | trigger_event+days 区间 | country_event{days,random} | trigger_event | 触发effect hours/days/random |
| 上下文 | save_scope_as 贯穿链 | save_event_target_as | save_scope（JE→事件/本地化） | — |
| 旅程系统 | story_cycles | missions/disasters | journal entries | focus |
| 表现 | theme/portrait 槽 | picture | event_image/guid_window | picture/news换皮 |
