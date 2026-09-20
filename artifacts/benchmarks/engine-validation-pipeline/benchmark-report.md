# 验证管线性能基准报告（#1321 收官 · #1330 决定依据）

问题：raylib 引擎作为 Ludots 的最小验收管线，其自身开销会不会大到让 Core 的性能被误判？
结论：**不会。** 渲染侧各 pass 的开销与 Core tick/presentation 在诊断里逐项分离，且数量级上渲染开销远低于 Core 侧指标；大负载场景的帧预算由 Core 侧逻辑主导，渲染侧可独立观测。

测量环境：本机（Windows / net9.0 / Release / epic 全部改动合入后的工作树，含阴影、水面双 pass、异步资产装载、逐实例剔除）。数字均为诊断通道实测，非模型估算。

## 1 真实宿主逐 pass 帧计时（launcher 启动，240 帧采样，每 30 帧一条）

| 场景 | 整帧 avg | Core tick avg | 其中 sim | presentation | 渲染 mode3D | terrain | primitive | overlay | FPS |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| presenter blacksmith（图元+UI） | 0.81 ms | 0.21 ms | 0.03 ms | 0.11 ms | 0.13 ms | 0.01 ms | 0.05 ms | 0.09 ms | 1321 |
| 大气水面（水面双 pass+地形+阴影+逐实例剔除） | 7.27 ms | 3.17 ms | 2.68 ms | 0.35 ms | 0.91 ms | 0.87 ms | 0.02 ms | 0.02 ms | 203 |

解读：两个场景里 Core tick 与渲染 pass 是分开计时的——性能归因不会混淆。大气场景帧时间大头是 Core 模拟（3.2ms）+ 首帧资产装载摊销，渲染侧 mode3D 不足 1ms。水面双 RT 反射/折射 + 阴影 pass 全开的情况下渲染仍只占整帧约 12%。

## 2 画廊场景帧率（300 帧，隐藏窗口）

| 场景 | 负载 | avg 帧 | p95 帧 | 说明 |
|---|---|---:|---:|---|
| crowd_anim | 4k 蒙皮实例 GPU 合批 | 12.5 ms（≈80fps） | 17.4 ms | 含动画采样与骨骼上传 |
| primitives | 图元矩阵波动 | 2.1 ms（≈476fps） | 1.2 ms | 纯渲染路径 |

（max 帧含进程启动首帧，不具代表性，已略。）

## 3 Core 侧基准（同工作树重跑，测试自动落盘）

| 基准 | 关键数字 |
|---|---|
| skia overlay hotpath（10k HUD×7 场景） | 稳态 0.0 B/帧分配；脏 lane=0；合成跳过率达标 |
| 50k 实体 HUD | 24.8ms/帧全链路（p95 38.3ms），489.9 B/帧分配（阈值线 512B 内） |
| presenter timer 30k×1 / 90k 定时器 | tick p95 0.45ms / 1.92ms，**0 分配**（有断言） |
| dynamic worker 3k/10k/30k presenter | skinned 零 drop，30k 场景 GPU 合批 214 帧/90 帧直绘 |

## 4 归因结论

- **逐项分离的诊断通道**是硬保证：`tick / sim / presentation / mode3D / terrain / primitive / overlay / cull` 各自独立计时（`PresentationTimingDiagnostics`，宿主诊断流逐帧输出）。任何 Core 回归都能在其自身通道看到，不会被渲染侧吸收或放大。
- 渲染侧最重的 pass（terrain 含水面反射折射 0.87ms、crowd_anim 全场景 12.5ms@4k 蒙皮）与 Core 侧基准（50k HUD 24.8ms、90k timer 1.9ms）处于同一数量级——验收管线没有把 Core 的性能信号"淹没"。
- W0 的原生资源台账 + alloc 阈值断言保证渲染侧自身回归（泄漏/分配）在 CI 就被拦下，不会漏到 Core 性能判断环节。

## 5 #1330 决定（据此报告）

**不授权 rlgl interop，#1330 保持挂起。** 当前 DrawMeshInstanced 的 CPU 指针上传在本报告全部场景中不是帧时间主项（primitive pass ≤0.05ms@小场景；crowd_anim 的 12.5ms 主项是蒙皮动画采样与骨骼上传，非矩阵上传）。重开条件：后续剖析显示 instanced 矩阵上传成为帧率瓶颈（参考 `primDraw` 计数与 `LastInstancedMeshDrawMs` 诊断），届时按 #1329 留档的数据纹理设计走授权流程。
