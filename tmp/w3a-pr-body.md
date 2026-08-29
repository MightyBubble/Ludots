Part of #1321 · 实现 #1327（W3a 资产句柄与租约）

**stacked on #1369（含前序全部提交，合并后本 PR diff 即为纯 W3a）**

## 改了什么

- **`RaylibAssetStore<T>`**（新）：按 URI 去重的原生资源存储，四个缓存（PrimitiveRenderer 模型/贴图、VfxRenderer、MaterialLibrary、GpuSkinnedModelCache）统一接入——**同 URI 跨渲染器只驻留一份物理 GPU 资源**（此前三份）。
  - **句柄租约 + 引用计数**：引用归零进退役队列，`FlushRetiredAssets()` 由唯一帧执行者在每帧末调用真正销毁（帧内仍在绘制的引用不会被释放；退役期内重新租用复活且不重载；异常帧的 finally 同样冲刷，持续异常不累积）。
  - **负缓存版本探测重试**：失败条目记录 mtime^length 版本；文件补齐或内容变化后下一次 Acquire 自动重试（同进程重入，不须重启）——此前的负缓存是进程生命周期永久的。另有显式 `Invalidate`。失败原因/尝试次数/状态经 `TryGetState` 可诊断。
  - **状态机**：`Unrequested→Preparing→Resident/Failed`（`CpuReady/UploadQueued` 留给 #1328 异步两阶段）；链式 Acquire 逐 URI 尝试、全败抛聚合；装载/销毁委托注入（无 GL 可测）；Dispose/Acquire 竞态全程锁内。
- **错误策略统一**（W2 合同落地）：配置了 URI 但装载失败从 warn+跳过收敛为 fail-loud；未配置 URI 仍返回不绘制。蒙皮缓存的永久负缓存条目移除（对齐可重试语义）。
- **生命周期**：存储销毁置于渲染器 Dispose 最末（蒙皮/材质库/VFX 释放回调仍需触碰资源）；材质库与蒙皮缓存的独立构造路径显式拥有并销毁自建存储；材质部分绑定失败回收已取得租约。

## 验证证据

- **11 个无 GL 测试**（存储 8 + 消费者级 3）+ adapter 全量 **218/218**。
- 画廊四场景 + 修复后蒙皮场景 native 账本全部平衡（`unknownUntrack=0`，tracked==untracked）。
- launcher 双 mod 冒烟截图正常；**视觉双审核均判等价**——pi 定量：blacksmith **max=2 零差异级**；atmosphere 差异被证明为水面带零均值高频噪声（高斯 σ=2 后 MSE 25.75→0.52，低频残差占比 7:1），岛屿与天空零差。codex 结构核对同判等价。

## 复核记录

- **codex（gpt-5.6-sol）**：4 阻断 + 3 应改，**全部成立并全部修复**——①Dispose/Acquire 竞态（_disposed 移入锁内）；②材质库独立构造路径泄漏自建存储（owns 标记+Dispose 销毁）；③材质部分绑定失败累积租约（catch 回收）；④蒙皮缓存独立存储同样泄漏（同②）；⑤PrepareNativeLoadable 破坏链式回退（转换失败淘汰当前 URI 继续）；⑥异常帧不冲刷退役队列（finally 冲刷）；⑦消费者级测试缺口（补 3 个）。
- **pi（claude opus）**：视觉定量复核交付（见上）；代码复核未安排（本会话 pi 代码读取通道持续 400/超时，分工定为 codex 代码 + pi 视觉，已在 W1b/W1c PR 记录）。

Closes #1327
