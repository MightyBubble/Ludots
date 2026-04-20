# Performer Raylib UAT 测试计划

本文定义 Performer-as-Actor 架构在 Raylib adapter 中的逐模块验收测试。每个模块包含两类 UAT：

- **玩家体验 UAT** — 从玩家视角验证可见效果
- **Mod 作者配置 UAT** — 从 Mod 开发者视角验证 JSON 配置的输入/输出/反馈

Raylib 全部测通后，再去 UE5 写适配。

---

## 0 Raylib 层定位与资产策略

### 0.1 定位

Raylib 适配层的目标是**能力覆盖基准**，不是视觉效果基准。每种 AssetKind / BehaviorKind 必须有可见输出来证明 Core 层数据链路完整，但不追求渲染保真度。视觉保真度留给 UE5 / Unity / Godot 等商业引擎适配层。

核心验证目标：
1. `PerformerBehaviorSystem` 正确驱动 attribute→param→assetSwap / materialSwap / animatorState 等链路
2. `PerformerEmitSystem` 按 AssetKind 正确分发到对应 proxy buffer
3. Raylib adapter 能把每种 proxy 映射到某种可见输出（哪怕是 placeholder 几何体）
4. 树形生命周期（创建/销毁/scope/参数继承）端到端正确

### 0.2 AssetKind 的 Raylib 模拟方案

| AssetKind | Raylib 原生能力 | UAT 模拟方案 | 验证重点 |
|-----------|----------------|-------------|---------|
| Mesh | LoadModel 完整支持（GLTF/OBJ/GLB） | 内置 Cube/Sphere + Kenney CC0 低模建筑 | 位置/旋转/缩放/颜色/材质切换/动态 swap |
| SkinnedMesh | LoadModelAnimations + UpdateModelAnimation（GLTF 骨骼动画有已知 bug，仅支持简单骨骼链） | 自造 2-3 骨骼 GLB（摆臂/点头级别） | 状态机→clip index→bone update 链路通，不验证蒙皮质量 |
| Decal | 无原生投影贴花，无 RVT | 复用 GroundOverlay（地面 quad + 纹理），AlignToSurface 用法线旋转 quad | grounding 对齐 + scale param 链路 |
| VFX | 无粒子系统 | Billboard sprite + alpha fade 模拟（DrawBillboard + 逐帧 alpha 衰减） | 生命周期（创建/销毁）+ 位置跟随父 performer + localOffset |
| Sound | LoadSound / PlaySound / SetSoundVolume 完整支持 | CC0 音效文件 | loop/非 loop + volume param 绑定 + 随 performer 销毁停止 |
| Spline | 无原生样条渲染 | DrawLine3D 连线段 + 沿线段插值移动 | 巡逻逻辑 + waypoint 事件 + speed param + pingPong |
| WorldHud | 通过 Skia overlay 已有 | 复用现有 HUD 渲染 | 位置跟随 + 可见性 |
| WorldText | 通过 Skia overlay 已有 | 复用现有文本渲染 | 文本内容绑定 + 位置跟随 |

### 0.3 SkinnedMesh 降级策略

Raylib 的 GLTF 骨骼动画存在已知问题（bone transform 计算偏差，复杂骨骼链变形异常）。UAT 采用降级策略：

- 测试模型限制为 2-3 个骨骼的简单链（如：root→torso→arm）
- 只验证：AnimatorRuntimeSystem 状态机输出 clip index → Raylib adapter 调用 UpdateModelAnimation → 骨骼有可见运动
- 不验证：蒙皮权重混合质量、复杂骨骼树、动画过渡平滑度
- 这些高保真验证留给 UE5 适配层（T18）

### 0.4 测试资产清单

所有测试资产放在 `mods/fixtures/blacksmith/assets/` 下。

#### 自造资产（Blender 导出 GLB）

| 文件 | 用途 | 规格 |
|------|------|------|
| `test_cube.glb` | Mesh 基础验证 + Material swap | 带 2 个材质槽的立方体 |
| `test_cube_damaged.glb` | 耐久度阈值 mesh swap | 缺角立方体 |
| `test_cube_ruined.glb` | 耐久度归零 mesh swap | 碎裂立方体 |
| `test_skinned.glb` | SkinnedMesh + Animator 链路 | 3 骨骼摆臂模型，含 idle/walk 两个 animation clip |
| `test_ground_quad.glb` | Decal 模拟 | 1m×1m 地面 quad，带 UV |
| `test_spline_path.json` | Spline 巡逻路线 | 4 个 waypoint 的闭合路径 |

