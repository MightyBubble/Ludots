# input-04 UXD · 施法提交档案的编辑器需求

> input-04 的编辑器需求（高保真规格）。第一性需求见 [input-04 PRD](../prd/input-04-cast-commit.md)；配置写法见 [input-04 配置说明](../config/input-04-cast-commit.md)；编辑器实现见 [editor spec](../spec-editor/input-04-cast-commit.md)。

## 1. 界面定位

施法提交编辑器：操作序列时间线 + 帧内动作表 + 偏好锁面板——扳机行为的可视化编排处。

## 2. 布局线框

```text
┌─ 施法提交编辑器：commit.aim ────────────────────────────────────────┐
├─ 激活序列 onActivate ───────────────────────────────────────────────┤
│ ① pushFrame(ctx.guided ▾)   [＋op]                                  │
├─ 帧内动作 frameActions ─────────────────────────────────────────────┤
│ [Confirm ▾] → ① submitOrder { spatialTarget ← cursorWorld ▾ }       │
│               ② popFrame            [＋动作]                        │
├─ 偏好锁面板 ────────────────────────────────────────────────────────┤
│ 锁定序预览：锁(template:Ability.Q) > 槽 > 形态集 > 模板 > 全局       │
│ ＋加锁 [scope▾ slot] [key▾ 技能槽2] [castCommitId▾ commit.aim]       │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 序列时间线 | onActivate ops | 增删排序；op 三值下拉 |
| 值源映射表 | payload 键 + 值源枚举 | 键补全自订单参数槽（ord-01 三键组/args） |
| 帧内动作表 | frameActions 动作→序列 | 动作补全自动作注册表（input-05） |
| 上下文档案选择 | interaction_context_profiles（input-03） | pushFrame 专属参数 |
| 锁面板 | locks + 偏好解析序 | 加锁即时预览生效层级 |

## 4. 关键交互流：做一个"按 Q 进入瞄准、确认后施放"的提交

1. 新建档案 `commit.aim`；onActivate 加 `pushFrame`，挂 `ctx.guided`。
2. frameActions 加动作 `Confirm` → `submitOrder`（spatialTarget←cursorWorld）→ `popFrame`。
3. 锁面板把模板 `Ability.Champion.Q` 锁到本档案（玩家不可改）。
4. 序列预演▶：压帧 → 确认 → 提交+弹帧。
5. 保存 → 两文件落盘（档案表 + 锁表）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 孤儿帧动作 | frameActions 无对应 pushFrame | 黄条（永不触发） |
| 锁冲突 | 同 key 多锁/作用域形状非法 | 红条 + 保存阻断 |
| 空表 | 根资产无档案 | 引导态（合法） |

## 6. 易用性验收口径

- 编排"压帧→确认→提交→弹帧"≤ 6 次交互。
- 锁面板预览与运行期偏好解析序一致（同源）。

**相关文档**：[input-04 PRD](../prd/input-04-cast-commit.md) · [editor spec](../spec-editor/input-04-cast-commit.md)
