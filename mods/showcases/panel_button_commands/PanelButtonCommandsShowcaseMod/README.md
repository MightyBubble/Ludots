# 面板按钮下令（panel_button_commands）

一张面板，把"指令按图规则聚合显示"这件事从头演到尾，全程零 C#。

## 进门看什么

- 底部中央一条面板：**技能格**按图规则从施法者身上聚合出来（改实体的技能表，格子跟着变，模板一个字不用改）；
- 悬停技能格 → 出提示；
- **点技能格** → 走与键盘完全相同的施法意图管线，火球真飞出去、靶子真掉血；
- **点「召唤靶子」** → 经效果在场生成一个真实体，面板上的靶子计数跟着涨。

## 背后只有一件事（SSOT）

PanelHost 的思想里没有"指令面板"这种独立概念——只有**图把指令聚合出来、面板照实显示、按钮把语义动作送进正式意图管线**。本 showcase 的全部文件都是这条链的声明：

| 文件 | 职责 |
|---|---|
| `assets/Panels/panel_templates.json` | 面板长什么样（技能格行模板 + 容器 + 召唤按钮） |
| `assets/GAS/graphs.json` | 数据从哪来（技能槽聚合/靶子计数）、点击后走哪（施法图/召唤图）、何时开面板 |
| `assets/GAS/effects.json` | 召唤效果的形状（CreateUnit 复用共享靶子模板） |
| `assets/Events/custom_events.json` | 点击伴随事件的载荷合同（slot） |
| `assets/Input/*.json` | 两个按钮动作 + 交互 context 挂载 |

技能、实体、表现全部来自 `FireballSharedMod` 共享库，本 mod 不自造任何一份。

## 怎么启动

启动器选「面板按钮下令」preset，或：

```
scripts/run-mod-launcher.cmd cli launch preset:panel_button_commands_raylib --adapter raylib
```

## 验收

```
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~PanelButtonCommands"
```

断言覆盖：悬停提示、点击召唤真实体（面板计数跟随）、点击火球真施法（意图接受 + 弹道全程飞抵目标）。已知跟进：headless 弹道引爆与无目标点击护栏见 issue #1739。
