# 迷雾表现优化（Fog Presentation）

把战争迷雾从「调试可视化」升级为「主流 MOBA 级产品表现」。**不动核心仿真层**（`src/Core/Vision/*` 的语义与契约保持不变），只重做 raylib 表现层与接入方式。

## 现状（main @ 5e3c9d32 实测代码走读）

数据通路：

```
VisionSystem (Core, 每 tick)
  → FogGlobalFieldVisualProjector      src/Core/Vision/FogGlobalFieldVisualProjector.cs
  → GlobalFieldVisualBuffer            src/Core/Presentation/Rendering/GlobalFieldVisualBuffer.cs
  → RaylibFieldRenderPresenter         src/Client/Ludots.Client.Raylib/Rendering/RaylibFieldRenderPresenter.cs
```

渲染侧现状：`RaylibFieldRenderPresenter` 把每个 fog field 画成一张 1 格 = 1 texel 的 RGBA8 纹理平面 quad（固定 Y=0.08m，`LoadMaterialDefault` + `BLEND_ALPHA`，无自定义 shader），脏矩形走 `UpdateTextureRec` 增量上传。逻辑层验收齐全（`src/Tests/GasTests/Production/FogOfWarShowcaseAcceptanceTests.cs`，FOG-2~FOG-10），但 **registry 中 fog_of_war showcase 的 screenshot 仍为空，没有任何视觉证据**。

## 与主流 MOBA（LoL / DOTA2）的差距

1. **颜色语义反了**：可见区被盖 35% 蓝色 tint（`72,214,255,90`），已探索深蓝 59%，未探索黑 75%（`RaylibFieldRenderPresenter.ResolveFogColorBytes`）。MOBA 产品里可见区必须零遮盖，迷雾是对场景的**压暗/去饱和**，不是加彩色罩。
2. **迷雾画在单位脚下**：渲染顺序在地形之后、实体之前（`RaylibHostLoop.cs:782` vs `:805`）。雾中敌人不被压暗/隐藏，迷雾未参与实体可见性（`CameraCullingSystem` 仅视锥剔除）。
3. **固定 Y=0.08m 浮空平面**：不贴地形高度，上 VisualHeightmap 地图必穿插/悬浮。
4. **硬格子边**：5m 格 1 texel/格，默认线性过滤在 RGBA 颜色间插值，边缘出现蓝→红→黑彩色过渡带，而非透明度羽化。主流做法是低分辨率 mask + 高斯模糊 + 双线性上采样。
5. **多层同高叠加**：ground/air/detection 每层一张同 Y quad，深度写关闭，纯靠绘制顺序叠 alpha，多层多 scope 时糊在一起。
6. **无时间维度**：可见→已探索瞬变，无 0.3~0.5s 淡入淡出；且 `FogLayerDefinition.UpdateHz` 注册了但 `VisionSystem` 未消费——每 tick 全量重算 + `AgeVisibleToExplored` 全表 `ReplaceValue`，仿真侧有实打实的浪费（可顺手修，属独立小 PR）。
7. **无小地图迷雾通路**。

## 可以保留的基础

- `GlobalFieldVisualBuffer` 字节契约不带 raylib 类型，架构测试守边界（`RaylibFieldRenderPresenterTests.DoesNotExposeFogFieldStoreInput`）——六边形边界正确。
- 稀疏分块 `FogField` + 脏跟踪 + `UpdateTextureRec` 局部上传策略高效，性能无障碍（1.28km 地图 5m 格 = 256×256 texel；1m 格单通道仅 1.6MB/层）。
- 四态模型（Unseen/Explored/Visible/**Denied** 反侦察）语义比 LoL 三态还全。
- 引擎已有 `terrain.fs` / `decal_project.fs` / `postprocess.fs` 挂载点，走 shader 路线无基础设施缺口。

## 建议演进路径（按收益排序）

1. **mask 化**：纹理从 RGBA 彩色改 R8 灰度（或 RG8：可见度 + 探索度两通道），颜色决策移到 shader；可见区 alpha=0。
2. **模糊羽化**：低分辨率 mask 渲到 RenderTexture，ping-pong 两次 9-tap 高斯再上采样（`LoadRenderTexture` + `BeginTextureMode` 即可）。
3. **贴地**：fog 采样并入 `terrain.fs`（worldXZ→fog UV uniform），地形片元直接乘暗，淘汰浮空 quad。
4. **实体侧**：实体 shader 加 fog factor 采样（同一张 mask）压暗雾中敌人；逻辑隐藏/last-known 残影消费现有 `FogKnowledgeProjector` 输出。
5. **时间平滑**：mask 双缓冲指数渐近（`current += (target-current)*k*dt`），约 0.3s 即有 LoL 雾散开质感。
6. **仿真侧小修**（独立 PR）：`VisionSystem` 真正按 `UpdateHz` 分频；`AgeVisibleToExplored` 只走脏 chunk。

## 验收建议

- 用 `ludots-agent-bridge` 跑 `fog_of_war_raylib` preset 抓**现状截图**作为基线证据（当前 registry 缺），改造后同机位对比截图 + 录像入 `showcase.registry.json`。
- 视觉验收走 `ludots-visual-capture` / `ludots-visual-review` 流程。
- 逻辑层既有 `FogOfWarShowcaseAcceptanceTests` 必须保持全绿（不改语义）。

## 非目标

- 不改 `CellVisibility` 四态语义、`FogField` 稀疏存储、`GlobalFieldVisualBuffer` 契约。
- 不做网络同步 / 回放侧改造。
