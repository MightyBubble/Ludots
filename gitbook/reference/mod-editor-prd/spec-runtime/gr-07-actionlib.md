# gr-07 runtime spec · 动作库 ActionLib

> 引擎实现任务书。第一性需求见 [gr-07 PRD](../prd/gr-07-actionlib.md)；现状见 [reference](../reference/gr-07-actionlib.md)。

## 1. 概述

动作目录合同：字段封闭、资产中性、双库命名空间。

## 2. 设计

- 字段三件封闭（name/graph/kind）；kind 门仅 Script；不读 host——资产不声明消费方（#1542 起仓库核心规范）。
- 撞名检查保持对 FuncLib 目录双向生效。
- 装载期不做 Yield 校验；消费方在绑定时校验自身挂起政策（可挂起图挂到不可挂起宿主，绑定期拒绝）。装载位置保持 FuncLib 之后。

## 3. 精确语义与不变量

- 动作名唯一且与函数名不重叠；每个动作的图 id、kind 二元组与注册表一致；挂起合法性由消费方绑定裁决，资产层不携带宿主信息。

## 4. 迁移与治理

host 字段删除后资产与消费方解耦；消费侧绑定校验的覆盖缺口按消费方各自测试跟进。

## 变更记录

- v2（2026-09-20）：host 字段删除，资产中性；Yield 校验移到消费方绑定期。
- v1（2026-08-15）：初版。

**相关文档**：[gr-07 PRD](../prd/gr-07-actionlib.md) · [reference](../reference/gr-07-actionlib.md) · [gr-05 spec](gr-05-execution.md)
