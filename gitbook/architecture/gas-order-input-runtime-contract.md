# GAS、订单与输入运行时合同

本页是 GAS、Graph、Order 和 Input 运行时行为的正式合同。它描述可观察行为、所有权和边界，不把某个类名、缓冲区代际或调用顺序当成玩法需求。实现与测试必须服从本页；PR #660 的历史 Issue 只保留证据，不再定义第二套完成状态。

## 第一性目标

酸黄瓜语言：Mod 作者把“我想让谁，在什么条件下，做什么”声明成能力、效果、订单和输入映射。引擎必须把这句话一次、完整、确定地执行；条件不满足就明确拒绝，不能吞掉、补一半或偷偷换一条路径。

### Mod 开发者视角

- 声明能力、效果、目标规则、订单和输入映射即可，不手工安装 Core 运行时组件，不创建私有队列，不读取表现层位置。
- 组合能力通过现有 graph/op/effect/订单管线表达；不新增业务 enum、preset 开关或平行运行时。
- 提交返回类型化结果；容量、缺失状态、非法目标和授权失败都带稳定错误原因。
- 资源有唯一 owner。创建、转移、拒绝、取消、失败、恢复和销毁都能找到对应的释放或回滚点。

### 玩家视角

- 技能、移动和组合命令要么按合同执行，要么立即显示可诊断的失败；不会出现“看起来成功但数值没变”“第一次操作特别卡”“旧命令偷偷继续”等状态。
- 同一角色的输入互不污染；一个角色没有权限或资源不足，不会改变其他角色的订单。
- 取消、打断和失败会停止后续动作并释放内部资源；完成才会继续续接订单。
- Graph 坐标、目标和容量是确定的。相同世界状态和输入必须得到相同结果。

## 七个 Feature

1. **技能效果原子执行**：属性、标签、子效果、事件和表现作为一个提交边界；任意一步失败，全部回滚，非法目标不能伪装成零变化。
2. **订单唯一生命周期**：接单立即返回类型化结果；已接受订单只发布一次 `Completed`、`Failed` 或 `Cancelled` 终态。
3. **组合命令与所有权闭合**：move-then-cast 在完成后续接；拒绝、取消、失败、异常、恢复和销毁会恰好转移或释放 continuation 与路径 payload。
4. **输入按角色隔离并鉴权**：每次激活绑定一个 actor，授权快照在提交边界内保持一致，结果可在同一调用链重入查询。
5. **技能槽位组合确定**：有效槽位唯一按 `granted > item > form > base` 解析，UI 预览与实际释放使用同一解析结果。
6. **Graph 使用确定玩法数据**：权威世界厘米坐标、固定容量和明确缺失服务；截断、过量和不可解析输入显式失败。
7. **大规模运行有界**：每帧预算耗尽可以继续，禁止动态扩容、静默丢弃和热路径实体结构迁移；稳定热路径保持 0GC。

## 不变量

- Core 不读取表现层 `VisualTransform` 作为玩法真相。
- 选中与收令只读模拟真相：`WorldPositionCm`、`CommandSourceSelectableTag`、以及已有的 `KnowledgeProjectionStore` live inspect 通道；不读相机 `CullState`。
- 热路径不调用 `World.Add`/`World.Remove`，不隐式扩容，不在查询中改变 archetype。
- 配置、Registry、容量、Graph 服务和运行时状态装配各自只有一个 owner 和一个 SSOT。
- 轻量 Ability Tag Grant 只由 Ability 执行域写入；有来源、叠层、刷新、驱散或条件结束的状态由 Effect 管理。
- 任何失败事务都恢复权威状态、外部队列和资源句柄；诊断只记录一次最终失败。

## 按键按下瞬间 / 松手瞬间

视觉帧与逻辑拍不是 1:1。pacemaker 在一个视觉帧内可以跳过逻辑拍，也可以连续补多个；真机跳帧时，视觉帧上的按下瞬间 / 松手瞬间约四成丢失（issue #1335 的面板注入实测，headless 1:1 测试看不到）。读法按消费方的节奏分两套：

