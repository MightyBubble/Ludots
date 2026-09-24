# 历法与周期

本页定义 Ludots 的世界历法层。它不另开调度器：只消费现有 `Step`，把日序投影成年、纪年、季节、月、旬、节气，并在相位变化时发事件。

正式时钟层仍以 [时间体系](time-system.md) 为准。

## 1 概述

游戏世界需要「今天是哪一天、过了哪个节、该结算什么」。Pacemaker / TimeFlow / GAS Step 只回答「走了几步、有没有停」。日、年、季节是历法的业务读法，不进时钟层。

- 全世界共用一个日序（从 0 起的绝对天数）。
- 一份历法表把日序投影成可读日期。可以同时挂多份历，同一天可以有不同纪年。
- 四季、月、旬、二十四节气、节日都是周期：一组相位，长度加起来等于周期天数。
- 年计数只有两种写法：均匀年 `yearLengthDays`，或相位表年 `yearCycleId`（年长可变，阴阳历闰年靠这个表达）。二选一，都写或都不写装载失败。
- 没有 `Calendar/world.json` 时，历法不推进。有这份文件时，缺表、缺字段、相位对不齐一律启动失败。

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
| `Calendar/world.json` | 日序怎么走：多少步算一天、从哪天开始、用哪份历、昼夜相位 |
| `Calendar/calendars.json` | 历法表：年长、纪年、周期与相位 |
| `CalendarRuntime` | 日序、当天已走步、投影、存档 |
| `CalendarSystem` | 只在启用时挂进循环，读 `GasClockStepPolicy.LastConsumedSteps`。Paused / 暂停令牌为 0 时不走日。没 `world.json` 不挂这个系统 |
| 全局订阅表（#1123） | 日子事件从这里派发：`TriggerManager.FireGlobalEvent`，配 `HasGlobalEventSubscribers` 探针 |

推进源只允许 `Step`。`Turn`、`FixedFrame`、`EntityLocal` 都拒绝。

## 3 详情

### 3.1 日序推进

`assets/config_catalog.json` 已登记两条路径。`Calendar/calendars.json` 默认带三份历：`calendar.solar360`（太阳历 + 节气 + 节日）、`calendar.lunisolar.zhang19`（阴阳历）、`calendar.regnal`（多年号）。`Calendar/world.json` 允许空：没有这份文件，运行时 `IsEnabled=false`，不发事件、不改日序。

`Engine/clock.json`、`GAS/clock.json`、`Physics2D/clock.json` 只管步进。历法不另开一份 clock，也不往那些文件里写日。

`world.json` 字段（全部显式必填）：

| 字段 | 含义 |
|---|---|
| `tickSource` | 只接受 `Step` |
| `ticksPerDay` | 多少个 Step 算一天，≥ 1。一天有多长只认这个数 |
| `startDayIndex` | 开局日序，≥ 0 |
| `activeCalendarId` | 主历，必须在历法表里 |
| `dayPhases` | 昼夜相位，按当天进度千分比切。首项 `startPermille` 必须是 0，后面递增且 < 1000 |

当天进度 = `ticksIntoDay * 1000 / ticksPerDay`，读接口是 `CaptureProgressSnapshot().DayPermille`。晓、昼、暮、夜查这根轴。钟面（例如 12:34）是界面把千分比画成表，不要再写一套「一天多少分钟」。写了 `minutesPerDay` 装载失败。

Mod 要启用历法，写 `Calendar/world.json`，并保证 catalog 里有这条 DeepObject（核心 catalog 已登记且 `AllowEmpty: true`）。

### 3.2 历法表

每份历的年计数二选一：

- `yearLengthDays`：均匀年。年 = `dayIndex / yearLengthDays + 1`，年内第几天从 1 计。
- `yearCycleId`：相位表年，指向本历 `cycles` 里一个周期，相位即年，年长可变。绝对年 = 完整圈数 × 每圈年数 + 圈内第几年。阴阳历的闰年（354 日平年、384/385 日闰年）靠这个表达，月长 29/30 也是明文相位。

两种都写或都不写，装载失败点名。

其余字段：

