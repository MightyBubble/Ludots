Part of #1321（W1 拆分项之一）

## 目标

从 `RaylibHostLoop` 拆出输入路由模块：真实鼠标/键盘输入与 synthetic playback 走同一输入合同，UI 捕获语义（`UiCaptured`、`UiWheelCaptured`）保持不变。

## 验收标准

- Given 真实鼠标输入或 synthetic playback；When UI 命中、拖拽、滚轮或释放；Then `UiCaptured`、`UiWheelCaptured` 与世界输入结果保持现有语义；合成回放与真实输入使用同一输入合同。
- Given 截图与合成回放依赖的时序基准（frameIndex、输入 → Core Tick 顺序）；When 拆分；Then 作为显式参数传递，回放不错帧。
- 拆分后模块无 static 可变状态残留；既有 `RaylibUiCaptureRoutingTests` 全部通过。

## 依赖

前置：#1323。
