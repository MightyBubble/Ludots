## What to build

把 #902 已预留的多座位模型做成生产路径：分屏布局产品化、per-seat 输入路由与 scheme 激活、UI per-seat owner。同模型扩展（PresentBinding.rect / ClientLocalSeat / LogicView 不新增平行概念），不另起 viewport 子系统。

合同依据：`gitbook/architecture/client-local-seat-and-logic-view.md` §3.3 / §7（P2.5 已收口 sole seat 激活链与存档 round-trip；本单只做「多座位」的生产化）。

## 详情

### 多 PresentBinding 呈现管线

- `PresentBindingPresentation` 撤销「恰好一个 ClientLocalSeat」的 P3 抛错：对每个 binding 各自 rebind projector / ray / culling，禁止合成「唯一全局可见集」
- 呈现剔除 / 视觉 LOD / 拾取按 binding 各自计算（#902 §3.3 预留项）
- 多绑定时 `ResolveAuthorityCamera` 的消费方改走 per-binding 枚举（`CopyPresentBindings`），不再抛错

### Per-seat 输入路由

- 输入后端快照按 seat 归属扇出：`AuthoritativeInput` 从全局单一快照改为 per-seat 通道（sole seat 行为不变）
- 每个声明的 `controlSchemeId` 各自激活（今日仅 sole seat 激活，多座位发布不激活——见合同 §3.3 控制方案激活链）
- `seat.ControlSchemeId` 热切换写回对应 seat

### UI per-seat owner

- 面板 intent 的 `playerSource: "seat"` 携带具体 seatId；HUD / 面板归属各 seat 的 PresentBinding rect（#1015 座位归因的引擎侧对口）
- 同一 UI 资产多座位各持一份状态，不抢同一套 HUD

### 分屏布局产品化

- 布局从数据声明（`HorizontalEqualSplit` / `VerticalEqualSplit` 已有纯函数底座），宿主 loop 按布局逐 binding 同步 metrics
- Raylib / Web 双宿主同步打通

## Acceptance criteria

- [ ] 双 seat 进图（各声明 controlSchemeId）到分屏呈现全链无 P3 抛错；每 binding 各自剔除 / 拾取
- [ ] 两路输入互不覆盖（WASD 只驱动自己的 possessed rep）
- [ ] 面板 / HUD 按 seat 归属，双座位各自可见各自状态
- [ ] 布局由数据声明可切换水平 / 垂直对半，无代码分支
- [ ] 同一物理设备绑定多 seat：输入扇出到所有绑定 seat，仅绑定时一次性 warning，不自动改映射、不拒绝运行（双人共驾类合法用例可运行）
- [ ] 共享面板：一份实例 + 多席受众；受众外 seat 的操作被拒并回流 reason；hotseat 轮换由数据声明驱动
- [ ] sole seat 生产路径零行为回归（P2.5 合同测试全绿）
- [ ] 合同页 §7 P3 勾选并回填证据

## UAT

```gherkin
功能: 同屏双人各玩各的
  场景: 双座位分屏进图
    假如 startupLocalSeats 声明 seat.0 与 seat.1 各绑不同 player 且各声明 controlSchemeId
    当 引擎加载地图
    那么 两个 PresentBinding 矩形不重叠且并集覆盖全屏
    并且 两个 scheme 各自激活
    并且 呈现剔除对每个 binding 各自计算

  场景: 两路输入互不覆盖
    假如 seat.0 与 seat.1 各有轴输入
    当 同一帧两路同时移动
    那么 各自 possessed rep 各自移动
    并且 不存在 last-writer-wins 覆盖

  场景: 面板归属座位
    假如 双座位各自打开命令面板
    那么 面板事件按 seatId 归因
    并且 互不抢占同一 HUD

  场景: 设备重复绑定只警示不仲裁
    假如 seat.0 与 seat.1 绑定同一块物理设备
    当 该设备产生输入
    那么 两个 seat 各自收到该输入并各自解释
    并且 绑定时已产生一次性 warning 点名设备与两个 seat
    并且 不发生自动改映射或拒绝运行

  场景: 共享面板一份实例多席受众
    假如 面板声明 audience 覆盖 seat.0 与 seat.1
    那么 任一 seat 可操作且面板状态为一份
    并且 受众外 seat 的操作被拒绝并回流 reason
```

## 边界

