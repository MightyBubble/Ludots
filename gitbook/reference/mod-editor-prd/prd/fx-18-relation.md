# fx-17 · 关系操作

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-18-relation.md)；编辑器需求见 [UXD](../uxd/fx-18-relation.md)；引擎实现见 [runtime spec](../spec-runtime/fx-18-relation.md)；编辑器实现见 [editor spec](../spec-editor/fx-18-relation.md)；现状见 [reference](../reference/fx-18-relation.md)。

## 1. 定位

Relation 效果改写实体间关系：挂父、摘父、保链——载具乘载、建筑驻防、编队从属的关系侧入口。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期加 relation 块，块只属于 Relation preset。
- **操作三式**：SetParent 挂父、RemoveParent 摘父、EnsureLink 保链。
- **槽位与条件字段**：subject 禁 None；SetParent 与 EnsureLink 还要求 parent 非 None；snap 仅 SetParent 合法、relationshipType 仅 EnsureLink 合法且必须已注册。
- SetParent 在效果事务内分阶段提交，可随效果回滚。
- **现状边界**：只有 SetParent 能通过启动计划编译；RemoveParent 与 EnsureLink 能写出合法配置但启动即被拒（治理见 spec E13）。

## 3. 运行行为

SetParent 可选把 subject 吸附到父位置；EnsureLink 经关系运行时建链；摘父立即拆除父子关系。

## 4. 异常承诺

槽位为 None、条件字段越权、relationshipType 未注册——启动失败并指明字段；运行期实体失效——抛错带实体 id；RemoveParent/EnsureLink 现状在计划编译期被拒。

**相关文档**：[配置说明](../config/fx-18-relation.md) · 见 fx-06（独占与认证）、rel-01（关系目录）
