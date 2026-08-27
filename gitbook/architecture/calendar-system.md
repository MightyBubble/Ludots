# 历法与周期

本页定义 Ludots 的世界历法层。它不另开调度器：只消费现有 `Step`，把日序投影成年、纪年、季节、月、旬、节气，并在相位变化时发事件。

正式时钟层仍以 [时间体系](time-system.md) 为准。

## 1 概述

游戏世界需要「今天是哪一天、过了哪个节、该结算什么」。Pacemaker / TimeFlow / GAS Step 只回答「走了几步、有没有停」。历法层回答业务时间。

- 全世界共用一个日序（从 0 起的绝对天数）。
- 一份历法表把日序投影成可读日期。可以同时挂多份历，同一天可以有不同纪年。
- 四季、月、旬、二十四节气都是周期：一组相位，长度加起来等于周期天数。
- 没有 `Calendar/clock.json` 时，历法不推进。有这份文件时，缺表、缺字段、相位对不齐一律启动失败。

## 2 结构

```text
Pacemaker / TimeFlow / GAS Step
        ↓ LastConsumedSteps
CalendarSystem
        ↓ ticksIntoDay / ticksPerDay
日序 dayIndex
        ↓ CalendarProjection
历法 A / 历法 B（年、纪年、周期相位）
        ↓
Calendar.DayAdvanced
Calendar.CyclePhaseEntered / Exited
Calendar.EraChanged
Calendar.DayPhaseChanged
```

| 件 | 职责 |
|---|---|
| `Calendar/clock.json` | 世界日怎么走：多少步算一天、从哪天开始、用哪份历、昼夜相位 |
| `Calendar/calendars.json` | 历法表：年长、纪年、周期与相位 |
| `CalendarRuntime` | 日序、当天已走步、投影、存档 |
| `CalendarSystem` | 每个固定步读 `GasClockStepPolicy.LastConsumedSteps`，Paused / 暂停令牌为 0 时不走日 |

推进源只允许 `Step`。`Turn`、`FixedFrame`、`EntityLocal` 都拒绝。

## 3 详情

### 3.1 世界钟

`assets/config_catalog.json` 已登记两条路径。`Calendar/calendars.json` 默认带一份 360 日年的 `calendar.solar360`。`Calendar/clock.json` 允许空：没有这份文件，运行时 `IsEnabled=false`，不发事件、不改日序。

`clock.json` 字段（全部显式必填）：

| 字段 | 含义 |
|---|---|
| `tickSource` | 只接受 `Step` |
| `ticksPerDay` | 多少个 Step 算一天，≥ 1。一天有多长只认这个数 |
| `startDayIndex` | 开局日序，≥ 0 |
| `activeCalendarId` | 主历，必须在历法表里 |
| `dayPhases` | 昼夜相位，按当天进度千分比切。首项 `startPermille` 必须是 0，后面递增且 < 1000 |

当天进度 = `ticksIntoDay * 1000 / ticksPerDay`，读接口是 `CaptureClockSnapshot().DayPermille`。晓、昼、暮、夜查这根轴。钟面（例如 12:34）是界面把千分比画成表，不要在 `clock.json` 再写一套「一天多少分钟」。写了 `minutesPerDay` 装载失败。

Mod 要启用历法，写 `Calendar/clock.json`，并保证 catalog 里有这条 DeepObject（核心 catalog 已登记且 `AllowEmpty: true`）。

### 3.2 历法表

每份历：

- `yearLengthDays`：一年几天。年 = `dayIndex / yearLengthDays + 1`，年内第几天从 1 计。
- `eras`：纪年。第一项 `startDayIndex` 必须是 0，后面递增。当前纪年取「起始日 ≤ 今天」的最后一项。纪年内年号按该纪年起点另算。
- `cycles`：周期。`lengthDays` 是重复周期。`phases[].lengthDays` 之和必须等于 `lengthDays`。

同一日序可以投到多份历。主历只影响 `Calendar.DayAdvanced` 的 `calendarId`；周期进出对每份历各自发事件，载荷带 `Calendar.CalendarId`。

