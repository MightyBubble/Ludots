# pres-03 editor spec · 动画配置

> 编辑器实现任务书。编辑器需求见 [pres-03 UXD](../uxd/pres-03-animation.md)；引擎侧见 [runtime spec](../spec-runtime/pres-03-animation.md)。

## 1. 概述

动画编辑器实现：状态机画布、剪辑库、档案映射表三视图共用一份动画域模型。

## 2. 设计

- **状态机画布**：节点/边视图模型，序列化为 states+transitions；packedStateIndex 自动分配且保持稳定（已引用索引不重排）。
- **剪辑库**：locators 编辑按 backendId 分行，assetRef 经 VFS 文件选择（cfg-02）；落地状态复用 pres-02 索引。
- **档案映射表**：行 = 控制器状态 × 剪辑下拉；未映射状态高亮。
- **试播**：画布内轻量状态求值器（条件种类与阈值同引擎枚举），不嵌入运行时。

## 3. 精确语义与不变量

- 画布序列化结果与手写等价 JSON 往返无损。
- 表单模型不含 builtin_clips 键（结构上不可产生）。
- 状态索引分配与引擎 packedStateIndex 语义一致。

## 4. 依赖接口与验收

- 消费：三表注册表投影、conditionKind/assetKind 枚举、VFS、pres-02 落地索引。
- 验收：画布构建的状态机通过引擎加载校验；试播转移条件与加载后行为一致。

**相关文档**：[pres-03 UXD](../uxd/pres-03-animation.md) · [pres-03 runtime spec](../spec-runtime/pres-03-animation.md)
