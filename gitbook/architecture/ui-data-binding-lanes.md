# UI 取数与呈现车道——现状图

> 本页是面板线的**施工底图**：把"游戏数据 → 看得见的东西"现有的五条机制摆清楚，指出重复，给出收敛路径。渲染皮层的统一归 [ADR-0002](../../docs/adr/ADR-0002-unified-ui-runtime-and-authoring-models.md)（三写法一套 UiScene）；面板合同归宪法 issue #858。本页只回答一个问题：**数据从哪来、谁在取**。

## 1. 五条车道

| # | 机制 | 出生 | 驱动方式 | 取数语言 | 管辖表面 | 真实消费者 |
|---|------|------|----------|----------|----------|------------|
| 1 | Presenter 声明式绑定 | 初版（2026-02）持续演进 | 脏位推送 + 可见者每帧重解析 | `ValueRef` 12 种来源（属性直读/比值/基值、跑图、实体色、朝向、黑板、指针坐标…） | 世界空间：蒙皮网格、世界 HUD（头顶条/字）、屏幕 HUD 行 | 全部世界视觉（massnav 实测 presenterActive 30009、worldHud 20000、screenBars/screenText 各 10000） |
| 2 | EntityInfoPanelsMod 自营 | 2026-04（#163–#167 线） | 自研 Service 取样/格式化/渲染 | 自研（Sample/Storage/Format/Insight + 自营模板目录） | 屏幕面板：实体信息卡 | 该 mod 自身 |
| 3 | PanelKit manifest | 2026-07 | manifest 声明 + C# 登记 descriptor | topic/profile/layout/density 引用 | 屏幕面板：HUD 布局 | 7 个 panel_kit_* 试点 + 小地图（车道冻结，#850/#1112 待裁决） |
| 4 | PanelHost pin | 2026-08（#1026） | 图求值（realtime 每帧 / snapshot）→ GraphOutputValueStore | 图输出 float | 屏幕面板：标量 | 仅 float；Bool/Entity/String 缺（#1010） |
| 5 | PanelHost collection | 2026-08（#1390 起） | 图求值产出包 → 列表投影 | `PanelSubjectKind` 13 种 subject（Entity/Ability/AbilitySlot/AbilityDefinition/Task/Tag/DialogueChoice/Item×2/Effect×2/Activity/ProgressionNode），实体包 + IntId 包 | 屏幕面板：列表 | 写表时（10-05）玩法 mod 仅叙事选项面板（showcase 侧另有 panel_collection_bags 族 9 场景在消费）；#1728/#1733 后新增总督档案卡（Entity 组）、总督指令卡（AbilitySlot 组）与面板按钮下令技能格（#1608） |

代码锚点：车道 1 在 `src/Core/Presentation/Presenters/`（`ValueRef.cs`、`PresenterParamBinding.cs`、`BehaviorSlot.cs` 的 `AttributeBindingConfig`、`PresenterDefinition.cs` 的 OwnerAttribute/TagWorkItem）；车道 2 在 `mods/capabilities/entityinfo/EntityInfoPanelsMod/`（无 PanelHost 引用）；车道 4/5 在 `src/Core/UI/PanelHosting/` 与 `src/Core/UI/PanelProjection/`（`PanelGraphEvaluator.cs`、`PanelRealtimeRefreshSystem.cs`、`PanelListProjector.cs`）。

## 2. 重复在哪

1. **"组"投影有三份**。车道 5 在 Core 里建好了 Entity 组与 Ability 组；但实体指令面板（`mods/EntityCommandPanelMod/`，自研 `ability_aggregation_profiles.json`）与实体信息卡（车道 2）都没用它，各自手写了聚合。**高速公路旁边踩着两条土路。**
2. **取数语言有三套心智**。车道 1 的 13 种 ValueRef（可直读属性）、车道 4 的图+float、车道 5 的图+bag。注意：presenter 直读属性**不违宪**——宪法 #858 管的是面板，表现面的属性直读是 attr-06 表现面的正统路径；但它与面板侧"变量由图算"（合同三）构成两套脑子。
3. **屏幕 UI 的皮有四份**。车道 1 的屏幕 HUD 行、车道 2 自营、EntityCommandPanelMod 自营、车道 4/5 的 PanelHost。

## 3. 定性与收敛

EntityInfoPanelsMod 与 EntityCommandPanelMod 是**业务聚合时代的产物**：在抽象车道（PanelHost pin/collection）建成之前，按各自业务手搓的聚合层。车道通了之后没有搬回来，Core 里的组投影长期只有叙事一个消费者——该状态已被 #1726 两刀（#1728/#1733，作者面上车道）终结；两个 mod 的 C# 自营层退役是后续票。

收敛路径（= PANEL epic 的实际施工序）：

1. **迁移优先于造新**：EntityInfo 先迁到车道 5（Entity 组 + IntId 组投影），EntityCommandPanel 随后（Ability/AbilitySlot 组）。每迁一个，就是 PANEL-2 四皮矩阵的一次实战，也是 #841 目录的种子内容。
2. **pin 补类型是顺手事**：Bool/Entity/String 标量 pin（#1010 剩余范围），不阻塞迁移。
3. **PanelKit 归位 L3 表面**：manifest 当布局语言，descriptor 目录化后与 PanelHost 模板对接（#841）。
4. **presenter 绑定留在世界空间**：它的脏位推送、LOD、裁剪是面板不需要的能力；attr-06 表现面合同继续管它。

## 相关

- 宪法：issue #858（六层模型与完成核）
- 数据合同剩余范围：#1010；数据面审计缺口：#1089；目录即数据：#841
- 迁移战役票：#1726
