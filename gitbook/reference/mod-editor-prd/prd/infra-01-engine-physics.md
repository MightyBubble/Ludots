# infra-01 · 引擎与物理配置

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/infra-01-engine-physics.md)；编辑器需求见 [UXD](../uxd/infra-01-engine-physics.md)；引擎实现见 [runtime spec](../spec-runtime/infra-01-engine-physics.md)；editor spec 见 [editor spec](../spec-editor/infra-01-engine-physics.md)；现状见 [reference](../reference/infra-01-engine-physics.md)。

## 1. 定位

引擎与物理的四个底层旋钮：引擎固定时钟（仿真步频）、物理 2D 时钟（物理步频与宽相策略）、求解器（迭代与修正参数）、运动学容量（刚体与接触事件预算）。它们决定"世界每秒走几步、每步多稳、能装多少碰撞"。

## 2. 产品承诺

- **时钟分层**：引擎固定时钟与物理时钟独立声明；物理按固定步补步（MaxStepsPerFixedTick 封顶），快的物理不拖慢仿真。
- **求解可调**：迭代数、位置修正比、休眠阈值、碰撞对上限全部显式可配；引擎默认值只是缺省，不是推荐。
- **容量显式无注入**：运动学三字段（刚体容量、接触事件队列容量、发射层白名单）必须显式写——没有默认注入，写漏即失败。
- **越界即拒**：步频下限、迭代下限、修正比区间、容量下限都有启动校验；宽相策略是封闭枚举。
- **文档与实配一致**：代码缺省值不构成承诺；以实际资产与事实页为准（见异常承诺节与 D2）。

## 3. 运行行为

引擎固定时钟驱动 FixedDeltaTime 与 stepRateHz；物理时钟按 PhysicsHz 在固定步内补步，宽相按策略产对，求解器按参数收敛，接触事件仅对白名单层发射；容量不足运行期报错。

## 4. 异常承诺

FixedHz < 1、PhysicsHz/MaxStepsPerFixedTick/迭代/容量越界、修正比出 [0,1]、宽相 CellSizeCm < 1、运动学字段缺失或 < 1——启动失败并指明字段与文件。

**相关文档**：[配置说明](../config/infra-01-engine-physics.md) · [cfg-06](cfg-06-game-config.md) · [infra-02](infra-02-navigation.md)
