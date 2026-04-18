# Performer Raylib UAT 测试计划

本文定义 Performer-as-Actor 架构在 Raylib adapter 中的逐模块验收测试。每个模块包含两类 UAT：

- **玩家体验 UAT** — 从玩家视角验证可见效果
- **Mod 作者配置 UAT** — 从 Mod 开发者视角验证 JSON 配置的输入/输出/反馈

Raylib 全部测通后，再去 UE5 写适配。

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
| 无效 assetId | `"assetId": "nonexistent_mesh"` | 控制台警告 `[Performer] AssetBinding: mesh 'nonexistent_mesh' not found in MeshAssetRegistry` | 不崩溃，不渲染，有明确错误信息 |
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
| 缺少 Animator behavior | 只有 AssetBinding(SkinnedMesh)，无 Animator | 控制台警告 `[Performer] SkinnedMesh without Animator behavior, will render in bind pose` | 不崩溃，渲染 bind pose |
| 无效 animatorControllerId | `"animatorControllerId": "bad_id"` | 控制台警告 | 不崩溃，保持 bind pose，不进入未声明状态 |

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

## 3A AssetBinding — GroundOverlay

### 3A.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 圆形地面指示器 | performer(AssetBinding: GroundOverlay, assetId: Circle, Grounding: AlignToSurface) | 地面出现圆形范围圈 | 与地表贴合，无浮空/穿地，形状与参数一致 |
| 扇形地面指示器 | performer(AssetBinding: GroundOverlay, assetId: Cone, Grounding: AlignToSurface) | 地面出现扇形范围圈 | 朝向、开角与宽度参数正确 |
| 线形地面指示器 | performer(AssetBinding: GroundOverlay, assetId: Line, Grounding: AlignToSurface) | 地面出现线形范围提示 | 起点/终点方向正确，不退化为其他形状 |

### 3A.2 Mod 作者配置 UAT

| 用例 | JSON 输入 | 运行时反馈 | 验收标准 |
|------|----------|----------|---------|
| 最小配置 | `{"slot":0,"kind":"AssetBinding","assetBinding":{"assetKind":"GroundOverlay","assetId":"Circle","grounding":"AlignToSurface"}}` | 地面出现圆形指示器 | 使用 `GroundOverlay` 作为规范 AssetKind，不再接受 `Decal` 代写地面指示器 |
| 无效 shape id | `"assetId": "Hexagon"` | 加载时错误或运行时显式拒绝 | 不崩溃，不渲染，不偷偷改成其他形状 |

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
| 无效 soundAssetId | `"soundAssetId": "bad"` | 控制台警告 | 不崩溃，静默跳过 |
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
| 无效 attributeName | `"attributeName": "nonexistent"` | 控制台警告 `[Performer] AttributeBinding: attribute 'nonexistent' not found` | 不崩溃，param 保持默认值 |
| 空 thresholds | `"thresholds": []` | 只做连续值绑定，不做阈值映射 | 正常工作 |
| 阈值顺序错误 | thresholds 未按降序排列 | 控制台警告 `[Performer] AttributeBinding: thresholds should be in descending order` | 自动排序后正常工作 |

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
| 无效 tagId | `"tagId": "nonexistent"` | 控制台警告 | 不崩溃，param 保持 0.0 |

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
| 无效 materialId | swapTable 中引用不存在的材质 | 控制台警告 | 显式使用 `baseMaterialId` |

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
| 无效 boneId | `"boneId": "nonexistent_bone"` | 控制台警告 `[Performer] Attachment: bone 'nonexistent_bone' not found` | 不崩溃，跳过挂载结果，不产生未声明位置 |

---

## 12 Performer 树生命周期

### 12.1 玩家体验 UAT

| 用例 | 输入 | 预期输出 | 验收标准 |
|------|------|---------|---------|
| 子树自动创建 | root performer 创建 | `children` 中声明的子 performer 全部自动创建 | 子 performer 数量与 `children` 数组长度一致 |
| Scope 销毁 | DestroyPerformerScope("working") | working scope 下所有 performer 销毁 | 其他 scope 不受影响 |
| 动态子创建 | Rule 触发 CreatePerformer(parent=root) | 子 performer 动态创建并挂到指定父节点 | 创建后归属正确 scope，层级关系正确 |
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
| 铁匠铺出现 | 创建 blacksmith entity | 2 工房 + 1 锅炉出现，贴合地面 | 5 个子 mesh 全部可见 |
| 开工 | 设置 "working" tag | 烟囱冒烟 + 工人出现并巡逻 | smoke VFX + worker 动画 + 锤击声 |
| 停工 | 移除 "working" tag | 烟消失 + 工人消失 + 声音停止 | 无残留 |
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
   - WorldHud → Widget/Screen-Space Bridge
   - WorldText → World Text Bridge
   - GroundOverlay → Ground Overlay / Decal Projector Bridge
3. UE5 跑同一套 §13 UAT JSON，验证 adapter parity
