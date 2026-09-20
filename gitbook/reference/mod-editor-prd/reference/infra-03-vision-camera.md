# infra-03 reference · 视野与相机

> 现状参考。第一性需求见 [infra-03 PRD](../prd/infra-03-vision-camera.md)；配置说明见 [infra-03 配置说明](../config/infra-03-vision-camera.md)。

## 1. 现状快照

- Vision/fog_layers：ArrayById；字段 id、cellSizeCm、updateHz（均正数校验）；引擎默认三层 ground(100/10)、air(250/5)、detection(100/10)。
- Camera/virtual_cameras：ArrayById；七台预设 Moba/Rts/TopDown/Tactical/Default/TPS/FPS；字段 id、displayName、priority、rigKind（Orbit/TopDown/ThirdPerson/FirstPerson）、distanceCm/pitch/fovYDeg/yaw、min/max 距离与俯仰、panMode+edgePan（margin/speed）+panCmPerSecond、enableGrabDrag、rotateMode、enableZoom/zoomCmPerWheel、followMode/followTargetKind、allowUserInput；消费 cameraRuntimeSystem。
- 叙事域按 cameraId 引用预设（dialogues 节点与 cinematics 步骤）；mod 覆盖样例见 NarrativeShowcaseMod。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 迷雾层加载校验 | src/Core/Vision/Config/VisionFogLayerConfigLoader.cs:16,48-50 |
| 相机预设加载（枚举与边界校验） | src/Core/Gameplay/Camera/VirtualCameraDefinitionLoader.cs:26 |
| 相机运行系统挂接 | src/Core/Engine/GameEngine.cs:1842 |
| 实配资产 | assets/Vision/fog_layers.json、assets/Camera/virtual_cameras.json |
| mod 覆盖样例 | mods/showcases/narrative/NarrativeShowcaseMod/assets/Camera/virtual_cameras.json |

**相关文档**：[infra-03 PRD](../prd/infra-03-vision-camera.md) · [misc-03 reference](misc-03-narrative.md)
