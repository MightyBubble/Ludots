# 指令 PresenterCommandKind 逐条

指令是 presenter 域的"动词层"：presenter 规则把事件编译成指令，运行时逐条执行。每条回答五件事：**做什么 / authoring 怎么写 / 在哪执行 / 现有演示与验收 / 缺口状态**。总目录见 [README.md](README.md)；参数从指令流到资产属性的机制见 [param-sink.md](param-sink.md)。

语义以代码为准：枚举 `src/Core/Presentation/Presenters/PresenterCommandKind.cs`；执行 switch `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:89-207`；authoring 字段白名单 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:778-785`。

## 执行管线与 authoring 形态

帧内顺序（注册见 `src/Core/Engine/GameEngine.cs:2131-2137`）：PresenterTimerSystem 先推进命名 timer 并发布 TimerExpired（当帧可被规则消费）→ PresenterRuleSystem 读事件产指令 → PresenterRuntimeSystem 逐条执行指令、管理实例生命周期。

authoring 写在 presenter 定义（或 bootstrap 定义）的 rules[].command.*，白名单字段（`src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:778-785`）：

```jsonc
{
  "kind": "…",            // 11 种内建指令名，或 Mod 注册的扩展 command key
  "route": "…",           // 仅 Extension 必填；内建指令由 kind 自动推导
  "definitionId": "…",    // CreatePresenter 必填；SetParam/DestroyScopedPresenter 用于 scope 定位
  "scopeTag": "…",        // scope 标签（字符串，编译为 scopeTag id）
  "scopeSource": "…",     // scope 来源（Fixed/EventPayloadA…）
  "ownerSource": "…",     // owner entity 来源
  "useEventPosition": true,
  "paramKey": "…", "paramLane": "Float|Int|Vector", "valueSource": "Fixed|EventKeyId|EventMagnitude|…",
  "paramValue": 0, "intValue": 0, "vectorValue": [0,0,0,0], "paramGraphProgramId": 0,
  "vectorXSource": "…", "vectorYSource": "…", "vectorZSource": "…", "vectorWSource": "…",
  "targetBehaviorSlot": "…",   // Activate/DeactivateBehavior 用
  "timerName": "…", "durationSeconds": 0, "durationRangeSeconds": 0   // Timer 用
}
```

路由策略（内建指令按 kind 固定，见 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:1243-1263`；枚举 `src/Core/Presentation/Presenters/PerformerExtensionRegistries.cs:8-16`）：`CreatePerformer` / `DestroyScope` / `ScopedInstance`（按 definition+scope 定位实例）/ `ExistingInstances`（路由到事件命中的现存实例）/ `SingleRuntime`。

## 指令逐条

### CreatePresenter

