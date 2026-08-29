## 交付进展（B1–B5 + showcase 实现）

**基建（一次性代码，全部落地）**
- B1 触发轨道：TriggerGraph 专用 op `OfferActivity`（enum 462 + 描述表 + handler + `IGraphRuntimeApi.OfferActivity` + 编译器符号/端口支持）；provider effect `activity.offer` + 条件 `world.subject_attribute`（`ActivityBridgeProviders.cs`，镜像 task 桥）。GAS 组合门自审：`artifacts/gas-composition-gate.md`（A 类，通过）。
- B2 排水：`ActivityPresentationDrainSystem` 挂 ClearPresentationFlags 相位，presentation/lifecycle 缓冲每步排空（修无界增长）。
- B3 数据面：`ActivityWebUiTopicProducer` + `ActivityPanelProfile`（SSOT=ActivityRuntimeService；选项/基础项/锁定原因/历史含所选选项/当帧 cue 窗口）。
- B4 面板：PanelKit panelType `activity`（描述符/目录/样例 manifest 第 7 块）。
- B5 命令：`activity.confirm` / `activity.showcase.trigger` / `activity.showcase.setAttribute`（命名命令经 WebUiCommandRouter）。

**随卡偿还的债**：cue 载荷统一 ScopeKey；`TryGetActiveOptions` 拒绝非 active；`ActivityView` 补 `SelectedOptionId`+`ScopeHost`；信号去重入快照（存档往返）；GameEngine 里 Task 桥/Activity 桥先于各自配置加载安装（修加载期键校验的先有鸡问题）；`ResetState` 清信号去重表。

**Showcase**：`mods/showcases/activity_dispatch/`（一个 mod、三条路径、真实入口 preset `activity_dispatch_cef_raylib`、注册表 `activity_dispatch`）+ per-op gallery 三件套（vignette/图/entry mod/wiki `OfferActivity.md`，自定义 ActivityNodeDriver + harness 程序注册补全）。

**验证**
- Activity 61/61、Gallery 208/208、DataPlane 106/106、PanelKit 80/80、GraphRuntime 13/13、Persistence 100/100 全绿。
- headless UAT：`ActivityDispatchShowcaseAcceptanceTests`（三路径端到端 / 池抽跨引擎确定性 / cue 排水），产物 `artifacts/acceptance/activity_dispatch/`。
- Agent Bridge 实机：独立实例（mapId=activity_dispatch）双 health pump 增长、`events.fire` forced/automatic 触发 0 错误、graph trace 完整记录 LoadPlacedEntity→OfferActivity 执行（`agent_bridge_forced_trace.json`）、截图 `activity_dispatch_rail.png`。

**已知边界（诚实申报）**
- CEF 面板实机视效未取证：本环境共享 Release 输出被另一运行实例锁定，隔离 publish 又缺 CefSharp 原生包；面板数据链路由 headless UAT + producer/PanelKit 测试覆盖。占用释放后走 preset 启动即可补。
- 全量 GasTests 112 失败经基线对照与逐项检查确认为预先存在（LFS 证据视频缺失、testhost 原生崩溃等），与本次改动无关；Architecture 6 失败同属既有债务。
- 注册表校验的 2 个错误为 HEAD 树检查（文件已暂存待提交）。
- issue 边界内未动：`presentation_cue` 死字段仍在（后续卡）；forced 路径 context_bindings 缺口照旧。
