---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-06
维护人: X28技术团队
文档版本: v1.0
适用范围: 交互层 - 输入系统 - InputAction到Order映射
状态: 已实现
依赖文档:
  - docs/06_交互层/13_输入系统/00_总览.md
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/01_OrderBuffer_架构设计.md
---

# InputOrderMapping 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 InputAction 到 Order 的映射系统，包括：
- 配置驱动的映射定义
- 运行时映射处理
- 用户改键支持
- **Shift+ 修饰键支持**（控制 Order 提交模式）

边界：
- 本文档仅涉及 InputAction 到 Order 的映射
- 不涉及物理按键到 InputAction 的映射（已有 InputSystem 负责）
- 不涉及 Order 的执行（由 OrderBuffer/AbilityTaskSystem 负责）

非目标：
- 复杂的组合按键（如长按、连击）
- 手势识别

## 1.2 设计目标

1. **配置驱动**：所有映射通过 JSON 配置定义，无需硬编码
2. **可运行时修改**：支持游戏内选项改变映射
3. **支持用户改键**：用户可自定义 InputAction 到 Order 的映射
4. **Selection 集成**：部分映射需要先选择目标
5. **修饰键支持**：Shift+ 控制 Order 提交模式（Immediate/Queued）

## 1.3 设计思路

三层映射架构：
1. **物理输入层**：键盘/鼠标/手柄 → InputAction（已有）
2. **InputAction层**：InputAction → OrderTag（本文档）
3. **Order执行层**：OrderTag → OrderBuffer（已有）

本系统负责第 2 层，将 InputAction 触发转换为 Order 并提交。

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 |
|------|------|
| InputAction | 输入动作（如 SkillQ、Command） |
| InputOrderMapping | InputAction 到 Order 的映射配置 |
| Trigger | 触发条件（如 PressedThisFrame） |
| ArgsTemplate | Order 参数模板 |
| SelectionType | 需要的选择类型（如 Ground、Entity） |

## 2.2 功能导图

```
InputOrderMapping 功能
├── 配置定义
│   ├── actionId（绑定的 InputAction）
│   ├── trigger（触发条件）
│   ├── orderTagKey（目标 Order 类型）
│   ├── argsTemplate（参数模板）
│   └── selectionType（选择类型）
├── 运行时处理
│   ├── 检测 InputAction 触发
│   ├── 构造 Order 参数
│   └── 提交到 OrderBuffer
└── 用户改键
    ├── 修改映射配置
    └── 持久化偏好
```

## 2.3 架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                      物理输入层                                  │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │ 键盘          │    │ 鼠标          │    │ 手柄          │       │
│  └──────┬───────┘    └──────┬───────┘    └──────┬───────┘       │
│         │                   │                   │               │
│         └───────────────────┼───────────────────┘               │
│                             ▼                                   │
│                   ┌──────────────────┐                          │
│                   │ PlayerInputHandler│ (已有)                   │
│                   │ default_input.json│                         │
│                   └────────┬─────────┘                          │
└────────────────────────────┼────────────────────────────────────┘
                             │ InputAction (SkillQ, Command, ...)
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   InputAction → Order 映射层                     │
│                   ┌──────────────────┐                          │
│                   │InputOrderMapping │                          │
│                   │ System           │                          │
│                   │input_order_      │                          │
│                   │ mappings.json    │                          │
│                   └────────┬─────────┘                          │
└────────────────────────────┼────────────────────────────────────┘
                             │ Order
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Order 执行层                                   │
│                   ┌──────────────────┐                          │
│                   │ OrderBufferSystem│                          │
│                   │ order_types.json │                          │
│                   └────────┬─────────┘                          │
│                            ▼                                    │
│                   ┌──────────────────┐                          │
│                   │AbilityTaskSystem │                          │
│                   └──────────────────┘                          │
└─────────────────────────────────────────────────────────────────┘
```

## 2.4 关联依赖

| 依赖模块 | 依赖方式 | 说明 |
|----------|----------|------|
| PlayerInputHandler | 查询 | 检测 InputAction 触发状态 |
| OrderBufferSystem | 调用 | 提交 Order |
| SelectionSystem | 查询 | 获取选择目标 |
| OrderTypeRegistry | 查询 | 获取 Order 配置 |

# 3 业务设计

## 3.1 业务用例与边界

**核心用例**：

1. 玩家按 Q 键 → SkillQ InputAction 触发 → 提交 castAbility Order (slot=0)
2. 玩家右键点击地面 → Command InputAction 触发 → 提交 moveTo Order (目标位置)
3. 玩家在选项中修改 Q 键映射到 SkillW → 运行时更新映射

**边界**：
- 本系统只负责 InputAction 到 Order 的转换
- 物理按键到 InputAction 由 InputSystem 负责
- Order 执行由 OrderBufferSystem 负责

## 3.2 业务主流程

```
InputAction 触发
    │
    ▼