默认 `calendar.solar360`：一年 360 日，四季各 90 日，十二月各 30 日，旬 10 日一转，二十四节气各 15 日。

### 3.3 事件

全局事件，不是地图域：

| 事件 | 何时 |
|---|---|
| `Calendar.DayAdvanced` | 日序 +1 |
| `Calendar.CyclePhaseExited` / `Entered` | 某份历的某个周期换相位 |
| `Calendar.EraChanged` | 纪年切换 |
| `Calendar.DayPhaseChanged` | 当天昼夜相位变了，日序可以不变 |

一次 Advance 跨过多天时，按天逐日发事件，不跳相位。

### 3.4 存档

存档域 `calendar`：`enabled`、`dayIndex`、`ticksIntoDay`、`activeCalendarId`。定义不存，以配置为准。恢复时 enabled / 主历必须和当前配置一致，否则失败。恢复不补发事件。

读当前日期：`GameEngine` 服务 `CalendarRuntime` 的 `Project` / `CaptureClockSnapshot`。面板 `Clock.DayIndex` 仍要等全局 scope（G3）才能用 `LoadSelfAttribute`；现在不要假装这些属性已经有实体。

## 4 场景

玩家开一局经营战。作者配 `ticksPerDay`，让一天对应一段玩法时间。日序走到春尽，`Calendar.CyclePhaseEntered` 带上 `summer`。生产规则订阅这个事件，把春耕减半关掉，夏补给恢复。UI 读主历投影，显示「第 1 年 · 夏 · 四月上旬 · 立夏」。

另一份历 `calendar.regnal` 挂在同一日序上。第 11 年换「开疆」纪年。玩家看到的年号变了，季节事件仍按 `calendar.solar360` 走。

暂停或 TimeFlow 暂停令牌让 `Step` 为 0：日子停，季节不切。

## 5 边界

- 不新建 TimeFlow domain，不把季节写成枚举塞进 Core。
- 不复活全局 `Turn` 钟来表示过了一年。
- 不在玩法 Mod 里再写一份 `if (day > 360)`。
- 闰年、阴阳合历、月长不齐：用相位表表达。本年不做隐式闰规则。
- `EntityLocalClock` 不驱动世界历。单体变速不影响日序。
- 没有 `clock.json` 时调用 `Project` 失败，不返回假日期。
- 未知字段、相位长度对不齐、主历不存在：装载失败并点名。
- `minutesPerDay` / 累计已过分钟不是世界钟字段。一天只按 `ticksPerDay` 翻页。

## 6 UAT

```gherkin
Feature: 世界日子按历法走

  Scenario: 过完春天就入夏
    Given 主历是一年 360 日、四季各 90 日
    And 当前是第 90 日、季节仍是春
    When 世界又走完 1 日
    Then 玩家看到季节变成夏
    And 节气变成立夏
    And 月份变成四月

  Scenario: 暂停时日子不动
    Given 世界历法已启用
    And 玩法步进被暂停
    When 画面又过了若干帧
    Then 日期不变
    And 没有季节切换事件

  Scenario: 同一天可以有另一套纪年
    Given 同一日序上还挂着一份纪年历
    And 日序走到该纪年的起始日
    Then 纪年历显示新纪年的第 1 年
    And 主历的四季算法不变

  Scenario: 没启用历法就不能读日期
    Given 没有 Calendar/clock.json
    When 有人要读今天是哪一年
    Then 系统失败并说明历法未启用

  Scenario: 一天只按一套进度走
    Given 一天是 20 步
    And 晓从进度 0 开始、昼从进度 250 开始
    When 世界走了 5 步
    Then 当天进度是 250
    And 玩家看到昼夜变成昼
    And 配置里不能再写一天多少分钟
```

## 7 深度材料

- 时钟层：`gitbook/architecture/time-system.md`
- 实现：`src/Core/Gameplay/Calendar/`
- 默认历法表：`assets/Calendar/calendars.json`
- 测试：`src/Tests/CalendarCoreTests/`