- **做什么**：按 definitionId 创建 presenter 实例（含 children 递归展开、参数默认值、初始变换）；持久 scoped 重复创建幂等（已存在则刷新位置与参数载荷，不重复建树）。
- **authoring**：`definitionId`（必填）+ `scopeTag`/`scopeSource`/`ownerSource`/`useEventPosition` + 可选参数载荷（paramKey/paramLane/valueSource…）。
- **在哪执行**：PresenterRuntimeSystem → `HandleCreatePresenter` → `CreateHierarchy`（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:447`）。
- **现有演示与验收**：仓库 mods 内 305 处使用、覆盖 36 个 mod；全链演示 = preset `presenter_blacksmith_showcase_raylib`（建筑出生自动展开子树）。
- **缺口状态**：无缺口。

```jsonc
{
  "event": { "kind": "EntitySpawned", "key": "blacksmith_building" },
  "condition": { "inline": "SourceHasVisualTransform" },
  "command": {
    "kind": "CreatePresenter",
    "definitionId": "blacksmith_root",
    "scopeSource": "EventPayloadA"
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:5-18`。

### DestroyPresenter

- **做什么**：递归销毁单个 presenter 实例（子树先销毁），立即回收。
- **authoring**：`kind: "DestroyPresenter"`，路由到事件命中的现存实例（ExistingInstances）。
- **在哪执行**：PresenterRuntimeSystem switch（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:95-97`）直接调 `Destroy`。
- **现有演示与验收**：**0 个 mod 使用**；仅有单测锁定语义（`src/Tests/PresentationTests/Presenter/PresenterTreeLifecycleTests.cs:488`）。生产代码的按实体/按 scope 销毁走 DestroyPresenterScope 与 DestroyScopedPresenter。
- **缺口状态**：注意——架构文档 [Presenter-as-Actor](../../architecture/presenter-as-actor-architecture.md) §9.2（:600-617）描述的 Deferred/死亡动画两段式销毁是**未实现的设计稿**；当前实现为立即销毁，无 DestroyMode/PendingDestroy。

### DestroyPresenterScope

- **做什么**：按 scopeTag 整组销毁（一个 owner 事件拆除它创建的整棵 presenter 树），要求正 scopeTag，否则装载/执行期 fail-loud。
- **authoring**：`scopeTag` + `scopeSource`。
- **在哪执行**：PresenterRuntimeSystem → `DestroyScope`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:99-106`）。
- **现有演示与验收**：191 处 / 30 个 mod；与 CreatePresenter 成对出现在铁匠铺 bootstrap（preset `presenter_blacksmith_showcase_raylib`）。
- **缺口状态**：无缺口。

```jsonc
{
  "event": { "kind": "EntityDestroyed", "key": "blacksmith_building" },
  "command": { "kind": "DestroyPresenterScope", "scopeSource": "EventPayloadA" }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:19-28`。

### DestroyScopedPresenter

- **做什么**：按 definitionId + scopeTag 精确销毁**单个** scoped 实例（不动同 scope 的其他 definition），`useEventPosition: true` 时按事件位置匹配实例。
- **authoring**：`definitionId` + `scopeTag`/`scopeSource` + 可选 `useEventPosition`。
- **在哪执行**：PresenterRuntimeSystem → `HandleDestroyScopedPresenter`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:429-464`）。
- **现有演示与验收**：58 处 / 13 个 mod（典型：RTS 移动路径预览线随 MovePathEnded 拆除、技能瞄准预览随 AbilityAimEnded 拆除）；此前文档缺条目，本页补齐。
- **缺口状态**：无缺口。

```jsonc
{
  "event": { "kind": "MovePathEnded", "key": "core_input.move_path.line" },
  "command": {
    "kind": "DestroyScopedPresenter",
    "definitionId": "core_input.move_path.line",
    "scopeSource": "EventPayloadA"
  }
}
```

来源：`mods/CoreInputMod/assets/Presentation/presenters.json:1121-1131`。

### SetParam

- **做什么**：写 presenter 黑板参数（Float/Int/Vector 三车道），值可取固定值或事件载荷（valueSource），写中 visual sink 键时自动标脏触发资产重发（见 [param-sink.md](param-sink.md)）；可带 definitionId+scopeTag 定位 scoped 实例，`useEventPosition` 时顺带刷新实例世界位置。
- **authoring**：`paramKey`/`paramLane`/`valueSource`（+paramValue/intValue/vectorValue 或 paramGraphProgramId 图程序求值；Vector 车道支持 vectorXSource…vectorWSource 逐分量取源）。
- **在哪执行**：PresenterRuntimeSystem → `SetParamAndPropagateToAffectedChildren`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:112-137`，含 scoped 实例解析）。
- **现有演示与验收**：223 处 / 11 个 mod；铁匠铺日夜相位与区域参数都由它写入（preset `presenter_blacksmith_showcase_raylib`）。
- **缺口状态**：无缺口。

```jsonc
{
  "event": { "kind": "GlobalDayNight", "key": "*" },
  "command": {
    "kind": "SetParam",
    "paramKey": "blacksmith.dayNight",
    "paramLane": "Float",
    "valueSource": "EventMagnitude"
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:1633-1644`。

### ActivateBehavior / DeactivateBehavior

- **做什么**：运行时激活/停用目标行为槽（0-31），停用即停止该槽的可视输出（如烟囱熄烟、工人停动画），激活后整层重引导。
- **authoring**：`targetBehaviorSlot`（必填，字符串槽名）。
- **在哪执行**：PresenterRuntimeSystem → `SetBehaviorActive` + `MarkHierarchyForBootstrap`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:139-165`）。
- **现有演示与验收**：各 15 处 / 3 个 mod；铁匠铺 working tag 点烟/熄烟是标准演示（preset `presenter_blacksmith_showcase_raylib`）。
- **缺口状态**：无缺口。

```jsonc
{
  "event": { "kind": "TagEffectiveChanged", "key": "working" },
  "condition": { "inline": "TagGained" },
  "command": { "kind": "ActivateBehavior", "targetBehaviorSlot": "body" }
},
{
  "event": { "kind": "TagEffectiveChanged", "key": "working" },
  "condition": { "inline": "TagLost" },
  "command": { "kind": "DeactivateBehavior", "targetBehaviorSlot": "body" }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:825-852`（blacksmith_chimney_smoke_vfx，body 槽默认 activeByDefault: false）。

### SinkParamToAsset

- **做什么**：对路由到的 presenter 实例**强制重 emit**——param 值未变也刷新资产输出；payload 允许 paramKey/paramLane 指定刷新的 lane。用途：外部系统改了资产侧状态（如材质库热替换）后，让画面立即反映，而不必制造一次假参数变更。
- **authoring**：`kind: "SinkParamToAsset"` + 可选 selector：`paramKey` 与 `paramLane` **必须成对**（都不写 = 刷新全部 lane；只写其一装载即抛；值字段一律被拒绝）；可选 `definitionId` 配合 scope 定位；路由 SingleRuntime。
- **在哪执行**：路由见 `src/Core/Presentation/Systems/PresenterRuleSystem.cs:644` 与装载白名单；**运行时执行分支随配套 PR 落地**（当前 `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:89-207` 的 switch 尚无此 case）。
- **现有演示与验收**：尚无（指令已入枚举与装载面，0 mod 使用；可玩 showcase 随配套 PR 提供）。
- **缺口状态**：实现随配套 PR 落地；落地前不要在生产 mod 依赖此指令。

### InitializeTransform

- **做什么**：把 presenter 世界变换强制重同步为 owner entity 当前 VisualTransform（+定义的 anchor offset）；用于 owner 变更后的强制对齐——常规跟随由 PresenterEntityTransformSyncSystem 每帧自动做，此指令是事件驱动的显式重同步入口。
- **authoring**：`kind: "InitializeTransform"`，路由 ExistingInstances；无载荷。
- **在哪执行**：PresenterRuntimeSystem → `HandleInitializeTransform`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:412-427`）→ `InitializeTransform`（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:470-508`，CreateHierarchy 建树时内部自动调用一次）。
- **现有演示与验收**：0 处 authoring、0 条文档；语义由 CreateHierarchy 内部调用路径与单测锁定。
- **缺口状态**：纯手动重同步入口，当前无生产作者面用例。

### TimerSet

- **做什么**：在 presenter 实例上启动命名 timer；`durationRangeSeconds` > 0 时在 [duration - range, duration] 内取抖动值。到时由 PresenterTimerSystem 发布 TimerExpired 事件（规则当帧可消费），支持 keyId 通配 `*`。
- **authoring**：`timerName` + `durationSeconds`（可选 `durationRangeSeconds`），路由 ExistingInstances。
- **在哪执行**：PresenterRuntimeSystem → PresenterTimerTable.Set（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:170-182`）；到时发布见 `src/Core/Engine/GameEngine.cs:2131-2133`。
- **现有演示与验收**：fixture `mods/fixtures/presenter_timer/PresenterTimerTestMod/assets/Presentation/presenters.json` + 验收 `artifacts/acceptance/presenter-timer/battle-report.md`（受击闪黄时序：TimerSet 0.3s → TimerExpired 复原；TimerKill "*" 打断）；可玩 showcase 随配套 PR。
- **缺口状态**：契约与验收就绪，尚无可玩 showcase preset。

```jsonc
{
  "event": { "kind": "GameplayEvent", "keyId": "PT.HitFlash" },
  "command": { "kind": "TimerSet", "timerName": "pt.flash", "durationSeconds": 0.3 }
}
```

来源：`mods/fixtures/presenter_timer/PresenterTimerTestMod/assets/Presentation/presenters.json:5-8`。

### TimerKill

- **做什么**：按名杀掉实例上的 timer（`timerName: "*"` 杀全部，映射 PresenterTimerNameRegistry.AllTimersId），被杀 timer 不再发布 TimerExpired。
- **authoring**：`timerName`（`*` 或具体名），路由 ExistingInstances。
- **在哪执行**：PresenterRuntimeSystem → PresenterTimerTable.Kill/KillAll（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:184-198`）。
- **现有演示与验收**：与 TimerSet 同 fixture 同验收（Suppressed tag 丢失 → TimerKill "*" 打断闪黄，battle-report 的 taglost_interrupt_no_expiry 断言）。
- **缺口状态**：同 TimerSet——契约就绪，可玩 showcase 随配套 PR。

```jsonc
{
  "event": { "kind": "TagEffectiveChanged", "keyId": "PT.Suppressed" },
  "condition": { "inline": "TagLost" },
  "command": { "kind": "TimerKill", "timerName": "*" }
}
```

来源：`mods/fixtures/presenter_timer/PresenterTimerTestMod/assets/Presentation/presenters.json:17-21`。

### Extension（扩展指令）

- **做什么**：Mod 注册一次性表现指令——作者面在 rules[].command.kind 写 **Mod 限定的 command key**（非 11 种内建名），loader 编译为 `PresenterCommandKind.Extension` + 动态 CommandKindId，执行时分发给 Mod 注册的 handler。
- **authoring**：`kind: "<ModCommandKey>"` + `route`（必填，必须与注册 descriptor 一致，否则装载 fail-loud）+ 常规载荷字段（paramKey/paramLane/valueSource/scopeTag…）。
- **在哪执行**：PresenterRuntimeSystem → `HandleExtensionCommand`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:245-274`：descriptor 查找、route 校验、routed performer 校验，然后 `PerformerCommandExecutionContext` 交给 handler）。
- **现有演示与验收（黄金模板）**：preset `capability_standard_performer_command_extension_showcase_raylib`；mod `mods/showcases/capability_standard/CapabilityStandardPerformerCommandExtensionShowcaseMod/`；文档 [Performer Command Extension](../../architecture/mod-extensible-runtime-showcases/performer-command-extension.md)。
- **缺口状态**：无缺口（模板、装载校验、运行时分发、可玩 showcase 全链齐备）。

```jsonc
{
  "event": { "kind": "GameplayEvent", "keyId": "CapabilityStandard.PerformerCommandExtension.Signal" },
  "command": {
    "kind": "CapabilityStandardPerformerCommandExtensionShowcaseMod.EmitSignalPing",
    "route": "ExistingInstances",
    "scopeTag": "capability_standard.performer_command_extension.signal",
    "paramKey": "capability_standard.performer_command_extension.signal_count",
    "paramLane": "Int",
    "valueSource": "EventPayloadA"
  }
}
```

来源：`mods/showcases/capability_standard/CapabilityStandardPerformerCommandExtensionShowcaseMod/assets/Presentation/presenters/capability_standard.performer_command_extension.signal_rules.json`；handler 注册与校验见同 mod 的 CapabilityStandardPerformerCommandExtensionShowcaseModEntry.cs。
