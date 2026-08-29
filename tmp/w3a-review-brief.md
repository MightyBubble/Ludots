# W3a #1327 复核请求：资产句柄与租约——RaylibAssetStore 统一四个缓存

分支 codex/eng3a-asset-store，看 git diff HEAD~1..HEAD。核心新文件 `src/Client/Ludots.Raylib.Render/Rendering/RaylibAssetStore.cs`；四个消费者迁移：RaylibPrimitiveRenderer（模型/贴图缓存）、RaylibVfxRenderer、RaylibMaterialLibrary、RaylibGpuSkinnedModelCache；FrameRenderer pass 序列后 FlushRetiredAssets；8 个无 GL 单测 RaylibAssetStoreTests。

## 设计（请审查而非复述）

1. **存储**：`RaylibAssetStore<T>` 按 (URI) 去重（每类一个实例：贴图/模型），租约 Lease=引用计数；引用归零→退役队列，`FlushRetired()`（帧执行者每帧末调用）真正销毁——本帧仍在绘制的引用不会被释放；退役期内重新 Acquire 复活不重载。负缓存：Failed 条目记 mtime^length 版本，下一次 Acquire 版本变化自动重试（文件补齐/内容变化同进程重入，不须重启）；另有显式 Invalidate。链式 Acquire 逐 URI 尝试、全败抛聚合。状态机 Unrequested→Preparing→Resident/Failed，CpuReady/UploadQueued 留给 #1328 异步。装载/销毁委托注入（可无 GL 单测）。锁覆盖全装载路径（冷路径）。
2. **错误策略统一**（W2 合同落地）：模型/贴图"配置了 URI 但装载失败"从 warn+跳过收敛为 fail-loud；未配置 URI 仍返回不绘制（非错误）。蒙皮缓存移除永久负缓存条目（对齐可重试语义）。
3. **Dispose 顺序**：渲染器 Dispose 中模型/贴图只释放租约（退役），存储 Dispose 放在 Dispose 最末（蒙皮缓存/材质库/VFX 的释放回调如 DetachOwnedMaps 仍需触碰资源，先销毁会悬空）；模型先于贴图销毁保持旧序。
4. **生命周期归属**：贴图归存储后，MaterialLibrary.UnloadOwned 变为空操作（租约在库 Dispose 释放）；PrimitiveRenderer 拥有两存储并注入 VfxRenderer/MaterialLibrary/GpuSkinnedModelCache（各自有无存储的独立构造回退）。

## 已有证据

- 单测 8/8（去重单次装载/双租约延迟销毁/退役复活/负缓存版本保持与重试/文件补齐重入/链回退聚合/Dispose 恰好一次/Invalidate）；adapter 全量 215/215。
- 画廊四场景（含 crowd_anim 蒙皮路径）native 账本平衡 unknownUntrack=0；launcher 双 mod 冒烟正常。
- 截图自差分：blacksmith max=2（等价）；atmosphere 6.13% 差异与既往波浪相位噪声签名一致（视觉双审核进行中）。

## 请重点审查

1. **生命周期正确性**：a) FlushRetired 在帧末由 FrameRenderer 调用——若某帧异常路径提前退出（finally 只做 Abort/EndDrawing），退役资源会跨帧累积，有无泄漏风险？b) 渲染器 Dispose 顺序（先释放全部租约→存储最后 Dispose）与蒙皮缓存 UnloadAll 的 Detach 回调时序；c) MaterialLibrary 独立构造（自建存储）与共享存储两条路径的行为等价性。
2. **行为变更面**：fail-loud 收敛对现有展示面/测试的影响是否已全覆盖（有没有测试断言旧的 warn+跳过语义）；"未配置 URI 返回 false"与"配置了但失败抛出"的边界是否有遗漏路径（如 SourceUris 全空白）。
3. **存储并发与复活语义**：retired 列表与 entries 字典的一致性（Revive 移除/Flush 遍历删除/Dispose 清空的交互）；Release 在 Destroyed 后的防御。
4. **单测覆盖缺口**。

输出：阻断/应改/通过三档，每条 file:line。用中文。
