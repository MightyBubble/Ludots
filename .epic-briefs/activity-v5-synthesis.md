# Activity v5 合流裁决（四路研究汇总）

输入：codex PDX 出题（CK3+EU4 6 案）、pi PDX 出题（Vic3+HOI4 6 案，含规格纠错）、codex 作用域研究、pi 投票语义研究。
本文是 v4 规格（`activity-v4-spec.md`）之上的增量裁决；冲突处以本文为准。

## 1. 对抗验证结论：12 案可表达性

| # | 案例 | v4 | +v5 增量后 | 残缺（需 §3 新增面） |
|---|---|---|---|---|
| 1 | CK3 月度脉冲候选 | 部分 | ✅ | MTTH 手搓图可表达；scope 级存储（G1） |
| 2 | CK3 条件即触发+链 | when 图 ✓ | ✅ | 链续弹（G5）；GAS 后果通路（G6） |
| 3 | CK3 风味弹层 | ✓ | ✅ | 呈现型 arrival + 资产字段（G7） |
| 4 | EU4 日历锚定 | 折算 tick | ✅ | 日历原语缺（G10，minor 可延） |
| 5 | EU4 MTTH 池 | pool+权重 ✓ | ✅ | 每国滚动需 fan-out+G1 |
| 6 | EU4 危机分阶段链 | 变量越线 ✓ | ✅+timeout | G2、G5、G6 |
| 7 | Vic3 日志条目+进度+到期默认 | ❌ | ✅+timeout(option:id) | **G1（进度的家）blocker** |
| 8 | Vic3 双国共享条目 | ❌ | ✅+scope shared | 撤销对方 pending 需 G2 |
| 9 | Vic3 双方事件+AI 拍板 | ❌ | ✅+vote(entities,unanimous) | **G9（AI 拍板）blocker** |
| 10 | HOI4 每国 MTTH 滚动 | ❌ | ✅+fan-out | G1 + 实体挂载订 map 事件限制（#1123） |
| 11 | HOI4 国策链+时限窗口+延迟 | ❌ | ✅+timeout(quorum_failure) | 延迟原语（G1+#1123 同根） |
| 12 | HOI4 全球新闻 | ❌ | ✅+scope global shared | G7 |

结论：**v4 + 本文件 §2 增量后，P社四家事件机制全部可数据驱动 + 图配置表达**；不可表达的部分全部收敛到 §3 的十个新增面，其中 G1/G2/G9 为结构性地基。

## 2. v5 schema 增量（两研究员草案直接采纳）

### 2.1 scope 块（codex 侧）

```jsonc
"scope": {
  "kind": "per_representative | per_team | per_faction | global",
  "instance_mode": "fan_out | shared",       // fan_out=每作用域一份；shared=一实例 N 访问者
  "key_domain": "representative | team | faction | map_global | application_global",
  "audience": "owner_seat | owner_player | team_members | faction_members | all_seats",
  "require_binding": true
}
```

- 准入键改 `(definitionId, scopeDomain, scopeKey)`——逻辑键（`map-01/player-2` 式），弃用 `Entity.Id` 直作键；global 禁用 0 键混装；
- `per_faction` 在引擎建立派系身份前**加载期拒绝**（不静默降级 team）；
- 四概念正交：业务作用域（scope）/ 执行上下文（ScopeHost 实体，图 Caster）/ 输入来源（seat）/ 呈现投递（audience）；
- `activity.confirm`/`activity.vote` 必带 `seatId`；服务端 seat→player→权限链推导，禁止前端自报实体 ID 当身份。

### 2.2 resolve.vote 块 + timeout（pi 侧）

```jsonc
"resolve": {
  "vote": {
    "voters": { "kind": "players | team | entities", "team_id?": 1, "entity_ids?": [], "exclude?": [] },
    "rule": "plurality | majority | weighted_majority | unanimous | chair",
    "weight": { "source": "flat | attribute", "attribute_key?": "diplomacy.clout",
                "missing": "zero | one", "snapshot_at": "open | live" },
    "quorum": { "mode": "none | all | min_votes | min_weight | percent", "value": 50 },
    "secret": false, "allow_change": true, "abstain": "allowed",
    "tie_break": "baseline | option:<id>",
    "settle_when": "rule_met | deadline | all_voted",
    "timeout": { "ticks": 600, "clock": "step",
                 "on_timeout": "count | quorum_failure | option:<id> | chair" }
  }
}
```

- vote 块 ⟹ 必须 shared instance（加载校验）；
- 平局默认 `baseline`（baseline 语义天然兼任僵局出口；投票活动**不豁免** baseline 强制）；
- 超时四态覆盖两家先例：Vic3 到期默认结算（option:id）、HOI4 到期静默关闭（quorum_failure）、EU4 到期收票（count）、房主（chair）；
- 新命令 `activity.vote {instanceId, optionId|abstain}`（confirm 对 vote 活动拒收，防双通道）；新 cue：`VoteCast/VoteChanged/VoteQuorumFailed/VoteResolved`；
- 计时复用 `IClock`（与 cooldown 同面），deadline 扫描挂现有排水编排，**禁新调度器/时钟域**；日历节拍走 MapHeartbeat→MapVariableChanged 自制（v4 §5）。

