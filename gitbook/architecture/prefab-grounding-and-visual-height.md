# Prefab Grounding 与 Visual Height

> **注意：** 本文描述的 Prefab 系统已被 [Performer-as-Actor 架构](performer-as-actor-architecture.md) 取代。Prefab 的"层级化视觉资产"概念现在由 Performer 树的 children + AssetBinding 实现，Grounding 语义由 `PerformerGroundingUtility` 承载。本文中关于 visual height 真相归属、Core-owned lowering、adapter 不拥有 grounding 语义等原则仍然有效，只是执行载体从 `PrefabFinalizationPipeline` 变为 `PerformerBehaviorSystem` + `PerformerEmitSystem`。

本页用人话说明 Ludots 里 prefab grounding 这件事到底在解决什么问题，以及为什么这块工作必须按固定顺序推进。

## 一句话结论

Prefab grounding 的本质，不是“把 prefab 放到地上”这么简单。

它真正要解决的是三件事：

- 地面的高度真相从哪里来
- prefab 在进入 adapter 之前要被整理成什么正式结果
- 所有 adapter 是否都严格消费同一份结果

如果这三件事没先讲清楚，后面做再多性能优化，都会很容易做歪。

## 先说问题本质

假设地图里有一个 prefab，它不是一个单独 mesh，而是一组带层级关系的 part：

- 有些 part 只是普通静态几何
- 有些 part 需要贴地
- 有些 part 可能还要求按地面法线对齐

这时候系统真正要回答的问题不是“UE5 怎么画”或“Raylib 怎么画”，而是：

- 当前地图的地面高度到底由谁说了算
- prefab 的每个 part 最终应该落在什么世界坐标
- 这个结果是 Core 先算好，还是 adapter 自己猜

Ludots 的正式答案应该只有一个：

- 地面真相由 Core 提供
- prefab 先在 Core 里完成 finalization
- adapter 只消费 finalization 后的叶子结果

## 什么叫正式真相

这里有两个不能混的真相：

### 1. 地图逻辑真相

这是地图、board、实体状态这些 authoritative gameplay truth。

### 2. 视觉地面真相

这是视觉层用来回答“这个世界点的地面高度是多少”“地面法线是什么”的真相。

在当前设计里，这个真相应该统一收口到 `IVisualHeightmap`，并通过 `CoreServiceKeys.VisualHeightmap` 暴露。

这意味着：

- prefab grounding 要用它
- 地面 raycast 要用它
- terrain height sync 要用它
- adapter 也要读它

不能出现下面这些分叉：

- 某个 showcase mod 自己临时塞一个高度来源
- UE5 自己推一套地面高度
- Raylib 走 visual height，UE5 走别的 projector
- 某条 render lane 静默跳过 grounding

## prefab 到底是什么

Prefab 不是“某个 adapter 直接拿去画的 mesh id”。

Prefab 的正式含义应该是：

- 一份 authored 的层级化视觉资产
- 它可以引用其他 prefab
- 每个 part 都可能带局部 transform、颜色、grounding 等描述

所以 prefab 在运行时不能直接被 adapter 当成最终画面输入。

它必须先经过一个 Core-owned 的 lowering 步骤，把层级结构整理成 leaf records。

这就是 `PrefabFinalizationPipeline` 这类能力真正应该承担的责任：

- 展开嵌套 prefab
- 组合 transform
- 合并颜色
- 处理 grounded part
- 产出 adapter 可以直接消费的 finalized leaves

## 为什么 adapter 不能自己决定

因为 adapter 一旦自己决定，就会很快出现行为分叉。

最典型的问题就是：

- 静态路径做了 prefab finalization
- skinned 路径没做
- Raylib 做了 grounding
- UE5 某条路径没做 grounding

这时候同一个 prefab，在不同 adapter、不同 lane 下会出现不同结果。

这不是“实现细节不同”，而是正式 contract 被破坏了。

正式规则应该是：

- 只要某条 runtime visual path 允许消费 prefab
- 那它就必须消费 finalization 后的统一结果

如果某条 lane 根本不支持 prefab，也可以。

但这件事必须是显式 shared validation，而不是静默绕过。

## 这三个 issue 分别在解决什么

### Issue #121

