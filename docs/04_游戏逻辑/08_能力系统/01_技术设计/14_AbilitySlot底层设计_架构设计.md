---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v0.1
适用范围: AbilitySlot 底层设计
状态: 设计中
---
# AbilitySlot 底层设计 — 架构设计

## 1 现状

`AbilityStateBuffer` 是固定 4 槽位（CAPACITY=4），无动态/被授予技能槽位区分。物品技能、Buff 授予技能无法优雅添加。

## 2 两层槽位设计

### 2.1 BaseSlots（固定槽位）

角色天生拥有的技能槽位（如 QWER、普攻），容量可配置。

```csharp
public unsafe struct AbilityBaseSlotBuffer
{
    public const int MAX_BASE_SLOTS = 8; // 可配置：MOBA=4, MMO=8+
    public fixed int AbilityIds[MAX_BASE_SLOTS];
    // 每槽可存 template entity 引用（flyweight）
    public fixed int TemplateEntityIds[MAX_BASE_SLOTS];
    public fixed int TemplateEntityWorldIds[MAX_BASE_SLOTS];
    public fixed int TemplateEntityVersions[MAX_BASE_SLOTS];
    public int Count;
}
```

### 2.2 GrantedSlotBuffer（动态槽位）

物品主动技能、Buff 授予技能、被动触发技能等动态来源。

```csharp
public unsafe struct GrantedSlotEntry
{
    public int SlotIndex;      // 统一编址空间中的 index（如 BaseSlots 用 0-7，Granted 从 8 开始）
    public int AbilityId;
    public int GrantSourceId;  // 授予来源标识（物品 ID / Effect ID / Buff ID）
    public int TemplateEntityId;
    public int TemplateEntityWorldId;
    public int TemplateEntityVersion;
}

public unsafe struct GrantedSlotBuffer
{
    public const int MAX_GRANTED = 16;
    // 使用展平数组存储
    public fixed int Data[MAX_GRANTED * 6]; // 每个 entry 6 个 int
    public int Count;
    
    public void Add(int slotIndex, int abilityId, int grantSourceId, Entity templateEntity);
    public void RemoveByGrantSource(int grantSourceId);
    public bool TryGet(int slotIndex, out GrantedSlotEntry entry);
}
```

### 2.3 统一编址

```
SlotIndex 0~7:   BaseSlots（固定，QWER + 额外）
SlotIndex 8~23:  GrantedSlots（动态，物品/Buff）
```

`AbilityExecSystem` 按统一 SlotIndex 查找，不区分来源：

```csharp
bool TryResolveAbility(Entity entity, int slotIndex, out int abilityId, out Entity templateEntity)
{
    if (slotIndex < AbilityBaseSlotBuffer.MAX_BASE_SLOTS)
    {
        // 从 BaseSlotBuffer 查找
    }
    else
    {
        // 从 GrantedSlotBuffer 查找
    }
}
```

## 3 物品技能生命周期

```
装备物品:
  → ItemSystem 检测物品有 ActiveAbilityId
  → 调用 GrantedSlotBuffer.Add(nextSlotIndex, abilityId, itemId, templateEntity)
  → UI 更新显示新技能槽位

卸下物品:
  → ItemSystem 调用 GrantedSlotBuffer.RemoveByGrantSource(itemId)
  → 若该技能正在执行中 → 触发 Interrupt
  → UI 隐藏对应槽位
```

## 4 与现有系统的兼容

- `AbilityStateBuffer` 作为遗留兼容层保留，内部转发到 `AbilityBaseSlotBuffer`
- Input 映射中 `ArgsTemplate.I0`（slot index）统一使用编址空间
- Order 的 `Cast_SlotIndex` Blackboard Key 使用统一编址
