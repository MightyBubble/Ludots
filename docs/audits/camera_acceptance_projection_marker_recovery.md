# Projection Marker Recovery Audit

本篇记录 `CameraAcceptanceMod` projection 场景中“点击地面不出现 marker”的根因、修复点与验证结果。

## 1 问题定义

在 `camera_acceptance_projection` 中，左键点击地面时，projection 验收需要通过共享输入链路触发射线投影，并生成一个短暂出现后消失的 presenter marker。

故障表现是：

* live app 中点击地面看不到 marker
* 关键输入链路没有走到 projection marker 发射逻辑

## 2 根因

根因不在 projection 渲染，而在 UI 输入捕获边界。

关键路径如下：

1. 验收面板与 showcase 面板把 scene root 做成了全屏节点。
2. `src/Libraries/Ludots.UI/UIRoot.cs` 在 `HandleInput()` 中通过 `Scene.HitTest(...)` 命中这个全屏 root。
3. `src/Libraries/Ludots.UI/Runtime/UiScene.cs` 将 pointer 事件视为 UI 已处理。
4. `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs` 把该帧标记为 `UiCaptured=true`。
5. `src/Core/Input/Systems/InputRuntimeSystem.cs` 将 UI 捕获映射到 gameplay 输入阻断。
6. `CoreInputMod` 的 selection/action 链路未收到 authoritative click，projection marker 逻辑自然不会执行。

这说明问题是跨层输入职责耦合，而不是 projection 本身失效。

## 3 修复

修复原则：

* 不加 fallback
* 不绕过共享 input/action 基建
* 不把 selection/raycast 重新耦回 camera 基建

实际修复点：

* `mods/fixtures/camera/CameraAcceptanceMod/UI/CameraAcceptancePanelController.cs`
  * scene root 改为仅覆盖卡片本体，不再使用全屏根节点吞掉 world click
  * UI 样式调用收缩到当前运行时稳定可用的字符串样式接口，避免 `Ludots.UI` 运行时 API 不匹配阻断验收
* `mods/showcases/camera/CameraShowcaseMod/UI/CameraShowcasePanelController.cs`
  * 同步应用相同的 UI root 边界修复，避免 showcase 重复吞掉 world click
* `src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
  * 增加 panel 不应捕获卡片外 world click 的回归测试

## 4 验证

自动化验证：

* `dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~CameraAcceptanceMod_ProjectionMap_ClickGround_EmitsCueMarkerThenExpires" --no-build`
* 结果：通过

真实运行验证：

1. `scripts/run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib`
2. `scripts/run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib`

运行结果：

* `resolve` 会显示 `rootMods=CameraAcceptanceMod`，并把 `orderedMods` 展开为 `LudotsCoreMod`、`CoreInputMod`、`CameraAcceptanceMod`
* live app 中 projection marker 已重新出现

## 5 结论

结论：本次故障的关键路径是 UI 全屏 root 误吞 world click，导致共享输入链路在进入 projection/raycast 之前就被截断。修复后，marker 已在自动化验收和 live app 中恢复。

## 6 相关文档

* 相机架构与验收背景：见 [../architecture/camera_character_control.md](../architecture/camera_character_control.md)
* CLI 启动规范：见 [../reference/cli_runbook.md](../reference/cli_runbook.md)
