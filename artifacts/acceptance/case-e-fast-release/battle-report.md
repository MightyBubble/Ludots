# Case E 快速框选清理验收

日期：2026-09-08。基线：`e92a1b499d`，分支 `codex/case-e-fast-release`。

## 1. 概述

玩家快速按下、拖动、抬起鼠标时，框选矩形或黄色预览会留在画面上。本次修复输入处理与交互监听同步的时序，使抬起完成选中结算，并清掉本轮预览。

确定性测试覆盖 14 个新用例、154 次手势：8 组单次快拖各重复 4 次；4 组连续快拖各 20 次；2 组连续快拖后立即重按各 21 次。与原有 Case E 用例合计 21 项，全部通过。

真实窗口使用本地 10k 地图、每位玩家 5,000 名候选。玩家 1 连续快拖 20 次后选中 1,403 名单位；将按下、移动、抬起积累到同一逻辑拍后仍为 1,403 名，矩形与黄色预览均已清理。玩家 2 的独立配置问题未通过验收，见边界。

## 2. 结构

复用现有链路：固定步输入快照 → 动作绑定派发 → 交互上下文监听门控 → 配置声明的清理与结算图 → Presenter 显示管线。

- [动作派发](../../../src/Core/Gameplay/MapTriggers/TriggerGraphActionBindingSystem.cs)：按键转换先完成，随后处理鼠标移动；派发期间复用动作和监听快照。
- [上下文门控](../../../src/Core/Gameplay/MapTriggers/InteractionContextTriggerMountSystem.cs)：每次动作完成后，仅核对动作主体的交互上下文，并落实监听增删。
- [绑定索引](../../../src/Core/Gameplay/MapTriggers/TriggerGraphActionBindingIndex.cs)：记录监听是否仍注册，避免执行快照中已经失效的监听。
- [验收测试](../../../src/Tests/GasTests/Production/CaseESelectionShowcaseAcceptanceTests.cs)：使用真实地图、图配置、输入快照和 Presenter 管线，屏幕投影使用确定性替身。

未新增配置项。本次不涉及 graph op、GAS preset、实体生命周期实现或配置 schema，GAS 组合门禁不适用。

## 3. 详情

### 复现与修复

| 条件 | 修复前行为 | 修复后约束 |
| --- | --- | --- |
| 按下与抬起进入同一逻辑拍 | 框选开启后，抬起监听要等下一拍才挂上，释放丢失 | 按下完成后立即挂监听，本拍的抬起可以完成结算 |
| 抬起时鼠标也发生移动 | 清理槽执行后，旧移动监听仍运行，重新填入黄色预览 | 监听撤销完成后才进入移动处理，已失效的监听不执行 |
| 上一框抬起与下一框按下同拍 | 固定按下优先会提前关闭新一框 | 快照最后仍按住时，先结束旧一框，再开始新一框 |

最初 8 个快拖复现用例在原实现中失败 5 个。补充选中名单断言又发现连续重按的顺序问题；最终同时验证清理、选中名单、继续按住时预览仍然有效。

### 历史核对

- `70d711f291` 将退出槽改为同步执行，但监听撤销继续延后到门控更新。
- `1462ae6777` 引入前台交互挂起；`1a176204d7` 加固其窗口核对。
- `70cef71dee` 引入按位置变化触发的 `PointerMoved`，使延迟撤销后的旧监听能够在清理后重新写入预览。
- `caa8113bb4` 限定只派发当前已挂载动作。本次仍保留这个约束：扫描已知动作是为了发现同拍新挂上的释放监听，不对未挂载动作求值。

新增核对只访问当前动作主体的上下文与监听，不按世界人口、候选数或选中数全表扫描。动作和监听列表复用容量；上下文本身的挂载仍可能分配对象，本报告不声称整条交互链零分配。本次验证正确性，未新增帧耗时基准。

## 4. 场景与记录

确定性场景为 `case_e_selection_field`，使用地图固定布局，无随机生成。四名己方单位在投影横坐标 -900、-300、300、900，纵坐标为 0；逐渲染帧调用引擎，固定步由原有节拍器推进。

1. 指挥官从 (-1200,-100) 按下，只保持 1、2、3 或 6 个渲染帧。
2. 拖到 (-300,100) 后抬起，应选中 2 名；抬起时再移到 (400,100)，应选中 3 名。
3. 等待 8 个渲染帧，框选状态关闭、预览集合为空、矩形实例为 0、可见黄色环为 0。
4. 连续做 20 次，中间只松开 1 或 2 帧，最后一次仍选中 3 名。
5. 连续操作后立即再按住，框和预览保持到这次真正抬起，随后清理。

逐用例记录为同目录的 `release-*.jsonl`、`repeat-*-False.jsonl` 和 `repeat-*-True.jsonl`；合并索引见 [trace.jsonl](trace.jsonl)，测试前后结果见 [validation.txt](validation.txt)，路径图见 [path.mmd](path.mmd)。

真实窗口记录见 [live.jsonl](live.jsonl) 和 [重放脚本](verify-live.ps1)。截图：[按住预览](held-preview.png)、[20 次快拖后](after-20-fast-drags.png)、[同拍抬起后](after-same-tick-release.png)。截图清理结论经过目视检查；精确的集合和实例计数由确定性测试断言。

## 5. 边界

- 广泛回归：285 项中 284 项通过。唯一失败为 `TriggerGraphRenameMigrationTests.ProductionGraphsAndMaps_UseRenamedKindAndMountField`。它用整文件字符串匹配禁止 `MapTrigger`，命中了夜袭地图的合法 `MapTrigger.PointerScreenX/Y` 参数。测试及资产与基线完全相同，本次未改。
- 本地 10k 地图尚有未提交配置：玩家 2 的代表实体使用 `case_e_raider`，该模板没有 `initialInteractionContext`。实测切换后本人候选 5,000、选中 0；[截图](player-2-after-10-fast-drags.png)及脚本失败结果保留。该场景不能宣称双玩家框选验收通过，需在 10k showcase 所属工作中补齐。相关模板见 [templates.json](../../../mods/showcases/case_e_selection/CaseESelectionMod/assets/Entities/templates.json)。
- 真实窗口验收规模是 10,000 个世界单位、1,403 个被选单位，不代表同时选中 10,000 个单位的帧耗时测试。
- 每个固定步快照只保留动作的按下、抬起标志及最后按键状态，本次遵守该聚合合同，不重放一个逻辑拍内任意多次完整点击的逐事件历史。
- 所有 Presenter 仍走现有配置与运行时；没有增加定时强制擦除、Case E 专用 Core 分支或全图清理。

## 6. UAT

```gherkin
Feature: 快速框选结束后画面干净，选中名单正确
  Scenario: 快速拖动并松开鼠标
    Given 我已进入框选场，面前有可以选择的己方单位
    When 我快速拖框圈住这些单位并松开鼠标
    Then 被框住的己方单位进入选中名单
    And 框选矩形和黄色预览消失
    And 蓝色选中标记保留

  Scenario: 松开时仍在移动鼠标
    Given 我正在拖框选择单位
    When 我一边移动鼠标一边松开
    Then 按最后的框选区域完成选择
    And 黄色预览不会在框消失后重新出现

  Scenario: 连续快速框选后立即开始下一框
    Given 我已经连续快速框选了二十次
    When 我立即再次按下并保持鼠标左键
    Then 新的框选矩形和黄色预览持续显示
    When 我松开鼠标左键
    Then 本轮选择完成，框选矩形和黄色预览消失
```

复跑：`dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter FullyQualifiedName~CaseESelectionShowcaseAcceptanceTests`。
