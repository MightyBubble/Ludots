# 瞄准控制方案

同一名英雄、同一批瞄准图，四套 control scheme 只换键位。

数据在 `mods/showcases/aim_control_schemes/AimControlSchemesShowcaseMod`。技能、指示器、取消、提交和施法模式都是 `AimInteractionMod` 的图。本场挂进入图，把 F1 / F2 / F3 绑到 `Aim.CastMode.Smart`、`Aim.CastMode.AimRelease`、`Aim.CastMode.AimClick`，键鼠技能方案另有一张左键命令图。

| 方案 | 瞄准设备 | 左键 / 摇杆 |
|---|---|---|
| `scheme.aim.kbm` | 光标 | 左键确认 |
| `scheme.aim.gamepad` | 右摇杆 | 右肩确认 |
| `scheme.aim.kbm_skills` | 光标 | 左键 `Aim.Primary`：敌对单位攻击，否则移动；空格确认 |
| `scheme.aim.gamepad_left` | 左摇杆 | 左摇杆同时驱动 `Aim.Stick` 和 `axisMove` |

施法模式看玩家的 `InteractionPref`。种子是 `castMode.smart`。F1 / F2 / F3 分别写成智能、松开施法、点击确认。

```
scripts/run-mod-launcher.cmd cli launch preset:aim_control_schemes_raylib --adapter raylib
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --filter "FullyQualifiedName~AimControlSchemesShowcaseAcceptanceTests"
```