┌───────────────────┐
│ 查找映射配置       │
└─────────┬─────────┘
          │
    ┌─────┴─────┐
    │ 找到配置？  │
    └─────┬─────┘
      否  │  是
    ┌─────┴─────┐
    │           │
    ▼           ▼
  忽略     检查触发条件
    │           │
    │     ┌─────┴─────┐
    │     │ 触发条件   │
    │     │ 满足？     │
    │     └─────┬─────┘
    │       否  │  是
    │     ┌─────┴─────┐
    │     │           │
    │     ▼           ▼
    │   忽略     检查选择需求
    │     │           │
    │     │     ┌─────┴─────┐
    │     │     │ 需要选择？ │
    │     │     └─────┬─────┘
    │     │       否  │  是
    │     │     ┌─────┴─────┐
    │     │     │           │
    │     │     ▼           ▼
    │     │  构造Order   获取选择数据
    │     │     │           │
    │     │     │     ┌─────┴─────┐
    │     │     │     │ 有选择？   │
    │     │     │     └─────┬─────┘
    │     │     │       否  │  是
    │     │     │     ┌─────┴─────┐
    │     │     │     │           │
    │     │     │     ▼           ▼
    │     │     │   忽略      构造Order
    │     │     │               │
    │     └─────┴───────────────┘
    │                   │
    │                   ▼
    │           ┌──────────────┐
    │           │ 提交到       │
    │           │ OrderBuffer  │
    │           └──────────────┘
    │                   │
    └───────────────────┘
```

## 3.3 关键场景与异常分支

### 场景 1：技能键映射

配置：
```json
{
  "actionId": "SkillQ",
  "trigger": "PressedThisFrame",
  "orderTagKey": "castAbility",
  "argsTemplate": { "I0": 0 }
}
```

流程：
1. 玩家按 Q 键
2. InputSystem 检测到 SkillQ PressedThisFrame
3. InputOrderMappingSystem 查找映射
4. 构造 Order: { OrderTagId: 100, Args.I0: 0 }
5. 提交到 OrderBuffer

### 场景 2：移动指令（需要地面选择）

配置：
```json
{
  "actionId": "Command",
  "trigger": "PressedThisFrame",
  "orderTagKey": "moveTo",
  "argsTemplate": {},
  "requireSelection": true,
  "selectionType": "Ground"
}
```

流程：
1. 玩家右键点击地面
2. InputSystem 检测到 Command PressedThisFrame
3. InputOrderMappingSystem 查找映射
4. 检测到需要 Ground 选择
5. 获取当前光标位置作为目标
6. 构造 Order: { OrderTagId: 101, Args.Spatial: 目标位置 }
7. 提交到 OrderBuffer

### 场景 3：用户改键

1. 用户在选项中选择"将 Q 映射到 SkillW"
2. 调用 InputOrderMappingSystem.Remap("SkillQ", "castAbility", { "I0": 1 })
3. 更新内存中的映射
4. 持久化到用户偏好文件

# 4 数据模型

## 4.1 概念模型

```
InputOrderMappingConfig
  └── mappings[]
        ├── actionId: string
        ├── trigger: TriggerType
        ├── orderTagKey: string
        ├── argsTemplate: OrderArgsTemplate
        ├── requireSelection: bool
        └── selectionType: SelectionType
