# fx-15 · 目标派发

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-11-target-dispatch.md)；编辑器需求见 [UXD](../uxd/fx-11-target-dispatch.md)；引擎实现见 [runtime spec](../spec-runtime/fx-11-target-dispatch.md)；editor spec 见 [editor spec](../spec-editor/fx-11-target-dispatch.md)；现状见 [reference](../reference/fx-11-target-dispatch.md)。

## 1. 定位

目标派发把候选集变成新效果请求：载荷效果沿三槽角色映射发射，一次查询即一次扇出。

## 2. 产品承诺

- **映射唯一来源**：派发上下文要么选预设要么写显式映射，二者互斥；都不写用默认映射——施法者不变、目标换解析实体、上下文保原目标。
- **三槽可重排**：载荷的施法者、目标、目标上下文三个角色可分别指派给原施法者、原目标、解析实体、原目标上下文四者之一。
- **载荷必须先注册**：payloadEffect 引用未注册效果即启动失败，不存在悬空扇出。
- **扇出走命令**：派发经命令缓冲与事务——事务内随提交发布，事务失败则扇出不发生。
- **链路内建**：查询、派发、二合一三种内建处理器覆盖常用链；图路径可在事务边界内等价扇出。

## 3. 运行行为

命令落地时按三槽重映射发布每个载荷请求；根预算在发布前把关（fx-09）。

## 4. 异常承诺

预设与映射同写、槽值域外、载荷未注册——启动失败并指明模板。

**相关文档**：[配置说明](../config/fx-11-target-dispatch.md) · [fx-11](fx-09-target-query.md) · [fx-12](fx-10-target-filter.md) · [fx-09](fx-07-response-chain.md)
