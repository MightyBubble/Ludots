# W0 #1322 复核请求：raylib native 资源驻留计量 + benchmark 阈值断言

仓库当前工作树的未提交改动（只看 `git diff -- src/` 与 `src/` 下新文件）是 #1322 W0 的实现，请做代码复核。用 `git diff -- src/` 看改动；新增文件：`src/Client/Ludots.Raylib.Render/Rendering/RaylibNativeResourceLedger.cs`、`RaylibNativeResources.cs`，测试 `src/Tests/RaylibAdapterTests/RaylibNativeResourceContractTests.cs`、`RaylibNativeResourceLedgerTests.cs`，画廊退出打印在 `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Program.cs`。

## 设计（请审查而非复述）

1. 门面 `RaylibNativeResources`：包装 Rl 的 Load/Unload（Texture/TextureFromImage/TextureCubemap(RenderTexture/Model/Shader/ShaderFromMemory/MaterialDefault/Sound/SoundAlias/UploadMesh/GenMeshCube/GenMeshSphere）。生产代码（Raylib.Render/Client.Raylib/Adapter.Raylib/Apps/Raylib 四根）禁止直连，由源码契约测试强制。cubemap 经 DllImport rlLoadTextureCubemap（从 RaylibSkyIbl 迁入，删了本地 interop 类）。
2. 台账 `RaylibNativeResourceLedger`：Track/Untrack by (kind, ulong identity)；字节为估算；身份失配只计数不抛（不得改变渲染行为）。身份键：纹理/着色器=GL 名，网格=VAO 名，模型=首网格 VAO 名，材质=maps 指针，声音=buffer 指针。
3. 阈值断言：50k HUD <512B/帧（基线 382.7）、skia hotpath 三场景 <64B/帧（基线 0.0）。
4. 画廊 RunScene 退出一行账本快照（可查询面）。

## 已有证据

- 新测试 6/6 通过；RaylibAdapterTests 全量 173/173（改 cubemap 前跑过，之后重跑中）；两个 benchmark 测试通过。
- 四个 GL 场景（primitives/crowd_anim/lighting/skia_overlay）截图正常、退出 resident=0 outstanding=0 tracked==untracked、unknownUntrack=0。
- 发现并如实暴露的既有行为：retracked=3 = PrimitiveRenderer 基础网格 GenMesh 后补色再 UploadMesh 的双重上传（W0 不改行为，留记录）。

## 请重点审查

1. **像素格式 bpp 表**（RaylibNativeResources.EstimateTextureBytes 的 switch）：数值是我按 raylib.h PixelFormat 记忆写的（1-22），仓库里没有可对照的枚举。请对照 raylib 5.5 的 raylib.h 核实每个值，错了预算信号会失真。
2. 身份键健壮性：材质 maps 指针/声音 buffer 指针截断为 ulong 是否有碰撞风险；模型取首网格 VAO 名在 meshCount=0 或共享网格时的行为。
3. 有没有漏掉的原生资源加载/释放路径（例如我明确排除的 Image/LoadImage/UnloadImage、ModelAnimations、GenImageColor——CPU 侧不进台账，这个排除是否合理）。
4. 行为零改动声明是否成立（所有包装都是直通 + 记账；LoadTextureCubemap 把原来 SkyIbl 的 id==0 throw 语义移进门面）。
5. 线程安全与热路径：Track/Untrack 都在加载/卸载（冷路径）与 lock 内；有没有我没想到的每帧调用点。
6. 契约测试的可绕过性。

输出：按严重度列问题（阻断/应改/可留），每条给 file:line 与理由；没有问题的项明确说通过。
