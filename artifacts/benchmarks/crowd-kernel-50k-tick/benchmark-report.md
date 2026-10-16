# 50k 单位 tick 门(种子 1337,deploy.bases 分布,每座基地远点行军,200 tick 稳态窗)

| 指标 | 毫秒/tick |
|---|---|
| 平均 | 21.2524 |
| P50 | 20.7179 |
| P95 | 24.4219 |
| 最大 | 29.4791 |

- 单位 50000,按 deploy.bases 分布,预热 40 tick(计时窗不含生成与下令)
- Release,迷雾不冻结,计时是推进成功的 session.Step(运动、迷雾、认知同步、校验码)
- 迷雾刷新 60 次,并行工人 4
- 开测时 1 分钟 load 2.52,核数 4,等待 0 ms;load average 开测 `2.52 2.75 3.66 1/750 2051241`,测后 `2.72 2.79 3.67 1/749 2051242`
- 开测规则:1 分钟负载高于核数则每 5 秒再读,最多 180 秒,到期仍高也计时,不重跑
- 硬门(仅优化构建):P95 < 33 ms
- 子步 1
- 阶段平均(毫秒/tick): motor=2.602 intent=6.987 sep=5.365 hash=2.354 sum=1.681 gather=0.634 scatter=0.702 fog=0.118 belief=0.001 plan=0.001 leader=0.009 refresh=0.002 cmd=0.001 rebake=0.000 accounted=20.457 parWork=43.689 parWall=13.890 gWall=0.770 gWork=1.430 sWall=4.592 sWork=14.623 iWall=6.430 iWork=21.812 mWall=2.098 mWork=5.824
- P95 那一 tick 的阶段: motor=3.584 intent=6.929 sep=7.210 hash=2.415 sum=1.673 gather=0.649 scatter=0.675 fog=0.474 belief=0.001 plan=0.001 leader=0.009 refresh=0.002 cmd=0.001 rebake=0.000 accounted=23.624 parWork=42.331 parWall=16.696 gWall=0.870 gWork=1.605 sWall=6.338 sWork=12.870 iWall=6.405 iWork=22.141 mWall=3.083 mWork=5.716
- 最慢 tick 的阶段: motor=8.389 intent=10.221 sep=4.830 hash=2.304 sum=1.706 gather=0.621 scatter=0.693 fog=0.000 belief=0.002 plan=0.001 leader=0.009 refresh=0.011 cmd=0.001 rebake=0.000 accounted=28.788 parWork=57.190 parWall=22.260 gWall=0.730 gWork=1.363 sWall=4.098 sWork=15.089 iWall=9.569 iWork=25.050 mWall=7.863 mWork=15.688
- slowest 20: gc0 0/20 (window gc0 ticks 0/200) gc1 0 gc2 0 alloc 0 fog+ 9 plans+ 0 rebake+ 0 hashReuse 0; p95 excess vs p50 [parWall +3.002 sep +2.612 intent +0.489]; max excess vs p50 [parWall +8.566 motor +4.776 intent +3.781]; max tick gc0=0 gc1=0 gc2=0 alloc=0 fog=0 plans=0 rebake=0 hashReuse=0 wall=29.479
