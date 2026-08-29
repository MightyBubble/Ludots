Part of #1321（W4 推广项，依赖 spike 结论）

## 目标

持久实例 buffer 从单 lane spike 推广到 typed instanced lane 与 ISM 车道；删除 `fixed` 指针上传路径（`RaylibPrimitiveRenderer.cs:2337` 一类）。

## 验收标准

- Given typed lane / ISM 车道；When 迁移到持久 buffer 增量更新；Then `DrawMeshInstanced` 不再从 CPU 裸指针接收矩阵；instanced 契约测试（InstancedBatchContractTests）全部通过。
- 迁移前后 5 万实例基准对比入报告；渲染输出一致（截图对比）。

## 依赖

前置：#1329 spike 通过。
