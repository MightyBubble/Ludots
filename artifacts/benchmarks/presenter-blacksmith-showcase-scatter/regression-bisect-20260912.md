# blacksmith 30K HUD bar/text + RandomDrift 回归二分（2026-09-12）

## 结论

- **同机同负载实证**：`d78b46f2b7`（05-03，"Optimize blacksmith 30k HUD effect path"）今天重跑
  scatter_30000_tight：**avg tick 1.87ms / fps 等效 533.7**（attr changes 137.3/帧，HUD bars 30000 + text 30000 全在）。
- 当前 HEAD 同负载：avg tick 46.6ms / fps 等效 21.5。**同负载同机 25× 回归，用户记忆的"最早远没这么差"成立。**
- **拐点提交：`46b1bd926d`（2026-08-13 17:48 UTC，"merge: Raylib client upgrade into presentation SSOT"）**。
  该合并是首个可构建的 BAD 探针点（28.0fps）；两个父线各自单独探针均 GOOD
  （SSOT 前驱 `541e7130f2` 129.6fps；raylib 升级线 tip `a694bb2749` 134.8fps）——
  回归由这次大合并的内容组合/合并解决引入，属于"一不留神"的一次性丢失。

## 数字阶梯（同机探针，仅 scatter_30000_tight，warmup 6 / measure 40）

| 提交 | 时间 | fps 等效 | 状态 |
|---|---|---:|---|
| d78b46f2b7 | 05-03 | 533.7 | GOOD |
| b46d031334 | 08-13 11:06Z | 135.3 | GOOD（5-03→8-13 有 4× 缓慢下滑，未崩） |
| 01b25bd25e | 08-13 | 128.8 | GOOD |
| 1192f13b9f | 08-13 16:29Z | 138.1 | GOOD |
| b0784005e5 | 08-13 17:09Z | 125.8 | GOOD |
| b6ad0b4736 | 08-13 17:09Z（Prefab 栈删除） | 132.4 | GOOD |
| bbb7fc22c6 | 08-13（请求缓冲分通道） | 138.1 | GOOD |
| ab1f91bf92 | 08-13 16:41Z | 136.9 | GOOD |
| 541e7130f2 | 08-13 17:23Z | 129.6 | GOOD |
| a694bb2749 | 08-12（raylib 升级线 tip） | 134.8 | GOOD |
| **46b1bd926d** | **08-13 17:48Z（SSOT 大合并）** | **28.0** | **BAD** |
| 80eb07a562 | 08-13 18:21Z | 26.8 | BAD |
| 6c84f46ef6 | 08-24 | 32.8 | BAD |
| f23fcb31b2 / 806a7c0ff7 | 08-29 | 35.1 / 35.3 | BAD |
| e4aa2dac95（HEAD） | 09-12 | 21.5（全量 120 帧口径 46.6ms） | BAD |

注：17:09:33–17:34Z 的 SSOT 合并冲刺链（cef23aad4d/9d71016f92/feeb263dcc 等）独立构建不过，
不可单独探针；46b1bd926d 是冲刺链上首个可构建提交。

## 回归的形状（同负载分段对比）

| 段 | 05-03 | HEAD |
|---|---:|---:|
| request flush | **0.00ms** | **19.4ms** |
| presenter emit | 0.17ms | 11.3ms |
| EffectProcessingLoop(sim) | 1.72ms | 6.4–7.1ms |
| attr changes/帧 | 137.3 | 162.6（负载同量级） |

05-03 架构下该场景 HUD 属性更新不走 PresentationRequestFlushSystem（flush 0ms）；
SSOT 合并后 30K 慢变更 presenter 的属性更新进入请求队列全量冲刷。修复方向：
恢复静态/慢变更 presenter 的非请求直投路径（或 flush 增量语义），目标 30K tight 回到 ≤5ms tick。

## 方法

探针脚本 `probe30k.sh`（本目录）：worktree 检出 → SDK9 钉版构建 →
`LUDOTS_BLACKSMITH_BENCH_SCENARIO=scatter_30000_tight` 单场景 → 读最新报告 fps 等效（阈值 50）。
两处坑已修：报告目录随 8-11 performer→presenter 改名（按 mtime 取最新）；SDK10 编 net8 旧树需 global.json 钉 9.0.312。
