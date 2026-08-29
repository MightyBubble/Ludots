Part of #1321（W5，只收历史欠账，收完即关）

## 目标

修正本 epic 开始前已存在的代码-文档漂移；此后文档变更并入对应工作流的验收项，不长期单独漂浮。

## 任务清单

- `gitbook/architecture/runtime-overview.md` SystemGroup 相位数补齐到 `ArchitectureGuardTests.cs` 锁定的 11 相（缺 `RuntimeEntityBinding` 与 `Continuation`）。
- 复查其余 epic「现状关键事实」第 7 条所列漂移在 #1326 完成后的残留，一并收口。

## 验收标准

- Given `ArchitectureGuardTests` 锁定的相位序列；When 文档更新；Then 相位列表逐项一致。
- 清扫后 `gitbook/architecture/` 下 raylib 渲染相关文档无与本 epic 现状事实冲突的表述。

## 依赖

收尾于 #1326 之后。
