# Runtime 烘焙预算与版本

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

本页只定义运行时重烤“在什么预算内执行、如何冻结输入、如何拒绝旧结果”。脏区发现和障碍足迹见 [Runtime 脏烘](runtime-dirty-rebake.md)；查询缓存见 [查询缓存](query-cache.md)。

## 2. 结构

不可变输入快照 → 有界 worker 队列 → 代次检查 → 主线程逐瓦片完整发布。提交预算、在途上限和发布预算应分别声明，不能把“每 tick 四块”误写成四块必在本 tick 完成。

## 3. 详情

### 3.1 编辑器功能

- Runtime 面板显示 tier、每个 fixed tick 的 tile 预算、worker 数、快照 revision 和当前 generation。
- Estimate 给出本次 dirty set 是否能在预算内完成；超预算时显示拆分或延期动作。
- 作者能看到“等待发布”“因 generation 过期而丢弃”“预算拒绝”等状态，不能只看到一个失败图标。

### 3.2 工具功能

- 离线工具计算完整质量的 estimate；运行时只接受显式 `bounded` tier 配置。
- 几何质量档位、体素尺寸和算法参数进入 build hash；worker 并行度、提交与发布预算单独记录，不让调度差异改变几何指纹。
- 每次 worker 使用不可变 source/semantic/obstacle snapshot；失败保留原因，不写半成品。

### 3.3 Runtime 功能

```text
snapshot → bounded worker bake → generation check → main-thread Replace
```

- worker 不直接写 `NavTileStore`；发布时检查 board、layer、profile、tile 和 generation。
- 新 dirty 事件到来时，旧 generation 的结果被丢弃并记录；revision 只在完整发布后递增。
- 预算耗尽返回可观察状态。旧瓦片可保留供诊断，但受结构变化影响的路线必须标为过期；执行层应停在不安全路段前，待新路径发布。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

连续变动的地图，也按预算稳定更新。

面向地图作者和首次体验该能力的玩家。管线能力：改变提交速度和障碍变化频率，观察积压、发布节奏和过期任务丢弃。

### 主循环

0–15 秒：启用低提交预算并连续移动墙体。15–35 秒：提高预算，观察积压减少。35–60 秒：在同一瓦片未完成时再次移动墙，观察旧代次被丢弃。惊喜时刻：较早完成的旧任务也无法把门恢复到旧位置。所有操作在 1 秒内反馈排队状态。

### 消融对照

冻结/恢复提交与发布泵，比较同一组障碍变更的积压和版本推进；安全检查始终开启。不给玩家提供关闭代次检查的生产开关。

### 解释层

HUD 显示提交/发布预算（tile/tick）、在途任务数、任务代次、瓦片版本、丢弃数、烘焙耗时（ms）和快照大小（bytes）。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 提交预算 | 配置声明的合法 tile/tick 预设 | 观察积压变化 |
| 发布预算 | 配置声明的合法 tile/tick 预设 | 观察主线程工作量 |
| 障碍变化频率 | 场景定义的慢 / 中 / 快 | 制造旧代次任务 |
| 处理开关 | 冻结 / 恢复 | 查看预算与等待 |
| 负载范围 | 单瓦片 / 跨瓦片 | 观察受影响规模 |

### 场景结构

主演示移动墙；子场景：快照冲突、同瓦片连续变动、容量拒绝。首屏：“把预算调低并移动墙，再提高预算观察队列。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 提交/发布预算和在途诊断 | Core queue | 待区分并配置 |
| worker pool 与代次 | Core queue | #1164 有实现，主线未收口 |
| 不可变 source/semantic 快照 | Core bake source | 待补完整输入边界 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

预算约束工作量，不保证毫秒完成时间。地形/语义/profile 在任务启动前冻结；主线仅对障碍提交做拷贝，其他输入有存活期不可变约定。代次负责丢弃过期结果，版本负责标识已发布数据，两者不混用。

## 6. UAT

```gherkin
Feature: Runtime 重烤遵守显式预算

  Scenario: 预算内发布
    Given 作者选择低预算运行档位
    When 玩家连续移动城门
    Then 面板持续显示等待更新的区域
    And 每次发布都能看到对应瓦片的新版本
    And 后台未完成时不会显示已全部更新

  Scenario: 旧 generation 不覆盖新结果
    Given 同一瓦片在重烤期间再次变脏
    When 旧 generation 完成
    Then 系统丢弃旧结果并显示原因
    And 新 generation 的结果才可以发布
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 9231f05fcf`。

主线有单后台 worker、障碍拷贝和主线程发布；地形/配置/profile 仍依赖队列存活期间不可变的约定。`origin/codex/nav-perf-query-cache-rebake-workers` / PR #1164 提供 worker、generation 和缓存实现，但仍是 open draft，未进入 main。runtime tier、预算报告和完整固定 tick 验收仍需收口。

分支接收判断：PR #1164 / 0cecb99c01 仍为 open draft；接收其 worker 和 superseded-generation guard，按主线 API 重整。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 预算、积压、丢弃原因面板 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 同源 runtime tier 估算和容量预检 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 提取 #1164 worker/代次，补快照及预算合同 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 旧代次不覆盖、容量拒绝和路径一致性 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/GasTests/Map/NavGateShowcaseContractTests.cs` 的 `NavGate_FreezeAblation_RevisionStalls_PathStillCrossesGate` 仅覆盖冻结/恢复与版本停滞，不覆盖完整 worker/代次/预算目标。本页目标合同在主线尚无完整独立验收。
