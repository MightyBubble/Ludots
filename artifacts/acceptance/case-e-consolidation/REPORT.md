# Case E 基建复用审计与整合报告

## 1. 概述

本次对照最新远端 main `886e03449c`、查询修复 `2a383853a2`、标记复用 `e92a1b499d`、输入修复 `47fd0480df`、Presenter 性能分支 `fb07451339`，以及尚未提交的万人场景代码。整合在独立工作区完成，未修改主开发目录。

提交前再次同步远端 main `daf70a5b48` 的区域扫掠更新，合并提交 `a733064881`；同步后 Case E、区域与教学场景回归 65/65，通过日志为 `main-sync.log`。

发现并处理了重复输入派发、重复全图收集、出生后回执改队、面板自带文本/图片替代实现。派生名单索引、空间索引、Presenter 销毁索引继续保留。

## 2. 结构

| 新增部分 | 现有能力与历史 | 本次处理 |
| --- | --- | --- |
| 单座位/多座位输入各写一套 | `TriggerGraphActionBindingSystem`，`70cef71dee`、`47fd0480df` | 合成 `DispatchReader`；单座位同样按当前玩家过滤监听器，保留四阶段手势顺序 |
| `QueryMap` 又写一份实体收集 | `EntitySetQueryRuntime`，`4a766d295a`、`e60c110e35` | 与 `CollectMapEntities` 共用分块遍历；复用 `World.CountEntities`；地图字段按列读取 |
| 回执改队与专用模拟系统 | `RuntimeEntitySpawnRequest.TeamIdOverride`、现有出生系统 | 直接声明出生阵营，删除回执通道、消费循环和 `CaseESelection10kSimulationSystem` |
| 面板自带 NullTextMeasurer/NullImageSizeProvider | 已注入的 UI 服务 | 删除两份替代实现，缺服务明确报错 |
| 标记隐藏与真实销毁 | `3c760240bd`、`fb07451339`、`e92a1b499d` | 继续使用 Param/Behavior/Command 和原来的销毁索引；补齐性能分支遗漏的 9 项测试 |
| 候选名单与空间命中 | `DerivedEntityIndex`、`SpatialQueryService` | 分别维护资格和位置，职责不同，保留 |

主线区域实体实现仍依赖已被 Case E 分支移除的地图心跳。合并时保留 `RegionVolumeTriggerSystem`，接入固定步更新；不恢复旧 `RegionTriggerSystem` 或全图生命周期心跳。无区域成员时跳过单位位置收集。区域、挂起恢复、运行时区域出生和教学场景均进入回归。

## 3. 详情

### 已完成

- 输入派发净减少 132 行。两名玩家都挂有操作监听器时，单座位输入只发给当前玩家。
- 查询结果数量和原有遍历顺序均有断言。Arch 逐行枚举为倒序，批量复制后保留相同顺序。
- 万人场景出生时就带最终阵营和玩家归属。士兵模板的 `Team.Id=0` 表示不固化阵营；小场景四名士兵在地图上明确配置阵营 1。出生请求中的 `TeamIdOverride` 与 `PlayerOwnerIdOverride` 显式给值。原来的固定阵营模板与不同出生阵营会被引擎拒绝，不能事后改队绕过校验。
- 玩家 2 复用已有指挥官模板及交互配置；地图覆盖阵营和位置。两名玩家各 5,000 候选，完整框选和 1→2→1 切换通过无头验证。
- 面板释放复用 UI surface lease；地图卸载先释放，再清理句柄。
- 蓝环、黄环显式配置已有 `activationCondition: { inline: TargetIsSolePossessedRep }`。编译器把当前玩家依赖交给 PresenterBehaviorSystem；切换只改激活状态，保留名单、实例和可见参数。没有新增 audience 配置、枚举或业务组件。

### 配置结构

蓝环、黄环的行为槽使用同一形状，集合增减仍由原有全局规则写 `case_e.ring.visible`：

```json
{
  "slot": "body",
  "kind": "AssetBinding",
  "activationCondition": { "inline": "TargetIsSolePossessedRep" },
  "assetBinding": {
    "assetKind": "Mesh",
    "assetId": "case_e.select_ring_slab",
    "materialId": "default_surface",
    "renderPath": "StaticMesh",
    "mobility": "Static",
    "visibilityParamKey": "case_e.ring.visible"
  }
}
```

这段为行为槽节选；完整默认参数、尺寸、样式和集合规则见 Mod 的 `assets/Presentation/presenters.json`。条件判断的 Target 来自集合事件中的名单拥有者，不从集合名字或颜色猜玩家。

### 性能对照

Release、`DOTNET_TieredCompilation=0`，同机分开运行。查询前后使用完全相同的新测试；旧代码侧只修改测试。以下是本次实测，不是历史 CSV 的转录。原始日志和 CSV 位于本目录。

| 测量范围 | 整合前 | 整合后 | 分配 |
| --- | ---: | ---: | ---: |
| 10,000 实体，全图收集 2,000 次 | 29.1551 ms | 9.9960 ms | 两边均 0 B |
| 10,000 实体，指定地图收集 2,000 次 | 129.3364 ms | 49.2484 ms | 两边均 0 B |
| 10,000 人口，单实体改队及索引读取 10,000 次 | 4.7140 ms | 4.3583 ms | 两边均 0 B |
| 10,000 标记，重复提交四次均值 | 18.5994 ms | 13.4009 ms | 两边均 0 B |
| 10,000 标记，重复清除四次均值 | 6.5190 ms | 5.5532 ms | 两边均 0 B |
| 10,000 标记，第一次提交 | 58.7804 ms | 48.9742 ms | 仍有首次创建分配 |

