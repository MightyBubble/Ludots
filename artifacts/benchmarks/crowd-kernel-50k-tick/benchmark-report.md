# 50k 单位 tick 门(种子 1337,deploy.bases 分布,每座基地远点行军,200 tick 稳态窗)

| 指标 | 毫秒/tick |
|---|---|
| 平均 | 21.9199 |
| P50 | 21.1082 |
| P95 | 26.8464 |
| 最大 | 40.1213 |

- 单位 50000,按 deploy.bases 分布,预热 40 tick(计时窗不含生成与下令)
- Release,迷雾不冻结,计时是推进成功的 session.Step(运动、迷雾、认知同步、校验码)
- 迷雾刷新 60 次,并行工人 4
- 开测时 1 分钟 load 0.42,核数 4,等待 0 ms;load average 开测 `0.42 1.44 3.10 2/893 1634825`,测后 `0.47 1.44 3.09 1/890 1634846`
- 开测规则:1 分钟负载高于核数则每 5 秒再读,最多 180 秒,到期仍高也计时,不重跑
- 硬门(仅优化构建):P95 < 33 ms
- 子步 1
- 阶段平均(毫秒/tick): motor=2.656 intent=7.062 sep=5.650 hash=2.419 sum=1.813 gather=0.698 scatter=0.695 fog=0.140 belief=0.002 plan=0.004 leader=0.010 refresh=0.015 cmd=0.001 rebake=0.000 accounted=21.165 parWork=43.847 parWall=14.263 gWall=0.772 gWork=1.437 sWall=4.874 sWork=14.678 iWall=6.492 iWork=21.762 mWall=2.125 mWork=5.970
- P95 那一 tick 的阶段: motor=2.221 intent=10.263 sep=8.003 hash=2.342 sum=1.859 gather=0.678 scatter=0.707 fog=0.000 belief=0.001 plan=0.001 leader=0.010 refresh=0.014 cmd=0.000 rebake=0.000 accounted=26.100 parWork=43.189 parWall=19.472 gWall=0.955 gWork=1.622 sWall=7.045 sWork=14.629 iWall=9.776 iWork=21.095 mWall=1.695 mWork=5.843
- 最慢 tick 的阶段: motor=2.393 intent=11.082 sep=19.581 hash=2.531 sum=1.868 gather=0.695 scatter=0.716 fog=0.494 belief=0.004 plan=0.016 leader=0.014 refresh=0.018 cmd=0.001 rebake=0.000 accounted=39.412 parWork=65.638 parWall=32.042 gWall=0.715 gWork=1.428 sWall=18.859 sWork=32.619 iWall=10.604 iWork=25.723 mWall=1.865 mWork=5.868
- slowest 20: gc0 0/20 (window gc0 ticks 0/200) gc1 0 gc2 0 alloc 0 fog+ 9 plans+ 0 rebake+ 0 hashReuse 0; p95 excess vs p50 [parWall +5.606 intent +3.715 sep +1.933]; max excess vs p50 [parWall +18.176 sep +13.511 intent +4.533]; max tick gc0=0 gc1=0 gc2=0 alloc=0 fog=1 plans=0 rebake=0 hashReuse=0 wall=40.121
