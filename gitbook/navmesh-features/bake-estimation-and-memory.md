# 烘焙估算、校准与内存预算

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)

## 1. 概述

Estimate 让作者在 full、dirty 或 window 烘焙前知道目标瓦片、操作量、内存和时间区间。它是计划和容量检查，不是性能承诺；真实耗时必须来自当前机器和同一输入的校准记录。

## 2. 结构

```text
board/source/profile/layer/obstacle snapshot
       -> target tiles + operation estimate
       -> memory upper bound + calibrated time band
       -> explicit bake gate
```

## 3. 详情

### 3.1 编辑器功能

- Estimate 面板显示 full/dirty/window、层数、profile 数、目标瓦片、并行度、内存上界和 p50/p90 时间区间。
- 估算与 Bake 使用同一输入 hash；源或 profile 改变后旧估算失效。
- 大地图显示 chunk-window 读取方案和拒绝原因，不要求一次性 materialize 整张逻辑地形。

### 3.2 工具功能

- 操作量按目标 tile × layer × profile 计算；障碍数量、顶点数、地形复杂度作为报告维度。
- Recast cell size、height、climb、slope 和算法能力进入 hash；并行度只影响墙钟时间，不改变几何。
- 校准记录绑定算法、profile 类、复杂度、机器和输入 hash；输入变更不能复用旧校准。

### 3.3 Runtime 功能

- runtime incremental 只使用显式 bounded tier、tile budget 和内存上限。
- 预算不足返回可观察状态；不偷偷降精度、不跳过障碍、不改成直线。
- 大世界使用窗口化 source 读取，避免把完整地形复制到单个数组。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

烘焙前先知道要花多少，超出能力时也能说清为什么。

### 主循环

作者在同一地图切换 full、dirty、window，比较目标瓦片和预算；完成一次真实 bake 后保存本机校准。惊喜时刻是移动一面墙只增加局部 tile，完整地图并没有重新计算。

### 消融对照

只改变并行度时操作量和产物 hash 不变，时间区间变化；只改变目标模式时目标 tile 和内存变化。超过上限时两边都明确拒绝。

### 解释层

显示 target mode、tile 数、layer/profile 乘数、估算内存、校准机器、p50/p90、runtime backlog 和拒绝原因。

### 旋钮清单

| 旋钮 | 范围 | 演示什么 |
|---|---|---|
| target mode | full / dirty / window | 工作量范围 |
| include neighbors | 开 / 关 | 脏区安全边界 |
| 并行度 | 合法 worker 档位 | 时间变化不改几何 |
| profile 数量 | 场景合法集合 | 几何产物乘数 |

### 场景结构

主演示局部脏烘焙；子场景是大图内存拒绝、校准前后和 runtime budget backlog。首屏：“先估算，再改一面墙，看影响范围是否只覆盖必要瓦片。”

### 门户资产

截图保留 estimate 报告和真实 bake 状态；预览读取地图、navmesh 和 runtime 配置。

### 反向 API 审计

需要统一 estimate report、calibration store、memory upper-bound calculator 和 Bridge progress API。现有 `NavBakeContext`、`NavBakeExecutionOptions` 和预算文档是复用入口。

## 5. 边界

估算不替代基准测试；校准不改变算法；内存预算不负责渲染缓存。runtime budget 与离线 full bake 的指标分开记录。

## 6. UAT

```gherkin
Feature: 作者能在烘焙前看见预算

  Scenario: dirty 烘焙只估算受影响区域
    Given 作者修改了一个结构障碍
    When 作者选择 dirty estimate
    Then 报告列出 dirty tile 和邻接 tile
    And 不把整张地图算成目标

  Scenario: 预算超限时停止
    Given 估算内存超过当前工具上限
    When 作者提交 Bake
    Then 工具在写入前拒绝任务
    And 页面显示超限原因和可调整的输入
```

## 7. 现状与 TODO

full/dirty/window 理论公式、Recast 派生参数和大图内存边界已有 `reference/nav-bake-budget-and-estimation.md`；统一 CLI/Bridge estimate report、本机校准持久化和真实进度状态仍未收口。旧 `.lhtm` 只能借鉴窗口读取思路，不能恢复为第二数据源。

| 责任层 | TODO | 完成判据 |
|---|---|---|
| 编辑器 | 同源 estimate/bake 报告 | 输入变化会使旧估算过期 |
| 工具 | 校准记录和内存上界实现 | 结果可复现、来源可追踪 |
| Runtime | bounded tier 与容量拒绝 | 不降级、不静默失败 |
| Showcase | full/dirty/window 真实对照 | 用户能理解成本差异 |
