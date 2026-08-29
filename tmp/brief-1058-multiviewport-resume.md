# 任务书：#1058 呈现侧收口（真机多视口绘制 + per-binding 剔除/拾取）——恢复运行版

工作树：C:\001_AI\LudotsProd\.worktrees\pi-1058-multiviewport（分支 codex/1058-multi-viewport-drawing，内容等价 main：a4fa091027 + #1302 + #1303）

## 现场状态（2026-08-28 16:55 快照提交）
进行中改动已快照为 wip commit（含：CameraCullingSystem.cs 修改 + 新文件 src/Core/Systems/PresentBindingCullPass.cs）。续做不要推倒。

## 任务（gh issue view 1058 及其进度评论：PR #1302 留下的过渡限制就是本切片）
1. **逐 binding 驱动序列沉淀 Core 侧**（owner 指令）：迭代 bindings、rebind projector/ray/culling、触发剔除、取 presenter 插值——宿主无关的 Core 驱动（照 #1302 PresentBindingPresentation 静态原语/回调/枚举形态）；Raylib/Web 只实现「把一个 binding 的相机姿态 + rect + 插值画到窗口」的宿主回调。禁止驱动循环写死在 RaylibHostLoop——UE5/Unity 是下游宿主，接的就是这个 Core 合同（先例：agent-debug-bridge 的 IHostFrameCapture/SyntheticInputDevice 端口模式）。
2. 多视口绘制：双 seat + horizontal-equal-split → 左右两半各自渲染各自 binding 的 LogicView 相机（Raylib 为主：scissor/viewport 或 render texture，勘察既有绘制结构后自选；Web 对等跟进）。
3. per-binding culling 多路复用：共享 CullState 的 **union 语义**（任一 binding 视口内即可见即画；每 binding 剔除计算独立；禁止合成唯一全局可见集当真相）。拾取按 rect 路由到对应 binding 的 ray。
4. sole seat 零回归（与 #1302 合入后行为一致）。
5. 测试：headless 引擎层（多 binding 各自剔除/拾取、并集语义、rect 拾取路由）；真机视觉验收留 #1058 整单（主线做）。

## 边界
不碰输入侧（per-seat handler / AuthoritativeInput / scheme 激活 / ClientLocalSeatDeviceBinding）；RaylibHostLoop 输入区段只读；不碰 InteractionMode/投影（另一切片）；UI 三轴不做；存档不碰（禁则④守卫在）。
布局切换水平/垂直声明驱动无代码分支（#1302 底座已有，别引入分支）。

## 工程注意
全量测试污染 artifacts/（留底/恢复/只提交源文件）；ArchitectureTests 5 个存量失败不追；trx logger；引用存在性自检；push 重试；开工前读 gitbook/contributing/ai-assisted-development.md。
PR（base main）：概要 / Raylib 视口切分方案取舍 / Core 驱动合同边界说明 / 与输入切片的潜在冲突点 / 复用清单 / 验证证据，Refs #1058。禁止合并。
