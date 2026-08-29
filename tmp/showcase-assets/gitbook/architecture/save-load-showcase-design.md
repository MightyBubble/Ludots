# save_load showcase 设计

> 状态：已实现，待运行验收（真机证据待补；网络窗口恢复后按 ludots-showcase-design 闸门 6/7 采集）。

## 一句话与目标用户

30 秒看懂「存档」：挪动英雄、存进真实磁盘槽位、继续玩、读档——世界回到存档点；冷启动后槽位还在。写给没读过代码的玩家与 mod 作者。

## 主循环

- **谁改变世界**：玩家点 [Nudge hero]（英雄位置随机漂移 ±900cm）。
- **用户看到什么**：青圈（英雄当前位置）与洋红框（存档点）之间的连线随操作伸长——drift 可见。
- **惊喜时刻**：读档瞬间 drift 归零、两圈重合；退出进程重进，槽位还在（冷启动闭环）。

## 消融对照

save point vs live：存档点位置常驻洋红标记；世界动=线拉长；读档=重合。无存档时只有青圈（世界一去不回）。

## 解释层

- HUD：tick、英雄坐标、存档点（tick + 坐标）、drift（cm）、存储根路径；
- 颜色：青=live、洋红=save point、绿=restored 匹配、红=fault；
- 故障可读：[Corrupt latest slot] 翻转槽位字节 → [Restore latest] 红字 section hash mismatch，无静默降级。

## 旋钮

| 旋钮 | 演示什么 |
|------|----------|
| Nudge hero | 世界状态可变（存的东西有意义） |
| Save via panel / Restore latest | 槽位往返；走 SavePanelMod 面板运行时（零拷贝复用） |
| Spawn excluded decoy | SaveExcludedTag：什么不进档（restore 后消失） |
| Corrupt latest slot | 完整性闸门 fail-fast 可见 |

## 场景结构

单场景 Grid 地图 save_load（英雄 + 两个标记实体）；左侧导引面板（阶段提示 + 实时状态），右侧 F5 槽位面板（SavePanelMod）。

## 门户资产

截图/录屏：惊喜时刻帧（读档后两圈重合 + drift 0）为封面；证据落 `artifacts/acceptance/save-load/`；验收文本 `gitbook/acceptance/save-load.feature`。

## 反向 API 审计

- SavePanelRuntime 的槽位操作需公开可组合（已满足：GlobalContext 暴露 + public 方法）；
- 槽位 header 元数据展示需 ListSlots + header 字段（已有）；
- 引擎存储根路径需可读（DesktopSaveStorage.RootDirectory，#1291 落地）；
- 无缺口项。

## 交付边界与完成判据

- 入口：`launcher preset save_load_showcase_raylib`（selectors: save_load_showcase + save_panel + agent_bridge）。
- 持久化专项闸门（skill）：写入 → 冷启动 → 重读 → 继续操作 五时序证据 + 槽位路径标注——待真机验收补齐；本 showcase 在该证据补齐前不宣告「可玩交付完成」。
