# 四皮面板：工程结构与换肤合同

一图流：**面板是纯投影，皮是渲染适配器，换皮=换适配器，不是换数据**。本页是四皮 showcase（`panel_skin_markup/compose/reactive/web`）的工程 SSOT。

## 结构

**作者拓扑（#1011 修正后）**：皮肤是引擎能力，不是作者负担。mod 作者 0 编码——写模板/图/地图 JSON，面板自动可见；换皮 = game.json 一行 `"panelSkin"`。

```text
FireballSharedMod（共享玩法，无皮，自带可玩钥匙）
  assets/Panels/panel_templates.json   模板：变量声明+取数来源（health/mana/attack realtime + healthBase/manaBase）
  assets/GAS/graphs.json               Graph.Fireball.Panel.OpenStatus（TriggerGraph：CreatePanel → ShowPanel）
  assets/game.json                     startupMapId/输入上下文/座位（可玩钥匙归共享 mod 所有）
  assets/Maps、GAS、Input、Runtime/     火球玩法（弹道/耗蓝/伤害，全数据）

引擎侧（src/Libraries，作者零接触）
  Ludots.UI.Panels                     PanelPresentationSystem：可见实例→自动布局上屏（变量→行，X/XBase→成对"cur / base"）；
                                       PanelSkinCatalog：default/markup/compose/reactive 四个 accent 变体；PanelPresentationInstaller
  Ludots.WebUI.Browser/PanelWebSkin    "web" 皮肤：CEF 离屏表面 + DataPlane（topic=ludots.panel.<templateId>，变量全量平铺）；
                                       由宿主在浏览器运行时装好后接装；headless 无运行时则面板数据照活、无表面
  宿主接线                             RaylibHostComposer/WebHostComposer/AcceptanceUiHostInstaller 统一调 PanelPresentationInstaller.Install

panel_skin_{markup,compose,reactive,web}（四个选皮演示 mod——0 C#，纯声明）
  mod.json     依赖 FireballSharedMod；无 main（asset-only，launcher ResourceOnly 一等公民）
  game.json    "panelSkin": "markup|compose|reactive|web" + 窗口配置；web 另需 browserRuntime 块 + "panelWebApp"（overlay 首页的 mod-VFS 路径）
  （web 专属）assets/overlay-app/ 页面三件（index.html/styles.css/main.js）
```

四个皮 mod 没有任何 C#、csproj、DLL；它们存在的意义只是给画廊/启动器四个可玩的选皮变体。

## 生命周期与数据流

1. 装载地图 → TriggerGraph 挂载触发器在 MapLoaded 入口跑图 → `CreatePanel` 建实例（scope=hero）→ `ShowPanel` 写入激活商店。
2. 引擎侧 `PanelPresentationSystem`（表现系统）每帧扫可见实例 → 租 `UiSurfaceSegment.Main` → 发布自动布局 UI → 每帧 `Invalidate`。
3. realtime 变量由 `PanelRealtimeRefreshSystem`（Cleanup 组）重算，修订号变化才真正重画。
4. 面板值全部来自 `PanelProjectionReader` 五路读嘴（SingleAttribute/AttributeBase/Derived/GraphOutput/TableLookup），fail-closed。
5. 显隐唯一写入口 `PanelActivationApi`（合同五）；皮渲染只读激活商店。

## 换肤现状与 CSS 合同

**解析链（四级，#1011）**：`CreatePanel 图 op panelSkin` > `模板 skin 字段` > `game.json panelSkin` > `default`。皮是实例属性：同屏可混排四皮（原生按实例取 accent，web 按实例建 CEF 表面）；`panelZOrder`（缺省 100）直通租约优先级，对齐虚幻 ZOrder。实例句柄出值边（虚幻 Create Widget 返回引用喂下游）需图 VM 新值类型，已登记下一片。

**今天**：换皮 = game.json `"panelSkin"` 一行（default/markup/compose/reactive 为 accent 变体，web 为浏览器皮）。引擎侧渲染按模板变量声明自动布局，无任何皮 C#。

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

`src/Tests/GasTests/Production/PanelFireballShowcaseAcceptanceTests.cs`：五用例全绿——
`PanelFireballDefaultSkin_NoSkinMod_ZeroCodePanelBecomesVisible`（**零编码主用例**：无任何皮 mod，仅共享玩法三 mod，面板自动可见+数值活）；
原生三皮各一 TestCase（加载对应 asset-only 选皮 mod，断言全链）；web headless 用例（无 CEF 宿主：面板与变量全链成立）。
launcher 预设：`preset:panel_skin_{markup,compose,reactive,web}_raylib`。

## 边界

- 皮不算数：渲染一律只读 `PanelVariableSet` 与激活商店；引擎侧系统不查 World。
- 显隐唯一写入口 `PanelActivationApi`；图必须显式 `ShowPanel`（旧皮不查激活的宽松期已废除）。
- 可玩钥匙（输入上下文/座位/启动地图）归共享玩法 mod 所有，皮 mod 只带选皮与窗口声明。
- fail-closed：未知 panelSkin 直接抛错列出已知项；`web` 缺 `panelWebApp` 同样抛错。
- `ref/` 目录式的构建残渣不得入库（已清理过一轮，csproj 不引用即删）。
