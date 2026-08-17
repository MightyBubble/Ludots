# pres-03 · 动画配置

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/pres-03-animation.md)；编辑器需求见 [UXD](../uxd/pres-03-animation.md)；引擎实现见 [runtime spec](../spec-runtime/pres-03-animation.md)；editor spec 见 [editor spec](../spec-editor/pres-03-animation.md)；现状见 [reference](../reference/pres-03-animation.md)。

## 1. 定位

动画域三张表把"动起来"拆成三层：控制器声明状态机（状态、转移、条件），剪辑声明一段动画数据在平台上的位置，档案把控制器状态接到具体剪辑——同一控制器配不同剪辑即不同体型单位的步态复用。

## 2. 产品承诺

- **状态机可编程**：转移用条件种类 + 参数索引 + 阈值表达（如速度超阈值切跑）；默认状态必填，状态表非空。
- **多后端寻址**：剪辑用 locators 数组按 backendId 寻址动画数据，一个剪辑可同时落地多个后端。
- **档案解耦复用**：animation_profiles 按状态索引绑剪辑；控制器的状态语义与具体剪辑文件互不认识。
- **命名纪律**：档案拒绝 snake_case 的 builtin_clips 键——内置剪辑表不复存在，写了即失败并被指路正确写法。
- **集群动画可用**：档案消费方含 Mass 集群动画，大规模单位同门动画不走逐实体 Presenter。

## 3. 运行行为

控制器注册后由 AnimatorRuntimeSystem 驱动状态求值与转移；档案在表现器/批次侧被引用后，状态索引映射到剪辑 id 再到平台数据；blendInputs 支持混合树输入。

## 4. 异常承诺

id 缺失或重复、states 为空、defaultStateIndex 缺失、locators 为空、档案引用未注册控制器或剪辑、出现 builtin_clips——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/pres-03-animation.md) · [pres-01](pres-01-performers.md) · [pres-04](pres-04-localization.md)
