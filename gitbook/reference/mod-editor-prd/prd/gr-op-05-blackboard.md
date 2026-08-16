# gr-op-05 · 节点：黑板

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-05-blackboard.md)；编辑器需求见 [UXD](../uxd/gr-op-05-blackboard.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-05-blackboard.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-05-blackboard.md)；现状见 [reference](../reference/gr-op-05-blackboard.md)。

## 1. 定位

图访问实体黑板的通道：按类型读写 Float/Int/Entity 三种键值。AI 决策与订单流水之间传递"记住的事"。

## 2. 产品承诺

- **键即符号**：黑板键经配置键注册表声明；图里只写键名，编译期解析。
- **读写分家**：Read 三件六类图里大多可用且只读；Write 三件是 Effect 专属动作。
- **类型各一条**：Float/Int/Entity 三对读写节点，无变体转换；键的类型由注册表给出。
- **一次一值**：每个节点读写一个键；批量搬运靠多条节点，不提供整表操作。

## 3. 运行行为

Read 在执行期读实体黑板缓冲当前值写值线；Write 在效果事务内把 value 写入实体黑板键。键未建时按黑板缓冲的缺省语义处理。

## 4. 异常承诺

引用未注册键、键类型与节点类型不符——编译失败并指明节点与键名。Read 遇实体无黑板缓冲，按缺省值处理不报错。

**相关文档**：[配置说明](../config/gr-op-05-blackboard.md) · [ord-04](ord-04-blackboard.md) · [gr-op-04](gr-op-04-attributes.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
