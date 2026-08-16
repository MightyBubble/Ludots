# gr-op-03 · 节点：标签

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-03-tags.md)；编辑器需求见 [UXD](../uxd/gr-op-03-tags.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-03-tags.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-03-tags.md)；现状见 [reference](../reference/gr-op-03-tags.md)。

## 1. 定位

图读 tag 的唯一入口：HasTag 一颗节点，问"这个实体现在有效挂着这个 tag 吗"。

## 2. 产品承诺

- **一问一答**：输入实体与一个 tag 名，输出 Bool；判定走有效标签语义（直接挂载加规则推导），不是裸位图。
- **tag 名是符号**：tag 在编译期经注册表解析成位 id；图里只写名字。
- **查表不进图**：任何"从 tag 反查实体/展示名"的表达需求归通用用户表与表现层，不提供图节点。

## 3. 运行行为

执行时读实体 tag 快照的有效缓存，一次判定写 Bool 值线；可作 Effect/Score/Validation/Derived/Query/Script 六类图里的门条件。

## 4. 异常承诺

引用未注册 tag 名——编译失败并指明节点与 tag 名。实体无 tag 组件按"没有该 tag"处理，不报错。

**相关文档**：[配置说明](../config/gr-op-03-tags.md) · [tag-01](tag-01-basics.md) · [gr-op-14](gr-op-14-control-flow.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
