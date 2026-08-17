# gr-op-12 · 节点：放置校验

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-12-placement.md)；编辑器需求见 [UXD](../uxd/gr-op-12-placement.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-12-placement.md)；editor spec 见 [editor spec](../spec-editor/gr-op-12-placement.md)；现状见 [reference](../reference/gr-op-12-placement.md)。

## 1. 定位

下点位与落点的形状校正四件：把落点拉回射程内、判点是否在圈内、吸到集合最近成员、吸到路网最近边。订单校验图与放置流的前哨。

## 2. 产品承诺

- **拉回射程**：ClampTargetToRange 按施法者与射程把击落点原地拉回可达范围，返回是否发生了拉回。
- **圈内判定**：IsPointInCircle 出 Bool，不改任何状态。
- **两种吸附**：SnapToNearestInCollection 吸到集合里最近实体并带有效输出口；SnapToNearestGraphEdge 吸到图边最近点，返回是否吸附成功。
- **线性四类通用**：四件在 Effect/Score/Validation/Derived 图可用——放行判定与放置动作同一族节点。

## 3. 运行行为

Clamp 与两种吸附直接修改击落点（TargetPos），是本卷少数有副作用的校验件；IsPointInCircle 纯判定。吸附查询分别走集合登记表与图边投影查询。

## 4. 异常承诺

集合键未注册、引脚类型不符——编译失败。吸附无候选——不报错：集合吸附出无效句柄（有效口为假），边吸附返回假。

**相关文档**：[配置说明](../config/gr-op-12-placement.md) · [ord-05](ord-05-input-protocol.md) · [gr-op-01](gr-op-01-context.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
