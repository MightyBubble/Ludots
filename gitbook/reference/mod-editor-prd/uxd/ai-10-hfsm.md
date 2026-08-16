# ai-09 UXD · 层次状态机的编辑器需求

> ai-09 的编辑器需求（高保真规格）。第一性需求见 [ai-09 PRD](../prd/ai-10-hfsm.md)；配置写法见 [ai-09 配置说明](../config/ai-10-hfsm.md)；编辑器实现见 [editor spec](../spec-editor/ai-10-hfsm.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

HFSM 面板是相位图的工位：嵌套态画成层级框，转移画成箭头，当前相位实时点亮——"哨兵现在在想什么"一眼可见。

## 2. 布局线框

```text
┌─ HFSM 面板 ───────────────────────────────────────────────────────────┐
├─ 左：状态机清单 ─┬─ 中：层级画布 ──────────────┬─ 右：属性 ──────────┤
│ ▸ sentry (纯谓词) │ ┌root[Compound]──────────┐ │ 转移 alert→combat   │
│ ▸ sentry.scripted │ │ ┌idle●┐ ┌alerting────┐ │ │ predicate [Always▾] │
│ ＋新建            │ │ └─────┘ │ alert combat │ │ │ condition [alwaysTrue▾] │
│                   │ │         │ retreat◀─┐  │ │ │ priority [0]        │
│                   │ └─────────┴───────────┼─┘ │ │ 平局规则：后者胜 I8 │
│                   │   transitions 列表 ↕排序 │ │ 当前态：combat·第3波│
├─ 底部：Stimulus 探针 [Latch] · onEnter/onTick/onExit 调用计数 ─────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 状态机清单 | hfsm 合并视图 | 徽标：状态数/转移数/是否带生命周期 |
| 层级画布 | states 树（Compound 嵌套 Leaf） | 拖入改 children；多父/孤儿即时红 |
| kind/predicate 下拉 | Leaf/Compound 与三谓词（严格拼写） | I2 大小写提示 |
| defaultChild 选择器 | 同 Compound 的 children | 必选；空禁存 |
| 生命周期图选择器 | GraphActionCatalog host=Hfsm | 三槽独立；标"64 步禁 Yield" |
| condition 选择器 | 同上 | 可空 |
| 转移排序 | transitions 数组 | 拖动改声明序；平局提示"后者胜"（I8） |
| Stimulus 探针 | HfsmWorld LatchStimulus | 手动置位观察 StimulusLatched 转移 |

## 4. 关键交互流：给哨兵加"脱战回岗"转移

1. 画布选 retreat 态，属性加转移 → to=idle、predicate=Always。
2. transitions 列表查看同 from 平局序，若与既有转移同 priority，编辑器提示"后声明者胜"。
3. 需要更优先则调 priority；保存。
4. Stimulus 探针置位 → 单步观察 idle→alert→combat 的 LCA 收展与 onEnter/onExit 计数。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 结构非法 | Compound 缺 defaultChild/Leaf 带 children/多父/不可达 | 画布红 + 禁存 |
| 平局隐患 | 同 from 同 priority 多转移 | 黄条标注实际胜者（I8） |
| 谓词拼写 | 手输 | 红字（严格大小写） |
| 生命周期超预算 | 运行中未 halt | 调试面板红字 + 定位图 |

## 6. 易用性验收口径

- 层级画布与 transitions 数组双向同步，声明序可视为可见。
- 平局胜者在编辑期即被标注（不让 I8 变运行期惊喜）。
- LCA 收展（onExit 上爬/onEnter 下钻）在单步调试中可视化。

**相关文档**：[ai-09 PRD](../prd/ai-10-hfsm.md) · [editor spec](../spec-editor/ai-10-hfsm.md)
