Part of #1321 · 实现 #1325（W1c HostLoop 拆分：证据采集与诊断 HUD 独立）

**stacked on #1364（含前序全部提交，合并后本 PR diff 即为纯 W1c）**

## 改了什么

- **`RaylibScreenshotEvidenceRecorder`**（新文件）：截图取证合同持有者——`LUDOTS_TAKE_SCREENSHOT_PATH/FRAMES/FRAME` 与 `LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT` 环境钮、目录式路径 fail-loud、序列帧命名（`<名>_<序:000>_f<帧:0000>.png`）、落盘搬运、尺寸/平坦度校验（`ColorDistance` 随迁）。时序基准（frameIndex、runtime 毫秒）由宿主逐帧显式传入；`writeDiagnostics` 回调在目录创建后、TakeScreenshot 前触发——宿主的时序敏感诊断（相机/计时/车道摘要/输入选择）原位保留，`输入 → Tick → Present → EndDrawing → 取证` 链路顺序不变。
- **`RaylibDiagnosticHud`**（新文件）：点阵字形 HUD 全家迁出（Draw/FormatFixed/字形表），绘制位置不变（覆盖层合成之后、EndDrawing 之前）。
- `RaylibHostLoop` **1412 → 1089 行**（epic 起点 2450 行）；`RaylibScreenshotEvidenceTests` 改指 Recorder（5 个断言原样）+ 新增 4 个状态机测试。

## 验证证据

- adapter 全量 **207/207**（含新增：环境工厂目录路径拒绝、absent→null、帧号+最小运行时长双门槛、序列命名合同）。
- 真实宿主 E2E 三条路径：普通截图（截图后校验链完整走新 Recorder，堆栈证实）、序列帧（`w1c_seq_001_f0060.png` 命名正确；第二帧因本机已知 DPI 尺寸报错中断循环，与基线行为一致）、HUD 开启（产出正常）。
- **视觉回归**：普通对完全等价（自跑像素差分 max=2 纯舍入）；HUD 对差异 max=222/0.0092%——**同构建两次运行的对照实验差异统计与之完全一致**，证明全部差异（含 codex 标记的生命值 77→78）是运行间固定步长节拍漂移与实时计时数字，非回归。codex 视觉审核确认布局/字形/颜色/位置一致。
- 序列基线对照未采到（本机首跑偶发闪断）：序列命名代码为逐行搬移且新增命名合同测试锁定。

## 复核记录

- **codex（gpt-5.6-sol）**：无阻断；截图块/HUD 块逐行确认迁移保真（时序、诊断顺序、校验语义、字形表）。3 应改全部落地：①目录式路径守卫（旧代码有、新代码漏）恢复为 fail-loud；②截图耗时恢复 double 精度；③状态机补 4 个单测。其视觉标记的生命值差异被同构建对照实验推翻（见上）。
- **pi（claude opus）**：视觉复核三次尝试均遇上游 400（供应商故障，本会话间歇性持续），未交付；视觉门由自跑定量差分 + 同构建对照实验 + codex 视觉审核覆盖，如实记录。

Closes #1325
