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

## 方向光 Shadow Map：RaylibDirectionalShadowMap

平面投影阴影（`RaylibPlanarShadows`）已退役，全场景统一走方向光 shadow map 车道：

```csharp
var shadow = new RaylibDirectionalShadowMap();   // 配置走环境配置树 RaylibShadowConfig(MapSize, ReceiverBiasWorld)
shadow.BeginFrame(lighting.SunDirectionToward, sceneCenter, sceneRadius);
// 各车道把投影体灌进深度 pass：
primitiveRenderer.DrawShadow(snapshot, shadow, meshes, camera);   // 图元/模型/billboard
primitiveRenderer.DrawShadow(skinnedBatch, shadow, meshes);       // GPU 蒙皮合批
shadow.EndFrame();
```

- **深度编码**：硬件深度打包进 RGBA 颜色 RT（RGB 24 位进位），接收端经 `uLightSpaceMatrix` 投影 + 3×3 PCF；RT 点采样 + 钳制包裹（bilinear 解码打包深度在数学上是错的）。
- **shader 单一来源**：四个接收 shader（`model_lit.fs` / `instancing.fs` / `skinning_instanced.fs` / `terrain.fs`）经 `// ludo:include shadow_sampling.glsl.inc` 共享同一块采样代码，`RaylibShaderLoader` 运行时展开（递归 ≤4、fail-loud、防路径穿越）；合同测试断言"禁内联 + 展开后逐字一致"双守卫。
- **投影资格**（收口于 `DrawShadowLeafAsset` 单点，`RaylibMaterialDrawState.CastsShadow`）：

| 材质 blend | 是否投影 | 说明 |
|---|---|---|
| Opaque | 是 | 实体深度 |
| Cutout | 是 | `shadow_depth_cutout` 采样 albedo alpha 打孔（阈值 `DefaultVegetationAlphaCutoff`），树冠影呈斑驳形态而非实心矩形 |
| AlphaBlend / Additive | 否 | alpha 是发光/覆盖语义，不构成遮挡体 |
| VFX / Decal | 否 | 条目级跳过 |

- **深度 pass shader 族**：`shadow_depth`（实体）、`shadow_depth_instanced`（ISM 合批）、`shadow_depth_skinning_instanced`（蒙皮合批）、`shadow_depth_cutout`（镂空 billboard）；镂空与实体打包编码逐字一致，合同测试锁定。
- 无 `1/2048` 之类的硬编码：`uShadowMapTexel` 由 `RaylibShadowConfig.MapSize` 推导。

## 材质标量 PBR 合同

材质资产 JSON（`Presentation/materials.json`）新增可选标量字段（贴图槽位不变：sourceUris[0..3] = albedo/roughness/metallic/normal，走 host_assets）：

```json
{ "id": "stone.wall", "domain": "Surface", "flags": "Opaque", "roughness": 0.7, "metalness": 0.1 }
```

- 有贴图 → 贴图优先（标量忽略）；无贴图 → 标量直达 `uRoughness`/`uMetallic`。
- 越界（非 [0,1] / 非数字）装载期 fail-loud。
- 合批管线与单物体通道同一合同（`MaterialAssetDescriptor.Roughness/Metalness`）。

## 材质实例与 shaderKey（对齐商业引擎心智）

材质是三轴正交的作者面，均数据驱动、解析期 fail-loud：

- **材质实例链**：`MaterialAssetDescriptor.ParentKey` 指向父材质，`MaterialAssetResolver` 沿链合并——子材质只写差异字段（换贴图、改 roughness/metalness、改 blend flags），未写字段继承父级；`IRenderMaterialAssets.TryResolve` 给解析后视图。
- **自定义着色行为**：`ShaderKey`（默认 `lit`）+ `RaylibShaderCatalog` 注册表分派。`RaylibLaneShader` 接线契约挂在实例化合批车道（`RegisterInstancingShader(key, lane)`）；非实例化车道遇非默认 shaderKey 一律 fail-loud，不静默降级。
- **命名参数直推 uniform**：`FloatParams` / `ColorParams` 字典按键名直达 shader uniform（如 emissive 强度/颜色），实例材质改参数即生效。

范式见引擎画廊 `material_binding` 场景：`[iron] 基础金属 → [rusty] 实例覆盖 albedo+roughness`、`[emissive] shaderKey=emissive 自发光车道 → [hot] 实例参数覆盖`。

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
