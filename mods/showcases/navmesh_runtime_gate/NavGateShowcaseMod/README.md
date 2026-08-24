# Navmesh Runtime Update showcase 设计——隘口封锁（NavGate）

> 状态：设计完成，实现随本提交落地（可玩闸门证据见文末验收记录小节）。

## 一句话与目标用户

「城门落下的一瞬，行军小队当场改道」——用一次看得见的封锁与绕行，演示 navmesh
运行时增量重烤能力。写给没读过引擎代码、但玩过 RTS / 用过 Unity NavMesh 或
UE Recast 的评估者。

对标的生产级语言：

| 生产引擎的做法 | 本 showcase 的对应 |
|---|---|
| UE NavMesh 调试视图：重建中的瓦片高亮 | 脏瓦片橙色描边，重烤完成即消退 |
| Unity NavMeshObstacle + carve：放障碍即挖洞 | 城门/手动障碍落地 → navmesh 挖洞 + 级联重烤 |
| NavMeshAgent.FindPath 后沿拐点行走 | 小队每代理走真实 `TryFindPath` 折线 |
| 生产 KPI：重烤耗时 / 路径长变化 / 重路由延迟 | HUD 显示待重烤瓦片、store revision、上批重烤 ms、平均路径长 |

## 主循环

- **谁改变世界**：自动巡演时间线——集结 → 小队 8 人 A 营(西南)行军 B 营(东北)
  → 途经隘口时城门落下（大障碍插入）→ 增量重烤 → 全队改道绕山 → 抵达 B →
  城门抬起 → 返程 A → 循环。玩家随时可介入（见旋钮）。
- **用户看到什么变**（反馈 < 1s）：门落下 → 隘口 navmesh 被挖出红圈空洞 →
  相邻瓦片橙色描边闪烁（重烤中）→ 小队绿色路径折断在门前 → 黄色新路径翻山绕行 →
  队伍当场掉头。
- **惊喜时刻**：门落下后一秒内，8 条路径齐刷刷放弃直线、集体绕山——
  「世界变了，寻路当帧跟上」。

## 消融对照

**F 键冻结/恢复增量重烤**（同一场景、运行时切换）：
- 开（默认）：门落 → 挖洞 → 改道；
- 冻结：门落 → navmesh **不更新** → 小队沿旧路径直穿门洞位置（路径线穿过红圈，
  单位滞留在门内），直到按 F 解冻的瞬间瓦片橙色亮起、路径弹开。

这一屏就是「为什么需要 runtime 更新」的答案。

## 解释层

HUD（全部来自真实运行管线，无硬编码数字）：

- 阶段横幅：集结 / 行军 / 隘口封锁 / 绕行 / 抵达 / 返程（世界文本，随时间线真实切换）；
- 重烤指标：待处理瓦片 N、store revision R、上批重烤耗时 ms（队列真实字段）；
- 小队指标：已到达 x/8、平均路径长 cm（各代理 TryFindPath 真实结果均值）。

颜色编码（左下角图例常驻）：

| 元素 | 颜色 |
|---|---|
| navmesh 面 | 蓝 |
| 脏/重烤中瓦片 | 橙色描边 |
| 城门 | 红色圆环 + 红圈空洞 |
| 当前路径 | 绿 |
| 改道后新路径 | 黄 |
| A/B 营地 | 白圈 + 文本 |
| 小队单位 | 青色小圈 |

## 旋钮清单（运行时，均不需重启）

| 旋钮 | 范围 | 演示什么（回答用户什么问题） |
|---|---|---|
| G 城门落/抬 | 二态 | 大障碍触发的多瓦片级联重烤全过程 |
| P / O 手动障碍，R 换半径 | 12m / 24m / 48m | 洞开多大、要重烤几块瓦片 |
| F 冻结重烤 | 开/关 | 消融：没有 runtime 更新世界会怎样 |
| N overlay 模式 | 面 / 线框 / 关 | 数据视图与效果视图切换 |
| T 巡演节奏 | 0.5x / 1x / 2x | 慢放逐帧看重烤与改道过程 |

## 场景结构

- **主演示**：`nav_gate_valley`——vhtm 起伏山谷（地形资产与 navmesh_debug_vhtm 同源），
  A(3200,3200) → B(22400,22400)，隘口取地图中央走廊 (12800,12800)；
- **子场景**：`$navmesh_debug_grid` / `$navmesh_debug_hex` / `$navmesh_debug_vhtm`
  三个纯数据调试场景（已存在，注册表互链），供想看裸数据的评审；
- **首屏引导**：世界文本「G 落门 · F 冻结重烤 · N 切视图 · T 调节奏」+ 阶段横幅。

## 门户资产

- 封面 = 惊喜时刻帧（门 + 橙脏瓦片 + 绿断路径 + 黄新路径同屏），实机截图；
- `showcase.registry.json` 条目 → 真实 launcher 入口；本文档即设计文档；
- 同源：地图与地形直接引用既有 vhtm 资产，无第二份配置。

## 反向 API 审计

