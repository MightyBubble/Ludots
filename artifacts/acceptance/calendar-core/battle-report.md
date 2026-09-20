# Scenario Card: calendar-core

## Intent
- Goal: prove one day index projects multiple explicit calendars (solar, lunisolar, regnal), festivals and solar terms as authored phase tables, and subscriber-gated event dispatch over the #1123 global table.
- Gameplay domain: Core `CalendarRuntime` consuming Step ticks.

## Determinism Inputs
- Seed: none
- Calendars: `calendar.solar360` (season/month/xun/solarTerm/festival), `calendar.lunisolar.zhang19` (235 explicit months, 19-year phase-tabled years, explicit leap month), `calendar.regnal` (four explicit eras)
- ticksPerDay: 1
- startDayIndex: 88 / 123 / 359 / 707 / 3599 / 89

## Action Script
1. Cross spring→summer on solar360 (day 89→90).
2. Cross Duanwu festival day 125 and Chuxi day 360.
3. Enter lunisolar leap year 3 (day 708) and its explicit leap month (day 885).
4. Cross the regnal era switch 立国→开疆 (day 3600).
5. Fire through TriggerManager global subscriptions; then advance with zero subscribers.

## Expected Outcomes
- Primary success condition: every projection matches the authored phase tables; map global subscriptions hear `Calendar.CyclePhaseEntered`; zero-subscriber advance fires nothing but still advances the day.
- Failure branch condition: any projection drifts off its table, or events dispatch without subscribers / reach nobody with subscribers present.

## Timeline
- `day88` -> day=88 立国 1年 春 三月下旬 谷雨
- `day89` -> day=89 立国 1年 春 三月下旬 谷雨
- `day90` -> day=90 立国 1年 夏 四月上旬 立夏
- `day123` -> day=123 立国 1年 夏 五月上旬 芒种
- `day124` -> day=124 立国 1年 夏 五月上旬 芒种
- `day125` -> day=125 立国 1年 夏 五月上旬 芒种
- `lunar707` -> day=707 立国 2年 — 十二月— —
- `lunar708` -> day=708 立国 3年 — 正月— —
- `lunar885` -> day=885 立国 3年 — 闰六月— —
- `regnal3599` -> day=3599 立国 10年 — —— —
- `regnal3600` -> day=3600 开疆 11年 — —— —

## Outcome
- success: yes (5/5 scenario groups)
- verdict: explicit phase tables drive multi-calendar dates; dispatch is subscriber-gated on the global table.
