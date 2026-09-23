# NavMesh 开放世界·四兵种分层通行

> 纯资产 showcase：**这个 Mod 里没有一行 C#**。四类通行能力、三个导航层、跨层连接全部由配置声明。

## 一句话

**同一片 2km 海峡战场上，陆军走陆面、山地军翻山面、水军依吃水走水道、两栖部队经码头与滩头换乘跨层——四条路线因通行能力而分流。**

## 玩法（60 秒）

| 步骤 | 操作 | 你会看到 |
|---|---|---|
| 1 | 启动后框选 A 营全部四支部队 | 蓝（陆军）/ 棕（山地军）/ 深蓝（水军）/ 绿（两栖）四个方阵 |
| 2 | 右键点对岸 B 营 | **四条颜色不同的路线同时画出并分叉** |
| 3 | 打开路径解释面板逐个点部队 | 每支部队显示所属 `layer`、经过的 Link、tile revision |
| 4 | 用陆军点击水面对岸的港口 | **明确失败**：目标 layer 对该 profile 不可达，不画跨海直线 |
| 5 | 关闭 `bridge_upper` Link 后重下陆军命令 | 改走更长的下桥，HUD 显示 Link 关闭导致重解 |

## 四类通行能力怎么表达

**关键：这四种不是四种类型（没有 `UnitKind` enum），而是同一套 profile / layer / Link 语义的不同取值。**

| 兵种 | 手段 | 配置位置 |
|---|---|---|
| 陆军 | `layer=0`（Ground）+ 较弱爬坡 | `agent_profiles.json` → `land_infantry`；对应 `navmesh.json` → `profiles[land_infantry]` |
| 山地军 | `layer=1`（Mountain）+ `maxClimbCm=140 / maxSlopeDeg=55` | `mountain_corps` |
| 水军 | `layer=2`（Water）+ `draftCm` / `beamCm` 决定能否过浅滩窄道 | `naval_shallow`（吃水 120）/ `naval_deep`（吃水 450） |
| 两栖部队 | `layer=0` 起步，**经 Link** 换层到 Water 再换回 Ground | `amphibious` + `navmesh.json` 的 links |

`navmesh.json` 的 `profiles[].id` **引用** `agent_profiles.json` 的 `id`——两者必须一致，这是本轮验证中踩到的硬约束（`NavMeshBakeConfigLoader` 会 fail-fast 报 unknown agent profile）。

## 配置入口

| 文件 | 作用 |
|---|---|
| `assets/Maps/navmesh_openworld_strait.json` | 2048-cell 单板 `mainland`、`Feature.NavMesh:On`、居中世界 |
| `assets/Navigation/navmesh.json` | 3 层（Ground/Mountain/Water）+ 5 烘焙 profile + 5 area 成本 + tile 颗粒度 |
| `assets/Navigation/agent_profiles.json` | 5 个通行 profile：半径/高度/吃水/船宽/mass/layer |
| `assets/Navigation/pathing.json` | profile → 寻路域选择 + 各 area 成本偏好 |
| `assets/config_catalog.json` | 显式声明上述配置的合并政策（**必需**，缺失会 fail-fast） |
| `assets/game.json` | 启动地图、表现容量、小地图与相机裁剪 |

## 消融对照

| 消融 | 做法 | 应看到 |
|---|---|---|
| 层消融 | 不发布 Water layer 的 NavTileStore | 水军报 layer 无数据，不投影到陆地 |
| Link 消融 | 关闭 `land_to_water_pier_a` | 两栖部队报无合法连接，**不生成跨海直线** |
| 重烤消融 | 冻结 `ProcessingEnabled` | HUD 显示冻结、revision 不增长、旧路径不被伪装成新结果 |

## 验收

```
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj \
  --filter "FullyQualifiedName~NavMeshOpenWorldContractTests"
```

覆盖：纯资产（无 C# 源）、单板 + RootBoard、三层隔离、四类烘焙 profile 的能力差异、五个 agent profile 的 layer/吃水分离。

## 当前状态

**配置与契约已落地；可玩运行验收未完成。**

阻塞项（见 `plan/refactor-issue.md`）：

1. **NavMesh Link 未实现** —— 全仓 `NavLink|NavMeshLink|LinkEdge|LinkSegment` 在 `src/Core/Navigation/` 零命中，`navmesh.json` 严格 loader 也不接受 `links` 键。两栖部队与桥上桥下因此**无法表达**。
2. **navmesh 侧不消费吃水/船宽** —— `DraftCm/BeamCm` 目前只在 `TagRuleTraversalPolicy`（NodeGraph 路径）生效。
3. **route 不随导航数据变化失效**、**编队路径不共享** —— 影响多层频繁重解的正确性与性能。

以上未落地前，本 showcase 只满足"配置合同正确"，不满足 `navmesh-features/showcase-delivery.md` 的"可玩交付"。
