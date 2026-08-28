# save_load showcase 真机验收报告（#1205）

- 进程：Ludots.App.Raylib.exe（preset save_load_showcase_raylib，--adapter raylib）
- mods：LudotsCoreMod, SavePanelMod, SaveLoadShowcaseMod, AgentBridgeMod；map=save_load
- 桥判活：health ok=true，pumpCount 2762→3077 持续增长

## 五时序（持久化专项闸门）

| 时序 | 结果 |
|------|------|
| T1 写入前 | tick 1157，digest 2C30B362E7045655 |
| T2 写入 | manual/uat-coldstart，tick 1884，17,628,138 bytes，digest 5DE9B9935E50F045 |
| T3 写入后读回 | digest 5DE9B9935E50F045 == 写入 digest；schema 1 |
| T4 冷启动 | 杀进程（pid 84504）→ 重启（新 pid 59564）→ 两槽位存活 |
| T5 读取后继续 | restore：restoredTick 1884，digest 5DE9B993 == 存档 digest（跨进程一致）；继续运行 tick 2377 |

落盘路径：`src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net9.0/Saves/saves/manual/*.ldsave`

## UI 交互证据

- `ui.query` 列出 5 个 showcase 按钮（Nudge hero / Save via panel / Restore latest / Spawn excluded decoy / Corrupt latest slot）
- `ui.click elementId=save-load-save` → handled:true → 槽位 2→3（新槽 panel-20260828-062720 tick 4253）——**SavePanelMod 面板按钮真实驱动并写盘**
- SavePanelMod 运行时自证：槽位列表含面板自身写的 panel-20260828-062004

## 截图

- save-load-t5-cold-restore.png（冷启动读档后继续运行）
- save-load-final.png（终态）

工具调用原始记录：bridge-trace.log
