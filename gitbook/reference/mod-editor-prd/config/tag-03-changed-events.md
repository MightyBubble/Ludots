# tag-03 配置说明 · Tag 变化与事件

> 配置写法与行为。第一性需求见 [tag-03 PRD](../prd/tag-03-changed-events.md)；编辑器需求见 [UXD](../uxd/tag-03-changed-events.md)；现状见 [reference](../reference/tag-03-changed-events.md)。

## 1. 示例

反应绑定（实体模板组件，教学骨架）——"受击时触发还击技能"：

```json
"ReactionBuffer": {
  "entries": [
    { "tag": "Event.Damaged", "slot": 2 }
  ]
}
```

图程序等待（Script 图，骨架）：

```csharp
// EventGate 节点：等待 tag Event.Reinforcement 到达（可设 deadline）
```

## 2. 用法与行为

| 用法 | 写法 | 效果 |
|---|---|---|
| 发布事件 | 任意 tag 变化 | 自动生成：tag ±、层数变（携旧/新值） |
| 反应技能 | 实体 ReactionBuffer（事件 tag→技能槽） | 事件到达即尝试激活该槽技能（事件源为显式目标） |
| 图等待 | EventGate（技能时间轴） | 等待事件 tag，可设超时放行 |
| 属性联动 | 属性变化同理走本管道（幅度=新值） | 属性事件与 tag 事件同一分发时序 |

## 3. 文件结构

无独立表：反应绑定在实体模板组件；事件 tag 名散布于授予方与消费方（tag-01 注册规则）。

## 4. 运行时加载效果

帧末：脏实体队列 → 快照对比 → 三类变化触发 → 事件发布（容量见事实页）；下一拍分发。同一缓冲语义保证同帧等待者可见。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 事件缓冲超容量 | 失败并报错（容量见事实页） |
| 反应槽无效 | 激活失败按技能失败语义报出 |

## 6. 实例

- 事件 tag 声明：`mods/CombatStanceBehaviorMod/assets/GAS/tag_rules.json`（`Event.DamageTaken`）
- 反应消费：效果/技能卷 ReactionBuffer 用法（rts 底座的受击反击可循此实现）

**相关文档**：[tag-03 PRD](../prd/tag-03-changed-events.md) · [tag-01 配置说明](tag-01-basics.md) · [tag-02 配置说明](tag-02-rules.md)
