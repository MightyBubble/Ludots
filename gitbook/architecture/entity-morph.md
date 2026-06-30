# Entity Morph

本页定义 Core **Entity Morph** 管线的正式边界、配置 SSOT 与 `CreateUnit` 的选型差异。

## 1 职责

Morph 负责在 GAS effect 触发后，将 **source 实体** 替换为 **目标模板** 实例，并按 profile 继承身份、属性、标签与选择集。

Morph **不负责**：

- 地形 / 阻挡 / 建造规则校验（属于 ability propose 阶段的独立 placement validation 基础设施）
- 绕过 GAS 直接修改实体

## 2 链路

```text
Effect preset Morph
  -> BuiltinHandlers.HandleMorphEntity (OnApply, handler 内预校验 placement)
  -> RuntimeEntityMorphQueue
  -> RuntimeEntityMorphSystem (EffectProcessing, 在 EffectProcessingLoopSystem 之后)
```

坐标解析 SSOT：`EffectTargetPointResolver`（与 `CreateUnit` 共用）。

物化后处理与 spawn 对齐：

- `EntityBuilder` 模板实例化
- `RuntimeEntityMapOwnershipSupport.TryCopyMapEntityFromSource`
- `PerformerEntitySpawnBootstrap.TryBootstrap`

## 3 配置 SSOT

| 资源 | 路径 | 作用 |
|------|------|------|
| Morph profiles | `assets/Configs/GAS/morph_profiles.json` | placement、stableId、inherit 策略 |
| Effect templates | `assets/Configs/GAS/effects.json` | `presetType: Morph` + `morph` 块 |
| Preset type | `assets/Configs/GAS/preset_types.json` | `Morph` / `MorphEntity` handler |

`morph_profiles.json` 所有语义字段必须显式配置，loader 不提供隐式 default。

### 3.1 Profile 字段

- `placement`: `AtSource` | `AtTargetPoint` | `PreservedExplicit`
- `stableIdPolicy`: `AllocateNew` | `Transfer`
- `destroySource`: bool（必填）
- `inherit.identity`: `PlayerOwner` | `Team`（可扩展，注册表见 `MorphIdentityInheritanceRegistry`）
- `inherit.attributes.mode`: `None` | `IntersectByName` | `AllDefined`
- `inherit.attributes.source`: `Base` | `Current`（mode 非 None 时必填）
- `inherit.tags.mode`: `None` | `StripListed` | `CarryListed` | `StripListedAndCarryListed`
- `inherit.effects.mode`: `StripAll`（必填）
- `inherit.selection.replaceSourceInAllSets`: bool（必填）

### 3.2 参考 effect

`Effect.Morph.Reference.DeployConsumeSource` 演示 RTS deploy consume source 的配置组合；目标模板 id 由具体 Mod 提供。Core 参考 profile 的 `inherit.tags.mode` 为 `None`；Mod 可通过 config merge 扩展 strip/carry 列表（tag 须已注册）。

## 4 与 CreateUnit 的选型

| 场景 | 推荐 |
|------|------|
| 在落点生成新单位，源单位保留 | `CreateUnit` |
| 源单位被建筑/新模板替换，继承 stableId / 归属 / 选择 | `Morph` + `morph.rts.deploy_consume_source` |

## 5 运行时服务

- `CoreServiceKeys.RuntimeEntityMorphQueue`
- `CoreServiceKeys.RuntimeEntityMorphReceiptQueue`
- `CoreServiceKeys.MorphProfileRegistry`

容量配置：`presentation.runtimeEntityMorphQueueCapacity`、`presentation.runtimeEntityMorphReceiptQueueCapacity`（`game.json`）。

## 6 实现入口

- `src/Core/Gameplay/Morph/`
- `src/Core/Gameplay/GAS/EffectTargetPointResolver.cs`
- `src/Core/Gameplay/GAS/BuiltinHandlers.cs` (`HandleMorphEntity`)
- `src/Tests/GasTests/MorphArchitectureTests.cs`
