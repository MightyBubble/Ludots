# 事故复盘 · #1535 属性层来源（2026-10-04 回滚）

> 一个已被否决的设计（属性层存 per-attribute 来源槽）由 Cursor Agent 实现并合入 main，重复发明了项目初版就存在的 effect 域来源机制，撞爆热路径布局合同，带着红 verify 与零 review 存活 7 天，最终整体回滚（PR #1721）。本页是事故档案，附 6 月以来同类案件扫描结果，供所有 Agent 与人开工前对照。

## 1. 时间线

- 2026-02-24 初版提交 `279aec619d`：`EffectContext.Source`（src/Core/Gameplay/GAS/Components/EffectContext.cs:8）与 `GameplayEvent.Source`（src/Core/Gameplay/GAS/Components/GameplayEvent.cs:11）已存在——写入者供给从项目第一天就在 effect/事件层，且生产路径在用（AbilityExecSystem.cs:1345 `Source = actor`）。
- 2026-09-07 #1460 立单：属性变化事件缺来源（真实问题），正文给出方案 a（独立组件）/方案 b（DirtyFlags 内联槽），未裁决。
- 2026-09-15 `06e322087d`（Cursor Agent）按方案 b 实现；当日补丁 `d31228c729`。
- 2026-09-27 PR #1535 合入 main（`0cc88ce375`）：DirtyFlags 48B→816B，HotPath 布局测试转红。PR 自身 verify FAILURE、零 review、零评论；attr-06 四份合同副本被顺手改写为配合实现。
- 2026-09-27 → 10-04：main 的 PR verify 全红（红基线），期间 6 次合并全部 admin 越过。
- 2026-10-04 裁决：属性层不携带因果，写入者供给归 effect 域。#1460 关单（not planned）；#1720 开而复关（考古证明初版机制已覆盖需求）；PR #1721 整体回滚并记 attr-06 治理项 A16。verify 恢复绿。

## 2. 根因

三层，缺一不会成灾：

1. **规范执行缺失（直接原因）**：《AI 辅助开发规范》第一条"搜索已有能力，前三步不得跳过"被跳过。`git grep Source src/Core/Gameplay/GAS` 一次就能看到 EffectContext.Source 与 AbilityExecSystem 的真实来源发布。#1460 正文的方案 a/b 本身就偏离分层原则，实现时无人对照写权威正本（gitbook/architecture/attribute-write-authority.md 全文无来源概念）。
2. **审查空转（流程原因）**：PR 零 review、零评论；gas-composition-gate 自审未附。
3. **红基线（系统性原因）**：2026-08-23 起 main 存量测试失败积累（#1182：87 个起步），红着 verify 合并成为惯例。红灯常亮后，#1535 的新增红混入环境噪音，7 天无人问——这是它能藏住的土壤。

## 3. 6 月以来同类案件扫描

口径：revert/退役/推翻类提交逐条核对原始提交是否进过 main；6 月以来全部 merged PR 的 verify 结论比对；"新增后短命被替换"单独核对。

| 级别 | 案件 | 事实 | 与本案关系 |
|---|---|---|---|
| 同型·重大 | #1535 属性层来源 | 见时间线 | 唯一一例"重复发明 + 错层 + 带病合入 + 长期潜伏" |
| 同型·快速自纠 | ChoiceList 伴生面 | 08-26 随 #1222 建对话选项伴生面；PanelHost 与 CreatePanel op 已于 08-18（#1026）落地；08-30 迁 PanelHost 并退役（`28bde70e93`/`53306a5c98`），寿命 4 天 | 同为"不复用既有共享面、自建平行面"，4 天内自纠 |
| 带病合入·当天拔除 | RTS 初始选中播种 | 09-22 落 main 当天破坏存/读确定性，当天 revert（`05b3b34c66`），诊断留档 | 回归漏过门，自纠快 |
| churn | 名册墙 demo 多轮当日提交/revert（08-26，Cursor Agent）；Wiki Bot OfferActivity 小时级提交/自撤/隔日重落（08-30）；#1379 次日整 PR revert（08-30） | 均进过 main | 污染历史、抬高审计成本，非错层设计 |
| 健康对照 | `da0b0c88a9` skia 性能撤销（实测 textDraw 0.98→3.07ms，带数据）；hzxueqi 当天自撤两次（09-18）；#1570/#1597/#1600 等有裁定号的退役 | — | 有证据的自纠与正路重构，不属事故 |

系统性计数：6 月以来合并 PR 400+，其中 verify 红合并 **65 次**——6-7 月 0（solution-verify 工作流 06-21 起），8 月 20 次、9 月 41 次、10 月 4 次。09-27 红基线形成后一周仅 6 次合并：红灯没有挡住合并，只是让信号失效。

## 4. 教训与防再发

1. **复用清单是开工门票**。涉 Core 的 PR 必须在描述里列出复用项（规范 §4.2 已有，缺的是执行）。列不出来说明发现阶段没做完——直接打回。
2. **合同测试红了不许合**。新增红与存量红必须区分；存量红按 #1182 歼灭，不允许"反正一直红"。本案后 verify 已恢复绿，保持绿是硬要求——下一次红合并发生时，当场回滚或当场修，不许适应。
3. **合同文档不许随实现自改**。attr-06 四副本被 #1535 顺手改写。合同措辞的变更必须独立成提交并留裁决记录（A16 是事后补救）。
4. **issue 里的方案是提案不是裁决**。#1460 正文写了方案 a/b，实现者照单全收而未对照分层宪法。动手前先查正本有没有说不。
5. **OwnerOnlyWrites 不是质量门**。admin 合并只解决身份，不解决质量。红 verify + admin 合并 = 本案路径，封死它。

## 相关

- 回滚 PR：#1721（含 attr-06 A16 裁决与 gas-composition-gate 自审 artifact）
- 关单：#1460（否决留痕）、#1720（考古：初版机制已覆盖需求）
- 规范正本：[AI 辅助开发规范](ai-assisted-development.md) §1/§3；[属性写权威](../architecture/attribute-write-authority.md)
