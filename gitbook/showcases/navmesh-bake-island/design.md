# Navmesh 烘焙可视化编辑器 showcase 设计(navmesh-bake-island)

> 状态:**设计完成,尚未实现,不可玩**。本文档是开工输入;实现走可玩交付闸门后才可宣称完成。

## 一句话与目标用户

在一个坡度复杂的小岛上,亲眼看 navmesh 怎么烤出来、怎么被建筑物逼着重烤、烤完的路网怎么被兵走掉——给"想知道这套 nav 管线到底能不能用"的游戏开发者。

## 主循环

- **谁改变世界**:玩家框选小兵右键下令(走路网)、按 B 放置建筑物(动态障碍触发 runtime 增量重烤)。
- **用户看到什么变**:① 烘焙进行时,瓦片逐块从灰变亮(动态 bake 过程可见);② 建筑落地的瞬间,周边 navmesh 瓦片闪红→重烤→挖出洞;③ 小兵路径实时绕开新建筑;④ 走性贴图(decal 贴在地形上)随重烤刷新,可通行=绿、水=蓝、不可行=红。
- **惊喜时刻**:玩家把建筑**故意堵在兵的必经之路上**——兵走到一半,路网塌出一个洞,兵当场折返绕行,HUD 的"重烤耗时 ms / 路径长度 cm"同时跳变。3 秒看懂"动态 navmesh"四个字。

## 消融对照

**重烤开/关**(`R` 键):关——建筑落地后 navmesh 原样不动,兵穿墙/卡死在洞上(展示没有能力的世界);开——增量重烤+绕行。同场景一键切换。

## 解释层

- HUD:`重烤耗时 X ms`(真实 store revision 推进耗时)、`路径长度 N cm / 重算 P 次`、`可行走瓦片 A/总瓦片 B`、`当前层:Ground(兵)/Water(船)`。
- 颜色编码(全部来自真实 nav 数据,非第二份数据):navmesh 多边形描边(绿=Ground 层,蓝=Water 层)、走性贴图热力(可行/水/断崖/封禁)、重烤中的瓦片高亮闪烁。
- 图例:左下角一句话——"绿=兵能走,蓝=船能走,红=坡太陡,黄闪=正在重烤"。

## 旋钮清单

| 旋钮 | 范围 | 回答什么 |
|---|---|---|
| 重烤开/关(R) | on/off | 动态障碍到底值不值这几十毫秒 |
| 坡度阈值(1-3) | 30°/45°/60° | 什么坡兵还能爬——贴图上红区随之涨缩 |
| 走性贴图显隐(T) | overlay/decal/off | 烘焙结果怎么读 |
| bake 档位(F) | offline 全烤 / runtime 增量 | 全量 vs 增量的耗时差(第 4 步 HUD 数字直接对比) |
| 烘焙喂入轨(G) | triangles / direct | 双轨对照:直灌(Epic #1350 新管线)vs 三角化,同一座岛两种喂法的耗时与拓扑差 |
| 框选目标层(空格) | Ground/Water | 兵与船分层选择的体验 |

## 场景结构

- **主演示**:小岛(`.height` 连续高度图:中心山+复杂坡+浅滩,用 `VisualHeightmapBinary.Write` 直接生成,**仓库不新增任何 .vhtm 产物**,对齐 #1343 更名方向),外围 grid 标水。1 个带动画小兵(Ground/Small 层,复用仓库 gpu-skinning 动画资产)+ 1 条船(Water/Medium 层,复用 east_asia 海面层方案 layer1/profile_Medium)。
- **子场景**:①纯坡度(关水看红区);②纯重烤(空地上连放三栋);③双层切换(兵/船互选)。
- **首屏引导**:"左键框选,右键下令;B 放建筑看 navmesh 挖洞;R 关掉重烤看看兵会多蠢;空格切船"。

## 门户资产

惊喜时刻帧(建筑堵路→兵折返+贴图挖洞同框)为封面;预览页从本 showcase 的 navmesh.json/bake 工件直接生成走性图例(禁第二份数据);README 三段说人话。

## 反向 API 审计

| 需要 | 现状 | 归属 |
|---|---|---|
| 烘焙过程逐瓦片回调(过程可视化) | NavBakeService 有 per-entry 产物,无 UI 流式回调 | **本次交付**:订阅 bake 结果增量发布(RuntimeIncrementalNavMeshRebuildQueue 已有 revision 链,补只读事件) |
| 走性贴图→地形 decal | WalkabilityTextureExporter 已出图;presenter 有 surfaceSource;RaylibHostLoop decal 接收面(vhtm 渲染源)承担高度拟合 | **本次交付**:把贴图作为 surfaceSource 资产绑定到地形 presenter |
| 水域 grid 标记→双层 navmesh | east_asia 生产链已证(seaLevelCm 分层) | 复用 |
| 建筑放置→结构障碍→增量重烤 | NavGate 链已有(RuntimeNavMeshStructuralObstacle) | 复用(修复其 DLL 不构建的旧债) |
| 带动画小兵 | gpu_skinning 画廊/MassNavigationMod 资产 | 复用 |
| 框选/右键下令 | MouseBox 链(main 侧有 #1385 回归) | 复用+规避(单兵单击直走最简路径,不强依赖 MouseBox) |
| `.height` 生成器 | `VisualHeightmapBinary.Write` 公开可写 | **本次交付**:程序化小岛生成器(输出 .height,零 .vhtm) |

## 交付边界与完成判据

- 范围:一个新 mod `NavBakeIslandShowcaseMod` + 小岛 `.height` 资产(程序生成)+ launcher preset + 注册表;不改 nav 核心(审计缺口另票)。烘焙默认走 `terrainFeed=direct`(Epic #1350 主线),`G` 键切回 triangles 做双轨消融。
- 入口:launcher preset `nav_bake_island_raylib`。
- 完成判据:可玩闸门 9 项(Agent Bridge 实机:两次 /health pumpCount 增长、驱动兵下令/放建筑/关重烤/切船、前后状态+截图)全过;UAT 以玩家视角 BDD。
- 依赖:#1396(债务收口)合入后基于其构建;main 当前 graph 画廊红不阻塞本 showcase(不同域)。
