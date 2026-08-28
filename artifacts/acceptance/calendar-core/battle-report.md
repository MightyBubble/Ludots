# Scenario Card: calendar-core

## Intent
- Goal: prove world day index projects season, month, xun, solar term, and era without a second scheduler.
- Gameplay domain: Core `CalendarRuntime` consuming Step ticks.

## Determinism Inputs
- Seed: none
- Calendar: `calendar.solar360` plus overlay `calendar.regnal`
- ticksPerDay: 1
- startDayIndex: 88

## Action Script
1. Read day 88: still spring.
2. Advance one day to 89: last day of spring / 谷雨.
3. Advance one day to 90: summer / 立夏 / 四月.

## Expected Outcomes
- Primary success condition: day 90 is summer, 立夏, 四月.
- Failure branch condition: season or solar term stay on spring values after day 90.

## Timeline
- `day88` -> day=88 立国 1年 春 三月下旬 谷雨
- `day89` -> day=89 立国 1年 春 三月下旬 谷雨
- `day90` -> day=90 立国 1年 夏 四月上旬 立夏

## Outcome
- success: yes
- verdict: Calendar projects business time from the existing Step clock.
