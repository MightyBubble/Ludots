# Web Adapter 边界协议首刀：wire Rotation 上链路 + DeltaCompressor 接线

- 分支：`codex/in-app-launcher-shell`（基线 origin/main @ b5ff09490d）
- 日期：2026-08-23
- 背景：web app 架构评审结论——服务端权威 + 呈现层穿越的拓扑正确，死于三个实现伤口
  （全量帧 / 无背压 / 相机服务端权威）与 wire 格式自断前程（无 Rotation）。本切片修前两处。

## 变更

1. **WirePrimitiveDrawItem 44B → 60B**：新增 Quaternion Rotation（x/y/z/w @ +44..+56）。
   服务端 `BinaryFrameEncoder`（静态 + skinned 两条 lane）与 `DeltaCompressor` 同步；客户端
   `FrameDecoder.ts` 全量/增量两条解码路径、`PositionInterpolator`（透传不插值）、
   `EntityManager`（`mesh.quaternion.set`）全链上旋转。
2. **DeltaCompressor 从死代码转正**：`PresentationExtractor` 每帧产出 full + delta 双编码；
   delta 每帧无条件推进 prev 快照（帧链连续性），仅在 (a) 无可见 skinned lane、(b) delta 小于
   full 时提供 delta 载荷。
3. **传输层按客户端帧链选帧**：`ClientSession` 记录 `_lastSentFrameNumber`，发送时刻选
   delta 当且仅当上一帧恰好送达（单槽丢帧自动断链回退 full——丢帧即失去 delta 资格，
   天然背压安全）。新增 `DeltaFramesSent` 会话观测指标（/health 可见）。

## 验证

| 项 | 结果 |
|---|---|
| `BinaryFrameEncoder_WritesRotationQuaternion_InPrimitiveWireItem`（新增） | 通过：60B 条目、四分量断言 |
| `WebTextProtocolTests` 全部 | 6/6 通过（尺寸断言走 `WirePrimitiveDrawItem.SizeInBytes` 常量自动跟随） |
| 客户端 `tsc && vite build` | 通过（仅既有 chunk-size 警告） |
| `Ludots.App.Web` / `Editor.Bridge` / `Launcher.Evidence` 构建 | 0 错误 |

## 已知边界（明示）

1. delta 帧不含 skinned lane 合并——skinned 可见帧强制走全帧；skin 合并进 delta 是下一刀。
2. HUD/UI 场景/相机仍每帧全量（占大头的是 primitives，本刀收益主体）；retained scene graph
   与 ack 背压（TD-2026-03-12 清单剩余项）后续切片。
3. Rotation 不做 slerp 插值（PositionInterpolator 透传最新值）；高频旋转会有阶梯感，待插值器升级。
4. `~/.npmrc omit=dev` 环境下 `npm ci` 不装 devDeps（tsc 缺失）——本地验证需 `npm ci --include=dev`，
   这是仓库 lock/环境层面的既有问题，本切片不顺手修。