```

## 4.2 数据结构与不变量

### InputOrderMapping 配置

```csharp
public enum InputTriggerType
{
    PressedThisFrame,   // 按下瞬间
    ReleasedThisFrame,  // 释放瞬间
    Held,               // 持续按住
    DoubleTap           // 双击
}

public enum SelectionType
{
    None,      // 不需要选择
    Ground,    // 地面位置
    Entity,    // 单个实体
    Entities   // 多个实体
}

public enum ModifierSubmitBehavior
{
    IgnoreModifier,   // 忽略修饰键
    ShiftToQueue,     // Shift+ = Queued 模式
    AlwaysImmediate,  // 始终 Immediate
    AlwaysQueued      // 始终 Queued
}

public class InputOrderMapping
{
    public string ActionId { get; set; }
    public InputTriggerType Trigger { get; set; }
    public string OrderTagKey { get; set; }
    public OrderArgsTemplate ArgsTemplate { get; set; }
    public bool RequireSelection { get; set; }
    public SelectionType SelectionType { get; set; }
    public ModifierSubmitBehavior ModifierBehavior { get; set; } = ModifierSubmitBehavior.ShiftToQueue;
}

public class OrderArgsTemplate
{
    public int? I0 { get; set; }
    public int? I1 { get; set; }
    public int? I2 { get; set; }
    public int? I3 { get; set; }
    public float? F0 { get; set; }
    public float? F1 { get; set; }
    public float? F2 { get; set; }
    public float? F3 { get; set; }
}
```

### 配置文件格式 (input_order_mappings.json)

```json
{
  "mappings": [
    {
      "actionId": "SkillQ",
      "trigger": "PressedThisFrame",
      "orderTagKey": "castAbility",
      "argsTemplate": { "i0": 0 },
      "requireSelection": false,
      "modifierBehavior": "QueueOnModifier"
    },
    {
      "actionId": "SkillW",
      "trigger": "PressedThisFrame",
      "orderTagKey": "castAbility",
      "argsTemplate": { "i0": 1 },
      "requireSelection": false,
      "modifierBehavior": "QueueOnModifier"
    },
    {
      "actionId": "Command",
      "trigger": "PressedThisFrame",
      "orderTagKey": "moveTo",
      "argsTemplate": {},
      "requireSelection": true,
      "selectionType": "Ground",
      "modifierBehavior": "QueueOnModifier"
    },
    {
      "actionId": "Stop",
      "trigger": "PressedThisFrame",
      "orderTagKey": "stop",
      "argsTemplate": {},
      "requireSelection": false,
      "modifierBehavior": "IgnoreModifier"
    }
  ],
  "userOverrides": {
    "enabled": true,
    "persistPath": "user://input_preferences.json"
  }
}
```

**modifierBehavior 说明**：

| 值 | 行为 |
|----|------|
| `QueueOnModifier` | 普通按键 = Immediate，QueueModifier+按键 = Queued |
| `IgnoreModifier` | 始终 Immediate，忽略修饰键 |
| `AlwaysQueued` | 始终 Queued（用于队列专用命令） |
| `AlwaysImmediate` | 始终 Immediate（用于紧急命令） |

**平台映射**：
- PC: `QueueModifier` → Shift 键
- 主机: `QueueModifier` → L1/R1 或其他按键
- 配置在 `default_input.json` 中的绑定决定

## 4.3 生命周期/状态机

InputOrderMapping 无复杂状态机，为纯配置驱动的无状态转换。

# 5 落地方式

## 5.1 模块划分与职责

| 模块 | 文件路径 | 职责 |
|------|----------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | 映射配置结构 |
| InputOrderMappingConfig | `src/Core/Input/Orders/InputOrderMappingConfig.cs` | 配置加载 |
| InputOrderMappingSystem | `src/Core/Input/Orders/InputOrderMappingSystem.cs` | 运行时映射处理 |

## 5.2 关键接口与契约

### InputOrderMappingSystem

```csharp
public sealed class InputOrderMappingSystem
{
    /// <summary>
    /// 处理输入映射（每帧调用）
    /// </summary>
    public void Update(float dt);
    
