# RFC-0066 GAS 装载期定容世界级存储（形态 2）

> 状态：提案 / Epic 开工  
> 对应 Epic 粘贴稿：`RFC-0066-epic-issue-body-draft.md`  
> 编号：RFC-0066  
> 范围：`AttributeBuffer` / `GameplayTagContainer` 及所有 64/256 位图合同副本

## 1. 概述

把属性种类与标签种类的上限，从「编译期写死在每个实体组件里」改为「**装载登记结束后按本局内容量定容一次，冻结后对局内永不增长**」。

实体不再内嵌固定 `float[64]` / 256 位标签图作为容量真相；容量真相落到**世界级（或会话级）SoA 列存**，实体只持有参与该表的句柄/行身份。对内容作者表现为「跟内容永远长」；对对局合同仍是：定长、0 分配热路径、满则失败关闭、禁止热路径结构迁移。

本 RFC **不是** 64→128 常量微调，也 **不是** 对局中自动扩容，更 **不是** 扩展属性高位编号旁路。

### 目标

- 装载期按实际登记种类分配本局属性列数与标签位宽（含绝对天花板防失控）。
- 对局内读写属性/标签的语义与今日一致，热路径保持 0 分配。
- 导航位图、知识披露掩码、展示查表、脏标记与 GAS 标签宇宙**同一容量合同**，禁止分叉。
- **Benchmark 前后对比为验收硬门槛**：迁移不得在未解释回归的情况下合入。

### 非目标

- 对局中、帧中、效果热路径上动态加长列/位图。
- 双轨存储（实体内嵌 64 槽 + 溢出旁路表）作为长期形态。
- 用 `ExtensionAttributeRegistry`（10001–20000）假装扩容。
- 同步放大 TagCount / TimedTag / EffectGranted 等软上限（另单评估）。

## 2. 结构

```text
Mod 装载登记 AttributeRegistry / TagRegistry
        │
        ▼
GasLoadTimeCapacityPlan.Freeze(...)   ← 唯一扩容窗口（装载/Schema 冻结）
        │  产出：AttributeSlotCount、TagIdSpace、字数、绝对天花板校验
        ▼
世界级 SoA 列存（属性 Base/Cap/Current + 定义位；标签位图列）
        │
        ├── 实体仅持行身份 / 参与标记（无内嵌定长容量真相）
        ├── Dirty / Snapshot / EffectiveCache 按 Plan 字数分配
        ├── TagRule / TagDisplay / TagBits / KnowledgeMask 同 Plan
        └── 冻结后 Register → 失败关闭
```

| 模块 | 职责 |
|------|------|
| `GasLoadTimeCapacityPlan` | 装载期容量计划：槽数、位宽、字数、冻结状态 |
| 世界级属性列存 | 按实体行 × 属性列的 SoA；替换 `AttributeBuffer` 内嵌数组的容量真相 |
| 世界级标签位列 | 按实体行 × Plan 字数的位图；替换 `GameplayTagContainer.Bits[4]` |
| 跨域位图合同 | 导航 `TagBits*`、知识掩码、展示表、DirtyFlags 与 Plan 同源 |
| Benchmark 对比床 | 固定场景采集 before/after JSON，回归阈值失败关闭 |
| 扩展属性债务 | 接通稠密槽或删除误用路径（子任务） |

### 复用清单（开工前）

- `AttributeRegistry` / `TagRegistry`：名字→稠密编号、可冻结
- `EntityKeyedSoaTable`：实体键 SoA 表模式（行身份、revision）——评估是否直接复用或平行世界列存
- `SystemGroup.SchemaUpdate`：装载后/开局前冻结窗口
- 现有 `GasBenchmarkTests` / `GasBenchmark`：对比床扩展点
- 存档指纹：`SnapshotMappings()` 名字↔编号稳定性

## 3. 详情

### 3.1 容量计划

- 输入：本局已登记属性数 `A`、标签数 `T`（标签可用 id 为 `1..T`，`0` 保留）。
- 输出：`AttributeSlotCount >= A`，`TagIdSpace >= T+1` 且为 64 的倍数（位图按 ulong 字对齐）。
- 策略：精确值或上取整到字边界；禁止静默截断。
- 绝对天花板（建议初始）：属性 ≤ 1024，标签位空间 ≤ 4096；超过则装载失败并指明是内容膨胀而非运行时扩容失败。
- `Freeze` 只能成功一次；之后任何 `Register` 失败关闭。

### 3.2 存储形态（目标态）

- **属性**：世界表持有 `Base/Cap/Current` 列与定义位；实体组件不再携带 `MAX_ATTRS=64` 定长真相。
- **标签**：世界表或与 Plan 字数一致的位图列；所有 `Bits[4]` / `TagDirty[32]` / `KnowledgeIdMask256` / `TagBits256` 升级为「按 Plan 字数」的单一实现或生成布局。
- **热路径**：Chunk/Inline-query 可遍历的行视图；禁止字典查找做每帧属性读写。
- **越界**：非法 attributeId/tagId **失败关闭**，禁止 `return 0` / 当没有（收紧今日部分静默路径）。

### 3.3 迁移阶段

