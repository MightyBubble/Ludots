# 相机 Rig 讲解器

Three.js 单页，可视化相机 rig 的三层结构：锚点 Pivot（跟人物体：`TargetCm` + `TargetHeightCm` + `RigPivotOffsetCm`）→ 摇臂 Arm（yaw / pitch / distance + 阻尼、贴地、防穿）→ 镜体 Lens（`FovYDeg` + `RigCameraOffsetCm` 冷端）。主视图画 rig 本体（绿环 = Yaw 轨道），右下角"镜体视口"是这台 rig 实时渲染出的玩家画面（scissor 叠加渲染，黄色十字 = 屏幕中心共点轴）。另外演示瞄准层 AimLayer、探测 Probe、facingMode 面向采纳，以及 20Hz sim 步进 + 渲染帧插值的时钟模型（关掉摇臂阻尼可见修复前的抖动）。

文件自包含：`three.min.js`（r147）与 `OrbitControls.js` 为本地 vendor，无 CDN 依赖。起一个静态服务器即可打开：

```sh
python -m http.server 8931
# 浏览器访问 http://127.0.0.1:8931/index.html
```