    /// <summary>
    /// 修改映射（用于用户改键）
    /// </summary>
    public void Remap(string actionId, string orderTagKey, OrderArgsTemplate argsTemplate);
    
    /// <summary>
    /// 重置为默认映射
    /// </summary>
    public void ResetToDefault(string actionId);
    
    /// <summary>
    /// 保存用户偏好
    /// </summary>
    public void SaveUserPreferences();
    
    /// <summary>
    /// 加载用户偏好
    /// </summary>
    public void LoadUserPreferences();
}
```

## 5.3 运行时关键路径与预算点

| 路径 | 预算 | 说明 |
|------|------|------|
| 映射查找 | O(n) n=映射数量 | 每帧遍历所有映射 |
| Order 构造 | O(1) | 固定开销 |
| 持久化 | 异步 | 不阻塞主线程 |

# 6 与其他模块的职责切分

## 6.1 切分结论

| 职责 | 负责模块 |
|------|----------|
| 物理按键 → InputAction | InputSystem (PlayerInputHandler) |
| InputAction → Order | InputOrderMappingSystem |
| Order 缓冲与执行 | OrderBufferSystem |
| 地面/实体选择 | SelectionSystem |

## 6.2 为什么如此

- **解耦**：每层独立配置，独立修改
- **可测试**：各层可独立测试
- **灵活**：支持多种改键方案

## 6.3 影响范围

实现 InputOrderMapping 后，以下模块需要适配：
- MobaLocalOrderSourceSystem：删除硬编码，使用配置

# 7 当前代码现状

## 7.1 现状入口

- InputSystem: `src/Core/Input/`
- 硬编码示例: `src/Mods/MobaDemoMod/Presentation/MobaLocalOrderSourceSystem.cs`

## 7.2 差距清单

| 现状 | 目标 | 差距 |
|------|------|------|
| 硬编码 SkillQ/W/E/R | 配置驱动 | 需新增配置系统 |
| 无用户改键 | 支持改键 | 需新增改键 API |
| 无持久化 | 持久化偏好 | 需新增存储 |

## 7.3 迁移策略与风险

**迁移策略**：

1. 新增 InputOrderMappingSystem
2. 添加默认配置
3. 迁移 MobaDemoMod 使用新系统
4. 删除硬编码

**风险**：

| 风险 | 缓解措施 |
|------|----------|
| 配置错误 | 加载时校验，fail-fast |
| 性能影响 | 每帧只检查触发的 Action |

# 8 验收条款

1. **配置加载**：从 JSON 加载映射配置，无硬编码，可通过单元测试验证
   - 证据：`src/Core/Input/Tests/InputOrderMappingTests.cs`

2. **触发转换**：InputAction 触发时正确生成 Order，可通过单元测试验证
   - 证据：`src/Core/Input/Tests/InputOrderMappingTests.cs`

3. **用户改键**：调用 Remap 后映射生效，可通过单元测试验证
   - 证据：`src/Core/Input/Tests/InputOrderMappingTests.cs`

4. **MobaDemoMod 迁移**：删除 MobaLocalOrderSourceSystem 中的硬编码，使用配置驱动
   - 证据：MobaDemoMod 运行时行为不变

5. **持久化**：用户偏好可保存和加载
   - 证据：文件系统验证
