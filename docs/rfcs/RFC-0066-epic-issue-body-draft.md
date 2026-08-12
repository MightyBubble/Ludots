<!-- 建议 Issue 标题： -->
<!-- [Epic] GAS 装载期定容世界级存储：属性/标签跟内容定容，对局合同不变（RFC-0066） -->

## 一句话

让属性种类、标签种类**跟着本局内容在装载期自动定容**，对局里仍然定长、零分配、禁止热路径扩容；用世界级 SoA 列存替换实体内嵌 64/256 硬顶，并以 **benchmark 前后对比** 作为合入硬门槛。

## 设计 SSOT

- RFC 正本：`docs/rfcs/RFC-0066-gas-loadtime-capacity-world-store.md`
- Epic 粘贴稿：本文（issue 建立后可删或改链到 issue）
- Baseline 对比床：`docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json`
- **不是**：对局中自动扩容；**不是** 只改 `MaxAttributes=128`；**不是** 扩展属性 10001 旁路

## 愿景管线

```text
Mod 登记名字
  → Freeze: GasLoadTimeCapacityPlan（本局唯一容量真相）
  → 世界级属性列存 / 标签位列（按 Plan 分配一次）
  → Dirty / Snapshot / 导航 TagBits / 知识掩码 / 展示表 同源
  → 对局：只读写，零扩容；越界与再登记失败关闭
  → 每 Phase：benchmark-after 对照 baseline，回归失败关闭
```

## 铁律

1. 扩容窗口只有装载/冻结；对局、热路径、效果管线内禁止加长。
2. 容量真相唯一：`GasLoadTimeCapacityPlan`；禁止 GAS 与导航/知识各写一套 256。
3. 禁止双轨长期并存（内嵌 64 + 溢出表）；迁移期开关必须有拆除 Phase。
4. 非法 id / 冻结后再登记 / 超绝对天花板 → 失败关闭，禁止当 0、当没有。
5. 内容只认名字；存档不依赖写死数字 id。
6. **Benchmark 前后对比不过 = 不能合主线**（未解释回归一律打回）。

## 阶段（子单可按此拆）

| Phase | 内容 | 退出 |
|-------|------|------|
| P0 | RFC/Epic、CapacityPlan 脚手架、**baseline 入库** | 本 PR |
| P1 | 属性世界列存切流 | 属性 UAT + 属性 metric 不回归 |
| P2 | 标签世界位列 + GAS Dirty/Snapshot | 标签 UAT + 标签 metric 不回归 |
| P3 | 导航 / 知识 / 展示 / 图标签集对齐 Plan | 跨域字数守卫绿 |
| P4 | 拆内嵌定长真相；扩展属性接通或删除 | 无双轨 |
| P5 | 全量 after 对照 + 文档回写 gitbook/TDD | Epic 关闭 |

## Benchmark 对比床（硬门槛）

固定 MetricId（详见 RFC §3.4）：

- `attr.footprint.per_entity`
- `attr.setw.get.hot`
- `attr.aggregate.tick`
- `tag.footprint.per_entity`
- `tag.add.has.hot`
- `tag.dirty.collect`
- `gas.pipeline.100k`

规则：同参复跑；默认耗时/ops 回归阈值 10%；热路径新增托管分配失败关闭；改阈值必须在 Epic 留痕。

## BDD 验收（玩家 / 内容作者视角）

```gherkin
Feature: 装载期按内容定容，对局内不再变长

  Scenario: 超过旧硬顶的内容仍能开局
    Given 多个 Mod 合计登记了超过 64 个属性名与超过 255 个标签名
    And 种类数未超过绝对天花板
    When 装载完成并冻结容量计划
    Then 对局开始成功
    And 单位上的高序号属性与标签均可正确读写与判定

  Scenario: 对局中不能再偷偷加长
    Given 容量计划已冻结
    When 尝试再登记新的属性名或标签名
    Then 操作失败并给出明确错误

  Scenario: 超天花板装载失败
    Given 配置超过绝对天花板
    When 装载容量计划
    Then 装载失败且说明是种类天花板

  Scenario: 高编号标签在寻路与迷雾中有效
    Given 编号大于 255 的标签用于通行与披露
    When 玩家下令移动并观察情报
    Then 路径与可见信息正确
    And 不出现 GAS 与导航/迷雾分叉

  Scenario: 性能对比床不回归
    Given 已提交的 baseline
    When 跑迁移后同参对比床
    Then 各 MetricId 未超阈值且热路径无新托管分配
```

## 明确不做

- 帧中/对局中自动扩容
- 实体内嵌槽满了再挂旁路表当正式方案
- 用 ExtensionAttribute 高位 id 假装扩容
- 本 Epic 默认不同步放大 TagCount/TimedTag/Granted 软上限

## 关联债务（报告，不顺手改别人的）

- `AttributeBuffer` 部分越界路径静默返回 0 —— 迁移时应收紧为失败关闭
- `ExtensionAttributeRegistry` 未接通实体槽 —— P4 处理
- 标签宇宙副本命名含 `256`（`TagBits256`、`KnowledgeIdMask256`）—— P3 与 Plan 对齐并去烙印