- `eras`：纪年。第一项 `startDayIndex` 必须是 0，后面递增。当前纪年取「起始日 ≤ 今天」的最后一项。均匀年的纪年内年号自起点满整年进位；相位表年按跨过的年相位计数。
- `cycles`：周期。`lengthDays` 是重复周期。`phases[].lengthDays` 之和必须等于 `lengthDays`。空周期表合法：纯纪年历只读年号和年，不发周期事件。

同一日序可以投到多份历。主历只影响 `Calendar.DayAdvanced` 的 `calendarId`；周期进出对每份历各自发事件，载荷带 `Calendar.CalendarId`。

默认表三份历，全部明文：

- `calendar.solar360`：一年 360 日，四季各 90 日，十二月各 30 日，旬 10 日一转，二十四节气各 15 日，节日周期铺满一年（春节 5 日、元宵、端午、七夕、中秋、重阳、除夕各 1 日，平日相位填满其余）。
- `calendar.lunisolar.zhang19`：19 年一章，共 6940 日。235 个月相位（125 个大月、110 个小月），7 个闰年各带一个明文「闰六月」相位；年用 `yearCycleId` 指向 19 相位的年周期。
- `calendar.regnal`：多年号历。立国、开疆（第 11 年）、靖远（第 21 年）、中兴（第 31 年），不带周期。

### 3.3 事件

全局事件，不是地图域，从 #1123 全局订阅表派发（`TriggerManager.FireGlobalEvent`，地图挂的全局触发听得到）：

| 事件 | 何时 |
|---|---|
| `Calendar.DayAdvanced` | 日序 +1 |
| `Calendar.CyclePhaseExited` / `Entered` | 某份历的某个周期换相位 |
| `Calendar.EraChanged` | 纪年切换 |
| `Calendar.DayPhaseChanged` | 当天昼夜相位变了，日序可以不变 |

派发按订阅者来，不遍历广播：

- 推进前先问 `HasGlobalEventSubscribers`。某个事件没人订，就不派发它，连为它准备的投影重建和相位 diff 都跳过。
- 一个事件都没人订时，只推日序。
- 订阅空窗期跨过的相位切换不补发。事件是通知，不是历史；订阅者后到，投影静默追平，不重放空窗内的进出对。
- Mod 事件回调（`IModContext.OnEvent`）算订阅者，同样过探针。

一次 Advance 跨得多天时，按天逐日发事件，不跳相位。唯一例外是 `DayPhaseChanged`：昼夜相位按本次 Advance 的首尾比较，只发一次（或整日数倍跨天、首尾同相位时不发）——中间天的昼夜窗口不重放。

订阅指定相位或日期用 TriggerGraph 条目的 `filters.payload`（载荷键值相等过滤，值限符号字符串或整数）：

```json
{ "label": "on_spring_begin", "event": "Calendar.CyclePhaseEntered", "start": "spring_rules",
  "filters": { "payload": { "Calendar.CycleId": "season", "Calendar.PhaseId": "spring" } } }
```

- 春始 / 春末：订 `CyclePhaseEntered` / `Exited`，过滤 `CycleId=season` + `PhaseId=spring`。
- 某月起 / 某月止：同一个事件，过滤 `CycleId=month` + `PhaseId=month.04`。节气、节日、旬、阴阳历月相位同法。
- 指定日期：订 `DayAdvanced`，过滤 `Calendar.DayIndex = 360`（整数比较）。
- 多历并存时加一条 `Calendar.CalendarId` 过滤即可只听某份历；不过滤则每份历的相位都各发一次。

`varName` 专用过滤槽是 payload 过滤的糖（`payload: {"MapTrigger.VarName": ...}` 等价），新作者面优先用 payload。符号走项目统一的「配置期符号、运行期 int」：历法表装载时 calendar / cycle / phase / era / dayPhase 符号注册进 `ConfigKeyRegistry`，事件载荷里的 `CalendarId` / `CycleId` / `PhaseId` / `EraId` 全是 key id（int），`filters.payload` 里的字符串期望值在图编译时解析成同一个 id，派发期只做 int 比较。`filters.payload` 的键和值类型按事件 schema 校验（声明外的键、string 参数写数值、float/entity 参数都在编译期拒绝）。图内读相位 / 日历 / 日序用 `LoadEntryPayloadInt`（载荷键如 `Calendar.PhaseId`）；要显示符号名时用 `ConfigKeyRegistry.GetName` 反查。

