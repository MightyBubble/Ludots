# J8: 元素组合产生不同技能

> 清单编号: J8 | 游戏示例: Dota Invoker QWE→Invoke

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按 Q/W/E 键记录元素序列，按 Invoke 键根据序列组合生成对应技能；元素序列存储在 Attribute，Invoke 通过 Phase Graph 查表生成技能。

## Ludots实现

```
Invoker QWE:
  Q press → cycle element slot:
    Attribute(element_0/1/2) 轮替记录
    AddTag("quas_count:N")

  Invoke press:
    Phase Graph:
      1. 读 element_0, element_1, element_2
      2. 查表 (QQQ=Cold Snap, QQW=Ghost Walk, ...)
      3. 将对应 ability 写入 slot 4/5 (invoked spell slots)
      4. RemoveTag 所有 element tags, 重置
```

元素序列存储在 3 个 Attribute (element_0/1/2)，每次按 Q/W/E 轮替记录。Invoke 技能通过 Phase Graph 读取 3 个 Attribute，查表映射到对应 ability ID，写入 invoked spell slots (slot 4/5)，最后重置元素序列。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| Attribute system | ✅ 已有 | element_0/1/2 Attribute 读写 |
| Phase Graph | ✅ 已有 | 查表逻辑 + 条件分支 |
| GameplayTagContainer | ✅ 已有 | element count tags |
| AbilityStateBuffer | ⚠️ 需要扩展 | 动态写入 invoked spell slots (slot 4/5) |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 动态 ability slot 写入 | P2 | AbilityStateBuffer 需支持运行时修改 slot 绑定的 ability ID |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
