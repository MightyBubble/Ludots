Part of #1321（W4 剔除项）

## 目标

lane 内逐实例 AABB frustum culling：消费现有 `CullState`（`CameraCullingSystem.cs:643-665`、`:1504-1532`），不改写 Core 实体可见性，不造第二套实体级 culling SSOT。

## 验收标准

- Given lane 内部分实例位于相机 frustum 外；When 构建本帧提交；Then 只提交可见实例；提交数量与诊断计数一致。
- Given Core 已写入实体 `CullState`；When 渲染侧剔除；Then 只消费不重算实体级可见性。
- 基准：大规模场景（如 5 万实例、相机朝向局部）前后 draw/上传量对比入报告。

## 依赖

无硬前置；与 #<ENG-4a>/<ENG-4b> 无顺序依赖。