### 3.4 存档

存档域 `calendar`：`enabled`、`dayIndex`、`ticksIntoDay`、`activeCalendarId`。定义不存，以配置为准。恢复时 enabled / 主历必须和当前配置一致，否则失败。恢复不补发事件。

图里读今天：Script、TriggerGraph、Query 用 `ReadCalendarEnabled`、`ReadCalendarDayIndex`、`ReadCalendarTicksIntoDay`、`ReadCalendarDayPermille`、`ReadCalendarDayPhase`、`ReadCalendarYear`、`ReadCalendarCyclePhase`、`ReadCalendarCycleDay`、`ReadCalendarCyclePhaseIndex`、`ReadCalendarDaysUntilPhase`。年份和周期可以点名哪一份历，留空就是当前主历；周期节点必须写周期 id。`ReadCalendarCyclePhase` 给出的是相位名字的编号，和事件载荷 `Calendar.PhaseId` 相同。`ReadCalendarCyclePhaseIndex` 给出的是该相位在这张周期表里的位置，从 0 起，和事件载荷 `Calendar.PhaseIndex` 相同。`solar360` 的季节表上春是 0、夏是 1、秋是 2、冬是 3。月份名仍是符号（`month.04`）。阴阳历会插入闰月相位，表上的第几格对不上四月。

`LoadConfigKey` 把作者写的符号解析成同一个编号，只认装载时已经登记过的名字；没登记就失败并点名这个词，不会新造一个编号。过滤槽 `filters.payload` 里的字符串仍按原来的方式登记。`IntToText` 不能把编号变回 `summer`。

`ReadCalendarDaysUntilPhase` 要写周期和相位名，历法可留空。不写 `day` 时，返回离该相位下一次开始还有几个整天；已经在这个相位里就是 0。写了 `day` 时，这个数是相位里的第几天（1 基）：还没进相位，就在进入后再数到这一天；已经在目标日，是 0；过了目标日，就数到下一次。这个周期的表上没有这个相位，或这一天比相位更长，都会失败并点名。

问「是不是第 N 日」用 `ReadCalendarDayIndex`、`ConstInt`、`CompareEqInt`。`Calendar.DayAdvanced` 的过滤 `Calendar.DayIndex = N` 只在跨过这一天时触发，图里的读节点整天都答得了。问「是不是端午」比较 `ReadCalendarCyclePhase`（周期 `festival`）和 `LoadConfigKey`（符号 `duanwu`）。端午在表上只有一天，人在这个相位里就是端午当天。春节有五天，人在相位里是节日期间，第一天再比较 `ReadCalendarCycleDay == 1`。问「是不是第 1 年四月初五」分三路比较：`ReadCalendarYear == 1`（从日序 0 起的绝对年，1 基）、月份相位等于 `LoadConfigKey` 的 `month.04`、`ReadCalendarCycleDay == 5`（当前月相位里的第几天，1 基）。纪年里的第几年、年内第几天仍只在投影里，图上没有对应读节点。

问「离第 360 日还有几天」用 `SubInt`：目标日序减去 `ReadCalendarDayIndex`。还没到是正数，就是今天是 0，已经过了是负数。得到的是整天。一天多少步在 `world.json`，图上读不到。

写日子：`ApplyCalendarStart` 在开局还没提交时落下日序和当天步数，不发事件；提交后再写成同一个值是空操作，写成别的值失败。`SetCalendarDayIndex` 拨到一个不早于今天的绝对日，中间每一天走和时钟同一条跨日路径。`SetCalendarTicksIntoDay` 改当天已走步，范围是 `[0, ticksPerDay)`，昼夜相位变了发 `Calendar.DayPhaseChanged`，不翻日。启用历法仍然只认 `Calendar/world.json`。没有 `Calendar.*` 实体属性，面板值图用这些读节点。日期不进 `Clock.*`。事件载荷里的相位、日历、日序仍用 `LoadEntryPayloadInt`。代码侧 `Project` / `CaptureProgressSnapshot` 还在。

标准连线：

