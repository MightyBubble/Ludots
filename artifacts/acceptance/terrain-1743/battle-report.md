# 工单 #1743:raylib 地形 高度 clamp 坑洞 + 远距 LOD 过糊 —— 根因与修复取证

分支 `fix/raylib-terrain-1743`(基线 `feat/crowdsim-navsurface` @ 9b014356e3)。取证日期 2026-10-09。
运镜脚本 `scripts/acceptance/run-terrain-1743-shots.sh`,crowd 演示(s1337 图,16km,高程 0..1400m,
uint16 缩放编码满量程映射),三机位经 AgentBridge `ludots.camera.control` 固定,前后同位对照。

## 缺陷一:高度 clamp 坑洞(平顶坑 + 坑壁条纹)

**根因**:`RaylibContinuousHeightmapRenderer.IsOvershootSentinel` 用 cm 域常数
`ushort.MaxValue * 0.88 ≈ 576.7m` 判"虚空/海洋哨兵"。该常数是 identity 缩放资产(原码≈厘米,
引入提交 4bbb061341,为 mass_navigation 而设)的隐含假设;crowd 资产 scale=(0, 140000, 65535),
满量程映射 0..1400m——**53.4% 的样本(560,318/1,048,576,连成 55 万格的整片高原)高于 576.7m,
全部被当作哨兵压回海平面 417.18m**。高原塌成海平面平顶"坑",坑缘 15.6m 采样三角网折线化成方角,
坑壁单格 ~160m 陡崖、坑内颜色仍按原始高度着色成横向条带。

**修复**:哨兵改为资产声明式合同——`RenderProfile.OceanVoidFillAtSampleCeiling`(默认关)。
声明时阈值 = `SampleScale.Decode(0.88 × 原码天花板)`(随资产 scale 换算);不声明则不做天花板压平。
导出器产出的 uint16 资产 scale 由真实 min/max 推出、满量程都是真实起伏,一律不声明。
值域本身无法区分"真实峰值"与"虚空填充"——这是资产语义,必须由资产声明。identity 资产的
历史行为逐位保持(阈值 57671cm)。

**证据**:`before/terrain1743-near-before-1.png`(方角平顶大坑 + 陡壁横向条纹,与用户上报截图
exp3/ext1 同形态)vs `after/terrain1743-near-after-1.png`(坑消失,真实山体起伏,条纹消除)。

## 缺陷二:远距低面数版本过糊(大色块、明暗结构全丢)

**根因**:overview 网格"每 chunk 一顶点"(32×32 chunk → 33×33 顶点盖 16km,500m/格——
`OverviewVertexLimit` 65536 的预算只用了 1089);且法线在粗网格自身差分(slope≈0 → 明暗全平)、
颜色按粗法线逐顶点重算。演示默认相机距离 19.2km,默认即在 overview 路径。减几何的同时把着色
输入也减了——主流引擎远景 LOD 是减几何、保轮廓与法线细节。

**修复**:overview 网格分辨率与 chunk 栅格解耦,吃满 `OverviewVertexLimit`(65536 → 255²,
62.5m/格,轮廓密度 ×8);法线与明暗改在源采样步长(15.6m)上对连续场中心差分——
粗网格定轮廓,细节法线保坡向着色。

**证据**:`before/terrain1743-far-before-1.png`(大片均一色块,受光/背光面不可辨,多片压平
台地,色块边缘台阶状)vs `after/terrain1743-far-after-1.png`(山脊线清晰,受光/背光面对比
明确,压平台地消失)。`wide` 组为同目标 2.5km 近景带状对照。

## 测试

- `RaylibAdapterTests` 325/325(322 原有 + 3 新钉:满量程峰顶保高、哨兵随 scale 换算/声明开关、
  profile 旗标克隆;`ResolveOverviewAxisPointCount` 预算开方测试替换旧 chunk 步长测试;
  `ResolveAbsoluteHeightBand` 着色合同不变)。
- `CrowdSimulationTests` 93/93(渲染侧改动不触 crowd 无头对拍路径,合并态全量旁证)。
- 附带观察(未动,记档):`ResolveAbsoluteHeightBand` 对 relative>1 的样本着"水色"——tropical
  island 图峰值略超声明跨度,峰顶着色偏水色。与本次两缺陷无关,如需处置另立工单。
