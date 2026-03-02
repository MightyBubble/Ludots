# GAS / Input / Order：Effect Preset 交互验收矩阵（WOW / DOTA / LOL）

本文给出可验收、可回归的测试用例矩阵，覆盖全部 `EffectPresetType`，并分别在三类交互模式下验证：

- WOW：`TargetFirst`
- DOTA：`AimCast`
- LOL：`SmartCast` / `SmartCastWithIndicator`

## 1. 验收目标

1. 每个 `EffectPresetType` 在 WOW / DOTA / LOL 都有独立技能验收用例。
2. 每个用例都验证两段链路：
   - 输入到命令链路：`InputOrderMappingSystem -> Order`
   - 效果执行链路：`Order/Ability -> EffectRequest -> GAS Runtime`
3. 所有用例可通过自动化回归重复执行。

## 2. 交互模式判定标准

- WOW (`TargetFirst`)：先有目标，再按技能键，立即下发订单。
- DOTA (`AimCast`)：按技能键进入瞄准态，左键确认后下发订单。
- LOL (`SmartCast`)：按技能键立即下发订单（优先鼠标悬停目标）。
- LOL (`SmartCastWithIndicator`)：按住技能键进入指示器态，抬键时下发订单。

## 3. Effect Preset 覆盖矩阵（33 条主用例）

| PresetType | WOW（TargetFirst）技能验收 | DOTA（AimCast）技能验收 | LOL（SmartCast/Indicator）技能验收 |
| --- | --- | --- | --- |
| InstantDamage | 单体点名伤害（目标实体） | 单体点名伤害（确认后生效） | 快捷施法单体伤害（悬停优先） |
| DoT | 单体流血/中毒（周期扣血） | 瞄准确认后附加 DoT | 快捷施法附加 DoT |
| Heal | 自疗/点疗（即时加血） | 瞄准确认后治疗 | 快捷施法治疗 |
| HoT | 目标持续回血 | 确认后挂 HoT | 快捷施法挂 HoT |
| Buff | 自身增益（护甲/移速） | 确认后增益生效 | 快捷施法增益 |
| ApplyForce2D | 方向推力（方向输入） | 瞄准确认后施加推力 | 快捷施法方向推力 |
| Search | 指定位置范围检索并分发 payload | 瞄准确认后范围检索 | 快捷施法范围检索 |
| PeriodicSearch | 区域周期检索（持续区） | 确认后创建持续区 | 快捷施法创建持续区 |
| LaunchProjectile | 指向性弹道发射 | 瞄准确认后发射 | 快捷施法发射弹道 |
| CreateUnit | 指定点召唤单位 | 确认后召唤单位 | 快捷施法召唤单位 |
| Displacement | 指向/方向位移（击退/拉拽/突进） | 确认后位移 | 快捷施法位移（含指示器模式） |

## 4. 自动化回归映射

### 4.1 交互矩阵自动化（44 条）

- 测试：`InputOrderPresetInteractionAcceptanceTests`
- 覆盖：
  - 11 个 preset
  - 4 种施法模式（WOW + DOTA + LOL 两种）
  - 共 44 条输入到订单回归

### 4.2 效果执行自动化（关键链路）

- `GasProductionFeatureReportTests`：生产级多 Mod 场景回归（含 Search/PeriodicSearch/Projectile/CreateUnit/DoT/HoT/Heal/Buff）。
- `ApplyForceEndToEndTests`：`ApplyForce2D` 到 Physics2D Sink 的端到端验证。
- `DisplacementPresetTests`：位移运行时与配置加载链路验证。
- `EffectTemplateLoaderTests`：`Displacement` 配置编译验证。

## 5. 可玩场景操作建议（手工冒烟）

推荐在 `MobaDemoMod` 入口图执行，当前已配置混合施法模式：

- `Q`：WOW 风格（`TargetFirst`）
- `W`：LOL 快捷施法（`SmartCast`）
- `E`：DOTA 风格（`AimCast`）
- `R`：LOL 指示器施法（`SmartCastWithIndicator`）

验收时优先观察：

1. 是否按模式触发（即时 / 进入瞄准 / 抬键释放）。
2. 目标来源是否正确（已选中实体 / 悬停实体 / 地面点）。
3. 订单参数是否正确写入（`slot`、`targetEntity`、`targetPosition`）。
