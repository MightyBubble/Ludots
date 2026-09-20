# NavTile 产物与 Manifest

> [功能目录](README.md) · [体系总合同](../navmesh-ssot.md)
>
> 第 1–6 节定义目标产品与设计验收；当前可用范围、分支成果和剩余工作只看第 7 节。本文不新增第二套数据或运行管线。

## 1. 概述

本页定义 NavMesh 如何保存、加载和拒绝过期产物。`.ntil` 是唯一权威瓦片格式，Manifest 负责说明它属于哪张地图、哪块 board、哪一层、哪种 profile 以及哪次源输入。

证据入口：`src/Core/Navigation/NavMesh/NavTileBinary.cs`、`src/Core/Navigation/NavMesh/NavTileStore.cs`、`src/Core/Navigation/NavMesh/NavAssetPaths.cs`。

## 2. 结构

NavTileBinary 序列化 .ntil；Manifest 标识地图/板/层/profile/瓦片、源指纹、build hash 和格式版本。加载服务先核验清单，再将完整瓦片发布到 NavTileStore。

## 3. 详情

### 3.1 编辑器功能

- Artifact 面板显示 map、board、layer、profile、tile、format version、source revision、build hash 和 tile revision。
- 保存后可以执行冷启动检查；编辑器重新读 `.ntil` 和 Manifest，不读取内存副本。
- 过期、缺失、格式不兼容和 identity 冲突显示不同状态与修复动作。

### 3.2 工具功能

- bake 一次写入 `.ntil`、Manifest 和可选的派生显示索引；不写第二种 query payload。
- Manifest 包含 tile identity、source revision、build hash、几何质量档位、format version 和 tile 持久版本。写入时间仅用于记录，不影响内容指纹。
- 旧导航格式拒载并给出重烘焙动作；地形源的 `.vhtm/.vtxm` 名称迁移遵循源适配规则，二者不是同一种产物。

### 3.3 Runtime 功能

- 加载服务先校验 Manifest 的 identity、格式和源指纹，再将完整瓦片发布到 `NavTileStore` 并接通 query/presentation。
- 冷启动加载后查询结果必须携带 artifact revision；Replace/Unload 时通知缓存和显示。
- 任一校验失败都返回明确错误，不能用空 tile、平地或另一个 board 的产物补位。

## 4. 场景与 Showcase 设计

### 一句话与目标用户

关掉编辑器，重开后仍能继续寻路。

面向地图作者和首次体验该能力的玩家。持久化能力：保存产物、关闭进程、重新加载后仍能操作；坏产物得到可定位拒载原因。

### 主循环

0–15 秒：烘焙保存并记录路径/产物指纹。15–35 秒：通过启动器关闭并重启地图。35–60 秒：从磁盘读取清单，再发相同命令继续行军。惊喜时刻是冷启动后仍走同一条路，无需编辑器保留内存。

### 消融对照

选择当前完整产物与已隔离的过期/缺失测试副本做加载对照；版本校验始终开启，失败不自动改用另一个版本。

### 解释层

持久产物绿色、过期橙色、缺失灰色；HUD 显示磁盘路径、格式版本、源指纹、tile 持久版本与当前进程发布计数。

### 旋钮清单

以下为目标演示中可在同一会话操作的控件；取值由场景配置或正式 API 约束。需要重烘焙的几何变更明确展示任务过程，不能伪装成立即生效。

| 运行中操作 | 范围 | 玩家能看懂什么 |
|---|---|---|
| 板/层/profile | Manifest 中的有效组合 | 查看不同产物 |
| 选中瓦片 | 清单条目 | 查看 identity 与来源 |
| 加载样例 | 有效 / 过期 / 缺失副本 | 验证拒载 |
| 查询目标 | 场景范围 | 验证加载后可操作 |
| 清单详情 | 展开 / 收起 | 解释加载来源 |

### 场景结构

主演示保存到冷启动；子场景：旧格式、缺失、损坏、身份冲突。首屏：“记住当前产物指纹，重开地图后再发一次移动命令。”

### 门户资产

以主循环的惊喜时刻截取真实画面，并保留操作前后状态。入口、配置和媒体由 `showcase.registry.json` 关联；预览消费场景配置与正式产物，禁止另写一份数值。尚未实现的媒体显示待制作，不挂不存在的图片链接。

### 反向 API 审计

| 所需接口 | 归属 | 现状与缺口 |
|---|---|---|
| 清单写入/校验 | Tools / 加载服务 | 完整 Manifest 待收口 |
| 磁盘读取与 store 发布 | Core | 已有 NavTileBinary/Store |
| 冷启动证据 | Launcher / 验收 | 需跨进程五节点记录 |

这些缺口属于对应功能的后续实现范围；本次文档设计不实现 API。阻塞主循环的接口补齐前，不能把演示标为可玩。

### 交付边界与完成判据

本页 Showcase 状态：设计完成；所述完整演示尚未实现，不可玩。已有底层代码或零散场景不代表本页演示完成。 实现阶段必须提供真实 launcher/Mod/地图入口，按 [统一 Showcase 交付合同](showcase-delivery.md) 验证操作、消融、解释层和故障反馈；本页功能判据见第 6 节。

## 5. 边界

source revision、瓦片持久版本与 store 内存发布计数分开定义；冷启动不要求内存计数与上一进程数值相同。写入时间仅是记录，不进入确定性 build hash。源文件 .height/.grid/.hex 不等同于导航 .ntil。

## 6. UAT

```gherkin
Feature: NavTile 产物支持冷启动

  Scenario: 保存后重新加载
    Given 作者已生成 `.ntil` 和 Manifest
    When 作者关闭并重新启动地图
    Then 系统显示相同的 build hash 和瓦片持久版本
    And 起点终点查询成功

  Scenario: 产物过期
    Given source revision 与 Manifest 不一致
    When 玩家请求路径
    Then 系统显示产物过期
    And 给出重新烘焙动作
    And 不返回旧路径
```

这些场景描述目标验收，不是已通过的测试记录。

## 7. 现状与 TODO

核对基线：2026-09-08，`origin/main 63afc7626f419acedcc4bd2f1ade33c8f6cd941f`。

主线已有 `.ntil`、NavTileStore 和路径加载基础；Manifest 完整字段、冷启动查询、旧格式拒载和编辑器 Artifact 面板仍需收口。

分支接收判断：主线已有 NavTileBinary/Store；nav-authoring-showcase 内存存储不能作为冷启动完成证据。

| 责任层 | 本页剩余功能点 | 完成判据 |
|---|---|---|
| 编辑器 | 产物检查与拒载修复动作 | 作者能定位并修改本页数据，错误有具体位置 |
| 工具 | 唯一 .ntil 写入、完整 Manifest 和确定性 hash | 相同输入可重复生成/验证，失败明确退出 |
| Runtime | 加载前核验、损坏/缺失/过期明确失败 | 正式管线消费结果并报告失败，相关回归通过 |
| Showcase | 保存前后、退出、重启加载、继续查询五节点证据 | 第 6 节行为通过，并满足统一运行验收要求 |

主线已有相关测试入口（证明已有合同覆盖范围；不表示本页目标 UAT 已执行）：

- `src/Tests/ArchitectureTests/NavBakeServiceContractTests.cs`
