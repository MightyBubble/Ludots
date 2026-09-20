# Mod 作者上手：四个扩展面的完整走法

面向第一次给 Ludots 写 Mod 的作者。四个旅程各走一遍：只拆配置、加一种新效果、加一个图算子、加表现指令与行为。每步给出目录结构、JSON 字段、C# 注册签名、预期行为和做错时的报错原文。合同正本见 [Mod Extensible Runtime](../mod-extensible-runtime.md)，逐用例的测试说明见 [扩展面验收测试](acceptance-tests.md)。

启动 showcase 用 binding 选择器加显式 adapter。`cli launch` 的 adapter 只认 `--adapter` 参数，缺省用机器默认平台（例如 web）；`launcher.presets.json` 里 preset 的 `adapterId` 不驱动 CLI 启动，跑 raylib 要显式写 `--adapter raylib`：

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_config_shards_showcase' --adapter raylib
```

## 全局规范

**注册窗口只有一个。** 引擎装配序：创建 `ModExtensionHub` → `ModLoader.LoadMods`（你的 `IMod.OnLoad` 在这里执行）→ `ModExtensions.Freeze()` → 编译 config → 把含 mod handler 的 `GasGraphOpHandlerTable` 注册为引擎服务。Freeze 之后再注册任何 key 都会抛异常；config 编译发生在冻结之后，所以配置能安全引用已注册的 key。

**key 必须以自己的 ModId 开头**（`"<ModId>."`，逐字符校验）。内建枚举名被 Core 保留，冒用被拒。别的 Mod 可以在配置里引用你的 key，但不能注册你命名空间下的 key。

**动态 id 段**（注册返回值即 id，四张注册表同段位）：

| 注册面 | 首个 mod id | 上限 |
|---|---|---|
| BuiltinHandler | 1024 | — |
| GraphOp opcode | 1024 | 2048 |
| PresetType | 1024 | 2048 |
| PresenterCommand / Behavior KindId | 1024 | 2048 |

**红线**：不为玩法变体加 Core 枚举；不绕过 `config_catalog.json` 私扫目录；不建平行管线或第二 VM；不写 fallback 让缺失配置静默通过。

## 旅程一：只拆配置（不写代码）

参考模板 `CapabilityStandardConfigShardsShowcaseMod`。

```text
MyMod/
  mod.json
  assets/
    game.json                  # startupMapId / windowTitle
    Maps/my_showcase_map.json
    GAS/abilities/my.ember_bolt.json
    GAS/effects/my.ember_bolt_damage.json
```

`mod.json` 必备 `name`（即命名空间前缀）、`version`、`main`（入口程序集路径）、`priority`、`dependencies`。

**不需要自己写 `config_catalog.json`。** Core 的 `assets/config_catalog.json` 已为五张正式表声明 shard 目录：`GAS/abilities`、`GAS/effects`、`GAS/graphs`、`GAS/preset_types`、`Presentation/presenters`。把 JSON 放进对应目录即被发现；合并策略 `ArrayById`——主文件先、shard 按稳定 VFS 序、按 `id` 字段合并进同一逻辑配置。只有引入 Core 目录里没有的新表，才需要 mod 侧声明 catalog 条目。

预期行为：启动后你的 ability / effect 与 Core 配置进同一个正式注册表，链路一致，玩家照常施放。shard 缺 `id` 或字段非法在装载期点名失败，没有静默跳过。详见 [Config Shards](config-shards.md)。

## 旅程二：加一种新效果（preset type）

参考模板 `CapabilityStandardEffectPresetTypeCodeShowcaseMod`（Heat Mark）。三步：

**① 注册 C# phase handler**（`OnLoad`）：

```csharp
context.Extensions.Gas.RegisterBuiltinHandler(
    "MyMod.ApplyHeatMark",
    ApplyHeatMark,
    new EffectOperationMetadata(EffectOperationKind.Pure, EffectAtomicDomain.None, "ApplyHeatMark"));
```

**② 声明 preset type**（`GAS/preset_types/my.heat_mark.json`，字段全必填）：

```json
[{
  "id": "MyMod.HeatMark",
  "components": ["DurationParams"],
  "activePhases": ["OnApply", "OnPeriod"],
  "allowedLifetimes": ["After"],
  "defaultPhaseHandlers": {
    "OnApply":  { "type": "builtin", "id": "MyMod.ApplyHeatMark" },
    "OnPeriod": { "type": "builtin", "id": "MyMod.ApplyHeatMark" }
  }
}]
```

每相位的 handler 二选一：`type: "builtin"` 指向已注册的 handler key，或 `type: "graph"` 指向已登记的图 id——C# 代码与图在这里统一挂接。

**③ 引用它的效果**（`GAS/effects/my.heat_mark.json`）：

```json
[{
  "id": "Effect.MyMod.HeatMark",
  "presetType": "MyMod.HeatMark",
  "lifetime": "After",
  "participatesInResponse": false,
  "duration": { "durationTicks": 90, "periodTicks": 15, "clockId": "FixedFrame" },
  "categories": ["Effect.MyMod"]
}]
```

`presetType` 写 ② 里声明的 key，不是枚举名。装载序 graphs → preset_types → effects；解析先查 `PresetTypeRegistry`（命中动态 id），再白名单，再内置枚举。运行时 `EffectPhaseExecutor` 按 int id 查 `defaultPhaseHandlers` 分发——枚举值落 `None`、`PresetTypeId > 255`，这正是验收断言。玩家侧：点按钮 → `EffectRequestQueue` → 下一正式 tick 你的 handler 被 GAS phase 调用。

**边界**：modifier 聚合语义绑死内置 `Buff` 枚举——自定义 preset 不会自动获得类 Buff 叠层聚合，要做聚合请复用内置 Buff。改行为差异改的是 graph 连线 / effect 步骤 / handler，不是给 `EffectPresetType` 加枚举值。详见 [Effect Preset Type Code](effect-preset-type-code.md)。

## 旅程三：加一个图算子

```csharp
context.Extensions.Gas.RegisterGraphOp(
    "MyMod.QueryThreat",
    GraphValueType.Float,     // 输出限 Void/Bool/Int/Float/Entity
    QueryThreat);             // 可选 params GraphValueType[] inputTypes：最多 3 个，限 Bool/Int/Float/Entity
