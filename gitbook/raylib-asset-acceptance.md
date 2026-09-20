# 教程：Raylib 资产验收台——把商店模型拖进窗口，当场验 PBR

> 一句话：把 Sketchfab / Fab / Unity 资产商店下载的模型**直接拖进窗口**，在引擎真实的光照栈（GGX 主光 + 天空 IBL + 深度阴影 + 昼夜太阳）下验收它的 PBR 材质和动画——**不用先转格式，不用开 Blender**。
>
> 它是独立工具（不挂 mod / launcher / showcase 注册环），但走的是引擎真实渲染管线：着色器 `src/Platforms/Desktop/model_file_lit.fs`、光照/IBL/阴影全部复用 `Ludots.Raylib.Render` 现有件。OBJ/FBX 进来时由引擎的 Assimp 转换器先转 GLB（[issue #1050](https://github.com/mightyBubble/Ludots/issues/1050) 崩溃的修复路径）。

## 60 秒上手

```bash
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance
```

窗口打开后，从资源管理器把模型文件（或装着模型的**整个文件夹**）拖进窗口。预期看到：

1. 模型自动落地居中，缓缓转台展示；
2. 左侧两列**参考球**（非金属/金属 × 粗糙度 0.15/0.50/0.85）与模型同光同影——它们是 PBR 的"地面真值"对照；
3. 左上 HUD 给出这个资产的真实读数：网格/材质/骨骼/动画数量、**贴图覆盖率**（albedo/normal/ORM/emissive 各多少材质有）、PBR 因子；
4. 太阳自动从清晨走到傍晚，阴影跟着转。

两段录屏（真实运行取样拼制，非特效）：

<video controls playsinline preload="metadata" poster="artifacts/evidence/raylib_asset_acceptance_demo/poster.png" src="artifacts/evidence/raylib_asset_acceptance_demo/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/raylib_asset_acceptance_demo/play.mp4。
</video>

功能全程演示（mannequin 行走模型）：最终光照 → albedo → 法线 → 粗糙度 → 缺省标量消融 → 回到最终，每段 2 秒。

<video controls playsinline preload="metadata" poster="artifacts/evidence/raylib_asset_acceptance_obj/poster.png" src="artifacts/evidence/raylib_asset_acceptance_obj/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/raylib_asset_acceptance_obj/play.mp4。
</video>

上一段是 [issue #1050](https://github.com/mightyBubble/Ludots/issues/1050) 的原始崩溃资产 `mass_navigation_blocker_rock.obj`——曾经裸 `LoadModel` 直接 AccessViolation，现在经转换正常装载。

## 验收怎么做：三步判读法

**第 1 步 · 对照参考球。** 参考球就是这套光照下的"已知答案"：非金属列应呈哑光渐变，金属列应亮反射。如果模型和参考球的光感明显不一致（比如全黑、过曝、没有高光），先怀疑资产本身或贴图通道，再怀疑引擎。

**第 2 步 · 拆通道看。** 按 `2`–`5` 把画面切成单通道：

| 按键 | 视图 | 你在验什么 |
|---|---|---|
| `1` | 最终光照 | 整体效果 |
| `2` | albedo | 基础色贴图对不对（颜色/透明度/有没有贴错） |
| `3` | 法线 | 法线贴图有没有生效、方向对不对（蓝紫色是正常法线色） |
| `4` | 粗糙度 | ORM 贴图 G 通道/因子是否合理（灰度=粗糙度） |
| `5` | 金属度 | 金属通道读数（黑白=金属度） |

**第 3 步 · 消融对照。** 按 `O` 切到"缺省标量 PBR"（忽略全部贴图，用引擎缺省 0.8 粗糙/0 金属渲染同一模型）。贴图版 vs 缺省版的差异，就是这些贴图**实际贡献**的东西——差异为零说明贴图没被采样到（配合 HUD 贴图覆盖率即可定位是资产没带贴图，还是通道出了问题）。

判读示例（带贴图 OBJ 转换后的纹理嵌入，HUD 显示 albedo 1/1）：`artifacts/evidence/raylib_asset_acceptance_demo/textured-cube.png`。

## 输入格式合同

| 拖入什么 | 引擎怎么处理 | 你得到什么 |
|---|---|---|
| `.glb` / `.gltf` | raylib native 装载 | 全功能：PBR 贴图 + 骨骼动画 |
| `.obj` / `.fbx` / `.dae` | 引擎 Assimp 转换器先转 GLB（按源文件哈希缓存，源不变不重转） | 几何 + 材质 + 贴图；动画取决于源文件（OBJ 无动画） |
| 装模型的**文件夹** | 递归找 `.glb → .gltf → .fbx → .obj → .dae`，取路径最短的一个 | 同上 |
| `.zip` / `.unitypackage` / USD / blend | **拒绝**，红面板给出理由 | 明确的错误提示（Sketchfab 下载请先解压 zip） |

装载失败永远在画面上给出可读的红面板 + 重试提示，不闪退、不静默降级（示例：`artifacts/evidence/raylib_asset_acceptance_obj/error-panel.png`）。转换器与格式分流的实现在 `src/Client/Ludots.Raylib.Render/Rendering/RaylibModelFileConverter.cs`。

## 旋钮速查

| 按键 | 作用 | 回答什么问题 |
|---|---|---|
| `1`–`5` | 视图拆通道（见上表） | 每张贴图各自长什么样 |
| `O` | 贴图 PBR ↔ 缺省标量 PBR | 贴图实际贡献了多少 |
| `K` | alpha 剔除 0.10 / 0.50 / 关 | 透明镂空资产的阈值对比 |
| `E` | IBL 强度 ×0 / ×0.5 / ×1 / ×2 | 环境反射的贡献 |
| `SPACE` / `N` / `P` | 播放暂停 / 下一个 / 上一个动画 | 多动画资产逐个验 |
| `-` / `=` | 动画速度 ×0.25 步进 | 慢放看细节 |
| `,` / `.` | 太阳相位手动调 | 特定光照角度下的表现 |
| `T` | 转台开关 | 停下来细看某一面 |
| `R` / `ESC` | 相机复位 / 退出 | — |
| 鼠标左键 / 滚轮 / WASD | 旋转 / 缩放 / 平移 | 自由观察 |

## 为什么有这个工具：#1050 的 60 秒故事

合并 #1032 后，游戏装载 OBJ 模型在 `LoadModel` 内部原生 AccessViolation（issue #1050，mass_navigation 的石块/方尖碑资产即崩溃形态）。最小探针证明：**裸 `InitWindow + LoadModel(obj)` 就崩**，与 Cubemap/IBL 无关；触发条件是 OBJ 面片没有 texcoord/normal 索引（`f 1 2 3` 纯顶点形态）——bug 在 native raylib.dll 的 OBJ 解析分支内，C# 侧无法直接修。

修复因此绕开崩溃分支：引擎新增统一装载入口 `RaylibModelFileLoader`（glTF 原生；OBJ/FBX/DAE 先经 Assimp 转 GLB），`RaylibPrimitiveRenderer`（原崩溃点）与 `RaylibGpuSkinnedModelCache` 的直连 `LoadModel` 全部换接。崩溃格式已固化为回归测试 `src/Tests/RaylibAdapterTests/RaylibModelFileConverterTests.cs`（RaylibAdapterTests 80/80 绿）。

## 无 UI 批量验收（agent / CI 视角）

拖放没法进 CI，所以同一条装载管线有无头通道：

```bash
# 单帧验收截图（隐藏窗口跑 N 帧后截图退出）
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance -- \
  --model <模型路径> --frames 240 --screenshot out.png

# 自动演示时间线（720 帧轮播全部视图模式与消融，供录像）
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance -- \
  --model <模型路径> --demo

# 重录本页嵌的两段录像
python scripts/record-raylib-asset-acceptance.py
```

录像取样走宿主静帧合同（`LUDOTS_TAKE_SCREENSHOT_PATH` + `LUDOTS_TAKE_SCREENSHOT_FRAMES`，与引擎画廊 `scripts/record-engine-galleries.py` 同一机制），ffmpeg 拼接成 `play.mp4`；证据清单见 `artifacts/evidence/raylib_asset_acceptance_demo/manifest.json` 与 `artifacts/evidence/raylib_asset_acceptance_obj/manifest.json`。

## 工程索引（改代码先看这里）

| 想改什么 | 动哪里 |
|---|---|
| 工具主体（拖放/HUD/旋钮/演示时间线） | `src/Apps/Raylib/Ludots.App.RaylibAssetAcceptance/Program.cs` |
| 逐材质 PBR 着色（贴图通道 + 视图模式 + 消融） | `src/Platforms/Desktop/model_file_lit.vs` / `model_file_lit.fs`，渲染类 `src/Client/Ludots.Raylib.Render/Rendering/RaylibFileModelLit.cs` |
| 格式转换 / 缓存 | `src/Client/Ludots.Raylib.Render/Rendering/RaylibModelFileConverter.cs`（新增格式在此登记并补测试） |
| 引擎装载入口（#1050 修复点） | `RaylibModelFileLoader.PrepareNativeLoadable`；调用方 `RaylibPrimitiveRenderer` / `RaylibGpuSkinnedModelCache` |
| 转换回归测试 | `src/Tests/RaylibAdapterTests/RaylibModelFileConverterTests.cs` |
| 录像脚本 | `scripts/record-raylib-asset-acceptance.py` |
| 着色器合同矩阵 | `src/Tests/RaylibAdapterTests/RaylibShaderContractTests.cs`（`model_file_lit.fs` 在接收端清单里） |

已知边界（如实）：glTF `alphaMode=BLEND` 的半透明不做排序（用 `K` 旋钮对比阈值）；法线贴图走屏幕空间 TBN（无预计算切线）；assimp 转换默认不开 FlipUVs，若首个真实资产贴图上下颠倒，在 `RaylibModelFileConverter.BuildPostProcessSteps` 加一行 `PostProcessSteps.FlipUVs`。
