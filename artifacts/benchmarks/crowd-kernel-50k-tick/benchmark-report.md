# 50k 单位 tick 门(种子 1337,deploy.bases 分布,每座基地远点行军,200 tick 稳态窗)

| 指标 | 毫秒/tick |
|---|---|
| 平均 | 26.0873 |
| P50 | 25.3027 |
| P95 | 30.8819 |
| 最大 | 52.2998 |

- 单位 50000,按 deploy.bases 分布,预热 40 tick(计时窗不含生成与下令)
- Release,装迷雾,计时是推进成功的 session.Step(运动、迷雾、认知同步、校验码)
- 迷雾刷新 60 次,并行工人 4
- 开测时 1 分钟 load 3.77,核数 4,等待 60004 ms;load average 开测 `3.77 2.47 1.12 1/547 2262542`,测后 `3.79 2.50 1.14 1/548 2262582`
- 开测规则:1 分钟负载高于核数则每 5 秒再读,最多 180 秒,到期仍高也计时,不重跑
- 硬门(仅优化构建):P95 < 33 ms
- 子步 1
- 阶段平均(毫秒/tick): motor=2.646 intent=8.918 sep=7.081 hash=2.990 sum=1.766 gather=0.815 scatter=0.851 fog=0.151 belief=0.002 plan=0.001 leader=0.010 refresh=0.003 cmd=0.001 rebake=0.000 accounted=25.234 parWork=51.327 parWall=16.923 gWall=1.182 gWork=2.323 sWall=5.897 sWork=17.822 iWall=7.721 iWork=25.100 mWall=2.124 mWork=6.082
- P95 那一 tick 的阶段: motor=3.440 intent=12.029 sep=8.656 hash=2.912 sum=1.710 gather=0.716 scatter=0.673 fog=0.000 belief=0.001 plan=0.001 leader=0.010 refresh=0.002 cmd=0.000 rebake=0.000 accounted=30.150 parWork=57.058 parWall=22.424 gWall=1.069 gWork=2.300 sWall=7.585 sWork=20.844 iWall=10.850 iWork=27.981 mWall=2.920 mWork=5.933
- 最慢 tick 的阶段: motor=3.745 intent=15.438 sep=9.274 hash=5.226 sum=2.000 gather=9.123 scatter=1.814 fog=4.151 belief=0.008 plan=0.015 leader=0.151 refresh=0.033 cmd=0.024 rebake=0.012 accounted=51.013 parWork=86.807 parWall=26.175 gWall=2.691 gWork=5.271 sWall=6.572 sWork=21.250 iWall=13.706 iWork=50.931 mWall=3.206 mWork=9.355
- slowest 20: gc0 0/20 (window gc0 ticks 0/200) gc1 0 gc2 0 alloc 0 fog+ 9 plans+ 0 rebake+ 0 hashReuse 0; p95 excess vs p50 [parWall +6.597 intent +3.121 sep +2.053]; max excess vs p50 [parWall +10.347 gather +8.328 intent +6.530]; max tick gc0=0 gc1=0 gc2=0 alloc=0 fog=1 plans=0 rebake=0 hashReuse=0 wall=52.300
