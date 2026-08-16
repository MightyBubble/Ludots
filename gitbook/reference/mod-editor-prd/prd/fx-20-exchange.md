# fx-19 · 兑换

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-20-exchange.md)；编辑器需求见 [UXD](../uxd/fx-20-exchange.md)；引擎实现见 [runtime spec](../spec-runtime/fx-20-exchange.md)；编辑器实现见 [editor spec](../spec-editor/fx-20-exchange.md)；现状见 [reference](../reference/fx-20-exchange.md)。

## 1. 定位

Exchange 效果执行一次兑换操作：输入侧扣、输出侧给，一次原子成败——买装备、付关税、以物易物的效果侧入口。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期；操作 id 经参数 `_ep.exchangeOperationId` 必需且为正。
- **作用域可选**：`_ep.exchangeScopeKey` 提供命名作用域，缺省走默认。
- **失败不炸效果**：兑换失败只记录结果与诊断，不抛错、不中断效果链。
- 操作本体（输入/输出/门槛）声明在兑换操作表，效果只引用 id。
- **现状边界**：处理器未通过原子域认证，模板无法通过启动计划编译（治理见 spec，E13 族）。

## 3. 运行行为

上下文由 source/target/targetContext 与作用域组装；TryExecute 一次原子结算，结果计入预算与诊断。

## 4. 异常承诺

非 Instant、缺参数或参数非正——启动失败并指明键名；操作名未注册——启动失败；兑换业务失败——记录不抛。

**相关文档**：[配置说明](../config/fx-20-exchange.md) · 见 misc-02（物品与兑换域）
