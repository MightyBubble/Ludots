# Quarks Particle Schema

本页是 Ludots Quarks 粒子资产的正式合同。写法源在这里；运行时解析器、Showcase 验收与 Raylib 后端都必须对齐本页，禁止平行口。

## 概述

- 粒子 VFX资产放在 `Presentation/particle_vfx.json`。
- Mesh 侧 VFX 只保留句柄：`mesh_assets.json` 的 `vfx` 只能写 `particleVfxId`。
- Raylib 只消费 finalization 后的 VFX leaf 与粒子快照，不拥有效果 key 解析，不创建第二套粒子 schema。

## 结构

```text
particle_vfx.json
  └─ particle VFX
      ├─ version = quarks.ludots.v1
      ├─ spawnMode / shape / renderMode / blendMode / primitive
      ├─ capacity + emission（maxParticles、seed、duration、rate、burst）
      ├─ shape params + start ranges + curves
      └─ optional: textureSheet / stretchedLengthScale / trailLengthSeconds

mesh_assets.json
  └─ VFX asset
      └─ vfx.particleVfxId → particle_vfx.id

host_assets.json
  └─ Billboard texture rows（billboard / stretched billboard 必填）
```

## 详情

### 版本与字段

| 字段 | 要求 |
|---|---|
| `version` | 必须是 `quarks.ludots.v1` |
| `spawnMode` | `Once` / `Loop`；只写在粒子资产上 |
| `renderMode` | `Billboard` / `StretchedBillboard` / `Primitive` / `Trail` |
| `primitive` | `Sphere` / `Cube`；`Primitive` 模式按此画原语，不是 mesh 资产绑定 |
| `startLife` | `[min, max]`，且 `min > 0` |
| `textureSheet` | 仅 Billboard / StretchedBillboard 必填 |
| `stretchedLengthScale` | 仅 StretchedBillboard 必填，且 `> 0` |
| `trailLengthSeconds` | 仅 Trail 必填，且 `> 0`；尾迹长度 = `velocity * trailLengthSeconds` |

禁止字段：

- `overflowPolicy`：容量满时固定丢弃最新 spawn，并累计 `RejectedSpawnCount`，不开放作者策略口。
- mesh `vfx.spawnMode`、`vfx.emitter`、`vfx.particleSystem`、legacy 颜色字段。

### Mesh 句柄

```json
{
  "id": "effect.camera.projection_cue",
  "type": "Primitive",
  "primitiveKind": "Sphere",
  "vfx": {
    "particleVfxId": "camera.projection_cue.particles"
  }
}
```

`spawnMode` 从被引用的粒子资产读取。Prefab VFX part 若再写 `spawnMode`，只能与粒子资产一致；不一致直接失败。

### 贴图

Billboard / StretchedBillboard 必须：

1. 粒子资产声明 `textureSheet.textureAssetId`
2. 对应 mesh 资产类型为 `Billboard`
3. `host_assets.json` 为 Raylib 提供可解析且存在的 `sourceUris`

Raylib 加载贴图失败必须立刻抛错（带上 URI 与失败原因），禁止缓存“未加载”状态后继续画。

## 场景

玩家打开 Raylib VFX Forge：九个粒子 VFX按 `particle_vfx.json` 注册，mesh VFX 只挂 `particleVfxId`，三张 flipbook PNG 必须真实落盘；缺任何一张贴图时客户端启动后绘制路径 fail-loud，而不是静默空画。

## 边界

- Core 拥有粒子 registry / parser / runtime snapshot。
- Raylib 只做渲染后端。
- 不在 adapter 里硬编码效果参数；观感字段进粒子资产。
- `Primitive` 不是 mesh 粒子；若未来要 mesh 粒子，必须新增真正的 mesh 资产绑定合同，不能复用这个名字偷渡。

## UAT

```gherkin
Feature: Quarks particle authoring contract
  Scenario: Mesh VFX is a particle handle
    Given a mesh asset declares vfx.particleVfxId
    And the referenced particle VFX exists in particle_vfx.json
    When Presentation config loads
    Then the mesh VFX spawnMode equals the particle VFX spawnMode
    And mesh vfx contains no spawnMode, emitter, or particleSystem fields

  Scenario: Missing flipbook texture fails loud
    Given a Billboard particle VFX references a texture asset
    And host_assets sourceUri points to a missing PNG
    When Raylib draws that particle VFX
    Then the renderer throws with the failed URI
    And it does not cache a unloaded texture
```
