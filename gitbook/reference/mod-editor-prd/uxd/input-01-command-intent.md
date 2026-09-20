# input-01 UXD · 命令意图档案的编辑器需求

> input-01 的编辑器需求（高保真规格）。第一性需求见 [input-01 PRD](../prd/input-01-command-intent.md)；配置写法见 [input-01 配置说明](../config/input-01-command-intent.md)；编辑器实现见 [editor spec](../spec-editor/input-01-command-intent.md)。

## 1. 界面定位

命令意图编辑器：以档案为单位的规则梯——每层一条"演员条件 × 目标条件 → 路由"，配好即可用模拟演员验证分派。

## 2. 布局线框

```text
┌─ 命令意图编辑器：intent.command.combat ──────────────────────────────┐
├─ 左：档案清单 ────────┬─ 中：规则梯 ─────────────────────────────────┤
│ ▸ …command.default ⚡2│ #30 ▸ actor[hasAbilityWithCategory: Attack]       │
│ ▸ …command.combat ●  │      target[stance: Aggressive]              │
│                      │      → route[orderTypeKey: attackTarget]  ↕ │
│ ▸ ＋新建档案         │ #20 → route[slot: contextGroup:…] ]  ↕      │
├─ 右：模拟面板 ────────┴───────────────────────────────────────────────┤
│ 演员 [坦克×2 兵营×1]  目标[敌实体▾] ▶分派                            │
│ → 坦克→attackTarget · 兵营→contextGroup 槽 · （未命中→不路由）      │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 档案清单 | 档案表 | 徽标=规则数；默认档案标 ⚓ |
| 规则梯 | rules 数组 | 拖拽排序即改 priority；层间互斥提示 |
| 条件行 | 演员三式 / 目标四式（tag 总账、姿态枚举、三态） | 条件可组合，空侧=不限 |
| 路由行 | 订单类型注册表 / `byAbilityCategory:` / `contextGroup:` 补全 | 二选一 |
| 模拟面板 | 意图解析干跑 | 输入演员+目标组合 → 逐演员路由结果 |

## 4. 关键交互流：让带攻击能力的单位右键敌目标时攻击

1. 打开默认档案，规则梯顶部＋一层，priority 30。
2. 演员条件选 `hasAbilityWithCategory: Ability.Attack`；目标条件 `stance: Aggressive`。
3. 路由落 `attackTarget`。
4. 模拟面板选"坦克×敌实体"▶ → 显示坦克→attackTarget；兵营落入下层兜底。
5. 保存 → 档案表落盘。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 全不命中 | 模拟有演员无路由 | 黄条"该演员本帧不路由" |
| 规则遮蔽 | 某层永远被高层盖住（干跑统计 0 命中） | 灰条 + 遮蔽者链接 |
| 引用悬空 | orderTypeKey/slot 来源不存在 | 红条 + 保存阻断 |

## 6. 易用性验收口径

- 加一层"条件→路由"≤ 4 次交互。
- 模拟分派结果与运行期逐演员一致（同源干跑）。

**相关文档**：[input-01 PRD](../prd/input-01-command-intent.md) · [editor spec](../spec-editor/input-01-command-intent.md)