## 3. 引擎新增面总清单（v4"唯一新增面"订正）

v4 §1 清单经对抗验证**不完整且 §4 有一处事实错误**（TriggerGraph 也禁 ApplyEffectTemplate——`GraphKindOperationPolicy` 只放行 Pure，已实证）。真实清单：

| # | 新增面 | 级别 | 形态 |
|---|---|---|---|
| G1 | **scope 级可写存储** | 结构性地基 | 裁定三选一：a) 活动域 scope 状态组件+读写 op b) 放宽 WriteBlackboard 掩码 c) 任务实体承载。每国进度/冷却/MTTH 滚动状态的共同根 |
| G2 | **pending 实例结算/撤销 op** | 结构性地基 | `ResolveActivity`/`DismissActivity`（Trigger/Script）；timeout 的执行抓手；共享实例"一方了结另一方消失"的根 |
| G3 | scope 合同 + `OfferActivityScope` op | 结构性 | §2.1；复用 PlayerEntityLookup/TeamEntityLookup/MapSession，零新注册表 |
| G4 | 投票机器 | 结构性 | `ActivityBallotCm` 组件 + 规则求值纯函数 + deadline 扫描 + `activity.vote` 命令 + 4 cue |
| G5 | 链续弹通路 | 裁定 | `OfferActivity` 掩码放宽至 Script，或维持"结算写状态→事件轨→下一活动"绕行（合同本禁直连链，绕行符合单层纪律） |
| G6 | settle 的 GAS 后果通路 | 裁定 | `CreateEffect` op（入提案窗口，自动获得 Hook/Modify/Chain 交互）或 TriggerGraph 效果 op 掩码放宽；v4 §4 错误的正式修正 |
| G7 | 呈现型 arrival + 资产字段 | 配套 | `"arrival": "notice"`（呈现不决策）；picture/sound 引用字段 |
| G8 | 语义补订 | 文档 | when/show/enable 再评估时机（拟：面板每次快照重求值，when 仅准入时）；args 键集校验改可达性分析（短路分支不误杀共享图）；recur 增 `max_times` 档；WeightedPick salt 组合规则 |
| G9 | **AI 拍板** | 结构性 | 选项 `weight` 字段 + AI 消费者系统（消费 pending 实例按权重代答）；轨道已有（InputRequestQueue/Autocast 先例），缺消费者 |
| G10 | 日历原语 | minor 可延 | 日期比较 op 或日历 mapvar 约定；当前折算 tick |

## 4. 待用户裁定点（合并去重后 12 条，两研究员各留原编号于各自文档）

1. **G1 方案**：scope 存储三选一（倾向 a：活动域组件+op，职责最清晰）；
2. **G5 方案**：OfferActivity 放宽 vs 强制绕行（倾向放宽——绕行链路长且要 G1 配合）；
3. **G6 方案**：CreateEffect op vs 掩码放宽（倾向 CreateEffect——保住"Script 碰不到 GAS"的类型边界，过桥显式）；
4. 逐投票者求值 `enable_when` 的 Caster 语义（投票者实体 vs scope host，pi 裁定点 #8，改 v4 §3.1 合同）；
5. global 边界：MapSession 级 vs 应用级（倾向 map_global 先行）；
6. 代表实体死亡/转移后作用域冻结还是转移（倾向冻结逻辑键）；
7. 单机是否强制 seatId（倾向是——统一路径，单人也有唯一 seat）；
8. 密投揭示粒度与入档（倾向结算时 tally 汇总，明细不入档）；
9. `tie_break: "random"` 延后（需 per-instance 流键，先只支持 baseline/option）；
10. shared global 的 seat 可见投影权限模型；
11. MapSession 稳定 UUID（同图多实例并存的前置）；
12. faction 身份是否立项（不立项则 per_faction 持续拒绝）。

## 5. 执行排序建议（裁定后）

S0–S3 落盘提交（评审既定）→ **v4 基线重构**（provider 退出、图化、arrival/recur/when/settle 改名——不含本文件增量）→ **v5 增量分两批**：地基层（G1+G2+G3，解锁 12 案里的 7 案）→ 表决层（G4+G7+G8，解锁投票/新闻/时限）→ G9 AI 拍板单列后置。G5/G6 随地基批复即可并入。

## 6. 与 #1383 / #1384 / PR #1283 / PR #1382 的合流（2026-08-29 增补）

外部进展核验（origin/main 已 fetch）：
- **PR #1382 已合并**：`ModifyAttributeSet`（op 465，`EffectAndTriggerGraph` 掩码）——TriggerGraph 具名属性写，经 `AttributeMutationOps` 权威通道，镜像不变量例外清单收录；**Script 保持 Pure**（政策明文）。
- **PR #1283 OPEN 未合并**（head `cursor/calendar-chronology-75c2`）：世界历法层；#1384 是它的后续卡（P0=历法事件改走 `FireGlobalEvent`，否则地图触发听不到）。
- main 的 op 号已被占用：462=StartDialogue、463=SetInteractionMode、464=SetPanelAudience、465=ModifyAttributeSet。

