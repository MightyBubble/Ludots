# input-05 · 过滤与输入方案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/input-05-filters-and-schemes.md)；编辑器需求见 [UXD](../uxd/input-05-filters-and-schemes.md)；引擎实现见 [runtime spec](../spec-runtime/input-05-filters-and-schemes.md)；编辑器实现见 [editor spec](../spec-editor/input-05-filters-and-schemes.md)；现状见 [reference](../reference/input-05-filters-and-schemes.md)。

## 1. 定位

输入侧的地基四件：动作与绑定表定义"有哪些按键动作"；过滤档案定义"哪些实体算我的"；控制方案定义"哪套输入上下文、意图与派发生效"；动作属性绑定把输入值直写属性。

## 2. 产品承诺

- **动作即词汇**：动作按四档类型声明，绑定按输入上下文分组、带优先级——映射与档案引用的都是这套词汇。
- **过滤锚点展开**：过滤档案从锚点实体出发按关系展开候选，再用 tag 双向筛（排除优先于包含表达）。
- **方案即套餐**：控制方案捆绑输入上下文集与默认意图、默认派发；可切换、可白名单限权。
- **轴即订单**：方案可把轴动作按节流与步长转成移动订单，无需逐帧编程。
- **输入直写属性**：动作值按通道、缩放与 UI 抢占规则写入属性缓冲，供表现与玩法读。

## 3. 运行行为

方案安装后生效上下文集决定动作触发；切换方案即换套餐；过滤档案供交互上下文与集合写入方消费；轴移动系统按节流提交订单。

## 4. 异常承诺

引用未注册的动作、属性、意图或派发档案、绑定形状非法、越权方案切换——启动失败或明确拒绝，并指明文件与条目。

**相关文档**：[配置说明](../config/input-05-filters-and-schemes.md) · [ord-06](ord-06-input-mappings.md) · [input-01](input-01-command-intent.md)
