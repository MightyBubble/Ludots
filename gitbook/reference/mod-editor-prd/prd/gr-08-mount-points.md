# gr-07 · 挂接点总表

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-08-mount-points.md)；编辑器需求见 [UXD](../uxd/gr-08-mount-points.md)；引擎实现见 [runtime spec](../spec-runtime/gr-08-mount-points.md)；编辑器实现见 [editor spec](../spec-editor/gr-08-mount-points.md)；现状见 [reference](../reference/gr-08-mount-points.md)。

## 1. 定位

挂接点是图与游戏系统的八道门：每道门声明只收哪种 kind 的图；效果相位、相位监听、派生属性、能力前置、订单校验、AI 打分、BT 叶、HFSM。

## 2. 产品承诺

- **一挂点一 kind 合同**：挂接时终检 kind，不符即拒并说明该挂点只收什么。
- **效果相位按相位分家**：提案相位收 Validation 图，其余相位收 Effect 图；监听另受纯度闸。
- **挂起点声明在宿主**：BT 叶与 HFSM 挂 Script 图，是否允许挂起随宿主政策（gr-04/06）。
- **次要挂点同一套图**：关卡脚本、进度校验、表现规则、瞄准预览、查询物化复用同一种程序形态。

## 3. 运行行为

挂接点的消费时机各异（相位执行、属性聚合、激活前置、订单准入、打分评估、行为树遍历、状态机生命周期）；全部在装载完成后才可能触发。

## 4. 异常承诺

挂接的图未注册、kind 不符、空程序挂接——挂接失败并指明图与挂点。

**相关文档**：[配置说明](../config/gr-08-mount-points.md) · [gr-02](gr-03-kinds.md) · [gr-06](gr-07-actionlib.md) · [gr-08](gr-09-outputs.md)
