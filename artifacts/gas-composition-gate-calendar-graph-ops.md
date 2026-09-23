## GAS Composition Gate — Self Review

- **Task / Issue**: 日历读写图节点。启用仍走 Calendar/world.json，图负责读当前投影、落定开局、往前拨日、改当天步数。
- **Date**: 2026-09-23
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 新能力是一组图节点，复用 CalendarRuntime、ConfigKeyRegistry 和现有图编译/补丁/执行管线。没有新的配置枚举，也没有第二套历法装载。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读日序、当天步数、千分比、昼夜相位、年份、周期相位、周期内第几天、历法是否启用 | 0 | GraphNodeOp 509–516，处理器转到 CalendarRuntime |
| 开局落定、往前拨日、改当天步数 | 0 | GraphNodeOp 517–519，写路径与时钟跨日、昼夜事件同一条 |
| 启用哪一份历、一天多少步、开局日序 | 3 | Calendar/world.json + calendars.json |
| 多历符号 | 1 | 图节点上的 calendar / cycle 字段，补丁期收成 ConfigKey id |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable 转发到 IGraphRuntimeApi，再进 CalendarRuntime
- Queues / Systems: 有引擎时走 GameEngine.CreateContext、TriggerManager.FireGlobalEvent、HasGlobalEventSubscribers，与 CalendarSystem 相同
- Resolvers / Registries: ConfigKeyRegistry、CalendarConfigLoader 已注册的符号
- Existing presets / graphs: 画廊沿用 script 驱动、FrontDoor 图分片和覆盖注册表

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ReadCalendarEnabled | 读历法开没开 | 没有 Calendar.* 实体属性，LoadSelfAttribute 读不到 |
| ReadCalendarDayIndex | 读当前日序 | 日序不在 Clock.*，也不在实体属性上 |
| ReadCalendarTicksIntoDay | 读当天已走步 | 同上 |
| ReadCalendarDayPermille | 读当天千分比 | 千分比是历法投影，不是时钟分钟 |
| ReadCalendarDayPhase | 读当前昼夜相位编号 | 相位编号在历法运行时 |
| ReadCalendarYear | 读指定历或主历的年份 | 年份是投影结果 |
| ReadCalendarCyclePhase | 读指定周期的当前相位编号 | 周期投影不能拆成已有读节点 |
| ReadCalendarCycleDay | 读当前相位里的第几天 | 同上 |
| ApplyCalendarStart | 开局窗口内落下日序和当天步数 | 现有 Advance 只会往前走并发事件，不能改开局 |
| SetCalendarDayIndex | 把日序拨到不早于今天的绝对日 | 组合多次 Advance 要调用方自己数步，还会漏掉逐日事件 |
| SetCalendarTicksIntoDay | 改当天已走步 | 时钟步进不能把当天步数写成作者给的绝对值 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。拨日中途失败不回滚已经走过的天；开局窗口在作者请求拨日时关闭。

### 6. Config SSOT

行为配置落在: graph / catalog（路径）: 启用与开局在 `Calendar/world.json`，历法表在 `Calendar/calendars.json`，图节点只读写已经装上的那一份。

是否新增 JSON schema: NO — 节点字段 calendar、cycle 走现有图文档。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
