# GPU shared-pose 10K 启动夹具

此目录只引用独立工作树 `C:\001_AI\Ludots-gpu-10k-opt`。不会修改主工作树，也不会自动启动窗口。

```powershell
python .\prepare_launch.py prepare
python .\prepare_launch.py build-mods
# 主代理完成 App 集成构建后：
python .\prepare_launch.py verify
python .\prepare_launch.py start
```

`start` 使用 `LUDOTS_AGENT_BRIDGE_PORT=47931`，并把日志写入本目录的 `runs/<UTC>/`。执行前应确认 47931 仍为空；启动后用 `/health` 连续请求两次，确认 `ok=true` 且 `pumpCount` 增长。

启动图固定为六个 Mod：`LudotsCoreMod`、`CoreInputMod`、`SelectionInteractionMod`（数据 Mod，无 DLL）、`MassNavigationMod`、`CapabilityStandardMassNavigationLargeWorld10kMod`、`AgentBridgeMod`。

## 让 Animator 进入真实移动状态

场景初始生成 10,000 个单位，但初始单位没有 `HasUnitTarget`，Animator 会保持 idle。使用 Bridge 时：

1. `ludots.session.info`，确认地图为 `mass_navigation`。
2. `ludots.entities.query`，按需要取一个单位的 `entityId`；也可以直接对多个实体重复发单体订单。
3. `ludots.orders.issue`：

```json
{"entityId": 1234, "orderType": "massNavigationMove", "worldXCm": 2500, "worldYCm": 1800}
```

4. 用 `ludots.orders.inspect` 确认订单被接受，再用 `ludots.entities.query` 或截图观察单位移动。移动期间 `HasUnitTarget` 为真，`MassNavigationLocomotionAnimatorParamSystem` 会把速度参数推入 locomotion Animator。

批量验证时，对 10K 单位逐个发订单会改变实验负载；若只需证明动画路径，给一个可见单位下达一次 `massNavigationMove` 即可。

HUD、HealthDrift 持续效果、小地图和 GpuSkinned presenter 均由 10K showcase 配置自动启用；不要通过环境变量关闭它们。
