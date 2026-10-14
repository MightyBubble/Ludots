# 瞄准控制方案

同一名英雄、同一批瞄准图，四套 control scheme 只换键位。

数据在 `mods/showcases/aim_control_schemes/AimControlSchemesShowcaseMod`。技能、指示器、取消、提交和施法模式都是 `AimInteractionMod` 的图。本场挂进入图，把 F1 / F2 / F3 / F4 绑到 `Aim.CastMode.Smart`、`Aim.CastMode.AimRelease`、`Aim.CastMode.AimClick`、`Aim.CastMode.TargetFirst`，键鼠技能方案另有一张左键命令图。

| 方案 | 瞄准设备 | 左键 / 摇杆 |
|---|---|---|
| `scheme.aim.kbm` | 光标 | 左键确认 |
| `scheme.aim.gamepad` | 右摇杆 | 右肩确认 |
| `scheme.aim.kbm_skills` | 光标 | 左键 `Aim.Primary`：敌对单位攻击，否则移动；空格确认 |
| `scheme.aim.gamepad_left` | 左摇杆 | 左摇杆同时驱动 `Aim.Stick` 和 `axisMove` |

施法手感看玩家偏好键 `pref.aim.cast_mode`。种子是 `interaction.context.aim.smart`。F1 / F2 / F3 / F4 经 `graph.aim.set_cast_mode` 写成智能、松开、点击、先选目标四个手势状态。先选目标读代表身上的 `aim.current_target`。没有活着的成员就打开点击瞄准。

```
scripts/run-mod-launcher.cmd cli launch preset:aim_control_schemes_raylib --adapter raylib
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --filter "FullyQualifiedName~AimControlSchemesShowcaseAcceptanceTests"
```
