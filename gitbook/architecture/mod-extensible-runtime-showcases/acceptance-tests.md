# 扩展面验收测试

本页说明 Mod 扩展运行时各面的测试用例：config shards、effect preset type 代码扩展、presenter command / behavior 扩展、graph op 注册面。每条说清锁的是哪份合同、怎么跑、main 上还缺什么。行为合同正本见 [Mod Extensible Runtime](../mod-extensible-runtime.md)，可玩案例见同目录各 showcase 页。

## 测试清单

注册合同层：`src/Tests/PresentationTests/ModExtensionRegistrationTests.cs`（PresentationTests 工程）。

| 用例 | 锁的合同 |
|---|---|
| `ModExtensions_RegisterProviderKeysAndAllowConsumerGraphReferences` | 四类注册 API（BuiltinHandler / GraphOp / PresenterCommand / PresenterBehavior）都返回 ≥1024 的动态 id；冻结后 key 可反查；graph op handler 能装进 `GasGraphOpHandlerTable` |
| `ModExtensions_RejectNamespaceImpersonationAndLateRegistration` | consumer 注册 `ProviderMod.*` key 抛命名空间错误；`Freeze()` 之后再注册抛 frozen |
| `ModExtensions_RejectDuplicateAndFrozenBuiltinHandlerRegistration` | 同 key 重复注册抛 already registered；冻结后注册抛 frozen |
| `ModExtensions_RejectInvalidGraphOpShapeAtRegistration` | TargetList 输出、Void 输入、fixed register 越界在注册期即拒 |
| `ModExtensions_GraphOpInputTypesAreFrozenByValue` | 注册后输入类型数组按值冻结，改动注册时传入的原数组不影响定义 |

showcase 端到端：`src/Tests/GasTests/Production/CapabilityStandardExtensibleRuntimeShowcaseAcceptanceTests.cs`（GasTests 工程，headless，四个 showcase 各一条）。

| 用例 | 锁的合同 |
|---|---|
| `ConfigShards_PlayerCastsAbilityLoadedFromIndependentShardFiles` | 独立 ability / effect shard 经 ConfigPipeline 合并装载，玩家施放触发完整链路 |
| `EffectPresetTypeCode_PlayerAppliesEffectAndModHandlerRunsThroughGas` | Mod 声明的 preset type 落动态 id（`PresetType == None && PresetTypeId > 255`），C# handler 经正式 GAS phase 被调用 |
| `PresenterBehaviorExtension_PlayerSeesCloudDriftTickFromModBehavior` | 扩展行为在 ContinuousTick lane 持续运行 |
| `PresenterCommandExtension_PlayerSignalRoutesToModCommandOnExistingPresenter` | 扩展指令按 ExistingInstances 路由到 Mod handler |

治理守卫：`src/Tests/GasTests/Effect/EffectCompositionSsotTests.cs` 的 `UncertifiedRevealArea_IsNotPublishedAsPresetOrFormalGraph` 锁「未认证能力不得进 `EffectPresetType` 枚举、`preset_types.json` 或正式 graph」的组合门禁。

## 覆盖矩阵

| 合同 | config shards | preset type | presenter 扩展 | graph op |
|---|---|---|---|---|
| 注册成功 + 动态 id | 无代码注册面 | 显式 | 显式 | 显式 |
| Freeze 后拒绝 | 走 hub 统一路径 | 显式 | 走 hub 统一路径 | 显式 |
| 命名空间抢注拒绝 | 走 hub 统一路径 | 走 hub 统一路径 | 走 hub 统一路径 | 显式 |
| 重复 key 拒绝 | 走 hub 统一路径 | 显式 | 走 hub 统一路径 | 走 hub 统一路径 |
| 注册期形状校验 | 不适用 | 不适用 | 不适用 | 显式 |
| JSON 编译 + 运行时执行 | 显式 | 显式 | 显式（command、behavior 各一） | 待 PR #1495 |
| 装载期失败关闭单测 | 缺 | 缺 | 缺 | 待 PR #1495 |

「走 hub 统一路径」指合同由 `ExtensionKeyRegistry` / `ModExtensionKeyOwnership` 对四类 API 统一执行，显式用例以 Gas 侧 API 为代表，没有为该面单列用例。

## 缺口

1. graph op 的 authoring 门（JSON 引用 mod op 键、Query 图拒绝扩展算子、缺输入失败关闭）共 8 个用例 `GraphExtensionOpAuthoringTests` 与 showcase 验收增强都在 [PR #1495](https://github.com/MightyBubble/Ludots/pull/1495) 待合入；合入前 main 上 mod op 注册成功但进不了正式图程序。
2. 装载期失败关闭（presenter 扩展的 route / lane 不匹配、preset type 引用未注册 handler）目前只有 loader 的抛异常实现和 showcase 正常路径覆盖，没有独立单测锁错误行为。补测试时挂在 PresentationTests / GasTests 现有工程下，不新建工程。

## 跑法

```powershell
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter "FullyQualifiedName~ModExtensionRegistrationTests"
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~CapabilityStandardExtensibleRuntime"
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~EffectCompositionSsotTests"
```

可玩验收用四个 preset（合同与启动说明见 [README](README.md)）；对应 `showcase.registry.json` binding：`capability_standard_config_shards_showcase`、`capability_standard_effect_preset_type_code_showcase`、`capability_standard_presenter_behavior_extension_showcase`、`capability_standard_presenter_command_extension_showcase`。

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_config_shards_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_effect_preset_type_code_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_presenter_behavior_extension_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_presenter_command_extension_showcase_raylib'
```

## UAT

```gherkin
Feature: 扩展面测试说明与真实测试一一对应

  Scenario: 我按页跑命令能复现
    Given 我在本页拿到一条 dotnet test 命令
    When 在仓库根执行
    Then 对应测试全部通过
    And 页面表格里的用例名与测试文件里的方法名一致

  Scenario: 我知道 graph op 缺口在哪
    Given 我读到 graph op 的 JSON 编译格写着「待 PR #1495」
    When 我打开 PR #1495
    Then 能看到 GraphExtensionOpAuthoringTests 与配套 showcase 验收
    And 我不会在 main 上重写一遍同样的门
```
