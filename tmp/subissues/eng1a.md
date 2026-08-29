Part of #1321（W1，本 epic 最重的单一动作）

## 目标

`RaylibFrameRenderer` 长出水面双 pass、NavMesh overlay、诊断 HUD 能力后成为唯一生产执行路径；HostLoop 删除内联帧序；`BuildPassPlan` 被真实消费；声明顺序与执行顺序机器校验一致。

## 背景（证据见 epic）

- 帧序三份表达：HostLoop 内联真实序列（主循环单方法约 880 行）；`RaylibFrameRenderer.cs:26-44` 枚举缺水面/NavMesh；`BuildPassPlan`（`:204-272`）不被 `RenderFrame` 消费且生产零实例化。
- HostLoop 的水面反射/折射双 pass 与 NavMesh overlay 不在 FrameRenderer 的注入字段里——这是能力扩权，不是简单搬迁。

## 验收标准

- Given HostLoop 产生一帧渲染输入；When 执行 Raylib 帧；Then 水面、NavMesh、地形、实例、UI、截图顺序由一个执行模块完成；`RaylibFrameRenderer` 在生产路径被真实调用；不再存在未消费的独立 pass 计划。
- Given 所有可选开关组合；When 构建执行帧；Then 声明的 pass 顺序与真实执行顺序一致；缺 pass、重复 pass、UI 提前执行均测试失败。
- Given 既有确定性截图 / 回放 CI；When 重构后运行；Then 全部不回归。

## 依赖

前置：#1322（计量守卫）。阻塞 #<ENG-1b> #<ENG-1c> 与 W2 接线包。