#### CC0 外部资产

| 来源 | 资产 | 用途 | 协议 |
|------|------|------|------|
| [Kenney Retro Medieval Kit](https://kenney-assets.itch.io/retro-medieval-kit) | 建筑/道具低模（GLTF） | 铁匠铺工房、锅炉外观 | CC0 |
| [Kenney Retro Fantasy Kit](https://kenney.nl/assets/retro-fantasy-kit) | 角色低模（GLTF） | 工人外观（如无骨骼则仅用于静态展示） | CC0 |

Kenney 资产为可选增强项。即使不使用外部资产，仅靠自造 GLB + 内置 Cube/Sphere 也能完成全部 UAT 验证。

#### 音效资产

| 文件 | 用途 | 来源 |
|------|------|------|
| `anvil_hit.wav` | 锤击声（loop） | 自造或 CC0（freesound.org） |
| `fire_crackle.wav` | 锅炉火焰声 | 自造或 CC0 |

### 0.5 铁匠铺 UAT 资产映射

```
blacksmith_root
├── workshop_1 (scope: structure)
│   Mesh: test_cube.glb → test_cube_damaged.glb → test_cube_ruined.glb（按耐久度阈值）
│   Material: slot 0 = brick_black(北方) / brick_red(南方)（按 region param）
├── workshop_2 (scope: structure)
│   同 workshop_1
├── furnace (scope: structure)
│   Mesh: test_cube.glb（或 Kenney 建筑模型）
├── smoke (scope: working)
│   VFX 模拟: Billboard sprite, alpha fade, localOffset=[0,5,0], visibility 由 working tag 驱动
└── worker (scope: working)
    SkinnedMesh: test_skinned.glb（3 骨骼摆臂）
    Animator: idle↔walk 状态机，working on 时进入工作态
    Spline: test_spline_path.json 巡逻
    Sound: anvil_hit.wav (loop)，由 working tag 驱动
```

### 0.6 Raylib 模拟实现要点

**VFX 模拟**：不实现粒子系统。用 `DrawBillboard` 绘制一个半透明 sprite。位置 = 父 performer WorldPosition + localOffset，可见性由 performer param/tag 驱动。这足以验证 VFX 的位置跟随 + behavior 开关链路。

**Decal 模拟**：不实现投影贴花。用 `DrawMesh`（flat quad）+ grounding 系统计算的地面位置和法线。AlignToSurface 模式下 quad 法线对齐地形法线。这足以验证 grounding + scale param 链路。

**Spline 模拟**：不实现样条曲线渲染。用 `DrawLine3D` 在相邻 waypoint 间画线段。巡逻移动用线性插值（非 Catmull-Rom），到达 waypoint 时触发事件。这足以验证巡逻逻辑 + waypoint 事件 + speed/pingPong 参数。

**Material 模拟**：Raylib 无 PBR 材质系统。用 `Color` 替代材质 ID — baseMaterialId 映射到一个 Color，swapTable 的每个 materialId 也映射到 Color。Material behavior 输出的 materialId 通过 adapter 查表转为 Color 赋给 model。这足以验证 param→materialId 查表链路。

### 0.7 2026-04-20 Direct ISM Benchmark 回填

为隔离“平台层 instancing draw”与“performer/entity/runtime 行为链路”的瓶颈，当前仓库额外维护一个直接绘制 showcase：

- 路径：`mods/showcases/raylib_ism_benchmark/`
- 目标：不经过 performer/entity 行为驱动，直接压 Raylib instanced static mesh 的最终绘制链路
- 资产：直接复用铁匠铺第三方 mesh（建筑、脚手架、矿炉、骑士）
- UI：HUD、text、slider 全部走 Skia final overlay
- 控制：实例数 slider 支持 `3k -> 300k`

当前已验证能力：

- 黑铁匠铺第三方 mesh 可被直接加载并参与 instanced static mesh draw
- 材质最小闭环已打通，当前 benchmark 使用模型自带纹理/材质，不再是纯色占位
- 默认 `30k` 实例时，最终画面可稳定输出，证据见：
  - `artifacts/raylib-ism-benchmark/benchmark-hud-frame120-v2.png`
  - `artifacts/raylib-ism-benchmark/launch-hud-v2.out.log`

默认 `30k` 实测面板数据：

- `fps=064`
- `bucketRebuild=8.33ms`
- `ismDraw=1.10ms`
- `skiaBuild=0.02ms`
- `skiaDraw=9.58ms`

当前判断口径：

- Raylib adapter 的 ISM 直接绘制链路已经证明可跑通黑铁匠铺 mesh，不应再把当前瓶颈默认归到平台层 ISM。
- 这次压测先暴露出的真实风险点是 Skia final HUD/text overlay，而不是 Raylib instanced mesh draw。
- `SkiaOverlayRenderer` 已修复一处高压场景下的缓存生命周期 bug：之前 cache 满时会在本帧中途清掉仍被 batch 引用的 bar/text sprite，导致 native `AccessViolation`。
- 该 benchmark 是“最终绘制路径证明”和“瓶颈隔离”工具，不替代本文件 §1-15 的正式 Performer UAT。

---

## 1 AssetBinding — Mesh

### 1.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 单 Mesh 渲染 | 创建一个 entity + performer(AssetBinding: Mesh, cube) | 屏幕上出现立方体 | 立方体位于 entity 世界坐标，颜色/缩放正确 |
| Performer 嵌套组合 Mesh | root performer + 3 个子 performer（各自 AssetBinding: Mesh + LocalOffset） | 屏幕上出现 3 个子 mesh | 子 mesh 相对位置、旋转、缩放与定义一致（取代旧 Prefab） |
| Mesh 动态切换 | `assetSwapParamKey` 绑定到属性阈值，属性从 1.0→0.3 | mesh 从 model_a 切换到 model_b | 切换帧无闪烁，新 mesh 位置不跳变 |
| Mesh 销毁 | entity 销毁 | mesh 消失 | 无残留绘制，StableId 回收 |

### 1.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 最小配置 | `{"slot":0, "kind":"AssetBinding", "assetBinding":{"assetKind":"Mesh", "assetId":"cube"}}` | 渲染出 cube | 只需 assetKind + assetId 即可工作，其他字段有合理默认值 |
| 无效 assetId | `"assetId": "nonexistent_mesh"` | 加载失败，指出 assetId 未注册 | 拒绝加载，不允许静默跳过 |
| 缩放/颜色 param 绑定 | `"scaleParamKey": 10, "colorParamKey": 11` | 通过 SetParam(10, 2.0) 缩放变为 2x；SetParam(11, vec4(1,0,0,1)) 变红 | param 变化立即生效 |

---

## 2 AssetBinding — SkinnedMesh

### 2.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 蒙皮模型 idle | performer(AssetBinding: SkinnedMesh) + Animator behavior | 模型播放 idle 动画 | 骨骼变形正确，无 T-pose 闪帧 |
| 动画状态切换 | SetParam(speedKey, 1.0) | 从 idle 切换到 walk | 过渡平滑，持续时间与 TransitionDefinition 一致 |

### 2.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 缺少 Animator behavior | 只有 AssetBinding(SkinnedMesh)，无 Animator | 加载失败，指出缺少 Animator | 拒绝加载，不允许 fallback |
| 无效 animatorControllerId | `"animatorControllerId": "bad_id"` | 加载失败，指出 controllerId 无效 | 拒绝加载，不允许 fallback |

---

## 3 AssetBinding — Decal

### 3.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 地面贴花 | performer(AssetBinding: Decal, Grounding: AlignToSurface) | 贴花贴合地面 | 贴花法线对齐地形，无浮空/穿地 |
| 贴花缩放 | scaleParamKey 绑定 | 贴花大小随 param 变化 | 缩放中心正确 |

### 3.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| Decal + SnapToGround | `"grounding": "AlignToSurface"` | 贴花对齐地面法线 | 与 SnapToGround 行为不同（有旋转对齐） |

---

## 4 AssetBinding — VFX

### 4.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 粒子特效播放 | performer(AssetBinding: VFX, "chimney_smoke") | 烟雾粒子从指定位置发射 | 位置跟随父 performer |
| VFX 生命周期 | 父 performer 销毁 | VFX 停止发射并淡出 | 无残留粒子 |

### 4.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| VFX + localOffset | `"localOffset": [0, 5, 0]` | 粒子从偏移位置发射 | 偏移相对于父 performer 坐标系 |

---

## 5 AssetBinding — Spline

### 5.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 道路渲染 | performer(AssetBinding: Spline, Usage: Render) | 样条道路可见 | 宽度和颜色与 param 一致 |
| 巡逻路线 | performer(Spline: Patrol, loop=true) | performer 沿路径循环移动 | 速度与 speedParam 一致，到端点平滑折返 |

### 5.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| Patrol + PingPong | `"pingPong": true` | 到端点后反向移动 | 不是瞬移回起点 |
| Patrol + WaypointEvent | `"waypointEventId": 42` | 到达 waypoint 时触发 PresentationEvent(42) | 可被 PerformerRule 捕获 |

---

## 6 Sound Behavior

### 6.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 循环声音 | SoundConfig(loop=true, "anvil_hammering") | 持续播放锤击声 | 声音随 performer 创建开始，销毁停止 |
| 音量 param | volumeParamKey 绑定 | 音量随 param 变化 | 0.0=静音，1.0=满音量，过渡平滑 |

### 6.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 无效 soundAssetId | `"soundAssetId": "bad"` | 加载失败，指出 soundAssetId 未注册 | 拒绝加载，不允许静默跳过 |
| 非循环声音 | `"loop": false` | 播放一次后停止 | 不重复播放 |

---

## 7 AttributeBinding Behavior

### 7.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 属性比率→颜色 | durability 从 1.0→0.0 | 颜色从绿渐变到红 | 每帧更新，无跳变 |
| 阈值映射→mesh swap | durability 跨 0.66→0.33→0.0 | mesh 切换 intact→damaged→ruined | 切换发生在正确阈值，不提前不延迟 |

### 7.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 无效 attributeName | `"attributeName": "nonexistent"` | 加载失败，指出 attribute 未注册 | 拒绝加载，不允许 silently keep default |
| 空 thresholds | `"thresholds": []` | 只做连续值绑定，不做阈值映射 | 正常工作 |
| 阈值顺序错误 | thresholds 未按降序排列 | 加载失败，指出 thresholds 顺序非法 | 拒绝加载，不允许自动排序 |

---

## 8 TagBinding Behavior

### 8.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| Tag on→可见 | "working" tag 激活 | param=1.0，关联 behavior 激活 | 激活帧生效 |
| Tag off→隐藏 | "working" tag 失效 | param=0.0，关联 behavior 停用 | 失效帧生效 |
| InvertLogic | `"invertLogic": true` | tag on→param=0, off→param=1 | 逻辑反转正确 |

### 8.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 无效 tagId | `"tagId": "nonexistent"` | 加载失败，指出 tagId 未注册 | 拒绝加载，不允许写默认值冒充成功 |

---

## 9 Material Behavior

### 9.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 材质切换 | param 300 从 0→1 | 砖色从黑→红 | 切换帧生效，无闪烁 |
| 默认材质 | param 值不在 swapTable 中 | 使用 baseMaterialId | 不崩溃 |

### 9.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 空 swapTable | `"swapTable": []` | 始终使用 baseMaterialId | 正常工作 |
| 无效 materialId | swapTable 中引用不存在的材质 | 加载失败，指出 materialId 未注册 | 拒绝加载，不允许 fallback |

---

## 10 Animator Behavior

### 10.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 状态机驱动 | speed param 从 0→1 | idle→walk 过渡 | 过渡时间与 TransitionDefinition 一致 |
| Blackboard→Animator | performer SetParam(speedKey, 2.0) | 动画播放速度 2x | 参数从 blackboard 正确流入 animator |
| 动画完成反馈 | 播放一次性动画 | stateParamKey 写回完成状态索引 | 可被 PerformerRule 捕获 |

### 10.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| speedParamKey 未设置 | `"speedParamKey": -1` | 使用默认播放速度 | 正常工作 |

---

## 11 Attachment Behavior

### 11.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 骨骼挂载 | 子 performer 挂载到父的右手骨骼 | 子 mesh 跟随骨骼运动 | 每帧位置正确，无延迟 |
| 挂载偏移 | `offset: [0, 0.1, 0]` | 子 mesh 在骨骼位置上方 0.1 | 偏移在骨骼空间 |
| 不继承缩放 | `inheritScale: false`，父缩放 2x | 子 mesh 保持原始大小 | 缩放独立 |
| 骨骼挂载跳过 Grounding | 子 performer BoneAttached + GroundingMode=SnapToGround | 子 mesh 跟随骨骼，不贴合地面 | Grounding 被跳过 |

### 11.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 无效 boneId | `"boneId": "nonexistent_bone"` | 运行时显式失败并记录骨骼解析错误 | 不允许 fallback 到父位置 |

---

## 12 Performer 树生命周期

### 12.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 子树自动创建 | root performer 创建 | `children` 中声明的子 performer 全部自动创建 | 子 performer 数量与 `children` 数组长度一致 |
| Scope 销毁 | DestroyPerformerScope("projectile") | projectile scope 下所有一次性 performer 销毁 | structure/working 常驻子树不受影响 |
| 动态子创建 | Rule 触发 CreatePerformer(parent=root) | 一次性 performer 动态创建并挂到指定父节点 | 用于真正生命周期对象，不替代常驻 showcase 子树 |
| 递归销毁 | root performer 销毁 | 整棵子树销毁 | 无孤儿 performer，所有 StableId 回收 |
| 参数继承 | root SetParam(300, 1) | 子 performer 读到 param 300=1 | 继承链正确 |
| 参数覆盖 | 子 performer SetParam(300, 2) | 子读到 2，孙读到 2，root 仍为 1 | 覆盖不影响父 |

### 12.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 循环引用 | A 的 children 包含 B，B 的 children 包含 A | 加载时错误 `[Performer] Circular child reference detected: A→B→A` | 不崩溃，拒绝加载 |
| 超过 32 个 behavior | 33 个 BehaviorSlot | 加载时错误 `[Performer] Max 32 behaviors per performer` | 不崩溃，拒绝加载 |

---

## 13 铁匠铺完整 UAT

### 13.1 玩家体验 UAT

| 用例 | 操作 | 预期画面 | 验收标准 |
|------|------|---------|---------|
| 铁匠铺出现 | 创建 blacksmith entity | 2 工房 + 1 锅炉 + smoke/worker 常驻节点全部创建 | 完整 performer tree 存在；初始 working=off 时 smoke/worker 可见性与行为关闭 |
| 开工 | 设置 "working" tag | 烟囱冒烟 + 工人进入巡逻/工作态 | smoke VFX 可见；worker 动画、声音、文本/HUD 联动生效；不依赖节点创建 |
| 停工 | 移除 "working" tag | 烟停止、工人退出工作态 | 节点仍常驻；VFX/动画/声音停用；无 lifecycle 抖动 |
| 白天→夜晚 | 触发 GlobalDayNight | 窗户灯光材质变亮 | 材质切换无闪烁 |
| 北方铁匠铺 | region param=0 | 黑砖材质 | 所有工房使用黑砖 |
| 南方铁匠铺 | region param=1 | 红砖材质 | 所有工房使用红砖 |
| 耐久度下降 | durability 从 1.0→0.5 | 工房 mesh 切换到 damaged | 阈值 0.66 处切换 |
| 耐久度归零 | durability→0 | 工房 mesh 切换到 ruined | 阈值 0.0 处切换 |
| 铁匠铺销毁 | 销毁 entity | 所有视觉消失 | 无残留，无报错 |

### 13.2 Mod 作者配置 UAT

| 用例 | JSON 操作 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 添加第 3 个工房 | children 中增加 workshop_3 | 第 3 个工房出现在指定偏移 | 无需改代码 |
| 修改巡逻路线 | 更换 splineAssetId | 工人沿新路线巡逻 | 热重载后生效 |
| 添加新阈值 | thresholds 增加 0.5 档位 | 新增 "半损" 状态 | 无需改代码 |
| 替换烟囱 VFX | 更换 assetId | 新特效播放 | 无需改代码 |

---

## 14 测试文件组织

```
src/Tests/PresentationTests/
├── PerformerAssetKindTests.cs          — §1-5 AssetKind 逐项
├── PerformerBehaviorKindTests.cs       — §6-11 BehaviorKind 逐项
├── PerformerTreeLifecycleTests.cs      — §12 树生命周期
├── BlacksmithPerformerUatTests.cs      — §13 铁匠铺完整 UAT
mods/fixtures/blacksmith/
├── BlacksmithTestMod/
│   ├── assets/Presentation/performers.json
│   ├── assets/Entities/templates.json
│   └── BlacksmithTestModEntry.cs
```

## 15 Raylib → UE5 适配顺序

1. Raylib 全部 §1-13 测通
2. UE5 adapter 实现 AssetKind 映射：
   - Mesh → ISM (Instanced Static Mesh)
   - SkinnedMesh → Skeletal Mesh Component
   - Decal → Decal Component
   - VFX → Niagara System
   - Sound → Audio Component
   - Spline → Spline Mesh Component
3. UE5 跑同一套 §13 UAT JSON，验证 adapter parity
