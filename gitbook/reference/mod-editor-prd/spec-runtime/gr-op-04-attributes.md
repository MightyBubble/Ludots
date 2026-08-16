# gr-op-04 runtime spec · 节点：属性与配置

> 引擎实现任务书。第一性需求见 [gr-op-04 PRD](../prd/gr-op-04-attributes.md)；现状见 [reference](../reference/gr-op-04-attributes.md)。

## 1. 概述

属性读写与配置读的图合同：符号解析、直写语义、监听图边界。

## 2. 设计

- LoadAttribute/LoadSelfAttribute 保持"读 Current 一次"形态；缺省值语义与属性缓冲约定一致。
- WriteSelfAttribute 是描述符表唯一 `derivedWrite=true` 的 op：Effect 与 Derived 两类可写，事务内直写 SetCurrent，不建修改器、不触发重聚合——这条"绕过聚合"是产品语义，不是实现捷径。
- LoadConfig 三件 `listenerOwner=true`：监听图（无 owner 模板上下文）编译拒绝；键经 ConfigKeyRegistry 解析。
- **治理项**：直写与修改器两路写入并存时无冲突提示——补一条编译期 lint（同图同属性双路写入）。

## 3. 精确语义与不变量

- WriteSelfAttribute 只写宿主自身属性；无"写任意实体"变体（写他人走 ModifyAttributeAdd，gr-op-10）。
- LoadConfig 绑定的配置键在图注册时解析一次；执行期读当前值。
- LoadConfigEffectId 产出的是效果 id（Int），不是模板名。

## 4. 迁移与治理

现状即基线；双路写入 lint 入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-04 PRD](../prd/gr-op-04-attributes.md) · [reference](../reference/gr-op-04-attributes.md)
