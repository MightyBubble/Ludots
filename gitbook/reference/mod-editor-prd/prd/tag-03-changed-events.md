# tag-03 · Tag 变化与事件

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/tag-03-changed-events.md)；编辑器需求见 [UXD](../uxd/tag-03-changed-events.md)；引擎实现见 [runtime spec](../spec-runtime/tag-03-changed-events.md)；编辑器实现见 [editor spec](../spec-editor/tag-03-changed-events.md)；现状见 [reference](../reference/tag-03-changed-events.md)。

## 1. 定位

tag 的每次增减都会变成**下一拍可消费的事件**——反应技能（受击触发）、AI 察觉、教程提示都挂在这条管道上。

## 2. 产品承诺

- **变化即事件**：tag 加入/退出/层数变化各生成对应事件，携带旧值新值与幅度。
- **恒定一拍延迟**：本帧的变化下一拍可见——事件序是确定性的，不是性能妥协而是时序合同。
- **反应绑定**：实体可声明"事件 tag → 技能槽"，事件到达即尝试激活对应技能。
- **双缓冲可见性**：本帧发布的事件本帧对同帧等待者可见（等待者检查的是已换入的当前缓冲），跨帧消费走正常事件流。

## 3. 运行行为

帧末收集脏实体 → 快照对比生成变化触发 → 转为事件发布；下一拍事件分发给反应绑定与图程序等待节点。

## 4. 异常承诺

事件缓冲与触发队列超容量即失败并报错，不静默丢事件。

**相关文档**：[配置说明](../config/tag-03-changed-events.md) · [UXD](../uxd/tag-03-changed-events.md) · [tag-01](tag-01-basics.md) · [fx 卷响应链](../prd/cfg-04-config-tables.md)
