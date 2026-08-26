# 验收：图编成代码还能对得上

玩家/作者不需要懂 Roslyn。进度只认 [图能力唯一入口](../architecture/graph-capability-status.md)；合同正本 [图 Codegen 产品化](../architecture/graph-codegen-productization.md)。

---

## 1. 概述

验证「画的图」和「编出来的代码」干的是同一件事，并且编辑器里能看见、能对拍、缺能力会红灯。

---

## 2. 结构

```text
作者蓝图 → 预览/对拍（Bridge）→ 解释金样 vs Codegen
         → Live Debug 后端徽章
覆盖登记 → 每个可执行节点 covered
```

---

## 3. 详情

- 预览与对拍走正式 Bridge，不靠手工跑测试工程。
- 金样是解释器行为；Codegen 差分失败即失败关闭。
- 覆盖登记缺项不得宣称产品完成。

---

## 4. 场景

1. 拼句字幕图预览绿灯，对拍字幕一致。
2. 未覆盖的等回话节点红灯点名。
3. Codegen 挂载后 Live Debug 仍显示作者节点名。

---

## 5. 边界

- 不验收第二套作者格式。
- 不验收「编不过偷偷解释」的产品模式。

---

## 6. UAT

```gherkin
Feature: 图编成代码还能对得上

  Scenario: 拼句图对拍过关
    Given 画廊里「写死一句字幕」那张图能在解释器下吐出「你好」
    When 我用 Codegen 对拍同一张图
    Then 出口同样是「你好」

  Scenario: 缺能力不能装成 Codegen
    Given 图上有一个当前切片未覆盖的节点
    When 我用强制 Codegen 模式装载
    Then 装载失败并点名那个节点
    And 游戏不会假装已经在用生成代码跑

  Scenario: 编辑器看得见生成代码
    Given 我打开一张全绿资格的图
    When 我打开 Codegen 面板
    Then 我能复制出生成的 C# 文本
```
