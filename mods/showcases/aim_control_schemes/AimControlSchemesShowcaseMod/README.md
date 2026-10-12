# 瞄准控制方案（aim_control_schemes）

一个英雄、三具假人，把同一套瞄准图放在四套设备方案里跑。本目录没有 C# 工程。

施法模式不是方案上的字段。进图时 `interaction_prefs.json` 的种子是 `castMode.smart`。F1 / F2 / F3 调 `SetCastMode`，四套方案共用这三张图。

## 四套方案差在哪

设备差异只在 `AimInteractionMod` 的 control scheme 和输入 context。本 mod 不另写 `control_schemes.json`：方案按 id 并进能力 mod 的列表，这一场只在 `game.json` 里选座位的初始方案。

| 启动后要切到 | 方案 | 瞄准 | 技能键 | 施法手感 |
|---|---|---|---|---|
| 默认 | `scheme.aim.kbm` | 鼠标光标 `Aim.Pointer` | Q W E R | 左键是 `Aim.Confirm` |
| 运行时切 | `scheme.aim.gamepad` | 右摇杆 `Aim.Stick` | 面键 | 确认是右肩；松开施法用 F2 |
| 运行时切 | `scheme.aim.kbm_skills` | 鼠标光标 | Q W E R | 左键是 `Aim.Primary`（敌对单位 `attackTarget`，其余 `moveTo`）；确认是空格 |
| 运行时切 | `scheme.aim.gamepad_left` | 左摇杆，和 `Move` 同一根 | 面键 | `axisMove` 沿摇杆朝向 `moveTo`；技能点落在英雄朝向上 |

`scheme.aim.kbm_skills` 与 `scheme.aim.gamepad_left` 写在瞄准能力 mod 里，因为换装的是通用键位，不是这一场的英雄。

F1 `castMode.smart`，F2 `castMode.aim_release`（按住转向，松开下令），F3 `castMode.aim_click`（先打开指示器，再确认）。范围圈和指示线跟着 `interaction.context.aim.click` / `interaction.context.aim.release` 的打开和关闭，画在瞄准演员上。悬停圈仍然看 `aim.hover`。

## 怎么启动

```
scripts/run-mod-launcher.cmd cli launch preset:aim_control_schemes_raylib --adapter raylib
```

进图座位是 `scheme.aim.kbm`。另外三套用运行时的 `ControlSchemeRuntime` 切换，启动器不另做四个地图。

## 验收

```
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --filter "FullyQualifiedName~AimControlSchemesShowcaseAcceptanceTests"
```

覆盖每套方案 × 三种施法模式的下令（槽位、单位或地面点）、瞄准手势打开时的悬停集合、取消后不下令，以及从键鼠切到手柄后鼠标不再带动瞄准点、摇杆会带动。
