# 工单 #1743:raylib 地形 高度 clamp 坑洞 + 远距 LOD 过糊 —— 取证报告(v2,夹具制)

分支 `fix/raylib-terrain-1743`(修复代码 `c249a379ce`,代码本轮未动)。取证日期 2026-10-09。
v1 证据(RPC 临时机位 + crowd 演示整机)被打回;v2 改为**夹具制重抓**,打回意见逐条落实。

## 重抓口径(对打回意见)

1. **同机位同 tick,相机 targetSource=Fixed 钉死**:夹具 mod `Terrain1743EvidenceMod`
   (照 CrowdSimulationS4DeployMod 的虚拟相机写法)声明 rig `Terrain1743.Evidence`:
   `targetSource=Fixed`、`fixedTargetCm=(1577300,1393500)`、`allowUserInput=false`、
   pan/rotate/zoom 全关(校验器强制)。地图 `terrain_1743_evidence` 的 `DefaultCamera`
   逐字段声明位姿;近/中/远 = 同目标三档距离(60000/250000/1920000cm),yaw 45 / pitch 55
   全程不动。每次截图前经 `ludots.camera.control get` 采样位姿,`pose-summary.txt`
   断言前后两跑 **target/yaw/pitch/distance 逐字段相等**(全部 True)。纯地形夹具无
   会话/单位/HUD,画面内容不随引擎 tick 变化,"同 tick"由场景静态性保证。
2. **覆盖坑洞区域,坐标从资产解码定位**:S1337 `.height`(CHTM v2,uint16,scale=
   0/140000/65535)解码后,以 32×32 采样窗扫全图,clamp 误判(解码高 ≥57670.8cm 的
   cm 域哨兵线)最密窗 = **样本 (1008,880),1024/1024 全误判**,真实高度
   597.96–725.47m——相机 Fixed 目标钉在该窗与其坑壁的中点 (1577300,1393500)。
   远景档(19.2km)覆盖全图多片坑洞区(全图 559,683 格被误判,占 53.4%)。
3. **三机位 × 前后六张同位对照;far 无白框**:near/mid/far 各前后两张(-1/-2),
   `before/` 与 `after/` 同名对照;像素差分图 `diff_near/mid/far.png`(差分统计见下)。
   v1 far 图里的白色小方框已查明:**crowd 演示单位集群的选中高亮框**(demo 脚本
   tick 200 `selectAll` 的选中态随单位画进远景),是演示内容不是地形渲染元素;v2
   夹具无单位/无 HUD,结构上不存在该元素,after-far 目检亦无任何白框。
4. **像素差分 + 坑洞区逐格高度对照**:
   - 差分(同位 before-1 vs after-1,max 通道):near meanAbsDiff=17.24、9.8% 像素
     >30;mid 12.10、6.7%;far 8.12、8.1%。
   - `pothole-height-table.csv`:误判最密窗 32×32 逐格(采样坐标/世界坐标/真实高/
     修复前渲染高/修复后渲染高/差值)。**修复前该窗 1024/1024 格渲染高度全部为
     41718.0cm(海平面);修复后逐格等于真实高度(597.96–725.47m)**。全图统计:
     修复前哨兵压平 559,683 格(53.4%),修复后 0 格;海平面以下压平两版一致(设计行为)。

## 缺陷与修复(同 v1 结论,代码未动)

- **缺陷一(平顶坑洞+坑壁条纹)**:`IsOvershootSentinel` 的 cm 域常数 57670.8m 是
  identity 缩放资产的隐含假设;crowd 资产满量程映射 0..1400m,53.4% 样本被压回
  海平面。修复:哨兵改 `RenderProfile.OceanVoidFillAtSampleCeiling` 资产声明
  (默认关,阈值随 SampleScale 换算);导出器资产一律不声明。
- **缺陷二(远景糊)**:overview 网格每 chunk 一顶点(33² 盖 16km)+ 粗网格自差分
  法线。修复:分辨率吃满 `OverviewVertexLimit`(255²,62.5m/格),法线/明暗在源
  采样步长(15.6m)上对连续场中心差分。

## 目检结论

- near:修复前画面中央大面积压平坑(平坦底部+陡崖边缘);修复后同位呈现真实山地
  (山脊/山谷/明暗面),条纹消除。
- mid(2.5km):修复前中上部大片平顶台地、边缘陡崖突变;修复后真实起伏。
- far(19.2km,overview 路径):修复前大片均一色块、受光/背光不可辨、台阶状色块边
  缘、多片压平台地;修复后山脊线与受光/背光面对比清晰、压平台地消失、无白色框元素。

## 取证装置与流程

- 夹具 `mods/fixtures/terrain/Terrain1743EvidenceMod`(纯资产,无代码):地图引用
  crowd s1337 的 `.height`(相对路径跨 mod 单命中),RenderProfile 与 crowd 地图同值;
  最小 `CrowdSimulationDebug.json`(autostart=false)满足 CrowdSimulationMod 的目录
  登记合并约束。
- 脚本 `scripts/acceptance/run-terrain-1743-shots.sh before|after`:launcher 起
  `mod:Terrain1743EvidenceMod mod:AgentBridgeMod`,三档距离各拍两张,位姿逐字段
  断言,定向杀进程。
- **BEFORE 在独立基线工作树(9b014356e3,修复前源码)产出,AFTER 在修复分支工作树
  产出**;两边 Release 二进制经符号 grep 验证(BEFORE 含旧 `IsOvershootSentinel`,
  AFTER 含新 `ResolveOceanVoidSentinelCm`/`OceanVoidFillAtSampleCeiling`)。v1 证据
  失效根因即在此:launcher 运行 Release 输出,而当时只构建过 Debug,导致 v1 两跑
  同为旧二进制(差分≈0),v2 已修正并验二进制。

## 测试

RaylibAdapterTests 325/325、CrowdSimulationTests 93/93(见修复提交 c249a379ce,未变)。
