using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using HudCssStylingMod;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// 有限 CSS 样式验收：showcase 资产（mods/showcases/hud_css_styling）声明 css 后，
    /// 样式逐层到达发射条目与屏幕坐标；屏幕像素偏移与相机距离无关；
    /// 解析器对未知属性/非法值/重复声明一律拒绝。
    /// </summary>
    [TestFixture]
    public sealed class HudCssStylingTests
    {
        private string _root = string.Empty;

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    dir = Path.GetDirectoryName(dir);
                }

                return dir ?? throw new InvalidOperationException("repo root not found");
            }
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_HudCssStyling", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void CssParser_ValidDeclarations_MapEverySupportedProperty()
        {
            WorldHudStyle style = WorldHudStyleCss.Parse(
                "width: 46px; height: 5px; font-size: 13px; opacity: 0.9; color: #E2493B; background-color: rgba(16, 18, 24, 0.85); translate: 0 -34px",
                "test");

            Assert.That(style.Width, Is.EqualTo(46f));
            Assert.That(style.Height, Is.EqualTo(5f));
            Assert.That(style.FontSize, Is.EqualTo(13f));
            Assert.That(style.Opacity, Is.EqualTo(0.9f));
            Assert.That(style.Color!.Value.X, Is.EqualTo(0xE2 / 255f).Within(0.001f));
            // 6 位 hex 无 alpha 通道,透明度为 1。
            Assert.That(style.Color!.Value.W, Is.EqualTo(1f));
            Assert.That(style.BackgroundColor!.Value.X, Is.EqualTo(16f / 255f).Within(0.001f));
            Assert.That(style.BackgroundColor!.Value.W, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(style.Translate!.Value, Is.EqualTo(new Vector2(0f, -34f)));
        }

        [TestCase("width: 5em")]
        [TestCase("opacity: 1.5")]
        [TestCase("color: not-a-color")]
        [TestCase("font-size: 0")]
        [TestCase("translate: 10px")]
        [TestCase("width: 10px; width: 12px")]
        [TestCase("")]
        public void CssParser_RejectedDeclarations_FailFast(string css)
        {
            Assert.Throws<InvalidOperationException>(() => WorldHudStyleCss.Parse(css, "test"));
        }

        [Test]
        public void CssParser_TypographyDeclarations_MapEveryProperty()
        {
            WorldHudStyle style = WorldHudStyleCss.Parse(
                "border: 2px solid #FFFFFFFF; border-radius: 4px; padding: 3px; " +
                "background: linear-gradient(to right, #101218, #282C38); color: linear-gradient(#E2493B, #FFB020); " +
                "font-weight: bold; font-style: italic; text-align: center; text-shadow: 1px 2px 3px rgba(0,0,0,0.85)",
                "test");

            Assert.That(style.BorderWidth, Is.EqualTo(2f));
            Assert.That(style.BorderColor!.Value.W, Is.EqualTo(1f));
            Assert.That(style.CornerRadius, Is.EqualTo(4f));
            Assert.That(style.Padding, Is.EqualTo(3f));
            Assert.That(style.BackgroundColor!.Value.X, Is.EqualTo(0x10 / 255f).Within(0.002f));
            Assert.That(style.BackgroundGradientTo!.Value.X, Is.EqualTo(0x28 / 255f).Within(0.002f));
            Assert.That(style.Color!.Value.X, Is.EqualTo(0xE2 / 255f).Within(0.002f));
            Assert.That(style.FillGradientTo!.Value.X, Is.EqualTo(0xFF / 255f).Within(0.002f));
            Assert.That(style.Bold, Is.True);
            Assert.That(style.Italic, Is.True);
            Assert.That(style.TextAlignCenter, Is.True);
            Assert.That(style.ShadowColor!.Value.W, Is.EqualTo(0.85f).Within(0.01f));
            Assert.That(style.ShadowOffsetX, Is.EqualTo(1f));
            Assert.That(style.ShadowOffsetY, Is.EqualTo(2f));
            Assert.That(style.ShadowBlur, Is.EqualTo(3f));

            WorldHudStyle barStyle = WorldHudStyleCss.Parse("box-shadow: 1px 1px 2px rgba(0,0,0,0.6)", "test2");
            Assert.That(barStyle.ShadowColor!.Value.W, Is.EqualTo(0.6f).Within(0.01f));
        }

        [TestCase("image: url(foo.png)")]
        [TestCase("image: ")]
        [TestCase("clip-path: polygon(0 0, 1 1)")]
        [TestCase("clip-path: circle(50%)")]
        public void CssParser_ImageAndClipRejectedDeclarations_FailFast(string css)
        {
            Assert.Throws<InvalidOperationException>(() => WorldHudStyleCss.Parse(css, "test"));
        }

        [Test]
        public void CssParser_ImageAndClipShape_MapCorrectly()
        {
            WorldHudStyle style = WorldHudStyleCss.Parse(
                "image: hud_css.icon.capital_star; clip-path: shield",
                "test");
            Assert.That(style.ImageAssetId, Is.EqualTo("hud_css.icon.capital_star"));
            Assert.That(style.ClipShape, Is.EqualTo(HudClipShape.Shield));

            WorldHudStyle diamond = WorldHudStyleCss.Parse("clip-path: diamond", "test2");
            Assert.That(diamond.ClipShape, Is.EqualTo(HudClipShape.Diamond));

            WorldHudStyle sliced = WorldHudStyleCss.Parse("image-slice: 12px", "test3");
            Assert.That(sliced.ImageSlice!.Value, Is.EqualTo(new Vector4(12f, 12f, 12f, 12f)), "一值=四边");

            WorldHudStyle ribbon = WorldHudStyleCss.Parse("nine-slice: 12px 0", "test4");
            Assert.That(ribbon.ImageSlice!.Value, Is.EqualTo(new Vector4(12f, 0f, 12f, 0f)), "两值=上下 左右(三宫格)");
        }

        [TestCase("border: 1px dashed black")]
        [TestCase("linear-gradient(to top, #fff, #000)")]
        [TestCase("background: linear-gradient(to right, #fff)")]
        [TestCase("text-shadow: 1px red")]
        [TestCase("font-style: oblique")]
        [TestCase("text-align: justify")]
        [TestCase("image-slice: 1px 2px 3px 4px 5px")]
        [TestCase("image-slice: -2px")]
        [TestCase("nine-slice: 12px 12px 12px 12px 12px")]
        [TestCase("padding: 1px 2px 3px 4px 5px")]
        public void CssParser_TypographyRejectedDeclarations_FailFast(string css)
        {
            Assert.Throws<InvalidOperationException>(() => WorldHudStyleCss.Parse(css, "test"));
        }

        [Test]
        public void ShowcaseMap_RichStyles_ReachItemsWithDecorations()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod", "CoreInputMod", "HudCssStylingMod");
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, "hud_css_styling_map", frames: 90);

            using var projection = PresenterBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
            PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, projection, 4);

            var worldHud = (WorldHudBatchBuffer)engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)!;
            var screenHud = (ScreenHudBatchBuffer)engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)!;

            // 每单位 9 元素:绶带(三宫格)/饰板(九宫格)/士气条/生命条/战况条/名字板/首都星/军旗(PNG)/盾徽。
            Assert.That(worldHud.Count, Is.EqualTo(144), "16 单位 × 9 元素");

            int richBars = 0;
            int nameplates = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar && item.BorderWidth > 0.5f && item.Id0 <= 0)
                {
                    richBars++;
                    Assert.That(item.CornerRadius, Is.EqualTo(4f), "战况条圆角");
                    Assert.That(item.Padding, Is.EqualTo(1f), "战况条留白");
                    Assert.That(item.FillGradientTo.W, Is.GreaterThan(0f), "战况条填充渐变");
                    Assert.That(item.BackgroundGradientTo.W, Is.GreaterThan(0f), "战况条背景渐变");
                    Assert.That(item.ShadowColor.W, Is.GreaterThan(0.5f), "战况条阴影");
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    nameplates++;
                    Assert.That(item.FontSize, Is.EqualTo(16), "名字 16px 裸字");
                    Assert.That(item.StyleFlags & 0x01, Is.Not.Zero, "名字粗体");
                    Assert.That(item.StyleFlags & 0x04, Is.Not.Zero, "名字居中锚");
                    Assert.That(item.ShadowColor.W, Is.GreaterThan(0.5f), "名字投影");
                    Assert.That(item.BoxBackground.W, Is.LessThan(0.5f), "名字无底板");
                    Assert.That(item.ScreenOffsetY, Is.EqualTo(-54f).Within(0.001f), "名字悬于饰板中央");
                }
            }

            Assert.That(richBars, Is.EqualTo(16), "16 个战况条带完整装饰");
            Assert.That(nameplates, Is.EqualTo(16), "16 个名字板带底板+阴影");
            Assert.That(screenHud.Count, Is.GreaterThan(0), "投影后屏幕缓冲有内容");

            int capitalIcons = 0;
            int rankBanners = 0;
            int shieldBadges = 0;
            int slicedPanels = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar && item.Id0 > 0)
                {
                    if (item.ImageSliceTop > 0f || item.ImageSliceBottom > 0f)
                    {
                        slicedPanels++;
                        if (item.Width > 50f)
                        {
                            Assert.That(item.ImageSliceTop, Is.EqualTo(12f), "饰板九宫格上切 12");
                            Assert.That(item.ImageSliceRight, Is.EqualTo(12f), "饰板九宫格右切 12");
                            Assert.That(item.ImageSliceBottom, Is.EqualTo(12f), "饰板九宫格下切 12");
                            Assert.That(item.ImageSliceLeft, Is.EqualTo(12f), "饰板九宫格左切 12");
                        }
                        else
                        {
                            Assert.That(item.ImageSliceTop, Is.EqualTo(12f), "绶带三宫格上切 12");
                            Assert.That(item.ImageSliceRight, Is.EqualTo(0f), "绶带三宫格左右不切");
                            Assert.That(item.ImageSliceBottom, Is.EqualTo(12f), "绶带三宫格下切 12");
                            Assert.That(item.ImageSliceLeft, Is.EqualTo(0f), "绶带三宫格左右不切");
                        }

                        continue;
                    }

                    if (MathF.Abs(item.ScreenOffsetX) < 0.001f)
                    {
                        capitalIcons++;
                        Assert.That(item.ScreenOffsetY, Is.EqualTo(-74f).Within(0.001f), "首都星居叠层顶");
                    }
                    else if (item.ScreenOffsetX < 0f)
                    {
                        rankBanners++;
                        Assert.That(item.Width, Is.EqualTo(14f), "军旗宽 14");
                        Assert.That(item.Height, Is.EqualTo(18f), "军旗高 18");
                        Assert.That(item.ScreenOffsetY, Is.EqualTo(-75f).Within(0.001f), "军旗挂星标左侧");
                    }
                    else
                    {
                        shieldBadges++;
                        Assert.That(item.ScreenOffsetX, Is.EqualTo(26f).Within(0.001f), "盾徽挂星标右侧");
                        Assert.That(item.ClipShape, Is.EqualTo(HudClipShape.Shield), "防御盾徽异形裁剪");
                    }
                }
            }

            Assert.That(capitalIcons, Is.EqualTo(16), "16 个首都星图标(SVG)");
            Assert.That(rankBanners, Is.EqualTo(16), "16 面军旗(PNG 位图腿)");
            Assert.That(shieldBadges, Is.EqualTo(16), "16 个盾徽(盾形裁剪+金边)");
            Assert.That(slicedPanels, Is.EqualTo(32), "16 饰板(九宫格)+16 绶带(三宫格)");
        }

        [Test]
        public void ShowcaseAsset_Loads_AndStylesReachConfigs()
        {
            PresenterDefinitionRegistry registry = LoadShowcasePresenters();

            Assert.That(registry.TryGet(registry.GetId("hud_css_styling_health_bar"), out PresenterDefinition bar), Is.True);
            WorldHudStyle barStyle = bar.Behaviors[0].AssetBinding.HudStyle;
            Assert.That(barStyle.Width, Is.EqualTo(36f));
            Assert.That(barStyle.Height, Is.EqualTo(5f));
            Assert.That(barStyle.Translate!.Value, Is.EqualTo(new Vector2(0f, -18f)));

            Assert.That(registry.TryGet(registry.GetId("hud_css_styling_nameplate"), out PresenterDefinition text), Is.True);
            WorldHudStyle textStyle = text.Behaviors[0].WorldText.HudStyle;
            Assert.That(textStyle.FontSize, Is.EqualTo(16f));
            Assert.That(textStyle.TextAlignCenter, Is.True, "名字裸字居中锚");
            Assert.That(textStyle.BackgroundColor, Is.Null, "名字裸字无底板");
        }

        [Test]
        public void StyledBar_Emit_ProducesStyledWorldHudItem()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register("css.bar", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.WorldHud,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = new Vector3(60f, 8f, 1f),
                            MaterialParamKey = 51,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                            HudStyle = WorldHudStyleCss.Parse(
                                "width: 46px; height: 5px; color: #E2493B; background-color: #101218D9; translate: 0 -26px; opacity: 0.9",
                                "test"),
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 8f, 9f), 9501, Entity.Null, default);
            ref var state = ref world.Get<PresenterState>(presenter);
            state.BehaviorActiveMask = 1u;
            ref var scale = ref world.Get<PresenterWorldScale>(presenter);
            scale.Value = Vector3.One;
            instances.SetParam(presenter, 51, ParamLane.Float, 0.65f, 0, default);

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                CreateWorldHudProjectionGlobals(world, owner),
                animatorStates: null!,
                soundRequests: null!);
            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            ref readonly WorldHudChannelItem hud = ref requests.WorldHudAt(0);
            // css 覆盖静态 authoring(localScale 60x8)与引擎默认(40x6)。
            Assert.That(hud.Item.Width, Is.EqualTo(46f));
            Assert.That(hud.Item.Height, Is.EqualTo(5f));
            Assert.That(hud.Item.ScreenOffsetX, Is.EqualTo(0f));
            Assert.That(hud.Item.ScreenOffsetY, Is.EqualTo(-26f));
            Assert.That(hud.Item.Color1.X, Is.EqualTo(0xE2 / 255f).Within(0.002f));
            Assert.That(hud.Item.Color1.W, Is.EqualTo(0.9f).Within(0.01f));
            Assert.That(hud.Item.Color0.X, Is.EqualTo(0x10 / 255f).Within(0.002f));
        }

        [Test]
        public void ScreenOffset_AppliesAfterProjection_AndSurvivesCameraDistance()
        {
            var worldHud = new WorldHudBatchBuffer(2);
            var screenHud = new ScreenHudBatchBuffer(2);
            var view = new FixedView();
            var camera = new CameraManager();
            camera.ApplyPose(new CameraPoseRequest { DistanceCm = 16000, Pitch = 50, TargetCm = Vector2.Zero });

            for (int i = 0; i < 2; i++)
            {
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = i + 1,
                    DirtySerial = 1,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = Vector3.Zero,
                    Width = 12,
                    Height = 3,
                    Value0 = 0.5f,
                    Color0 = Vector4.One,
                    Color1 = Vector4.One,
                    ScreenOffsetX = i == 0 ? 0f : -14f,
                    ScreenOffsetY = i == 0 ? 0f : -26f,
                });
            }

            Vector2 nearDelta = ProjectAndMeasureOffset(camera, view, worldHud, screenHud, distanceCm: 16000);
            Vector2 farDelta = ProjectAndMeasureOffset(camera, view, worldHud, screenHud, distanceCm: 42000);

            // 大战略合同:偏移是屏幕像素,拉近拉远都不随透视缩放。
            Assert.That(nearDelta.X, Is.EqualTo(-14f).Within(0.01f));
            Assert.That(nearDelta.Y, Is.EqualTo(-26f).Within(0.01f));
            Assert.That(farDelta.X, Is.EqualTo(-14f).Within(0.01f));
            Assert.That(farDelta.Y, Is.EqualTo(-26f).Within(0.01f));
        }

        private static Vector2 ProjectAndMeasureOffset(
            CameraManager camera,
            FixedView view,
            WorldHudBatchBuffer worldHud,
            ScreenHudBatchBuffer screenHud,
            float distanceCm)
        {
            using var world = World.Create();
            camera.ApplyPose(new CameraPoseRequest { DistanceCm = distanceCm, Pitch = 50, TargetCm = Vector2.Zero });
            var projector = new CoreScreenProjector(camera, view);
            using var projection = new WorldHudToScreenSystem(world, worldHud, null, projector, view, screenHud);
            projection.Update(1f / 60);

            ScreenHudBarItem plain = default;
            ScreenHudBarItem shifted = default;
            bool sawPlain = false;
            bool sawShifted = false;
            foreach (ref readonly ScreenHudBarItem bar in screenHud.GetBarSpan())
            {
                if (bar.StableId == 1) { plain = bar; sawPlain = true; }
                if (bar.StableId == 2) { shifted = bar; sawShifted = true; }
            }

            Assert.That(sawPlain && sawShifted, Is.True, "both items must survive projection");
            return new Vector2(shifted.ScreenX - plain.ScreenX, shifted.ScreenY - plain.ScreenY);
        }

        [Test]
        public void ShowcaseMap_LoadsAndEmits_StyledHudForEveryUnit()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod", "CoreInputMod", "HudCssStylingMod");
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, "hud_css_styling_map", frames: 90);

            // 投影与宿主循环同序:Tick 之后驱动世界→屏幕投影。
            using var projection = PresenterBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
            PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, projection, 4);

            var worldHud = (WorldHudBatchBuffer)engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)!;
            var screenHud = (ScreenHudBatchBuffer)engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)!;

            Assert.That(HudCssStylingModEntry.DiagQueued, Is.EqualTo(16), "mod 应入队 16 个单位");
            Assert.That(worldHud.Count, Is.EqualTo(144), "16 单位 × 9 元素先落世界缓冲");

            int bars = 0;
            int texts = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar && item.BorderWidth <= 0.5f && item.Id0 <= 0)
                {
                    bars++;
                    Assert.That(item.Width, Is.EqualTo(36f), "css width 必须落到每个条目");
                    Assert.That(item.Height, Is.EqualTo(item.Height > 4f ? 5f : 3f), "css height(bar=5/morale=3)");
                    Assert.That(item.ScreenOffsetY, Is.EqualTo(item.Height > 4f ? -18f : -10f).Within(0.001f), "css translate 必须落到每个条目");
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    texts++;
                    Assert.That(item.FontSize, Is.EqualTo(16), "css font-size 必须落到每个文本");
                    Assert.That(item.ScreenOffsetY, Is.EqualTo(-54f).Within(0.001f));
                    Assert.That(item.StyleFlags & 0x04, Is.Not.Zero, "名字居中锚标志随链路落到位");
                }
            }

            Assert.That(bars, Is.EqualTo(32), "16 单位 × 生命条+士气条(战况条/图标另计)");
            Assert.That(texts, Is.EqualTo(16), "16 单位 × 名字板");

            // 血量补丁经属性绑定写成填充率:95/100 与 8/100 必须出现在值里。
            float maxRatio = 0f;
            float minRatio = 1f;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar && item.Height > 4f)
                {
                    maxRatio = MathF.Max(maxRatio, item.Value0);
                    minRatio = MathF.Min(minRatio, item.Value0);
                }
            }

            Assert.That(maxRatio, Is.GreaterThan(0.9f), "最满的血条约 95/100");
            Assert.That(minRatio, Is.LessThan(0.1f), "最空的血条约 8/100");
            Assert.That(screenHud.Count, Is.GreaterThan(0), "投影后屏幕缓冲必须有内容");
        }

        private PresenterDefinitionRegistry LoadShowcasePresenters()
        {
            string assetPath = Path.Combine(
                RepoRoot,
                "mods", "showcases", "hud_css_styling", "HudCssStylingMod",
                "assets", "Presentation", "presenters.json");

            var vfs = new VirtualFileSystem();
            Directory.CreateDirectory(Path.Combine(_root, "Core", "Presentation"));
            File.WriteAllText(
                Path.Combine(_root, "Core", "config_catalog.json"),
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            File.WriteAllText(
                Path.Combine(_root, "Core", "Presentation", "presenters.json"),
                File.ReadAllText(assetPath));
            vfs.Mount("Core", Path.Combine(_root, "Core"));

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: _ => 1,
                resolveEntityTemplateKey: _ => 1,
                resolveTextTokenId: _ => 101,
                resolveBehaviorAssetId: (_, _) => 1).Load(catalog);
            return registry;
        }

        private sealed class FixedView : IViewController
        {
            public Vector2 Resolution => new(640, 480);
            public float Fov => 60;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private static Dictionary<string, object> CreateWorldHudProjectionGlobals(World world, Entity owner)
        {
            Entity viewer = world.Create();
            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
            projectionStore.Upsert(
                viewer,
                owner,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));

            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("test"));
            seats.SetPossession("test", 1, viewer);

            return new Dictionary<string, object>
            {
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(projectionStore),
                [CoreServiceKeys.ClientLocalSeatRegistry.Name] = seats,
            };
        }
    }
}
