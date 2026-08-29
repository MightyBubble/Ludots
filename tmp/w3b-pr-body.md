Part of #1321 · 实现 #1328（W3b 两阶段异步资产装载）

**stacked on #1375（含前序全部提交，合并后本 PR diff 即为纯 W3b）**

## 改了什么

- **两阶段装载**：`RaylibAssetStore` 增加 `TryAcquireOrBegin`（`Resident / InFlight / Failed` 三态）。未命中时 kick worker 执行 **CPU 相**（贴图=文件解码→`Image`；模型=Assimp/FBX→GLB 转换），状态机 `Preparing→CpuReady→UploadQueued→Resident` 走满（#1327 预留的异步态启用）；**GL 上传相** 由 `PumpUploads` 在渲染线程每帧执行（Draw/DrawShadow/合批桶全部入口帧首接线）。
- **InFlight 语义**：本帧不绘制、不记负缓存、下一帧重问；全部 URI 失败仍聚合 fail-loud（W2 合同不变）。billboard 与 billboard-shadow 对 InFlight 跳帧不抛。
- **同步 API 统一**：`TryAcquire` 与异步路径共用状态机——遇 InFlight 等待 worker 并在本线程完成上传（蒙皮/材质/VFX 的同步合同保持，消除双装载窗口）。
- **bootstrap/验收通道**：`LUDOTS_RAYLIB_SYNC_ASSET_LOAD=1` 强制纯同步；半配置异步委托 fail-loud 拒绝。
- **生命周期补强**：Dispose 等待全部 worker 并清理未上传的 CPU payload（native `Image` 防泄漏）；`RetryCount` 语义对齐为每次尝试一次；vendored 绑定缺失的文件级 `LoadImage` 经门面 DllImport 补齐（cubemap 先例）。

## 验证证据

- 7 个异步单测 + adapter 全量 **225/225**；画廊 crowd_anim/lighting native 账本平衡；launcher 双 mod 冒烟正常。
- **pi 视觉像素审核判等价**：blacksmith 零差异级（max=2，100% ≤2）；atmosphere 差异被证明为水面带零均值高频噪声（地形内部 ≥10 占比 **0%**、差异块均 1.4px、**无资产空洞**〔内容-黑色双向偏移 0 像素〕、相机零位移）——异步装载没有造成任何元素缺失。

## 复核记录

- **codex（gpt-5.6-sol）**：3 阻断 + 3 应改，**全部成立并全部修复**——①旧 TryAcquire 与异步路径双装载窗口（InFlight 条目可被同步覆盖→资源泄漏）→ 统一状态机；②billboard 把 InFlight 当缺失抛异常中断渲染 → 跳帧；③合批桶/蒙皮阴影入口缺泵 → 全部接线；④Dispose 不等 worker、CPU payload 泄漏 → 等待+payloadDisposer；⑤RetryCount 双计数；⑥半配置静默退化 → fail-loud。另修我方测试的一处无效断言（负缓存 kick 计数挂错 store）。
- **pi（claude opus）**：视觉定量复核交付（见上）。

Closes #1328
