# 四皮面板：工程结构与换肤合同

一图流：**面板是纯投影，皮是渲染适配器，换皮=换适配器，不是换数据**。本页是四皮 showcase（`panel_skin_markup/compose/reactive/web`）的工程 SSOT。

## 结构

```text
FireballSharedMod（共享基建，无皮）
  assets/Panels/panel_templates.json   模板：变量声明+取数来源（health/mana/attack realtime + healthBase/manaBase）
  assets/GAS/graphs.json               Graph.Fireball.Panel.OpenStatus（kind:TriggerGraph）
  assets/Maps/fireball_arena.json      MapTriggerGraphs 挂载（scopeInstanceId=fireball-hero）
  assets/GAS/{abilities,effects}.json  火球玩法（弹道/耗蓝/伤害，全数据）
  assets/Input/                        SkillQ=<Keyboard>/q + 自动索敌
  Runtime/                             仅输入 order source 装配；零面板逻辑

UiShowcaseCoreMod/Showcase/FireballPanelShowcaseMounting.cs（皮的唯一 C#）
  InstallSkinSurface(context, ownerId, skinLabel, accentR/G/B)   挂表面系统
  FireballPanelSurfaceSystem                                      找单实例→租 UiSurface→发布 UI→每帧 Invalidate
  BuildPanel                                                      UiElementBuilder 砌 UI（读 PanelVariableSet，分母走 AttributeBase）

panel_skin_{markup,compose,reactive}（三个原生皮 mod，结构同构）
  Entry.cs    一次 InstallSkinSurface 调用：皮名 + 主题色三字节（≈15 行）
  game.json   startupInputContexts/startupLocalSeats（启动器可玩钥匙）+ 窗口配置
  mod.json    严格 schema（name/main/version/priority/dependencies）
  csproj      仅 Ludots.Core + UiShowcaseCoreMod + FireballSharedMod 三个引用

panel_skin_web（真正的浏览器皮：CEF + WebUI DataPlane）
  Entry.cs                       IBrowserRuntime.CreateSurfaceAsync 建 CEF 表面；headless 宿主（无 BrowserRuntime）记录并跳过，面板照常创建
  FireballWebSkinTopicProducer   IWebUiTopicProducer：从 PanelHost 投影取值发 LatestWins 快照（topic=ludots.showcase.fireball.status）
  FireballWebSkinDataPlaneSystem 每帧 Invalidate 租约 + 0.25s 节流 PublishTopics
  FireballWebSkinCanvasContent   CEF 表面 → Ui.Canvas 合成（右上角 320×220）
  Assets/overlay-app/            页面三件（index.html/styles.css/main.js），走 BrowserAppResourceResolver
  game.json                      另需 browserRuntime 块（enabled/required/provider=cef）供启动器供给 CEF
```

每 mod 的文件就四样；皮间零共享代码、零交叉引用，全部差异集中在 Entry 的参数里。

## 生命周期与数据流

1. 装载地图 → TriggerGraph 挂载触发器在 MapLoaded 入口跑图 → `CreatePanel` op 建面板实例（scope=hero）。
2. 皮 mod 的 MapLoaded handler 调 `InstallSkinSurface` 注册表面系统（此刻不要求面板已存在）。
3. 表面系统首个表现帧：`RequireSinglePanelInstance` 取实例（唯一性强制）→ 租 `UiSurfaceSegment.Main` → 发布 `UiSurfaceContribution`。
4. 每帧 `Invalidate`；realtime 变量由 `PanelRealtimeRefreshSystem`（Cleanup 组）重算，修订号变化才真正重画。
5. 面板值全部来自 `PanelProjectionReader` 五路读嘴（SingleAttribute/AttributeBase/Derived/GraphOutput/TableLookup），fail-closed。

## 换肤现状与 CSS 合同

**今天**：三个原生皮共享同一个 `BuildPanel`（C# `UiElementBuilder`，颜色内联在调用里）；"换皮" = `skinLabel + accent` 参数。Web 皮已换真身——CEF 离屏表面合成进 `Ui.Canvas`，数据走 WebUI DataPlane（topic 快照，LatestWins），页面是 mod 自带 `Assets/overlay-app` 静态三件。

**Web 皮的三条硬合同**（接坑记录，页面作者必读）：
1. `window.ludotsDataplane` facade 在 `FrameLoadEnd` 之后才注入，跑在内联脚本之后——页面握手/订阅必须轮询等待 facade 出现，不能一次性判定缺席。
2. 宿主→页面走 `ludots.dataplane.control` 通道的 `MessageEvent`，`kind` 是 PascalCase（`Snapshot`/`Delta`），页面判断需大小写不敏感；`data.payload` 是 wire packet 的 JSON 字符串。
3. 合成画布节点只在表面租约 Invalidate 时重采样 CEF 最新帧——皮系统必须像原生皮一样每帧 `Invalidate`，否则 DOM 更新永远停在首帧画面。

**CSS 引擎存量**：`Ludots.UI.HtmlEngine.Markup.UiCssParser`（ExCSS）→ `UiStyleSheet`（选择器/声明/keyframes）→ `UiDocument.StyleSheets` → `UiStyleResolver` 应用。样例资产：`UiShowcaseCoreMod.Assets.Showcase.*.css`（程序集内嵌资源，C# 读取加载）。

**还没有的（要做"mod 纯 CSS 换皮"需补三件）**：
1. 样式外提：`BuildPanel` 的样式从 C# 调用抽成 CSS（元素类名/选择器契约，如 `.panel.fireball .hp`）；
2. 皮样式声明面：mod 携带 `panel-skin.css` 数据文件、按皮 id 声明与加载（走 mod 资产管线而非内嵌资源）；
3. 覆盖合同：下游 mod 覆盖上游样式的优先级/显式 `overrideOf`（对齐 graphs 家族的整图替换合同）。

这三件归 #858 面板 epic 的后续切片；在本页登记，勿另开 SSOT。

## 验收

`src/Tests/GasTests/Production/PanelFireballShowcaseAcceptanceTests.cs`：原生三皮一套夹具三用例（每皮一个 `[TestCase]`），全链断言（单实例、变量真值含分母、Q→GAS 结算→realtime 刷新、表面挂载）；Web 皮单独用例 `PanelFireballWebSkin_HeadlessHost_SkipsCefOverlayButCreatesPanel`（headless 宿主无 CEF：跳过表面但面板与变量全链成立）。launcher 预设：`preset:panel_skin_{markup,compose,reactive,web}_raylib`。

## 边界

- 皮不得查 World 找实体、不得算数值——数据一律经 PanelVariableSet（历史上的反例已在收敛批删除）。
- 皮 mod 间禁止互相引用；共享逻辑一律上移 UiShowcaseCoreMod 或 Core。
- `ref/` 目录式的构建残渣不得入库（已清理过一轮，csproj 不引用即删）。
