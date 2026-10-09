# F03-b 演示交互验收 —— crowd_simulation_s4_deploy_1337

日期:2026-10-09(L28 验收图重抓)。分支 `feat/crowdsim-navsurface` @ 1f7016b097 合并态。

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

## 上一轮验收图作废的根因

审计判定"入库三图与被核验的不是同一次捕获"成立,且 HEAD 上演示根本无法复现该次捕获。逐项查明:

1. **会话脚本未按 tick 升序**:F02 合并时把迷雾命令(reveal/obscure/forget 等,tick 60/100/160/200/240)追加在结构命令(tick 0…210)之后,而 `CrowdCommandQueue.Schedule` 要求升序——S4 演示在合并态启动即抛异常。已把脚本条目按 tick 重排(纯排序,命令内容不变)。
2. **编排脚本用引擎 tick 当会话时钟**:桥 `session.info` 的 tick 是引擎固定 tick,会话 tick 与它无固定比值(规划停摆让会话持续落后),上一轮的抓拍阈值全部落在错误时刻,与"重烘帧停在 tick 95"同类。改用运行日志的 `CrowdSimulation tick N` 行做会话时钟。
3. **B 键 press 点按整拍漏检**:合成输入 press 的按下时长不足一个呈现帧,演示的边沿采样看不到(建造帧停在观察模式的原因)。改为 keyDown/停/keyUp 跨帧按住。
4. **黄色脏 tile 高亮从未真正可见(含上一轮入库图)**:路线渲染器只对折点采样地面高度,1km tile 轮廓的四角直边在丘陵地形整体埋进山体,而默认相机视野内见不到 tile 角点——像素级扫描证实上一轮入库的重烘帧同样不含任何黄色像素,原报告"黄色脏 tile 高亮轮廓"的描述从一开始就不成立。修复:tile 轮廓按导航格(62.5m)逐格细分成贴地折线,轮廓随地形起伏后可见。

## 截图与逐项核验

三张同一运行产出,文件名中的 tick 取自画面 HUD 自身读数。

| 截图 | HUD 读数(逐字) | 画面内容 |
|---|---|---|
| `raylib-s1337-s7demo-rebake-t128.png` | tick 128;单位 12000(闲 6000/行 5487/达 9/不可达 504/跳跃 0);最近重烘:tick 120→123 建造 · 脏 tile 2 · 缓存命中 4/10 · 重规划 1 · 挤离 7 · 无处安放 0;回放:待触发;观察模式 | 黄色脏 tile 轮廓:两枚受影响 1km tile 的共享边,贴地细线自画面左侧横穿至右下、随地形起伏;行军主力在建筑(脚本 op,画面中部)前分流绕行;地面青蓝流场箭头。tick 128 晚于首个结构 op tick 120 |
| `raylib-s1337-s7demo-hud-t328.png` | tick 328;单位 12000(闲 6000/行 5291/达 21/不可达 504/跳跃 0);最近重烘:tick 210→213 道路 · 脏 tile 2 · 缓存命中 4/10 · 重规划 1 · 挤离 0 · 无处安放 0;回放:逐位一致;观察模式 | HUD 常态(本运行默认状态,捕获于任何合成输入之前):地形 + 行军主力,无放置/悬停元素 |
| `raylib-s1337-s7demo-build-t382.png` | tick 382;建造模式 · 建筑 rect 140m(此处可放);最近重烘:tick 210→213 道路 · 脏 tile 2 · 缓存命中 4/10 · 重规划 1 · 挤离 0 · 无处安放 0;回放:逐位一致 | 指针(屏幕 320,360)处绿色矩形足迹轮廓 + 中心白色悬停环;地面流场箭头。建造预览只切演示态不提交现场指令,回放结论保持逐位一致 |

对照说明:重烘帧的"最近重烘 120→123 建造/重规划 1/挤离 7"与画面上建筑落成、队伍分流绕行对应;黄色轮廓是 tick 123 报告发布后 2.5s 真实时间内的线性淡出(该帧处于窗口内约 0.3s,接近全亮)。HUD 帧在自动回放(tick 260)完成后捕获,回放行"逐位一致"。建造帧的可放判定"(此处可放)"与绿色足迹框由同一状态驱动(`CanPlace` 通过 → `PlaceGreen` 轮廓)。

截图产出方式:编排脚本 `scripts/acceptance/run-crowdsim-s4-f03b-shots.sh`——同一次运行内,重烘帧在日志 crowd tick ≥120 后连发 20 张吃满闪窗(截图相对指令有 0.5~3s 呈现滞后),HUD 帧等日志出现 `replay … status=1`,建造帧 B 跨帧按住 + 指针(320,360,已探明为可放点,足迹避开脚本道路条带)。合成输入走 AgentBridge(`ludots.input.raw`/`ludots.screenshot`),不碰演示代码。

## 通道与约束自查

- HUD:ScreenOverlayBuffer(宿主既有覆盖层);世界落点:InputBackend + ScreenRayProvider + 高度图射线;分层视图:GlobalFieldVisualProjectorRegistry + 路线通道(与 S3 演示同一基建);指令:placeStructure/removeStructureAt 正式指令流,呈现线程只入队、仿真 tick(PostMovement 组,先于 Cleanup 组会话步进)消费。
- 内核零演示专用代码(唯一新增 `SuppressAutoReplay()` 为通用运行时控制面);对拍真值与 replay 语义未动(RunReplay/CompareReplay 原样)。
- 本轮演示侧修复仅两处呈现/编排层:脚本 json 条目按 tick 排序(资产修正)、tile 闪烁轮廓贴地细分(`S4DeployDemoPresentationSystem`),内核与对拍面零改动。
- 全量测试:crowd 92/92 绿(本工作树实测;审计时点口径为 88,后续 F03-b 演示与迷雾相关测试已并入)。
