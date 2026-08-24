# Presenter 快速上手：10 分钟让第一个可视物出现在画面上

第三方 mod 作者入口。架构全景见 [Presenter-as-Actor 架构设计](presenter-as-actor-architecture.md)，配置文件字段全表见 [Raylib 渲染配置结构](../reference/raylib-render-config-structure.md)。本文只回答一件事：**照抄什么、放哪、怎么跑**。

Presenter 路线的分工：你的 mod 只声明"什么事件出现什么可视物"（纯 JSON，零 C#），引擎负责把它变成 draw buffer，raylib 适配器负责画。**引擎核心 mod（LudotsCoreMod）已内置 `cube`/`sphere` 图元与 `default_surface` 材质**，最小例子一个模型文件都不用带。

## 你要写的三个文件（全部数据，零 C#）

以"地图上摆一个大红方块"为例（完整可对照真例：`mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod/assets/`）。

**① 实体模板** 你 mod 里的 assets/Entities/templates.json——声明一个会触发 `EntitySpawned` 事件的实体（**模板 id 就是事件 key**）：

```jsonc
[{ "id": "mygame.demo_cube",
   "components": {
     "Name": { "Value": "MyGameDemoCube" },
     "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
     "FacingDirection": { "AngleRad": 0.0 },
     "AttributeBuffer": { "base": {} },
     "GameplayTagContainer": {},
     "TagCountContainer": {} } }]
```

**② Presenter 定义 + 出生规则** 你 mod 里的 assets/Presentation/presenters.json——定义"长什么样"（behaviors 组合），规则回答"何时出现/何时消失"：

```jsonc
[
  { "id": "mygame.demo_cube_actor",
    "behaviors": [
      { "slot": "body", "kind": "AssetBinding", "activeByDefault": true,
        "assetBinding": {
          "assetKind": "Mesh",
          "assetId": "cube",                      // LudotsCoreMod 内置图元
          "materialId": "default_surface",        // 内置材质
          "renderPath": "InstancedStaticMesh",
          "mobility": "Movable",
          "localScale": [400, 400, 400] },        // 世界单位 cm
        "style": { "color": [1.0, 0.25, 0.1, 1.0] } } ] },
  { "id": "mygame.demo_cube_bootstrap",
    "rules": [
      { "event": { "kind": "EntitySpawned", "key": "mygame.demo_cube" },
        "condition": { "inline": "None" },
        "command": { "kind": "CreatePresenter",
                     "definitionId": "mygame.demo_cube_actor",
                     "scopeSource": "EventPayloadA" } },
      { "event": { "kind": "EntityDestroyed", "key": "mygame.demo_cube" },
        "command": { "kind": "DestroyPresenterScope",
                     "scopeSource": "EventPayloadA" } } ] }
]
```

**③ 地图摆一个实例**——在你 mod 的 `assets/Maps/<你的地图>.json` 的 `Entities` 数组加一行（模板名 = ①的 id，Overrides 挪位置）：

```jsonc
{ "Template": "mygame.demo_cube", "Overrides": { "WorldPositionCm": { "Value": { "X": 650, "Y": 0 } } } }
```

## 跑起来

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:<你的预设> --adapter raylib
```

（预设注册在仓库根 `launcher.presets.json`，一条带 `selectors` + `adapterId: "raylib"` 即可，照抄 `"raylib_client_parity_raylib"` 那条改 id。）

预期：进地图即见红方块。**配置错了不会静默**：字段拼错、materialId 不存在、车道不匹配，装载期直接抛错终止——没有占位体、没有默认兜底。

## 三个高频坑

1. **`renderPath` 车道要配得上 `assetKind`**：静态网格走 `InstancedStaticMesh`（禁带动画）；带骨骼动画的模型走 `GpuSkinnedInstance`（真例见 parity 的 mannequin）。车道不匹配是合同错误，装载即抛。
2. **事件 `key` 必须与模板 id 完全一致**：EntitySpawned 按 id 路由，抄错不报错——但 presenter 永远不出现（事件无人认领）。
3. **换自己的模型要两行注册**：`mesh_assets.json` 声明句柄（`type: "Model"`），`host_assets.json` 按 `backendId: "raylib"` 绑源文件（`sourceUris` 指向你的 GLB，用 `"ModName:assets/..."` 虚拟 URI）——贴图、材质参数也在 host 侧，字段表见 [Raylib 渲染配置结构](../reference/raylib-render-config-structure.md)。

## 下一步

- 全部配置文件的字段语义与 fail-loud 边界：[Raylib 渲染配置结构](../reference/raylib-render-config-structure.md)
- 定义还能组合什么（树形 children、参数黑板、Animator、贴花、粒子）：[Presenter-as-Actor 架构设计](presenter-as-actor-architecture.md)
- 粒子效果写法：[Quarks 粒子 Schema](quarks-particle-schema.md)
- 20 个引擎能力场景（一键跑，带验收截图）：[Raylib 引擎能力总览](raylib-engine-capabilities.md)
