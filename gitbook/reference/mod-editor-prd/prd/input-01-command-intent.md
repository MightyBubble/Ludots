# input-01 · 命令意图档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/input-01-command-intent.md)；编辑器需求见 [UXD](../uxd/input-01-command-intent.md)；引擎实现见 [runtime spec](../spec-runtime/input-01-command-intent.md)；editor spec 见 [editor spec](../spec-editor/input-01-command-intent.md)；现状见 [reference](../reference/input-01-command-intent.md)。

## 1. 定位

命令意图档案回答"这次指针命令对每个选中演员变成什么单"：按演员条件与目标条件分规则，把命令路由到订单类型或技能槽——输入映射（ord-06）的组路由大脑。

## 2. 产品承诺

- **逐演员分规则**：一条命令对每个演员独立过规则表，priority 高者优先，命中即路由。
- **双侧条件**：演员侧看能力标签三式；目标侧看标签、姿态、有无实体三态。
- **两种路由终点**：落一个订单类型，或落技能槽（按能力标签取槽、按上下文组取槽）。
- **帧级意图切换**：当前帧意图 = 交互帧显式指定，退到控制方案默认，再退到不路由。
- **档案独立并行**：档案间互不编组，一次只有一个意图生效。

## 3. 运行行为

每帧由仲裁器从交互上下文栈与控制方案解析生效意图；命中档案后按规则序对演员集合逐个择路由，交输入映射系统提交订单。

## 4. 异常承诺

规则引用未注册的订单类型或槽位来源、条件形状非法——启动失败并指明档案与规则序号。

**相关文档**：[配置说明](../config/input-01-command-intent.md) · [ord-06](ord-06-input-mappings.md) · [input-02](input-02-cast-dispatch.md)
