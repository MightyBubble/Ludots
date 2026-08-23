# pres-03 runtime spec · 动画配置

> 引擎实现任务书。第一性需求见 [pres-03 PRD](../prd/pres-03-animation.md)；现状见 [reference](../reference/pres-03-animation.md)。

## 1. 概述

动画三表加载与运行合同：状态机求值、多后端剪辑寻址、档案映射、Mass 集群采样。

## 2. 设计

- 加载合同保持：三表 ArrayById 深合并；控制器校验 states 非空 + defaultStateIndex 必填；剪辑校验 locators 非空；档案加载时解析控制器/剪辑 id。
- 旧键拒绝保持：builtin_clips 抛错文案指路 stateClips——不提供兼容读法。
- 运行合同保持：AnimatorRuntimeSystem 逐实体求值；Mass 集群路径按档案批量采样，不逐实体走 Presenter。
- **治理项**：转移参数 parameterIndex 用字符串参数名逐转移匹配，参数集合无声明面——考虑在控制器层暴露只读参数清单供编辑器自动补全（不新增表）。

## 3. 精确语义与不变量

- 档案映射键为 packedStateIndex；同一控制器的状态索引在档案中重复绑定时以后到者胜（深合并语义）。
- locator 匹配不到当前 backendId 的剪辑：该剪辑对该后端不可用，引用侧不降级。

## 4. 迁移与治理

现状即基线；参数清单只读接口入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[pres-03 PRD](../prd/pres-03-animation.md) · [reference](../reference/pres-03-animation.md)
