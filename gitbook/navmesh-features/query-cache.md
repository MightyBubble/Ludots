# Detour 查询缓存

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

查询缓存复用已加载的 Detour query mesh，减少重复组装和分配。它只服务查询性能，不改变 NavTile 的权威性、路径语义或 Runtime 重烤职责。

## 2. 结构

NavTileStore 的加载集合/版本 → 共享 Detour mesh → 每次查询的工作缓冲。已有分支 DetourQueryMeshCache 和 LoadedVersion 可供提取；几何缓存与路径结果缓存分别处理几何版本和策略版本。

## 3. 详情

### 3.1 编辑器功能

- 性能面板显示 cache hit/miss、LoadedVersion、构建中数量和失效原因。
- 诊断可以按 board/layer/profile/tile 查看缓存键，不提供绕过 revision 的“强制命中”。
- 缓存压力和容量不足显示为性能状态，不能伪造成功路径。

### 3.2 工具功能

- 几何缓存绑定完整 store 身份与已加载瓦片集合版本；完整 store 身份由 map/board/layer/profile 与 build hash 确定，不能只取单个坐标作键。
- 工具压测复用正式 registry/store，不在 benchmark 中另造内存 tile。
- 缓存容量和查询工作缓冲由 Core 管理；烘焙 worker 并行度归运行时预算页，CLI 只展示正式指标。

### 3.3 Runtime 功能

- `DetourQueryMeshCache` 以 `LoadedVersion` 绑定 NavTileStore；同一 key 只允许一个构建任务。
- Replace、Unload 或已加载集合版本变化使旧几何缓存失效；源变化在重新发布前阻止过期路径执行。被丢弃的未发布烘焙任务不改变 store 版本。
- 查询网格构建失败保留失败原因；缓存与 store 版本不符时不得服务新查询。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

同一张地图反复寻路，不必反复组网。

面向地图作者和首次体验该能力的玩家。性能管线：重复查询、增加请求量、发布新瓦片，观察组网次数、命中率与失效。

### 主循环

0–15 秒：清空样例缓存后查询一次。15–35 秒：重复相同瓦片的不同目标，观察复用。35–60 秒：落门发布新瓦片后再查询。惊喜时刻：稳定查询不重建，地图一变就建立新版本且路径正确。

### 消融对照

样例中启用“每批显式清理缓存”的对照动作，与正常复用比较；两边都调用相同查询和校验。对照动作需 Core 管理资源生命周期，不复制算法。

### 解释层

命中绿、构建黄、失效紫；HUD 显示组网次数、LoadedVersion、查询延迟（ms）、分配（bytes/批）、缓冲容量和失效原因。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 请求批量 | 配置中的合法数量预设 | 观察重复组网成本 |
| 目标分布 | 同瓦片 / 跨瓦片 | 观察缓存覆盖 |
| 缓存对照 | 复用 / 每批清理 | 测量缓存收益 |
| 地图变更 | 门开 / 关 | 验证版本失效 |
| 查询并发 | 配置支持的档位 | 验证单飞与缓冲隔离 |

### 场景结构

主演示稳定查询与动态失效；子场景：并发冷 miss、容量不足、卸载。首屏：“先连续查询，再落门，观察组网次数和版本。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| store 级缓存及 LoadedVersion | Query Core | #1164 分支实现待整合 |
| 缓存指标和缓冲复用 | Query Core | 待稳定 API 与分配证据 |
| 对照清理 | 缓存生命周期 | 必须正式失效，不伪造 hit/miss |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

只缓存查询几何，不承担 worker 调度。area cost 变化复用几何；不同 Agent 的查询过滤器/代价表必须隔离，路径结果缓存包含 AgentTypeId 和策略版本，不只包含几何 profile。容量、并发缓冲与失效都归 Core；界面不提供关闭版本校验的开关。冷 miss 与稳定命中的分配分别测量。

## 6. UAT

```gherkin
Feature: 查询缓存服从瓦片版本

  Scenario: 相同版本命中缓存
    Given 同一 board、layer、profile 和 tile revision 已查询过一次
    When 玩家再次查询相同瓦片
    Then 面板显示 cache hit
    And 不重复构建 Detour query mesh

  Scenario: 瓦片替换后失效
    Given 玩家已经命中旧 revision
    When 新 NavTile 发布
    Then 旧缓存被标记失效
    And 下一次查询建立或等待新版本
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

当前 main 没有完整 `DetourQueryMeshCache`。PR #1164 的 `codex/nav-perf-query-cache-rebake-workers` 分支已实现缓存、LoadedVersion、单飞和 worker，但仍是 open draft，需按当前 `NavTileStore` 合同重整后再合入，并补零分配回归。

分支接收判断：origin/codex/nav-perf-query-cache-rebake-workers (0cecb99c01)，PR #1164 仍 open draft；分支已实现，主线未收口。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 真实缓存指标面板 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 使用正式 registry/store 的性能验收入口 | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 提取 #1164 缓存/单飞，补容量与缓冲隔离 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 冷/热查询分开测量，Replace/Unload 后正确失效 | 第 6 节行为通过，并满足统一运行验收要求 |
