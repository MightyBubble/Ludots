# CrowdSimulation 移植交接

合入状态认本页。阶段和规矩认 [导航体系重构](crowdsimulation-and-navsurface.md)，样板、演示、数据页挂在它下面。本页不改那些合同。尖端一变就改本页，不要另写一份交接。

核对时间 2026-10-16。集成分支 `cursor/crowdsim-integrate-af78` 尖端 `d06634cd6d622c2c781ad76d6498f6cbadc473dc`。底座 `feat/crowdsim-navsurface` 尖端 `ee83bd8ce574304aee0c7f50c51632a076f837e9`。

## 合完了没有

没有全部合完。

集成分支比底座超前 730 个提交，底座上的提交都已经在集成分支里。集成分支还停在 `cursor/crowdsim-integrate-af78`。

这次用 `git ls-remote` 扫了远端 `cursor/crowdsim-*`、`cursor/l*-af78` 和 `feat/crowdsim-navsurface`。还没进集成分支的，是下面三组。

### Markup HUD

`cursor/crowdsim-hud-markup-af78` 尖端 `8fd77c34ad5a29ab90a9a26d0c37045ae381de12`，超前 11 个提交。草稿 [PR #1811](https://github.com/MightyBubble/Ludots/pull/1811)，基线是集成分支。

集成分支上的 HUD 仍由 `mods/capabilities/navigation/CrowdSimulationUiMod/UI/CrowdHudController.cs` 建 `ReactivePage<CrowdHudState>`。这 11 个提交换成 Markup：`assets/CrowdSimulationUi/hud.html` 加已有的 `hud.css`，读数走 `UiTextBinding` 和 `UiVectorDrawList`。文本和矢量改动只重画损伤区域；结构、样式、尺寸、交互变化整画。`GlyphTable.Remember` 第一次建表时解析

```text
0123456789+-. ,%/·–—²…KBMNAx
```

源码里中间点、短横、长横、上标 2、省略号写成 `\u00B7\u2013\u2014\u00B2\u2026`。堆速率第一次出现负号时，这个字符已经在表里。

11 个提交从新到旧：

- `8fd77c34ad` 读数变化只重画改动区域，数字格式字符在字号表建好时就解析完
- `64e6dbeb9b` HUD 配置对齐原型：鼠标操作说明、告警阈值、曲线长度、S4 默认开遥测
- `a15c514edb` HUD 改成照原型搬的 Markup 文档，删掉 ReactivePage 版
- `fcd3a99271` 固定步频率窗口，供偏好页画位置
- `8af2ef98c5` 按兵种读所在移动层的单位数
- `82d0039fba` `UiElement.ClearChildren`，code-behind 按名单重生成子节点
- `41cbea0041` 矢量绘制表加按像素半径的实心圆
- `00f05ff03f` 动作号、选项名单、最近三条指令与叠加层开关
- `a5b8f0b311` 座位动作沿注入抽成 `ClientLocalSeatActionEdges`
- `bf1886cd11` Markup code-behind 绑定走 `UiSurfaceHost` 租约发布也不丢
- `3596899566` 后端无关的矢量绘制表 `UiVectorDrawList`

Release，跑在该分支上，过滤结果：

- `CrowdHudRetainedRepaintTests`（2）、`CrowdHudAllocTests`（4）、`AllPanelsVisible_FramesAllocateNothing`（缓存、检视各 1）：8 过，0 失败，1 分 5 秒。后两例的窗口是 30 秒、帧 16 ms，断言采样帧和非采样帧 0 字节、窗口内 0 次 GC、重画折算 CPU 时间 p95 ≤ 1.5 ms（名义参照光栅 0.87 ms）、开 HUD 与关 HUD 的 tick 中位数落在噪声内。测到的 p95 毫秒数没有留在仓库里。
- `CrowdHudLayoutTests`、`CrowdHudSamplingTests`、`CrowdTelemetryPanelTests`、`ShowcaseHudTests`、`CrowdPlayerViewTests`：44 过，30 秒。
- `UiVectorDrawListTests`、`MarkupSurfaceBindingTests`、`UiTextBindingTests`、`UiLayoutAllocationTests`：31 过，689 毫秒。

`CrowdHudRetainedRepaintTests` 在 1920×1080 和 1280×720 上各跑 200 帧，要求损伤重绘和另起画布整画逐像素一致，并且只改读数的帧不走整画。这轮没有新的 Raylib 窗口对拍 Base44 的截图。

### 配置清理

可读尖端是 `cursor/crowdsim-cfg-d-layers-4c0c`，`f468ef0d3a2f9c62731123bb3ddc3e4fa70ea14b`，相对集成分支超前 45 个提交。下面四支的尖端都已经是它的祖先。45 是整摞相对集成分支的提交数，把四支的超前数加起来会算重。

- `cursor/crowdsim-cfg-a-history-4c0c` @ `91b72282e6aacc17015bd45c5a32705c1fe73e07`，单独看超前 5
- `cursor/crowdsim-cfg-b-ids-4c0c` @ `57f5c602e336b95dfff42c3e33390b91489d792f`，单独看超前 13
- `cursor/crowdsim-cfg-c-rules-4c0c` @ `0a11f394c7af85442f0800dc619c01ae26627192`，单独看超前 8
- `cursor/crowdsim-cfg-abc-stack-4c0c` @ `854e7f2ac97e91142cfb47f0ef7d9047add7a731`，单独看超前 30

提交说明里能看见的合同变化：未知键走严格 schema；玩家视图、调试视图、HUD、编辑器包去掉 version；`.navsurface` 去掉版本号，文件布局算进烘焙指纹；玩家号反查留在配对规则内部；批量部署按配对表上的玩家号找归属；层开关按单位行；单位模板去掉 `personalRadiusCm` 和 `agentTypeId`；删掉 `spawn.targetSearchTries` 和空的 world；`seed` 必填；速度键 `speedCmPerSec`；`special` 改成 `autoDeploy`，取值反转；颜色收成共用的 `red` / `green` / `blue` / `alpha`；presentation 去掉 kind，由 deploy 或 path 块决定；删掉只给 HUD 文案用的 `rates.tickHzMin` / `rates.tickHzMax`；`turnEpsRad` 进调试视图。合之前看 `cfg-d` 的 diff。键名和 `autoDeploy` 的极性要审。

### c4 文档

`cursor/crowdsim-c4-solver-lifecycle-af78` 尖端 `a90fb6741a53301fdcc1f24b26dd61cc39f3b8c2` 只多一个提交：删掉 S4 演示里对不上脚本的人数和历史句。其余已经在集成分支里。

## 尖端已经在集成分支里

下列尖端是集成分支的祖先。SHA 是 2026-10-16 核对时的远端尖端。

```text
cursor/crowdsim-alloc-flake-af78 16ba408b18fa4879ad9cd0307fb44cf69daf6e1d
cursor/crowdsim-bake-digest-af78 f37605f0eab19a37ec8a4ac554941ffe5ed66d89
cursor/crowdsim-c0-spatial-vocab-af78 284d04e81cd7170b7b542fac3ded98528a8ab3e1
cursor/crowdsim-c1-config-split-af78 0451717fe0f96eed1aca230328285c439ade7975
cursor/crowdsim-c2-pair-rules-af78 f7b95f2e3bc1cf4e28985c28ce28f48e25cdbac5
cursor/crowdsim-c3-nav-schema-af78 1a6e89601b330c47f972181ff03e16b1104ca59f
cursor/crowdsim-d1-small-defects-af78 d2e19af70c05605429cbf3ca6ce327928c623bf3
cursor/crowdsim-data-schema-af78 6aac2bd5a404e95e6ea44220db1c8a59747dd6ee
cursor/crowdsim-deploy-initial-units-af78 2b448323d0156367312025c08a77af183fd57b4c
cursor/crowdsim-e02-template-inspector-af78 783b9f2ae6d577feba69c7d923ad02c84ffbdbb9
cursor/crowdsim-e03-merge-provenance-af78 8611e2746c81e24ed09d9ee2c335e064de9db268
cursor/crowdsim-e04-placement-af78 c3c9c5dc5a2f97dd80064bec00cb8de1ff5f84e9
cursor/crowdsim-editor-stroke-fill-af78 421148d369fe24360af0794048856cf0bd09e3a1
cursor/crowdsim-g01-state-digest-af78 2462c65d53bdd71cf3c31d36cf8f14dcfa110c4d
cursor/crowdsim-g02-validate-af78 92f57e6b58ee6af3d09cda778e48d68cd6c8edf2
cursor/crowdsim-gap-modcontext-leak-af78 8465a12b3c483f3b98151318d30e8416e2ee08c6
cursor/crowdsim-gastests-seat-fixture-af78 6247bcc7425be737793121e39cb673e3de0cbbdb
cursor/crowdsim-hud-followups-af78 832a26ee8282353cd8aeae5eca3874b80f7f4ad7
cursor/crowdsim-l43-selection-af78 236551d4fd0ba7c3ebf2b8d9360382969f657877
cursor/crowdsim-l75-grid-bake-af78 ddda57e745da322a20efe989dcdb5bbdac9b5ab8
cursor/crowdsim-l75-query-mesh-af78 4c4509b36b0fe4f6c9e7ba3d41e349abb0ceb1e5
cursor/crowdsim-l76-vision-stamp-af78 cad291ca2f97b5db6373890e4554518073255b59
cursor/crowdsim-l79-spawn-request-af78 9acd8a76b0b4fd8776285065086ef8442555950d
cursor/crowdsim-m01-empty-mod-af78 4b71ff57fecabf59782526b8276e4be07da3ab05
cursor/crowdsim-m02-jump-bake-af78 aada263f8843feccf2249506970af3def47fa25e
cursor/crowdsim-m02-navsurface-bake-af78 b850f6fbd954a61785511fc132d632fbdd79a5c2
cursor/crowdsim-m03-size-check-af78 6be162b3403b0f24ec25b197e0fa91762c6b66a7
cursor/crowdsim-minimal-roster-af78 ee441273a2f5c95af6fb4c7ff4996f8b72fea638
cursor/crowdsim-navsurface-ludots-bake-af78 8c9044ae36972edd76bf41853aa9a3f12c51c45b
cursor/crowdsim-p1-ecs-perf-af78 8776d906fbdf2f281c353a0aeeda60a27e49de3a
cursor/crowdsim-perf50k-headroom-af78 52123cbd890a3a555481dca2f1c2fb53cbad2e4a
cursor/crowdsim-presenter-contract-af78 647d9650947d3b55e549183aaaf5de4283ac7d71
cursor/crowdsim-purity-finish-af78 7e58e5716f2649270317a7ed462f9ad77d02a284
cursor/crowdsim-r0-integrate-main-af78 9b05416f80e2af01ff16e3c0c5b6016dc821e3b1
cursor/crowdsim-runtime-hitch-af78 9fb1a5213199d91d803bc288288dc4c3f4f8ec00
cursor/crowdsim-showcases-af78 127ff5f74d3922e5ff026c106b27d89fbcfc52e9
cursor/crowdsim-stray-extend-parity-af78 0d9f4611fd4961c566196adb034ac89d38582a30
cursor/crowdsim-stray-flowcache-af78 fd4729ce524436fe44645fca38972f26bc4e86ed
cursor/crowdsim-sweep-cleanup-af78 7d4846bb935cb51e169c284005143968af15b6d3
cursor/crowdsim-t1-telemetry-hud-af78 c6d0459321a93be24482f77cf35076ee86280243
cursor/crowdsim-terrain-type-overlay-af78 06211c14e52bbd98853b22c005f54f693ad3de2c
cursor/crowdsim-uat-fog-af78 6b2cca2205875964c2f54d098756d2e25aedcae2
cursor/crowdsim-uat-movement-af78 fbdb1abba27f763508b81f35a8a9500b57d5a19f
cursor/crowdsim-uat-terrain-af78 b156a3d8f280a079460a3f657b6b3228ebe649b6
cursor/crowdsim-ui-infra-af78 d88a8dc2e3d9c082728b0780837449e1622b4cfa
cursor/crowdsim-web-config-editor-af78 5a3e9697bf992fce3199781254a0fdb5ef8f7268
cursor/crowdsim-web-debug-tools-af78 c3b03b42ef12980f8690c290f17dfe900d2e1a1a
cursor/crowdsim-web-host-af78 73d055093fe86d3ff95f781b99728e4b657e254a
cursor/crowdsim-web-overlays-af78 a0b681a582ac363ae18d54d0675f792341e6ae7f
cursor/crowdsim-web-parity-fixes-af78 e6cc6952e31741d7ee3cb0caf9ef1cffc195ca73
cursor/crowdsim-web-player-view-af78 6aa49fa566c4320291ea2635a3908dd1b3ebf99e
cursor/crowdsim-web-telemetry-panel-af78 d3d4933109f30a4d8f26e704946cf4487e542265
cursor/crowdsim-web-validation-fuzz-af78 91d242c30230ed2a784b09832890232d7da62034
cursor/l39-l65-sort-without-linq-af78 b3fc7c73e8b0fdd0c30638a6521a10e2e4e004a7
cursor/l62-crowd-50k-33ms-af78 6272d930ad3d857fadc05c71c3d553993056ead1
cursor/l64-fog-area-fixture-af78 bafe6db0a72f7b3bc9585a684ea354b463860f2f
cursor/l68-test-contracts-af78 949ed8247f4c0792634085ddc9aad7f31957fc22
cursor/l69-mulexact-comment-af78 163bd0c4f16b7675b6986d241c8ddaa7aabbb1ac
cursor/l70-structures-readonly-expiries-af78 a019c0f06b83e566fd754fdd2c8941a23924e506
cursor/l74-fog-stats-incremental-af78 a8ef727ced74373f597634d8b97321ee677cb859
cursor/l86-fog-cell-min-af78 7419126050417f23f49e2cd127c268f901557a7c
cursor/l87-s4-overlay-views-af78 3cfbcff6cabaa7f87218eb78e291f2ed7e8df60e
feat/crowdsim-navsurface ee83bd8ce574304aee0c7f50c51632a076f837e9
```

`cursor/crowdsim-web-*` 在这份名单里，表示那些提交是集成分支的祖先。群体演示接 Web 宿主的合并是 [#1807](https://github.com/MightyBubble/Ludots/pull/1807)（`d6d9781cc5`）。撤回提交 `32edb3f18e` 去掉了 `WebHostCrowdShowcaseTests`，并收回了当时加进 Web 宿主回路的群体接线。验收在 Raylib。平台宿主还在 `src/Adapters/Web/Ludots.Adapter.Web`。

内核 5 万单位的 tick 门在集成分支的 `CrowdKernel50KBenchmarkTests`：优化构建上 P95 低于 33 ms。写本页时没有重跑这道门。

通行矩阵在 `src/Core/CrowdSimulation/Config/CrowdTraversalConfig.cs`。行 × 区域不可通行写 false，可通行写 `cost`、`speedDivisor`、`depart`、`arrive`。链接种类另写 `upCm`、`downCm`、`rangeNavCells`、`costPerNavCell`。

[数据结构](crowdsimulation-data-schema.md) 页首仍写「设计稿，代码还是旧格式」。上面这个类型已经在集成分支上。页首和代码不一致时，以代码为准。`cfg-d` 还要再改键，合进去之前别按页首去改代码。

数值以 `Fix64` 为准。行为对拍的口径写在导航页 2026-10-09 那一段。

## 下一手

1. 审 [#1811](https://github.com/MightyBubble/Ludots/pull/1811)。HUD 的下一版就是这支 Markup。
2. 审 `cursor/crowdsim-cfg-d-layers-4c0c` 的 diff。`cfg-a`、`cfg-b`、`cfg-c`、`cfg-abc-stack` 跟着它走，各开一次合并会重复。
3. c4 那个文档提交可以跟着配置清理走，也可以单独带上。
4. 集成分支要不要进 `feat/crowdsim-navsurface`，另说。本页不代合。
5. [#1807](https://github.com/MightyBubble/Ludots/pull/1807) 保持撤回。

## 再核一次

```bash
git fetch origin cursor/crowdsim-integrate-af78 feat/crowdsim-navsurface
git rev-list --count origin/feat/crowdsim-navsurface..origin/cursor/crowdsim-integrate-af78
git merge-base --is-ancestor <tip> origin/cursor/crowdsim-integrate-af78
```

第二条在 2026-10-16 的结果是 730。尖端变了，730 和上面的名单就过期。
