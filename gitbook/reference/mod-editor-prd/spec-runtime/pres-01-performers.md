# pres-01 runtime spec · 表现器档案

> 引擎实现任务书。第一性需求见 [pres-01 PRD](../prd/pres-01-performers.md)；现状见 [reference](../reference/pres-01-performers.md)。

## 1. 概述

表现器档案加载与 Presenter 生成合同：内联行为、引用预解析、分片合并、剔除语义。

## 2. 设计

- 加载合同保持：ArrayById 深合并 + 冲突报告 + `__delete` 注销；id 注册与解析一次完成。
- 引用解析链保持：mesh/material/text token/实体模板/效果/animator/profile 均在加载期换 id，未注册即抛错。
- 行为模型保持内联：behaviors 数组是唯一行为声明面；**治理项**：曾列于计划的 `prefabs`、`presentation_behaviors` 两表不存在，也不得新增——组合体一律 behaviors 表达（见 todo/domains.md D4）。
- **治理项**：kind/slot 白名单目前由引擎构造注入，无独立声明面——若编辑器需要下拉数据源，暴露只读枚举接口，不新增配置表。

## 3. 精确语义与不变量

- 同一 slot 的行为互斥；同 id 档案跨 mod 深合并只赢写到的字段。
- 分片目录条目与主文件同 id 即合并；整表可空。
- 引用解析失败 = 启动失败，无降级渲染。

## 4. 迁移与治理

现状即基线；白名单只读接口与 D4 目录勘误入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[pres-01 PRD](../prd/pres-01-performers.md) · [reference](../reference/pres-01-performers.md)
