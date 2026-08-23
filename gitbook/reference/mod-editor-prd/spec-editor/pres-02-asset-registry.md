# pres-02 editor spec · 表现资产清单

> 编辑器实现任务书。编辑器需求见 [pres-02 UXD](../uxd/pres-02-asset-registry.md)；引擎侧见 [runtime spec](../spec-runtime/pres-02-asset-registry.md)。

## 1. 概述

资产浏览器实现：四表统一视图、导入向导（逻辑行+host 行成对产出）、批次编辑器。

## 2. 设计

- **统一资产模型**：mesh/material/host/batch 四注册表投影到同一网格视图，来源 mod 打标。
- **导入向导**：文件经 VFS 选择（cfg-02 地址空间）→ 生成 mesh 逻辑行 + host 平台行；sourceUris 永不落入逻辑行（结构性排除，非校验拦截）。
- **落地索引**：维护"逻辑资产 → host 行"映射，驱动"未落地"状态与缩略图。
- **批次编辑器**：groups 编辑器 + 事件键选择（消费 GAS/表现事件枚举投影）；空 groups 不可保存。

## 3. 精确语义与不变量

- 向导产物必须原样通过引擎四表加载校验（不产生禁字段）。
- 落地索引与 host 表实时一致（保存即更新）。

## 4. 依赖接口与验收

- 消费：mesh/material/host/batch 注册表投影、后端 id、VFS、事件枚举。
- 验收：导入→表现器可选的产物通过启动校验；删除 host 行后"未落地"状态即时出现。

**相关文档**：[pres-02 UXD](../uxd/pres-02-asset-registry.md) · [pres-02 runtime spec](../spec-runtime/pres-02-asset-registry.md)
