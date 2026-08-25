# 管线与表现合同：Trigger / 地图变量 / 字幕 / 命令轨 / 相机

**跨域管线与表现层四个能力的合同，只认本页。** 入口在 [叙事线 Showcase](narrative-showcase-line.md)。和本页打架的，听本页的。

引擎实现在 `src/Core/Scripting/TriggerManager.cs`、`src/Core/Gameplay/MapTriggers/MapVariableStore.cs`、`mods/capabilities/narrativefrontend/`、`src/Core/Gameplay/Camera/`。

---

## 1. 概述

叙事域不侵入其他职责的落地方式：一切跨域效果（写地图变量、开活动、发镜头震动）只发生在触发器订阅方；一切玩家可见的呈现（字幕、镜头）走正式 presenter 链。作者为跨域反应写订阅代码时，模板就一段。

## 2. 结构

```text
订阅     = ModEntry.OnLoad 里 context.OnEvent(EventKey, handler)
地图变量 = 地图 JSON 声明，订阅方读写，奇偶分页即范例
字幕     = NarrativeDirector 视图 → feeder → NarrativeFrontend → UI
命令轨   = CinematicStepEntered 每步 → CameraImpulse.Emit（官方副作用出口）
相机     = ActivateCamera/ClearCamera 切虚拟相机档案（域内合法动作）
```

## 3. 详情

### 3.1 Trigger 订阅（跨域反应唯一通路）

`context.OnEvent(TaskEventKeys.Signal, ctx => { ...; return Task.CompletedTask; })`；`ctx.GetEngine()` 取引擎，`ctx.Get(类型化ServiceKey)` 读载荷。完整模板见入口页 §4.2。触发错误进 `TriggerManager.Errors`（验收断言恒为 0）。
验收锚：`narrative_chain` 全链、`map_variable_write`。

### 3.2 地图变量（MapVariableStore）

地图 JSON `Variables: [{"name","type":"int","initial"}]`（小写严格解析）；`engine.CurrentMapSession.Variables.ReadInt/WriteInt`，**只在订阅方写**。变量值可作为订阅方的决策输入（范例：奇偶开不同对话页）。
验收锚：`map_variable_write`（1+1=2 开偶数页，再+1=3 开奇数页）。

### 3.3 字幕 presenter 链

演出/对话视图（`TryGetActiveCinematicView` / `TryGetActiveDialogueView`）→ showcase 的 feeder 系统 → `NarrativeFrontendService.Publish`（按 Signature 去重）→ 前端渲染 `SubtitleBubble` / `OverlayDialogue` / `ChoiceList`。渲染滞后一帧，断言一律等待式。
验收锚：`subtitle_presenter`（三步逐帧替换 + 清屏）。

### 3.4 Presenter 命令轨

每步 `CinematicStepEntered` → 订阅方 `CameraImpulseRuntime.Emit(CameraImpulseSource{...})`（镜头震动特效）。与 §3.5 相机切换是两回事：冲动是叠加特效（域外、订阅方发），ActivateCamera 是镜头语言（域内动作）。
验收锚：`presenter_track`（每步 impulse 计数递增）。

### 3.5 相机联动

演出步/对话节点 `cameraId` → `ActivateCamera` 设 `VirtualCameraRequest` → 相机系统消费；`clearCameraOnComplete` 结束回落。可观测锚：`engine.AuthorityCamera().VirtualCameraBrain.ActiveCameraId`。相机档案由 CameraProfilesMod 提供。
验收锚：`presenter_track`（Tactical→Inspect→回落）、`action_gallery`（开/清断言）、`narrative_chain` 相机步。
