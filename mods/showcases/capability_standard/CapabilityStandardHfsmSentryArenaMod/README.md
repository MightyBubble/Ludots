# HFSM 岗哨演武场

入侵者会穿过岗哨线。岗哨由 `HfsmWorld` 读取 `hfsm.sentry.scripted`，生命周期叶子由 `GraphProgramHfsmHost` 执行；青色是待命，黄色是警戒，红色是战斗，蓝色是撤退。

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_hfsm_sentry_arena_raylib'
```

启动后直接使用左上控制台。可以暂停和单步，开关入侵者，调整警戒半径与思考频率；点击 `L2 开` 会关闭状态机响应，方便看同场差别。万人灰点只表示无图压测基线，`LifecycleRuns=0`。

验收测试：`HfsmSentryArenaShowcaseAcceptanceTests`、`GraphBehaviorSeparatedShowcaseAcceptanceTests`。
