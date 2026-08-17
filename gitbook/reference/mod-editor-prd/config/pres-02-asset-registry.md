# pres-02 配置说明 · 表现资产清单

> 配置写法与行为。第一性需求见 [pres-02 PRD](../prd/pres-02-asset-registry.md)；编辑器需求见 [UXD](../uxd/pres-02-asset-registry.md)；现状见 [reference](../reference/pres-02-asset-registry.md)。

## 1. 示例配置

核心 mod 真实资产（目录条目 `Presentation/mesh_assets.json`（根数据为空） 全量、material_assets.json 全量）：

```json
[
  { "id": "cube", "type": "Primitive", "primitiveKind": "Cube" },
  { "id": "sphere", "type": "Primitive", "primitiveKind": "Sphere" }
]
```

```json
[ { "id": "default_surface", "domain": "Surface" } ]
```

host_assets（教学骨架，合成；字段取自 loader 合同）：

```json
[
  {
    "id": "raylib.hero_mesh",
    "backendId": "raylib",
    "assetKind": "Mesh",
    "assetId": "core.hero.mesh",
    "sourceUris": [ "models/core/hero.glb" ]
  }
]
```

instanced_batches（教学骨架，合成；全仓库尚无真实行数据，见 todo/domains.md D1）：

```json
[
  {
    "id": "batch.trees",
    "renderPath": "Instanced",
    "ownerStableId": "tree_spawner",
    "groups": [ { "meshId": "tree", "materialId": "default_surface" } ],
    "behaviors": [ { "slot": "body", "kind": "AssetBinding" } ],
    "progressiveSubmission": { "maxDrawsPerFrame": 8 }
  }
]
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| mesh_assets | `type` | Primitive / Model / Billboard / VFX 白名单；写 Prefab 直接抛错指路表现器 |
| mesh_assets | `primitiveKind` | 图元形状（Cube/Sphere/…），type=Primitive 时生效 |
| mesh_assets | `vfx` | VFX 型资产引用（particle_vfx 表注册项） |
| mesh_assets | `sourceUris` | **禁止字段**——出现即失败 |
| material_assets | `domain` | 必填枚举，材质归属域 |
| material_assets | `flags` | 含 blend mode 在内的表面旗标 |
| host_assets | `backendId` | 行级过滤键：非当前后端的行被忽略 |
| host_assets | `assetKind` / `assetId` | 指向 mesh 或 material 表的逻辑 id |
| host_assets | `sourceUris` | 平台真实路径（host 表是唯一合法位置） |
| instanced_batches | `renderPath` / `ownerStableId` | 批次渲染通道与属主 |
| instanced_batches | `groups` | 非空数组；mesh/material 组合 |
| instanced_batches | `customDataChannels` | 每实例自定义数据通道 |
| instanced_batches | `behaviors` | 行为内联（与 pres-01 behaviors 同构） |
| instanced_batches | `progressiveSubmission` | 渐进提交预算，帧内分摊绘制 |

## 3. 文件结构

目录条目 `Presentation/*`（引擎默认根多数为空，由 mod 贡献） 下四表：mesh_assets.json、material_assets.json、host_assets.json、instanced_batches.json（均 ArrayById，详见事实页目录计数）。引擎侧另有 lod_profiles.json 与 particle_vfx.json：前者只有引擎默认（id + high/medium/low 各 maxDistanceCm/minScreenCoverage01），后者根表空、内容随 mod 下沉。

## 4. 运行时加载效果

mesh/material 逐条注册进对应注册表；host 由宿主合成器按 backendId 过滤后挂真实路径；instanced 批次加载时解析 mesh/material/属性/事件键为 id。**生效级别：重启**（资产身份扩张禁热）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| mesh 条目 type 非法/缺失/Prefab | 启动失败，指明条目 |
| mesh/material 出现 sourceUris | 启动失败 |
| material 缺 domain | 启动失败 |
| host 的 assetId 未注册 | 启动失败，指明行 |
| 批次 groups 为空 | 启动失败 |

## 6. 实例

- 目录条目 `Presentation/mesh_assets.json`（根数据为空）、material_assets.json
- `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/host_assets.json`（若存在；否则教学骨架）（host 真实用法）
- instanced_batches：无真实数据（D1）

**相关文档**：[pres-02 PRD](../prd/pres-02-asset-registry.md) · [pres-01 配置说明](pres-01-performers.md)
