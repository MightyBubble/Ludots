# F03-b 演示交互验收 —— crowd_simulation_s4_deploy_1337

日期:2026-10-09。分支 `feat/crowdsim-f03b-demo`(基于 `feat/crowdsim-navsurface` @ 3723f2eecf 之上)。

## 交付

S4 部署演示从"纯数据、看不懂"升级为交互演示(`CrowdSimulationS4DeployMod` 代码化):HUD 调试信息、V 分层视图、B/X 建造拆除、重烘脏 tile 闪烁、交互后自动回放停用。会话本体仍由 `CrowdSimulationRuntime` 按地图聚焦激活,交互层只挂呈现;HUD/交互数据全部读内核现成状态,内核只新增一个公开面 `SuppressAutoReplay()`。

## 按键清单

| 按键 | 作用 |
|---|---|
| V | 巡回分层视图:路线(主力组流场贴花 + 目标标记)→ 可走区域遮罩 → NavMesh+HPA 线框 |
| B | 进入/退出建造模式 |
| 左键 | 建造模式下在光标处放置当前模板(足迹绿 = 可放 / 红 = 不可放) |
| X | 拆除光标处结构(建造模式下悬停既有结构显示橙色轮廓) |
| 1 / 2 / 3 | 切结构模板:building(rect 140m)/ s7barrier(rect 70m,4s 寿命)/ road(path 30m×250m,自光标向 +X) |

进入任何交互操作(首条现场指令进入会话)后,本次运行不再触发 tick 260 自动回放,HUD 显示"回放已停用(交互模式)";不做交互时原行为不变(本验收 HUD 截图即无交互运行,自动回放 status=1 逐位一致)。

## 截图与逐项核验

| 截图 | 内容 | 核验结果 |
|---|---|---|
| `raylib-s1337-s7demo-hud.png` | HUD 常态(tick 654,无交互) | 标题/状态/重烘/回放/按键五行齐;单位 12000 状态分布上屏;最近重烘行显示 tick 210→213 道路(脏 tile 2 / 缓存 4/10 / 重规划 1);回放行"逐位一致" |
| `raylib-s1337-s7demo-build.png` | 建造预览(tick 427,合成输入 B + 指针) | HUD"建造模式 · 建筑 rect 140m(此处可放)";绿色矩形足迹轮廓 + 中心白色悬停环 |
| `raylib-s1337-s7demo-rebake.png` | 重烘高亮 + 改道路线(tick ~240) | 黄色脏 tile 高亮轮廓(中下方);青蓝方向性流场贴花走廊(左下→中上);HUD 重烘行显示 tick 120→123 建造(脏 tile 2 / 挤离 14 / 重规划 1)——对应脚本建筑 op,队伍绕行改道 |

截图产出方式:HUD 常态 = 宿主环境变量取证(LUDOTS_TAKE_SCREENSHOT_*);建造/重烘 = AgentBridge 合成输入(`ludots.input.raw` 指针 + B 键)与按帧截图(`ludots.screenshot`),编排脚本 `scripts/acceptance/run-crowdsim-s4-f03b-shots.sh`。

## 通道与约束自查

- HUD:ScreenOverlayBuffer(宿主既有覆盖层);世界落点:InputBackend + ScreenRayProvider + 高度图射线;分层视图:GlobalFieldVisualProjectorRegistry + 路线通道(与 S3 演示同一基建);指令:placeStructure/removeStructureAt 正式指令流,呈现线程只入队、仿真 tick(PostMovement 组,先于 Cleanup 组会话步进)消费。
- 内核零演示专用代码(唯一新增 `SuppressAutoReplay()` 为通用运行时控制面);对拍真值与 replay 语义未动(RunReplay/CompareReplay 原样,S7 真值测试全绿)。
- 全量测试:87/87 绿(85 原有 + 2 个 L23 指令入队门测试)。