```json
{ "op": "ReadCalendarDayIndex" }
{ "op": "ConstInt", "intValue": 360 }
{ "op": "CompareEqInt" }
```

```json
{ "op": "ReadCalendarCyclePhase", "cycle": "festival" }
{ "op": "LoadConfigKey", "symbol": "duanwu" }
{ "op": "CompareEqInt" }
```

```json
{ "op": "ReadCalendarYear" }
{ "op": "ReadCalendarCyclePhase", "cycle": "month" }
{ "op": "LoadConfigKey", "symbol": "month.04" }
{ "op": "ReadCalendarCycleDay", "cycle": "month" }
```

年份与 `ConstInt` 1 比较，月份相位与 `month.04` 的编号比较，周期内第几天与 `ConstInt` 5 比较。

```json
{ "op": "ConstInt", "intValue": 360 }
{ "op": "ReadCalendarDayIndex" }
{ "op": "SubInt" }
```

`SubInt` 的 a 接目标日序，b 接今天。

```json
{ "op": "ReadCalendarDaysUntilPhase", "cycle": "festival", "phase": "duanwu" }
```

```json
{ "op": "ReadCalendarDaysUntilPhase", "cycle": "month", "phase": "month.04", "day": 5 }
```

`day` 是四月里的第 5 天。不写 `day` 只问相位什么时候开始。

```json
{ "op": "ReadCalendarCyclePhaseIndex", "cycle": "season" }
```

相位序号只用来和 `Calendar.PhaseIndex` 对齐，不用来判断是不是某一月、某一个节日。

## 4 场景

玩家开一局经营战。作者配 `ticksPerDay`，让一天对应一段玩法时间。日序走到春尽，`Calendar.CyclePhaseEntered` 带上 `summer`。生产规则订阅这个事件，把春耕减半关掉，夏补给恢复。UI 读主历投影，显示「第 1 年 · 夏 · 四月上旬 · 立夏」。

日序走到端午相位。地图上订了同一事件的触发收到 `duanwu`，集市 Mod 开端午集市三天（相位表里端午是一天，开几天是玩法的事）。走到除夕，跨年结算订阅 `Calendar.CyclePhaseEntered` 的 `chuxi`。

阴阳历挂在同一日序上。第三年是闰年，385 日，带一个明文「闰六月」相位。走到闰六月，月相周期进 `month.031`，年周期仍是 `year.03`——闰不闰月对年计数没有特殊分支，全是相位表读数。

另一份历 `calendar.regnal` 挂在同一日序上。第 11 年换「开疆」纪年，第 21 年换「靖远」，第 31 年换「中兴」。玩家看到的年号变了，季节事件仍按 `calendar.solar360` 走。

整个会话没人订阅日子事件：日序照走，`Project` 读数照常，一天事件都不发。地图后加载补挂了订阅：从下一个相位边界开始听，之前的切换不补发。

暂停或 TimeFlow 暂停令牌让 `Step` 为 0：日子停，季节不切。

## 5 边界

