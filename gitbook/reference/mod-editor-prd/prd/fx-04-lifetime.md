# fx-03 · 生命周期与时长

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-04-lifetime.md)；编辑器需求见 [UXD](../uxd/fx-04-lifetime.md)；引擎实现见 [runtime spec](../spec-runtime/fx-04-lifetime.md)；编辑器实现见 [editor spec](../spec-editor/fx-04-lifetime.md)；现状见 [reference](../reference/fx-04-lifetime.md)。

## 1. 定位

寿命决定效果的物理形态：即时效果是同帧脉冲，限时效果是带倒计时的实体，无限效果是直到被移除的状态。

## 2. 产品承诺

- **三值精确**：Instant 同帧内联不落实体；After 存活 N tick 后过期；Infinite 永不自然过期，只能被移除。
- **duration 规则随寿命收窄**：Instant 禁带时长块；After 必带正时长；Infinite 可省，周期可独立存在。
- **周期首拍确定性错峰**：周期效果的首拍由确定性散列错开，大量同类效果不挤同一 tick；同配置同输入永远同首拍。
- **过期条件独立**：expireCondition 与时长解耦——到时求值、条件为真才过期，走过期再走移除。
- **时钟可指定**：效果在自己的时钟域里计量 tick；缺省固定帧时钟。

## 3. 运行行为

到期判定惰性计算；被取消的到期走移除相位而非过期相位；过期与移除各自发布展示事件。实体本地时钟以目标实体为准。

## 4. 异常承诺

Instant 带时长块、After 缺时长或时长非正、显式全零时长块、未注册时钟——启动失败并指明条目。

**相关文档**：[配置说明](../config/fx-04-lifetime.md) · [fx-01](fx-02-template.md) · [fx-04](fx-05-phases.md) · [rt-01](rt-01-clocks.md)
