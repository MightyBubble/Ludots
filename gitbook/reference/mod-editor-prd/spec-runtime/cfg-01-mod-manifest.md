# cfg-01 spec · mod 数据

> 引擎实现任务书。第一性需求见 [cfg-01 PRD](../prd/cfg-01-mod-manifest.md)；配置说明见 [cfg-01 配置说明](../config/cfg-01-mod-manifest.md)；现状见 [cfg-01 reference](../reference/cfg-01-mod-manifest.md)。

## 1. 概述

清单解析保持封闭字段白名单与严格校验；编辑器侧提供规范化写回与新建向导的数据合同；`configRoots` 声明字段挂靠本清单（设计见 cfg-05 spec）。

## 2. 设计

### 解析合同

- 字段白名单封闭集合：name、version、description、main、priority、dependencies、author、url、changelog、tags、processSharedAssemblies、configRoots。白名单外字段启动失败。
- name / version 必填非空字符串；version 的三段语义在依赖解析阶段校验。
- 编辑器写回使用规范化序列化：固定字段序、缩进、空值省略，保证 diff 稳定。

### 新建 mod 向导的产物

目录骨架（默认配置根 + 空 assets）+ 最小合法清单 + 可选示例配置。向导产出的清单必须一次通过解析合同。

### 依赖解析

- 顺序解析（产品路径）：依赖闭包 DFS 后序遍历，依赖按键名字母序访问、根按选择顺序，由启动器烘焙为有序清单（见 cfg-03 spec）；priority 不参与。
- 本地回退排序（调试、无头直启）：拓扑排序，就绪候选按 priority 降序、发现序升序。
- 版本范围语法：`^ ~ >= <= > < =` 与 `*`，空串等价 `*`。
- 失败模式：缺依赖、版本不符、重名、循环——全部启动失败，错误消息带双向引用（谁缺谁、谁与谁重名）。

## 3. 精确语义与不变量

- 清单字段白名单与本地回退拓扑序用 Ordinal；启动器侧 mod/依赖/绑定解析为忽略大小写（治理项：统一大小写策略，见开放决策）。
- priority 必须是整数（JSON number），不接受字符串数字。
- dependencies 值必须是字符串；`dependencies` 本身必须是对象。
- processSharedAssemblies 元素非空、写入前 Trim。
- 规范化序列化往返无损：解析 → 序列化 → 再解析得到相同清单。

## 4. 迁移与治理

1. 白名单增加 `configRoots`（cfg-05 spec），随该任务交付。
2. 编辑器新建向导与规范化写回按本合同实现。
3. 验收：未知字段拒绝、往返无损、向导产物可启动。

风险：白名单扩充为纯新增，默认行为不变。

## 变更记录

- v1（2026-08-15）：初版——解析合同、向导产物合同、依赖解析不变量、configRoots 挂靠点。
- v2（2026-08-15）：顺序解析改为启动计划烘焙语义，priority 定位为本地回退与展示排序（审读修复报告第 1 条）。

**相关文档**：[cfg-01 prd](../prd/cfg-01-mod-manifest.md) · [cfg-01 reference](../reference/cfg-01-mod-manifest.md) · [cfg-05 spec](cfg-05-config-pipeline.md)（configRoots 设计）