| Phase | 交付 | 退出条件 |
|-------|------|----------|
| P0 | RFC/Epic、CapacityPlan 脚手架、**baseline benchmark 入库** | baseline JSON 可复跑 |
| P1 | 属性世界列存并行（或特性开关），读写与聚合切流 | 属性 UAT + 属性对比床不回归 |
| P2 | 标签世界位列 + GAS 内 Dirty/Snapshot/Effective | 标签 UAT + 标签对比床不回归 |
| P3 | 导航/知识/展示/图标签集对齐同一 Plan | 跨域守卫测试：字数一致 |
| P4 | 删除内嵌定长真相与扩展高位误用；文档回写 gitbook | 无双轨；TDD/架构页更新 |
| P5 | 全量 benchmark after 对照 baseline，写回归说明 | Epic 关闭门槛 |

### 3.4 Benchmark 前后对比（硬门槛）

**对比床场景（名称稳定，禁止漂移）：**

| MetricId | 场景 | 采集项 |
|----------|------|--------|
| `attr.footprint.per_entity` | 1 万实体挂属性存储 | 托管堆增量 / 推算每实体字节 |
| `attr.setw.get.hot` | 1 万实体 × 100 迭代 SetCurrent+GetCurrent | ops/s、线程分配字节、耗时 |
| `attr.aggregate.tick` | 聚合一帧（固定修饰负载） | ms/帧、分配字节 |
| `tag.footprint.per_entity` | 1 万实体挂标签容器 | 每实体字节 |
| `tag.add.has.hot` | 1 万实体 × 100 迭代 AddTag+HasTag | ops/s、分配字节 |
| `tag.dirty.collect` | 稀疏脏标签收集（对齐现有 SparseDirtyTags） | ms、分配字节、访问次数 |
| `gas.pipeline.100k` | 对齐 `GasBenchmark.Run` 量级（可降采样但 before/after 同参） | 总耗时、分配、GC |

**规则：**

1. P0 在**当前内嵌定长实现**上跑出 baseline，提交到 `docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json`。
2. 每个迁移 Phase 产出 `benchmark-after-<phase>.json`（同 schema）。
3. 对比器对每个 MetricId 检查：耗时 / ops 回归超过阈值（默认 10%，可按 metric 覆盖）或热路径新增托管分配 → **失败关闭**，除非 Epic 评论写明原因与新阈值。
4. before/after 必须同一机器类参数：`ENTITY_COUNT`、`ITERATIONS`、容量 Plan（baseline 阶段 Plan=64/256）。

### 3.5 扩展属性

`ExtensionAttributeRegistry` 要么映射进 Plan 稠密槽，要么删除对外误导 API。禁止高位 id 直打实体槽。

## 4. 场景

1. **内容膨胀**：多 Mod 合计登记超过 64 属性或 255 标签，装载成功定容，单位数值与状态判定正确。
2. **打满天花板**：超过绝对天花板，装载失败，错误说明是内容种类过多。
3. **冻结后再登记**：开局后尝试登记新名字 → 失败，对局不继续脏登记。
4. **寻路/迷雾**：高编号标签参与通行与披露时，与 GAS 可见性一致，无「登记了但导航当没有」。
5. **存读档**：按名字映射恢复；不依赖写死数字 id。
6. **性能对照**：同一对比床，迁移后不出现未解释的热路径变慢或新分配。

## 5. 边界

| 边界 | 约定 |
|------|------|
| 扩容窗口 | 仅装载/冻结；对局零扩容 |
| 容量真相 | 唯一：`GasLoadTimeCapacityPlan` |
| 失败模式 | 超天花板、冻结后再登记、越界 id → 失败关闭 |
| 双轨 | 迁移期允许临时开关，P4 前必须拆除 |
| 软上限 | TagCount/TimedTag/Granted 默认不抬 |
| 存档 | 名字 SSOT；组件布局变更走正式序列化合同 |
| 回滚 | 不提供「旧 64 布局静默兼容」；对比床保证性能与正确性 |

## 6. UAT（Cucumber）

```gherkin
Feature: 装载期按内容定容，对局内不再变长

  Scenario: 超过旧 64/255 硬顶的内容仍能开局
    Given 多个 Mod 合计登记了超过 64 个属性名与超过 255 个标签名
    And 种类数未超过绝对天花板
    When 装载完成并冻结容量计划
    Then 对局开始成功
    And 单位模板上声明的高序号属性与标签均可正确读写与判定

  Scenario: 对局中不能再偷偷加长
    Given 容量计划已冻结
    When 运行时代码尝试再登记一个新的属性名或标签名
    Then 操作失败并给出明确错误
    And 已有单位的数值与标签状态保持不变

  Scenario: 超天花板装载失败
    Given 配置尝试登记超过绝对天花板的属性或标签种类
    When 装载容量计划
    Then 装载失败
    And 失败信息说明是种类天花板而非静默截断

  Scenario: 高编号标签在寻路与迷雾中有效
    Given 已定容到足够位宽且存在编号大于 255 的标签
    When 该标签用于通行规则与情报披露
    Then 单位路径与可见信息与配置一致
    And 不存在“GAS 认得、导航/迷雾当没有”的分叉

  Scenario: 性能对比床不回归
    Given 已提交的 baseline 对比床结果
    When 在同一场景参数下跑迁移后对比床
    Then 各 MetricId 未超过约定回归阈值
    And 热路径对比项不新增托管分配
```

## 7. 文档与验收出口

- Epic 关闭前：正式结论回写 `gitbook/architecture/`（GAS 定容合同、标签位图、知识掩码）与 TDD-06 容量表述。
- RFC 本文件在接受前只作讨论与开工 SSOT；接受后按 `docs/rfcs/README.md` 规则回写正式文档。
