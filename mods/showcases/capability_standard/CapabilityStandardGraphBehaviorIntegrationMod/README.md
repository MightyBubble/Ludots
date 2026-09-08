# BT + HFSM 联合演武场

同一名入侵者穿过两道防线：左边行为树追击，右边分层状态机切换状态。这个入口适合第一次看 Ludots 图行为。

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_graph_behavior_integration_raylib'
```

启动后直接使用左上控制台。暂停后可单步比较两边的下一次决策，也可一起开关 L2、入侵者、感知半径和思考频率。左边读取 `AI/behavior_trees.json`，右边读取 `AI/hfsm.json`；控制台只操作这两套正式运行时。

验收测试：`GraphBehaviorIntegrationShowcaseAcceptanceTests`、`GraphBehaviorSeparatedShowcaseAcceptanceTests`。
