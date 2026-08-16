# ab-05 · 激活门

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ab-05-activation-gates.md)；编辑器需求见 [UXD](../uxd/ab-05-activation-gates.md)；引擎实现见 [runtime spec](../spec-runtime/ab-05-activation-gates.md)；编辑器实现见 [editor spec](../spec-editor/ab-05-activation-gates.md)；现状见 [reference](../reference/ab-05-activation-gates.md)。

## 1. 定位

激活门是技能起播前的关卡序列：目标要活着、槽位要有效、tag 门要过、前置图要真、进度需求要满足——全部通过时间轴才获准起播。

## 2. 产品承诺

- **顺序固定**：存活 → 目标校验 → 槽位 → tag 门 → 前置图 → 进度需求，逐关拒绝、拒绝即起播失败并带原因。
- **tag 门语义唯一**：在场须含全部 requiredAll 且不含任何 blockedAny；无任何 tag 的单位只被空的 requiredAll 放行。
- **前置图可编程**：前置校验是一张 Validation 图，能读施法者、目标、目标坐标——复杂条件不进硬代码。
- **进度需求分两挡**：useRequirement 挡激活，showRequirement 只挡可见；已开开关的技能再激活意味着关闭——关不经过冷却与门。
- **门可等目标**：需要显式目标范围的进度需求，在首个条目是等待玩家目标的门时可等回填后再判。

## 3. 运行行为

两个入口共用同一批判定件：直接激活入口与订单起播入口；后者在顺序与延迟评估上有差异（见 spec），失败映射为可观察的施法失败原因。

## 4. 异常承诺

任一门拒绝即本次激活失败：tag 门、前置图、进度需求各有独立失败原因；槽位无效与黑板缺失属订单失败。

**相关文档**：[配置说明](../config/ab-05-activation-gates.md) · [ab-01](ab-01-definition.md) · [ab-04](ab-04-cooldown.md)
