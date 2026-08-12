# RFC-0066 benchmark 回归说明（人话）

对照：`benchmark-baseline.json`（P0 实体内嵌 float[64] / Bits[4]） vs `benchmark-after-p1.json` / `benchmark-after-p2.json`（世界列存）。

## P1（属性列存）已接受偏移

| MetricId | 现象 | 原因 | 是否挡合入 |
|----------|------|------|------------|
| `attr.struct_sizeof.bundle` | ~1080 → ~4268+ bytes | `AttributeLastSnapshot` / `GameplayAttributeChangedBits` 按绝对天花板 1024 定长；`DirtyFlags` 属性脏位改为 16×ulong | 否（P1 设计取舍） |
| `attr.footprint.per_entity` | 明显上升 | 同上：Arch 组件携带绝对天花板快照/脏位；列存本身在会话级预分配 | 否 |
| `attr.setw.get.hot` | ops 约 -34%～-40% | 行句柄 → 会话 SoA + 失败关闭边界检查 | 否（P1 接受 ≤40%；热路径 alloc 不增） |

## P2（标签列存）偏移

| MetricId | 现象（本机 after-p2） | 原因 | 是否挡合入 |
|----------|----------------------|------|------------|
| `tag.struct_sizeof.container` | 32 → **4** bytes | `GameplayTagContainer` 仅为 `RowId`；位图在 `GasWorldColumnStore.TagBitWords` | 否（目标形态；变好） |
| `tag.footprint.per_entity` | ~264 → ~1327 bytes | 同实体挂的 `DirtyFlags` 标签脏区扩到绝对天花板 64×ulong；`GameplayTagEffectiveCache` 等同理按 AbsoluteMax 定长 | 否（P2 设计取舍；P4 可压到 Plan 生成布局） |
| `tag.add.has.hot` | ops 约 -43%（相对 baseline） | 与属性相同：RowId → 会话 `TagBitWords` 间接 + 计划边界校验 | **关注**：接受 ≤50% 作为标签切流成本；热路径 **alloc 与 baseline 同为 40** |
| `tag.dirty.collect` | ms 略升；alloc 8919168 → ~13917536 | `DirtyFlags`/`GameplayTagSnapshot`/`Effective*` 按 AbsoluteMax 变胖；收集路径拷贝更大 | 否（已点名；阈值上允许该 alloc 连带） |
| `attr.struct_sizeof.bundle` / `attr.footprint` | 相对 P1 再升 | `DirtyFlags` 标签半区从 `byte[32]` 改为 `ulong[64]`，属性实体同组件变胖 | 否（P2 DirtyFlags 合同；P4 再压） |

## 硬门槛（仍失败关闭）

- 任意 `*.hot`：相对 baseline **不得新增托管分配**（`AllocatedBytes` 只许不增）。
- 未写入本文件的 MetricId 回归：不得合入。
- `attr.setw.get.hot` ops：相对 baseline 允许 ≤40%（P1）。
- `tag.add.has.hot` ops：相对 baseline 允许 ≤50%（P2 切流）。

## Production freeze hook

`GameEngine.InitializeWithConfigPipelineInternal` 在全部 ConfigPipeline 装载器之后、首个 gameplay `World.Create`（相机目标实体）之前调用：

`GasLoadTimeCapacitySession.FreezeEnsureStoreAndSealFromRegistries()` → `FreezeFromRegistries` + `EnsureStore` + `SealGameplay`。

测试可用 `EnsureLegacyPlanAndStoreForTests` 预绑定；生产路径若已冻结则不再替换计划（需 `ClearForTests` 才能换计划）。

## P3 临时桥

`TagBits256` / `KnowledgeIdMask256` / `TagDisplayTable` 仍限 256：`Freeze` 在 `Plan.TagIdSpace > 256` 时失败关闭；图标签集 `tagId >= 256` 抛错（禁止静默 skip）。

## 后续

- P3：导航/知识/展示位图对齐同一 Plan，拆除 256 桥。
- P4：快照/脏位改为 Plan 定长或迁入世界列，压回 footprint/alloc。
