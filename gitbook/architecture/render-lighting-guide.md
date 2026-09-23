# 渲染光照栈与下游使用指南

光照栈分两条车道，合同词汇一致，按物体形态选择：

| 车道 | 适用 | 入口 |
|---|---|---|
| 合批管线 | 静态网格群 / 蒙皮人群（大量实例） | `PrimitiveDrawItem` → `RaylibPrimitiveRenderer`（`instancing` / `skinning_instanced` 着色器） |
| 单物体通道 | 道具、少量模型、验收场景陪衬 | `RaylibLitModel`（`model_lit` 着色器） |

两车道都是 **Cook-Torrance GGX 单灯 metallic-roughness + split-sum 天空 IBL** + 光照总线 `RaylibFrameLighting`（光向/环境/光色/强度/雾/视点）。

## 单物体带光照：RaylibLitModel

```csharp
var lit = new RaylibLitModel();            // LoadShader(model_lit)，uniform 合同 fail-loud
var lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.55f);

// 每帧一次：光照总线 + 天空 IBL 色（含 split-sum 环境烘焙/节流重烘）
lit.BeginFrame(lighting, camera.position);

// 路径 A：共享 mesh 直接画（道具）
lit.DrawMesh(mesh, transform, tint, roughness: 0.8f, metallic: 0f);

// 路径 B：挂到模型材质，随后用标准 DrawModelEx（模型类）
lit.AttachToModel(model);
lit.ApplyDrawUniforms(tint, roughness, metallic);
Rl.DrawModelEx(model, position, axis, angle, scale, color);
```

画廊范式：`GalleryLitProps`（共享立方体/球 + 单实例）与 `GpuSkinningScene`（自有实例）。

## 平面投影阴影：RaylibPlanarShadows

```csharp
var shadows = new RaylibPlanarShadows(alpha: 110);
shadows.GroundY = 0.21f;                   // 接收平面高度（必须高于地面几何顶面）

// 模型：换装默认着色器 → planar 矩阵 → 还原
shadows.DrawModelShadow(model, position, rotationAngleY, scale, lighting.SunDirectionToward);

// mesh：
shadows.DrawMeshShadow(mesh, modelTransform, lighting.SunDirectionToward);
```

限制：接收面为水平面（`GroundY`）；重叠投影会加深。非平面接收需等 native 升级后的 shadow map 车道。

## 材质标量 PBR 合同

材质资产 JSON（`Presentation/materials.json`）新增可选标量字段（贴图槽位不变：sourceUris[0..3] = albedo/roughness/metallic/normal，走 host_assets）：

```json
{ "id": "stone.wall", "domain": "Surface", "flags": "Opaque", "roughness": 0.7, "metalness": 0.1 }
```

- 有贴图 → 贴图优先（标量忽略）；无贴图 → 标量直达 `uRoughness`/`uMetallic`。
- 越界（非 [0,1] / 非数字）装载期 fail-loud。
- 合批管线与单物体通道同一合同（`MaterialAssetDescriptor.Roughness/Metalness`）。

## split-sum 天空 IBL（预滤波环境立方图 + BRDF LUT）

`RaylibSkyIbl`（Ludots.Raylib.Render）承担环境烘焙，两车道（`model_lit` / `instancing`）的 ambientSpecular 统一升级为真 split-sum：

- **环境立方图**：CPU 按解析天空函数（昼夜 ramp 派生的天顶/地平线/地面色 + 太阳光晕，与 `skybox.fs` 同形）逐像素写 6×64² RGBA，mip 链每级按 `roughness = mip / 6` 做 GGX 重要性采样预滤波（mip0 为镜面直采）；经 native 5.5 的 `rlLoadTextureCubemap` 上传（vendored 绑定缺该入口，`RaylibSkyIblInterop` 本地声明，数据布局逐 mip 6 face 连续）。
- **BRDF LUT**：512² RGBA（R=specular scale、G=bias），C# 数值积分 GGX/NdotV（Hammersley 256 采样），`GenImageColor`→`LoadTextureFromImage` 装载。
- **着色器**：`ambientSpecular = textureLod(uPrefilteredEnv, reflect(-V,N), roughness*6) × (F0*brdf.r + brdf.g) × uEnvSpecular`；环境漫反射保持半球近似（zenith/ground 按法线混合）。
- **接线**：cubemap 走 `MATERIAL_MAP_CUBEMAP` 槽位、LUT 走 `MATERIAL_MAP_BRDF` 槽位（native 5.5 `DrawMesh`/`DrawMeshInstanced` 对 CUBEMAP 槽以 `GL_TEXTURE_CUBE_MAP` 绑定并回填 uniform=槽位号）；`RaylibLitModel` 构造期预烘 LUT、`BeginFrame` 烘/重烘环境图并挂材质槽；`RaylibPrimitiveRenderer` 在 `ApplyFrameLighting` 与实例化绘制前同样驱动；DrawModelEx 路径用 `BindIblToMaterial` 挂槽（见 GpuSkinningScene）。

**CPU 烘焙取舍**：零额外 GL pass；BRDF LUT 与光照无关（构造期一次性，Debug 约 250ms；两车道各持一份），环境图随昼夜相位重烘（步进 >0.02 才重烘，单次约 20ms）——重烘即换纹理对象（id 变化），材质槽每帧重挂覆盖。GPU 端无逐帧成本（对比基线 avg +0.5ms 内，为采样与绑槽开销）。`uEnvSpecular` 为强度闸（默认 1.0）。

## native raylib 5.0 已知限制（绕法已内建）

| 限制 | 现象 | 绕法（本栈已采用） |
|---|---|---|
| （5.5 已修复）矩阵 uniform、骨骼导出、Mesh 布局 | — | native 已升级官方 5.5；历史绕法见 git 历史 |
| GLTF 模型 mesh 按值封送布局错位 | `DrawMesh(model.meshes[i], …)` AV 崩溃 | 模型路径一律 `DrawModelEx`（内部指针布局一致） |
| `SHADER_UNIFORM_VEC4` 通道不稳 | vec4 uniform 不生效 | 着色器矩阵列用 vec3 语义（`model_lit` 不依赖 vec4 uniform） |

新增着色器时遵守：矩阵不进 uniform、模型 mesh 不按值过边界。

## 宿主接入（表现 showcase 侧）

宿主 `RaylibHostLoop` 已对地形/高度图/图元合批喂光照；单物体通道供 showcase/编辑器道具使用——构造 `RaylibLitModel` 后在 Presenter 资产绑定的模型上 `AttachToModel`（DrawModelEx 路径再每帧 `BindIblToMaterial` 挂 IBL 槽），HUD/道具即携带 GGX 明暗与天空 IBL。
