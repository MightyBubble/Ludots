# infra-03 配置说明 · 视野与相机

> 配置写法与行为。第一性需求见 [infra-03 PRD](../prd/infra-03-vision-camera.md)；编辑器需求见 [UXD](../uxd/infra-03-vision-camera.md)；现状见 [reference](../reference/infra-03-vision-camera.md)。

## 1. 示例配置

引擎真实迷雾三层（`assets/Vision/fog_layers.json` 全量）：

```json
[
  { "id": "ground", "cellSizeCm": 100, "updateHz": 10 },
  { "id": "air", "cellSizeCm": 250, "updateHz": 5 },
  { "id": "detection", "cellSizeCm": 100, "updateHz": 10 }
]
```

引擎真实相机预设（`assets/Camera/virtual_cameras.json` 的 Moba 行，节选）：

```json
{
  "id": "Moba",
  "displayName": "MOBA",
  "priority": 0,
  "rigKind": "Orbit",
  "distanceCm": 3000, "pitch": 55, "fovYDeg": 50, "yaw": 180,
  "minDistanceCm": 2000, "maxDistanceCm": 5000,
  "minPitchDeg": 50, "maxPitchDeg": 65,
  "panMode": "EdgePan",
  "edgePanMarginPx": 15, "edgePanSpeedCmPerSec": 6000,
  "enableGrabDrag": true,
  "rotateMode": "None",
  "enableZoom": true, "zoomCmPerWheel": 500,
  "followMode": "Hold"
}
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| fog_layers | `id` | 迷雾层名；效果侧 revealArea 按 scope/layers 引用（见 fx-19） |
| fog_layers | `cellSizeCm` | 格子粒度；小格子精但贵 |
| fog_layers | `updateHz` | 层重算频率；低频层省算力 |
| virtual_cameras | `rigKind` | Orbit / TopDown / ThirdPerson / FirstPerson 四骨架 |
| virtual_cameras | `distanceCm`/`pitch`/`yaw`/`fovYDeg` | 初始位姿与垂直视场 |
| virtual_cameras | `min/max DistanceCm`、`min/maxPitchDeg` | 缩放与俯仰边界；区间倒置即失败 |
| virtual_cameras | `panMode` + `edgePan*` + `panCmPerSecond` | 平移模式（EdgePan/KeyboardAndEdge 等）与速度、边缘余量像素 |
| virtual_cameras | `enableGrabDrag`/`rotateMode` | 抓拖与旋转交互开关 |
| virtual_cameras | `enableZoom`/`zoomCmPerWheel` | 滚轮缩放与每格距离 |
| virtual_cameras | `followMode`/`followTargetKind` | 跟随模式与目标类型 |
| virtual_cameras | `allowUserInput` | 玩家输入总闸 |

## 3. 文件结构

`assets/Vision/fog_layers.json` 与 `assets/Camera/virtual_cameras.json`（均 ArrayById）。mod 覆盖某台相机时同 id 深合并（如叙事 mod 只改 followMode）。

## 4. 运行时加载效果

迷雾层注册供视野系统周期重算；相机预设注册后由相机运行系统消费，按 id 激活。**生效级别：重启**；运行期切预设（如叙事相机激活）走运行时 API 而非改表。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 迷雾层 cellSizeCm/updateHz 非正 | 启动失败，指明层 |
| 相机 id 缺失/重复合并冲突 | 启动失败 |
| rigKind 非法 | 启动失败 |
| min > max（距离/俯仰） | 启动失败 |
| edgePanMarginPx ≤ 0 | 启动失败 |

## 6. 实例

- `assets/Vision/fog_layers.json`（三层）
- `assets/Camera/virtual_cameras.json`（七预设）；叙事相机的 mod 覆盖用法见 `mods/showcases/narrative/NarrativeShowcaseMod/assets/Camera/virtual_cameras.json`

**相关文档**：[infra-03 PRD](../prd/infra-03-vision-camera.md) · [misc-03 配置说明](misc-03-narrative.md)