查询下降可对应到本次合并循环、列读取和批量复制。上表 Presenter 测量发生在动态玩家条件接入前，不能归因为此次条件刷新优化。其保护条件是复用后实例数、结构版本、视觉标识数量不增长，重复显示/隐藏零分配。动态条件另测一万单位、两套标记、共三万保留实例；蓝环和 HUD 分别验证切换、隐藏期间取消、恢复及重复清除选择，结果见 `possession-final.log`，不等于客户端 GPU 帧耗时。

动态切换最终测量在关闭本次验证进程后执行，20 次切换平均：Mesh 4.122875 ms、WorldText/HUD 6.126780 ms，均为 0 B。相同实例随后重复清除和恢复三次也为 0 B，实例数、结构版本和实体 archetype 不变。普通稳定帧只检查依赖版本；新实例、关联关系或当前玩家变化才遍历有关定义的实例。

压力夹具原先用“预览数量 >= 0”作为等待条件，会在输入尚未推进时直接结束。修正前两个基线用例均失败；修正同一驱动后，前后版本均验证 104/104 和 1,004/1,004 的候选及完整预览。较大地图另测两名玩家各 5,000 的完整选择。以上不等于客户端万人同时选中的 FPS 测量。

## 4. 场景与证据

- 目标回归：147/147，日志 `delivery-final.log`；Presenter 扩展回归与切换耗时见 `possession-final.log`。原有 9 项复用测试包含在扩展回归中。
- Presenter 扩展回归 270 项：269 通过，1 项旧错误文字断言失败。`Load_RejectsBindingsMissingRequiredSourcePayload` 的 graph 用例期待 `Presenter binding graph.sourceId`，实际错误包含更具体的配置路径；原性能分支 `fb07451339` 的已有 Release 测试产物也复现同一失败。此次变更的 26 项专项全部通过。基线复现见 `loader-baseline.txt`。
- 实机：独立 Raylib 进程，经 Agent Bridge 确认地图 `case_e_selection_10k_field` 与 pumpCount 增长；点击玩家按钮、调整镜头、真实鼠标拖框。
- 实机结果：玩家 1 选中 4,089；玩家 2 选中 4,900；切回玩家 1 为 4,089；桥日志新增错误 0。此次实际框取区域不同，不把这些数量称为完整框住全部单位。
- `trace.jsonl` 保存调用和结果；`player-1.png`、`player-2.png` 是目标进程截图；`verify-live.ps1` 可复跑。
- 动态隐藏实机验收单独保存在 `visibility-trace.jsonl`。玩家一选中 4,089 个单位后，场景蓝色标记像素为 17,908；切换玩家二后为 0；切回玩家一恢复为 17,908。`visibility-player-1-selected.png`、`visibility-player-2-hidden.png`、`visibility-player-1-restored.png` 均来自独立进程 47922；`verify-player-visibility.ps1` 检查画面而非仅检查面板文字。日志新增错误 0。
- 启动：`.\scripts\run-mod-launcher.cmd cli launch '$case_e_selection' mod:AgentBridgeMod --adapter raylib --build always`。

## 5. 边界与未解决项

1. **玩家条件范围。** 此次动态刷新针对已有唯一操控座位条件；多视口观看权限、任意图条件依赖跟踪不在该条件合同内。条件更新复用定义实例索引；实例或关联关系改变时也会刷新，不能称为任意事件恒定成本。Mesh 与 WorldText 输出已有万人回归，其他行为的完整生命周期和耗时不能由这两项外推。
2. **P1：首次大批量标记创建仍会分配。** 隐藏复用解决再次显示和清除的结构变更，未实现配置驱动的预留策略。48.9742 ms 的第一次提交不满足单帧 16.7 ms 目标。
3. **查询声明仍有范围限制。** `BindCollection` 目前接受的查询前导指令及过滤依赖是受限集合，不能声称支持任意谓词。一次性 `QueryMap` 仍使用可增长结果缓冲；这次没有实现令牌化分页，也没有把全部过滤图编译成 ECS 执行器。
4. **索引重求值不等于整链 O(1)。** 单实体修改只重求值一次，但集合源成员变化后 `SynchronizeSource` 仍物化整份集合。引用方式写组件必须发出变更通知；不能承诺任意裸引用写入都自动刷新。
5. **两份玩家查询图暂时保留。** 现有绑定器不能直接把当前玩家作为受跟踪动态参数，并处理该参数变化。直接拼接一套参数 DSL 会重复建设；本次不以隐藏约定替代它。
6. 本次完成单座位切换玩家的表现隔离；首次万人提交性能仍未达标，未跑全仓测试。

## 6. UAT

```gherkin
Feature: 在万人场景操作自己的部队
  Scenario: 两名玩家轮流框选
    Given 场景中有两名玩家各自的五千名单位
    When 我切换到玩家一并框住全部单位
    Then 玩家一的已选名单有五千名单位
    When 我切换到玩家二并再次框选
    Then 玩家二的已选名单有五千名单位
    And 玩家一的名单保持不变

  Scenario: 快速结束框选
    Given 我正在拖动选择框
    When 我快速松开并再次按下鼠标
    Then 已结束手势的预览名单被清空
    And 新手势能够继续选择

  Scenario: 反复选择同一批单位
    Given 我已经选择过这一批单位
    When 我清除选择再选回来
    Then 标记正确消失并再次出现
    And 不会越来越多地叠出重复标记

  Scenario: 换玩家时只显示当前玩家的选中标记
    Given 玩家一已经选中一批单位
    When 我切换到玩家二
    Then 玩家一的选中标记隐藏
    And 切回玩家一后原有标记恢复

  Scenario: 隐藏期间取消预览
    Given 玩家一的预览在我查看玩家二时已经取消
    When 我切回玩家一
    Then 被取消的黄色预览不会重新出现
```
