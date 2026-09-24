## GAS Composition Gate — Self Review

- **Task / Issue**: 补齐问日子要的四个图节点。相位名字对编号、相位在表里的第几格、离某个相位开始还有几天、整数相减。
- **Date**: 2026-09-24
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 新能力是四个图节点，复用 CalendarRuntime、CalendarProjection 的相位表行走、ConfigKeyRegistry.GetId、以及已有的整数比较和加法。没有新的历法枚举，也没有第二套装载。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 把已经登记的符号写成编号 | 0 | GraphNodeOp LoadConfigKey，运行时 GetId，未登记则失败并点名 |
| 读周期相位在表里的位置 | 0 | GraphNodeOp ReadCalendarCyclePhaseIndex，与 Calendar.PhaseIndex 同一个 0 基序号 |
| 读离某相位下一次开始、或相位里第几天还有几天 | 0 | GraphNodeOp ReadCalendarDaysUntilPhase。不写 day 时已经在相位里是 0；写了 day 则覆盖进入前、目标日、目标日之后 |
| 整数相减 | 0 | GraphNodeOp SubInt，和 AddInt 同一类端口与图种 |
| 是不是某一天、是不是某年某月某日 | 2 | 上述节点与 CompareEqInt、AddInt、ConstInt 连线 |
| 启用哪一份历、相位表 | 3 | Calendar/world.json + calendars.json |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable 转发到 IGraphRuntimeApi / ConfigKeyRegistry
- Queues / Systems: 不改 CalendarSystem，不改时钟
- Resolvers / Registries: ConfigKeyRegistry.GetId、CalendarDefinitionRegistry、现有周期补丁
- Existing presets / graphs: CompareEqInt、AddInt、ReadCalendarDayIndex、ReadCalendarYear、ReadCalendarCyclePhase、ReadCalendarCycleDay

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadConfigKey | 把已登记符号解析成编号 | 图内没有 GetId。过滤槽的字符串转换会 Register，拼错会得到一个永远对不上的新编号 |
| ReadCalendarCyclePhaseIndex | 读当前相位在该周期相位表里的位置 | ReadCalendarCyclePhase 给出的是相位名字的编号，和 Calendar.PhaseIndex 不是同一个数 |
| ReadCalendarDaysUntilPhase | 读离指定相位下一次开始的整日数 | 相位起点不在图里，作者不能自己把 lengthDays 加出来 |
| SubInt | 两个整数相减 | 现有整数运算只有加和两种比较，没有减 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。四个节点只读或只做寄存器运算。

### 6. Config SSOT

行为配置落在: graph（路径）: 节点字段 symbol、calendar、cycle、phase 走现有图文档。历法表仍是 Calendar/calendars.json。

是否新增 JSON schema: NO — 不新增历法配置文件，不新增日期枚举。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
