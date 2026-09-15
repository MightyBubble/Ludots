# HUD 生产者基准（重构前锁点）

- 提交：56964b3d74（含 revision 早退激活）
- 机器：开发机 Release 无头（1280×720 视口、默认相机、100 移动 agent、10K 全量在场）
- 基准测试：`MassNavigationHudGhostLifecycleTests.HudPipelineBaselineBenchmark`（60 样本中位数，16 帧预热）

## 四段中位数（ms/帧）

| sync | emit | proj | build | worldHud 条目 |
|---|---|---|---|---|
| 2.459 | 0.835 | 1.290 | 0.099 | 20000 |

## 实机参考（idle 全可见，timing 流）

frame=34.5ms / fps≈29：tick 16.0（sim 7.0 + presentation 9.0）、post 4.9（hudProj 4.5 @1920×1080 含地平线遮挡重建）、gpuSkinBuild 3.5、overlayPaint 2.0、endDraw 6~7.7、GC 230~270KB/帧零 gen0。

既有合同基准（10000 agents）：sync median=3.506 p95=5.975；emit median=2.821 p95=4.763；0 B/frame。

## 重构目标（HUD inline 专 lane）

presenter 人口 30K→10K；sync/emit 段显著缩减；worldHud 合同与配置面不变。每步以本基准 + 既有合同基准对照，回退即止。
