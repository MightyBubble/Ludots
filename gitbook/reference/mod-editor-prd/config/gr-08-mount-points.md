# gr-07 配置说明 · 挂接点总表

> 配置写法与行为。第一性需求见 [gr-07 PRD](../prd/gr-08-mount-points.md)；编辑器需求见 [UXD](../uxd/gr-08-mount-points.md)；现状见 [reference](../reference/gr-08-mount-points.md)。

## 1. 示例配置

图侧只写图本身；挂接由各域配置按名引用（如 action_lib 的 BT 条目，gr-06 第 1 节）。效果相位图的真实引用方式见 fx-05/fx-06；能力前置见 ab-05；AI 打分见 ai-02（待写篇，见 README 卷 5/6/9）。

## 2. 字段与行为

| 挂点 | 只收 kind | 消费时机 |
|---|---|---|
| 效果相位图 | OnPropose 相位收 Validation；其余相位收 Effect | 效果相位执行 |
| 相位监听 | 同上，另受纯度闸（gr-02 监听相容） | 相位事件监听 |
| 派生属性 | Derived | 属性聚合 |
| 能力前置 | Validation | 技能激活门 |
| 订单校验 | Validation | 订单准入 |
| AI 打分 | Score | 效用评估 |
| BT 叶 | Script（可挂起） | 行为树遍历 |
| HFSM | Script（不可挂起） | 状态机生命周期 |

次要挂点：关卡脚本（Script，步数预算更小且禁挂起）、进度校验（Validation）、表现规则（条件 Validation、参数 Score）、瞄准预览（Query，gr-08）、查询物化（Query，gr-08）。

## 3. 文件结构

挂接声明分散在各域配置（effects/abilities/order_types/AI/behavior_trees/hfsm 等）；图本体仍在 graphs.json。各域字段写法以各域篇为准，本篇不重复。

## 4. 运行时加载效果

装载顺序保证图先注册、挂接后解析（gr-00 第 4 节）；挂接点在运行期首次消费前完成 kind 终检（不符即拒，错误文案指明挂点只收的 kind）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 挂接的图未注册 | 挂接失败 |
| kind 与挂点不符 | 挂接失败，指明图、实际 kind、挂点只收的 kind |
| 空程序挂接 | 挂接失败 |
| 监听图违反纯度闸 | 装载失败（gr-02） |

## 6. 实例

- BT 叶动作集：`assets/GAS/action_lib.json`（gr-06 第 6 节）
- Score 图消费样本：`mods/showcases/utility_autocast/UtilityAutocastShowcaseMod`；容量背景见 [事实与取值表](../facts.md)（效果相位 scratch、图输出容量）

**相关文档**：[gr-07 PRD](../prd/gr-08-mount-points.md) · [gr-02 配置说明](gr-03-kinds.md) · [gr-06 配置说明](gr-07-actionlib.md)
