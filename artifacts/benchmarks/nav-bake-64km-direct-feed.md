# Nav 直灌双轨 A/B 实测(#1344 切片,同二进制对照)

对照对象:`terrainFeed=direct`(高度场列直灌)vs `terrainFeed=triangles`(每格三角化喂 Recast)。
两轮同二进制(同分支 Release 构建)、同资产、同进程时段,32 逻辑核,各 3 探区 × 2 格边 × 5 轮取中位。

## 单瓦片全管线(RecastNavTileBaker.TryBake)

| 探区 | 格边 | 瓦片边 | 三角轨(ms) | 直灌轨(ms) | 三角轨分配 | 直灌轨分配 |
|---|---|---|---|---|---|---|
| relief | 100cm | 64m | 397.5 | 280.7 | 179.8MB | 150.9MB |
| plains | 100cm | 64m | 326.6 | 327.4 | 136.0MB | 106.0MB |
| sea | 100cm | 64m | 297.9 | 363.9 | 128.4MB | 103.8MB |
| relief | 800cm | 512m | 24,271 | 31,019 | 9.14GB | 9.36GB |
| plains | 800cm | 512m | 28,771 | 17,837 | 10.13GB | 10.04GB |
| sea | 800cm | 512m | 18,893 | 19,636 | 5.42GB | 5.42GB |

## 结论

1. **耗时在噪声带内互有胜负**:1m 档平原两轨仅差 0.8ms;8m 档 relief 直灌反而慢 28%。
   上午跨会话测得的"863ms→327ms(2.6×)"是机器负载差异假象,已作废。
2. **瓶颈定罪:Recast 本体,不是喂入方式**。8m 档两轨都是 ~9GB 分配——那是
   5120²体素高度场(RcSpan 数组+池)自身的体积;watershed 分区(BuildRcConfig
   硬编码 WATERSHED)与 detail 网格在 26M 列上的开销统治一切。
3. **直灌的真实收益是架构性的**:消灭每格三角形汤(托管 List 分配)与
   ResolveAreaIdFromPolyMesh 的逐多边形线性扫描(area 随 span 原生流动);
   1m 档分配降约 20%。且直灌轨 area/水位/断崖语义与三角轨经 1200 点采样
   对拍一致(≥90%,见 NavTerrainFeedDualTrackTests)。
4. **性能主战场移交**:单瓦片成本要打下来,靠 #1347 档位化(分区策略
   MONOTONE 选项、瓦片边长×体素分辨率组合封顶)与 #1344 分片断点续烤
   (8m 档 42.5s/19.6s 的单瓦片本身就是分片粒度候选)。

## 复现

```
dotnet run -c Release --project src/Benchmarks/NavBake64Bench -- <east_asia_continuous.height> --repeats 5 --feed triangles
dotnet run -c Release --project src/Benchmarks/NavBake64Bench -- <east_asia_continuous.height> --repeats 5 --feed direct
```

(bench 的 --feed 支持在 #1367 基准工程上追加;两轨对照出自同一构建。)
