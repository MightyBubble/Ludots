---
文档类型: 开发指南
创建日期: 2026-03-02
维护人: X28技术团队
文档版本: v1.0
适用范围: 3C Camera 标准
---

# 16. Camera 标准规范

## 概述

Editor 和 Raylib 游戏客户端共享同一套轨道相机模型（Orbit Camera），通过 `MapConfig.DefaultCamera` 配置统一初始相机状态。

## 轨道相机模型

相机围绕 `Target` 点做球面运动，由 4 个参数完全确定：

| 参数 | 单位 | 含义 | 默认值 |
|:--|:--|:--|:--|
| `TargetXCm` / `TargetYCm` | cm | 相机注视点（逻辑坐标 → 3D: X=X, Z=Y） | 0 |
| `Yaw` | 度 | 水平旋转。0°=北，90°=东，180°=南 | 180 |
| `Pitch` | 度 | 俯仰角。0°=平视，90°=垂直俯视 | 45 |
| `DistanceCm` | cm | 相机到注视点的距离 | 14142 (~141m) |
| `FovYDeg` | 度 | 垂直视场角 | 60 |

### Yaw 方向约定

```
          North (Z-)
            |
  West -----+-----> East (X+)
  (X-)      |
          South (Z+)

Yaw=0°:   camera at North, looking South
Yaw=90°:  camera at East, looking West
Yaw=180°: camera at South, looking North  ← Editor 默认
Yaw=270°: camera at West, looking East
```

### 3D 坐标映射

```
逻辑层 (cm):  WorldPositionCm.X → 3D X (米)
              WorldPositionCm.Y → 3D Z (米)
              Height            → 3D Y (米)

CameraPresenter:
  camX = target.X + hDist * sin(yaw)
  camY = vDist
  camZ = target.Z - hDist * cos(yaw)
  where hDist = distance * cos(pitch), vDist = distance * sin(pitch)
```

## MapConfig.DefaultCamera

每张地图在 JSON 中声明默认相机：

```json
{
  "Id": "entry",
  "DefaultCamera": {
    "TargetXCm": 0,
    "TargetYCm": 0,
    "Yaw": 180,
    "Pitch": 45,
    "DistanceCm": 14142,
    "FovYDeg": 60
  }
}
```

### 加载优先级

1. `MapConfig.DefaultCamera` — 地图级配置（基础）
2. Mod Trigger — 可覆盖（如 MobaDemoMod 设置 DistanceCm=25000）
3. 运行时用户操作 — 滚轮缩放、右键旋转

### Editor 行为

- **加载地图时**: 从 `mapConfig.DefaultCamera` 读取，设置 Three.js 相机位置
- **保存地图时**: 将当前 Editor 相机状态反推为轨道参数，写入 `mapConfig.DefaultCamera`
- **无配置时**: 使用默认值（yaw=180, pitch=45, dist≈141m, fov=60）

### Engine 行为

- `GameEngine.LoadMap()` 调用 `ApplyDefaultCamera(mapConfig)` 设置 `CameraState`
- 在 `MapLoaded` 事件之前执行，Mod trigger 可以覆盖

## 标准默认值

所有新地图应使用以下默认相机（除非有明确的游戏设计需求）：

```json
"DefaultCamera": {
  "Yaw": 180,
  "Pitch": 45,
  "DistanceCm": 14142,
  "FovYDeg": 60
}
```

这等价于 Editor 的默认视角 `camera.position.set(0, 100, 100); camera.lookAt(0, 0, 0)`。

## Mod 覆盖指南

如果 Mod 需要不同的初始相机（如 MOBA 鸟瞰、RTS 远景），在 MapLoaded trigger 中设置：

```csharp
session.Camera.State.DistanceCm = 25000f;
session.Camera.State.Pitch = 60f;
```

不要修改 `FovYDeg`（保持 60° 统一），除非有特殊需求（如第一人称）。

## 已知的遗留配置

以下 Mod 仍有硬编码相机参数，应逐步迁移到 DefaultCamera：

| Mod | 当前方式 | 建议 |
|:--|:--|:--|
| MobaDemoMod | moba_config.json + trigger | 改为 DefaultCamera + trigger 仅覆盖 target |
| Universal3CCameraMod | 硬编码 yaw=35, pitch=60 | 改为读取 DefaultCamera |
| TerrainBenchmarkMod | 硬编码 yaw=35, pitch=60, dist=40000 | 改为 DefaultCamera |
| Physics2DPlaygroundMod | 硬编码 pitch=60, dist=12000 | 改为 DefaultCamera |
| Navigation2DPlaygroundMod | 硬编码 pitch=65, dist=18000 | 改为 DefaultCamera |
| PerformanceVisualizationMod | 硬编码 dist=80000 | 改为 DefaultCamera |

## 废弃项

- `CameraLogic.cs` — 已被 `ICameraController` 替代，距离限制 500-10000 过时
- Raylib 初始 Camera3D `fovy=45` — 已改为 60 与 CameraState 统一
