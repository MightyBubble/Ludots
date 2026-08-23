# fx-15 UXD · 弹道的编辑器需求

> fx-15 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

LaunchProjectile 效果编辑页的弹道表单：飞行、命中策略、碰撞、子效果四组参数。

## 2. 布局线框

```text
┌─ 效果编辑页 · 弹道 ────────────────────────────────────────────┐
│ 飞行  speed [1200]  range [2000]  arcHeight [0]                │
│       travelMode (·)Direction ( )TrackTarget                   │
│ 命中  impactPolicy (·)DestroyOnFirstHit ( )ContinueOnHit       │
│       maxHitCount [1]（贯穿策略时启用，1..上限见事实页）        │
│ 碰撞  collisionHalfWidth [80]  relationFilter [All ▾]          │
│       collisionExcludeSource [✔ 排除发射者]（仅 true 可存）     │
│ 子效果 hitEffect [Effect.Moba.Damage.R ▾]*                     │
│       impactEffect [可选 ▾]  presentationEffect [可选 ▾]        │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 飞行数值框 | 整数 | 负数即时红条 |
| travelMode / impactPolicy 单选 | Direction/TrackTarget；DestroyOnFirstHit/ContinueOnHit | 不提供 Legacy；impactPolicy 联动 maxHitCount 可用性 |
| maxHitCount | 1..命中历史容量（事实页） | 滑条限界 |
| relationFilter 选择 | 关系过滤枚举 | 与 fx-10 过滤同源 |
| 子效果选择器 | 效果模板注册表 | hitEffect 必选；另两个可空 |
| 排除源开关 | 布尔 | 只写 true，关闭即删字段（false 禁存） |

## 4. 关键交互流：把直射箭改成贯穿箭

1. 打开 Arrow 效果 → 弹道表单。
2. travelMode 保持 TrackTarget；impactPolicy 切 ContinueOnHit。
3. maxHitCount 启用，设 3（上限内）。
4. 保存；校验通过后模板热通道提示"下次施放生效"。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 缺 hitEffect；collisionHalfWidth<=0 | 清空选择 / 输入越界 | 红条，保存禁用 |
| Legacy 意图 | 导入旧配置含 Legacy | 迁移提示改 Direction/TrackTarget 并显式配碰撞 |
| 引用悬空 | 子效果被删 | 选择器标"未注册"并阻保存 |

## 6. 易用性验收口径

- 四组参数在单屏无滚动可见。
- "贯穿需要哪些字段"（策略→maxHitCount 联动）无需查文档即可正确完成。

**相关文档**：[fx-15 PRD](../prd/fx-15-projectile.md) · [editor spec](../spec-editor/fx-15-projectile.md)