- 不新建 TimeFlow domain，不把季节写成枚举塞进 Core。
- 不复活全局 `Turn` 钟来表示过了一年。
- 不在玩法 Mod 里再写一份 `if (day > 360)`。
- 闰年、阴阳合历、月长不齐、节日：全部用明文相位表表达（含 `yearCycleId` 相位表年）。本年不做隐式闰规则。
- 事件不遍历广播：没订阅者的事件不派发、不算投影。订阅空窗不补发。
- `EntityLocalClock` 不驱动世界历。单体变速不影响日序。
- 没有 `world.json` 时不挂 `CalendarSystem`，不每帧问开没开。调用 `Project` 失败，不返回假日期。图里只有 `ReadCalendarEnabled` 能读到未启用，其它读写同样失败。
- 图不能另造一份历。开局日序写在 `world.json`；`ApplyCalendarStart` 只在开局窗口里改这一份已经装上的历。
- 日序不能往回拨。开局窗口在跨日、订阅者看见昼夜相位变化、作者写过日序或当天步数、或存档恢复之后关闭。
- 未知字段、相位长度对不齐、主历不存在、年计数二选一写重或漏写：装载失败并点名。
- `minutesPerDay` / 累计已过分钟不是日序字段。一天只按 `ticksPerDay` 翻页。
- 时钟层（Engine / GAS / Physics2D / TimeFlow）不认识日、年、季节。日期属性走 `Calendar.*`。

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

  Scenario: 节日是铺满一年的周期相位
    Given 主历的节日周期把一年铺满
    And 日序走到端午相位
    When 日序跨过该相位边界
    Then 周期相位进入事件带上端午
    And 不在代码里写任何节日规则

  Scenario: 阴阳历的闰年是明文相位
    Given 一份 19 年章法的阴阳历挂在同一日序上
    And 章内第三年是闰年、带闰六月相位
    When 日序走到闰六月起始日
    Then 月周期进入闰六月相位
    And 年计数仍按年相位表读
    And 代码里没有闰月判断

  Scenario: 没人订阅就不发日子事件
    Given 世界历法已启用
    And 没有任何地图或 Mod 订阅日历事件
    When 世界又走完若干天
    Then 日序照常推进
    And 一个日历事件都不发

  Scenario: 订阅空窗不补发
    Given 世界历法已启用
    And 春夏边界跨过去时没人订阅
    When 有地图随后挂上季节订阅
    And 世界再走到下一个相位边界
    Then 新订阅从下一个边界开始听
    And 空窗内的进出事件不重放

  Scenario: 没启用历法就不能读日期
    Given 没有 Calendar/world.json
    When 有人要读今天是哪一年
    Then 系统失败并说明历法未启用

  Scenario: 从图里读出今天
    Given 世界历法已启用
    And 今天停在开局日序
    When 作者在图里读取日序、年份和当前季节
    Then 图给出和历法投影相同的日序
    And 年份是主历上的年份
    And 季节相位是当前季节

  Scenario: 问今天是不是端午
    Given 节日周期里端午只有一天
    And 今天正走在端午相位里
    When 作者把当前节日相位和已经登记的端午这个名字比较
    Then 两边相同
    And 相位在表里排第几格不能拿来当端午

  Scenario: 问今天是不是第 1 年四月初五
    Given 主历走到第 1 年四月的第 5 天
    When 作者分别比较年份、四月这个相位名字、以及这个月里的第几天
    Then 三样都对上
    And 四月在月表里的第几格不参与这次比较

  Scenario: 问离第 360 天还有几天
    Given 今天的日序早于 360
    When 作者用 360 减去今天的日序
    Then 得到还差的整天数
    And 已经过了这一天时得到负数
    And 就是今天时得到 0

  Scenario: 问离下一个端午还有几天
    Given 节日周期里有端午相位
    When 作者问离端午下一次开始还有几天
    Then 已经在端午里得到 0
    And 还没到得到整日数
    And 这个周期的表上没有这个相位时失败并点名周期和相位

  Scenario: 问离四月初五还有几天
    Given 月份周期里四月有初五
    When 今天还在三月，作者问四月的第 5 天还有几天
    Then 得到进入四月后再数到初五的整天数
    When 今天是四月初三
    Then 得到 2
    When 今天已经过了四月初五
    Then 得到离下一次四月初五的整天数
    And 这一天比该相位更长时失败并点名相位和天数

  Scenario: 开局日子还没落定时可以改开局
    Given 世界历法已启用
    And 开局日序还没有被提交
    When 作者把开局写成另一组日序和当天步数
    Then 今天变成这组开局
    And 不发出日子事件
    When 作者再写成另一组不同的值
    Then 写入失败并点名已经提交的日子和请求的日子

  Scenario: 日子只能往前拨
    Given 今天是第 90 日
    When 作者把日序拨到第 91 日
    Then 玩家看到进入第 91 日时的季节进出
    When 作者再把日序拨回第 90 日
    Then 写入失败
    And 今天仍停在第 91 日

  Scenario: 时钟答不出今天几号
    Given 引擎和玩法步进都在走
    And 没有启用世界历
    When 有人去时钟配置里问今天几号
    Then 问不到日期
    And 日子只在历法里

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
- 测试：`src/Tests/CalendarCoreTests/`、`src/Tests/GasTests/Graph/GraphCalendarOpsTests.cs`
