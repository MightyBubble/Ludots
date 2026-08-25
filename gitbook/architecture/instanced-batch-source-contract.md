# Instanced Batch 外部 Source Contract

本文定义 presenter-owned instanced batch 在 Core 层引用外部实例数据源的正式契约。

## 目标

大规模实例集合可以把具体 transform 数据放在外部资产中，但 Core 仍然拥有语义真相：

- `instanced_batches.json` 是 batch group 的 authoring SSOT。
- Core 负责校验 group 的稳定地址、mesh/material 引用、source metadata 和实例数量。
- `InstancedBatchFactorizedSourceLoader` 通过 VFS 读 `assetUri` 指向的 factorized（SoA）变换数据并断言 authored 实例数。
- `InstancedBatchEmissionSystem` 只按 Core-authored `InstanceCount` 做 progressive chunk。
- adapter 消费 Core 已加载的 factorized 数据，不解析 `assetUri`，不拥有实例数量或 batch 语义。

## Group Authoring

每个 group 必须且只能声明一种实例来源：

```json
{
  "id": "group.example",
  "meshAssetId": "mesh.example",
  "bucketId": "bucket.example",
  "instanceSpanId": "span.example",
  "source": {
    "format": "ludots.instanced_transform_factorized.v1",
    "assetUri": "ExampleMod:assets/Presentation/example_instanced_source.json",
    "setId": "set.alpha",
    "instanceCount": 50000,
    "groundToVisualHeightmap": true
  }
}
```

Inline `transforms` 保持原有语义。外部 `source` group 的 `transforms` 在 Core 中为空数组，`InstanceCount` 来自 `source.instanceCount`。

## Source Fields

| 字段 | 要求 | 语义 |
|------|------|------|
| `format` | 必填非空字符串 | 外部实例数据格式标识，必须精确等于 `ludots.instanced_transform_factorized.v1`，不接受别名 |
| `assetUri` | 必填非空字符串 | VFS 可解析的 authored asset URI（`ModId:path`） |
| `setId` | 必填非空字符串 | 外部资产内的实例集合标识 |
| `instanceCount` | 必填正整数 | Core progressive submission 使用的实例数量 |
| `groundToVisualHeightmap` | 可选 bool，默认 `false` | 声明该 source 需要 Core visual-height grounding 语义 |

`groundToVisualHeightmap` 不是 adapter 私有开关。若启用，它必须绑定到 Core-owned visual height contract；adapter 不得用自己的地面高度真相替代。

## Loader Rules

`InstancedBatchAssetConfigLoader` 必须拒绝以下形状：

- 同时声明 `transforms` 和 `source`。
- 既没有 `transforms` 也没有 `source`。
- `source` 不是 object。
- `source` 缺少 `format`、`assetUri`、`setId` 或 `instanceCount`。
- `source.instanceCount <= 0`。
- `source.groundToVisualHeightmap` 不是 bool。
- `source` 或 group 上出现未声明字段。

## Factorized Source Loader（#1152 已实现）

`InstancedBatchFactorizedSourceLoader` 在 config load 阶段通过 VFS 读 `assetUri`，严格解析
`ludots.instanced_transform_factorized.v1` 资产并产出 SoA 变换数组（`positionCm`/`rotation`/`scale` 按分量存数组）：

```json
{
  "format": "ludots.instanced_transform_factorized.v1",
  "sets": {
    "set.alpha": {
      "instanceCount": 50000,
      "positionCm": { "x": [...], "y": [...], "z": [...] },
      "rotation": { "x": [...], "y": [...], "z": [...], "w": [...] },
      "scale": { "x": [...], "y": [...], "z": [...] }
    }
  }
}
```

规则：

- 顶层字段只能是 `format` 与 `sets`；`format` 必须精确等于 `ludots.instanced_transform_factorized.v1`。
- `sets` 必须包含 group 声明的 `setId`，缺失即失败。
- set 内 `instanceCount` 必须为正整数且与 authored `source.instanceCount` 完全一致（实例数断言）。
- `positionCm` 必填；`rotation`/`scale` 可选（缺省为单位四元数/单位缩放）。每个分量数组长度必须精确等于 `instanceCount`，值必须有限。
- 任何未声明字段、缺失 set、长度不齐、非有限值或 count 不一致都 fail fast，且不产生部分注册。

## Adapter Boundary

Core 不做这些事情：

- 不解析商业引擎资源路径。
- 不创建 UE5、Unity 或 Raylib resident handles。
- 不引入 importer/map side channel。
- 不把外部实例展开为一实体一实例。

平台适配层通过 typed InstancedBatch request 消费 Core 已加载的 factorized SoA 数据（`group.FactorizedSource`），缓存只是执行细节，不能成为语义 SSOT。`groundToVisualHeightmap` 的 grounding 必须在 lane 边界采样 Core-owned `IVisualHeightmap` 服务；Core visual height 服务不可用时 fail loud，adapter 不得用自己的地面高度真相替代。
