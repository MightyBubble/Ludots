# Map-Owned Participant Contract

本文定义 Ludots Core 中 team/player participant 的正式契约。participant 真相必须挂靠在 map-authored entity 上，通过正常 map load 生命周期进入运行时；禁止再引入第二套 participant 容器、扫描 fallback 或隐藏默认 player id。

## 1. 单一容器

`MapConfig.Entities` 是 map-owned entity 的唯一正式容器。

允许的 map entity 形态有两类：

- spatial entity：带 `WorldPositionCm` 等空间组件，进入空间分区与表现链路
- logical entity：不带空间组件，只承担 identity、resource、relationship 或 control 语义

team/player representative entity 可以是 logical entity。Core 不得因为 participant authoring 缺少空间组件而补造临时 entity。

相关源码：

- `src/Core/Config/MapConfig.cs`
- `src/Core/Systems/MapLoader.cs`
- `src/Core/Systems/MapLoadEntityIndex.cs`

## 2. Map Authoring Shape

正式 authoring 入口如下：

- `EntitySpawnData.InstanceId`
- `MapConfig.Teams`
- `MapConfig.Players`
- `MapConfig.ParticipantRelationships`

最小语义：

- `Teams[*].RepresentativeInstanceId` 绑定到一个 map-authored entity
- `Players[*].RepresentativeInstanceId` 绑定到一个 map-authored entity
- `Players[*].TeamId` 显式声明 player 所属 team

`InstanceId` 是 representative binding 的稳定键。author 选择不写 `InstanceId` 时，该 entity 只是普通 map entity；一旦写了，就必须非空、已 trim、且在同一 map 内唯一。

相关源码：

- `src/Core/Config/MapConfig.cs`
- `src/Core/Map/MapManager.cs`
- `src/Core/Systems/MapLoadEntityIndex.cs`

## 3. Representative Entity Truth

map load 时，Core 通过 `MapLoadEntityIndex` 把 participant binding 解析为 representative entity，并把 identity 写回实体本身：

- team representative：`TeamIdentity { TeamId }`
- player representative：`PlayerIdentity { PlayerId }`
- player representative 同时写入 `PlayerOwner { PlayerId }`
- player representative 同时写入 `Team { Id = TeamId }`

这意味着 team/player 的 resource、tag、effect 都直接挂在 representative entity 上，沿用现有 ECS/GAS primitive：

- `AttributeBuffer`
- `GameplayTagContainer`
- `TagCountContainer`
- `ActiveEffectContainer`

禁止把 participant resource 再平行存到 team/player 私有字典中作为主真相。

相关源码：

- `src/Core/Gameplay/Components/IdentityComponents.cs`
- `src/Core/Gameplay/Teams/ParticipantBindingResolver.cs`
- `src/Core/Gameplay/Components/TeamIdentity.cs`

## 4. Relationship Truth

participant relationship 的正式真相是 entity relationship：

- team-team：team representative entity 与 team representative entity
- player-player：player representative entity 与 player representative entity
- player-team：player representative entity 与 team representative entity

`TeamManager` 和 lookup service 只允许作为派生缓存存在：

- `TeamEntityLookup`：`TeamId -> representative entity`
- `PlayerEntityLookup`：`PlayerId -> representative entity`
- `TeamManager`：从 focused map/session 的 participant relationship 派生出的 team hot-path cache

focused map 切换时，lookup object identity 必须稳定；系统拿到的是同一个 service object，由新 session 内容覆盖，而不是替换 service 实例。

相关源码：

- `src/Core/Gameplay/Teams/TeamEntityLookup.cs`
- `src/Core/Gameplay/Teams/PlayerEntityLookup.cs`
- `src/Core/Gameplay/Teams/TeamManager.cs`
- `src/Core/Engine/GameEngine.MapLoadLifecycle.cs`

## 5. Local Player Binding

正式 local player 链路如下：

```text
MapLaunchContext.SelectedPlayerId
  -> PlayerEntityLookup
  -> CoreServiceKeys.LocalPlayerId
  -> CoreServiceKeys.LocalPlayerEntity
  -> input / order systems
```

`SelectedPlayerId` 属于 map load launch context，不属于 map identity。启动入口通过 `GameConfig.StartupSelectedPlayerId` 声明默认本地玩家，并由 `GameEngine.LoadStartupMap()` 注入 launch context。

Mod 自定义 launch payload 只能走 `MapLaunchContext.Metadata`，Core 不为单个 Mod 需求新增顶层强类型字段。

正式路径禁止：

- 扫描 `PlayerOwner.PlayerId == 1`
- `_playerId = 1` 之类的隐式默认
- map load 之后再靠 post-scan 猜测 local player
- 在 `MapConfig.Players` 里声明静态 local player 标记

兼容边界：

- 旧 showcase/runtime 若仍显式手动写 `LocalPlayerEntity`，Core 会在 focused map session 上回收并恢复这条显式绑定
- 但只要走正式 participant path，就必须同时依赖显式 `LocalPlayerId`

相关源码：

- `src/Core/Input/Systems/LocalPlayerEntityResolverSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs`
- `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs`

## 6. Validation Rules

Core 必须在 map load 期间显式失败，禁止 silent fallback：

- duplicate `TeamId`
- duplicate `PlayerId`
- duplicate representative `InstanceId` where uniqueness is required
- unresolved representative `InstanceId`
- blank / whitespace / non-trimmed authored `InstanceId`
- player 引用未绑定 `TeamId`
- launch context `SelectedPlayerId` 引用未绑定 player
- participant relationship 缺少有效 `TypeId`

相关源码：

- `src/Core/Gameplay/Teams/ParticipantBindingResolver.cs`
- `src/Core/Systems/MapLoadEntityIndex.cs`

## 7. Session Boundary

participant-focused runtime state属于 map session：

- `MapSession.TeamEntityLookup`
- `MapSession.PlayerEntityLookup`
- `MapSession.LocalPlayerId`
- `MapSession.LocalPlayerEntity`
- `MapSession.TeamRelationships`
- `MapSession.LaunchContext`

focused map 切换、push/pop、resume 时，Core 必须发布当前 session 的 participant state；map unload 时必须清理 focused lookup/local-player service，避免把上一张图的 participant cache 当成当前图真相。

相关源码：

- `src/Core/Map/MapSession.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Core/Engine/GameEngine.MapLoadLifecycle.cs`

## 8. Tests

本契约当前由以下测试覆盖：

- `src/Tests/GasTests/ParticipantBindingContractTests.cs`
- `src/Tests/GasTests/MapLoadEntityIndexContractTests.cs`
- `src/Tests/GasTests/MapManagerInheritanceTests.cs`
- `src/Tests/GasTests/Features/InputRouting/InputOrderContractTests.cs`
- `src/Tests/GasTests/AuthoritativeInputConvergenceTests.cs`

这些测试验证：

- logical participant entity 可通过正常 map path 加载
- representative identity/component 写入正确
- lookup 和 local player publish 正确
- invalid authoring 明确失败
- map inheritance 不会丢 participant authoring
- input/order 正式链路不依赖默认 player 1
