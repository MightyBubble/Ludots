# 行为树演武场

入侵者会穿过巡逻区。守卫由 `BehaviorTreeWorld` 读取 `bt.patrolChaseAttack`，叶子来自 ActionLib；绿色是巡逻，黄色是追击，红色是攻击。

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_behavior_tree_arena_raylib'
```

启动后直接使用左上控制台。可以暂停和单步，开关入侵者，调整视野与思考频率；点击 `L2 开` 会关闭行为树，场景保留巡逻作为同场对照。万人灰点只表示无图压测基线，`ScriptSlices=0`。

验收测试：`BehaviorTreeArenaShowcaseAcceptanceTests`、`GraphBehaviorSeparatedShowcaseAcceptanceTests`。
