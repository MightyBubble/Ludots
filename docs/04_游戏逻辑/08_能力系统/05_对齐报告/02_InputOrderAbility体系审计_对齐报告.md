---
文档类型: 对齐报告
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v1.0
适用范围: Input / Order / Ability 体系
状态: 已完成
---
# Input / Order / Ability 体系审计对齐报告

## 1 审计范围

- **Input 层**：`src/Core/Input/` 全部代码 + `assets/Configs/Input/`
- **Order 层**：`src/Core/Gameplay/GAS/Orders/` + `OrderDispatchSystem` + `OrderBufferSystem`
- **Ability 层**：`src/Core/Gameplay/GAS/` 核心系统
- **Targeting / Indicator**：`TargetSelector` / `GroundOverlayBuffer` / Performer 系统
- **集成点**：`MobaLocalOrderSourceSystem`、`GameEngine.cs` 系统注册

## 2 P0 严重问题（已修复）

### 2.1 Order Tag ID 六重真相

**问题**：castAbility=100, moveTo=101, attackTarget=102, stop=103 在 6 处独立定义（OrderStateTags.cs / OrderTypeConfigLoader.RegisterDefaults / game.json / order_types.json / GameEngine.cs / MobaLocalOrderSourceSystem.cs）。

**修复**：
- `OrderTypeConfigLoader.RegisterDefaults()` 改为引用 `OrderStateTags` 常量
- `GameEngine.cs` 移除 `GetValueOrDefault` 回退，缺失配置 fail-fast
- `MobaLocalOrderSourceSystem.cs` 移除硬编码回退分支
- SSOT 确定为：`OrderStateTags.cs`（C# const）+ `game.json`（运行时配置）

### 2.2 OrderDispatchSystem 静默丢弃

**问题**：未识别 OrderTagId 的 Order 在 while 循环中被静默丢弃，无日志、无回退。

**修复**：
- 添加默认路由到 `_commandOrders` 队列
- 添加 `DefaultRoutedCount` 诊断计数器
- DEBUG 模式下输出告警日志

### 2.3 respondChainOrderTagId = 0 BUG

**问题**：`GameEngine.cs:576` 传入字面量 `0`，导致默认 Order（OrderTagId=0）被错误路由到 chain orders 队列。

**修复**：
- 改为从 `gasOrderTags["respondChain"]` 读取，缺失时传 `-1`
- `OrderDispatchSystem` 增加 `_respondChainOrderTagId >= 0` 守卫

### 2.4 硬编码 60Hz

**问题**：`OrderSubmitter.CalculateExpireStep()` 假设 60 ticks/sec，实际引擎 FixedHz=30。

**修复**：
- 方法签名增加 `stepRateHz` 参数
- 调用链（Submit → HandleQueuedMode → HandleSameTypePolicy）全部传播 stepRateHz
- `OrderBufferSystem` 构造函数接受 `stepRateHz`，默认 30

## 3 代码增强（已实现）

### 3.1 Held → Start/End Order 映射

- 新增 `HeldPolicy` 枚举（`EveryFrame` / `StartEnd`）
- `StartEnd` 模式：PressedThisFrame 发射 `{OrderTagKey}.Start`，ReleasedThisFrame 发射 `{OrderTagKey}.End`
- 文件：`InputOrderMapping.cs`、`InputOrderMappingSystem.cs`

### 3.2 SmartCastWithIndicator 交互模式

- 新增 `InteractionModeType.SmartCastWithIndicator = 3`
- 按下显示指示器（进入 aiming），松开施放，右键/ESC 取消
- 通过 `AimingStateChangedHandler` 回调通知 Performer 显示/隐藏指示器

### 3.3 OrderSelectionType 增强

- `Ground` 重命名为 `Position`（保留 `[Obsolete]` 别名保证向后兼容）
- 新增 `Direction = 4`（方向向量输入）
- 新增 `Vector = 5`（两点向量输入，用于 Rumble R / Viktor E 类技能）

### 3.4 TargetShape 增强

- 新增 `Line = 4`、`Ring = 5`、`Rectangle = 6`

### 3.5 OrderTypeConfig 默认值修复

- `OrderStateTagId` 默认值从 `Active_CastAbility (100)` 改为 `0`（未设置），防止新 Order 类型静默继承 CastAbility 行为

## 4 死代码清理（已完成）

| 项目 | 文件 | 操作 |
|---|---|---|
| `SpatialTargeting` | `GAS/Components/SpatialTargeting.cs` | 已删除（空 struct，零引用） |
| `PromoteNextOrder` | `OrderBufferSystem.cs:133-143` | 已删除（被 Inline 版替代） |

## 5 待设计项（文档规划）

以下功能需要独立架构设计文档：

| 功能 | 文档路径 | 负责层 |
|---|---|---|
| Toggle Ability | `04_游戏逻辑/08_能力系统/01_技术设计/12_ToggleAbility_架构设计.md` | GAS |
| 输入缓冲/排队技能 | `04_游戏逻辑/08_能力系统/01_技术设计/13_输入缓冲与技能排队_架构设计.md` | GAS + Order |
| AbilitySlot 底层 | `04_游戏逻辑/08_能力系统/01_技术设计/14_AbilitySlot底层设计_架构设计.md` | GAS |
| 客户端 Order 验证器 | `06_交互层/13_输入系统/01_架构设计/04_OrderValidator_架构设计.md` | Input + Order |
| 高级选择系统 | `06_交互层/13_输入系统/01_架构设计/05_高级选择系统_架构设计.md` | Input |
| Auto SmartCast | `06_交互层/13_输入系统/01_架构设计/06_AutoSmartCast_架构设计.md` | Input |

## 6 交由其他系统的项

- **自动攻击 / 巡逻 / 驻守 / 攻击移动**：AI 系统（BB Key 已预留）
- **宏系统**：AI 系统
- **焦点/推荐目标列表**：AI 基建
- **Alt+自身施法**：Mod 层
- **阵型系统**：Mod 特定系统
- **战争迷雾**：独立系统（通过 IOrderValidator 接口对接）
- **集结点**：Entity BB 参数 + Build 技能配合
