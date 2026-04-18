# Performer 参数黑板与 Animator 统一

本文定义 Performer 参数黑板的多类型设计，以及如何将现有 Animator 参数系统统一到同一个黑板中。

## 1 动机

当前存在两个独立的参数系统：

- `PerformerInstanceBuffer` 的 8 个 float override — 容量不足，只有 float 类型
- `AnimatorParameterBuffer` — per-entity 独立组件，包含 64 个 bool 位 + float/int 参数

两者互不相通。Mod 开发者无法通过 performer command 直接控制动画参数，必须走额外的桥接层。

## 2 设计：分 lane 多类型黑板

```csharp
public sealed class PerformerParamBlackboard
{
    // ── Float lane（大多数参数：scale、speed、ratio、角度）──
    private int[] _floatKeys;
    private float[] _floatValues;

    // ── Int lane（asset ID swap、material ID、动画状态索引、bool 用 0/1）──
    private int[] _intKeys;
    private int[] _intValues;

    // ── Vector lane（color RGBA、offset、position）──
    private int[] _vecKeys;
    private Vector4[] _vecValues;

    // ── Per-handle 段索引（三条 lane 共用 handle→offset 映射）──
    private int[] _floatOffsets, _floatCounts;
    private int[] _intOffsets, _intCounts;
    private int[] _vecOffsets, _vecCounts;

    // ── 写入 ──
    public void SetFloat(int handle, int paramKey, float value);
    public void SetInt(int handle, int paramKey, int value);
    public void SetBool(int handle, int paramKey, bool value); // → int lane, 0/1
    public void SetVector(int handle, int paramKey, Vector4 value);

    // ── 读取（带父→子继承链）──
    // parent chain 存储在 blackboard 内部的 _parentHandles[] 中
    // 通过 SetParent(handle, parentHandle) 设置
    public float ResolveFloat(int handle, int paramKey, float defaultValue)
    {
        int current = handle;
        while (current >= 0)
        {
            if (TryGetFloat(current, paramKey, out float v)) return v;
            current = _parentHandles[current];
        }
        return defaultValue;
    }

    public int ResolveInt(int handle, int paramKey, int defaultValue);
    public Vector4 ResolveVector(int handle, int paramKey, Vector4 defaultValue);

    // ── Parent chain 管理 ──
    public void SetParent(int handle, int parentHandle);
    public int GetParent(int handle);
}
```

### 2.1 Lane 选择规则

| Lane | 用途 | 示例 |
|------|------|------|
| Float | 连续值 | scale、speed、ratio、角度、blend weight、音量 |
| Int | 离散值 + bool | asset ID swap、material ID、动画状态索引、开关(0/1)、阈值输出 |
| Vector | 多分量值 | color RGBA、position offset、direction |

bool 不单独开 lane，用 Int lane 的 0/1 表示。

### 2.2 ParamDefault 多类型

```csharp
public struct ParamDefault
{
    public int ParamKey;
    public ParamLane Lane;    // Float / Int / Vector
    public float FloatValue;
    public int IntValue;
    public Vector4 VectorValue;
}

public enum ParamLane : byte
{
    Float = 0,
    Int = 1,
    Vector = 2,
}
```

### 2.3 父→子继承链

子 performer 查找参数时，沿 `ParentHandle` 链向上查找直到找到值或返回默认值。这使得 root performer 可以设置 region param，所有子 performer 自动继承。

继承规则：
- 子 performer 自身的 `SetParam` 优先于父的值
- 继承链不限深度
- 三条 lane 独立继承

## 3 Animator 参数统一

### 3.1 现有架构

```
AnimatorParameterBuffer (per-entity ECS 组件)
    ↓
AnimatorRuntimeSystem (读取参数，驱动状态转移)
    ↓
AnimatorRuntimeState (运行时状态)
    ↓
AnimatorPackedState (128 位紧凑格式，送往 adapter)
```

### 3.2 新架构

```
PerformerParamBlackboard (per-performer，多类型)
    ↓
PerformerBehaviorSystem → Animator behavior (读 blackboard 参数)
    ↓
AnimatorRuntimeSystem (不变，但输入源改为 blackboard)
    ↓
AnimatorRuntimeState (不变)
    ↓
AnimatorPackedState (不变，送往 adapter)
```

### 3.3 Animator 参数映射

| Animator 参数类型 | Blackboard Lane | 说明 |
|------------------|-----------------|------|
| Float (speed, blend weight) | Float lane | 直接映射 |
| Int (state index) | Int lane | 直接映射 |
| Bool (trigger, flag) | Int lane (0/1) | `SetBool(key, true)` → `SetInt(key, 1)` |

### 3.4 AnimatorConfig 中的 param key 约定

```csharp
public struct AnimatorConfig
{
    public int AnimatorControllerId;
    public int AnimationProfileId;
    public int SpeedParamKey;            // blackboard float lane
    public int StateParamKey;            // blackboard int lane（状态索引输出）
    // Animator 的所有参数都通过 param key 映射到 blackboard
    // 不再有独立的 AnimatorParameterBuffer
}
```

`AnimatorFeedbackBuffer` 产生的反馈同样回写到 performer blackboard，避免第二套事件真相。约定如下：

- `StateParamKey`：当前状态索引
- `StateParamKey + 1`：最近一次 `AnimatorFeedbackKind`
- `StateParamKey + 2`：最近一次反馈的 `FromStateIndex`
- `StateParamKey + 3`：最近一次反馈的 `ToStateIndex`
- `StateParamKey + 4`：最近一次反馈的 `NormalizedTime01`
- `StateParamKey + 5`：最近一次反馈的 `Value0`

这样 Rule/Behavior 只需要读取 blackboard，不需要额外桥接 `AnimatorFeedbackBuffer`。

### 3.5 迁移影响

| 文件 | 处置 |
|------|------|
| `AnimatorParameterBuffer.cs` | 删除 |
| `AnimatorRuntimeSystem.cs` | 重写输入源（从 ECS 组件→blackboard） |
| `AnimatorFeedbackBuffer.cs` | 重写输出（反馈写回 blackboard） |
| `AnimatorPackedState.cs` | 保留（adapter 消费格式不变） |
| `AnimatorRuntimeState.cs` | 保留（运行时状态追踪不变） |
| `AnimatorControllerDefinition.cs` | 保留（状态机定义不变） |

## 4 PerformerCommand 的 SetParam 字段

```csharp
public struct PerformerCommand
{
    public PerformerCommandKind CommandKind;  // 独立枚举，不复用 PresentationCommandKind
    // SetParam 时使用：
    public int ParamKey;
    public ParamLane ParamLane;
    public float FloatValue;
    public int IntValue;
    public Vector4 VectorValue;
    // 其他字段：
    public int PerformerDefinitionId;
    public int ParentHandle;
    public int ScopeTag;
    public int TargetBehaviorSlot;
}
```

JSON 中的 SetParam 命令：

```jsonc
{ "kind": "SetParam", "paramKey": 200, "lane": "float", "floatValue": 1.0 }
{ "kind": "SetParam", "paramKey": 300, "lane": "int", "intValue": 2 }
{ "kind": "SetParam", "paramKey": 400, "lane": "vector", "vectorValue": [1, 0, 0, 1] }
```
