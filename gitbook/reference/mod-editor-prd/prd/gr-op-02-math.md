# gr-op-02 · 节点：数学与比较

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-02-math.md)；编辑器需求见 [UXD](../uxd/gr-op-02-math.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-02-math.md)；editor spec 见 [editor spec](../spec-editor/gr-op-02-math.md)；现状见 [reference](../reference/gr-op-02-math.md)。

## 1. 定位

图里的算术与判定：Float 四则与极值、钳制与取值变形、随机数、数值比较，加上 Int 加法与比较、实体等值判定和实体三目选择。一切公式与门条件的积木。

## 2. 产品承诺

- **Float 全家桶**：Add/Mul/Sub/Div/Min/Max 六个双目、Clamp/Abs/Neg 三个变形、RandomFloat01 一个随机源、CompareGtFloat 一个比较，全部输出 Float（比较出 Bool）。
- **Int 与实体各就各位**：AddInt、CompareLtInt、CompareEqInt 管整数；CompareEqEntity 判两个实体句柄是否同一；SelectEntity 按 Bool 条件二选一实体。
- **无隐式转换**：Int 与 Float 是两条值线，互相不自动转；比较与加减各走各的类型。
- **确定性边界**：除 RandomFloat01 外全部是纯函数；RandomFloat01 每次执行取新值。

## 3. 运行行为

双目节点读 a、b 两个值引脚各一次，结果写目的寄存器；Clamp 读 value/min/max；SelectEntity 读 condition 与两个实体。比较节点产出 Bool 值线，供 JumpIfFalse 与 SelectEntity 消费。

## 4. 异常承诺

引脚类型不符、必需引脚悬空——编译失败并指明节点与引脚。除法除零等数值域问题不构成图错误，结果按引擎浮点语义产出。

**相关文档**：[配置说明](../config/gr-op-02-math.md) · [gr-op-01](gr-op-01-context.md) · [gr-op-14](gr-op-14-control-flow.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
