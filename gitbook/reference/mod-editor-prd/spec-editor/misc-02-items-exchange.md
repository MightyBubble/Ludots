# misc-02 editor spec · 物品与兑换

> 编辑器实现任务书。编辑器需求见 [misc-02 UXD](../uxd/misc-02-items-exchange.md)；引擎侧见 [runtime spec](../spec-runtime/misc-02-items-exchange.md)。

## 1. 概述

物品工坊实现：形状/布局两个网格画板、物品卡表单、兑换公式表与试算。

## 2. 设计

- **画板**：rows 掩码与容器网格的网格编辑控件；序列化直接产出 rows/blockedRows/namedSlots。
- **物品卡**：引用型字段全部下拉封闭（形状/槽位/效果/技能），数据源为对应注册表投影。
- **兑换公式**：行式编辑器（门槛/投入/产出三段）；kind 集合来自引擎枚举投影。
- **试算**：编辑器侧按操作定义对样例单位做预检（关系旗标 + 投入清点），判定与 ExchangeRuntime 同源规则。

## 3. 精确语义与不变量

- 画板序列化与手写掩码 JSON 等价；旋转集为派生只读预览。
- 引用下拉与四注册表实时一致。

## 4. 依赖接口与验收

- 消费：Items 四表投影、Relationship 目录、GAS 效果/能力注册表。
- 验收：新物品 + 兑换产物通过启动校验；试算结论与运行期执行结果一致。

**相关文档**：[misc-02 UXD](../uxd/misc-02-items-exchange.md) · [misc-02 runtime spec](../spec-runtime/misc-02-items-exchange.md)
