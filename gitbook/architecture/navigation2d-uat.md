# Navigation2D UAT

## Contract / Authoring

必须覆盖这些 fail-fast 场景：

- 缺 `NavProfileRef`
- 缺 `Team` / `TeamIdentity`
- 缺 `WorldPositionCm`
- `NavCrowdResolve` 缺 `NavCrowdProfileRef`
- 缺 board / pathing 环境
- 非法 profile / policy id
- `NavOnly` / `NavCrowdResolve` 单位手填 `Mass2D`

预期：加载或校验直接失败，不能进入“画面能跑但底层没接通”的半可用状态。

## Semantic RTS

- 同 team 多个 group 同时移动，目标互不覆盖
- Friendly 小体型穿 Friendly 大体型时，大体型 cooperative yield
- 小体型穿敌方 / 中立大体型时，对方基本不动
- 大体型穿敌方小体型时，对方被推开但不穿模
- goal pile-up 和障碍内 slot 不会无限抖动
- timeout / retry / abandon 会进入 diagnostics

## Physics Layering

- `NavOnly`
- `NavCrowdResolve`
- `FullPhysics2D`

三种模式都要有专项验证。

击飞专项还要验证：

- override 正确激活
- 结束后正确回落
- 不遗留额外 runtime / 性能拖累

## Diagnostics / UAT 产物

正式验收产物至少包含：

- `trace.jsonl`
- `battle-report.md`
- `path.mmd`
- 关键帧截图
- `diagnostics.txt`

调试面板必须明确区分：

- 热生效
- reset 生效

右上角 overlay 必须长期可见，至少能看到：

- FPS
- solver 分布
- rule summary
- nav / physics / presentation / frame ms
- alloc / heap

## Perf / Soak

至少跑：

- 5k
- 10k
- 20k

记录：

- `nav_ms`
- `physics_ms`
- `presentation_ms`
- `frame_ms`
- `frame_alloc_bytes`
- `heap_bytes`

长跑还要验证：

- reset 后 group/runtime 不泄漏
- timeout / retry 不异常累加
- steady-state 不出现持续 GC 抖动
