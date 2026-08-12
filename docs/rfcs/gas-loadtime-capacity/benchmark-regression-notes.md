# RFC-0066 P1 benchmark 回归说明（人话）

对照：`benchmark-baseline.json`（P0 实体内嵌 float[64]） vs `benchmark-after-p1.json`（世界列存 + 行句柄）。

## 接受的偏移（有意、非静默忽略）

| MetricId | 现象 | 原因 | 是否挡合入 |
|----------|------|------|------------|
| `attr.struct_sizeof.bundle` | ~1080 → ~4268 bytes | `AttributeLastSnapshot` / `GameplayAttributeChangedBits` 按绝对天花板 1024 定长；`DirtyFlags` 属性脏位改为 16×ulong。值仍在世界 SoA，组件只扛脏/快照合同。 | 否（P1 设计取舍；P4 可再压到 Plan 生成布局） |
| `attr.footprint.per_entity` | 明显上升 | 同上：Arch 组件里带着 1024 槽快照/脏位，托管堆按实体组件计量会变胖；列存本身在会话级预分配，不进该「每实体增量」口径。 | 否（与上同因） |
| `tag.footprint.per_entity` | 小幅上升 | 标签实体也挂 `DirtyFlags`，属性脏字扩宽后同组件变大（标签位仍 P2）。 | 否（连带；P2 拆标签脏布局时再收） |
| `tag.dirty.collect` alloc | 略增 | 同上：`DirtyFlags` 更大，收集路径拷贝/缓冲略增；不是标签语义变慢的主因。 | 否（连带；阈值上允许小幅 alloc，见下） |
| `attr.setw.get.hot` | ops 约 -34%（本机 after-p1） | 热路径从「实体内 fixed 缓冲」改为「行句柄 → 会话 SoA」+ 失败关闭边界检查；多一次间接。 | **关注**：P1 接受 ≤40% 作为切流成本；热路径 **不得新增托管分配**（alloc 与 baseline 同为 40） |
| `tag.add.has.hot` | 偶发略低于 10% 阈值 | 标签存储未改（P2）；同参复跑抖动/机器负载，非属性列存主因。 | 否（P1 不改标签热路径；P2 再对照） |

## 硬门槛（仍失败关闭）

- 任意 `*.hot` / 聚合度量：`AllocatedBytes` 相对 baseline **只许不增**（除上表已点名的 `tag.dirty.collect` 因 DirtyFlags 扩宽的小幅连带）。
- 未写入本文件的 MetricId 回归：不得合入。

## 后续

- P2：标签列存时把 `DirtyFlags` 标签半区与属性脏字按 Plan 收紧或拆组件，压回 tag footprint/alloc。
- P4：快照/脏位改为 Plan 定长或迁入世界列，去掉绝对天花板组件膨胀。
