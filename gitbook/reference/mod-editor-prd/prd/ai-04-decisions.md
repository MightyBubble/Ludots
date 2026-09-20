# ai-04 · 决策

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-04-decisions.md)；编辑器需求见 [UXD](../uxd/ai-04-decisions.md)；引擎实现见 [runtime spec](../spec-runtime/ai-04-decisions.md)；editor spec 见 [editor spec](../spec-editor/ai-04-decisions.md)；现状见 [reference](../reference/ai-04-decisions.md)。

## 1. 定位

decision 是效用 AI 的选择单元："对哪类目标、在什么条件下、想做哪件事"。它声明一个目标过滤器、一组考量（input×归一化×曲线×权重×聚合）、一组任务引用与节流参数——评估出分数后由决策者（ai-05）择优执行。

## 2. 产品承诺

- **考量链显式可读**：每条考量四件套必填引用（input/normalization/curve），Weight 与 Aggregate 控制入和方式。
- **四种聚合语义**：Multiply 入乘积、WeightedSum/PriorityBucket 入加权和、Veto 一票否决（curved≤0 整决策归零）；总分=(multiply+weighted)×Weight。
- **节流参数齐备**：Priority/BaseScore/Weight/MomentumBonus/MinDurationSteps/CooldownSteps + 技能绑定与共享冷却 tag。
- **任务引用是连续区间**：Tasks 必填≥1，且解析到编译任务表后必须连续——分片乱序会被拒。

## 3. 运行行为

评估时：TargetFilter 先筛候选；对每个候选按考量链算分（raw→norm→curve→聚合）；Veto 归零直接出局；通过就绪门后与当前最优比较（含 momentum 与 cooldown 状态）。选中后逐任务提交（ai-07）。

## 4. 异常承诺

TargetFilter 未定义、Considerations 引用未定义、Tasks 为空/不连续、SelectionMode 外的参数非法——启动失败并带 路径:id.字段。

**相关文档**：[配置说明](../config/ai-04-decisions.md) · [ai-03](ai-03-norm-curves.md) · [ai-05](ai-05-dm-profiles.md)
