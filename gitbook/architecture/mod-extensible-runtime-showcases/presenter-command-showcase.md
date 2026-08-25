# Showcase: Presenter Command 全息

## 概述

这个 showcase 在一张地图上布置四个站点，用数据规则驱动全部内建 presenter 指令。玩家进入地图后看到 `Presenter Command 全息` 面板；每个站点对应一组按钮，按钮只发布 presentation event，所有行为都由 presenter 规则编译成正式指令、由 `PresenterRuntimeSystem` 执行。

它证明内建指令集（SetParam / TimerSet / TimerExpired / TimerKill / SinkParamToAsset / ActivateBehavior / DeactivateBehavior / CreatePresenter / DestroyPresenter / DestroyScopedPresenter / DestroyPresenterScope / InitializeTransform）可以完全从数据侧组合出可玩的玩法闭环。

## 结构

```text
CapabilityStandardPresenterCommandShowcaseMod/
  CapabilityStandardPresenterCommandShowcaseModEntry.cs
  PresenterCommandShowcaseUI.cs
  assets/
    game.json
    Maps/
      capability_standard_presenter_command_showcase.json
    Presentation/
      presenters/
        capability_standard.presenter_command.ground.json
        capability_standard.presenter_command.flash_plaza.json
        capability_standard.presenter_command.lamp_params.json
        capability_standard.presenter_command.boiler_switch.json
        capability_standard.presenter_command.portal_field.json
```

## 四个站点

### A 闪烁广场（TimerSet / TimerKill / SetParam / TimerExpired）

一排 5 个单位（`pcmd.flash_unit`）。`受击` 发布 `pcmd.hit`：规则一 SetParam 写 `pcmd.unit.color` 变黄，规则二 TimerSet `pcmd.flash` 0.6 秒；到期后 TimerExpired 规则把颜色写回蓝色。`压制` 发布 `pcmd.suppressed`（TagGained）：TimerKill `"*"` 清掉实例全部 timer，并把颜色直接写到绿色——到期复原永远不会发生，单位停在压制色。

### B 灯柱参数 sink（SetParam / SinkParamToAsset）

3 根灯柱（`pcmd.lamp_post`，Static mobility、event-driven static emit）声明 `colorParamKey`（Vector）与 `scaleParamKey`（Float）。`循环灯色` / `循环缩放` 按钮走三档 key 轮转 SetParam，当帧把 param sink 进 asset 属性并重 emit。第 4 根对照柱不写值：`强制刷新对照柱` 发布 `pcmd.lamp.refresh`，规则编译成 `SinkParamToAsset`（definitionId + 固定 scopeTag，SingleRuntime 路由），只把该 scoped 实例标记为需要重 emit。

### C 烟囱开关（ActivateBehavior / DeactivateBehavior）

锅炉（`pcmd.boiler`）挂子节点烟囱（`pcmd.chimney_smoke`，`activeByDefault: false`）。`烟囱开关` 发布 `pcmd.working` 的 TagEffectiveChanged：TagGained → ActivateBehavior `body`，TagLost → DeactivateBehavior `body`。

### D 传送与清场（Create / Destroy / DestroyScoped / DestroyScope / InitializeTransform）

- `召唤靶标`：为靶标创建独立 owner，发布 `pcmd.summon`（带位置）→ CreatePresenter（共享 scopeTag `pcmd.target.field`，useEventPosition）。
- `精确拆除`：发布 `pcmd.remove.scoped`（source = 最老靶标的 owner）→ DestroyScopedPresenter 按 definition+owner+scope 唯一解析。
- `整域清场`：发布 `pcmd.clear.field` → DestroyPresenterScope 一次销毁同 scope 全部靶标。
- `路由销毁`：发布 `pcmd.vanish`（source = 最新靶标的 owner）→ DestroyPresenter 走 ExistingInstances 单体路由。
- `传送门`：改 portal owner 的 VisualTransform 后发布 `pcmd.portal.resync` → InitializeTransform 重同步（portal 为 Static mobility，不跟随每帧 transform tick，只有显式指令才动）。

## 详情

- 规则全部是数据（presenters 分片 JSON），按钮只写 `PresentationEventStream`，Mod 代码不直接操作 presenter 实例。
- 站点 D 的 create/destroy 规则放在无实例的 `pcmd.field_director` 上，避免 ExistingInstances/Scoped 路由对 owner definition 实例的隐式扇出。
- 靶标用「每靶标独立 owner + 共享 scope」的形态：`_scopedInstances` 以 (def, owner, scope) 为键不会去重，`_byScope` 又能按 scope 整域清场。

## 边界

- builtin 指令不得携带 extension route。
- SinkParamToAsset 只接受 paramKey/paramLane 刷新 selector，不接受值 payload。
- 按钮不得绕过 `PresenterCommandBuffer` / `PresenterRuntimeSystem` 直接改实例状态。

## UAT

```gherkin
Feature: 玩家用按钮驱动四个站点的 presenter 指令

  Scenario: A 站受击后闪烁并自动复原
    Given 我启动 `capability_standard_presenter_command_showcase_raylib`
    And 地图显示 Presenter Command 全息面板
    When 我点击 `A·受击闪烁`
    Then 首位单位变黄且表内有 pcmd.flash timer
    When 我等待 0.6 秒
    Then 单位颜色复原为蓝

  Scenario: A 站压制后不再到期
    Given 单位已受击变黄
    When 我点击 `A·压制复原`
    Then timer 表被清空且单位变绿
    And 等待超过 0.6 秒后颜色保持绿色

  Scenario: B 站循环灯色当帧生效
    When 我点击 `B·循环灯色`
    Then 三根灯柱同步换色且 emit 请求携带新颜色

  Scenario: B 站强制刷新对照柱
    When 我点击 `B·强制刷新对照柱`
    Then 对照柱不写值也产生一次重 emit

  Scenario: C 站烟囱开关
    When 我点击 `C·烟囱开关`
    Then BehaviorActiveMask 的 body 位置位
    When 我再次点击 `C·烟囱开关`
    Then 该位清零

  Scenario: D 站召唤/拆除/清场/传送
    When 我点击 `D·召唤靶标` 两次
    Then 场上存在 2 个 scoped 靶标
    When 我点击 `D·精确拆除`
    Then 剩余 1 个
    When 我点击 `D·路由销毁`
    Then 剩余 0 个
    When 我再次召唤后点击 `D·整域清场`
    Then 该 definition 查不到任何实例
    When 我点击 `D·传送门`
    Then 传送门世界位置等于 owner 变换加 anchor 偏移
```
