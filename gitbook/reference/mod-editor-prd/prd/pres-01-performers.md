# pres-01 · 表现器档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/pres-01-performers.md)；编辑器需求见 [UXD](../uxd/pres-01-performers.md)；引擎实现见 [runtime spec](../spec-runtime/pres-01-performers.md)；editor spec 见 [editor spec](../spec-editor/pres-01-performers.md)；现状见 [reference](../reference/pres-01-performers.md)。

## 1. 定位

表现器是"一类实体长什么样"的档案：把表现参数、资产绑定与行为槽位打包成一个具名单元，实体模板按名引用。表现层的其余表——网格、材质、动画、文本——都经由表现器的绑定与行为被消费。

## 2. 产品承诺

- **一档案一物种**：表现器以 id 全局命名，实体模板引用即获得完整表现；换皮 = 换表现器或覆盖其字段。
- **行为内联**：行为直接内联在表现器的 `behaviors` 数组里，槽位（slot）+ 种类（kind）+ 资产绑定（assetBinding）三元一体；不存在独立的"表现行为表"。
- **无 prefab 通道**：组合体不走 prefab——引擎显式拒绝 prefab 型网格资产并指路"用带 AssetBinding 子项的表现器"；一切组合在表现器内表达。
- **分片可扩展**：整表可空，主文件之外可开分片目录，社区内容按片追加（分片表清单见事实页）。
- **引用即校验**：绑定参数、行为资产、模板/效果/文本引用在加载期解析为 id，解析不到即失败。

## 3. 运行行为

表现实体生命周期系统按档案生成与回收 Presenter；visibility 决定剔除策略，maxVisibilityDistanceCm 决定距离剔除；behaviors 按 slot 挂载资产行为，事件键驱动激活。

## 4. 异常承诺

id 重复、必填字段缺失、行为 kind 不在白名单、资产/文本/模板引用解析失败——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/pres-01-performers.md) · [pres-02](pres-02-asset-registry.md) · [pres-03](pres-03-animation.md)