链接：
[#121 Add an explicit core-owned visual height asset contract for map loading](https://github.com/MightyBubble/Ludots/issues/121)

它解决的是：

- 地面高度真相从哪里来

人话就是：

- 地图加载时，必须能正式声明“这张地图用哪个 visual height asset”
- Core 必须在正常 map load 流程里把这个 asset 绑定成 `IVisualHeightmap`
- 不是靠 showcase runtime 临时注入
- 不是靠 adapter 自己猜

这一步是前提。

如果这一步没做好，后面任何 grounding 都会失去统一真相。

### Issue #116

链接：
[#116 Ensure UE5 skinned runtime paths honor prefab finalization and grounding contracts](https://github.com/MightyBubble/Ludots/issues/116)

它解决的是：

- 所有消费路径是否都吃同一份 prefab finalization contract

人话就是：

- prefab 只要被允许进入某条 render lane
- 那这条 lane 就必须先吃 finalization 结果

如果某条 lane 不支持 prefab，就要显式报错。

不能出现：

- static lane 先 finalization
- skinned lane 直接绕过去

这个 issue 表面看是 UE5 问题，实际本质是 shared contract 一致性问题。

Core 仓库只保留 shared contract 和平台无关测试；UE5 render bridge 的具体实现与 adapter wiring 测试归开发者仓库维护。

### Issue #119

链接：
[#119 Add a batch-oriented grounding path for grounded prefab parts](https://github.com/MightyBubble/Ludots/issues/119)

它解决的是：

- 在语义不变的前提下，把 grounding 执行得更高效

人话就是：

- 现在的 grounding 太偏 per-part 了
- prefab 很大或实例很多时，会有重复采样和重复 raycast
- 所以需要 batch-oriented path

但注意，这不是第一步。

它不是在定义 grounding 语义，而是在优化 grounding 的执行形状。

## 正确的推进顺序

这块工作最容易犯的错误，就是直接盯着性能 issue 开始做。

正确顺序应该是：

1. 先做 `#121`
2. 再做 `#116`
3. 最后做 `#119`

原因很简单：

- 先解决“真相从哪来”
- 再解决“所有路径是否都遵守同一 contract”
- 最后再解决“怎样更快”

如果反过来，最后大概率会得到一套更快、但语义仍然分叉的系统。

## 推荐的正式边界

### Core 负责什么

- map load 时绑定 visual height asset
- 提供统一的 `IVisualHeightmap`
- 负责 prefab finalization
- 负责 grounded part 的统一 lowering
- 负责 shared validation

### adapter 负责什么

- 消费 finalized leaves
- 把 leaf 结果变成各自平台的渲染提交
- 不拥有 grounding 语义
- 不拥有 visual height 真相

### projector 负责什么

如果系统还保留 `IVisualGroundProjector` 之类的接口，它更适合被理解为：

- 一种加速执行的工具接口

而不是：

- 地面真相本身

也就是说，projector 可以帮助 shared grounding pipeline 跑得更快，但不能变成另一套独立语义来源。

## 推荐的需求表述

以后再讨论 prefab grounding，建议统一用下面这套口径：

- visual height 是 map-owned 的 Core service
- prefab 是 authored hierarchy，不是 adapter-ready mesh
- finalization 是 prefab 到 render leaf 的正式 lowering 步骤
- grounding 是 finalization 的一部分，不是 adapter 私活
- batch grounding 是优化，不是新语义

## 最后的判断标准

如果一项改动满足下面四条，就说明方向大概率是对的：

- 它没有新增第二套 visual height truth
- 它没有让 adapter 再次拥有 prefab 语义决定权
- 它没有让某条 render lane 静默绕过 finalization
- 它只是让 shared contract 更完整或更快

如果做完以后出现这些现象，就说明方向错了：

- 某个 adapter 结果和别的 adapter 不一样
- 某个 showcase 必须手工塞 runtime 才能工作
- 某条 skinned/static lane 有隐藏特判
- 为了性能又造出第二条 grounding 管线

## 配套深度材料

- `docs/architecture/presentation_snapshot_contract.md`
- `docs/architecture/persistent_static_adapter_sync.md`
- `src/Core/Presentation/Assets/PrefabFinalizationPipeline.cs`
- 开发者仓库中的商业引擎 adapter render bridge
- `src/Tests/PresentationTests/PrefabFinalizationAndVisualHeightmapTests.cs`
- `src/Tests/PresentationTests/PresentationFoundationTests.cs`
