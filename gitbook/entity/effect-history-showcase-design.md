# Effect History Showcase 设计

## 一句话与目标用户

让新用户看到：同一个延迟效果可以严格按当前身份、最后已知位置或明确失效结果执行，而且历史记录不会被实体销毁或 Id 复用改写。

## 主循环

### 能力形态与动态轴

这是一个“管线/基础设施”能力。动态轴是：目标身份、viewer 知识、效果延迟和实体生命周期发生变化时，效果解析结果与历史记录如何保持一致。

### 60 秒循环

1. 玩家从左侧控制台选择一个效果模板和目标策略。
2. 玩家点击场景中的实体，提交一个延迟效果。
3. 系统显示效果取得时的身份、知识 revision、目标值和 RootId。
4. 玩家按“隐藏目标”使 viewer 的知识降级，或按“移除目标”触发实体销毁。
5. 延迟效果到达执行 tick 后，场景和右侧记录显示：使用了 live 值、last-known 值，还是因 stale/missing value 明确失败。
6. 玩家点击“复用 Id”创建新实体，验证旧引用不会命中新实体。

惊喜时刻是：同一个数值 Id 被新实体复用时，旧效果仍然显示旧身份并拒绝对新实体执行；记录中的 Attribute/Tag 变化保持不变。

## 消融对照

主场景提供 A/B 两条同源效果链：

- A：显式 `LiveEntity` / authoritative live position 策略。
- B：显式 `LastKnownPosition` 策略。

两条链使用相同的 source、target、RootId、执行延迟和 Attribute/Tag 结果，只切换目标策略。第三个按钮触发 `StaleIdentity`，展示无 fallback 的失败分支。

## 解释层

左侧操作区显示：

- 当前策略：Live / LastKnown / Point / Cell；
- 取得 tick、执行 tick、Knowledge revision、RootId；
- 当前目标身份：Id、WorldId、Version、稳定展示身份；
- 解析结果：Resolved / LastKnown / MissingValue / Stale / CapacityRejected。

右侧历史区显示执行记录：EffectTemplate、source/target 身份、Attribute delta、Tag delta、结果和 tick。颜色含义固定为：绿色=执行，蓝色=知识快照，黄色=等待，红色=明确拒绝，灰色=历史事实。

图例只解释这些通用状态，不出现业务类名。

## 运行时旋钮

Showcase 首屏只显示两个预设，避免把基础设施演示做成控制面板：

- **保守历史**：LastKnown + 短 TTL + stale reject。
- **权威延迟**：Live + 长 TTL + source policy explicit。

展开“高级覆盖”后才显示以下参数：

| 旋钮 | 范围 | 用户问题 |
|---|---:|---|
| 目标策略 | Live / LastKnown / Point / Cell | 效果使用哪类目标值？ |
| 延迟 tick | 0–120 | 目标变化后效果何时执行？ |
| 知识 TTL | 1–120 tick | 旧知识保留多久？ |
| 目标生命周期 | Keep / Remove / RemoveAndReuse | 实体销毁和 Id 复用时会发生什么？ |
| Attribute delta | -100–100 | 执行记录会写入什么属性变化？ |
| Tag 变化 | Add / Remove / None | 执行记录会写入什么标签变化？ |

所有旋钮都在运行时修改当前 showcase session 的正式配置或操作状态；不要求改文件重启。
每次提交效果时，生效的预设和覆盖值必须写入 `EffectExecutionRecord`，保证回放和验收可复现；HUD 只显示记录中的实际值。

## 场景结构

### 主演示

同一张小型地图上有 source、target、一个空位置和一个可复用身份槽位。默认首屏显示操作提示：选择策略，点击目标，等待执行，再触发隐藏或移除。

### 子场景

1. KnowledgeSnapshot：显示 last-known 值和 TTL 衰减。
2. EffectTargetRef：显示 Live、Known、LastKnown、Point、Cell 五种目标引用。
3. Lifecycle：移除实体、捕获 EntitySnapshot、复用数值 Id。
4. EffectExecutionRecord：显示 RootId、Attribute/Tag 变化和明确失败结果。

### 首屏引导

“先选一种目标策略，再点场中实体。按 Hide 让知识过期，按 Remove 触发实体生命周期变化，最后看右侧记录说明效果为什么执行或拒绝。”

## 门户资产与同源方案

- 设计文档：本文件。
- UAT：`gitbook/entity/effect-history-showcase-uat.md`。
- 运行时配置：`mods/showcases/effect_history/EffectHistoryShowcaseMod/assets/`。
- Showcase 注册：`showcase.registry.json`。
- 验收资产：`artifacts/acceptance/effect-history/`。

截图必须来自真实运行的 Live/LastKnown/Stale 三个状态；不使用测试生成的静态图冒充实机证据。预览和 HUD 读取同一份 showcase 配置，不复制参数。

## 反向 API 审计

| 需要的能力 | 归属 | 本次交付 |
|---|---|---|
| 跨 tick 身份值与 stale 解析 | Core Entity lifecycle | 是 |
| 销毁前 EntitySnapshot 捕获 | Core lifecycle | 是 |
| KnowledgeSnapshot 实际值载荷 | Core Knowledge | 是 |
| EffectTargetRef 目标解析 | Core GAS/Effect | 是 |
| EffectExecutionRecord 有界记录 | Core GAS/Effect | 是 |
| 运行时控制台和状态 HUD | Showcase Mod | 是 |
| Live/LastKnown/Point/Cell 的地图编辑器 | Editor | 否，当前 showcase 不依赖 |
| 多客户端 per-viewer 复制 | Networking #709 | 否，记录为后续消费者 |

若本次交付的 Core API 不能支撑主循环、A/B 消融、故障分支或 HUD 解释层，则状态只能是阻塞，不能用测试专用回调绕过。

## 交付边界与完成判据

### Core 范围

- EntityRef、EntitySnapshot、KnowledgeSnapshot、EffectTargetRef、EffectExecutionRecord；
- 统一销毁捕获入口；
- 有界 SoA store、TTL、revision、容量错误；
- 与 `EffectContext.RootId`、现有 GAS 事务、GameplayEvent、Attribute/Tag changed pipeline 接合；
- 明确同 tick 销毁顺序：效果解析先于销毁捕获时使用 live；销毁捕获完成后只允许 snapshot/stale 结果；
- Stale、MissingValue、CapacityRejected 路径必须使用预分配结构和 caller-owned buffer，不能在失败分支分配对象；
- 不新增 Combat、Damage、Kill、Missile、BattleReport Core 类型。

### Showcase 范围

- 可启动 Mod、地图、配置、launcher/preset 和注册表；
- Live / LastKnown / Point / Cell / Stale 五种真实交互路径；
- 四个以上运行时旋钮；
- 首屏操作指引、状态 HUD、历史记录、错误可读；
- headless UAT、path.mmd、trace.jsonl、battle-report.md；
- Agent Bridge 真实运行验收和截图。

### 完成状态

只有以下条件全部满足才可称为“可玩交付完成”：

- 干净入口可以启动该 Mod；
- 新玩家能在首屏理解并触发主循环；
- HUD 和记录来自正式 Core 管线；
- A/B 消融和 stale 失败路径可操作；
- Core/Mod 测试、文档治理和 `git diff --check` 通过；
- Agent Bridge `/health` 两次 pumpCount 增长，并取得目标 Mod/地图的交互证据。
