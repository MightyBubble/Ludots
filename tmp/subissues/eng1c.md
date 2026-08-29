Part of #1321（W1 拆分项之一）

## 目标

从 `RaylibHostLoop` 拆出证据采集（环境变量截图 / `IHostFrameCapture`）与诊断 HUD（点阵字形渲染）两个模块，时序基准显式传递。

## 背景（证据见 epic）

- 截图用 `runtimeStopwatch` 与 `frameIndex` 的精确相对时序卡在帧循环内（`RaylibHostLoop.cs:972-980`）；`IHostFrameCapture` 在 `OnFramePresented` 读取已呈现帧。时序基准搬散会让确定性取证静默失效——这是拆分里最容易被低估的破坏面。

## 验收标准

- Given `IHostFrameCapture` 请求与环境变量截图请求同时存在；When 一帧完成 `EndDrawing`；Then 两者读取同一已呈现帧；PNG 尺寸、可解码性、平坦度校验仍执行；截图路径失败直接报错不静默跳过。
- Given 输入 → Core Tick → Present → EndDrawing → 取证 链路；When 拆分；Then 顺序语义不变，确定性截图 CI 不回归。
- 诊断 HUD 拆出后画面输出与现状一致（截图对比）。

## 依赖

前置：#1323。