- 不做远端 / 串流 / 观战（用户已明确暂不做）
- 不改四分职责模型；旁观 / 换 client 仍是模型预留，不在本单
- 不重做 UI 系统，只做 per-seat owner 归属


## 补充（2026-08 术语治理定案，#902 §3.5 / #1117）：Device→Seat 的 P3 落地形态

三层分工：

- **启动配置**（StartupLocalSeatConfig / launcher）：只管"这个 App 开几个 Seat、每个 Seat 的 controlSchemeId"——**不放物理设备 id**（设备热插拔，启动配置放不住）
- **Seat 运行时**：设备→Seat 绑定归这里（绑定寿命 = seat 寿命）
- **Adapter**：设备枚举与热插拔（只有宿主知道手柄插没插），往上只暴露"设备出现/消失 + 稳定标识"

**预留唯一钩子**：给 `ClientLocalSeat` 加设备句柄集合字段（本轮只留所有权位置，P3 只填不改结构）。

**P3 收敛项**：`CoreServiceKeys.SyntheticInput` 当前是 App 级单例设备（禁则 2 已知违反点）——P3 时改为 Seat 持有，Adapter 不再把设备句柄放进 App 级服务容器；#1117 守卫先行冻结（豁免名单只减不增）。

**存档联动**：多座位存档形态受 #1118 裁决约束（本机 I/O 概念不进存档/网络载荷）。

## 补充（2026-08-28 设计评审定案）：输入扇出合同与共享面板三轴模型

### 输入扇出：设备 → 绑定集合，引擎不仲裁

与 Input Context 已定哲学同源（commandSystem.md：「操作互斥是作者的约定处理好的，编辑器只提供警示」），延伸到 seat×设备层。先分清两种重复：

- **同一 `controlSchemeId` 被多个 seat 声明**：合法且是主流形态——两支同型号手柄各用 `scheme.pad`，各绑各的物理设备，零冲突，不报警
- **同一物理设备（稳定设备 id）被多个 seat 绑定**：输入忠实扇出到所有绑定 seat，引擎不仲裁、不改映射、不拒绝运行。重复绑定存在合法用例（双人共驾一个单位、辅助代按、演出镜像操作），配错与否由作者负责

落地规则：

- per-seat 路由的分发规则定为「设备 → 绑定集合扇出」，禁止实现成「设备 → 唯一 seat」的隐含假设
- Warning 触发点：`ClientLocalSeatDeviceBinding.BindDevice` 发现设备稳定 id 已属于其他 seat 时，发一次性 warning（点名设备 id 与两个 seatId），不拦运行
- 按键级重叠检测（两个 scheme 在同一设备上映射相同物理键）不进运行时，属编辑器 / 静态检查层

### 面板归属三轴：owner / audience / surface

现状 `playerSource` 只有 `'seat'` 一个合法值，原「面板归属各 seat」是单值模型；分屏场景三轴恰好同值所以未暴露，共享 HUD 场景要求拆开：

| 轴 | 管什么 |
|---|---|
| owner | 面板变量的语义主体：seat / participant / team / world |
| audience | 受众席位集合（可看、可操作）：seatId 列表或 all-seats |
| surface | 长在哪块呈现面：PresentBinding rect，归现有体系，不新增概念 |

- 数据形态：面板模板 / intent 声明 `ownerKind` + `audienceSeats`；surface 继续由 PresentBinding 管
- 交互路由：面板事件由触发的 seat 输入通道归因，对 `audienceSeats` 做 admission；受众外 seat 操作共享面板 → 拒绝并回流 reason（面板宪法 #858 §4）
- 共享 = 一份面板实例 + 多席受众；禁止按 seat 复制实例（状态分叉，违反宪法合同二「数据只写一份」）
- hotseat 回合制由 audience 声明 + 图 op 切换覆盖，不新增机制
- 「能看」与「能操作」的再拆分留给 #1119 远端时代（旁观能看不能点），本单不建
- `playerSource` 枚举扩展与面板宪法线（PANEL-E）对齐，不在两处各写一套

### 开工前置

- **#1122 先修**（裁决：选项 1，运行时激活不落偏好存储）——多座位会把激活链从一次变 N 次，静默改写偏好存储的副作用随之放大 N 倍

## Blocked by

- ~~P2.5 收口 PR（sole seat `controlSchemeId` 激活链 + 存档 localSeats round-trip 测试）合入 main~~（PR #1059 已合入，解除）

