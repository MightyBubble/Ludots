# 渲染光照栈与下游使用指南

光照栈分两条车道，合同词汇一致，按物体形态选择：

| 车道 | 适用 | 入口 |
|---|---|---|
| 合批管线 | 静态网格群 / 蒙皮人群（大量实例） | `PrimitiveDrawItem` → `RaylibPrimitiveRenderer`（`instancing` / `skinning_instanced` 着色器） |
| 单物体通道 | 道具、少量模型、验收场景陪衬 | `RaylibLitModel`（`model_lit` 着色器） |

两车道都是 **Cook-Torrance GGX 单灯 metallic-roughness** + 光照总线 `RaylibFrameLighting`（光向/环境/光色/强度/雾/视点）。

## 单物体带光照：RaylibLitModel

```csharp
var lit = new RaylibLitModel();            // LoadShader(model_lit)，uniform 合同 fail-loud
var lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.55f);

// 每帧一次：光照总线 + 天空半球 IBL 色
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

## 解析式天空 IBL

`RaylibFrameLighting` 从昼夜 ramp（`ambient_day_ramp.json`）派生 `SkyZenithColor` / `SkyGroundColor`；`model_lit` 按法线朝向混合作环境漫反射、Fresnel 加权作环境镜面近似。无立方图 / LUT 依赖；昼夜联动免费获得。自定义光照时直接构造 `RaylibFrameLighting` 后调 `SetDayPhase` 即可。

## native raylib 5.0 已知限制（绕法已内建）

| 限制 | 现象 | 绕法（本栈已采用） |
|---|---|---|
| 矩阵 uniform 通道不可用（`SetShaderValueMatrix` / MAT4） | uniform 保持零矩阵 | 模型/投影矩阵一律走 `DrawModelEx`/`DrawMesh` 的 transform 原生通道 |
| GLTF 模型 mesh 按值封送布局错位 | `DrawMesh(model.meshes[i], …)` AV 崩溃 | 模型路径一律 `DrawModelEx`（内部指针布局一致） |
| `SHADER_UNIFORM_VEC4` 通道不稳 | vec4 uniform 不生效 | 着色器矩阵列用 vec3 语义（`model_lit` 不依赖 vec4 uniform） |

新增着色器时遵守：矩阵不进 uniform、模型 mesh 不按值过边界。

## 宿主接入（表现 showcase 侧）

宿主 `RaylibHostLoop` 已对地形/高度图/图元合批喂光照；单物体通道供 showcase/编辑器道具使用——构造 `RaylibLitModel` 后在 Presenter 资产绑定的模型上 `AttachToModel`，HUD/道具即携带 GGX 明暗与天空 IBL。