| 需要的接口 | 现状 | 归属 |
|---|---|---|
| 待处理瓦片快照（脏瓦片高亮） | 队列私有 `_fifo` | **本次交付**：`PendingTilesSnapshot()` |
| 重烤暂停/恢复（消融） | 无 | **本次交付**：`ProcessingEnabled` |
| 上批重烤耗时（HUD） | 无计时 | **本次交付**：`LastBatchElapsedMs` |
| 路径查询 | `NavQueryService.TryFindPath` 已有 | 复用 |
| 地面覆盖（线/圈/环/描边） | `GroundOverlayBuffer` 已有 | 复用（注意：Circle/Ring 的描边
  走 `BorderWidth`，`Width` 仅对 Line 生效——本次实机踩过） |
| 世界 HUD 文本 | `WorldHudBatchBuffer` 已有 | **后续**：文本 HUD 需接 token 管线，本次以
  颜色图例 + 阶段控制台输出承担解释层 |
| 单位模板化 presenter 视觉 | 单位渲染栈 | **后续**：本次小队用真实实体 +
  GroundOverlay 呈现（演示对象是 navmesh 路径与重烤，不是单位渲染） |
| 跨瓦 navmesh 查询 | Detour 跨瓦焊接存在南向缺陷与深目标投影缺陷 | **后续（引擎缺陷）**：
  已登记，全场戏暂收敛在西南角单瓦舞台（`NavGateIds` 注释） |
| runtime 重烤线程模型 | 同线程同步烘焙，单瓦可达秒级 | **后续（引擎缺陷，NAV-R2）**：
  见 `artifacts/techdebt/2608-nav-runtime-bake-livelock.md`；本次以
  detail 采样调优（`RecastNavTileBaker`）+ 三圈自动熔断兜底 |

## 交付边界与完成判据

- 入口：launcher preset `$nav_gate` → `raylib.nav-gate.launch.graph.json` →
  NavGateShowcaseMod + 地图 `nav_gate_valley`；
- 可玩闸门：ludots-showcase-design 九项 + headless 合同测试
  （门落 → revision 增 → 路径绕门 → 抵达 B；冻结 → 路径仍穿门）+ 桥接实机证据；
- UAT 以 Cucumber 落 `scripts/acceptance/acceptance.index.json`。

## 验收记录

**headless 合同测试**：`NavGateShowcaseContractTests` 2/2 绿
（落门 → revision 增 → 路径避开门洞 → 全员抵 B；冻结 → revision 停 → 旧路径穿门 →
解冻恢复）；`NavMeshDebugVhtmReliefContractTests` 2/2 绿（vhtm 起伏保全）。

**桥接实机验收**（`raylib.nav-gate-bridge` 变体 + AgentBridgeMod，2026-08-23）：

- 自动巡演闭环：集结 → 行军 → **首程即落门**（5 秒兜底保证惊喜时刻）→ 隘口封锁
  （增量重烤）→ 绕行 → 抵达 B → 抬门 → 返程循环，连续运行 13 分钟无卡死；
- 三帧证据（`artifacts/agent-bridge/shots/`）：
  - `navgate_surprise_moment.png`：红门环 + 橙脏瓦 + 黄改道路径 + 青单位 + 白营地环
    同帧（惊喜时刻）；
  - `navgate_freeze_ablation.png`：F 冻结后 G 落门——橙瓦滞留、**绿色旧路径穿门而过**
    （陈旧 navmesh 代价的直观证据）；
  - `navgate_recovery_lifted.png`：解冻恢复 + 抬门后干净绿路径；
- 交互注入：`input.inject`（NavGate_ToggleFreeze / NavGate_ToggleGate press+release）
  全部生效，冻结/恢复有控制台回执（"增量重烤 已恢复"）；
- 稳定性熔断：自动落门满 3 圈后输出 "稳定性熔断 NAV-R2"，巡演停止自动落门但持续
  稳定运行（手动 G/F/N/P/O/R/T 不受限）；
- 实机暴露并当场处理的缺陷：
  1. **NAV-R2（P0）**：runtime 重烤同线程秒级阻塞 + `RcMeshDetails.BuildPolyDetail`
     采样循环平方级膨胀（历史实机会话第 6 圈、复测第 3 圈整体卡死，`dotnet-stack`
     取证）→ 修复：`detailSampleDist 6→16 / maxError 1→4`（hull 顶点高度本就精确，
     只损失面内细化，合同测试全绿；`sampleDist=0` 不可用——Detour 序列化越界）；
     残余风险（单瓦秒级阻塞仍在）登记 `artifacts/techdebt/2608-nav-runtime-bake-livelock.md`；
  2. 展示层 bug：Circle/Ring 描边字段误用 `Width`（仅 Line 生效）→ 改 `BorderWidth`，
     前两轮截图圈类全部不可见的根因；
  3. 桥接工具在长烘焙批次与窗口失焦期间超时（>10s 无呈现泵）——恢复窗口前台即可
     继续取证，已作为桥接验收操作注意事项记入本节。

**已知边界**（后续清单见反向 API 审计）：vhtm 地形网格在 raylib 宿主尚无渲染
呈现（背景为空 + navmesh 覆盖层），跨瓦布场待引擎缺陷修复后放大。