```

形状约束在注册期即拒：`TargetList` 是 VM 隐式 scratch，不能作签名类型；`Void` 不能作输入；fixed register 有上限。注册返回 opcode ≥1024，handler 真实装进引擎服务表。

**authoring**：`GAS/graphs.json` 节点的 `op` 直接写 provider key（如 `"MyMod.QueryThreat"`），走与内置算子同一扇控制流编译门；未知键、Query 图挂扩展算子、缺输入都在编译期失败关闭。黄金模板：provider `CapabilityStandardGraphOpProviderMod`（注册 `QueryThreat`）+ consumer `CapabilityStandardGraphOpExtensionShowcaseMod`（图里引用它，玩家点按钮看威胁评分）；编译门用例 `GraphExtensionOpAuthoringTests` 8 例；详见 [Graph Op Extension](graph-op-extension.md)。

## 旅程四：加表现指令与行为

**扩展指令（一次性命令）**：

```csharp
context.Extensions.Presentation.RegisterPresenterCommand(
    "MyMod.EmitSignalPing",
    new PresenterCommandExtensionDescriptor(
        PresenterCommandRouteStrategy.ExistingInstances,
        EmitSignalPing));
```

路由五选一：`ExistingInstances` / `ScopedInstance` / `SingleRuntime` / `CreatePresenter` / `DestroyScope`。配置（`Presentation/presenters/my.signal_rules.json`）：

```json
[{
  "id": "my.signal_rules",
  "rules": [{
    "event": { "kind": "GameplayEvent", "keyId": "MyMod.Signal" },
    "command": {
      "kind": "MyMod.EmitSignalPing",
      "route": "ExistingInstances",
      "scopeTag": "my.signal",
      "paramKey": "my.signal_count", "paramLane": "Int", "valueSource": "EventPayloadA"
    }
  }]
}]
```

`route` 必填且必须与注册 descriptor 一致。详见 [Presenter Command Extension](presenter-command-extension.md) 与 [commands.md 的 Extension 条目](../../reference/presenter-capability-catalog/commands.md)。

**扩展行为（持续驱动）**：

```csharp
context.Extensions.Presentation.RegisterPresenterBehavior(
    "MyMod.CloudDrift",
    new PresenterBehaviorExtensionDescriptor(
        PresenterBehaviorExecutionLane.ContinuousTick,
        RunCloudDrift));
PresenterParamKeyRegistry.Register("my.cloud.drift");
```

配置：`behaviors[].kind` 写 key + `execution.lane`。**lane 合同**：仅放行四条——`Bootstrap` / `ContinuousTick` / `OwnerAttributeDirty` / `OwnerTagDirty`；后两条必须再带 `execution.trigger`（`attributeId` 或 `tagId`，解析不到正 id 即失败）；`ParamDirty` / `Activation` / `Destroy` 对扩展不可用；挂 child instance 的扩展行为只认前两条。槽位没有内建行为的专属载荷对象，handler 读写自定义数据只用 `IPresenterBehaviorOps` 的参数读写；handler 内不能直接建删 presenter 实例。详见 [Presenter Behavior Extension](presenter-behavior-extension.md) 与 [behaviors.md 的 Extension 条目](../../reference/presenter-capability-catalog/behaviors.md)。

预期行为：事件 → 规则编译为 `Extension` kind + 动态 id → `PresenterRuntimeSystem.HandleExtensionCommand` / `PresenterBehaviorSystem.ProcessExtensionBehaviors` 分发。玩家看到行为持续运行、按钮计数变化。

## 失败关闭报错原文

| 做错的事 | 报错 |
|---|---|
| key 没带自己 ModId 前缀 | `Extension key '...' must be prefixed with the loading mod id 'MyMod.'.` |
| 冒用内建名 | `... is reserved by Core. Use a mod-qualified key.` |
| Freeze 后注册 / 重复注册 | `Extension registry is frozen. Cannot register '...'.` / `... already registered` |
| config 的 route 与注册不一致 | `...route '...' does not match registered route '...' for '...'` |
| config 的 lane 与注册不一致 | `...execution.lane '...' does not match registered lane '...'` |
| 引用未注册的 preset type | `presetType '...' is not registered in preset_types.json, a Core EffectPresetType, or a loaded mod extension.` |
| graphs.json 写未注册的算子键 | `UnknownNodeOp`：`Unknown or non-...-authorable op 'MyMod.QueryThreat'` |

## 自验

四个可玩 showcase 的 binding：`capability_standard_config_shards_showcase`、`capability_standard_effect_preset_type_code_showcase`、`capability_standard_presenter_behavior_extension_showcase`、`capability_standard_presenter_command_extension_showcase`。测试命令与逐用例说明见 [扩展面验收测试](acceptance-tests.md)。