对 §3 新增面清单的修订：
| # | 影响 |
|---|---|
| G1 | **方案 (b) 已被 main 具体化**：scope 状态 = scope host 实体属性，TriggerGraph 经 `ModifyAttributeSet` 写、Validation/Script 经 `LoadAttribute/LoadSelfAttribute` 读。settle Script 仍禁写（Pure 政策）——写路径走触发轨或 CreateTask，边界与"结算=纯协调"一致。G1 从"待裁三选一"降为"沿 main 既定政策落地" |
| G6 | `ModifyAttributeSet` 给出了**开洞的正典模式**：具名政策 + 权威通道 + 镜像不变量例外清单。`CreateEffect` op 若立项，照此模式开（Script 保持 Pure，效果提案是显式过桥） |
| G10 | 历法到位后大半消解：固定日期/节日/周期触发 = TriggerGraph 订阅 `Calendar.*` 事件族（**依赖 #1384 P0 修好抛法**）；日期**条件求值**仍缺读出口（依赖 #1384 P2 全局属性出口——注意那是他们的 G3，与本文件 G3 同名不同物） |
| 新增注记 | ① 本地未提交的 `OfferActivity=462` 与 main 的 `StartDialogue=462` **撞号**——S0 落盘前 rebase 并重编 enum；`CreateTask` 号段同步顺延。② main 已出现 `SetPanelAudience` op——audience 路由在 op 层已开工，v5 §2.1 的 audience 字段与其对齐。③ v5 G4 的 deadline 扫描系统挂载遵守 #1383 纪律：**有 vote/timeout 活动装载才 RegisterSystem**（Calendar 正典模式），不常挂空转；本线 `ActivityPresentationDrainSystem` 属 #1383 边界节明列的"drain 该常驻"类，合规不动 |

## 7. 域界重划（2026-08-29 增补）：Task=旅程，Activity=时刻

对抗映射复盘发现域界漂移：旅程形状内容（Vic3 日志条目、HOI4 国策、EU4 危机进度）曾被映射为 Activity。重划：

- **Task** = 跨周期追踪容器：进度/完成条件/阶段链/期限/追踪面板；
- **Activity** = 决策时刻机器：选项/表决/到期裁决/单层结算；
- **硬规则**：Activity schema 禁止长出进度/阶段/期限字段——想加即内容放错域；
- 桥：结算 CreateTask（既有）；旅程到达节点 → OfferActivity（缺，见 T2）。

对 §3 的修订：
- G1 再缩水：S1/S2/S3/S5 判归 Task 域；G1 仅剩调度簿记（MTTH 掷骰状态等，world anchor 属性即可）；
- G3 升格为 **Task/Activity 共享机器**（scope 合同 + fan-out + deadline 扫描模式两域同用，抽公共子机器而非两域各自长骨架）；
- 新增 T 组缺口（Task 侧补齐以承接旅程）：
  - T1 任务期限/到期语义（到期自动完成+默认效果；v5 timeout 的任务侧半张）；
  - T2 任务状态变化 → OfferActivity 的轨（现仅 C# 事件订阅，无图轨）；
  - T3 每国 task fan-out（与 G3 同机器）。

## 8. P社 SDK 一手调研合流（2026-08-29，详见 pdx-sdk-vs-v5.md）

四家官方 wiki 取证（Wayback/镜像，原文级）后对 §3/§4 的修订：
- **删除** MTTH 调度器设想（CK3/Vic3 已无 MTTH，EU4 disaster 自述替代之；blocker 以设计决策关闭）；
- **简化** 活动 timeout：单人到期=自动结算 baseline（HOI4/EU4 先例，显式标记避位置 bug）；四态 on_timeout 收窄进 vote 块；"到期静默收尾"判归任务侧（Vic3 JE timeout 先例）；
- **新增** 活动块 `immediate`（呈现前执行，动态文案/立绘）与 `after`（跨选项清理）——三家结构共识；
- **context_bindings 改案**：实例携带命名作用域快照表（发射/结算时 save、图按名读、resolved 即清）——三家同构，替代"定义级参数表"原思路；
- **G3 audience 对齐**：major/show_major/fire_for_sender/选项级 original_recipient_only；呈现策略 Vic3 式（默认图标入列表，popup 显式强弹不暂停）；
- **T1 字段表获得完整参照**（Vic3 JE schema 全套：possible/complete/fail/invalid/timeout/pulse×3/goal 固定/modifiers_while_active/weight/scripted_button）；
- **T2 确认**：完成发事件=完成效果里显式发（completion_reward/on_complete 模式），无引擎级完成事件；
- G2 维持（shared 实例撤销是四家未覆盖的我们增量需求）。
待裁定点净变动：#5（tie_break random）等维持；新增裁定：immediate/after 是否首批就做（倾向：immediate 首批、after 次批）。
