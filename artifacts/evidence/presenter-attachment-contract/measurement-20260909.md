# Presenter 挂接回归测量（2026-09-09）

## 范围

设计与预期行为见 [#1483](https://github.com/MightyBubble/Ludots/issues/1483)，实现见 [#1486](https://github.com/MightyBubble/Ludots/pull/1486)。本文件只记录测量与限制。

- 基线：`1bdc6ea5c8`，已合入主线 `ffd11358f7929544c9f1f193b96aeb1eb28616d3`。
- 修复：`e02e5f3ea2`。参数依赖改为复用数组，缓存启用中的挂接配置，并修复公开位置更新接口未传播到挂接子对象的问题。
- 主工作区及 47921 进程未改动。整机验证使用独立 47922 进程，按预设帧数自然退出。

## 功能回归

| 范围 | 结果 | 原始证据 |
| --- | --- | --- |
| Presenter 功能回归，排除规模测试及下述存量失败 | 642 通过 | [TRX](after/presenter-functional-final.trx) |
| 公开位置接口更新后，挂接 HUD 同帧移动 | 修复前失败，修复后包含在上述通过集合 | [修复前 TRX](before/position-setter-before.trx) |
| Blacksmith 配置及相关合同 | 6 通过 | [TRX](after/config-regressions.trx) |
| VfxForge 地图可见 VFX 数量 | 干净主线同样失败，期望至少 9，实际 0 | [主线 TRX](before/vfx-origin-main.trx) |

Raylib 应用与 AgentBridgeMod 的 Release 构建通过。VfxForge 失败未被静默放过，也未修改该场景配置来规避断言。

功能回归命令：

```powershell
dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Presenter&TestCategory!=benchmark&FullyQualifiedName!~PresenterAttachmentScaleTests&FullyQualifiedName!~MapLoad_WiresQuarksParticleAssetsIntoRaylibVfxPresenterPath' --logger 'trx;LogFileName=presenter-functional-final.trx' --results-directory artifacts/evidence/presenter-attachment-contract/after -v quiet
```

## 局部 CPU 测量

Release，关闭分层编译，预热 32 次、采样 65 次；1k / 5k / 10k 共 51 个用例。执行顺序 A1 → B1 → B2 → A2，每轮 51 个用例均通过，预热后的被测更新分配量均为 0。原始 CSV 包含中位数、P95、分配量与依赖访问数量。

| 10k 场景，中位数 ms | A1 基线 | B1 修复 | B2 修复 | A2 基线 |
| --- | ---: | ---: | ---: | ---: |
| 根对象移动，无子对象 | 1.5272 | 2.9534 | 1.3392 | 3.0537 |
| 根对象移动，每个带 3 个持续挂接子对象 | 14.4318 | 21.1571 | 9.3461 | 29.0909 |
| 修改相关参数，定义挂接行为 | 30.4250 | 15.9024 | 29.2321 | 44.1494 |
| 修改相关参数，实例挂接行为 | 56.7655 | 20.2807 | 27.0620 | 45.5887 |

对照组本身有约两倍波动，尚不能给出稳定加速倍数。不能用其中最有利的一轮代替交错测量结果。

原始结果分别位于 `before/alternating-a1/`、`after/alternating-b1/`、`after/alternating-b2/`、`before/alternating-a2/`；同名 TRX 位于各自上级目录。

参数用例含 60000 个依赖节点，每批相关参数更新访问 10000 个依赖；Once 和无关参数用例不访问持续挂接依赖。修复后依赖数组占 3670016 字节，不含字典和 ECS 存储。旧版仅节点内嵌的固定子列表就占 60000 × 388 = 23280000 字节，尚未计入对象头及字典。此比较只证明该存储部分缩小。

复现时分别检出上述两个提交，构建后单独运行：

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT = 'artifacts/evidence/presenter-attachment-contract/after/review-scale'
dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~PresenterAttachmentScaleTests' --logger 'trx;LogFileName=review-scale.trx' --results-directory artifacts/evidence/presenter-attachment-contract/after -v quiet
```

## 整机采样

入口 `launcher.mass-navigation-10k-hud.runtime.json`，1600×900，targetFps=0，AMD Radeon(TM) 8060S Graphics，OpenGL 3.3。场景包含持续健康 Effect、MassNavigation、贴地、Animator、10000 个 GpuSkinned 实例和 20000 个 HUD 项。报告 15 个蒙皮批次、30009 个 Presenter、约 10009 次贴地采样、1 个唯一姿势、每帧姿势纹理上传 376832 字节；HUD 缓冲无丢弃。

早期帧样本：

| 测量范围 | 耗时 |
| --- | ---: |
| 整帧 | 约 320–339 ms（约 3.0–3.2 FPS） |
| EndDrawing | 约 265–278 ms |
| Engine tick | 约 18–27 ms |
| Presentation，包含在 tick 内 | 约 17–20 ms |
| HUD 投影 | 约 14–27 ms |
| Animator | 约 1.1 ms |
| 变换同步 | 约 1.3–1.4 ms |
| 贴地同步 | 约 0.7 ms |
| 姿势构建 | 约 0.15 ms |
| 纹理上传 CPU 提交 | 约 0.05 ms |

上述区间有包含关系，不能相加。日志中的 MassNavigation 计时需核对与固定步的采样关系，不能直接与渲染帧计时求和。EndDrawing 不是 GPU 时间戳，尚未拆开 GPU 执行、驱动同步与窗口提交等待。

原始证据：[主采样](whole-machine/raylib-timing.log)、[启动日志](whole-machine/startup.log)、[运行会话](whole-machine/session.json)、[截图](whole-machine/orbit.png)。截图只能证明画面内容，不能证明镜头运动时文字无抖动。

关阴影诊断的整帧样本从约 1525 ms 降到 37 ms，随后约 62 ms，存在明显时间漂移，无法归因给阴影开关。原始证据：[关阴影计时](whole-machine/no-shadows/raylib-timing.log)、[启动日志](whole-machine/no-shadows/startup.log)。采样期间观察到前台进程曾为 Windows LockApp，之后变成 Ludots；此观察提示环境干扰，但不足以证明所有慢帧都由锁屏造成。需要桌面正常显示时重做受控 A/B。

启动环境：

```text
LUDOTS_AGENT_BRIDGE_PORT=47922
LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES=30
LUDOTS_RAYLIB_AUTO_ORBIT_DEG_PER_SEC=8
LUDOTS_AUTO_EXIT_FRAME=360
LUDOTS_TAKE_SCREENSHOT_FRAME=180
LUDOTS_RAYLIB_DIAGNOSTIC_PATH=<绝对输出目录>/raylib-timing.log
LUDOTS_TAKE_SCREENSHOT_PATH=<绝对输出目录>/orbit.png
```

关阴影诊断另设 `LUDOTS_RAYLIB_DRAW_SHADOWS=false`，自动退出帧数为 240。

## 尚未完成

稳定的整机 A/B、GPU 时间戳、运动中的 HUD 正确性、旧隐式挂接配置迁移、初始化与参数更新失败原子性、骨骼/贴地的同帧顺序及正式文档仍是合入前事项。当前数据不支持“10k 已恢复 60 FPS”。
