# fx-21 · 位移

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-17-displacement.md)；编辑器需求见 [UXD](../uxd/fx-17-displacement.md)；引擎实现见 [runtime spec](../spec-runtime/fx-17-displacement.md)；editor spec 见 [editor spec](../spec-editor/fx-17-displacement.md)；现状见 [reference](../reference/fx-17-displacement.md)。

## 1. 定位

Displacement 效果把目标沿计算方向推移一段距离：距离与时长决定分段速度，方向由四种模式之一解析。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期加 displacement 块，且 displacement 块只属于 Displacement preset。
- **方向四式**：ToTarget、AwayFromSource、TowardSource、Fixed；非 Fixed 模式禁配 fixedDirectionDeg。
- **正数合同**：totalDistanceCm 与 totalDurationTicks 必须为正。
- **叠加即替换**：同一目标已有活跃位移时新段就地覆写旧的——一个目标至多一条活跃位移，绝不叠加、不并排。
- overrideNavigation 决定位移期间是否压制移动输入；位移属外部位移原子域，独占效果计划。

## 3. 运行行为

替换时旧段若压制过移动输入而新段不再压制，则撤销压制；写权窗口保持打开，时钟由位移系统在写权确认后刷新；上限约束单段位移而非连锁累计。

## 4. 异常承诺

非 Displacement preset 带块、非 Instant、非 Fixed 配固定方向、距离或时长非正——启动失败并指明字段。

**相关文档**：[配置说明](../config/fx-17-displacement.md) · @@fx6@@（外部原子独占律）
