# Tech Debt Report: TD-20260905-VISUAL-UNIT

Date: 2026-09-05
Reporter: 主会话(sango port 分支 feat/sango-port-m0)
Owner: 引擎维护者(单位合同与观测面);mod 侧已由报告人修复
Severity: P1(表现层正确性;被 mod 违反时 100 倍位姿/尺度漂移且无任何告警)
Scope: Cross-layer(Core Visual 域契约 ↔ Mod 资产/代码 ↔ AgentBridge 观测面)

## Trigger
- Scenario: sango mod 城池/部队/标签 presenter 全部渲染在距地图中心 ~165km 处
  (88 座城不可见),散树以 644-947 米高渲染;用户两轮报告"模型大小有问题"。
- Entry point: mod 侧直写 `VisualTransform.Position`(米域)写入逻辑厘米值;
  `localScale`/散布 `scale`/`grounding.offset` 三个无标注单位的资产字段按厘米
  语义填写、按"纯乘子/米"语义消费。
- Repro steps: 启动 sango_m1_raylib;`ludots.presenters.query` 任意城 marker
  → `visualPos={-167000,0,149000}`、`presenterPlaneCm={-16700000,...}`(引擎把
  米域值×100 当逻辑厘米回读,即城在 -167km);相机对 `targetXCm=-167000`(桥正确
  换算为 -1670m)看不到任何城。

## Evidence
- 单位 SSOT 与唯一正式换算点:`src/Core/Math/WorldPlane2D.cs:213-221`
  (`LogicCmToVisualMeters`,逻辑 XY 厘米 → 视觉 XZ 米);
  `src/Core/Presentation/Systems/WorldToVisualSyncSystem.cs:219-224`(仅从
  `WorldPositionCm` 同步路径换算)。
- 官方 spawner 的正确写法:`src/Config/TemplateEntityBatchSpawner.cs:1371`。
- 视觉域米制旁证:`src/Core/Gameplay/Camera/CameraViewportUtil.cs:165-221`
  (`CmToM(state.TargetCm)`);`src/Platform/Ludots.Platform.Abstractions/CameraClipPlanes.cs:5-7`
  (near 0.1m / far ≥10000m);`RaylibContinuousHeightmapRenderer.cs:437-440`
  (`camera.target.X * 100f`)。
- scale 全链无换算(纯乘子):`AssetBindingVisualScale.cs:9-21` →
  `PresenterAssetEmitRuntime.cs:733-742` → `PresentationVisualProxyEmitter.cs:41-49`
  → `RaylibPrimitiveRenderer.cs`(DrawModelEx/CreateScale 原样)。
- 散布加载/消费:`InstancedBatchFactorizedSourceLoader.cs:116-127`(scale 原样,
  同级字段名为 `positionCm`)+ `RaylibInstancedBatchLaneStore.cs:249-258`
  (位置 ×0.01、scale 原样)——两条路径自洽,但资产字段无单位标注。
- grounding.offset 单位=米:`PresenterGroundingUtility.cs:13-14,225-241`。
- 运行时铁证:AgentBridge `ludots.presenters.query` 城行 visualPos/-167000m、
  presenterScale {1,1,1}、adapterDrawn true(截图与会话 2026-09-05)。
- mod 修复(本波提交):SangoCityMarkers/SangoMapLabels/SangoTroopNativeRuntime×3
  处写点改 `LogicCmToVisualMeters`;presenters.json localScale/localPosition ÷100、
  grounding.offset 转米;scatter scale ÷100。

## Impact
- User-visible impact:mod 侧一发单位错即整场景不可见/巨物,且引擎零告警、零拒绝;
  远平面裁剪会"自愈"式隐藏部分超巨物,造成时有时无的假象(本轮:M3.i 原始 15-23km
  树被 far-plane 剔除"看不见",修数据到 644-947m 后反而全部显形)。
- Correctness/stability risk:无状态损坏;纯表现层。但取证工具在窗口失焦时静默返回
  陈旧帧,污染视觉验收(本报告人上一轮验收即被此污染,见 Fuse 项 3)。
- Blast radius:所有 mod 的 presenter 资产与 VisualTransform 直写代码;AgentBridge
  截图/工具使用方。

## Fuse Decision
- Mode: explicit-degrade(mod 侧即刻修复=消解;引擎侧观测面 proposals 见下,未实施)
- Reason: 单位合同本身在引擎内自洽,不需要行为性 fallback;缺的是把违约暴露出来的
  观测面与资产单位标注。任何静默"自动 ÷100"式容错都被否决(会掩盖违约、引入歧义)。
- Observability fields(建议,未实施):
  1. PresenterBehaviorSystem grounding 采样越界(高度图 XY 出界)时按 stableId 一次性
     Warn(带采样坐标与高度图边界)——本案 88 城第一时间就会点名。
  2. AgentBridge screenshot 结果带 frame tick/age;pump 停滞时与 tools 一致地报
     bridge.timeout 而不是静默返回最后呈现帧。
  3. (可选)emit 后静态 mesh 世界 AABB 超 far-plane×k 时 Diagnostics 计数。

## Containment and Follow-up
- Immediate containment(已完成):mod 三处写点换算 + 两份资产单位重标(本波提交);
  测试 83+32 全绿;重启会话以 presenters.query 数值(米域)与无引导截图复核。
- Permanent fix direction(引擎侧,按需排期):
  1. gitbook 表现层文档立"单位 SSOT"一节:VisualTransform/相机/网格顶点=米,
     WorldPositionCm/TargetCm/高度图 heightCm=厘米,唯一换算点 WorldPlane2D;
     localScale/散布 scale=纯乘子(资产示例给"米域乘子"注释);grounding.offset=米。
  2. 资产 schema 单位标注与校验:presenters.json localScale/grounding.offset 与
     scatter 格式 v2 增加 unit 字段(或文档化命名约定 positionCm/scale 语义),
     ConfigPipeline 对越界量级(如单实例世界高超 far-plane)给 Warn。
  3. 上条 Observability 1/2 落地(小改,PresenterBehaviorSystem + AgentBridge)。
  4. (评估项)VisualTransform 直写面是否收敛为 spawner/sync 专用——开放 struct
     组件直写无守卫是本次违约的使能器;若保持开放,至少靠 1-3 把违约可见化。
- Target milestone: 引擎下个表现层 PR(文档+观测面);schema v2 随下一个资产格式
  版本。

## 附:本案时间线(供复盘)
1. M3.i(05c89adf9)按"原生~1m"盲设 localScale/散布 scale,量级差 4-40 倍;
   同时 SangoCityMarkers 等三处把厘米写进米域——城全部 165km 外,极巨物多被
   far-plane 裁剪,目检"8/10 通过"实为看不到。
2. e7d8ef677(2026-09-04)按错误单位假设重标定:全部 ÷3-4 但仍差 100 倍;树
   644-947m 进入可视距,巨物显形——用户二报。
3. 本波:Explore 全链源码审计 + presenters.query 运行时取证定位真实合同,mod 侧
   修复;上一轮"四组取证通过"作废(引导性图像分析 + 失焦陈旧帧双重污染)。
