# infra-03 · 视野与相机

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/infra-03-vision-camera.md)；编辑器需求见 [UXD](../uxd/infra-03-vision-camera.md)；引擎实现见 [runtime spec](../spec-runtime/infra-03-vision-camera.md)；编辑器实现见 [editor spec](../spec-editor/infra-03-vision-camera.md)；现状见 [reference](../reference/infra-03-vision-camera.md)。

## 1. 定位

两张表管"玩家能看见什么、从哪看"：战争迷雾按层声明（格子尺寸与更新频率），虚拟相机按预设声明（骨架类型、距离、俯仰、视场、平移/旋转/缩放/跟随的完整交互参数）。策略游戏的观感由这两张表定型。

## 2. 产品承诺

- **迷雾分层**：ground/air/detection 各自独立声明格子与频率——地面部队、空中单位、侦测位可以有不同分辨率的时间与空间粒度。
- **相机是档案不是代码**：每台虚拟相机是一个具名预设（rig 类型 + 一组边界 + 交互模式）；游戏按 id 切换，mod 可覆盖任何预设字段。
- **交互参数完备**：边缘平移、抓拖、旋转、滚轮缩放、跟随模式与目标类型、用户输入许可——预设里写什么，玩家就得到什么。
- **预设可组合复用**：七台底座预设（Moba/Rts/TopDown/Tactical/Default/TPS/FPS）是起点不是上限；mod 深合并只改要改的字段。

## 3. 运行行为

迷雾层按 updateHz 周期重算可见性格子；相机运行系统按当前预设求 rig 位姿，输入按预设的交互许可路由（边缘平移/滚轮/跟随）。

## 4. 异常承诺

层 id 缺失、cellSizeCm/updateHz 非正、相机 id 缺失或重复、rigKind 非法、边界区间倒置（min > max）、edgePanMarginPx ≤ 0——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/infra-03-vision-camera.md) · [fx-18](fx-19-vision.md) · [misc-03](misc-03-narrative.md)