- **逻辑拍上的按下瞬间 / 松手瞬间，是逻辑拍消费方的唯一权威读法。** 读 `FrozenInputActionReader.PressedThisTick / ReleasedThisTick`。`AuthoritativeInputAccumulator` 把两次冻结之间所有视觉帧的按下 / 松手 OR 进快照，跳帧瞬间的按下与抬起不丢。读取入口：
  - 全局：`CoreServiceKeys.AuthoritativeInput`（`AuthoritativeInputSnapshotSystem` 在每个逻辑拍的 InputCollection 开头冻结）。
  - per-seat：`ClientLocalSeatInputChannel.Reader`，与全局快照同一逻辑拍冻结。
  - 指针三键（confirm/command/cancel）：`AuthoritativePointerButtonSnapshot` 家族上的 `PressedThisFrame` 属性。它的 accumulator 同样按逻辑拍折叠，语义就是逻辑拍上的按下 / 松手，属性名沿用历史拼写。
  - 回放：`AuthoritativeAction` 持久化的 Pressed/Released 即逻辑拍上的按下 / 松手；回放隔离时冻结快照整拍替换，消费方无感。
- **视觉帧上的按下瞬间 / 松手瞬间只属于视觉帧消费方。** `PlayerInputHandler.PressedThisFrame / ReleasedThisFrame` 只覆盖当前视觉帧。适用层：宿主循环（adapter 的帧回调）、presentation 系统（`RegisterPresentationSystem` 注册的 ISystem）、`IInputFrameConsumer`。固定步 SystemGroup 里注册的系统禁止读它——这些系统在逻辑拍节奏里跑，读视觉帧瞬间必丢。
- `IInputActionReader` 是两种节奏共用的多态读口：同一次 `PressedThisFrame` 调用，落在 live handler 上是视觉帧瞬间，落在冻结快照上是逻辑拍瞬间（快照每逻辑拍冻结一次，看不到视觉帧）。多态管线（订单映射等）持接口读冻结快照，拿到的就是逻辑拍瞬间；能拿到具体类型的新代码优先写 `PressedThisTick`。

跳帧回归：`src/Tests/GasTests/InteractionInput/InputEdgeSemanticsTests.cs`（accumulator 全局链、per-seat 通道、双跳帧按下 / 松手合并）与 `SoundShowcaseAcceptanceTests` 的热键跳帧用例（真 pacemaker 三视觉帧一逻辑拍，帧读旧法可复现丢失）。

## 测试归属

测试按行为边界归档，不按历史 Issue 编号命名：

```text
src/Tests/GasTests/Features/
  EffectExecution/
  GraphRuntime/
  InputRouting/
  OrderLifecycle/
  RuntimeBudget/
  TagState/
src/Tests/GasTests/Integration/ProductionWiring/
src/Tests/ArchitectureTests/Runtime/
```

关键证据：

- `Features/EffectExecution/`：Effect 叠层回滚、标签贡献、监听缓存、fan-out 预算。
- `Features/GraphRuntime/`：固定容量、确定路径和稳态分配。
- `Features/InputRouting/`：actor 授权、映射、槽位解析和类型化接单结果。
- `Features/OrderLifecycle/`：订单代际、continuation 和路径 payload 所有权。
- `Features/RuntimeBudget/`：Ability/Effect/Lifetime 的共享预算与恢复。
- `Features/TagState/`：能力状态在安全装配阶段准备，目标接收器与 owner 分离。
- `Integration/ProductionWiring/`：GameEngine 组合根注入生产 Graph、诊断和容量服务。
- `ArchitectureTests/Runtime/`：blittable、组件尺寸和显式服务构造器等无法由单一玩法场景保证的边界。

测试必须优先验证真实运行行为和类型/程序集边界。不得用 `ReadAllText + Contains` 复述源码实现；需要静态门禁时使用编译后的 API、Roslyn/analyzer 或最小稳定的目录/所有权检查。

## 完成门禁

- 七个 Feature 都有 Mod 作者与玩家可观察的验收场景。
- Effect、Order、Input、Graph 和预算边界覆盖成功、拒绝、取消、失败、异常、恢复和销毁。
- 固定容量失败发生在权威状态改变之前；没有静默丢弃、fallback 或兼容旁路。
- 聚焦测试、生产构建、架构测试和 `git diff --check` 通过；新增失败必须归因并处理。
- 独立只读审计无未解决 P1/P2；未完成项只登记在唯一 SSOT Issue #689。
