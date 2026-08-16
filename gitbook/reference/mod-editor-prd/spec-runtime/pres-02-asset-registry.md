# pres-02 runtime spec · 表现资产清单

> 引擎实现任务书。第一性需求见 [pres-02 PRD](../prd/pres-02-asset-registry.md)；现状见 [reference](../reference/pres-02-asset-registry.md)。

## 1. 概述

表现资产四表（mesh/material/host/instanced_batches）与引擎侧配套表（lod_profiles/particle_vfx）的加载与消费合同。

## 2. 设计

- 四表加载合同保持：ArrayById 深合并、id 唯一守卫、逐条注册进对应注册表。
- 封闭面保持：mesh 的 type 白名单（Primitive/Model/Billboard/VFX）、mesh/material 禁 sourceUris、host 按 backendId 行过滤——逻辑/平台分离不放宽。
- instanced_batches 合同保持：groups 非空、customDataChannels、behaviors 内联、progressiveSubmission；GAS/表现事件键在加载期解析。
- **治理项**：instanced_batches 全仓库无真实 JSON 行数据——通道 latent（见 todo/domains.md D1）；补一个可启动 showcase 或在文档侧长期标注"骨架"。
- **治理项**：lod_profiles/particle_vfx 与四表同域但不同加载节奏，目录侧无"条目被谁消费"对账（同 T3 类问题）。

## 3. 精确语义与不变量

- 逻辑 id（mesh/material）全局命名；host 行不注册逻辑 id，仅绑定。
- host 行的 assetId 必须指向已注册逻辑资产；批次引用同理。
- 引用解析失败 = 启动失败，无占位降级。

## 4. 迁移与治理

现状即基线；D1 showcase 与目录对账入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[pres-02 PRD](../prd/pres-02-asset-registry.md) · [reference](../reference/pres-02-asset-registry.md)
