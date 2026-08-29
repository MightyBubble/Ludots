Part of #1321（W0，全 epic 前置小包，零行为风险）

## 目标

raylib native 资源（Texture / Model / Mesh / Shader / RenderTexture2D）在创建与释放处登记 resident bytes 与计数，可查询、可诊断；为关键 benchmark 补 alloc / resident 阈值断言。不改任何运行行为，不改 GC 配置。

## 背景（证据见 epic）

- 全仓库无 `GC.AddMemoryPressure`、无进程/VRAM 计量；资源创建/释放散布在各 renderer 的 cache 与 Dispose（`RaylibPrimitiveRenderer.cs:2940-3013`、`RaylibTerrainRenderer.cs:533-563` 等）。
- 正确做法已在仓库存在：`PresenterTimerBenchmarkTests.cs:35` 的 0-alloc 硬断言、`artifacts/benchmarks/presentation-skia-hotpath/benchmark-report.md` 的 0.0 B/帧实测；缺口是没有阈值防线的 50k HUD 382.7 B/帧一类指标。

## 验收标准

- Given 模型/贴图/RT/shader 创建或释放；When 生命周期变化；Then 可查询当前 resident bytes、创建数、释放数；漏登记与重复登记可被测试发现。
- Given HUD 50k / skia hotpath / dynamic worker benchmark；When 跑基准；Then alloc 与 resident 指标超过显式阈值时 CI 失败，不是只写报告。
- Given 任意现有 showcase/验收路径；When 计量开启；Then 行为与渲染输出不变（截图证据链不回归）。

## 依赖

无前置。W1 重构开始前必须完成（先取证后动刀）。
