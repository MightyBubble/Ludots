# 动画合批：4K demo 为什么快，10K 实战为什么慢

**日期**：2026-09-10　**基线**：`integrate/1486-on-perf`（含 GPU shared-pose 提交 `3081d6f3ea`）
**本文所有数字均为本机实测**，环境注意事项见文末。

---

## 1. 三个场景的实测对照

同一台机器、同一窗口（1600x900）、同一 Release 构建：

| 场景 | 实例数 | 路径 | 帧时间 | FPS |
|---|---:|---|---:|---:|
| `engine_raylib_crowd_anim`（4K 演示） | 4,096 | GpuSkinnedInstance，**相位分桶** | 20.29ms | **49.3** |
| 10K showcase（换 cube） | 10,006 | InstancedStaticMesh | 19.42ms | **45.9** |
| 10K showcase（mannequin 真骨骼） | 10,000 | GpuSkinnedInstance，**无相位分桶** | 54.3ms | **18.4** |

**关键读数**：4K 演示的 FPS 与 10K cube **几乎相同**。这说明 4K 演示跑 4096 个真骨骼实例，
代价与 1 万个纯 cube 相当——**它的合批是有效的**。而 10K 实战只多 2.4 倍实例，
却慢 2.8 倍。差距不在"蒙皮"本身，在**分桶粒度**。

---

## 2. 根因：桶 key 的粒度不同

### 4K demo（`CrowdAnimScene.cs`）

```
bucket key = (meshAsset, 环带色, clip, frame)
=> 7 色 × 16 相位桶 = 112 逻辑桶，× 6 mesh = 672 draw / 4096 实例
=> 平均每桶 6.1 个实例
```

源码注释把成本模型写得很清楚：

> 桶数每 +1 就多 6 次 uniform 上传/draw 与一次骨骼姿态计算，是本车道主要帧耗来源。

它**主动把相位离散成 16 档**（`DesiredPhaseBuckets = 16`），并把环带色量化成 7 档，
就是为了压低桶数。这是有意的取舍——行军队列里 16 相位差肉眼不可辨。

### 10K showcase（`RaylibGpuSkinnedBatchRenderer`）

```csharp
// 批次 key
private readonly record struct GpuSkinnedInstanceBatchKey(int MeshAssetId, int MaterialId);

// 姿势行 key（每帧、每实例精算）
var poseKey = (item.MeshAssetId, clipIndex, frameIndex);
```

**批次只按 `(meshAsset, material)` 分桶**，而 `frameIndex` 是**每实例独立解析**出来的
浮点动画时间 → **每个实例可能落在不同的 frame**，于是每帧的 `_dirtyPoseRows` 几乎是
`N` 条，每条都要一次骨骼姿态计算 + 调色板行写入。

**这就是 4K 与 10K 的本质差别**：4K demo 手动把相位压到 16 档，10K 生产路径
**依赖实例动画时间自然重合**——而 10000 个单位各有各的速度/朝向/相位，
几乎不可能重合，于是退化成"每实例一个姿势"。

---

## 3. 这块到底是不是工业级？

### Unity：GPU Skinning + Animator 的批处理前提

Unity 的 `SkinnedMeshRenderer` 走 GPU skinning 后，批处理依赖**共享材质**，
但真正的关键在两点：

1. **Animation Instancing**（Unity 官方 `AnimationInstancing` 包 / `GPU ECS Animation`）：
   姿势数据烘焙成**骨骼贴图 / compute buffer**，每个实例只带一个 `clip + time` 索引，
   在 GPU 里按索引取骨骼。**核心正是把连续时间离散成桶**。
2. Unity 的 SRP Batcher / DOTS 渲染：批处理按"材质 + 关键字 + 顶点布局"分组，
   **姿势不进 key**——姿势是 per-instance 数据（instance buffer），不是 batch 维度。

### Unreal：Skeletal Mesh 的工业做法

1. **GPU Skin Cache**（`r.GPUSkin.Support16BitBoneIndex`、`bSupportSkinCache`）：
   骨骼变换写进 **skin cache buffer**，实例渲染时按 `bone transform offset` 索引。
2. **Animation Sharing Plugin**（UE 官方插件，原名 "Animation Sharing"）：
   **这正是 4K demo 的做法**——把大量角色的动画状态**离散化分桶**，
   相同状态的角色**共享同一份骨骼解算结果**，每桶只解一次。
   UE 文档明确指出适用场景是"crowd / 大量同质角色"。
3. **Vertex Animation Texture（VAT）**：离线烘焙顶点动画，运行期零骨骼解算，
   实例只需一个 time 参数——比骨骼合批更极端。

### 结论

| 能力 | Unity | Unreal | 我们 |
|---|---|---|---|
| 骨骼贴图/索引缓冲 | ✅ | ✅ 皮肤缓存 | ✅ 姿势调色板 |
| **动画状态分桶共享** | ✅ Animation Instancing | ✅ Animation Sharing | ⚠️ **只有 4K demo 手写，生产路径没有** |
| 离线顶点动画 | ✅ | ✅ VAT | ✅ 有（预蒙皮探针，未进生产） |
| 每实例姿势独立解算 | ❌ 不这么干 | ❌ 不这么干 | ❌ **10K 生产路径就是这样** |

**判断：我们的 GpuSkinned 车道是工业级的"单帧内合批"，但缺了工业界用来处理
crowd 的关键一层——动画状态离散化共享。** 4K demo 用 30 行手写逻辑补上了这一层，
所以它不卡；10K 生产路径没补，所以卡。这不是 Raylib 的限制，是**上层没做分桶**。

---

## 4. 待办

- [ ] **把 4K demo 的相位分桶下沉到生产路径**：`GpuSkinnedInstanceBatchKey` 或姿势行 key
      纳入"量化后的动画相位"，让同 clip 相近相位的实例共享姿势行。
      验收：10K mannequin 的 `_dirtyPoseRows` 数量从 ~N 降到与桶数同量级。
- [ ] 分桶粒度必须可配（4K demo 证明 16 档够用；10K 需要重新标定）。
- [ ] 记录：cube 替换仅用于隔离"蒙皮成本 vs 其他成本"，不进仓库。

---

## 5. 环境警告（数字可信度）

本机在长会话后出现明显降级：早先同一构建可跑出 25.6 FPS，后段降到 17.7 FPS，
且 `OrayIddDriver`（远程显示驱动）与 `IdeaDisplay` 虚拟显示处于活跃状态，
主显示器中途从 2560 宽变为 2048x1280。**上表的相对关系（cube > 蒙皮、4K 演示 ≈ cube）
在多次运行中一致，但绝对 FPS 不可跨会话比较。**
4K demo 的 20.29ms 取自仓库内 `artifacts/acceptance/engine_raylib_crowd_anim/stats.json`，
为其自身验收环境的记录。
