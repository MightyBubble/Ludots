# gr-op-14 UXD · 节点：Script 控制流的编辑器需求

> gr-op-14 的编辑器需求（高保真规格）。第一性需求见 [gr-op-14 PRD](../prd/gr-op-14-control-flow.md)；配置写法见 [gr-op-14 配置说明](../config/gr-op-14-control-flow.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-14-control-flow.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

Script 图的结构化外衣：作者写糖（Branch/While/Wait），画布显示结构块；汇编八件留给高级作者与生成器。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：控制流（Script 图；JumpIfFalse 亦在 Effect 图）──┐
│ ▸ 结构化   BranchBool / SwitchInt / While / Until / Wait          │
│ ▸ 汇编     Jump / JumpIfFalse / Call / Return / Yield /          │
│            HaltReturnInt / InvokeScript / MoveInt                 │
├─ 画布结构块 ─────────────────────────────────────────────────────┤
│ ┌ While [water < 3] ────────────────┐                           │
│ │  sipAdd（水+1）  ⏸Wait              │                           │
│ └──────────────↺────────────────────┘                           │
│  ■ Halt（value=water）               ← 每图必有，缺省自动补       │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 糖面板 | 糖常量表（五个） | 拖入即插糖节点，保存时展开 |
| 函数名选择器 | FuncLib 注册表（gr-06） | InvokeScript 的 imm |
| Halt 自动补 | 图扫描 | 保存前若无显式终结自动插入并提示 |
| 钉槽引导 | 寄存器文件 | 循环变量建议 `pinRegister` |
| 深度投影 | vm 限额 | InvokeScript 链显示当前嵌套深度 |

## 4. 关键交互流：写一个节拍循环

1. Script 图拖 While，条件接 CompareLtInt（water<3）。
2. 循环体放 AddInt；编辑器提示"water 是循环变量，建议钉槽 0"，确认写入 `pinRegister`。
3. 体尾拖 Wait（=Yield）；画布显示 ⏸ 标记。
4. 保存：糖展开、自动补 Halt（value 接 water）；深度投影显示 1/上限。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 缺 Halt | 保存前无终结 | 自动补 + 一次性说明 |
| 子图含 Wait | FuncLib 图里放糖 | 拒绝落点并提示"子图禁 Yield" |
| 深度预警 | InvokeScript 嵌套接近上限 | 黄条 |
| 糖用于 Effect 图 | Wait/While/Until 拖入 Effect 图 | 置灰（BranchBool 除外） |

## 6. 易用性验收口径

- While 循环从拖入到编译通过 ≤ 5 步，钉槽建议自动出现。
- 任何图保存时不可能带着"缺 Halt"错误离场。

**相关文档**：[gr-op-14 PRD](../prd/gr-op-14-control-flow.md) · [editor spec](../spec-editor/gr-op-14-control-flow.md)
