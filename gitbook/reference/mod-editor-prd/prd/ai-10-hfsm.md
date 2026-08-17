# ai-11 · 层次状态机

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-10-hfsm.md)；编辑器需求见 [UXD](../uxd/ai-10-hfsm.md)；引擎实现见 [runtime spec](../spec-runtime/ai-10-hfsm.md)；editor spec 见 [editor spec](../spec-editor/ai-10-hfsm.md)；现状见 [reference](../reference/ai-10-hfsm.md)。

## 1. 定位

HFSM 是分层的状态化行为：Compound 态嵌 Leaf 态，转移带谓词与可选条件图，叶态可挂 onEnter/onTick/onExit 生命周期图。适合"哨兵在 idle/alert/combat/retreat 间切换"这类显式相位行为。

## 2. 产品承诺

- **结构即层级**：Compound 必须指 defaultChild、不得有 children 缺失；Leaf 不得有 children；禁多父禁不可达。
- **三种谓词 + 条件图**：Never/Always/StimulusLatched（触发后自动清零）；condition 图 ReturnInt≠0 判真。
- **生命周期随 LCA**：切换时从旧叶上爬到最近公共祖先逐层 onExit，再下钻到新叶逐层 onEnter；onTick 每波。
- **平局后定义者胜**：同 from 转移按 priority 降序取最优，priority 相同时**后声明者胜**（与直觉相反，I8）。

## 3. 运行行为

每波逐 agent：当前叶态向上逐层评估同 from 转移（谓词→条件图），选出最优；切换沿 LCA 收展并清 StimulusLatched。生命周期图在 64 步预算内禁 Yield，未 halt 报错；两指令+halt 有快路径，程序缓存 8 条。

## 4. 异常承诺

Compound 缺 defaultChild、Leaf 带 children、多父/不可达、predicate 非法（大小写敏感）、condition/onEnter 等图名未注册、生命周期图超预算未 halt——启动或运行失败并带状态机 id 与字段。

**相关文档**：[配置说明](../config/ai-10-hfsm.md) · [ai-10](ai-09-behavior-trees.md) · [ai-02](ai-01-utility-overview.md)
