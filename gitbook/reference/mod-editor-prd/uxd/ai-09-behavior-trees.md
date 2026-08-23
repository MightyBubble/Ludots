# ai-09 UXD · 行为树的编辑器需求

> ai-09 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

行为树面板是树的画布与台架：左画结构、右验行为——JSON 扁平表在这里升维成可执行的可视图。

## 2. 布局线框

```text
┌─ 行为树面板 ──────────────────────────────────────────────────────────┐
├─ 左：树清单 ──┬─ 中：画布 ─────────────────┬─ 右：节点属性 ──────────┤
│ ▸ patrolChase │   ?root(Selector)          │ 节点 attack             │
│   9 节点 ✔    │   ├─engage(Sequence) ✔     │ kind   [Action ▾]       │
│ ＋新建树      │   │  ├─seeEnemy ✔ Cond     │ leaf   [ScriptSlice ▾]  │
│               │   │  └─engageSelect ✔      │ action [bt.attack ▾]    │
│               │   └─patrol ⏳ Running       │ 运行态：Running·游标2   │
│               │        (action: bt.patrol) │ 上限：64 节点/16 栈深    │
├─ 底部：think wave 面板 [Restart] [Tick·预算 32] · 波形：●●●○● ────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 树清单 | behavior_trees 合并视图 | 徽标：节点数、校验状态 |
| 画布 | 树结构图（root→nodes 展开） | 拖拽接子；多父/断链即时红 |
| kind/leaf 下拉 | 四值/五值枚举（严格大小写，落盘原样） | I2 提示"此处区分大小写" |
| action 选择器 | GraphActionCatalog 中 host=BehaviorTree 的项 | 仅 ScriptSlice 可选时启用 |
| 运行态叠层 | BehaviorTreeWorld agent 状态 | 节点着色 Success/Failure/Running |
| think wave 面板 | 手动触发 Restart/Tick | 每次 Tick 显示本轮 stats |

## 4. 关键交互流：给树加"血量低撤退"分支

1. 画布选 engage 的 Selector 父节点，加 Condition 叶 lowHealth。
2. 属性面板 action 绑 bt.lowHealth；action 选择器只列已注册 BT 图。
3. 画布顶部出现小工具提示 Condition 必须 halt（ReturnInt≠0=Success）。
4. 点 Tick 单步：观察 lowHealth 着色与 Selector 走向。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 结构非法 | 多父/不可达/root 缺失 | 画布红色高亮 + 禁止保存 |
| 枚举大小写 | 手输 kind/leaf | 即时红字（I2 规则本地化） |
| action 断链 | ActionLib 无此名 | 下拉红框 |
| 跨波 Running | Action Yield | 节点⏳标记 + 游标数 |
| 超上限 | 节点>64 或深>16 | 添加动作被拒并提示 |

## 6. 易用性验收口径

- 扁平 JSON ↔ 树画布双向同步，任一侧编辑都落同一份文件。
- 结构错误（多父/不可达/重复 id）在画布层即被拦截。
- Tick 单步调试可见每节点状态与脚本步消耗。

**相关文档**：[ai-09 PRD](../prd/ai-09-behavior-trees.md) · [editor spec](../spec-editor/ai-09-behavior-trees.md)
