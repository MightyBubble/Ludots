Part of #1321 · 实现 #1322（W0 计量守卫，全 epic 前置小包）

## 改了什么

- **门面 `RaylibNativeResources`**（新）：raylib 原生资源 Load/Unload 的唯一生产入口。生产代码（Raylib.Render / Client.Raylib / Adapter.Raylib / Apps/Raylib 四根）禁止直连 `Rl.*` 的加载卸载，含三种限定形式（`Rl.` / `Raylib.` / `Raylib_cs.Raylib.`）与 cubemap 的 `rlLoadTextureCubemap` DllImport（自 `RaylibSkyIbl` 迁入并删除本地 interop）。40 个调用点全部改走门面。
- **台账 `RaylibNativeResourceLedger`**（新）：按 `(kind, identity)` 登记驻留字节与计数。身份键：纹理/着色器/RT 用 GL 名、网格用 VAO 名、模型用 meshes 指针、材质用 maps 指针、声音用 buffer 指针。身份为 0（失败加载哨兵）不入账；失配以计数器暴露（Retracked / UnknownUntrack），不改变渲染行为。
- **源码契约测试**：空白容忍正则封堵直连 + 括号配对提取方法体后断言每个包装方法内有记账调用。
- **基准阈值断言**：50k HUD alloc < 512 B/帧（基线 382.7 B）、skia hotpath 三场景 < 64 B/帧（基线 0.0 B）——超阈值 CI 失败，不再只写报告。
- **画廊 `RunScene` 退出一行账本快照**（可查询面）；**真实音频设备测试**实证 SoundAlias 身份唯一。

## 验证证据

- 新测试 34/34 通过（账本语义 4 + bpp 表参数化 26 + 契约 2 + 声音别名 1 + 原声音设备 1）；RaylibAdapterTests 全量 173/173。
- 两个带阈值断言的 benchmark 测试通过。
- 四个 GL 场景（primitives / crowd_anim / lighting / skia_overlay）截图正常、退出时 tracked==untracked、unknownUntrack=0。3D 场景残留 1 项 Texture 5.76MB = `GalleryFont` 共享屏幕尺寸 Skia 合成纹理（进程生命周期资源，无销毁路径，native 随进程释放）——如实驻留，非泄漏。

## 双复核记录

**codex（gpt-5.6-sol）**：两个阻断（失败加载入账制造假驻留；契约测试可被全限定名绕过——`RaylibSkiaRenderer` 实际绕过被当场抓到并修复）+ 应改（模型/别名身份、bpp 参数化）——已全部处置；其对 bpp 表的独立核对与本 PR 的 raylib.h 5.5 官方源核对一致。
**pi（claude opus）**：采纳邻接检查收敛到方法体；驳回两项有反证：bpp 表（pi 自述按记忆且无法联网，官方源逐项核实无误）、SoundAlias 塌缩（真实设备测试证明两别名各自入账、retrack=0）。

## 顺带发现（已登记，不在本 PR 处理）

- `RaylibPrimitiveRenderer` 基础网格 GenMesh 后补色再 `UploadMesh` 的双重上传（retracked=3 如实暴露；是否造成 GL buffer 泄漏待 raylib 源码确认）→ 记入 W3/W4。
- 范围外种类（Image / ModelAnimations / DroppedFiles，CPU 侧）不进 GPU 台账。

## 已知工作树状态说明

本分支只包含 W0 的 40 个文件（diff 逐文件校验只含门面替换行）。工作树另有先于本 PR 的未提交在途改动（Activity/PanelKit/GraphOps 一线），与本分支无文件交集；PresentationTests 全量在该在途状态下有 ~20 个失败，经错误内容归因均属在途改动（错误码合同变更、mod DLL 缺失等），与 W0 无关。

Closes #1322
