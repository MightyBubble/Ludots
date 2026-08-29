Part of #1321 · 实现 #1324（W1b HostLoop 拆分：输入路由模块独立）

**stacked on #1359（含 W0/W1a/W2 提交，前序 PR 合并后本 PR diff 即为纯 W1b）**

## 改了什么

- **`RaylibHostInputRouter`**（新文件）自 `RaylibHostLoop` 拆出：真实鼠标/键盘与 synthetic 回放经同一输入合同（`UpdateInput` 单入口），UI 捕获语义（`UiCaptured` / `UiWheelCaptured` / `Handled`）与逐行行为不变；`frameIndex` 与诊断路径由宿主显式传入——回放时序与真实输入共享同一基准，不错帧。
- **static 可变状态清零**：指针捕获/按钮/移动缓存收敛为路由器实例字段；`SyntheticUiPlayback` 与其环境变量读取随迁。
- **`RaylibAdapterEnv`**（新文件，复核应改项）：环境读取与诊断追加的公共工具；路由器不再反向依赖 `RaylibHostLoop`，宿主保留薄转发兼容既有调用点。
- `RaylibHostLoop` **-620 行**；`RaylibUiCaptureRoutingTests` 改指 `RaylibHostInputRouter.ShouldCaptureWorldPointer`，5 个语义断言原样（codex 确认未削弱）。

## 验证证据

- adapter 全量 203/203。
- **三对截图双视觉审核均判等价**：pi 像素级差分——presenter 与回放对 **max diff=2**（纯舍入噪声）、atmosphere 差异被证明为零均值高频波浪噪声（3×3 模糊后 >8 差异从 11.6% 塌到 0.89%，去噪后陆地轮廓 IoU 0.9925、零位移）；codex 构成核对 + ffmpeg SSIM 0.9946 / PSNR 54.3。
- **回放事件级验证**：诊断日志显示 down@frame180 (190,205) captured=True → up@frame260 (310,270)，帧号坐标与合成回放合同分毫不差；`uiCaptured=True` 正确流入引擎输入选择诊断。（注：该 mod 面板在拖拽下不位移，两侧行为一致，属 mod 特性非路由问题。）

## 复核记录

- **codex（gpt-5.6-sol）**：无阻断；逐行确认 `UpdateInput` 及全部辅助（捕获/释放/失焦/滚轮方向/键盘转发/回放插值）无语义漂移。1 应改（路由器反向依赖宿主诊断/环境工具）→ 已落地为 `RaylibAdapterEnv`。实例隔离测试建议 → `UpdateInput` 与 raylib 输入 API 直接耦合无法 headless 驱动，隔离性由单实例宿主装配 + 回放事件级证据覆盖，已记录。
- **pi（claude opus）**：视觉像素级复核交付（见上）；代码级复核两次尝试分别上游 400/超时，未交付——pi 对多文件代码读取任务在本会话持续不稳定（图像与微任务正常），如实记录。

Closes #1324
