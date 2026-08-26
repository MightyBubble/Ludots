# 统一时间轴编辑器

面向作者：用同一套轨道界面编三种已经存在的时间合同。运行时各走各的加载器和系统，编辑器只做投影和写回。

相关合同：

- 演出序列：`Sequencer/sequences.json`，见 [Story Runtime](story-runtime-dialogue-sequencer.md)
- 技能时间轴：`GAS/abilities.json` 的 `exec.items`，见 [ab-02](../reference/mod-editor-prd/uxd/ab-02-exec-timeline.md)
- 演出计时：`Presentation/presenters.json` 的 `TimerSet` / `TimerExpired` / `TimerKill`，见 [Presenter-as-Actor](presenter-as-actor-architecture.md)

## 1. 概述

打开编辑器后进 `/timeline`，先选上下文，再选 Mod 和文件。中间是轨道，左边是条目清单，右边是选中块的字段。保存时按原文件形状写回，不另存一份“统一时间轴资产”。

三种上下文共用拖块、改时长、调色板和校验条。差别只在适配器：秒还是 tick、有哪些轨道、哪几个字段能写。

本地预览只移动编辑器指针，用来看块会不会叠在一起。它不是引擎试播，也不调用 `SequencerRuntime` / `AbilityExecSystem` / `PresenterTimerTable`。

## 2. 结构

```text
React /timeline
  └── TimelineEditor（轨道画布）
        ▲ project / apply
  ├── sequencer 适配器      → Sequencer/sequences.json
  ├── ability-exec 适配器   → GAS/abilities.json 或 GAS/abilities/*.json
  └── presenter-timer 适配器 → Presentation/presenters.json 或 presenters/*.json

Editor.Bridge
  GET  /api/mods/{modId}/timeline/catalogs
  GET  /api/mods/{modId}/timeline/file?relativePath=…
  PUT  /api/mods/{modId}/timeline/file?relativePath=…
```

实现入口：

- 画布：`src/Tools/Ludots.Editor.React/src/pages/timeline/TimelineEditor.tsx`
- 适配器：`src/Tools/Ludots.Editor.React/src/pages/timeline/contexts/`
- 叙事页仍用同一画布：`src/Tools/Ludots.Editor.React/src/pages/story/SequencerTimelineEditor.tsx`
- Bridge 允许名单：`EditorRepo.TimelineCatalogs`

## 3. 详情

### 3.1 启动

```bash
dotnet run --project src/Tools/Ludots.Editor.Bridge -c Release
cd src/Tools/Ludots.Editor.React && npm ci && npm run dev
```

打开 <http://localhost:5173/timeline>。地图编辑器底部也有「时间轴」入口。

### 3.2 三个上下文

| 上下文 | 作者在编什么 | 时间单位 | 轨道怎么来 | 拖动写回什么 |
|---|---|---|---|---|
| 演出序列 | 镜头、字幕、到点信号 | 秒 | Camera / Subtitle / Signal | `start` / `duration` |
| 技能时间轴 | 持续、瞬发、等待、收束 | tick | Clip / Signal / Gate / End | `tick`；Clip 写 `duration` 或 `durationTicks`；数组顺序不动 |
| 演出计时 | 命名倒计时和到期反应 | 秒（从触发算起） | 每个计时名一条；另有生命周期 / 打断 / 通配到期 | 只写 `durationSeconds` 和规则字段；开始时刻由触发链推出来，不能拖起始 |

技能条目上限 16，满了调色板禁用。消费按数组序，tick 乱序只警告，不重排。

演出计时里，`TimerSet` 是持续条，`TimerExpired` 上的其他命令落在该计时结束处，`TimerKill` 落在打断轨。`presenter.duration` 和 `*` 不能当 `TimerSet` 的名字。

### 3.3 落盘

Bridge 只接受这几类相对路径：

- `Sequencer/sequences.json`
- `GAS/abilities.json` 与 `GAS/abilities/*.json`
- `Presentation/presenters.json` 与 `Presentation/presenters/*.json`

路径带 `..` 或落到允许名单外，直接拒绝。写盘前要求数组每项都有非空 `id`。

叙事配置页 `/story-authoring` 仍走原来的 story catalog 接口，写的是同一份 `Sequencer/sequences.json`。

## 4. 场景

1. 编开场过场：选「演出序列」，打开 `NarrativeShowcaseMod` 的 `Sequence.Narrative.Intro`，把第二条字幕拖到镜头切换之后。
2. 编建造技能：选「技能时间轴」，打开 `RtsRedAlertLikeShowcaseMod` 的 `Ability.Rts.RedAlert.BuildPowerPlant`，把某个 `EffectSignal` 的 tick 从 30 改到 36。
3. 编受击闪一下：选「演出计时」，打开闪烁广场那条 Presenter，把 `pcmd.flash` 从 0.6 秒拉到 1.2 秒；到期恢复颜色的反应跟着移到新的结束处。

## 5. 边界

- 编辑器不发明第四种运行时时间轴，也不把三种合同合成一份新 schema。
- 本地预览不是干跑。技能试播仍要等引擎侧干跑接口；演出序列预览仍要跑 `SequencerRuntime`。
- Presenter 计时在运行时是实例上的命名倒计时，不是全局 NLE。时间轴上的开始时刻只是反应链投影。
- 不写 Presenter / Ability 的合并视图。改的是当前 Mod 自己的文件。
- 满 16 条技能条目、保留计时名、通配 `TimerSet`：适配器失败关闭，不静默丢掉。

## 6. UAT

```gherkin
Feature: 作者用同一套时间轴编三种合同
  作者打开编辑器的时间轴页，切换上下文后仍能看懂轨道、拖块、保存。

  Scenario: 演出序列拖镜头
    Given 作者打开 /timeline 并选「演出序列」
    And 目标 Mod 是 NarrativeShowcaseMod
    And 当前条目是 Sequence.Narrative.TrialReveal
    When 作者把镜头块从 0 秒拖到 0.4 秒
    Then 轨道上镜头块停在 0.4 秒
    And 保存后 Sequencer/sequences.json 里该轨道的 start 是 0.4

  Scenario: 技能条目改到达 tick
    Given 作者打开 /timeline 并选「技能时间轴」
    And 目标 Mod 是 RtsRedAlertLikeShowcaseMod
    And 当前条目是 Ability.Rts.RedAlert.BuildPowerPlant
    When 作者把第二条 EffectSignal 拖到 tick 8
    Then 该块显示在 8t
    And 数组里它仍是第 2 项
    And 保存后该项的 tick 是 8

  Scenario: 技能轴满员
    Given 当前技能的 exec.items 已有 16 条
    When 作者再点调色板里的「效果瞬发」
    Then 编辑器拒绝添加
    And 提示已经满 16

  Scenario: 演出计时改闪烁长度
    Given 作者打开 /timeline 并选「演出计时」
    And 当前文件是闪烁广场那条 Presenter
    When 作者把 pcmd.flash 的右边拖到 1.2 秒
    Then 持续条变成 1.2 秒
    And 到期恢复颜色的点跟着落到 1.2 秒
    And 保存后对应 TimerSet 的 durationSeconds 是 1.2

  Scenario: 不能手写保留计时名
    Given 作者选中一条 TimerSet
    When 作者把计时名改成 presenter.duration
    Then 编辑器拒绝这次改动
    And 原规则保持不变
```

**相关文档**：[Story Runtime](story-runtime-dialogue-sequencer.md) · [ab-02 执行时间轴 UXD](../reference/mod-editor-prd/uxd/ab-02-exec-timeline.md) · [Presenter 指令目录](../reference/presenter-capability-catalog/commands.md)
