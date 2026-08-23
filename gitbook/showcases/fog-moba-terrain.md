# 战争迷雾 MOBA 地形 showcase 设计
## 一句话与目标用户
用熟悉的三路、河道和草丛，移动观察者看见视锥、墙体遮挡与离开视野后的最后知识如何实时变化。

## 主循环
玩家用 WASD 移动观察者，方向键改变朝向；视锥随位置和朝向更新，墙体遮挡视线，草丛把普通视野变成隐藏区域。离开区域后，已探索地面保留而实体知识进入 last-known 状态。每次进入河道草丛都会把遮挡和 concealment 的差异同时呈现出来。

## 消融对照
按 `F` 在真实规则开关与消融模式间切换：消融模式关闭墙体和草丛规则，但保留同一个观察者、半径和地图。场景不变，只有 Core 视觉结果改变。

## 解释层
HUD 读取运行时快照，显示观察者坐标、视野形状、范围、可见/已探索/未探索单元格计数、墙体与草丛规则状态，以及当前模式。颜色图例：亮青为可见，蓝灰为已探索，深色为未知，橙色为墙体，绿色为草丛。

## 旋钮清单
| 旋钮 | 操作 | 回答的问题 |
| --- | --- | --- |
| 视野形状 | `V` | 圆形视野和视锥是否产生不同结果？ |
| 视野范围 | `R` | 更远的观察距离会覆盖多少区域？ |
| 规则消融 | `F` | 墙体和草丛规则是否真的参与结果？ |
| 记忆层 | `M` | 离开视野后仍保留什么知识？ |
| 观察者方向 | 方向键 | 朝向是否改变视锥覆盖？ |

## 场景结构
主演示是一张三路、中央河道、两侧草丛和高墙的对称地图。观察者出生在中路入口，三个可识别目标分别位于墙后、草丛和河道。首屏提示“WASD 移动，方向键转向，按 V/F/M/R 改变规则并观察颜色变化”。

## 门户资产
文档、注册表、launcher preset 和验收产物均指向同一 showcase 配置。实机截图只在 Agent Bridge 连接到 `fog_moba_terrain_showcase` 后生成，预览不复制地图参数。

## 反向 API 审计
本次复用 `VisionSystem`、`FogCellMap`、`FogFieldStore`、`FogGlobalFieldVisualProjector`、`GlobalFieldVisualBuffer` 和 `RaylibFieldRenderPresenter`。HUD 使用新 runtime 的只读快照；没有新增第二套可见性算法。若后续需要实体级渲染裁剪，应把裁剪接口归入 Presentation/Knowledge，而不是 showcase。

## 交付边界与完成判据
本次交付包含真实 Mod、地图、Raylib preset、输入、HUD、Cucumber UAT 和 Agent Bridge 证据。完成判据是：干净启动进入该地图；WASD、方向键和四个旋钮可用；HUD 数值随真实 Core 状态变化；Agent Bridge 的 health、session、输入前后查询和截图均指向该地图；相关 build/test 通过。
