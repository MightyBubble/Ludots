#### 案 27：panel.abilities —— 技能指令（#1015 主战场）

> 状态：🔴 目标态——#1015 主战场；拒因：G8（$payload）+ #1015（admission 拒绝回执→按钮态）。

```jsonc
{
  "id": "panel.abilities",
  "graph": "Graph.Unit.Abilities",            // 技能冷却/可用态输出
  "pins": [ { "name": "cooldown", "key": "ability.cooldown", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "ability.cast", "control": "bar.abilities", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "ability.cast", "intent": "unit.castAbility", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.bottomCenter ┌────────────────────────────────────┐
                    │ 【⚔】【🛡】【✨】【💥】              │ 冷却=cooldown 回读置灰转圈
                    └────────────────────────────────────┘
```

30 秒预期：点技能释放、冷却转圈、预算拒绝回执置灰。依赖：G8、#1015。
